from __future__ import annotations

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    AttemptPlanner,
    AttemptPlanningNeedsAttention,
    AttemptPlanningSettings,
    BoundaryAdjustment,
    NpcCalibrationResult,
    PlanningTarget,
    ProbeCandidate,
)
from swsh_app.backend import PROTOCOL_VERSION
from swsh_app.localization import BASELINE


class FakePlanningCalculator:
    def __init__(self, *, corrupt_generation=False, stale_snapshot=False):
        self.requests = []
        self.corrupt_generation = corrupt_generation
        self.stale_snapshot = stale_snapshot

    def execute(self, request, cancel_event):
        self.requests.append(dict(request))
        target = request["targetCandidates"][0]
        boundary = request["boundaryAdjustment"]
        candidate = request["probeCandidates"][0]
        model_advances = candidate.get("interferenceAdvances", 0)
        trigger = target["generationAdvance"] - model_advances - boundary["offset"]
        if self.corrupt_generation:
            trigger += 1
        current = request["currentAdvance"]
        settings = request["attemptPlanning"]
        reserve = settings["relocationReserve"] + settings["preciseReserve"]
        coarse_target = trigger - reserve
        coarse_advances = coarse_target - current
        batches = []
        cursor = current
        while cursor < coarse_target:
            amount = min(settings["coarseBatchSize"], coarse_target - cursor)
            cursor += amount
            batches.append({
                "startAdvance": cursor - amount,
                "requestedAdvances": amount,
                "plannedEndAdvance": cursor,
                "requiresRelocationBeforeNextBatch": True,
            })
        predicted = target["generationAdvance"] + (1 if self.corrupt_generation else 0)
        data = {
            "reportSchema": "auto-swsh-attempt-plan",
            "reportVersion": 1,
            "status": "Feasible",
            "targetSnapshotId": "stale" if self.stale_snapshot else request["targetSnapshotId"],
            "targetRequestDigest": request["targetRequestDigest"],
            "currentAdvance": current,
            "boundary": boundary,
            "probeCandidateId": candidate["candidateId"],
            "evaluationCount": 6,
            "underBudgetPositionCount": 0,
            "targetStatuses": [{"candidateId": target["candidateId"], "status": "Feasible"}],
            "plans": [{
                "targetCandidateId": target["candidateId"],
                "targetGenerationAdvance": target["generationAdvance"],
                "triggerAdvance": trigger,
                "modeledPreTriggerAdvances": model_advances,
                "predictedGenerationAdvance": predicted,
                "boundaryId": boundary["boundaryId"],
                "boundaryOffset": boundary["offset"],
                "boundaryModelVersion": boundary["modelVersion"],
                "boundaryRevision": boundary["revision"],
                "probeCandidateId": candidate["candidateId"],
                "coarseTargetAdvance": coarse_target,
                "coarseAdvances": coarse_advances,
                "relocationReserve": settings["relocationReserve"],
                "preciseReserve": settings["preciseReserve"],
                "coarseBatches": batches,
                "checkpointRequiredAfterEachBatch": True,
            }],
            "hardwareStatus": "PendingHardwareValidation",
            "canStartFormalAutomation": False,
        }
        return {
            "type": "result",
            "protocolVersion": PROTOCOL_VERSION,
            "algorithmCommit": BASELINE["algorithm"]["commit"],
            "requestId": request["requestId"],
            "runId": request["runId"],
            "epochId": request["epochId"],
            "contextRevision": request["contextRevision"],
            "data": data,
        }


def calibrated_result(candidate=None, *, status="CalibratedOffline"):
    candidate = candidate or ProbeCandidate("npc-calibrated", {"interferenceAdvances": 2})
    return NpcCalibrationResult(
        status=status,
        candidate_ids=(candidate.candidate_id,),
        candidate=candidate.as_dict(),
        training_experiment_ids=("train-1", "train-2", "train-3"),
        validation_experiment_ids=("verify-1", "verify-2"),
        accounting=(),
        context_key="a" * 64,
    )


class AttemptPlannerTests(unittest.TestCase):
    def make_planner(self, calculator):
        return AttemptPlanner(
            calculator=calculator,
            run_id="run-plan",
            epoch_id="epoch-plan",
            context_revision=5,
        )

    def plan(self, planner, *, calibration=None):
        return planner.plan(
            anchor_seed0="123456789abcdef0",
            anchor_seed1="0fedcba987654321",
            current_advance=2,
            targets=[PlanningTarget("target-8", 8)],
            target_snapshot_id="snapshot-v4",
            target_request_digest="a" * 64,
            boundary=BoundaryAdjustment("battle.entry", 1, "boundary-v2", 4),
            probe_actions=[{"kind": "interference"}],
            calibration=calibration or calibrated_result(),
            settings=AttemptPlanningSettings(
                maximum_plan_evaluations=32,
                coarse_batch_size=2,
                relocation_reserve=1,
                precise_reserve=1,
            ),
        )

    def test_plan_result_enforces_equation_and_all_reserved_checkpoints(self):
        calculator = FakePlanningCalculator()
        result = self.plan(self.make_planner(calculator))

        self.assertEqual(result.status, "Feasible")
        self.assertEqual(len(calculator.requests), 1)
        self.assertEqual(calculator.requests[0]["operation"], "attempt.plan")
        self.assertEqual(result.target_snapshot_id, "snapshot-v4")
        plan = result.plans[0]
        self.assertEqual(plan["triggerAdvance"], 5)
        self.assertEqual(plan["modeledPreTriggerAdvances"], 2)
        self.assertEqual(plan["boundaryOffset"], 1)
        self.assertEqual(plan["predictedGenerationAdvance"], 8)
        self.assertEqual(plan["coarseAdvances"], 1)
        self.assertEqual(plan["coarseBatches"][0]["plannedEndAdvance"], 3)
        self.assertFalse(result.as_dict()["canStartFormalAutomation"])

    def test_ambiguous_calibration_and_mismatched_equation_never_produce_a_plan(self):
        calculator = FakePlanningCalculator()
        planner = self.make_planner(calculator)
        with self.assertRaisesRegex(ValueError, "offline-calibrated"):
            self.plan(planner, calibration=calibrated_result(status="Ambiguous"))
        self.assertEqual(calculator.requests, [])

        with self.assertRaisesRegex(AttemptPlanningNeedsAttention, r"violates p \+ M"):
            self.plan(self.make_planner(FakePlanningCalculator(corrupt_generation=True)))

    def test_stale_candidate_snapshot_is_rejected(self):
        with self.assertRaisesRegex(AttemptPlanningNeedsAttention, "stale candidate snapshot"):
            self.plan(self.make_planner(FakePlanningCalculator(stale_snapshot=True)))


if __name__ == "__main__":
    unittest.main()
