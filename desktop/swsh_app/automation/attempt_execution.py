"""Checkpointed execution of a validated M4 attempt plan.

The coordinator owns decisions and evidence checks. An injected stage port owns
all button actions; a single task lease must remain held by its caller.
"""
from __future__ import annotations

from collections.abc import Callable, Mapping
from dataclasses import dataclass
from enum import StrEnum
import json
import threading
import uuid
from types import MappingProxyType
from typing import Any, Protocol

from ..backend import PROTOCOL_VERSION, validate_event
from .attempt_planner import AttemptPlanResult
from .seed_observer import SeedSequenceObservation
from .stage_executor import StageOutcome, StageResult


MAX_ADVANCE = 1_000_000_000


class AttemptExecutionError(ValueError):
    pass


class AttemptExecutionNeedsAttention(RuntimeError):
    def __init__(self, reason_code: str, message: str):
        super().__init__(message)
        self.reason_code = reason_code


class AttemptExecutionStatus(StrEnum):
    TRIGGER_EXECUTED = "TRIGGER_EXECUTED_PENDING_SCENE_CONFIRMATION"
    NEEDS_ATTENTION = "NEEDS_ATTENTION"
    CANCELLED = "CANCELLED"


@dataclass(frozen=True, slots=True)
class AttemptExecutionSettings:
    relocation_observation_bits: int = 16
    maximum_position_error: int = 64
    maximum_coarse_batch_size: int = 5_000
    precise_reserve: int = 8
    maximum_replans: int = 100
    source_kind: str = "synthetic"
    execution_mode: str = "simulation"

    def __post_init__(self):
        for name, value, maximum in (
            ("relocation_observation_bits", self.relocation_observation_bits, 4096),
            ("maximum_replans", self.maximum_replans, 1_000),
        ):
            if isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= maximum:
                raise AttemptExecutionError(f"{name} must be between 1 and {maximum}")
        for name, value, maximum in (
            ("maximum_coarse_batch_size", self.maximum_coarse_batch_size, 100_000),
            ("maximum_position_error", self.maximum_position_error, 1_000_000),
            ("precise_reserve", self.precise_reserve, 100_000),
        ):
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= maximum:
                raise AttemptExecutionError(f"{name} must be between 0 and {maximum}")
        if self.source_kind not in {"synthetic", "replay", "real"}:
            raise AttemptExecutionError("source_kind must be synthetic, replay, or real")
        if self.execution_mode not in {"simulation", "replay", "diagnostic", "formal"}:
            raise AttemptExecutionError("execution_mode must be simulation, replay, diagnostic, or formal")


@dataclass(frozen=True, slots=True)
class RelocationResult:
    first_observed_advance: int
    current_advance: int
    window_start: int
    window_end: int
    observation_length: int
    observations: str
    state_before_observations: Mapping[str, str]
    state_after_observations: Mapping[str, str]
    frame_ids: tuple[int, ...]
    evidence_ids: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class AttemptExecutionResult:
    status: AttemptExecutionStatus
    attempt_id: str
    run_id: str
    epoch_id: str
    context_revision: int
    current_advance: int
    target_candidate_id: str | None
    target_generation_advance: int | None
    trigger_advance: int | None
    target_snapshot_id: str | None
    target_request_digest: str | None
    hardware_status: str
    reason_code: str | None
    reason: str | None
    events: tuple[Mapping[str, Any], ...]

    def as_dict(self) -> dict[str, Any]:
        return {
            "reportSchema": "auto-swsh-attempt-execution",
            "reportVersion": 1,
            "status": self.status.value,
            "attemptId": self.attempt_id,
            "runId": self.run_id,
            "epochId": self.epoch_id,
            "contextRevision": self.context_revision,
            "currentAdvance": self.current_advance,
            "targetCandidateId": self.target_candidate_id,
            "targetGenerationAdvance": self.target_generation_advance,
            "triggerAdvance": self.trigger_advance,
            "targetSnapshotId": self.target_snapshot_id,
            "targetRequestDigest": self.target_request_digest,
            "hardwareStatus": self.hardware_status,
            "canStartFormalAutomation": False,
            "reasonCode": self.reason_code,
            "reason": self.reason,
            "events": json.loads(json.dumps([dict(event) for event in self.events], allow_nan=False)),
        }


