from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtGui import QColor, QImage
from PySide6.QtWidgets import QApplication

from swsh_app.automation import (
    AnimationFramePhase,
    AnimationFrameScore,
    EvidenceStore,
    EvidenceValidationError,
    SeedObserver,
    SceneGuard,
    SceneGuardError,
    SceneObservation,
    SimulationDevicePort,
    VirtualClock,
    can_start_formal,
    load_replay_manifest,
)


APP = QApplication.instance() or QApplication([])
ALGORITHM_COMMIT = "a" * 40


def _write_image(path: Path, color: str) -> str:
    image = QImage(16, 16, QImage.Format.Format_RGBA8888)
    image.fill(QColor(color))
    if not image.save(str(path), "PNG"):
        raise AssertionError(f"could not write test image: {path}")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ReplayTests(unittest.TestCase):
    def test_image_replay_drives_seed_observer_with_virtual_clock(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            colors = ["black", "red", "blue", "black"]
            frames = []
            for frame_id, color in enumerate(colors, start=1):
                name = f"frame-{frame_id}.png"
                digest = _write_image(root / name, color)
                frames.append({
                    "frameId": frame_id,
                    "sourceTimestampNs": (frame_id - 1) * 1_000_000_000,
                    "path": name,
                    "sha256": digest,
                })
            manifest_path = root / "replay.json"
            manifest_path.write_text(json.dumps({
                "schema": "auto-swsh-frame-replay",
                "schemaVersion": 1,
                "replayId": "synthetic-seed-001",
                "scenarioId": "test-scenario",
                "sourceKind": "synthetic",
                "baselineFrameId": 1,
                "source": {"type": "images", "frames": frames},
                "actions": [{"purpose": "seed", "index": 0, "frameIds": [2, 3, 4]}],
            }), encoding="utf-8")

            clock = VirtualClock()
            replay = load_replay_manifest(manifest_path, clock=clock)

            def classify(snapshot):
                if snapshot.frame_id == 4:
                    return AnimationFrameScore(AnimationFramePhase.IDLE)
                return AnimationFrameScore(
                    AnimationFramePhase.ANIMATION,
                    zero_score=0.1,
                    one_score=0.92,
                    motion_score=0.9,
                )

            observer = SeedObserver(
                replay,
                classify,
                clock=clock,
                timeout_seconds=4,
                poll_seconds=0.01,
            )
            device = SimulationDevicePort(frame_source=replay)
            lease = device.acquire_task_lease()
            result = observer.observe_bit(
                lambda: device.trigger_seed_bit(None, lease, "epoch-1", "seed", 0, threading.Event())
            )

            self.assertEqual(result.bit, 1)
            self.assertIsNone(result.reason)
            self.assertEqual(result.frame_ids, (2, 3))
            self.assertEqual(result.evidence_ids, ("synthetic-seed-001:frame:2", "synthetic-seed-001:frame:3", "synthetic-seed-001:frame:4"))
            self.assertGreaterEqual(clock.monotonic_ns(), 3_000_000_000)
            self.assertLess(clock.monotonic_ns(), 3_100_000_000)
            self.assertEqual(replay.remaining_actions, 0)
            device.release_task_lease(lease)
            self.assertEqual(device.events[-1][0], "lease_released")
            replay.close()
            with self.assertRaisesRegex(ValueError, "closed"):
                replay.snapshot_frame()

    def test_replay_rejects_asset_hash_mismatch_and_path_escape(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            digest = _write_image(root / "frame.png", "red")
            manifest = {
                "schema": "auto-swsh-frame-replay",
                "schemaVersion": 1,
                "replayId": "bad-replay",
                "scenarioId": "test-scenario",
                "sourceKind": "real",
                "baselineFrameId": 1,
                "source": {"type": "images", "frames": [{
                    "frameId": 1, "sourceTimestampNs": 0,
                    "path": "frame.png", "sha256": "0" * 64,
                }]},
                "actions": [{"purpose": "seed", "index": 0, "frameIds": [2]}],
            }
            path = root / "replay.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "hash mismatch"):
                load_replay_manifest(path)
            manifest["source"]["frames"][0]["sha256"] = digest
            manifest["source"]["frames"][0]["path"] = "../outside.png"
            path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "escapes"):
                load_replay_manifest(path)

    def test_simulation_fault_injection_rejects_stale_frames_without_marking_a_bit(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            frames = []
            for frame_id, color in enumerate(("black", "red", "blue"), start=1):
                name = f"f{frame_id}.png"
                digest = _write_image(root / name, color)
                frames.append({
                    "frameId": frame_id,
                    "sourceTimestampNs": frame_id * 1_000_000,
                    "path": name,
                    "sha256": digest,
                })
            manifest = root / "replay.json"
            manifest.write_text(json.dumps({
                "schema": "auto-swsh-frame-replay",
                "schemaVersion": 1,
                "replayId": "stale-frame-test",
                "scenarioId": "test-scenario",
                "sourceKind": "synthetic",
                "baselineFrameId": 1,
                "source": {"type": "images", "frames": frames},
                "actions": [{"purpose": "seed", "index": 0, "frameIds": [2, 3]}],
            }), encoding="utf-8")
            clock = VirtualClock()
            source = load_replay_manifest(manifest, clock=clock)
            device = SimulationDevicePort(frame_source=source, faults={"stale_frame_once": True})
            lease = device.acquire_task_lease()
            observer = SeedObserver(
                source,
                lambda _: AnimationFrameScore(
                    AnimationFramePhase.ANIMATION, zero_score=0.1, one_score=0.9, motion_score=0.8
                ),
                clock=clock,
                timeout_seconds=0.05,
                poll_seconds=0.01,
            )

            result = observer.observe_bit(
                lambda: device.trigger_seed_bit(None, lease, "epoch-1", "seed", 0, threading.Event())
            )

            self.assertIsNone(result.bit)
            self.assertEqual(result.reason, "animation_not_seen")
            self.assertEqual(source.remaining_actions, 0)
            device.release_task_lease(lease)
            source.close()


class SceneGuardTests(unittest.TestCase):
    def test_scene_requires_consecutive_fresh_frames_and_current_context(self):
        guard = SceneGuard(stable_frame_count=2, max_frame_age_ns=100, minimum_confidence=0.9)
        first = guard.observe(
            SceneObservation("stable_menu", 1, 10, 4, 0.99, "frame-1"),
            expected_context_revision=4,
            now_ns=20,
        )
        second = guard.observe(
            SceneObservation("stable_menu", 2, 15, 4, 0.98, "frame-2"),
            expected_context_revision=4,
            now_ns=20,
        )

        self.assertFalse(first.stable)
        self.assertTrue(second.stable)
        self.assertEqual(
            guard.require("stable_menu", expected_context_revision=4, now_ns=20).evidence_ids,
            ("frame-1", "frame-2"),
        )
        stale = guard.observe(
            SceneObservation("stable_menu", 2, 15, 4, 0.99, "old-frame"),
            expected_context_revision=4,
            now_ns=20,
        )
        self.assertFalse(stale.accepted)
        self.assertEqual(stale.reason_code, "stale_frame")
        with self.assertRaisesRegex(SceneGuardError, "SCENE_NOT_STABLE"):
            guard.require("stable_menu", expected_context_revision=4, now_ns=20)

    def test_scene_guard_rejects_stale_revision_unknown_page_and_old_frame(self):
        guard = SceneGuard(stable_frame_count=1, max_frame_age_ns=5)
        stale_revision = guard.observe(
            SceneObservation("menu", 1, 10, 3, 1.0),
            expected_context_revision=4,
            now_ns=11,
        )
        unknown = guard.observe(
            SceneObservation(None, 2, 10, 4, 1.0),
            expected_context_revision=4,
            now_ns=11,
        )
        stale = guard.observe(
            SceneObservation("menu", 3, 1, 4, 1.0),
            expected_context_revision=4,
            now_ns=11,
        )

        self.assertEqual(stale_revision.reason_code, "stale_context")
        self.assertEqual(unknown.reason_code, "scene_unknown")
        self.assertEqual(stale.reason_code, "stale_frame")



class EvidenceTests(unittest.TestCase):
    def test_synthetic_evidence_never_unlocks_hardware_validation(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            store = EvidenceStore(root / "scenario")
            store.initialize(
                "scenario-001",
                scenario_revision=1,
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-1",
                required_cases=["seed", "npc"],
            )
            artifact = root / "capture.json"
            artifact.write_text('{"result":"replayed"}\n', encoding="utf-8")
            for case in ("seed", "npc"):
                store.import_artifact(
                    artifact,
                    case_id=case,
                    kind="experiment_report",
                    source_kind="synthetic",
                )
            store.mark_framework_ready(validation_cases=["T01", "T39"])

            report = store.report()
            self.assertEqual(report["frameworkStatus"], "FrameworkReady")
            self.assertEqual(report["hardwareStatus"], "PendingHardwareValidation")
            self.assertEqual(report["missingRealEvidenceCases"], ["seed", "npc"])
            self.assertFalse(report["canStartFormalAutomation"])
            self.assertFalse(can_start_formal(store.load()))
            with self.assertRaisesRegex(EvidenceValidationError, "real evidence"):
                store.review_hardware(reviewer="tester", device_profile={"model": "switch"})

    def test_version_change_invalidates_evidence_and_review(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            store = EvidenceStore(root / "scenario")
            store.initialize(
                "scenario-002",
                scenario_revision=1,
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-1",
                required_cases=["seed"],
            )
            store.update_versions(
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-1",
                device_profile={"model": "switch", "capture": "usb"},
            )
            artifact = root / "seed.txt"
            artifact.write_text("real capture reference", encoding="utf-8")
            store.import_artifact(
                artifact,
                case_id="seed",
                kind="action_log",
                source_kind="real",
            )
            store.mark_framework_ready(validation_cases=["T01"])
            store.review_hardware(
                reviewer="tester",
                device_profile={"model": "switch", "capture": "usb"},
                reviewed_at_utc="2026-09-27T00:00:00+00:00",
            )
            self.assertTrue(store.report()["canStartFormalAutomation"])

            store.update_versions(
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-2",
            )
            report = store.report()
            self.assertEqual(report["hardwareStatus"], "PendingHardwareValidation")
            self.assertEqual(report["missingCurrentEvidenceCases"], ["seed"])
            self.assertEqual(len(report["staleVersionEvidence"]), 1)
            self.assertFalse(report["canStartFormalAutomation"])

    def test_tampered_evidence_blocks_formal_start(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            store = EvidenceStore(root / "scenario")
            store.initialize(
                "scenario-003",
                scenario_revision=1,
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-1",
                required_cases=["seed"],
            )
            profile = {"model": "switch"}
            store.update_versions(
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-1",
                device_profile=profile,
            )
            artifact = root / "seed.txt"
            artifact.write_text("original", encoding="utf-8")
            imported = store.import_artifact(
                artifact,
                case_id="seed",
                kind="action_log",
                source_kind="real",
            )
            store.mark_framework_ready(validation_cases=["T01"])
            store.review_hardware(
                reviewer="tester",
                device_profile=profile,
                reviewed_at_utc="2026-09-27T00:00:00+00:00",
            )
            (store.root / imported["path"]).write_text("tampered", encoding="utf-8")

            report = store.report()
            self.assertTrue(report["canStartFormalAutomation"] is False)
            self.assertEqual(len(report["invalidEvidence"]), 1)


if __name__ == "__main__":
    unittest.main()
