from __future__ import annotations

from pathlib import Path
import sys
import threading
import unittest
from types import MappingProxyType

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import (
    AttemptExecutionCoordinator,
    AttemptExecutionSettings,
    AttemptExecutionStatus,
    AttemptPlanResult,
    AttemptStageScripts,
    EcsAttemptActionPort,
    SeedSequenceObservation,
)
from swsh_app.automation.seed_observer import SeedBitObservation
from swsh_app.automation.stage_executor import StageOutcome, StageResult
from swsh_app.localization import BASELINE


SEED0 = "123456789abcdef0"
SEED1 = "0fedcba987654321"
SNAPSHOT = "encounter-snapshot-v1"
DIGEST = "a" * 64


class FakeObserver:
    def __init__(self):
        self.frame = 0

    def observe_sequence(self, count, trigger, *, cancel_event=None):
        bits = []
        samples = []
        for index in range(count):
            if cancel_event.is_set():
                return SeedSequenceObservation("".join(map(str, bits)), tuple(samples), False, index)
            trigger(index)
            first = self.frame + 1
            self.frame += 2
            samples.append(SeedBitObservation(
                bit=index % 2, reason=None, frame_ids=(first, first + 1),
                zero_score=0.9, one_score=0.1, motion_score=0.8,
                started_at_ns=first, ended_at_ns=first + 1,
                evidence_ids=(f"evidence:{first}",),
            ))
            bits.append(index % 2)
        return SeedSequenceObservation("".join(map(str, bits)), tuple(samples), True, None)


class FakeCalculator:
    def __init__(self, *, candidate_count=1):
        self.candidate_count = candidate_count
        self.requests = []

    def execute(self, request, cancel_event):
        self.requests.append(dict(request))
        start = request["start"]
        end = request["end"]
        midpoint = (start + end) // 2
        candidates = []
        for offset in range(self.candidate_count):
            first = midpoint + offset
            state = {"seed0": SEED0, "seed1": SEED1}
            candidates.append({
                "firstObservedAdvance": first,
                "stateAfterObservedAdvance": state,
                "stateBeforeObservations": state,
                "stateAfterObservations": state,
            })
        return {
            "type": "result",
            "protocolVersion": request["protocolVersion"],
            "requestId": request["requestId"],
            "runId": request["runId"],
            "epochId": request["epochId"],
            "contextRevision": request["contextRevision"],
            "algorithmCommit": BASELINE["algorithm"]["commit"],
            "data": {
                "windowStart": start,
                "windowEnd": end,
                "candidateCount": self.candidate_count,
                "complete": True,
                "candidates": candidates,
            },
        }


class FakeActions:
    def __init__(self, *, run_id="run-1", epoch_id="epoch-1", context_revision=3,
                 coarse_outcome=StageOutcome.COMPLETED):
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.coarse_outcome = coarse_outcome
        self.coarse_batches = []
        self.precise_batches = []
        self.relocation_bits = []
        self.final_plans = []
        self.stopped = False
        self._ticks = 0

    def _stage(self, stage_id, outcome=StageOutcome.COMPLETED):
        self._ticks += 1
        return StageResult(
            self.run_id, self.epoch_id, self.context_revision, stage_id, outcome,
            self._ticks * 10, self._ticks * 10 + 1, self._ticks, self._ticks + 1,
            None if outcome == StageOutcome.COMPLETED else "injected action failure",
        )

    def execute_coarse_batch(self, stage_id, advances, cancel_event):
        self.coarse_batches.append((stage_id, advances))
        return self._stage(stage_id, self.coarse_outcome)

    def execute_precise_advances(self, stage_id, advances, cancel_event):
        self.precise_batches.append((stage_id, advances))
        return self._stage(stage_id)

    def trigger_relocation_bit(self, index, cancel_event):
        self.relocation_bits.append(index)

    def execute_final_trigger(self, plan, cancel_event):
        self.final_plans.append(dict(plan))
        return self._stage("final-trigger")

    def stop_scripts(self):
        self.stopped = True


