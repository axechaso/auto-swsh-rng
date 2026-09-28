"""Bridge complete M2 search snapshots into bounded M4 planning and execution."""
from __future__ import annotations

from dataclasses import dataclass, replace
import threading
from typing import Callable
import re
from types import MappingProxyType
from typing import Mapping

from .attempt_execution import (
    AttemptActionPort,
    AttemptExecutionCoordinator,
    AttemptExecutionResult,
    AttemptExecutionSettings,
    AttemptExecutionStatus,
)
from .attempt_planner import (
    MAX_TARGETS,
    AttemptPlanResult,
    AttemptPlanner,
    AttemptPlanningSettings,
    PlanningTarget,
)
from .boundary import BoundaryAdjustment
from .models import (
    AutomationConfig,
    AutomationRunResult,
    AutomationRunStatus,
)
from .npc_calibration import NpcCalibrationResult
from .runner import AutomationDevicePort, AutomationNeedsAttention, AutomationRunner, validate_formal_start
from .seed_observer import SeedObserver


SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class M2M4WorkflowError(ValueError):
    pass


class M2M4WorkflowNeedsAttention(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class NpcCalibrationOutcome:
    calibration: NpcCalibrationResult
    current_advance: int

    def __post_init__(self):
        if not isinstance(self.calibration, NpcCalibrationResult):
            raise M2M4WorkflowError("calibration must be an NpcCalibrationResult")
        if (isinstance(self.current_advance, bool) or not isinstance(self.current_advance, int)
                or not 0 <= self.current_advance <= 1_000_000_000):
            raise M2M4WorkflowError("current_advance must be between 0 and 1,000,000,000")


@dataclass(frozen=True, slots=True)
class M2M4ClosedLoopResult:
    status: str
    run_id: str
    search_result: AutomationRunResult | None
    calibration_outcome: NpcCalibrationOutcome | None
    attempt_result: AttemptExecutionResult | None
    hardware_status: str
    reason: str | None = None
    capture_result: Any | None = None

    def as_dict(self):
        search = None
        if self.search_result is not None:
            search = {
                "status": self.search_result.status.value,
                "phase": self.search_result.phase.value,
                "targetSnapshotId": self.search_result.target_snapshot_id,
                "targetRequestDigest": self.search_result.target_request_digest,
                "candidateCount": len(self.search_result.candidate_rows),
                "reason": self.search_result.reason,
            }
        return {
            "reportSchema": "auto-swsh-m2-m4-closed-loop",
            "reportVersion": 1,
            "status": self.status,
            "runId": self.run_id,
            "search": search,
            "calibration": (
                self.calibration_outcome.calibration.as_report()
                if self.calibration_outcome else None
            ),
            "currentAdvanceAfterCalibration": (
                self.calibration_outcome.current_advance
                if self.calibration_outcome else None
            ),
            "attempt": self.attempt_result.as_dict() if self.attempt_result else None,
            "capture": (
                self.capture_result.as_dict() if hasattr(self.capture_result, "as_dict")
                else dict(self.capture_result) if isinstance(self.capture_result, Mapping)
                else None
            ),
            "hardwareStatus": self.hardware_status,
            "canStartFormalAutomation": False,
            "reason": self.reason,
        }


class M2M4AttemptCoordinator:
    """Plan and execute only candidates from one complete, immutable M2 result.

    Candidate lists larger than the CLI planner's per-request limit are split
    into pages. Every page is planned before the executor chooses a candidate,
    and all pages remain bound to the same composite M2 snapshot identity.
    """

    def __init__(self, *, search_result: AutomationRunResult, calculator,
                 observer: SeedObserver, actions: AttemptActionPort,
                 calibration: NpcCalibrationResult, boundary: BoundaryAdjustment,
                 probe_actions, run_id: str, context_revision: int,
                 planning_settings: AttemptPlanningSettings = AttemptPlanningSettings(),
                 execution_settings: AttemptExecutionSettings = AttemptExecutionSettings(),
                 cancel_event: threading.Event | None = None):
        if not isinstance(search_result, AutomationRunResult):
            raise M2M4WorkflowError("search_result must be an AutomationRunResult")
        if search_result.status != AutomationRunStatus.TARGETS_FOUND:
            raise M2M4WorkflowError("M4 requires a completed M2 search with targets")
        if not search_result.epochs:
            raise M2M4WorkflowError("M4 requires a target-bearing seed epoch")
        epoch = search_result.epochs[-1]
        if any(item.candidate_count != 0 for item in search_result.epochs[:-1]):
            raise M2M4WorkflowError("M2 target rows must belong to the first target-bearing epoch")
        if epoch.candidate_count < 1 or epoch.candidate_rows_truncated:
            raise M2M4WorkflowError("M4 requires a complete, non-truncated candidate set")
        if len(search_result.candidate_rows) != epoch.candidate_count:
            raise M2M4WorkflowError("M2 candidate rows do not match the complete candidate count")
        snapshot_id = search_result.target_snapshot_id
        request_digest = search_result.target_request_digest
        if (not isinstance(snapshot_id, str) or not snapshot_id
                or not isinstance(request_digest, str) or not SHA256_RE.fullmatch(request_digest)
                or search_result.search_order_version != "advance-fields-v1"
                or epoch.target_snapshot_id != snapshot_id
                or epoch.target_request_digest != request_digest
                or epoch.search_order_version != search_result.search_order_version):
            raise M2M4WorkflowError("M2 result is missing a stable, complete search snapshot identity")
        if (isinstance(epoch.searched_start, bool) or not isinstance(epoch.searched_start, int)
                or isinstance(epoch.searched_end, bool) or not isinstance(epoch.searched_end, int)
                or epoch.searched_end < epoch.searched_start
                or epoch.searched_positions != epoch.searched_end - epoch.searched_start + 1):
            raise M2M4WorkflowError("M2 result does not prove full inclusive scan coverage")
        if not isinstance(run_id, str) or not run_id or len(run_id) > 128:
            raise M2M4WorkflowError("run_id is required")
        if isinstance(context_revision, bool) or not isinstance(context_revision, int) or context_revision < 0:
            raise M2M4WorkflowError("context_revision must be a non-negative integer")
        if not isinstance(boundary, BoundaryAdjustment):
            raise M2M4WorkflowError("boundary must be a BoundaryAdjustment")

        targets = []
        seen_ids = set()
        for row in search_result.candidate_rows:
            if not isinstance(row, Mapping):
                raise M2M4WorkflowError("M2 returned an invalid candidate row")
            advance = row.get("advance")
            ordinal = row.get("candidateOrdinal")
            if (isinstance(advance, bool) or not isinstance(advance, int) or not 0 <= advance <= 1_000_000_000
                    or isinstance(ordinal, bool) or not isinstance(ordinal, int) or ordinal < 0):
                raise M2M4WorkflowError("M2 candidate identity requires a valid advance and candidateOrdinal")
            if not epoch.searched_start <= advance <= epoch.searched_end:
                raise M2M4WorkflowError("M2 candidate falls outside the completed search range")
            candidate_id = f"advance-{advance}-ordinal-{ordinal}"
            if candidate_id in seen_ids:
                raise M2M4WorkflowError("M2 returned duplicate candidate identities")
            seen_ids.add(candidate_id)
            targets.append(PlanningTarget(candidate_id, advance))
        if len(targets) != epoch.candidate_count:
            raise M2M4WorkflowError("M2 candidate identities are incomplete")

        try:
            probe_actions = tuple(MappingProxyType(dict(item)) for item in probe_actions)
        except (TypeError, ValueError) as exc:
            raise M2M4WorkflowError("probe_actions must be a sequence of JSON action objects") from exc
        if not probe_actions or any(not isinstance(item, Mapping) for item in probe_actions):
            raise M2M4WorkflowError("probe_actions must contain modeled actions")

        self.search_result = search_result
        self.epoch = epoch
        self.calculator = calculator
        self.observer = observer
        self.actions = actions
        self.calibration = calibration
        self.boundary = boundary
        self.probe_actions = probe_actions
        self.run_id = run_id
        self.context_revision = context_revision
        self.planning_settings = planning_settings
        self.execution_settings = execution_settings
        self.cancel_event = cancel_event or threading.Event()
        self.targets = tuple(targets)
        self.target_snapshot_id = snapshot_id
        self.target_request_digest = request_digest
        self.planning_page_count = (len(targets) + MAX_TARGETS - 1) // MAX_TARGETS

    def cancel(self):
        self.cancel_event.set()
        try:
            self.actions.stop_scripts()
        except Exception:
            pass

    def run(self, *, attempt_id: str, current_advance: int = 0) -> AttemptExecutionResult:
        planner = AttemptPlanner(
            calculator=self.calculator,
            run_id=self.run_id,
            epoch_id=self.epoch.epoch_id,
            context_revision=self.context_revision,
            cancel_event=self.cancel_event,
        )

        def replan(position: int, relocation_reserve: int, precise_reserve: int) -> AttemptPlanResult:
            settings = replace(
                self.planning_settings,
                relocation_reserve=relocation_reserve,
                precise_reserve=precise_reserve,
            )
            pages = []
            for start in range(0, len(self.targets), MAX_TARGETS):
                if self.cancel_event.is_set():
                    raise M2M4WorkflowNeedsAttention("planning cancelled")
                pages.append(planner.plan(
                    anchor_seed0=self.epoch.seed0,
                    anchor_seed1=self.epoch.seed1,
                    current_advance=position,
                    targets=self.targets[start:start + MAX_TARGETS],
                    target_snapshot_id=self.target_snapshot_id,
                    target_request_digest=self.target_request_digest,
                    boundary=self.boundary,
                    probe_actions=self.probe_actions,
                    calibration=self.calibration,
                    settings=settings,
                ))
            return _merge_plan_pages(pages, self.target_snapshot_id, self.target_request_digest)

        executor = AttemptExecutionCoordinator(
            calculator=self.calculator,
            observer=self.observer,
            actions=self.actions,
            attempt_id=attempt_id,
            run_id=self.run_id,
            epoch_id=self.epoch.epoch_id,
            context_revision=self.context_revision,
            cancel_event=self.cancel_event,
        )
        return executor.run(
            anchor_seed0=self.epoch.seed0,
            anchor_seed1=self.epoch.seed1,
            current_advance=current_advance,
            replan=replan,
            settings=self.execution_settings,
        )


class M2M4ClosedLoopCoordinator:
    """Run M2 search, offline NPC calibration, and M4 under one task lease.

    Calibration and scenario actions remain injected ports. This keeps the
    complete coordinator runnable with replay/simulation while requiring any
    future hardware adapter to share the exact lease passed here.
    """

    def __init__(self, *, config: AutomationConfig, device: AutomationDevicePort,
                 observer: SeedObserver, calculator, calibration_provider: Callable,
                 actions_factory: Callable, boundary: BoundaryAdjustment, probe_actions,
                 planning_settings: AttemptPlanningSettings = AttemptPlanningSettings(),
                 execution_settings: AttemptExecutionSettings = AttemptExecutionSettings(),
                 evidence_store=None, on_event=None, capture_stage_factory=None,
                 cancel_event: threading.Event | None = None):
        if not isinstance(config, AutomationConfig):
            raise M2M4WorkflowError("config must be an AutomationConfig")
        if not callable(calibration_provider) or not callable(actions_factory):
            raise M2M4WorkflowError("calibration_provider and actions_factory must be callable")
        if capture_stage_factory is not None and not callable(capture_stage_factory):
            raise M2M4WorkflowError("capture_stage_factory must be callable")
        if not isinstance(boundary, BoundaryAdjustment):
            raise M2M4WorkflowError("boundary must be a BoundaryAdjustment")
        self.config = config
        self.device = device
        self.observer = observer
        self.calculator = calculator
        self.calibration_provider = calibration_provider
        self.actions_factory = actions_factory
        self.boundary = boundary
        self.probe_actions = tuple(probe_actions)
        self.planning_settings = planning_settings
        self.execution_settings = execution_settings
        self.evidence_store = evidence_store
        self.on_event = on_event
        self.capture_stage_factory = capture_stage_factory
        self.cancel_event = cancel_event or threading.Event()
        self._run_lock = threading.Lock()
        self._has_run = False
        self._active_runner = None
        self._active_attempt = None
        self._active_capture = None

    def cancel(self):
        self.cancel_event.set()
        if self._active_runner is not None:
            self._active_runner.cancel()
        else:
            try:
                self.device.cancel()
            except Exception:
                pass
        if self._active_attempt is not None:
            self._active_attempt.cancel()
        if self._active_capture is not None:
            cancel = getattr(self._active_capture, "cancel", None)
            if callable(cancel):
                cancel()

    def run(self, *, attempt_id: str) -> M2M4ClosedLoopResult:
        if self._has_run or not self._run_lock.acquire(blocking=False):
            raise RuntimeError("an M2-M4 closed-loop coordinator can run only once")
        self._has_run = True
        status = "FAILED"
        reason = None
        search_result = None
        calibration_outcome = None
        attempt_result = None
        capture_result = None
        lease = None
        owns_lease = False
        cleanup_errors = []
        try:
            proceed = True
            try:
                validate_formal_start(self.config, self.evidence_store)
            except AutomationNeedsAttention as exc:
                status, reason = "NEEDS_ATTENTION", str(exc)
                proceed = False
            if proceed and self.cancel_event.is_set():
                status, reason = "CANCELLED", "用户请求停止。"
                proceed = False
            if proceed:
                try:
                    lease = self.device.acquire_task_lease()
                    owns_lease = lease is not None
                except Exception as exc:
                    status, reason = "NEEDS_ATTENTION", f"DEVICE_LEASE_FAILED: {exc}"
                    proceed = False
                if proceed and lease is None:
                    status, reason = "NEEDS_ATTENTION", "DEVICE_LEASE_FAILED: no lease was returned"
                    proceed = False

            if proceed:
                status = "RUNNING"
                runner = AutomationRunner(
                    self.config,
                    device=self.device,
                    observer=self.observer,
                    calculator=self.calculator,
                    on_event=self.on_event,
                    evidence_store=self.evidence_store,
                    task_lease=lease,
                    cancel_event=self.cancel_event,
                )
                self._active_runner = runner
                search_result = runner.run()
                self._active_runner = None
                if search_result.status != AutomationRunStatus.TARGETS_FOUND:
                    status = (
                        "CANCELLED" if search_result.status == AutomationRunStatus.CANCELLED
                        else "NEEDS_ATTENTION" if search_result.status == AutomationRunStatus.NEEDS_ATTENTION
                        else "FAILED"
                    )
                    reason = search_result.reason or "M2 did not return a complete target snapshot"
                    proceed = False

            if proceed and self.cancel_event.is_set():
                status, reason = "CANCELLED", "用户请求停止。"
                proceed = False
            if proceed:
                try:
                    calibration_outcome = self.calibration_provider(
                        search_result, lease, self.cancel_event,
                    )
                except Exception as exc:
                    status, reason = "NEEDS_ATTENTION", f"NPC_CALIBRATION_FAILED: {exc}"
                    proceed = False
            if proceed and not isinstance(calibration_outcome, NpcCalibrationOutcome):
                status = "NEEDS_ATTENTION"
                reason = "NPC_CALIBRATION_OUTCOME_INVALID"
                proceed = False
            if proceed:
                calibration = calibration_outcome.calibration
                if (calibration.status != "CalibratedOffline"
                        or calibration.hardware_status != "PendingHardwareValidation"):
                    status = "NEEDS_ATTENTION"
                    reason = f"NPC_CALIBRATION_{calibration.status.upper()}"
                    proceed = False
            if proceed and self.cancel_event.is_set():
                status, reason = "CANCELLED", "用户请求停止。"
                proceed = False

            if proceed:
                calibration = calibration_outcome.calibration
                actions = self.actions_factory(lease, search_result, calibration_outcome)
                bridge = M2M4AttemptCoordinator(
                    search_result=search_result,
                    calculator=self.calculator,
                    observer=self.observer,
                    actions=actions,
                    calibration=calibration,
                    boundary=self.boundary,
                    probe_actions=self.probe_actions,
                    run_id=self.config.run_id,
                    context_revision=self.config.context_revision,
                    planning_settings=self.planning_settings,
                    execution_settings=self.execution_settings,
                    cancel_event=self.cancel_event,
                )
                self._active_attempt = bridge
                attempt_result = bridge.run(
                    attempt_id=attempt_id,
                    current_advance=calibration_outcome.current_advance,
                )
                self._active_attempt = None
                if attempt_result.status == AttemptExecutionStatus.TRIGGER_EXECUTED:
                    status = "ATTEMPT_TRIGGERED_PENDING_CAPTURE"
                    reason = None
                    if self.capture_stage_factory is not None:
                        capture_stage = self.capture_stage_factory(
                            lease, attempt_result, search_result, calibration_outcome,
                            self.cancel_event,
                        )
                        if not callable(getattr(capture_stage, "run", None)):
                            raise M2M4WorkflowError("capture stage factory must return an object with run()")
                        self._active_capture = capture_stage
                        capture_result = capture_stage.run()
                        self._active_capture = None
                        capture_status = getattr(capture_result, "status", None)
                        if capture_status == "SAVED_SUCCESS":
                            capture_report = (
                                capture_result.as_dict()
                                if callable(getattr(capture_result, "as_dict", None)) else None
                            )
                            commit = capture_report.get("saveCommit") if isinstance(capture_report, Mapping) else None
                            workflow_report = capture_report.get("workflow") if isinstance(capture_report, Mapping) else None
                            if (not isinstance(capture_report, Mapping)
                                    or capture_report.get("reportSchema") != "auto-swsh-capture-attempt"
                                    or capture_report.get("attemptId") != attempt_result.attempt_id
                                    or not isinstance(commit, Mapping) or commit.get("stage") != "Completed"
                                    or not isinstance(workflow_report, Mapping)
                                    or workflow_report.get("phase") != "completed"):
                                status = "NEEDS_ATTENTION"
                                reason = "capture result lacks a matching completed SaveReceipt"
                            else:
                                status = "SUCCESS_SAVED"
                        elif capture_status == "CANCELLED":
                            status = "CANCELLED"
                            reason = getattr(capture_result, "reason", None) or "capture stage was cancelled"
                        else:
                            status = "NEEDS_ATTENTION"
                            reason = getattr(capture_result, "reason", None) or (
                                "capture and save verification did not complete"
                            )
                elif attempt_result.status == AttemptExecutionStatus.CANCELLED:
                    status = "CANCELLED"
                    reason = attempt_result.reason
                else:
                    status = "NEEDS_ATTENTION"
                    reason = attempt_result.reason
        except Exception as exc:
            status, reason = "FAILED", f"{type(exc).__name__}: {exc}"
        finally:
            self._active_runner = None
            self._active_attempt = None
            self._active_capture = None
            if owns_lease:
                try:
                    self.device.stop_scripts()
                except Exception as exc:
                    cleanup_errors.append(f"stop_scripts: {exc}")
                try:
                    self.device.release_task_lease(lease)
                except Exception as exc:
                    cleanup_errors.append(f"release_task_lease: {exc}")
            if cleanup_errors:
                status = "NEEDS_ATTENTION"
                cleanup_reason = "CLEANUP_FAILED: " + "; ".join(cleanup_errors)
                reason = f"{reason}; {cleanup_reason}" if reason else cleanup_reason
            self._run_lock.release()
        return self._result(status, search_result, calibration_outcome, attempt_result, reason,
                            capture_result=capture_result)

    def _result(self, status, search_result, calibration_outcome, attempt_result, reason,
                *, capture_result=None):
        return M2M4ClosedLoopResult(
            status=status,
            run_id=self.config.run_id,
            search_result=search_result,
            calibration_outcome=calibration_outcome,
            attempt_result=attempt_result,
            hardware_status="PendingHardwareValidation",
            reason=reason,
            capture_result=capture_result,
        )


def _merge_plan_pages(pages, snapshot_id, request_digest):
    if not pages:
        raise M2M4WorkflowNeedsAttention("planner received no target candidates")
    first = pages[0]
    plans = []
    statuses = []
    for page in pages:
        if (page.current_advance != first.current_advance
                or page.target_snapshot_id != snapshot_id
                or page.target_request_digest != request_digest
                or page.hardware_status != "PendingHardwareValidation"):
            raise M2M4WorkflowNeedsAttention("planner pages do not belong to one confirmed M2 snapshot")
        plans.extend(page.plans)
        statuses.extend(page.target_statuses)
    plan_ids = [item.get("targetCandidateId") for item in plans]
    if len(plan_ids) != len(set(plan_ids)):
        raise M2M4WorkflowNeedsAttention("planner duplicated a target across pages")
    feasible = bool(plans)
    status = "Feasible" if feasible else (
        pages[0].status if all(page.status == pages[0].status for page in pages)
        else "NoFeasibleTriggerPosition"
    )
    return AttemptPlanResult(
        status=status,
        current_advance=first.current_advance,
        target_snapshot_id=snapshot_id,
        target_request_digest=request_digest,
        plans=tuple(plans),
        target_statuses=tuple(statuses),
        evaluation_count=sum(page.evaluation_count for page in pages),
        under_budget_position_count=sum(page.under_budget_position_count for page in pages),
        hardware_status="PendingHardwareValidation",
        reason=None if feasible else "; ".join(filter(None, (page.reason for page in pages))) or None,
    )
