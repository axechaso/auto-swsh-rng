from __future__ import annotations

from pathlib import Path
import sys
import threading
import unittest
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtGui import QColor, QImage

from swsh_app.automation import (
    AttemptExecutionStatus,
    AttemptExecutionSettings,
    AttemptPlanningSettings,
    AutomationConfig,
    AutomationPhase,
    AutomationRunResult,
    AutomationRunStatus,
    BoundaryAdjustment,
    M2M4AttemptCoordinator,
    M2M4ClosedLoopCoordinator,
    NpcCalibrationOutcome,
    NpcCalibrationResult,
    ReplayActionSegment,
    ReplayAttemptActionPort,
    ReplayFrameRef,
    ReplayFrameSource,
    SeedBitObservation,
    SeedEpochSummary,
    SeedSequenceObservation,
    VirtualClock,
)
from swsh_app.automation.stage_executor import StageOutcome
from swsh_app.automation.stage_executor import StageResult
from swsh_app.backend import PROTOCOL_VERSION
from swsh_app.localization import BASELINE
from swsh_app.automation.replay import ReplayValidationError


SEED0 = "123456789abcdef0"
SEED1 = "0fedcba987654321"
SNAPSHOT_ID = "encounter-range-4d9aa48733d13859"
REQUEST_DIGEST = "b" * 64


def image(color):
    result = QImage(2, 2, QImage.Format.Format_ARGB32)
    result.fill(QColor(color))
    return result


def search_result(count=1, first_advance=10):
    searched_end = max(19, first_advance + count - 1)
    rows = tuple(
        {"advance": first_advance + index, "candidateOrdinal": 0}
        for index in range(count)
    )
    epoch = SeedEpochSummary(
        epoch_id="epoch-m2",
        seed0=SEED0,
        seed1=SEED1,
        observation_bits=128,
        verification_bits=128,
        searched_positions=searched_end + 1,
        searched_start=0,
        searched_end=searched_end,
        candidate_count=count,
        target_snapshot_id=SNAPSHOT_ID,
        target_request_digest=REQUEST_DIGEST,
        search_order_version="advance-fields-v1",
    )
    return AutomationRunResult(
        run_id="run-m2",
        status=AutomationRunStatus.TARGETS_FOUND,
        phase=AutomationPhase.SEARCH_TARGETS,
        epochs=(epoch,),
        candidate_rows=rows,
        target_snapshot_id=SNAPSHOT_ID,
        target_request_digest=REQUEST_DIGEST,
        search_order_version="advance-fields-v1",
    )


class FakeReplayObserver:
    def __init__(self, frames):
        self.frames = frames

    def observe_sequence(self, count, trigger, *, cancel_event=None):
        bits = []
        samples = []
        for index in range(count):
            if cancel_event and cancel_event.is_set():
                return SeedSequenceObservation("".join(map(str, bits)), tuple(samples), False, index)
            baseline = self.frames.snapshot_frame()
            trigger(index)
            observed = []
            last_frame = baseline.frame_id
            for _ in range(3):
                page = self.frames.frames_after(last_frame, limit=1)
                if not page:
                    return SeedSequenceObservation("".join(map(str, bits)), tuple(samples), False, index)
                observed.extend(page)
                last_frame = page[-1].frame_id
            bit = index % 2
            bits.append(bit)
            samples.append(SeedBitObservation(
                bit=bit,
                reason=None,
                frame_ids=tuple(frame.frame_id for frame in observed),
                zero_score=0.9 if bit == 0 else 0.1,
                one_score=0.9 if bit == 1 else 0.1,
                motion_score=0.8,
                started_at_ns=observed[0].captured_at_ns,
                ended_at_ns=observed[-1].captured_at_ns,
                evidence_ids=tuple(frame.evidence_id for frame in observed),
            ))
        return SeedSequenceObservation("".join(map(str, bits)), tuple(samples), True, None)


