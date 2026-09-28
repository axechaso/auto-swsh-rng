from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

os_env = os.environ
os_env.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtGui import QColor, QImage
from PySide6.QtWidgets import QApplication

from swsh_app.automation import (
    AttemptExecutionResult,
    AttemptExecutionStatus,
    CaptureAttemptCoordinator,
    EncounterAttemptSnapshot,
    FieldRead,
    ReplayCaptureActionPort,
    ReplayCaptureFixture,
    SaveCommitStore,
    RosterMember,
    load_replay_manifest,
)


APP = QApplication.instance() or QApplication([])
ALGORITHM_COMMIT = "a" * 40


class CaptureAttemptReplayTests(unittest.TestCase):
    def test_failed_ball_retry_prompts_identity_reverse_and_t40_complete(self):
        events = [
            {"kind": "battle_entered"},
            {"kind": "opponent_observed", "details": scene_details()},
            {"kind": "prepare_move_or_item"},
            {"kind": "open_ball_selection"},
            {"kind": "ball_thrown"},
            {"kind": "capture_failed"},
            {"kind": "prepare_move_or_item"},
            {"kind": "open_ball_selection"},
            {"kind": "ball_thrown"},
            {"kind": "capture_confirmed"},
            {"kind": "prompt_opened", "details": {"promptKind": "experience"}},
            {"kind": "prompt_resolved", "details": {"promptKind": "experience"}},
            {"kind": "prompt_opened", "details": {"promptKind": "party"}},
            {"kind": "prompt_resolved", "details": {"promptKind": "party"}},
            {"kind": "post_capture_complete"},
        ]
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, port, reverse_requests, store = build_coordinator(
                Path(temporary), events=events, fields=("species", "shiny", "nature"),
                reads={"species": ["Rookidee", "Rookidee"], "shiny": [True, True],
                       "nature": [None, None]},
                reverse_result=reverse_result([
                    {"generationAdvance": 154, "species": "Rookidee", "shiny": True},
                    {"generationAdvance": 155, "species": "Rookidee", "shiny": True},
                ]),
                requirements={"species": "Rookidee", "shiny": True},
            )
            result = coordinator.run()
            self.assertEqual(store.load().stage, "Completed")

        self.assertEqual(result.status, "SAVED_SUCCESS")
        self.assertEqual(result.workflow_report["phase"], "completed")
        self.assertEqual(result.workflow_report["ballThrows"], 2)
        self.assertEqual(result.identity["identity_key"], "party:2|caught-signature")
        self.assertEqual(result.target_verification["status"], "TARGET_VERIFIED_WITH_MULTIPLE_CANDIDATES")
        self.assertEqual(result.save_commit["stage"], "Completed")
        self.assertTrue(result.save_commit["receipt"]["post_save_identity_evidence_ids"])
        self.assertEqual(port.frame_source.remaining_actions, 0)
        self.assertEqual(reverse_requests[0]["species"], "Rookidee")
        self.assertIn("nature", reverse_requests[0]["unresolvedFields"])
        self.assertNotIn("targetFilters", reverse_requests[0])
        self.assertEqual(result.hardware_status, "PendingHardwareValidation")

    def test_ambiguous_roster_identity_stops_before_field_observation(self):
        events = standard_events()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            coordinator, port, _, store = build_coordinator(
                root, events=events, fields=("species",),
                reads={"species": ["Rookidee", "Rookidee"]},
                after_roster=(
                    RosterMember("party:1", "old-signature", "Rookidee", 18),
                    RosterMember("party:2", "caught-signature", "Rookidee", 18),
                    RosterMember("party:3", "other-signature", "Pikachu", 10),
                ),
                reverse_result=reverse_result([]), requirements={"species": "Rookidee"},
            )
            result = coordinator.run()

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.reason_code, "MULTIPLE_NEW_ENTITIES")
        self.assertFalse(any(item["kind"].startswith("observe_") for item in port.events))
        self.assertFalse(store.path.exists())

    def test_unknown_required_property_is_not_guessed_or_saved(self):
        events = standard_events()
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, _, reverse_requests, store = build_coordinator(
                Path(temporary), events=events, fields=("species", "nature"),
                reads={"species": ["Rookidee", "Rookidee"], "nature": [None, None]},
                reverse_result=reverse_result([
                    {"generationAdvance": 155, "species": "Rookidee"},
                    {"generationAdvance": 156, "species": "Rookidee"},
                ]),
                requirements={"species": "Rookidee", "nature": "Timid"},
            )
            result = coordinator.run()

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.target_verification["reasonCode"], "TARGET_FIELDS_REMAIN_UNKNOWN")
        self.assertEqual(result.observations["nature"]["status"], "unknown")
        self.assertIsNone(reverse_requests[0]["nature"])
        self.assertFalse(store.path.exists())

    def test_incomplete_reverse_search_never_enters_save_commit(self):
        events = standard_events()
        incomplete = reverse_result([{"generationAdvance": 155, "species": "Rookidee"}])
        incomplete["complete"] = False
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, _, _, store = build_coordinator(
                Path(temporary), events=events, fields=("species",),
                reads={"species": ["Rookidee", "Rookidee"]},
                reverse_result=incomplete, requirements={"species": "Rookidee"},
            )
            result = coordinator.run()

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.reason_code, "REVERSE_SEARCH_INCOMPLETE")
        self.assertFalse(store.path.exists())

    def test_cancel_before_capture_stops_without_consuming_a_replay_action(self):
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, port, _, store = build_coordinator(
                Path(temporary), events=standard_events(), fields=("species",),
                reads={"species": ["Rookidee", "Rookidee"]},
                reverse_result=reverse_result([]), requirements={"species": "Rookidee"},
            )
            remaining = port.frame_source.remaining_actions
            coordinator.cancel()
            result = coordinator.run()

        self.assertEqual(result.status, "CANCELLED")
        self.assertEqual(result.reason_code, "USER_CANCELLED")
        self.assertEqual(port.frame_source.remaining_actions, remaining)
        self.assertFalse(store.path.exists())

    def test_opponent_escape_stops_capture_without_reverse_or_save(self):
        events = standard_events()
        events[5] = {"kind": "opponent_escaped"}
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, _, reverse_requests, store = build_coordinator(
                Path(temporary), events=events, fields=("species",),
                reads={"species": ["Rookidee", "Rookidee"]},
                reverse_result=reverse_result([]), requirements={"species": "Rookidee"},
            )
            result = coordinator.run()

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.reason_code, "OPPONENT_ESCAPED")
        self.assertEqual(reverse_requests, [])
        self.assertFalse(store.path.exists())

    def test_zero_reverse_candidates_and_out_of_range_candidates_do_not_save(self):
        cases = (
            ([], "REVERSE_NO_CANDIDATES"),
            ([{"generationAdvance": 250, "species": "Rookidee"}], "CANDIDATES_CROSS_USER_RANGE_BOUNDARY"),
        )
        for candidates, expected_reason in cases:
            with self.subTest(expected_reason=expected_reason), tempfile.TemporaryDirectory() as temporary:
                coordinator, _, _, store = build_coordinator(
                    Path(temporary), events=standard_events(), fields=("species",),
                    reads={"species": ["Rookidee", "Rookidee"]},
                    reverse_result=reverse_result(candidates),
                    requirements={"species": "Rookidee"},
                )
                result = coordinator.run()

            self.assertEqual(result.status, "NEEDS_ATTENTION")
            self.assertEqual(result.target_verification["reasonCode"], expected_reason)
            self.assertFalse(store.path.exists())

    def test_post_save_slot_mismatch_preserves_save_confirmed_recovery_state(self):
        events = standard_events()
        with tempfile.TemporaryDirectory() as temporary:
            coordinator, _, _, store = build_coordinator(
                Path(temporary), events=events, fields=("species",),
                reads={"species": ["Rookidee", "Rookidee"]},
                reverse_result=reverse_result([{"generationAdvance": 155, "species": "Rookidee"}]),
                requirements={"species": "Rookidee"}, post_save_slot="party:3",
            )
            result = coordinator.run()
            self.assertEqual(store.load().stage, "SaveConfirmed")
            self.assertEqual(
                store.recovery_decision()["decision"],
                "verify_post_save_identity_then_complete",
            )

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.reason_code, "POST_SAVE_IDENTITY_MISMATCH")


