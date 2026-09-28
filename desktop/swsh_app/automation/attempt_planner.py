"""Protocol client and strict result checks for bounded M4 attempt planning."""
from __future__ import annotations

from dataclasses import dataclass
import re
import threading
from types import MappingProxyType
from typing import Any, Mapping, Protocol, Sequence
import uuid

from ..backend import PROTOCOL_VERSION, validate_event
from .boundary import BoundaryAdjustment
from .npc_calibration import NpcCalibrationResult, ProbeCandidate


MAX_ADVANCE = 1_000_000_000
MAX_PLAN_EVALUATIONS = 100_000
MAX_TARGETS = 512
MAX_COARSE_BATCHES = 1_000
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class AttemptPlanningError(ValueError):
    pass


class AttemptPlanningNeedsAttention(RuntimeError):
    pass


class PlanningCalculatorPort(Protocol):
    def execute(self, request: dict[str, Any], cancel_event: threading.Event) -> dict[str, Any]: ...


@dataclass(frozen=True, slots=True)
class PlanningTarget:
    candidate_id: str
    generation_advance: int

    def __post_init__(self):
        if not isinstance(self.candidate_id, str) or not self.candidate_id.strip():
            raise AttemptPlanningError("candidate_id is required")
        if len(self.candidate_id) > 128:
            raise AttemptPlanningError("candidate_id cannot exceed 128 characters")
        _advance(self.generation_advance, "generation_advance")

    def to_protocol(self) -> dict[str, Any]:
        return {"candidateId": self.candidate_id, "generationAdvance": self.generation_advance}


@dataclass(frozen=True, slots=True)
class AttemptPlanningSettings:
    maximum_plan_evaluations: int = MAX_PLAN_EVALUATIONS
    coarse_batch_size: int = 5_000
    maximum_coarse_batches: int = MAX_COARSE_BATCHES
    relocation_reserve: int = 0
    precise_reserve: int = 0

    def __post_init__(self):
        if (isinstance(self.maximum_plan_evaluations, bool)
                or not isinstance(self.maximum_plan_evaluations, int)
                or not 1 <= self.maximum_plan_evaluations <= MAX_PLAN_EVALUATIONS):
            raise AttemptPlanningError("maximum_plan_evaluations must be between 1 and 100,000")
        if (isinstance(self.coarse_batch_size, bool) or not isinstance(self.coarse_batch_size, int)
                or not 1 <= self.coarse_batch_size <= 100_000):
            raise AttemptPlanningError("coarse_batch_size must be between 1 and 100,000")
        if (isinstance(self.maximum_coarse_batches, bool)
                or not isinstance(self.maximum_coarse_batches, int)
                or not 1 <= self.maximum_coarse_batches <= MAX_COARSE_BATCHES):
            raise AttemptPlanningError("maximum_coarse_batches must be between 1 and 1,000")
        for name, value in (("relocation_reserve", self.relocation_reserve),
                            ("precise_reserve", self.precise_reserve)):
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 100_000:
                raise AttemptPlanningError(f"{name} must be between 0 and 100,000")


@dataclass(frozen=True, slots=True)
class AttemptPlanResult:
    status: str
    current_advance: int
    target_snapshot_id: str
    target_request_digest: str
    plans: tuple[Mapping[str, Any], ...]
    target_statuses: tuple[Mapping[str, Any], ...]
    evaluation_count: int
    under_budget_position_count: int
    hardware_status: str
    reason: str | None = None

    def as_dict(self) -> dict[str, Any]:
        return {
            "reportSchema": "auto-swsh-attempt-plan",
            "reportVersion": 1,
            "status": self.status,
            "currentAdvance": self.current_advance,
            "targetSnapshotId": self.target_snapshot_id,
            "targetRequestDigest": self.target_request_digest,
            "plans": [_thaw(plan) for plan in self.plans],
            "targetStatuses": [_thaw(item) for item in self.target_statuses],
            "evaluationCount": self.evaluation_count,
            "underBudgetPositionCount": self.under_budget_position_count,
            "hardwareStatus": self.hardware_status,
            "canStartFormalAutomation": False,
            "reason": self.reason,
        }