class AttemptCalculatorPort(Protocol):
    def execute(self, request: dict[str, Any], cancel_event: threading.Event) -> dict[str, Any]: ...


class AttemptActionPort(Protocol):
    def execute_coarse_batch(self, stage_id: str, advances: int, cancel_event: threading.Event) -> StageResult: ...

    def execute_precise_advances(self, stage_id: str, advances: int, cancel_event: threading.Event) -> StageResult: ...

    def trigger_relocation_bit(self, index: int, cancel_event: threading.Event) -> None: ...

    def execute_final_trigger(self, plan: Mapping[str, Any], cancel_event: threading.Event) -> StageResult: ...

    def stop_scripts(self) -> None: ...


@dataclass(frozen=True, slots=True)
class AttemptStageScripts:
    coarse_batch: Callable[[int], str]
    precise_advances: Callable[[int], str]
    relocation_bit: Callable[[int], str]
    final_trigger: Callable[[Mapping[str, Any]], str]

    def __post_init__(self):
        if any(not callable(getattr(self, name)) for name in (
            "coarse_batch", "precise_advances", "relocation_bit", "final_trigger",
        )):
            raise AttemptExecutionError("all scenario stage script builders must be callable")


class EcsAttemptActionPort:
    """Adapt validated stage parameters to the existing lease-owning ECS executor."""

    def __init__(self, stage_executor, scripts: AttemptStageScripts):
        self.stage_executor = stage_executor
        self.scripts = scripts
        self._relocation_sequence = 0

    def execute_coarse_batch(self, stage_id, advances, cancel_event):
        return self.stage_executor.execute(stage_id, self.scripts.coarse_batch(advances))

    def execute_precise_advances(self, stage_id, advances, cancel_event):
        return self.stage_executor.execute(stage_id, self.scripts.precise_advances(advances))

    def trigger_relocation_bit(self, index, cancel_event):
        if cancel_event.is_set():
            raise AttemptExecutionNeedsAttention("CANCELLED", "relocation observation was cancelled")
        stage_id = f"relocation-{self._relocation_sequence:04d}-bit-{index:04d}"
        self._relocation_sequence += 1
        result = self.stage_executor.execute(stage_id, self.scripts.relocation_bit(index))
        if result.outcome != StageOutcome.COMPLETED:
            raise AttemptExecutionNeedsAttention(
                f"RELOCATION_ACTION_{result.outcome.value.upper()}",
                result.message or "relocation animation action did not complete",
            )

    def execute_final_trigger(self, plan, cancel_event):
        return self.stage_executor.execute("final-trigger", self.scripts.final_trigger(plan))

    def stop_scripts(self):
        self.stage_executor.cancel()