class FakeCalculator:
    def __init__(self):
        self.requests = []

    def execute(self, request, cancel_event):
        self.requests.append(dict(request))
        operation = request["operation"]
        if operation == "seed.solve":
            data = {
                "observationCount": 128,
                "boundarySemanticsVersion": "retail-seed-observation-v1",
                "stateAfterObservations": {"seed0": SEED0, "seed1": SEED1},
            }
            return self._response(request, data)
        if operation == "seed.verify":
            return self._response(request, {
                "matches": True,
                "stateAfterObservations": {"seed0": SEED0, "seed1": SEED1},
            })
        if operation == "encounter.search":
            scan_start = request["scanCursor"]
            scan_end = min(request["end"], scan_start + request["scanLimit"] - 1)
            all_rows = ([{"advance": 1, "candidateOrdinal": 0}]
                        if request["start"] <= 1 <= request["end"]
                        and scan_start <= 1 <= scan_end else [])
            cursor = request["candidateCursor"]
            page_rows = all_rows[cursor:cursor + request["candidatePageSize"]]
            page_complete = cursor + len(page_rows) >= len(all_rows)
            data = {
                "searchedStart": scan_start,
                "searchedEnd": scan_end,
                "candidateStart": cursor,
                "searchedPositions": scan_end - scan_start + 1,
                "scanComplete": scan_end == request["end"],
                "nextScanCursor": None if scan_end == request["end"] else scan_end + 1,
                "candidateTotal": len(all_rows),
                "candidatePageComplete": page_complete,
                "nextCandidateCursor": None if page_complete else cursor + len(page_rows),
                "snapshotId": "snapshot-m2-full",
                "requestDigest": "a" * 64,
                "orderVersion": "advance-fields-v1",
                "rows": page_rows,
            }
            return self._response(request, data)
        if operation == "seed.locate":
            start = request["start"]
            state = {"seed0": SEED0, "seed1": SEED1}
            data = {
                "windowStart": start,
                "windowEnd": request["end"],
                "candidateCount": 1,
                "complete": True,
                "candidates": [{
                    "firstObservedAdvance": start,
                    "stateBeforeObservations": state,
                    "stateAfterObservations": state,
                    "stateAfterObservedAdvance": state,
                }],
            }
        else:
            request_planning = request["attemptPlanning"]
            boundary = request["boundaryAdjustment"]
            candidate = request["probeCandidates"][0]
            modeled = candidate.get("interferenceAdvances", 0)
            plans = []
            statuses = []
            for target in request["targetCandidates"]:
                generation = target["generationAdvance"]
                trigger = generation - modeled - boundary["offset"]
                reserve = request_planning["relocationReserve"] + request_planning["preciseReserve"]
                coarse_target = trigger - reserve
                coarse = coarse_target - request["currentAdvance"]
                batches = []
                cursor = request["currentAdvance"]
                while cursor < coarse_target:
                    amount = min(request_planning["coarseBatchSize"], coarse_target - cursor)
                    batches.append({
                        "startAdvance": cursor,
                        "requestedAdvances": amount,
                        "plannedEndAdvance": cursor + amount,
                        "requiresRelocationBeforeNextBatch": True,
                    })
                    cursor += amount
                plans.append({
                    "targetCandidateId": target["candidateId"],
                    "targetGenerationAdvance": generation,
                    "triggerAdvance": trigger,
                    "modeledPreTriggerAdvances": modeled,
                    "predictedGenerationAdvance": generation,
                    "boundaryId": boundary["boundaryId"],
                    "boundaryOffset": boundary["offset"],
                    "boundaryModelVersion": boundary["modelVersion"],
                    "boundaryRevision": boundary["revision"],
                    "probeCandidateId": candidate["candidateId"],
                    "coarseTargetAdvance": coarse_target,
                    "coarseAdvances": coarse,
                    "relocationReserve": request_planning["relocationReserve"],
                    "preciseReserve": request_planning["preciseReserve"],
                    "coarseBatches": batches,
                    "checkpointRequiredAfterEachBatch": True,
                })
                statuses.append({"candidateId": target["candidateId"], "status": "Feasible"})
            data = {
                "reportSchema": "auto-swsh-attempt-plan",
                "reportVersion": 1,
                "status": "Feasible",
                "targetSnapshotId": request["targetSnapshotId"],
                "targetRequestDigest": request["targetRequestDigest"],
                "currentAdvance": request["currentAdvance"],
                "boundary": boundary,
                "probeCandidateId": candidate["candidateId"],
                "evaluationCount": len(request["targetCandidates"]),
                "underBudgetPositionCount": 0,
                "targetStatuses": statuses,
                "plans": plans,
                "hardwareStatus": "PendingHardwareValidation",
                "canStartFormalAutomation": False,
            }
        return self._response(request, data)

    @staticmethod
    def _response(request, data):
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


