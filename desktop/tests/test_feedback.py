from __future__ import annotations

from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    BoundaryAdjustment,
    CalibrationSample,
    FeedbackArchiveStore,
    FeedbackController,
    FeedbackError,
    RetryWorkflow,
    TimingSensitivityEstimator,
    attribute_calibration_sample,
)


CONTEXT = "c" * 64


class FeedbackAttributionTests(unittest.TestCase):
    def test_attribution_checks_reverse_checkpoint_scene_identity_and_source_in_order(self):
        base = sample("sample-1", 4)
        accepted, residual, reason = attribute_calibration_sample(
            base, expected_context_fingerprint=CONTEXT,
        )
        self.assertTrue(accepted)
        self.assertEqual(residual, 4)
        self.assertEqual(reason, "TRUSTED_UNIQUE_RESIDUAL")

        incomplete = sample("incomplete", 4, reverse_complete=False)
        self.assertEqual(attribute_calibration_sample(
            incomplete, expected_context_fingerprint=CONTEXT,
        )[2], "REVERSE_WINDOW_INCOMPLETE")
        multiple = sample("multiple", 4, reverse_candidate_advances=(104, 105))
        self.assertEqual(attribute_calibration_sample(
            multiple, expected_context_fingerprint=CONTEXT,
        )[2], "REVERSE_NO_UNIQUE_CANDIDATE")
        bad_checkpoint = sample("checkpoint", 4, relocation_checkpoint_matches=False)
        self.assertEqual(attribute_calibration_sample(
            bad_checkpoint, expected_context_fingerprint=CONTEXT,
        )[2], "PRECISE_CHECKPOINT_MISMATCH")
        synthetic = sample("synthetic", 4, source_kind="synthetic")
        self.assertEqual(attribute_calibration_sample(
            synthetic, expected_context_fingerprint=CONTEXT,
        )[2], "NON_REAL_EVIDENCE_CANNOT_CALIBRATE")