class AttemptPlanner:
    """Ask the C# model to enumerate p against the exact stateful action chain."""

    def __init__(self, *, calculator: PlanningCalculatorPort, run_id: str, epoch_id: str,
                 context_revision: int, cancel_event: threading.Event | None = None):
        if not isinstance(run_id, str) or not run_id or len(run_id) > 128:
            raise AttemptPlanningError("run_id must be a non-empty string of at most 128 characters")
        if not isinstance(epoch_id, str) or not epoch_id or len(epoch_id) > 128:
            raise AttemptPlanningError("epoch_id must be a non-empty string of at most 128 characters")
        if (isinstance(context_revision, bool) or not isinstance(context_revision, int)
                or context_revision < 0):
            raise AttemptPlanningError("context_revision must be a non-negative integer")
        self.calculator = calculator
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.cancel_event = cancel_event or threading.Event()

    def plan(self, *, anchor_seed0: str, anchor_seed1: str, current_advance: int,
             targets: Sequence[PlanningTarget], target_snapshot_id: str,
             target_request_digest: str, boundary: BoundaryAdjustment,
             probe_actions: Sequence[Mapping[str, Any]], calibration: NpcCalibrationResult,
             settings: AttemptPlanningSettings = AttemptPlanningSettings()) -> AttemptPlanResult:
        seed0 = _seed(anchor_seed0, "anchor_seed0")
        seed1 = _seed(anchor_seed1, "anchor_seed1")
        current = _advance(current_advance, "current_advance")
        if current > MAX_ADVANCE:
            raise AttemptPlanningError("current_advance cannot exceed 1,000,000,000")
        if not isinstance(targets, Sequence) or isinstance(targets, (str, bytes)) or not 1 <= len(targets) <= MAX_TARGETS:
            raise AttemptPlanningError("targets must contain 1 to 512 search candidates")
        targets = tuple(targets)
        if any(not isinstance(target, PlanningTarget) for target in targets):
            raise AttemptPlanningError("targets must contain PlanningTarget values")
        if len({target.candidate_id for target in targets}) != len(targets):
            raise AttemptPlanningError("target candidate IDs must be unique")
        if not isinstance(target_snapshot_id, str) or not target_snapshot_id or len(target_snapshot_id) > 128:
            raise AttemptPlanningError("target_snapshot_id must identify the immutable search snapshot")
        if not isinstance(target_request_digest, str) or not SHA256_RE.fullmatch(target_request_digest):
            raise AttemptPlanningError("target_request_digest must be a lowercase SHA-256 digest")
        if not isinstance(boundary, BoundaryAdjustment):
            raise AttemptPlanningError("boundary must be a BoundaryAdjustment")
        if not -100_000 <= boundary.offset <= 100_000:
            raise AttemptPlanningError("boundary offset must be between -100,000 and 100,000")
        if (not isinstance(calibration, NpcCalibrationResult)
                or calibration.status != "CalibratedOffline"
                or calibration.hardware_status != "PendingHardwareValidation"
                or calibration.candidate is None
                or len(calibration.candidate_ids) != 1):
            raise AttemptPlanningError("planning requires one offline-calibrated NPC candidate")
        candidate_data = dict(calibration.candidate)
        candidate_id = candidate_data.get("candidateId")
        if candidate_id != calibration.candidate_ids[0]:
            raise AttemptPlanningError("NPC calibration result candidate identity is inconsistent")
        calibrated_candidate = ProbeCandidate(candidate_id, {
            name: value for name, value in candidate_data.items() if name != "candidateId"
        })
        if not isinstance(probe_actions, Sequence) or isinstance(probe_actions, (str, bytes)) or not 1 <= len(probe_actions) <= 8:
            raise AttemptPlanningError("probe_actions must contain 1 to 8 modelled actions")
        actions = []
        for action in probe_actions:
            if not isinstance(action, Mapping) or not isinstance(action.get("kind"), str):
                raise AttemptPlanningError("each probe action must include kind")
            actions.append(_json_object(action, "probe action"))

        request = {
            "operation": "attempt.plan",
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": self.run_id,
            "epochId": self.epoch_id,
            "contextRevision": self.context_revision,
            "seed0": seed0,
            "seed1": seed1,
            "currentAdvance": current,
            "targetCandidates": [target.to_protocol() for target in targets],
            "targetSnapshotId": target_snapshot_id,
            "targetRequestDigest": target_request_digest,
            "boundaryAdjustment": {
                "boundaryId": boundary.boundary_id,
                "offset": boundary.offset,
                "modelVersion": boundary.model_version,
                "revision": boundary.revision,
            },
            "attemptPlanning": {
                "maximumPlanEvaluations": settings.maximum_plan_evaluations,
                "coarseBatchSize": settings.coarse_batch_size,
                "maximumCoarseBatches": settings.maximum_coarse_batches,
                "relocationReserve": settings.relocation_reserve,
                "preciseReserve": settings.precise_reserve,
            },
            "probeActions": actions,
            "probeCandidates": [calibrated_candidate.to_protocol()],
        }
        if self.cancel_event.is_set():
            raise AttemptPlanningNeedsAttention("attempt planning cancelled")
        try:
            response = self.calculator.execute(request, self.cancel_event)
            validate_event(response, request)
        except Exception as exc:
            if self.cancel_event.is_set():
                raise AttemptPlanningNeedsAttention("attempt planning cancelled") from exc
            raise AttemptPlanningNeedsAttention(f"attempt planning request failed: {exc}") from exc
        if response.get("type") != "result" or not isinstance(response.get("data"), Mapping):
            raise AttemptPlanningNeedsAttention(str(response.get("message", "invalid attempt.plan response")))
        return _validate_result(
            response["data"], current_advance=current, targets=targets,
            target_snapshot_id=target_snapshot_id, target_request_digest=target_request_digest,
            boundary=boundary, candidate=calibrated_candidate, settings=settings,
        )