class M2ToM4ReplayIntegrationTests(unittest.TestCase):
    def make_replay(self):
        clock = VirtualClock()
        refs = [ReplayFrameRef(1, 0, "replay:frame:1", lambda: image("black"))]
        segments = []

        def add_segment(purpose, index, frame_ids):
            frames = []
            for frame_id in frame_ids:
                color = "red" if frame_id % 2 else "blue"
                refs.append(ReplayFrameRef(
                    frame_id,
                    frame_id * 1_000_000_000,
                    f"replay:frame:{frame_id}",
                    lambda color=color: image(color),
                ))
                frames.append(refs[-1])
            segments.append(ReplayActionSegment(purpose, index, tuple(frames)))

        add_segment("coarse_batch", 0, [2])
        add_segment("relocation_bit", 0, [3, 4, 5])
        add_segment("relocation_bit", 1, [6, 7, 8])
        add_segment("final_trigger", 0, [9])
        return ReplayFrameSource(
            replay_id="m4-replay",
            scenario_id="scenario-1",
            source_kind="synthetic",
            baseline=refs[0],
            segments=segments,
            clock=clock,
        )

    def make_calibration(self, interference_advances=0):
        candidate = {"candidateId": "npc-calibrated", "interferenceAdvances": interference_advances}
        return NpcCalibrationResult(
            status="CalibratedOffline",
            candidate_ids=(candidate["candidateId"],),
            candidate=candidate,
            training_experiment_ids=("train-1", "train-2", "train-3"),
            validation_experiment_ids=("verify-1", "verify-2"),
            accounting=(),
            context_key="c" * 64,
        )

    def test_m2_snapshot_flows_through_m4_relocation_and_replay_trigger(self):
        source = self.make_replay()
        actions = ReplayAttemptActionPort(
            source, run_id="run-m2", epoch_id="epoch-m2", context_revision=5,
        )
        calculator = FakeCalculator()
        coordinator = M2M4AttemptCoordinator(
            search_result=search_result(first_advance=12),
            calculator=calculator,
            observer=FakeReplayObserver(source),
            actions=actions,
            calibration=self.make_calibration(interference_advances=1),
            boundary=BoundaryAdjustment("battle.entry", 1, "boundary-v1", 1),
            probe_actions=({"kind": "battle_entry"},),
            run_id="run-m2",
            context_revision=5,
            planning_settings=AttemptPlanningSettings(coarse_batch_size=20),
            execution_settings=AttemptExecutionSettings(
                relocation_observation_bits=2,
                maximum_position_error=0,
                maximum_coarse_batch_size=20,
                precise_reserve=0,
                maximum_replans=2,
                source_kind="replay",
                execution_mode="replay",
            ),
        )

        result = coordinator.run(attempt_id="attempt-1")

        self.assertEqual(result.status, AttemptExecutionStatus.TRIGGER_EXECUTED, result.reason)
        self.assertEqual(result.target_candidate_id, "advance-12-ordinal-0")
        self.assertEqual(result.target_generation_advance, 12)
        self.assertEqual(result.trigger_advance, 10)
        self.assertEqual(result.target_snapshot_id, SNAPSHOT_ID)
        self.assertEqual(result.target_request_digest, REQUEST_DIGEST)
        self.assertEqual(source.remaining_actions, 0)
        self.assertEqual([request["operation"] for request in calculator.requests],
                         ["attempt.plan", "attempt.plan", "seed.locate", "attempt.plan"])
        self.assertEqual([event["kind"] for event in actions.events if event["kind"] in {
            "coarse_batch", "relocation_bit", "final_trigger",
        }], [
            "coarse_batch", "relocation_bit", "relocation_bit", "final_trigger",
        ])
        source.close()

    def test_search_result_with_stale_or_partial_snapshot_is_rejected(self):
        original = search_result()
        epoch = original.epochs[0]
        stale = AutomationRunResult(
            run_id=original.run_id,
            status=original.status,
            phase=original.phase,
            epochs=(epoch,),
            candidate_rows=original.candidate_rows,
            target_snapshot_id="other-snapshot",
            target_request_digest=REQUEST_DIGEST,
            search_order_version="advance-fields-v1",
        )
        with self.assertRaisesRegex(ValueError, "stable, complete search snapshot"):
            M2M4AttemptCoordinator(
                search_result=stale,
                calculator=FakeCalculator(),
                observer=FakeReplayObserver(self.make_replay()),
                actions=object(),
                calibration=self.make_calibration(),
                boundary=BoundaryAdjustment("battle.entry", 0, "boundary-v1", 1),
                probe_actions=({"kind": "battle_entry"},),
                run_id="run-m2",
                context_revision=5,
            )

        incomplete_epoch = SeedEpochSummary(
            epoch_id="epoch-m2", seed0=SEED0, seed1=SEED1,
            observation_bits=128, verification_bits=128, candidate_count=2,
            searched_positions=20, searched_start=0, searched_end=19,
            target_snapshot_id=SNAPSHOT_ID,
            target_request_digest=REQUEST_DIGEST,
            search_order_version="advance-fields-v1",
        )
        incomplete = AutomationRunResult(
            run_id=original.run_id,
            status=original.status,
            phase=original.phase,
            epochs=(incomplete_epoch,),
            candidate_rows=original.candidate_rows,
            target_snapshot_id=SNAPSHOT_ID,
            target_request_digest=REQUEST_DIGEST,
            search_order_version="advance-fields-v1",
        )
        with self.assertRaisesRegex(ValueError, "complete candidate count"):
            M2M4AttemptCoordinator(
                search_result=incomplete,
                calculator=FakeCalculator(),
                observer=FakeReplayObserver(self.make_replay()),
                actions=object(),
                calibration=self.make_calibration(),
                boundary=BoundaryAdjustment("battle.entry", 0, "boundary-v1", 1),
                probe_actions=({"kind": "battle_entry"},),
                run_id="run-m2",
                context_revision=5,
            )

    def test_m4_plans_every_candidate_page_before_selecting_from_large_m2_snapshot(self):
        class FinalAction:
            def __init__(self):
                self.final_calls = 0

            def execute_coarse_batch(self, stage_id, advances, cancel_event):
                raise AssertionError("earliest candidate requires no coarse action")

            def execute_precise_advances(self, stage_id, advances, cancel_event):
                raise AssertionError("earliest candidate requires no precise action")

            def trigger_relocation_bit(self, index, cancel_event):
                raise AssertionError("earliest candidate requires no relocation")

            def execute_final_trigger(self, plan, cancel_event):
                self.final_calls += 1
                return StageResult(
                    "run-m2", "epoch-m2", 5, "final-trigger", StageOutcome.COMPLETED,
                    1, 2, 1, 2,
                )

            def stop_scripts(self):
                pass

        calculator = FakeCalculator()
        actions = FinalAction()
        coordinator = M2M4AttemptCoordinator(
            search_result=search_result(513),
            calculator=calculator,
            observer=object(),
            actions=actions,
            calibration=self.make_calibration(),
            boundary=BoundaryAdjustment("battle.entry", 0, "boundary-v1", 1),
            probe_actions=({"kind": "battle_entry"},),
            run_id="run-m2",
            context_revision=5,
            planning_settings=AttemptPlanningSettings(coarse_batch_size=20),
            execution_settings=AttemptExecutionSettings(
                relocation_observation_bits=2,
                maximum_position_error=0,
                maximum_coarse_batch_size=20,
                precise_reserve=0,
                maximum_replans=1,
                source_kind="synthetic",
                execution_mode="simulation",
            ),
        )

        result = coordinator.run(attempt_id="attempt-pages", current_advance=10)

        plan_requests = [request for request in calculator.requests if request["operation"] == "attempt.plan"]
        self.assertEqual(coordinator.planning_page_count, 2)
        self.assertEqual([len(request["targetCandidates"]) for request in plan_requests], [512, 1])
        self.assertEqual(result.status, AttemptExecutionStatus.TRIGGER_EXECUTED, result.reason)
        self.assertEqual(result.target_candidate_id, "advance-10-ordinal-0")
        self.assertEqual(actions.final_calls, 1)

    def test_replay_action_fault_returns_failed_stage_and_never_advances_manifest(self):
        source = self.make_replay()
        actions = ReplayAttemptActionPort(
            source, run_id="run-m2", epoch_id="epoch-m2", faults={"coarse_batch": 1},
        )
        stage = actions.execute_coarse_batch("coarse-0000", 8, threading.Event())
        self.assertEqual(stage.outcome, StageOutcome.FAILED)
        self.assertEqual(source.remaining_actions, 4)
        with self.assertRaisesRegex(ReplayValidationError, "action mismatch"):
            source.trigger("relocation_bit", 0)
        source.close()