class SeedRelocator:
    """Observe fresh animation bits and accept only one complete CLI location."""

    def __init__(self, *, calculator: AttemptCalculatorPort, observer,
                 trigger_bit: Callable[[int, threading.Event], None], run_id: str,
                 epoch_id: str, context_revision: int, cancel_event: threading.Event):
        self.calculator = calculator
        self.observer = observer
        self.trigger_bit = trigger_bit
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.cancel_event = cancel_event

    def locate(self, *, anchor_seed0: str, anchor_seed1: str,
               expected_first_observed_advance: int, minimum_first_observed_advance: int,
               maximum_position_error: int, observation_bits: int,
               source_kind: str) -> RelocationResult:
        expected = _advance(expected_first_observed_advance, "expected_first_observed_advance")
        minimum = _advance(minimum_first_observed_advance, "minimum_first_observed_advance")
        if minimum > expected:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_POSITION_BOUNDS_INVALID", "relocation lower bound exceeds its estimate",
            )
        seed0 = _seed(anchor_seed0, "anchor_seed0")
        seed1 = _seed(anchor_seed1, "anchor_seed1")
        window_start = max(minimum, expected - maximum_position_error)
        window_end = min(MAX_ADVANCE - observation_bits, expected + maximum_position_error)
        if window_end < window_start:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_WINDOW_EMPTY", "the relocation window does not fit the supported position range",
            )

        sequence = self.observer.observe_sequence(
            observation_bits,
            lambda index: self.trigger_bit(index, self.cancel_event),
            cancel_event=self.cancel_event,
        )
        if not isinstance(sequence, SeedSequenceObservation) or not sequence.complete:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_OBSERVATION_INCOMPLETE", "animation sequence was incomplete or ambiguous",
            )
        if (len(sequence.observations) != observation_bits
                or len(sequence.samples) != observation_bits
                or any(bit not in "01" for bit in sequence.observations)
                or any(sample.bit not in (0, 1) for sample in sequence.samples)
                or tuple(str(sample.bit) for sample in sequence.samples) != tuple(sequence.observations)):
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_OBSERVATION_INVALID", "animation observer returned an invalid bit sequence",
            )
        frame_ids = tuple(frame for sample in sequence.samples for frame in sample.frame_ids)
        if (not frame_ids or any(not sample.frame_ids for sample in sequence.samples)
                or len(frame_ids) != len(set(frame_ids))):
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_FRAME_EVIDENCE_INVALID", "relocation bits must use distinct captured frames",
            )
        evidence_ids = tuple(dict.fromkeys(
            evidence_id for sample in sequence.samples for evidence_id in sample.evidence_ids
        ))

        request = {
            "operation": "seed.locate",
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": self.run_id,
            "epochId": self.epoch_id,
            "contextRevision": self.context_revision,
            "seed0": seed0,
            "seed1": seed1,
            "start": window_start,
            "end": window_end,
            "observations": sequence.observations,
            "sourceKind": source_kind,
            "evidenceIds": list(evidence_ids),
        }
        if self.cancel_event.is_set():
            raise AttemptExecutionNeedsAttention("CANCELLED", "relocation was cancelled")
        response = self.calculator.execute(request, self.cancel_event)
        validate_event(response, request)
        if response.get("type") != "result" or not isinstance(response.get("data"), Mapping):
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_CALCULATOR_FAILED",
                str(response.get("message", "seed.locate did not return a result")),
            )
        data = response["data"]
        candidates = data.get("candidates")
        candidate_count = _integer(data.get("candidateCount"), "candidateCount")
        if (data.get("complete") is not True or data.get("windowStart") != window_start
                or data.get("windowEnd") != window_end or not isinstance(candidates, list)
                or len(candidates) != candidate_count):
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_RESPONSE_INCOMPLETE", "seed.locate response did not cover the requested stable window",
            )
        if candidate_count == 0:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_NO_CANDIDATE", "no RNG position matches the observed animation sequence",
            )
        if candidate_count != 1:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_AMBIGUOUS", "multiple RNG positions match; do not choose the nearest candidate",
            )
        candidate = candidates[0]
        if not isinstance(candidate, Mapping):
            raise AttemptExecutionNeedsAttention("RELOCATION_CANDIDATE_INVALID", "seed.locate candidate is invalid")
        first = _integer(candidate.get("firstObservedAdvance"), "firstObservedAdvance")
        current = first + observation_bits
        if first < window_start or first > window_end or current > MAX_ADVANCE:
            raise AttemptExecutionNeedsAttention("RELOCATION_CANDIDATE_OUT_OF_WINDOW", "located RNG position is outside its window")
        before = _state(candidate.get("stateBeforeObservations"), "stateBeforeObservations")
        after = _state(candidate.get("stateAfterObservations"), "stateAfterObservations")
        after_duplicate = _state(candidate.get("stateAfterObservedAdvance"), "stateAfterObservedAdvance")
        if after != after_duplicate:
            raise AttemptExecutionNeedsAttention(
                "RELOCATION_STATE_BOUNDARY_MISMATCH", "seed.locate returned inconsistent post-observation states",
            )
        return RelocationResult(
            first, current, window_start, window_end, observation_bits, sequence.observations,
            MappingProxyType(before), MappingProxyType(after), frame_ids, evidence_ids,
        )