def _validate_result(data, *, current_advance, targets, target_snapshot_id,
                     target_request_digest, boundary, candidate, settings):
    required_statuses = {
        "Feasible", "InsufficientExecutionBudget", "NoFeasibleTriggerPosition", "TargetAlreadyPassed",
    }
    if data.get("status") not in required_statuses:
        raise AttemptPlanningNeedsAttention("attempt planner returned an unsupported status")
    if data.get("reportSchema") != "auto-swsh-attempt-plan" or data.get("reportVersion") != 1:
        raise AttemptPlanningNeedsAttention("attempt planner returned an unsupported report schema")
    if data.get("targetSnapshotId") != target_snapshot_id or data.get("targetRequestDigest") != target_request_digest:
        raise AttemptPlanningNeedsAttention("attempt planner response belongs to a stale candidate snapshot")
    if data.get("currentAdvance") != current_advance:
        raise AttemptPlanningNeedsAttention("attempt planner changed the confirmed current position")
    if data.get("hardwareStatus") != "PendingHardwareValidation" or data.get("canStartFormalAutomation") is not False:
        raise AttemptPlanningNeedsAttention("offline attempt planning cannot authorize formal automation")
    if data.get("probeCandidateId") != candidate.candidate_id:
        raise AttemptPlanningNeedsAttention("attempt planner used a different NPC calibration candidate")
    boundary_result = data.get("boundary")
    if not isinstance(boundary_result, Mapping) or any((
        boundary_result.get("boundaryId") != boundary.boundary_id,
        boundary_result.get("offset") != boundary.offset,
        boundary_result.get("modelVersion") != boundary.model_version,
        boundary_result.get("revision") != boundary.revision,
    )):
        raise AttemptPlanningNeedsAttention("attempt planner changed the boundary adjustment")
    evaluation_count = _integer(data.get("evaluationCount"), "evaluationCount")
    if evaluation_count > settings.maximum_plan_evaluations:
        raise AttemptPlanningNeedsAttention("attempt planner exceeded the requested evaluation bound")
    under_budget = _integer(data.get("underBudgetPositionCount"), "underBudgetPositionCount")
    target_ids = {target.candidate_id for target in targets}
    target_statuses = data.get("targetStatuses")
    if not isinstance(target_statuses, list) or any(
        not isinstance(item, Mapping) or item.get("candidateId") not in target_ids
        for item in target_statuses
    ):
        raise AttemptPlanningNeedsAttention("attempt planner returned invalid target status rows")
    if any(item.get("status") not in {
        "Feasible", "InsufficientExecutionBudget", "NoFeasibleTriggerPosition",
        "TargetAlreadyPassed", "NoTriggerRange",
    } for item in target_statuses):
        raise AttemptPlanningNeedsAttention("attempt planner returned an unsupported target status")
    if {item["candidateId"] for item in target_statuses} != target_ids:
        raise AttemptPlanningNeedsAttention("attempt planner omitted one or more target candidates")
    raw_plans = data.get("plans")
    if not isinstance(raw_plans, list):
        raise AttemptPlanningNeedsAttention("attempt planner omitted its plan list")
    target_by_id = {target.candidate_id: target for target in targets}
    plans = []
    for row in raw_plans:
        if not isinstance(row, Mapping):
            raise AttemptPlanningNeedsAttention("attempt planner returned an invalid plan")
        target_id = row.get("targetCandidateId")
        target = target_by_id.get(target_id)
        if target is None:
            raise AttemptPlanningNeedsAttention("attempt planner referenced a stale target candidate")
        trigger = _integer(row.get("triggerAdvance"), "triggerAdvance")
        model_advance = _integer(row.get("modeledPreTriggerAdvances"), "modeledPreTriggerAdvances")
        predicted = _integer(row.get("predictedGenerationAdvance"), "predictedGenerationAdvance")
        if trigger < current_advance or trigger + model_advance + boundary.offset != target.generation_advance:
            raise AttemptPlanningNeedsAttention("attempt plan violates p + M(S(p)) + b = g")
        if predicted != target.generation_advance:
            raise AttemptPlanningNeedsAttention("attempt plan target generation position changed")
        if row.get("probeCandidateId") != candidate.candidate_id:
            raise AttemptPlanningNeedsAttention("attempt plan used an unvalidated NPC candidate")
        if any((
            row.get("boundaryId") != boundary.boundary_id,
            row.get("boundaryOffset") != boundary.offset,
            row.get("boundaryModelVersion") != boundary.model_version,
            row.get("boundaryRevision") != boundary.revision,
        )):
            raise AttemptPlanningNeedsAttention("attempt plan boundary fingerprint changed")
        reserve = settings.relocation_reserve + settings.precise_reserve
        coarse_target = _integer(row.get("coarseTargetAdvance"), "coarseTargetAdvance")
        coarse_count = _integer(row.get("coarseAdvances"), "coarseAdvances")
        if (coarse_target < current_advance or trigger - coarse_target != reserve
                or coarse_count != coarse_target - current_advance):
            raise AttemptPlanningNeedsAttention("attempt plan does not preserve relocation and precise reserves")
        if row.get("checkpointRequiredAfterEachBatch") is not True:
            raise AttemptPlanningNeedsAttention("coarse batches must require a relocation checkpoint")
        batches = row.get("coarseBatches")
        if not isinstance(batches, list) or len(batches) > settings.maximum_coarse_batches:
            raise AttemptPlanningNeedsAttention("attempt plan has an invalid number of coarse batches")
        cursor = current_advance
        for batch in batches:
            if not isinstance(batch, Mapping) or batch.get("startAdvance") != cursor:
                raise AttemptPlanningNeedsAttention("coarse batches are not contiguous")
            amount = _integer(batch.get("requestedAdvances"), "requestedAdvances")
            if amount < 1 or amount > settings.coarse_batch_size:
                raise AttemptPlanningNeedsAttention("coarse batch exceeds the configured batch size")
            cursor += amount
            if (batch.get("plannedEndAdvance") != cursor
                    or batch.get("requiresRelocationBeforeNextBatch") is not True):
                raise AttemptPlanningNeedsAttention("coarse batch checkpoint metadata is invalid")
        if cursor != coarse_target:
            raise AttemptPlanningNeedsAttention("coarse batches do not end at the reserved checkpoint")
        plans.append(MappingProxyType(dict(row)))
    if (data["status"] == "Feasible") != bool(plans):
        raise AttemptPlanningNeedsAttention("attempt planner status disagrees with the feasible plan list")
    return AttemptPlanResult(
        status=data["status"],
        current_advance=current_advance,
        target_snapshot_id=target_snapshot_id,
        target_request_digest=target_request_digest,
        plans=tuple(plans),
        target_statuses=tuple(MappingProxyType(dict(item)) for item in target_statuses),
        evaluation_count=evaluation_count,
        under_budget_position_count=under_budget,
        hardware_status=data["hardwareStatus"],
        reason=data.get("reason"),
    )


def _seed(value, name):
    if not isinstance(value, str) or len(value) != 16 or any(char not in "0123456789abcdefABCDEF" for char in value):
        raise AttemptPlanningError(f"{name} must be exactly 16 hexadecimal characters")
    return value.lower()


def _advance(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > MAX_ADVANCE:
        raise AttemptPlanningError(f"{name} must be an integer from 0 to {MAX_ADVANCE}")
    return value


def _integer(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise AttemptPlanningNeedsAttention(f"{name} must be a non-negative integer")
    return value


def _json_object(value, name):
    import json

    try:
        result = json.loads(json.dumps(dict(value), ensure_ascii=False))
    except (TypeError, ValueError) as exc:
        raise AttemptPlanningError(f"{name} must be JSON-compatible") from exc
    if not isinstance(result, dict):
        raise AttemptPlanningError(f"{name} must be an object")
    return result


def _thaw(value):
    if isinstance(value, Mapping):
        return {key: _thaw(item) for key, item in value.items()}
    if isinstance(value, tuple):
        return [_thaw(item) for item in value]
    if isinstance(value, list):
        return [_thaw(item) for item in value]
    return value
