from __future__ import annotations

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    CaptureEvent,
    CaptureEvidenceError,
    CapturePhase,
    CaptureWorkflow,
    EncounterAttemptSnapshot,
    FieldObservation,
    FieldRead,
    ObservationStatus,
    RosterMember,
    aggregate_field_reads,
    identify_new_roster_entity,
    verify_capture_target,
)


def field_read(value, frame, *, revision=2, confidence=0.96, field="species"):
    return FieldRead(field, value, frame, revision, confidence, str(value), [f"frame-{frame}"])


def known(field, value, *frames):
    return FieldObservation(
        field, ObservationStatus.KNOWN, value=value, confidence=0.95,
        frame_ids=frames, evidence_ids=[f"e-{field}-{frame}" for frame in frames],
    )


def event(kind, **details):
    return CaptureEvent(kind, [f"evidence-{kind}"], details)


def workflow_to_post_capture():
    workflow = CaptureWorkflow(attempt_id="attempt-capture")
    for item in ("battle_entered", "opponent_observed", "prepare_move_or_item",
                 "open_ball_selection", "ball_thrown", "capture_confirmed"):
        workflow.apply(event(item))
    return workflow


def workflow_to_observation():
    workflow = workflow_to_post_capture()
    workflow.apply(event("post_capture_complete"))
    workflow.apply(event(
        "new_entity_confirmed", identityKey="party:6|entity-new", sceneMatch=True,
    ))
    return workflow


class FieldObservationTests(unittest.TestCase):
    def test_known_requires_two_distinct_stable_frames_and_preserves_raw_evidence(self):
        result = aggregate_field_reads([field_read("Rookidee", 11), field_read("Rookidee", 12)])
        self.assertEqual(result.status, ObservationStatus.KNOWN)
        self.assertEqual(result.value, "Rookidee")
        self.assertEqual(result.frame_ids, (11, 12))
        self.assertEqual(result.raw_values, ("Rookidee", "Rookidee"))

        repeated = aggregate_field_reads([field_read("Rookidee", 11), field_read("Rookidee", 11)])
        self.assertEqual(repeated.status, ObservationStatus.UNKNOWN)
        stale = aggregate_field_reads([
            field_read("Rookidee", 11, revision=2), field_read("Rookidee", 12, revision=3),
        ])
        self.assertEqual(stale.status, ObservationStatus.UNKNOWN)

    def test_disagreeing_ocr_is_ambiguous_and_low_confidence_is_not_guessed(self):
        ambiguous = aggregate_field_reads([field_read("31", 21), field_read("3I", 22)])
        self.assertEqual(ambiguous.status, ObservationStatus.AMBIGUOUS)
        self.assertEqual(set(ambiguous.candidates), {"31", "3I"})
        low = aggregate_field_reads([
            field_read("Timid", 31, confidence=0.4), field_read("Timid", 32, confidence=0.5),
        ])
        self.assertEqual(low.status, ObservationStatus.UNKNOWN)

    def test_known_field_model_rejects_missing_frame_or_evidence_proof(self):
        with self.assertRaisesRegex(CaptureEvidenceError, "two distinct video frames"):
            FieldObservation("nature", ObservationStatus.KNOWN, value="Timid",
                             confidence=0.95, frame_ids=[1], evidence_ids=["img"])


