"""M2 restore, independently verify, search and confirmed-empty restart loop."""
from __future__ import annotations

from collections.abc import Mapping, Sequence
from collections import deque
from typing import Any, Protocol
import threading
import time
import uuid
from types import MappingProxyType

from ..backend import PROTOCOL_VERSION, validate_event
from .models import (
    AutomationConfig,
    AutomationEvent,
    AutomationPhase,
    AutomationRunResult,
    AutomationRunStatus,
    SeedEpochSummary,
)


class CalculatorPort(Protocol):
    """Executes one protocol-2 JSON request and returns its response event.

    Implementations must honor ``cancel_event`` by terminating an active
    calculation and must return the complete protocol envelope, including the
    request identity fields.
    """

    def execute(self, request: dict[str, Any], cancel_event: threading.Event) -> dict[str, Any]: ...


class AutomationDevicePort(Protocol):
    """Device and scene adapter; a single lease spans the complete run."""

    def acquire_task_lease(self) -> Any: ...

    def preflight(self, config: AutomationConfig, lease: Any, cancel_event: threading.Event) -> None: ...

    def restore_scene(
        self, config: AutomationConfig, lease: Any, epoch_id: str,
        *, after_restart: bool, cancel_event: threading.Event,
    ) -> bool: ...

    def trigger_seed_bit(
        self, config: AutomationConfig, lease: Any, epoch_id: str,
        purpose: str, index: int, cancel_event: threading.Event,
    ) -> None: ...

    def restart_game(
        self, config: AutomationConfig, lease: Any, epoch_id: str,
        cancel_event: threading.Event,
    ) -> bool: ...

    def cancel(self) -> None: ...

    def stop_scripts(self) -> None: ...

    def release_task_lease(self, lease: Any) -> None: ...


class AutomationNeedsAttention(RuntimeError):
    """A physical state or evidence gap makes safe automatic progress unclear."""


class _Cancelled(Exception):
    pass


