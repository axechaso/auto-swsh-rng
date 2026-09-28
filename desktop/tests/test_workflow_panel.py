from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtGui import QColor, QImage
from PySide6.QtWidgets import QApplication

from swsh_app.automation import EvidenceStore
from swsh_app.workflow_panel import WorkflowPanel, current_script_revision
from swsh_app.localization import BASELINE


APP = QApplication.instance() or QApplication([])


def write_image(path: Path, color: str) -> str:
    image = QImage(16, 16, QImage.Format.Format_RGBA8888)
    image.fill(QColor(color))
    if not image.save(str(path), "PNG"):
        raise AssertionError(f"could not write test image: {path}")
    return hashlib.sha256(path.read_bytes()).hexdigest()


class WorkflowPanelTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.logs = []
        self.panel = WorkflowPanel(self.logs.append)

    def tearDown(self):
        self.panel.shutdown()
        self.panel.deleteLater()
        APP.processEvents()
        self.temp.cleanup()

    def create_scenario(self, scenario_id="route-test"):
        directory = self.root / scenario_id
        directory.mkdir()
        self.panel.scenario_directory.setText(str(directory))
        self.panel.scenario_id.setText(scenario_id)
        self.panel.initialize_scenario()
        return EvidenceStore(directory)

    def test_new_scenario_is_pending_and_formal_start_is_always_disabled(self):
        store = self.create_scenario()

        manifest = store.load()
        self.assertEqual(manifest["frameworkStatus"], "NotImplemented")
        self.assertEqual(manifest["hardwareStatus"], "PendingHardwareValidation")
        self.assertFalse(self.panel.formal_start_button.isEnabled())
        self.assertIn("被门禁阻止", self.panel.formal_status.text())

    def test_imported_real_evidence_is_verified_without_marking_hardware_ready(self):
        store = self.create_scenario()
        artifact = self.root / "seed.png"
        write_image(artifact, "red")

        entry = self.panel.import_evidence(
            artifact, case_id="seed", kind="image", source_kind="real",
        )

        self.assertIsNotNone(entry)
        self.assertEqual(store.load()["hardwareStatus"], "PendingHardwareValidation")
        report = store.report(verify_files=True)
        self.assertEqual(report["missingCurrentRealEvidenceCases"], [
            "npc", "advance", "capture", "reverse", "feedback", "save",
        ])
        self.assertFalse(report["canStartFormalAutomation"])
        self.assertEqual(self.panel.evidence_table.rowCount(), 1)
        self.assertEqual(self.panel.evidence_table.item(0, 0).text(), "SHA-256 正常")
        self.assertFalse(self.panel.formal_start_button.isEnabled())

        imported_path = store.root / entry["path"]
        imported_path.write_bytes(imported_path.read_bytes() + b"changed")
        self.panel.refresh_report()
        self.assertIn("失效：", self.panel.evidence_table.item(0, 0).text())

    def test_loading_scenario_updates_stale_automation_fingerprint(self):
        store = self.create_scenario()
        store.update_versions(
            algorithm_commit=BASELINE["algorithm"]["commit"],
            script_revision="old-pyside6-script",
        )

        self.panel.load_scenario()

        manifest = store.load()
        self.assertEqual(manifest["scriptRevision"], current_script_revision())
        self.assertEqual(manifest["hardwareStatus"], "PendingHardwareValidation")

    def test_replay_manifest_hashes_and_frames_are_browsable(self):
        store = self.create_scenario()
        replay_root = self.root / "replay"
        replay_root.mkdir()
        frames = []
        for frame_id, color in enumerate(("black", "red", "blue"), start=1):
            image_path = replay_root / f"frame-{frame_id}.png"
            digest = write_image(image_path, color)
            frames.append({
                "frameId": frame_id,
                "sourceTimestampNs": (frame_id - 1) * 1_000_000,
                "path": image_path.name,
                "sha256": digest,
            })
        manifest = replay_root / "replay.json"
        manifest.write_text(json.dumps({
            "schema": "auto-swsh-frame-replay",
            "schemaVersion": 1,
            "replayId": "replay-test",
            "scenarioId": "route-test",
            "sourceKind": "synthetic",
            "baselineFrameId": 1,
            "source": {"type": "images", "frames": frames},
            "actions": [{"purpose": "seed", "index": 0, "frameIds": [2, 3]}],
        }), encoding="utf-8")

        source = self.panel.load_replay(manifest)

        self.assertIsNotNone(source)
        self.assertEqual(len(self.panel.replay_frames), 3)
        self.assertIn("动作段 1", self.panel.replay_status.text())
        self.assertEqual(self.panel.replay_frame_index, 0)
        self.panel.step_replay(1)
        self.assertEqual(self.panel.replay_frame_index, 1)
        self.assertIn("帧 2 / 3", self.panel.replay_frame_label.text())
        self.assertNotIn("警告", self.panel.replay_status.text())
        self.assertFalse(self.panel.formal_start_button.isEnabled())
        self.assertEqual(store.load()["hardwareStatus"], "PendingHardwareValidation")

    def test_replay_scenario_mismatch_is_shown_without_starting_any_action(self):
        self.create_scenario("route-a")
        replay_root = self.root / "replay"
        replay_root.mkdir()
        digest = write_image(replay_root / "frame.png", "green")
        second_digest = write_image(replay_root / "frame-2.png", "yellow")
        manifest = replay_root / "replay.json"
        manifest.write_text(json.dumps({
            "schema": "auto-swsh-frame-replay",
            "schemaVersion": 1,
            "replayId": "wrong-scenario",
            "scenarioId": "route-b",
            "sourceKind": "real",
            "baselineFrameId": 1,
            "source": {"type": "images", "frames": [
                {"frameId": 1, "sourceTimestampNs": 0,
                 "path": "frame.png", "sha256": digest},
                {"frameId": 2, "sourceTimestampNs": 1,
                 "path": "frame-2.png", "sha256": second_digest},
            ]},
            "actions": [{"purpose": "seed", "index": 0, "frameIds": [2]}],
        }), encoding="utf-8")

        self.assertIsNotNone(self.panel.load_replay(manifest))
        self.assertIn("警告", self.panel.replay_status.text())
        self.assertIn("route-b", self.panel.replay_status.text())
        self.assertFalse(self.panel.formal_start_button.isEnabled())


if __name__ == "__main__":
    unittest.main()