class CaptureWorkflowTests(unittest.TestCase):
    def test_capture_animation_success_does_not_skip_identity_observation_or_reverse(self):
        workflow = workflow_to_post_capture()
        self.assertTrue(workflow.capture_succeeded)
        self.assertFalse(workflow.identity_confirmed)
        self.assertEqual(workflow.phase, CapturePhase.POST_CAPTURE_PROMPTS)

        with self.assertRaisesRegex(CaptureEvidenceError, "prompts must be resolved"):
            workflow.apply(event("prompt_opened", promptKind="dex"))
            workflow.apply(event("post_capture_complete"))

    def test_post_capture_prompts_are_explicit_and_target_needs_reverse_proof(self):
        workflow = workflow_to_post_capture()
        workflow.apply(event("prompt_opened", promptKind="experience"))
        with self.assertRaisesRegex(CaptureEvidenceError, "does not match"):
            workflow.apply(event("prompt_resolved", promptKind="dex"))
        workflow.apply(event("prompt_resolved", promptKind="experience"))
        workflow.apply(event("post_capture_complete"))
        workflow.apply(event("new_entity_confirmed", identityKey="party:6|new", sceneMatch=True))
        workflow.apply(event("observations_complete"))
        with self.assertRaisesRegex(CaptureEvidenceError, "full diagnostic window"):
            workflow.apply(event("reverse_complete", complete=False))

    def test_transition_table_rejects_skipped_capture_steps_and_terminal_reuse(self):
        workflow = CaptureWorkflow(attempt_id="attempt-invalid")
        with self.assertRaisesRegex(CaptureEvidenceError, "not valid"):
            workflow.apply(event("capture_confirmed"))
        workflow.apply(event("stop"))
        self.assertEqual(workflow.phase, CapturePhase.NEEDS_ATTENTION)
        with self.assertRaisesRegex(CaptureEvidenceError, "terminal"):
            workflow.apply(event("battle_entered"))

    def test_report_keeps_attempt_id_evidence_and_pending_hardware_neutral_status(self):
        workflow = workflow_to_post_capture()
        report = workflow.as_report()

        self.assertEqual(report["reportSchema"], "auto-swsh-capture-workflow")
        self.assertEqual(report["status"], "pending")
        self.assertTrue(report["captureSucceeded"])
        self.assertFalse(report["identityConfirmed"])
        self.assertEqual(report["events"][-1]["evidenceIds"], ["evidence-capture_confirmed"])


class EntityIdentityTests(unittest.TestCase):
    def test_reserved_party_slot_is_confirmed_by_before_and_after_roster_difference(self):
        before = [RosterMember("party:1", "old-1", "Rookidee", 15)]
        after = [
            RosterMember("party:1", "old-1", "Rookidee", 15),
            RosterMember("party:2", "caught-2", "Rookidee", 18),
        ]
        identity = identify_new_roster_entity(
            before=before, after=after,
            before_evidence_ids=["team-before"], after_evidence_ids=["team-after"],
            storage_mode="reserved_party_slot", expected_species="Rookidee", expected_level=18,
        )
        self.assertEqual(identity.status, "known")
        self.assertEqual(identity.storage_slot, "party:2")
        self.assertEqual(identity.identity_key, "party:2|caught-2")

    def test_full_party_requires_explicit_box_slot_navigation_evidence(self):
        before = [RosterMember("party:1", "old-1", "Rookidee", 15)]
        after = [*before, RosterMember("box:2:4", "caught-4", "Rookidee", 18)]
        with self.assertRaisesRegex(CaptureEvidenceError, "explicit slot and navigation evidence"):
            identify_new_roster_entity(
                before=before, after=after, before_evidence_ids=["before"],
                after_evidence_ids=["after"], storage_mode="explicit_box_slot",
            )
        identity = identify_new_roster_entity(
            before=before, after=after, before_evidence_ids=["before"],
            after_evidence_ids=["after"], storage_mode="explicit_box_slot",
            expected_slot="box:2:4", box_navigation_evidence_ids=["box-navigation"],
        )
        self.assertEqual(identity.status, "known")
        self.assertIn("box-navigation", identity.evidence_ids)

    def test_ambiguous_or_mismatched_storage_slot_never_confirms_identity(self):
        before = [RosterMember("party:1", "old-1", "Rookidee", 15)]
        after = [
            *before,
            RosterMember("party:2", "new-2", "Rookidee", 18),
            RosterMember("party:3", "new-3", "Pikachu", 10),
        ]
        identity = identify_new_roster_entity(
            before=before, after=after,
            before_evidence_ids=["before"], after_evidence_ids=["after"],
            storage_mode="reserved_party_slot",
        )
        self.assertEqual(identity.status, "ambiguous")
        self.assertIsNone(identity.identity_key)