class ClosedLoopDevice:
    def __init__(self):
        self.lease = object()
        self.active = False
        self.events = []

    def acquire_task_lease(self):
        self.active = True
        self.events.append(("acquire", self.lease))
        return self.lease

    def preflight(self, config, lease, cancel_event):
        self.events.append(("preflight", lease))

    def restore_scene(self, config, lease, epoch_id, *, after_restart, cancel_event):
        self.events.append(("restore", lease, epoch_id, after_restart))
        return True

    def trigger_seed_bit(self, config, lease, epoch_id, purpose, index, cancel_event):
        self.events.append(("seed_bit", lease, purpose, index))

    def restart_game(self, config, lease, epoch_id, cancel_event):
        self.events.append(("restart", lease, epoch_id))
        return True

    def cancel(self):
        self.events.append(("cancel",))

    def stop_scripts(self):
        self.events.append(("stop", self.lease))

    def release_task_lease(self, lease):
        self.events.append(("release", lease))
        self.active = False


class M2SeedObserver:
    def observe_sequence(self, count, trigger, *, cancel_event=None):
        bits = "0" * count
        for index in range(count):
            if cancel_event and cancel_event.is_set():
                return SimpleNamespace(observations=bits[:index], complete=False, unknown_index=index)
            trigger(index)
        return SimpleNamespace(observations=bits, complete=True, unknown_index=None)