class AttemptExecutionCoordinator:
    """Execute one planned attempt with a fresh position solve after every coarse batch."""

    def __init__(self, *, calculator: AttemptCalculatorPort, observer, actions: AttemptActionPort,
                 attempt_id: str, run_id: str, epoch_id: str, context_revision: int = 0,
                 cancel_event: threading.Event | None = None):
        if not isinstance(attempt_id, str) or not attempt_id or len(attempt_id) > 128:
            raise AttemptExecutionError("attempt_id must be a non-empty string of at most 128 characters")
        if not isinstance(run_id, str) or not run_id or len(run_id) > 128:
            raise AttemptExecutionError("run_id must be a non-empty string of at most 128 characters")
        if not isinstance(epoch_id, str) or not epoch_id or len(epoch_id) > 128:
            raise AttemptExecutionError("epoch_id must be a non-empty string of at most 128 characters")
        if isinstance(context_revision, bool) or not isinstance(context_revision, int) or context_revision < 0:
            raise AttemptExecutionError("context_revision must be a non-negative integer")
        self.calculator = calculator
        self.observer = observer
        self.actions = actions
        self.attempt_id = attempt_id
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.cancel_event = cancel_event or threading.Event()
        self._run_lock = threading.Lock()
        self._has_run = False

    def cancel(self):
        self.cancel_event.set()
        self.actions.stop_scripts()

    def run(self, *, anchor_seed0: str, anchor_seed1: str, current_advance: int,
            replan: Callable[[int, int, int], AttemptPlanResult],
            settings: AttemptExecutionSettings = AttemptExecutionSettings()) -> AttemptExecutionResult:
        if not isinstance(settings, AttemptExecutionSettings):
            raise AttemptExecutionError("settings must be an AttemptExecutionSettings value")
        if not callable(replan):
            raise AttemptExecutionError("replan must be callable")
        current = _advance(current_advance, "current_advance")
        seed0, seed1 = _seed(anchor_seed0, "anchor_seed0"), _seed(anchor_seed1, "anchor_seed1")
        if self._has_run or not self._run_lock.acquire(blocking=False):
            raise RuntimeError("an attempt execution coordinator can run only once")
        self._has_run = True
        events: list[Mapping[str, Any]] = []
        snapshot_id = None
        request_digest = None
        last_plan = None
        reason_code = None
        reason = None
        target_id = None
        target_generation = None
        trigger_advance = None
        if settings.execution_mode == "formal":
            result = self._result(
                AttemptExecutionStatus.NEEDS_ATTENTION, current, None, None, None,
                None, None, "FORMAL_START_BLOCKED", "offline attempt execution cannot start formal automation", (),
            )
            self._run_lock.release()
            return result

        relocator = SeedRelocator(
            calculator=self.calculator,
            observer=self.observer,
            trigger_bit=self.actions.trigger_relocation_bit,
            run_id=self.run_id,
            epoch_id=self.epoch_id,
            context_revision=self.context_revision,
            cancel_event=self.cancel_event,
        )
        try:
            for cycle in range(settings.maximum_replans):
                self._check_cancel()
                immediate = self._plan(
                    replan, current, 0, settings, snapshot_id, request_digest,
                )
                snapshot_id = immediate.target_snapshot_id
                request_digest = immediate.target_request_digest
                immediate_plan = self._choose_plan(immediate)
                target_id = immediate_plan["targetCandidateId"]
                target_generation = _integer(immediate_plan["targetGenerationAdvance"], "targetGenerationAdvance")
                trigger_advance = _integer(immediate_plan["triggerAdvance"], "triggerAdvance")
                last_plan = immediate_plan
                self._record(events, "plan_selected", {
                    "cycle": cycle,
                    "targetCandidateId": target_id,
                    "targetGenerationAdvance": target_generation,
                    "triggerAdvance": trigger_advance,
                    "targetSnapshotId": snapshot_id,
                })
                if _integer(immediate_plan["coarseAdvances"], "coarseAdvances") == 0:
                    precise = trigger_advance - current
                    if precise < 0 or precise > settings.precise_reserve:
                        raise AttemptExecutionNeedsAttention(
                            "PRECISE_RESERVE_INVALID", "remaining precise advances exceed the validated reserve",
                        )
                    if precise:
                        stage_id = f"precise-{cycle:04d}"
                        stage = self.actions.execute_precise_advances(stage_id, precise, self.cancel_event)
                        self._check_stage(stage, stage_id)
                    self._record(events, "precise_stage_completed", {
                        "stageId": stage_id, "advances": precise,
                        "startFrameId": stage.start_frame_id, "endFrameId": stage.end_frame_id,
                        "startedAtNs": stage.started_at_ns, "finishedAtNs": stage.finished_at_ns,
                    })
                    final_stage = self.actions.execute_final_trigger(immediate_plan, self.cancel_event)
                    self._check_stage(final_stage, "final-trigger")
                    self._record(events, "final_trigger_executed", {
                        "stageId": final_stage.stage_id,
                        "startFrameId": final_stage.start_frame_id, "endFrameId": final_stage.end_frame_id,
                        "targetGenerationAdvance": target_generation,
                        "startedAtNs": final_stage.started_at_ns,
                        "finishedAtNs": final_stage.finished_at_ns,
                    })
                    return self._result(
                        AttemptExecutionStatus.TRIGGER_EXECUTED, trigger_advance,
                        target_id, target_generation, trigger_advance, snapshot_id,
                        request_digest, None, None, tuple(events),
                    )

                protected = self._plan(
                    replan, current, settings.relocation_observation_bits,
                    settings, snapshot_id, request_digest,
                )
                protected_plan = self._choose_plan(protected)
                target_id = protected_plan["targetCandidateId"]
                target_generation = _integer(protected_plan["targetGenerationAdvance"], "targetGenerationAdvance")
                trigger_advance = _integer(protected_plan["triggerAdvance"], "triggerAdvance")
                last_plan = protected_plan
                batches = protected_plan.get("coarseBatches")
                if not isinstance(batches, list):
                    raise AttemptExecutionNeedsAttention("COARSE_BATCHES_INVALID", "planner omitted coarse batches")

                if _integer(protected_plan["coarseAdvances"], "coarseAdvances") == 0:
                    expected_start = current
                    minimum_start = current
                    stage_id = None
                else:
                    batch = batches[0] if batches else None
                    if not isinstance(batch, Mapping) or batch.get("startAdvance") != current:
                        raise AttemptExecutionNeedsAttention(
                            "COARSE_BATCH_INVALID", "first coarse batch is not anchored at the confirmed state",
                        )
                    amount = _integer(batch.get("requestedAdvances"), "requestedAdvances")
                    planned_end = _integer(batch.get("plannedEndAdvance"), "plannedEndAdvance")
                    if amount < 1 or planned_end != current + amount:
                        raise AttemptExecutionNeedsAttention("COARSE_BATCH_INVALID", "coarse batch bounds are invalid")
                    stage_id = f"coarse-{cycle:04d}"
                    stage = self.actions.execute_coarse_batch(stage_id, amount, self.cancel_event)
                    self._check_stage(stage, stage_id)
                    self._record(events, "coarse_batch_completed", {
                        "stageId": stage_id, "startAdvance": current,
                        "requestedAdvances": amount, "plannedEndAdvance": planned_end,
                        "startFrameId": stage.start_frame_id, "endFrameId": stage.end_frame_id,
                        "startedAtNs": stage.started_at_ns, "finishedAtNs": stage.finished_at_ns,
                    })
                    expected_start = planned_end
                    minimum_start = current

                located = relocator.locate(
                    anchor_seed0=seed0,
                    anchor_seed1=seed1,
                    expected_first_observed_advance=expected_start,
                    minimum_first_observed_advance=minimum_start,
                    maximum_position_error=settings.maximum_position_error,
                    observation_bits=settings.relocation_observation_bits,
                    source_kind=settings.source_kind,
                )
                if located.current_advance <= current:
                    raise AttemptExecutionNeedsAttention(
                        "RELOCATION_POSITION_NOT_FORWARD", "relocation did not advance beyond the last confirmed state",
                    )
                current = located.current_advance
                self._record(events, "position_relocated", {
                    "expectedFirstObservedAdvance": expected_start,
                    "firstObservedAdvance": located.first_observed_advance,
                    "currentAdvance": current,
                    "windowStart": located.window_start,
                    "windowEnd": located.window_end,
                    "observationLength": located.observation_length,
                    "observations": located.observations,
                    "stateBeforeObservations": dict(located.state_before_observations),
                    "stateAfterObservations": dict(located.state_after_observations),
                    "frameIds": list(located.frame_ids),
                    "evidenceIds": list(located.evidence_ids),
                })
            raise AttemptExecutionNeedsAttention(
                "REPLAN_LIMIT_REACHED", "attempt position did not converge within the configured replan limit",
            )
        except AttemptExecutionNeedsAttention as exc:
            reason_code, reason = exc.reason_code, str(exc)
            status = AttemptExecutionStatus.CANCELLED if reason_code == "CANCELLED" else AttemptExecutionStatus.NEEDS_ATTENTION
            try:
                self.actions.stop_scripts()
            except Exception:
                pass
        except Exception as exc:
            reason_code, reason = "ATTEMPT_EXECUTION_FAILED", str(exc)
            status = AttemptExecutionStatus.CANCELLED if self.cancel_event.is_set() else AttemptExecutionStatus.NEEDS_ATTENTION
            try:
                self.actions.stop_scripts()
            except Exception:
                pass
        finally:
            self._run_lock.release()
        return self._result(
            status, current, target_id, target_generation, trigger_advance,
            snapshot_id, request_digest, reason_code, reason, tuple(events),
        )

    def _plan(self, replan, current, relocation_reserve, settings, snapshot_id, request_digest):
        self._check_cancel()
        result = replan(current, relocation_reserve, settings.precise_reserve)
        if not isinstance(result, AttemptPlanResult) or result.current_advance != current:
            raise AttemptExecutionNeedsAttention(
                "PLAN_POSITION_MISMATCH", "replanner returned a plan for a different confirmed position",
            )
        if result.hardware_status != "PendingHardwareValidation":
            raise AttemptExecutionNeedsAttention(
                "PLAN_HARDWARE_STATUS_INVALID", "offline planning cannot authorize hardware validation",
            )
        if snapshot_id is not None and (
            result.target_snapshot_id != snapshot_id or result.target_request_digest != request_digest
        ):
            raise AttemptExecutionNeedsAttention(
                "STALE_TARGET_SNAPSHOT", "target search snapshot changed during attempt execution",
            )
        if result.status != "Feasible" or not result.plans:
            raise AttemptExecutionNeedsAttention(
                f"PLAN_{result.status.upper()}", result.reason or "no target remains safely reachable",
            )
        for plan in result.plans:
            self._validate_plan(
                plan, current, relocation_reserve, settings.precise_reserve,
                settings.maximum_coarse_batch_size,
            )
        return result

    @staticmethod
    def _validate_plan(plan, current, relocation_reserve, precise_reserve,
                       maximum_coarse_batch_size=100_000):
        if not isinstance(plan, Mapping):
            raise AttemptExecutionNeedsAttention("PLAN_ROW_INVALID", "attempt plan row is invalid")
        trigger = _integer(plan.get("triggerAdvance"), "triggerAdvance")
        target = _integer(plan.get("targetGenerationAdvance"), "targetGenerationAdvance")
        modeled = _integer(plan.get("modeledPreTriggerAdvances"), "modeledPreTriggerAdvances")
        boundary = plan.get("boundaryOffset")
        if isinstance(boundary, bool) or not isinstance(boundary, int):
            raise AttemptExecutionNeedsAttention("PLAN_BOUNDARY_INVALID", "plan boundary offset is invalid")
        if trigger < current or trigger + modeled + boundary != target:
            raise AttemptExecutionNeedsAttention("PLAN_EQUATION_INVALID", "plan violates the target generation boundary equation")
        if (plan.get("relocationReserve") != relocation_reserve
                or plan.get("preciseReserve") != precise_reserve):
            raise AttemptExecutionNeedsAttention("PLAN_RESERVE_MISMATCH", "planner changed the requested checkpoint reserves")
        coarse_target = _integer(plan.get("coarseTargetAdvance"), "coarseTargetAdvance")
        coarse_advances = _integer(plan.get("coarseAdvances"), "coarseAdvances")
        if (coarse_target < current or coarse_advances != coarse_target - current
                or trigger - coarse_target != relocation_reserve + precise_reserve):
            raise AttemptExecutionNeedsAttention("PLAN_COARSE_BUDGET_INVALID", "planner coarse and checkpoint budgets disagree")
        batches = plan.get("coarseBatches")
        if not isinstance(batches, list):
            raise AttemptExecutionNeedsAttention("COARSE_BATCHES_INVALID", "planner omitted coarse batch details")
        cursor = current
        for batch in batches:
            if not isinstance(batch, Mapping) or batch.get("startAdvance") != cursor:
                raise AttemptExecutionNeedsAttention("COARSE_BATCH_INVALID", "coarse batches are not contiguous")
            count = _integer(batch.get("requestedAdvances"), "requestedAdvances")
            if (count < 1 or count > maximum_coarse_batch_size
                    or batch.get("plannedEndAdvance") != cursor + count):
                raise AttemptExecutionNeedsAttention("COARSE_BATCH_INVALID", "coarse batch end does not match its requested count")
            if batch.get("requiresRelocationBeforeNextBatch") is not True:
                raise AttemptExecutionNeedsAttention("COARSE_CHECKPOINT_REQUIRED", "each coarse batch requires a relocation checkpoint")
            cursor += count
        if cursor != coarse_target or (coarse_advances == 0) != (len(batches) == 0):
            raise AttemptExecutionNeedsAttention("COARSE_BATCH_TOTAL_INVALID", "coarse batches do not match the planned total")

    @staticmethod
    def _choose_plan(result):
        return min(
            result.plans,
            key=lambda plan: (
                _integer(plan.get("triggerAdvance"), "triggerAdvance"),
                _integer(plan.get("targetGenerationAdvance"), "targetGenerationAdvance"),
                str(plan.get("targetCandidateId", "")),
            ),
        )

    def _check_stage(self, stage, expected_stage_id):
        if not isinstance(stage, StageResult):
            raise AttemptExecutionNeedsAttention("ACTION_RESULT_INVALID", "action port returned an invalid stage result")
        if (stage.run_id != self.run_id or stage.epoch_id != self.epoch_id
                or stage.context_revision != self.context_revision or stage.stage_id != expected_stage_id):
            raise AttemptExecutionNeedsAttention("ACTION_RESULT_IDENTITY_MISMATCH", "action stage identity did not match the active attempt")
        if stage.outcome != StageOutcome.COMPLETED:
            code = "CANCELLED" if stage.outcome == StageOutcome.CANCELLED else f"ACTION_STAGE_{stage.outcome.value.upper()}"
            raise AttemptExecutionNeedsAttention(code, stage.message or f"action stage {expected_stage_id} did not complete")

    def _check_cancel(self):
        if self.cancel_event.is_set():
            raise AttemptExecutionNeedsAttention("CANCELLED", "attempt execution was cancelled")

    def _record(self, events, kind, details):
        events.append(MappingProxyType({
            "attemptId": self.attempt_id,
            "sequence": len(events) + 1,
            "event": kind,
            "details": json.loads(json.dumps(details, ensure_ascii=False, allow_nan=False)),
        }))

    def _result(self, status, current, target_id, target_generation, trigger, snapshot_id,
                request_digest, reason_code, reason, events):
        return AttemptExecutionResult(
            status, self.attempt_id, self.run_id, self.epoch_id, self.context_revision, current,
            target_id, target_generation, trigger, snapshot_id, request_digest,
            "PendingHardwareValidation", reason_code, reason, events,
        )


def _integer(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > MAX_ADVANCE:
        raise AttemptExecutionNeedsAttention("RESPONSE_INTEGER_INVALID", f"{name} must be a supported non-negative integer")
    return value


def _advance(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > MAX_ADVANCE:
        raise AttemptExecutionError(f"{name} must be between 0 and {MAX_ADVANCE}")
    return value


def _seed(value, name):
    if not isinstance(value, str) or len(value) != 16 or any(char not in "0123456789abcdefABCDEF" for char in value):
        raise AttemptExecutionError(f"{name} must be exactly 16 hexadecimal characters")
    return value.lower()


def _state(value, name):
    if not isinstance(value, Mapping):
        raise AttemptExecutionNeedsAttention("RELOCATION_STATE_INVALID", f"{name} must be an RNG state object")
    return {"seed0": _seed(value.get("seed0"), f"{name}.seed0"),
            "seed1": _seed(value.get("seed1"), f"{name}.seed1")}