class AttemptExecutionTests(unittest.TestCase):
    def setUp(self):
        self.calculator = FakeCalculator()
        self.observer = FakeObserver()
        self.actions = FakeActions()
        self.coordinator = AttemptExecutionCoordinator(
            calculator=self.calculator, observer=self.observer, actions=self.actions,
            attempt_id="attempt-1", run_id="run-1", epoch_id="epoch-1", context_revision=3,
        )

    def planner(self, *, snapshot_ids=None):
        calls = []

        def replan(current, relocation_reserve, precise_reserve):
            calls.append((current, relocation_reserve, precise_reserve))
            trigger = 118
            generation = 120
            coarse_target = trigger - relocation_reserve - precise_reserve
            if coarse_target < current:
                return AttemptPlanResult(
                    "TargetAlreadyPassed", current, SNAPSHOT, DIGEST, (),
                    (MappingProxyType({"candidateId": "target-120", "status": "TargetAlreadyPassed"}),),
                    len(calls), 0, "PendingHardwareValidation", "all candidates are behind the confirmed position",
                )
            coarse_count = coarse_target - current
            batches = []
            cursor = current
            while cursor < coarse_target:
                amount = min(30, coarse_target - cursor)
                batches.append({
                    "startAdvance": cursor,
                    "requestedAdvances": amount,
                    "plannedEndAdvance": cursor + amount,
                    "requiresRelocationBeforeNextBatch": True,
                })
                cursor += amount
            snapshot = snapshot_ids[min(len(snapshot_ids) - 1, len(calls) - 1)] if snapshot_ids else SNAPSHOT
            row = MappingProxyType({
                "targetCandidateId": "target-120",
                "targetGenerationAdvance": generation,
                "triggerAdvance": trigger,
                "modeledPreTriggerAdvances": 2,
                "predictedGenerationAdvance": generation,
                "boundaryId": "battle.entry",
                "boundaryOffset": 0,
                "boundaryModelVersion": "boundary-v1",
                "boundaryRevision": 0,
                "probeCandidateId": "npc-v1",
                "probeActions": [{"kind": "menuclose"}],
                "coarseTargetAdvance": coarse_target,
                "coarseAdvances": coarse_count,
                "relocationReserve": relocation_reserve,
                "preciseReserve": precise_reserve,
                "coarseBatches": batches,
                "checkpointRequiredAfterEachBatch": True,
            })
            return AttemptPlanResult(
                "Feasible", current, snapshot, DIGEST, (row,),
                (MappingProxyType({"candidateId": "target-120", "status": "Feasible"}),),
                len(calls), max(0, coarse_count), "PendingHardwareValidation",
            )
        return replan, calls

    def run_attempt(self, *, settings=None, snapshot_ids=None):
        replan, calls = self.planner(snapshot_ids=snapshot_ids)
        result = self.coordinator.run(
            anchor_seed0=SEED0, anchor_seed1=SEED1, current_advance=0,
            replan=replan,
            settings=settings or AttemptExecutionSettings(
                relocation_observation_bits=2, maximum_position_error=2,
                precise_reserve=4, maximum_replans=20,
            ),
        )
        return result, calls

    def test_coarse_batches_relocate_then_precise_trigger_remains_snapshot_bound(self):
        result, calls = self.run_attempt()

        self.assertEqual(result.status, AttemptExecutionStatus.TRIGGER_EXECUTED)
        self.assertEqual(result.current_advance, 118)
        self.assertEqual(result.target_generation_advance, 120)
        self.assertEqual(result.target_candidate_id, "target-120")
        self.assertEqual(result.hardware_status, "PendingHardwareValidation")
        self.assertEqual(result.as_dict()["canStartFormalAutomation"], False)
        self.assertGreater(len(self.actions.coarse_batches), 1)
        self.assertEqual(self.actions.precise_batches[0][1], 4)
        self.assertEqual(len(self.actions.final_plans), 1)
        self.assertEqual(len([event for event in result.events if event["event"] == "position_relocated"]), len(self.calculator.requests))
        self.assertEqual({request["operation"] for request in self.calculator.requests}, {"seed.locate"})
        self.assertTrue(all(request["sourceKind"] == "synthetic" for request in self.calculator.requests))
        self.assertEqual(result.target_snapshot_id, SNAPSHOT)
        self.assertTrue(all(call[0] <= 118 for call in calls))

    def test_ambiguous_relocation_stops_without_precise_or_final_actions(self):
        self.calculator.candidate_count = 2
        result, _ = self.run_attempt()

        self.assertEqual(result.status, AttemptExecutionStatus.NEEDS_ATTENTION)
        self.assertEqual(result.reason_code, "RELOCATION_AMBIGUOUS")
        self.assertTrue(self.actions.stopped)
        self.assertEqual(self.actions.precise_batches, [])
        self.assertEqual(self.actions.final_plans, [])

    def test_action_failure_stops_the_task_and_never_issues_final_trigger(self):
        self.actions.coarse_outcome = StageOutcome.FAILED
        result, _ = self.run_attempt()

        self.assertEqual(result.status, AttemptExecutionStatus.NEEDS_ATTENTION)
        self.assertEqual(result.reason_code, "ACTION_STAGE_FAILED")
        self.assertTrue(self.actions.stopped)
        self.assertEqual(self.actions.final_plans, [])

    def test_changed_target_snapshot_invalidates_replanning(self):
        result, _ = self.run_attempt(snapshot_ids=[SNAPSHOT, "different-snapshot"])

        self.assertEqual(result.status, AttemptExecutionStatus.NEEDS_ATTENTION)
        self.assertEqual(result.reason_code, "STALE_TARGET_SNAPSHOT")
        self.assertEqual(self.actions.coarse_batches, [])

    def test_formal_mode_is_blocked_before_any_device_action(self):
        result, _ = self.run_attempt(settings=AttemptExecutionSettings(
            relocation_observation_bits=2, maximum_position_error=2,
            precise_reserve=4, maximum_replans=20, execution_mode="formal",
        ))

        self.assertEqual(result.reason_code, "FORMAL_START_BLOCKED")
        self.assertEqual(self.actions.coarse_batches, [])
        self.assertEqual(self.actions.relocation_bits, [])

    def test_ecs_stage_adapter_maps_each_planned_action_to_its_scenario_script(self):
        class StageExecutor:
            def __init__(self):
                self.calls = []
                self.cancelled = False

            def execute(self, stage_id, script):
                self.calls.append((stage_id, script))
                return StageResult(
                    "run-1", "epoch-1", 3, stage_id, StageOutcome.COMPLETED,
                    10, 11, 2, 3,
                )

            def cancel(self):
                self.cancelled = True

        executor = StageExecutor()
        actions = EcsAttemptActionPort(executor, AttemptStageScripts(
            coarse_batch=lambda amount: f"coarse:{amount}",
            precise_advances=lambda amount: f"precise:{amount}",
            relocation_bit=lambda index: f"relocate:{index}",
            final_trigger=lambda plan: f"trigger:{plan['targetCandidateId']}",
        ))
        actions.execute_coarse_batch("coarse-1", 17, threading.Event())
        actions.execute_precise_advances("precise-1", 3, threading.Event())
        actions.trigger_relocation_bit(4, threading.Event())
        actions.execute_final_trigger({"targetCandidateId": "target-1"}, threading.Event())
        actions.stop_scripts()

        self.assertEqual(executor.calls, [
            ("coarse-1", "coarse:17"),
            ("precise-1", "precise:3"),
            ("relocation-0000-bit-0004", "relocate:4"),
            ("final-trigger", "trigger:target-1"),
        ])
        self.assertTrue(executor.cancelled)


if __name__ == "__main__":
    unittest.main()