class AutomationRunner:
    """Runs the M2 closed search loop over injected, testable service ports.

    A run ends at ``TARGETS_FOUND`` when a complete search yields candidates;
    later NPC, planning and capture phases are intentionally separate states.
    Empty search results cause a game restart only after every inclusive range
    chunk has completed and its returned bounds have been checked.
    """

    def __init__(self, config: AutomationConfig, *, device: AutomationDevicePort,
                 observer, calculator: CalculatorPort, on_event=None, evidence_store=None):
        self.config = config
        self.device = device
        self.observer = observer
        self.calculator = calculator
        self.on_event = on_event
        self.evidence_store = evidence_store
        self.cancel_event = threading.Event()
        self._run_lock = threading.Lock()
        self._has_run = False
        self._events = deque(maxlen=2_048)
        self._event_count = 0

    def cancel(self) -> None:
        self.cancel_event.set()
        try:
            self.device.cancel()
        except Exception:
            pass

    def run(self) -> AutomationRunResult:
        if self._has_run:
            raise RuntimeError("an automation runner can only be run once")
        if not self._run_lock.acquire(blocking=False):
            raise RuntimeError("this automation runner is already running")
        self._has_run = True
        epochs = deque(maxlen=100)
        rows: list[Mapping[str, Any]] = []
        lease = None
        phase = AutomationPhase.PREFLIGHT
        status = AutomationRunStatus.FAILED
        reason: str | None = None
        acquired = False
        try:
            self._check_cancel()
            if self.config.execution_mode == "formal":
                if self.evidence_store is None:
                    raise AutomationNeedsAttention(
                        "FORMAL_START_BLOCKED: a verified scenario evidence store is required"
                    )
                try:
                    report = self.evidence_store.report(verify_files=True)
                except Exception as exc:
                    raise AutomationNeedsAttention(f"FORMAL_START_BLOCKED: evidence check failed: {exc}") from exc
                if (
                    report.get("scenarioId") != self.config.scenario_id
                    or report.get("frameworkStatus") != "FrameworkReady"
                    or report.get("hardwareStatus") != "HardwareValidated"
                    or report.get("canStartFormalAutomation") is not True
                ):
                    raise AutomationNeedsAttention(
                        "FORMAL_START_BLOCKED: scenario evidence does not authorize formal automation"
                    )
            try:
                lease = self.device.acquire_task_lease()
                acquired = True
            except Exception as exc:
                raise AutomationNeedsAttention(f"DEVICE_LEASE_FAILED: {exc}") from exc
            if lease is None:
                raise AutomationNeedsAttention("DEVICE_LEASE_FAILED: no lease was returned")

            self._emit(phase, "正在检查设备、场景和运行配置。")
            self._check_cancel()
            try:
                self.device.preflight(self.config, lease, self.cancel_event)
            except Exception as exc:
                self._check_cancel()
                raise AutomationNeedsAttention(f"PREFLIGHT_FAILED: {exc}") from exc

            epoch_index = 0
            after_restart = False
            while True:
                self._check_cancel()
                epoch_id = uuid.uuid4().hex
                epoch_index += 1
                phase = AutomationPhase.RESTORE_SCENE
                self._emit(phase, f"第 {epoch_index} 轮：恢复测种场景。", epoch_id)
                try:
                    restored = self.device.restore_scene(
                        self.config, lease, epoch_id,
                        after_restart=after_restart,
                        cancel_event=self.cancel_event,
                    )
                except Exception as exc:
                    self._check_cancel()
                    raise AutomationNeedsAttention(f"SCENE_RESTORE_FAILED: {exc}") from exc
                if restored is not True:
                    raise AutomationNeedsAttention("SCENE_RESTORE_UNCONFIRMED")

                phase = AutomationPhase.OBSERVE_SEED
                self._emit(phase, "采集 128 位测种动画。", epoch_id)
                anchor = self._observe_and_verify_seed(lease, epoch_id)

                phase = AutomationPhase.SEARCH_TARGETS
                self._emit(
                    phase,
                    f"完整搜索 RNG 推进范围 [{self.config.min_advance}, {self.config.max_advance}]。",
                    epoch_id,
                )
                search = self._search_range(lease, epoch_id, anchor)
                summary = SeedEpochSummary(
                    epoch_id=epoch_id,
                    seed0=anchor["seed0"],
                    seed1=anchor["seed1"],
                    observation_bits=128,
                    verification_bits=128,
                    searched_positions=search["searched_positions"],
                    candidate_count=search["candidate_count"],
                    candidate_rows_truncated=search["candidate_rows_truncated"],
                )
                epochs.append(summary)
                rows = search["rows"]

                if search["candidate_count"] > 0:
                    status = AutomationRunStatus.TARGETS_FOUND
                    reason = None
                    self._emit(
                        phase,
                        f"完整搜索完成，找到 {search['candidate_count']} 个候选；等待 NPC 校准和后续阶段。",
                        epoch_id,
                        candidateCount=search["candidate_count"],
                    )
                    break

                self._emit(
                    phase,
                    "完整搜索确认范围内没有候选。",
                    epoch_id,
                    searchedPositions=search["searched_positions"],
                )
                if self.config.max_epochs is not None and epoch_index >= self.config.max_epochs:
                    raise AutomationNeedsAttention("EPOCH_LIMIT_REACHED_AFTER_COMPLETE_EMPTY_SEARCH")

                phase = AutomationPhase.RESTART
                self._emit(phase, "确认空搜索后重启游戏。", epoch_id)
                try:
                    restarted = self.device.restart_game(
                        self.config, lease, epoch_id, self.cancel_event,
                    )
                except Exception as exc:
                    self._check_cancel()
                    raise AutomationNeedsAttention(f"RESTART_FAILED: {exc}") from exc
                if restarted is not True:
                    raise AutomationNeedsAttention("RESTART_UNCONFIRMED")
                after_restart = True

        except _Cancelled:
            status = AutomationRunStatus.CANCELLED
            reason = "用户请求停止。"
        except AutomationNeedsAttention as exc:
            status = AutomationRunStatus.NEEDS_ATTENTION
            reason = str(exc)
        except Exception as exc:
            status = AutomationRunStatus.FAILED
            reason = f"{type(exc).__name__}: {exc}"
        finally:
            cleanup_errors = []
            if acquired:
                self._emit(AutomationPhase.CLEANUP, "停止动作并释放设备租约。")
                try:
                    self.device.stop_scripts()
                except Exception as exc:
                    cleanup_errors.append(f"stop_scripts: {exc}")
                try:
                    self.device.release_task_lease(lease)
                except Exception as exc:
                    cleanup_errors.append(f"release_task_lease: {exc}")
            if cleanup_errors:
                status = AutomationRunStatus.NEEDS_ATTENTION
                cleanup_reason = "CLEANUP_FAILED: " + "; ".join(cleanup_errors)
                reason = f"{reason}; {cleanup_reason}" if reason else cleanup_reason
            self._run_lock.release()

        return AutomationRunResult(
            run_id=self.config.run_id,
            status=status,
            phase=phase,
            epochs=tuple(epochs),
            candidate_rows=tuple(rows),
            reason=reason,
            events=tuple(self._events),
            total_epochs=epoch_index if "epoch_index" in locals() else 0,
            total_events=self._event_count,
            event_history_truncated=self._event_count > len(self._events),
        )

    def _observe_and_verify_seed(self, lease, epoch_id: str) -> dict[str, str]:
        last_reason = "SEED_OBSERVATION_UNKNOWN"
        for read_index in range(self.config.max_seed_rounds):
            self._check_cancel()
            if read_index:
                self._emit(
                    AutomationPhase.OBSERVE_SEED,
                    f"前一轮观测不完整或验证不符，恢复后重新测种（{read_index + 1}/{self.config.max_seed_rounds}）。",
                    epoch_id,
                )
                self._restore_for_retry(lease, epoch_id)

            measured = self.observer.observe_sequence(
                128,
                lambda index: self._trigger_bit(lease, epoch_id, "solve", index),
                cancel_event=self.cancel_event,
            )
            self._check_cancel()
            if not getattr(measured, "complete", False) or not _valid_bits(getattr(measured, "observations", None), 128):
                last_reason = "SEED_OBSERVATION_UNKNOWN: " + str(getattr(measured, "unknown_index", None))
                continue

            self._emit(AutomationPhase.OBSERVE_SEED, "128 位动画观测完整，正在计算种子。", epoch_id)
            try:
                solve_event = self._calculator_request(
                    {"operation": "seed.solve", "observations": measured.observations}, epoch_id,
                )
            except Exception as exc:
                self._check_cancel()
                raise AutomationNeedsAttention(f"SEED_SOLVE_FAILED: {exc}") from exc
            solved = solve_event["data"]
            if (
                solved.get("observationCount") != 128
                or solved.get("boundarySemanticsVersion") != "retail-seed-observation-v1"
            ):
                raise AutomationNeedsAttention("SEED_SOLVE_SEMANTICS_UNSUPPORTED")
            starting = solved.get("stateAfterObservations")
            _validate_state(starting, "stateAfterObservations")

            self._emit(AutomationPhase.VERIFY_SEED, "采集独立动画并验证种子。", epoch_id)
            verified_sample = self.observer.observe_sequence(
                128,
                lambda index: self._trigger_bit(lease, epoch_id, "verify", index),
                cancel_event=self.cancel_event,
            )
            self._check_cancel()
            if not getattr(verified_sample, "complete", False) or not _valid_bits(getattr(verified_sample, "observations", None), 128):
                last_reason = "SEED_VERIFICATION_UNKNOWN: " + str(getattr(verified_sample, "unknown_index", None))
                continue
            try:
                verify_event = self._calculator_request({
                    "operation": "seed.verify",
                    "seed0": starting["seed0"],
                    "seed1": starting["seed1"],
                    "observations": verified_sample.observations,
                }, epoch_id)
            except Exception as exc:
                self._check_cancel()
                raise AutomationNeedsAttention(f"SEED_VERIFY_FAILED: {exc}") from exc
            verified = verify_event["data"]
            if verified.get("matches") is True:
                _validate_state(verified.get("stateAfterObservations"), "stateAfterObservations")
                return verified["stateAfterObservations"]
            last_reason = "SEED_VERIFICATION_FAILED"
        raise AutomationNeedsAttention(last_reason)

    def _search_range(self, lease, epoch_id: str, anchor: Mapping[str, str]) -> dict[str, Any]:
        del lease  # Search is read-only; retaining the lease keeps device ownership stable.
        scan_cursor = self.config.min_advance
        total_candidates = 0
        searched_positions = 0
        candidate_rows: list[Mapping[str, Any]] = []
        while scan_cursor <= self.config.max_advance:
            self._check_cancel()
            last_error = None
            chunk_result = None
            for attempt in range(self.config.search_retries + 1):
                self._check_cancel()
                try:
                    request = self.config.search_request_copy()
                    request.update({
                        "operation": "encounter.search",
                        "seed0": anchor["seed0"],
                        "seed1": anchor["seed1"],
                        "start": self.config.min_advance,
                        "end": self.config.max_advance,
                        "scanCursor": scan_cursor,
                        "scanLimit": self.config.chunk_size,
                        "candidateCursor": 0,
                        "candidatePageSize": 10_000,
                    })
                    first_page = self._calculator_request(request, epoch_id)["data"]
                    scanned_end = self._validate_search_page(
                        first_page,
                        scan_start=scan_cursor,
                        requested_end=self.config.max_advance,
                        scan_limit=self.config.chunk_size,
                        candidate_cursor=0,
                    )
                    total = first_page["candidateTotal"]
                    snapshot_id = first_page["snapshotId"]
                    request_digest = first_page["requestDigest"]
                    order_version = first_page["orderVersion"]
                    rows = list(first_page["rows"])
                    next_candidate = first_page["nextCandidateCursor"]
                    page = first_page
                    while page["candidatePageComplete"] is False:
                        self._check_cancel()
                        if next_candidate is None:
                            raise ValueError("candidate page is incomplete but has no nextCandidateCursor")
                        page_request = {
                            **request,
                            "candidateCursor": next_candidate,
                            "snapshotId": snapshot_id,
                            "requestDigest": request_digest,
                        }
                        page = self._calculator_request(page_request, epoch_id)["data"]
                        page_end = self._validate_search_page(
                            page,
                            scan_start=scan_cursor,
                            requested_end=self.config.max_advance,
                            scan_limit=self.config.chunk_size,
                            candidate_cursor=next_candidate,
                        )
                        if page_end != scanned_end:
                            raise ValueError("candidate page changed the scanned bounds")
                        if (
                            page["snapshotId"] != snapshot_id
                            or page["requestDigest"] != request_digest
                            or page["orderVersion"] != order_version
                            or page["candidateTotal"] != total
                        ):
                            raise ValueError("candidate page belongs to a different search snapshot")
                        rows.extend(page["rows"])
                        next_candidate = page["nextCandidateCursor"]
                    if len(rows) != total:
                        raise ValueError("candidate pages did not enumerate the complete snapshot")
                    if first_page["scanComplete"] != (scanned_end == self.config.max_advance):
                        raise ValueError("scanComplete does not match the requested range")
                    expected_scan_cursor = None if scanned_end == self.config.max_advance else scanned_end + 1
                    if first_page["nextScanCursor"] != expected_scan_cursor:
                        raise ValueError("nextScanCursor does not match the scanned bounds")
                    chunk_result = {
                        "candidate_count": total,
                        "searched_start": scan_cursor,
                        "searched_end": scanned_end,
                        "rows": rows,
                        "next_scan_cursor": expected_scan_cursor,
                    }
                    last_error = None
                    break
                except _Cancelled:
                    raise
                except Exception as exc:
                    self._check_cancel()
                    last_error = exc
                    if attempt < self.config.search_retries:
                        self._emit(
                            AutomationPhase.SEARCH_TARGETS,
                            f"搜索分块 [{scan_cursor}, {self.config.max_advance}] 未确认完整，重试。",
                            epoch_id,
                            attempt=attempt + 1,
                        )
            if last_error is not None:
                raise AutomationNeedsAttention(
                    f"SEARCH_INCOMPLETE [{scan_cursor}, {self.config.max_advance}]: {last_error}"
                ) from last_error
            total_candidates += chunk_result["candidate_count"]
            searched_positions += chunk_result["searched_end"] - scan_cursor + 1
            candidate_rows.extend(MappingProxyType(dict(row)) for row in chunk_result["rows"])
            self._emit(
                AutomationPhase.SEARCH_TARGETS,
                f"已完整搜索 [{scan_cursor}, {chunk_result['searched_end']}]，发现 {chunk_result['candidate_count']} 个候选。",
                epoch_id,
                searchedStart=scan_cursor,
                searchedEnd=chunk_result["searched_end"],
                candidateCount=chunk_result["candidate_count"],
            )
            if chunk_result["next_scan_cursor"] is None:
                break
            scan_cursor = chunk_result["next_scan_cursor"]
        return {
            "candidate_count": total_candidates,
            "searched_positions": searched_positions,
            "candidate_rows_truncated": False,
            "rows": candidate_rows,
        }

    @staticmethod
    def _validate_search_page(data, *, scan_start, requested_end, scan_limit, candidate_cursor):
        if not isinstance(data, Mapping):
            raise ValueError("search page data must be an object")
        searched_end = min(requested_end, scan_start + scan_limit - 1)
        if data.get("searchedStart") != scan_start or data.get("searchedEnd") != searched_end:
            raise ValueError("search page bounds do not match the requested scan cursor")
        if data.get("candidateStart") != candidate_cursor:
            raise ValueError("candidate page start does not match the requested cursor")
        if data.get("scanComplete") is not (searched_end == requested_end):
            raise ValueError("scanComplete is invalid")
        next_scan_cursor = data.get("nextScanCursor")
        expected_scan_cursor = None if searched_end == requested_end else searched_end + 1
        if next_scan_cursor != expected_scan_cursor:
            raise ValueError("nextScanCursor is invalid")
        total = data.get("candidateTotal")
        rows = data.get("rows")
        if isinstance(total, bool) or not isinstance(total, int) or total < 0:
            raise ValueError("search page candidateTotal is invalid")
        if not isinstance(rows, Sequence) or isinstance(rows, (str, bytes)):
            raise ValueError("search page rows are invalid")
        if any(not isinstance(row, Mapping) for row in rows):
            raise ValueError("search page contains an invalid candidate row")
        next_candidate = data.get("nextCandidateCursor")
        page_complete = data.get("candidatePageComplete")
        expected_next = candidate_cursor + len(rows)
        if not isinstance(page_complete, bool) or expected_next > total:
            raise ValueError("candidate page completeness metadata is invalid")
        if page_complete != (expected_next >= total):
            raise ValueError("candidatePageComplete is inconsistent with the returned page")
        if next_candidate != (None if page_complete else expected_next):
            raise ValueError("nextCandidateCursor is inconsistent with the returned page")
        for name in ("snapshotId", "requestDigest", "orderVersion"):
            value = data.get(name)
            if not isinstance(value, str) or not value:
                raise ValueError(f"search page {name} is required")
        if len(data["requestDigest"]) != 64 or any(
            char not in "0123456789abcdef" for char in data["requestDigest"]
        ):
            raise ValueError("search page requestDigest must be lowercase SHA-256")
        if data["orderVersion"] != "advance-fields-v1":
            raise ValueError("search page orderVersion is unsupported")
        return searched_end

    def _restore_for_retry(self, lease, epoch_id):
        self._check_cancel()
        try:
            restored = self.device.restore_scene(
                self.config, lease, epoch_id,
                after_restart=False,
                cancel_event=self.cancel_event,
            )
        except Exception as exc:
            self._check_cancel()
            raise AutomationNeedsAttention(f"SEED_RETRY_RESTORE_FAILED: {exc}") from exc
        if restored is not True:
            raise AutomationNeedsAttention("SEED_RETRY_RESTORE_UNCONFIRMED")

    def _trigger_bit(self, lease, epoch_id, purpose, index):
        self._check_cancel()
        self.device.trigger_seed_bit(
            self.config, lease, epoch_id, purpose, index, self.cancel_event,
        )

    def _calculator_request(self, fields: Mapping[str, Any], epoch_id: str) -> dict[str, Any]:
        self._check_cancel()
        request = {
            **fields,
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": self.config.run_id,
            "epochId": epoch_id,
            "contextRevision": self.config.context_revision,
        }
        response = self.calculator.execute(request, self.cancel_event)
        self._check_cancel()
        validate_event(response, request)
        if response.get("type") == "error":
            raise RuntimeError(str(response.get("message", "calculator request failed")))
        if response.get("type") != "result" or not isinstance(response.get("data"), Mapping):
            raise ValueError("calculator returned an invalid result event")
        return response

    def _emit(self, phase, message, epoch_id=None, **details):
        event = AutomationEvent(
            run_id=self.config.run_id,
            epoch_id=epoch_id,
            phase=phase,
            message=message,
            timestamp_ns=time.monotonic_ns(),
            details=MappingProxyType(dict(details)),
        )
        self._events.append(event)
        self._event_count += 1
        if self.on_event is not None:
            try:
                self.on_event(event)
            except Exception:
                # UI/log observers do not control the automation outcome.
                pass

    def _check_cancel(self):
        if self.cancel_event.is_set():
            raise _Cancelled()


def _valid_bits(value, count):
    return isinstance(value, str) and len(value) == count and all(bit in "01" for bit in value)


def _validate_state(value, name):
    if not isinstance(value, Mapping):
        raise AutomationNeedsAttention(f"{name} is missing")
    for key in ("seed0", "seed1"):
        part = value.get(key)
        if not isinstance(part, str) or len(part) != 16 or any(char not in "0123456789abcdefABCDEF" for char in part):
            raise AutomationNeedsAttention(f"{name}.{key} is not a fixed-width hexadecimal state")
