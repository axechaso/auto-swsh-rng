from __future__ import annotations

from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    BoundaryAdjustment,
    BoundaryApplicationLedger,
    SaveCommitError,
    SaveCommitStore,
    SaveReceipt,
    solve_trigger_advance,
    update_boundary_adjustment,
)


ALGORITHM_COMMIT = "a" * 40


class BoundaryAdjustmentTests(unittest.TestCase):
    def test_t41_positive_and_negative_residuals_move_b_and_trigger_in_expected_direction(self):
        current = BoundaryAdjustment("encounter.trigger", 0, "model-v1")
        base_trigger = solve_trigger_advance(8_000, 500, current)

        positive = update_boundary_adjustment(
            current, 4, alpha=0.5, minimum_offset=-20, maximum_offset=20,
            maximum_step=4, model_version="model-v2",
        )
        negative = update_boundary_adjustment(
            current, -4, alpha=0.5, minimum_offset=-20, maximum_offset=20,
            maximum_step=4, model_version="model-v2",
        )

        self.assertEqual(positive.offset, 2)
        self.assertEqual(negative.offset, -2)
        self.assertEqual(solve_trigger_advance(8_000, 500, positive), base_trigger - 2)
        self.assertEqual(solve_trigger_advance(8_000, 500, negative), base_trigger + 2)

    def test_boundary_adjustment_is_applied_once_at_its_declared_boundary(self):
        adjustment = BoundaryAdjustment("battle.entry", 3, "route-model-4")
        ledger = BoundaryApplicationLedger()

        predicted = ledger.predict_generation(
            attempt_id="attempt-1",
            boundary_id="battle.entry",
            trigger_advance=90,
            deterministic_consumption=7,
            adjustment=adjustment,
        )

        self.assertEqual(predicted, 100)
        with self.assertRaisesRegex(ValueError, "already applied"):
            ledger.predict_generation(
                attempt_id="attempt-1",
                boundary_id="battle.entry",
                trigger_advance=90,
                deterministic_consumption=7,
                adjustment=adjustment,
            )
        with self.assertRaisesRegex(ValueError, "different action boundary"):
            ledger.predict_generation(
                attempt_id="attempt-2",
                boundary_id="menu.close",
                trigger_advance=90,
                deterministic_consumption=7,
                adjustment=adjustment,
            )


class SaveCommitTests(unittest.TestCase):
    def start_store(self, root):
        store = SaveCommitStore(Path(root) / "save-commit.json")
        store.start(
            run_id="run-1",
            epoch_id="epoch-1",
            attempt_id="attempt-1",
            target_identity="species:123|pid:ABCDEF12",
            target_slot="party:5",
            target_evidence_ids=["battle-summary", "target-check"],
            algorithm_commit=ALGORITHM_COMMIT,
            script_revision="script-v3",
            created_at_utc="2026-09-28T01:00:00+00:00",
        )
        return store

    def test_t40_recovers_each_durable_stage_without_restarting_or_repeating_save(self):
        with tempfile.TemporaryDirectory() as temporary:
            store = self.start_store(temporary)
            self.assertEqual(store.recovery_decision()["decision"], "verify_target_then_request_save")

            requested, should_issue_save = store.request_save()
            self.assertEqual(requested.stage, "SaveRequested")
            self.assertTrue(should_issue_save)
            decision = store.recovery_decision()
            self.assertEqual(decision["decision"], "hold_scene_and_verify_save_before_any_retry")
            self.assertFalse(decision["allow_ordinary_restart"])
            self.assertFalse(decision["allow_save_action"])

            duplicate, should_issue_save_again = store.request_save()
            self.assertEqual(duplicate.stage, "SaveRequested")
            self.assertFalse(should_issue_save_again)

            confirmed = store.confirm_save(save_evidence_ids=["save-screen", "target-after-save"])
            self.assertEqual(confirmed.stage, "SaveConfirmed")
            self.assertEqual(
                store.recovery_decision()["decision"],
                "verify_post_save_identity_then_complete",
            )
            receipt = SaveReceipt(
                attempt_id="attempt-1",
                target_identity="species:123|pid:ABCDEF12",
                target_slot="party:5",
                save_page_evidence_id="save-screen",
                post_save_identity="species:123|pid:ABCDEF12",
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-v3",
                confirmed_at_utc="2026-09-28T01:01:00+00:00",
            )
            completed = store.complete(receipt)
            self.assertEqual(completed.stage, "Completed")
            self.assertEqual(store.recovery_decision()["decision"], "already_completed")
            self.assertFalse(store.recovery_decision()["allow_ordinary_restart"])

    def test_save_commit_rejects_out_of_order_or_mismatched_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            store = self.start_store(temporary)
            with self.assertRaisesRegex(SaveCommitError, "only be confirmed"):
                store.confirm_save(save_evidence_ids=["save-screen"])
            store.request_save()
            store.confirm_save(save_evidence_ids=["save-screen"])
            mismatched = SaveReceipt(
                attempt_id="attempt-1",
                target_identity="species:123|pid:ABCDEF12",
                target_slot="party:5",
                save_page_evidence_id="save-screen",
                post_save_identity="species:999|pid:00000000",
                algorithm_commit=ALGORITHM_COMMIT,
                script_revision="script-v3",
                confirmed_at_utc="2026-09-28T01:01:00+00:00",
            )
            with self.assertRaisesRegex(SaveCommitError, "post-save identity"):
                store.complete(mismatched)
            self.assertEqual(store.load().stage, "SaveConfirmed")


if __name__ == "__main__":
    unittest.main()