class FeedbackControllerTests(unittest.TestCase):
    def make_controller(self, root, **overrides):
        options = dict(
            initial_adjustment=BoundaryAdjustment("encounter.trigger", 0, "model-v1"),
            context_fingerprint=CONTEXT,
            archive=FeedbackArchiveStore(Path(root) / "feedback.json"),
            update_window=3,
            evaluation_window=3,
            alpha=0.5,
            minimum_offset=-20,
            maximum_offset=20,
            maximum_step=4,
            maximum_residual=64,
        )
        options.update(overrides)
        return FeedbackController(**options)

    def test_bounded_median_update_persists_and_duplicate_sample_cannot_be_applied_twice(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(temporary)
            decisions = [controller.ingest(sample(f"sample-{index}", 4)) for index in range(3)]
            self.assertEqual(controller.current_adjustment.offset, 2)
            self.assertEqual(controller.current_adjustment.revision, 1)
            self.assertEqual(decisions[-1].reason_code, "BOUNDARY_OFFSET_UPDATED")
            duplicate = controller.ingest(sample("sample-2", 4))
            self.assertFalse(duplicate.accepted_for_model)
            self.assertEqual(duplicate.reason_code, "DUPLICATE_SAMPLE_REJECTED")
            restored = self.make_controller(temporary)
            self.assertEqual(restored.current_adjustment.offset, 2)
            self.assertEqual(restored.current_adjustment.revision, 1)
            self.assertEqual(len(restored.records), 3)

    def test_large_residual_is_archived_but_requires_recalibration(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(temporary)
            decision = controller.ingest(sample("outlier", 100))

        self.assertFalse(decision.accepted_for_model)
        self.assertEqual(decision.reason_code, "RESIDUAL_OUTSIDE_UPDATE_BOUND")
        self.assertTrue(decision.recalibration_required)
        self.assertEqual(decision.boundary_after.offset, 0)

    def test_even_residual_window_uses_half_away_rounding_only_after_alpha(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(temporary, update_window=2, alpha=0.5)
            controller.ingest(sample("residual-2", 2))
            decision = controller.ingest(sample("residual-3", 3))

        # median(2, 3) * 0.5 = 1.25, rounded once to the nearest advance.
        self.assertEqual(decision.boundary_after.offset, 1)

    def test_sample_from_an_old_boundary_model_cannot_update_the_new_revision(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(temporary, update_window=1)
            controller.ingest(sample("update", 4))
            self.assertEqual(controller.current_adjustment.revision, 1)
            stale = controller.ingest(sample("stale", 4))

        self.assertFalse(stale.accepted_for_model)
        self.assertEqual(stale.reason_code, "SAMPLE_MODEL_VERSION_MISMATCH")
        self.assertEqual(stale.boundary_after.revision, 1)

    def test_worsening_or_oscillating_residuals_roll_back_to_stable_offset(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(
                temporary, update_window=2, evaluation_window=2, worsening_margin=0,
            )
            controller.ingest(sample("before-1", 4))
            controller.ingest(sample("before-2", 4))
            self.assertEqual(controller.current_adjustment.offset, 2)
            controller.ingest(sample("after-1", 20, boundary_model_version="model-v1+feedback-1",
                                     boundary_model_revision=1))
            rollback = controller.ingest(sample("after-2", -20, boundary_model_version="model-v1+feedback-1",
                                                 boundary_model_revision=1))

        self.assertEqual(rollback.reason_code, "UPDATE_ROLLED_BACK_RESIDUALS_WORSENED")
        self.assertEqual(rollback.boundary_after.offset, 0)
        self.assertEqual(rollback.boundary_after.revision, 2)
        self.assertTrue(rollback.recalibration_required)

    def test_changed_context_invalidates_the_old_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            controller = self.make_controller(temporary)
            controller.ingest(sample("sample-1", 0))
            with self.assertRaisesRegex(FeedbackError, "context changed"):
                self.make_controller(temporary, context_fingerprint="d" * 64)


class TimingSensitivityTests(unittest.TestCase):
    def test_repeated_one_parameter_slopes_enable_a_separate_bounded_delay_proposal(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = FeedbackArchiveStore(Path(temporary) / "feedback.json")
            estimator = TimingSensitivityEstimator(
                parameter_id="post_menu_wait_ms", context_fingerprint=CONTEXT, archive=archive,
            )
            for index in range(3):
                estimator.add_pair(
                    pair_id=f"pair-{index}", delay_minus_ms=100, delay_plus_ms=110,
                    sample_minus=sample(f"minus-{index}", 0),
                    sample_plus=sample(f"plus-{index}", 5),
                    control_fingerprint=CONTEXT,
                )
            update = estimator.propose_update(
                current_delay_ms=100, corrected_residual=2, alpha=0.5,
                minimum_delay_ms=50, maximum_delay_ms=200, maximum_step_ms=5,
            )
            restored = TimingSensitivityEstimator(
                parameter_id="post_menu_wait_ms", context_fingerprint=CONTEXT, archive=archive,
            )

        self.assertEqual(update.status, "proposed")
        self.assertEqual(update.slope_advances_per_ms, 0.5)
        self.assertEqual(update.proposed_delay_ms, 98)
        self.assertEqual(restored.stable_slope(), (0.5, "TIMING_SLOPE_STABLE"))

    def test_near_zero_or_sign_unstable_slope_disables_timing_updates(self):
        estimator = TimingSensitivityEstimator(
            parameter_id="wait", context_fingerprint=CONTEXT,
        )
        for index in range(3):
            estimator.add_pair(
                pair_id=f"zero-{index}", delay_minus_ms=100, delay_plus_ms=110,
                sample_minus=sample(f"zero-minus-{index}", 4),
                sample_plus=sample(f"zero-plus-{index}", 4),
                control_fingerprint=CONTEXT,
            )
        update = estimator.propose_update(
            current_delay_ms=100, corrected_residual=4, alpha=0.5,
            minimum_delay_ms=0, maximum_delay_ms=1000, maximum_step_ms=100,
        )
        self.assertEqual(update.status, "disabled")
        self.assertEqual(update.reason_code, "TIMING_SLOPE_NEAR_ZERO")
        with self.assertRaisesRegex(FeedbackError, "trusted samples"):
            estimator.add_pair(
                pair_id="synthetic", delay_minus_ms=100, delay_plus_ms=110,
                sample_minus=sample("synthetic-minus", 0, source_kind="synthetic"),
                sample_plus=sample("synthetic-plus", 5, source_kind="synthetic"),
                control_fingerprint=CONTEXT,
            )

        unstable = TimingSensitivityEstimator(parameter_id="wait", context_fingerprint=CONTEXT)
        for index, residual in enumerate((5, 5, -5)):
            unstable.add_pair(
                pair_id=f"sign-{index}", delay_minus_ms=100, delay_plus_ms=10,
                sample_minus=sample(f"sign-minus-{index}", 0),
                sample_plus=sample(f"sign-plus-{index}", residual),
                control_fingerprint=CONTEXT,
            )
        self.assertEqual(unstable.stable_slope(), (None, "TIMING_SLOPE_SIGN_UNSTABLE"))


class RetryWorkflowTests(unittest.TestCase):
    def test_definite_miss_requires_persisted_diagnostics_and_a_new_verified_epoch(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = FeedbackArchiveStore(Path(temporary) / "feedback.json")
            controller = FeedbackController(
                initial_adjustment=BoundaryAdjustment("encounter.trigger", 0, "model-v1"),
                context_fingerprint=CONTEXT,
                archive=archive,
                update_window=3,
            )
            decision = controller.ingest(sample("miss-sample", 0))
            workflow = RetryWorkflow(
                run_id="run-1", epoch_id="epoch-1", context_fingerprint=CONTEXT,
                archive=archive, maximum_restarts=2,
            )
            authorized = workflow.decide_after_attempt(
                attempt_id="attempt-1", attempt_status="MISSED", target_state="definite_miss",
                reverse_complete=True, save_commit_stage=None, evidence_persisted=True,
                feedback_decision=decision,
            )
            restarted = workflow.confirm_restart(
                restarted=True, new_epoch_id="epoch-2", seed_verified=True,
                seed_evidence_ids=("seed-before", "seed-after", "seed-verify"),
            )
            recovered = RetryWorkflow(
                run_id="run-1", epoch_id="epoch-1", context_fingerprint=CONTEXT,
                archive=archive, maximum_restarts=2,
            )

        self.assertTrue(authorized.restart_allowed)
        self.assertEqual(authorized.reason_code, "DEFINITE_MISS_PERSISTED")
        self.assertFalse(restarted.restart_allowed)
        self.assertEqual(restarted.epoch_id, "epoch-2")
        self.assertEqual(recovered.epoch_id, "epoch-2")
        self.assertEqual(recovered.restart_count, 1)

    def test_possible_target_save_recovery_and_missing_archive_never_restart(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = FeedbackArchiveStore(Path(temporary) / "feedback.json")
            controller = FeedbackController(
                initial_adjustment=BoundaryAdjustment("encounter.trigger", 0, "model-v1"),
                context_fingerprint=CONTEXT, archive=archive,
            )
            decision = controller.ingest(sample("sample-1", 0))
            workflow = RetryWorkflow(
                run_id="run-2", epoch_id="epoch-1", context_fingerprint=CONTEXT, archive=archive,
            )
            possible = workflow.decide_after_attempt(
                attempt_id="attempt-1", attempt_status="NEEDS_ATTENTION", target_state="possible",
                reverse_complete=True, save_commit_stage=None, evidence_persisted=True,
                feedback_decision=decision,
            )
            save_recovery = workflow.decide_after_attempt(
                attempt_id="attempt-1", attempt_status="NEEDS_ATTENTION", target_state="definite_miss",
                reverse_complete=True, save_commit_stage="SaveRequested", evidence_persisted=True,
                feedback_decision=decision,
            )
            missing_archive = workflow.decide_after_attempt(
                attempt_id="attempt-1", attempt_status="MISSED", target_state="definite_miss",
                reverse_complete=True, save_commit_stage=None, evidence_persisted=False,
                feedback_decision=decision,
            )

        self.assertFalse(possible.restart_allowed)
        self.assertEqual(possible.reason_code, "POTENTIAL_TARGET_MUST_BE_RETAINED")
        self.assertFalse(save_recovery.restart_allowed)
        self.assertEqual(save_recovery.reason_code, "SAVE_COMMIT_RECOVERY_REQUIRED")
        self.assertFalse(missing_archive.restart_allowed)
        self.assertEqual(missing_archive.reason_code, "ATTEMPT_EVIDENCE_NOT_PERSISTED")


def sample(sample_id, residual, **overrides):
    values = dict(
        sample_id=sample_id,
        attempt_id=f"attempt-{sample_id}",
        source_kind="real",
        context_fingerprint=CONTEXT,
        target_advance=100,
        boundary_id="encounter.trigger",
        boundary_model_version="model-v1",
        boundary_model_revision=0,
        reverse_candidate_advances=(100 + residual,),
        reverse_complete=True,
        reverse_truncated=False,
        reverse_eligible_for_calibration=True,
        relocation_checkpoint_matches=True,
        npc_and_scene_valid=True,
        new_entity_identity_confirmed=True,
        generation_boundary_confirmed=True,
        evidence_ids=(f"video-{sample_id}", f"reverse-{sample_id}"),
    )
    values.update(overrides)
    return CalibrationSample(**values)


if __name__ == "__main__":
    unittest.main()