def scene_details():
    return {
        "species": "Rookidee", "level": 18,
        "kind": "Symbol", "area": "Bridge Field", "weather": "Normal Weather",
    }


def standard_events():
    return [
        {"kind": "battle_entered"},
        {"kind": "opponent_observed", "details": scene_details()},
        {"kind": "prepare_move_or_item"},
        {"kind": "open_ball_selection"},
        {"kind": "ball_thrown"},
        {"kind": "capture_confirmed"},
        {"kind": "post_capture_complete"},
    ]


def reverse_result(candidates):
    return {
        "complete": True,
        "truncated": False,
        "sourceKind": "synthetic",
        "window": {"start": 90, "end": 210, "diagnosticWindowComplete": True},
        "evidenceIds": ["reverse:reviewed"],
        "eligibleForCalibration": False,
        "candidates": candidates,
    }


def build_coordinator(root: Path, *, events, fields, reads, reverse_result,
                      requirements, after_roster=None, post_save_slot="party:2"):
    root.mkdir(parents=True, exist_ok=True)
    purpose_counts = {}
    action_specs = []
    baseline_path = root / "frame-1.png"
    baseline_image = QImage(16, 16, QImage.Format.Format_RGBA8888)
    baseline_image.fill(QColor("black"))
    if not baseline_image.save(str(baseline_path), "PNG"):
        raise AssertionError("could not write replay baseline")
    baseline_entry = {
        "frameId": 1,
        "sourceTimestampNs": 0,
        "path": baseline_path.name,
        "sha256": hashlib.sha256(baseline_path.read_bytes()).hexdigest(),
    }

    def add(purpose, frame_count=1):
        index = purpose_counts.get(purpose, 0)
        purpose_counts[purpose] = index + 1
        action_specs.append((purpose, index, frame_count))
        return None

    add("roster_before")
    for item in events:
        add(item["kind"])
    add("roster_after")
    for field in fields:
        frame_count = len(reads[field])
        add(f"observe_{field}", frame_count)
    add("save_action")
    add("save_confirmation")
    add("post_save_identity")

    frames = []
    action_json = []
    reads_by_field = {}
    next_frame = 2
    replay_id = "capture-fixture"
    for purpose, index, frame_count in action_specs:
        frame_ids = []
        for _ in range(frame_count):
            frame_id = next_frame
            next_frame += 1
            image_path = root / f"frame-{frame_id}.png"
            image = QImage(16, 16, QImage.Format.Format_RGBA8888)
            image.fill(QColor(frame_id % 255, (frame_id * 3) % 255, (frame_id * 7) % 255))
            if not image.save(str(image_path), "PNG"):
                raise AssertionError("could not write replay image")
            frames.append({
                "frameId": frame_id,
                "sourceTimestampNs": frame_id * 33_333_333,
                "path": image_path.name,
                "sha256": hashlib.sha256(image_path.read_bytes()).hexdigest(),
            })
            frame_ids.append(frame_id)
        action_json.append({"purpose": purpose, "index": index, "frameIds": frame_ids})
        if purpose.startswith("observe_"):
            field = purpose.removeprefix("observe_")
            values = reads[field]
            reads_by_field[field] = [
                FieldRead(
                    field, value, frame_id, 3, 0.97 if value is not None else 0.3,
                    "" if value is None else str(value), [f"{replay_id}:frame:{frame_id}"],
                )
                for value, frame_id in zip(values, frame_ids, strict=True)
            ]

    manifest = root / "capture-replay.json"
    manifest.write_text(json.dumps({
        "schema": "auto-swsh-frame-replay",
        "schemaVersion": 1,
        "replayId": replay_id,
        "scenarioId": "m5-capture",
        "sourceKind": "synthetic",
        "baselineFrameId": 1,
        "source": {"type": "images", "frames": [baseline_entry, *frames]},
        "actions": action_json,
    }), encoding="utf-8")
    frame_source = load_replay_manifest(manifest)
    fixture = ReplayCaptureFixture(
        events=events,
        roster_before=(RosterMember("party:1", "old-signature", "Rookidee", 12),),
        roster_after=tuple(after_roster or (
            RosterMember("party:1", "old-signature", "Rookidee", 12),
            RosterMember("party:2", "caught-signature", "Rookidee", 18),
        )),
        field_reads=reads_by_field,
        post_save_identity="party:2|caught-signature",
        post_save_slot=post_save_slot,
    )
    port = ReplayCaptureActionPort(frame_source, fixture)
    attempt = AttemptExecutionResult(
        status=AttemptExecutionStatus.TRIGGER_EXECUTED,
        attempt_id="attempt-m5", run_id="run-m5", epoch_id="epoch-m5",
        context_revision=3, current_advance=150, target_candidate_id="candidate-155",
        target_generation_advance=155, trigger_advance=154,
        target_snapshot_id="snapshot-m5", target_request_digest="b" * 64,
        hardware_status="PendingHardwareValidation", reason_code=None, reason=None, events=(),
    )
    snapshot = EncounterAttemptSnapshot(
        seed0="123456789abcdef0", seed1="0fedcba987654321", game="Shield",
        kind="Symbol", area="Bridge Field", weather="Normal Weather", source_kind="synthetic",
        target_range=(100, 200), diagnostic_window=(90, 210),
        diagnostic_window_complete=True, context={"considerMenuClose": True},
        planned_generation_advance=155, evidence_ids=["attempt-video"],
    )
    reverse_requests = []

    def lookup(request):
        reverse_requests.append(request)
        return reverse_result

    store = SaveCommitStore(root / "save-commit.json")
    coordinator = CaptureAttemptCoordinator(
        attempt_result=attempt,
        encounter_snapshot=snapshot,
        actions=port,
        reverse_lookup=lookup,
        save_store=store,
        requirements=requirements,
        observation_fields=fields,
        algorithm_commit=ALGORITHM_COMMIT,
        script_revision="replay-script-v1",
    )
    return coordinator, port, reverse_requests, store


if __name__ == "__main__":
    unittest.main()