class ReverseAndVerificationTests(unittest.TestCase):
    def make_snapshot(self, **overrides):
        values = dict(
            seed0="123456789abcdef0",
            seed1="0fedcba987654321",
            game="Shield",
            kind="Symbol",
            area="Bridge Field",
            weather="Normal Weather",
            source_kind="replay",
            target_range=(100, 200),
            diagnostic_window=(90, 210),
            diagnostic_window_complete=True,
            context={"considerMenuClose": True, "menuCloseNonPlayerCharacters": 4,
                     "holdDirection": False, "dexRecommendationSlots": [0, 0, 0, 0]},
            tid=111,
            sid=222,
            planned_generation_advance=155,
            evidence_ids=["attempt-video"],
        )
        values.update(overrides)
        return EncounterAttemptSnapshot(**values)

    def test_reverse_request_uses_only_actual_observations_and_preserves_unknowns(self):
        workflow = workflow_to_observation()
        observations = {
            "species": known("species", "Rookidee", 41, 42),
            "level": known("level", 18, 43, 44),
            "nature": FieldObservation("nature", ObservationStatus.UNKNOWN),
        }
        request = self.make_snapshot().build_reverse_request(observations=observations, workflow=workflow)

        self.assertEqual(request["species"], "Rookidee")
        self.assertEqual((request["levelMinimum"], request["levelMaximum"]), (18, 18))
        self.assertEqual(request["ivs"], [[0, 31]] * 6)
        self.assertIn("nature", request["unresolvedFields"])
        self.assertNotIn("targetFilters", request)
        self.assertNotIn("targetShiny", request)
        self.assertEqual(request["plannedGenerationAdvance"], 155)

    def test_reverse_request_normalizes_observed_weather_mark_and_height(self):
        workflow = workflow_to_observation()
        observations = {
            "species": known("species", "Rookidee", 51, 52),
            "mark": known("mark", "Weather", 53, 54),
            "height": known("height", "L", 55, 56),
            "shiny": known("shiny", "Square", 57, 58),
        }
        request = self.make_snapshot().build_reverse_request(observations=observations, workflow=workflow)

        self.assertEqual(request["specificMark"], "Cloudy")
        self.assertEqual(request["height"], "Large")
        self.assertEqual(request["shiny"], "Square")

    def test_static_reverse_requires_actual_species_and_scene_identity(self):
        workflow = workflow_to_observation()
        with self.assertRaisesRegex(CaptureEvidenceError, "confirmed actual species"):
            self.make_snapshot(kind="Static").build_reverse_request(
                observations={}, workflow=workflow,
            )
        wrong_scene = workflow_to_post_capture()
        wrong_scene.apply(event("post_capture_complete"))
        wrong_scene.apply(event("new_entity_confirmed", identityKey="party:6|new", sceneMatch=False))
        with self.assertRaisesRegex(CaptureEvidenceError, "frozen encounter scene"):
            self.make_snapshot().build_reverse_request(observations={}, workflow=wrong_scene)

    def test_target_success_requires_all_candidates_in_range_and_meeting_required_conditions(self):
        reverse = {
            "complete": True,
            "sourceKind": "replay",
            "window": {"diagnosticWindowComplete": True},
            "evidenceIds": ["capture-summary"],
            "eligibleForCalibration": False,
            "candidates": [
                {"generationAdvance": 140, "species": "Rookidee", "shiny": True},
                {"generationAdvance": 141, "species": "Rookidee", "shiny": True},
            ],
        }
        result = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="replay", user_range=(100, 200),
            requirements={"species": "Rookidee", "shiny": True},
            observations={"species": known("species", "Rookidee", 61, 62)},
            reverse_result=reverse,
        )
        self.assertTrue(result.verified)
        self.assertEqual(result.status, "TARGET_VERIFIED_WITH_MULTIPLE_CANDIDATES")
        self.assertFalse(result.calibration_sample_eligible)

    def test_target_verification_uses_numeric_iv_ranges_and_shiny_categories(self):
        from swsh_app.automation.capture_workflow import _matches_requirement

        self.assertTrue(_matches_requirement(17, [0, 31]))
        self.assertFalse(_matches_requirement(32, [0, 31]))
        self.assertTrue(_matches_requirement([31, 12, 0], [[31, 31], [10, 15], [0, 31]]))
        self.assertTrue(_matches_requirement("Star", True))
        self.assertTrue(_matches_requirement("Square", "Either"))
        self.assertTrue(_matches_requirement("None", False))

        reverse = {
            "complete": True,
            "sourceKind": "replay",
            "window": {"diagnosticWindowComplete": True},
            "evidenceIds": ["capture-summary"],
            "candidates": [{
                "generationAdvance": 155,
                "ivs": [31, 12, 0, 4, 19, 27],
                "shiny": "Star",
                "height": "Large",
            }],
        }
        result = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="replay", user_range=(100, 200),
            requirements={"ivs": [[31, 31], [10, 15], [0, 31], [0, 31], [0, 31], [0, 31]],
                          "shiny": True, "height": "Large"},
            observations={}, reverse_result=reverse,
        )
        self.assertTrue(result.verified)

    def test_precise_real_match_can_calibrate_but_out_of_range_or_unresolved_cannot(self):
        reverse = {
            "complete": True,
            "sourceKind": "real",
            "window": {"diagnosticWindowComplete": True},
            "evidenceIds": ["video", "summary"],
            "eligibleForCalibration": True,
            "candidates": [{"generationAdvance": 155, "species": "Rookidee", "nature": "Timid"}],
        }
        result = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="real", user_range=(100, 200),
            requirements={"species": "Rookidee", "nature": "Timid"},
            observations={"species": known("species", "Rookidee", 71, 72)},
            reverse_result=reverse,
        )
        self.assertTrue(result.calibration_sample_eligible)
        self.assertEqual(result.verification_level, "unique_reverse_inference")

        outside = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="real", user_range=(100, 150),
            requirements={"species": "Rookidee"}, observations={}, reverse_result=reverse,
        )
        self.assertFalse(outside.verified)
        self.assertFalse(outside.calibration_sample_eligible)

        unresolved = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="real", user_range=(100, 200),
            requirements={"ec": "00000000"}, observations={}, reverse_result=reverse,
        )
        self.assertFalse(unresolved.verified)
        self.assertEqual(unresolved.reason_code, "TARGET_FIELDS_REMAIN_UNKNOWN")

    def test_target_verification_requires_target_criteria_and_evidence(self):
        reverse = {
            "complete": True,
            "sourceKind": "real",
            "window": {"diagnosticWindowComplete": True},
            "evidenceIds": [],
            "candidates": [{"generationAdvance": 155, "species": "Rookidee"}],
        }
        empty = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="real", user_range=(100, 200), requirements={},
            observations={}, reverse_result=reverse,
        )
        missing = verify_capture_target(
            capture_succeeded=True, identity_confirmed=True, scene_match=True,
            source_kind="real", user_range=(100, 200), requirements={"species": "Rookidee"},
            observations={}, reverse_result=reverse,
        )
        self.assertFalse(empty.verified)
        self.assertEqual(empty.reason_code, "TARGET_REQUIREMENTS_EMPTY")
        self.assertFalse(missing.verified)
        self.assertEqual(missing.reason_code, "REVERSE_EVIDENCE_MISSING")


if __name__ == "__main__":
    unittest.main()