class M2M3M4ClosedLoopTests(unittest.TestCase):
    def make_device_and_coordinator(self, *, calibration_status="CalibratedOffline",
                                    execution_mode="simulation", capture_stage_factory=None):
        device = ClosedLoopDevice()
        calculator = FakeCalculator()
        config = AutomationConfig(
            run_id="closed-loop-run",
            context_revision=5,
            scenario_id="scenario-1",
            min_advance=1,
            max_advance=1,
            search_request={"game": "Shield", "kind": "overworld"},
            chunk_size=1,
            max_epochs=1,
            execution_mode=execution_mode,
        )
        action_leases = []

        class Actions:
            def execute_coarse_batch(self, stage_id, advances, cancel_event):
                raise AssertionError("the current candidate must not need coarse actions")

            def execute_precise_advances(self, stage_id, advances, cancel_event):
                raise AssertionError("the current candidate must not need precise advances")

            def trigger_relocation_bit(self, index, cancel_event):
                raise AssertionError("the current candidate must not need relocation")

            def execute_final_trigger(self, plan, cancel_event):
                self_final.append(device.active)
                return StageResult(
                    config.run_id, search_epoch[0], config.context_revision,
                    "final-trigger", StageOutcome.COMPLETED, 1, 2, 1, 2,
                )

            def stop_scripts(self):
                device.events.append(("attempt_stop", device.lease))

        self_final = []
        search_epoch = [None]
        candidate = {"candidateId": "npc-calibrated", "interferenceAdvances": 0}
        calibration = NpcCalibrationResult(
            status="CalibratedOffline",
            candidate_ids=(candidate["candidateId"],),
            candidate=candidate,
            training_experiment_ids=("train-1", "train-2", "train-3"),
            validation_experiment_ids=("verify-1", "verify-2"),
            accounting=(),
            context_key="c" * 64,
        )
        if calibration_status != "CalibratedOffline":
            calibration = NpcCalibrationResult(
                status=calibration_status,
                candidate_ids=(),
                candidate=None,
                training_experiment_ids=("train-1",),
                validation_experiment_ids=(),
                accounting=(),
                context_key="d" * 64,
            )

        def calibration_provider(search, lease, cancel_event):
            self.assertIs(lease, device.lease)
            search_epoch[0] = search.epochs[-1].epoch_id
            self.assertEqual(search.status, AutomationRunStatus.TARGETS_FOUND)
            return NpcCalibrationOutcome(calibration, current_advance=1)

        def actions_factory(lease, search, outcome):
            action_leases.append(lease)
            return Actions()

        coordinator = M2M4ClosedLoopCoordinator(
            config=config,
            device=device,
            observer=M2SeedObserver(),
            calculator=calculator,
            calibration_provider=calibration_provider,
            actions_factory=actions_factory,
            boundary=BoundaryAdjustment("battle.entry", 0, "boundary-v1", 1),
            probe_actions=({"kind": "battle_entry"},),
            planning_settings=AttemptPlanningSettings(coarse_batch_size=1),
            execution_settings=AttemptExecutionSettings(
                precise_reserve=0,
                maximum_replans=1,
                source_kind="synthetic",
                execution_mode="simulation",
            ),
            capture_stage_factory=capture_stage_factory,
        )
        return coordinator, device, action_leases, self_final

    def test_m2_calibration_and_m4_share_one_lease_until_trigger_finishes(self):
        coordinator, device, action_leases, final_lease_state = self.make_device_and_coordinator()

        result = coordinator.run(attempt_id="closed-attempt")

        self.assertEqual(result.status, "ATTEMPT_TRIGGERED_PENDING_CAPTURE", result.reason)
        self.assertEqual(result.search_result.status, AutomationRunStatus.TARGETS_FOUND)
        self.assertEqual(result.attempt_result.status, AttemptExecutionStatus.TRIGGER_EXECUTED)
        self.assertEqual(action_leases, [device.lease])
        self.assertEqual(final_lease_state, [True])
        releases = [event for event in device.events if event[0] == "release"]
        self.assertEqual(releases, [("release", device.lease)])
        self.assertFalse(device.active)
        self.assertFalse(result.as_dict()["canStartFormalAutomation"])

    def test_optional_m5_capture_stage_keeps_the_same_lease_until_save_result(self):
        device = None
        observed = []

        class CaptureStage:
            status = "SAVED_SUCCESS"
            reason = None

            def run(self):
                observed.append(("run", device.active))
                return self

            def as_dict(self):
                return {
                    "reportSchema": "auto-swsh-capture-attempt",
                    "reportVersion": 1,
                    "status": self.status,
                    "attemptId": "closed-attempt",
                    "workflow": {"phase": "completed"},
                    "saveCommit": {"stage": "Completed", "receipt": {}},
                }

        def capture_stage_factory(lease, attempt_result, search_result, calibration_outcome, cancel_event):
            observed.append(("factory", device.active, lease is device.lease,
                             attempt_result.status, search_result.status,
                             calibration_outcome.calibration.status, cancel_event.is_set()))
            return CaptureStage()

        coordinator, device, _, _ = self.make_device_and_coordinator(
            capture_stage_factory=capture_stage_factory,
        )

        result = coordinator.run(attempt_id="closed-attempt")

        self.assertEqual(result.status, "SUCCESS_SAVED", result.reason)
        self.assertEqual(observed[0], (
            "factory", True, True, AttemptExecutionStatus.TRIGGER_EXECUTED,
            AutomationRunStatus.TARGETS_FOUND, "CalibratedOffline", False,
        ))
        self.assertEqual(observed[1], ("run", True))
        self.assertEqual(result.as_dict()["capture"]["saveCommit"]["stage"], "Completed")
        self.assertEqual([event[0] for event in device.events if event[0] == "release"], ["release"])
        self.assertFalse(device.active)

    def test_m5_stage_cannot_report_success_before_completed_save_receipt(self):
        class IncompleteCaptureStage:
            status = "SAVED_SUCCESS"
            reason = None

            def run(self):
                return self

            def as_dict(self):
                return {
                    "reportSchema": "auto-swsh-capture-attempt",
                    "reportVersion": 1,
                    "status": self.status,
                    "attemptId": "closed-attempt",
                    "workflow": {"phase": "completed"},
                    "saveCommit": {"stage": "SaveRequested", "receipt": None},
                }

        coordinator, _, _, _ = self.make_device_and_coordinator(
            capture_stage_factory=lambda *args: IncompleteCaptureStage(),
        )
        result = coordinator.run(attempt_id="closed-attempt")

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertIn("completed SaveReceipt", result.reason)

    def test_ambiguous_m3_calibration_stops_before_m4_action_factory(self):
        coordinator, device, action_leases, _ = self.make_device_and_coordinator(
            calibration_status="Ambiguous",
        )

        result = coordinator.run(attempt_id="closed-attempt")

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertEqual(result.reason, "NPC_CALIBRATION_AMBIGUOUS")
        self.assertIsNone(result.attempt_result)
        self.assertEqual(action_leases, [])
        self.assertEqual([event[0] for event in device.events if event[0] == "release"], ["release"])

    def test_formal_gate_blocks_before_device_lease_is_acquired(self):
        coordinator, device, action_leases, _ = self.make_device_and_coordinator(
            execution_mode="formal",
        )

        result = coordinator.run(attempt_id="formal-attempt")

        self.assertEqual(result.status, "NEEDS_ATTENTION")
        self.assertIn("FORMAL_START_BLOCKED", result.reason)
        self.assertEqual(device.events, [])
        self.assertEqual(action_leases, [])


if __name__ == "__main__":
    unittest.main()
