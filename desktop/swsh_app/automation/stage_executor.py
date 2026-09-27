"""Serial, cancellable ECS stages owned by one automation task lease."""
from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
import threading
import time

from ..controller import ControllerSession, ControllerTaskLease


class StageOutcome(StrEnum):
    COMPLETED = "completed"
    CANCELLED = "cancelled"
    ABORTED = "aborted"
    FAILED = "failed"


@dataclass(frozen=True, slots=True)
class StageResult:
    run_id: str
    epoch_id: str
    context_revision: int
    stage_id: str
    outcome: StageOutcome
    started_at_ns: int
    finished_at_ns: int
    start_frame_id: int | None
    end_frame_id: int | None
    message: str | None = None


class AutomationStageExecutor:
    """Runs existing ECS actions while preventing manual serial interleaving."""

    def __init__(
        self,
        session: ControllerSession,
        *,
        run_id: str,
        epoch_id: str,
        context_revision: int = 0,
        frames=None,
        label_root=None,
    ):
        if not run_id or not epoch_id:
            raise ValueError("run_id and epoch_id are required")
        if context_revision < 0:
            raise ValueError("context_revision cannot be negative")
        self.session = session
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.frames = frames
        self.label_root = label_root
        self.cancel_event = threading.Event()
        self.lease: ControllerTaskLease | None = None

    def acquire(self) -> ControllerTaskLease:
        if self.lease is not None and not self.lease.released:
            return self.lease
        self.lease = self.session.acquire_task_lease()
        return self.lease

    def connect(self, port: str) -> None:
        self._require_lease()
        self.session.connect(port, lease=self.lease)

    def execute(self, stage_id: str, script: str, path=None) -> StageResult:
        if not stage_id:
            raise ValueError("stage_id is required")
        lease = self._require_lease()
        started_at_ns = time.monotonic_ns()
        start_frame_id = self._latest_frame_id()
        if self.cancel_event.is_set():
            return StageResult(
                self.run_id,
                self.epoch_id,
                self.context_revision,
                stage_id,
                StageOutcome.CANCELLED,
                started_at_ns,
                time.monotonic_ns(),
                start_frame_id,
                self._latest_frame_id(),
                "Automation stop requested.",
            )

        try:
            session_result = self.session.run(
                script,
                path,
                cancel=self.cancel_event,
                label_root=self.label_root,
                lease=lease,
            )
            outcome = {
                "completed": StageOutcome.COMPLETED,
                "cancelled": StageOutcome.CANCELLED,
                "aborted": StageOutcome.ABORTED,
            }.get(session_result, StageOutcome.FAILED)
            message = None if outcome == StageOutcome.COMPLETED else str(session_result)
        except Exception as exc:
            outcome = StageOutcome.FAILED
            message = str(exc)

        return StageResult(
            self.run_id,
            self.epoch_id,
            self.context_revision,
            stage_id,
            outcome,
            started_at_ns,
            time.monotonic_ns(),
            start_frame_id,
            self._latest_frame_id(),
            message,
        )

    def cancel(self) -> None:
        self.cancel_event.set()
        self.session.stop()

    def release(self) -> None:
        if self.lease is not None:
            self.lease.release()

    def _require_lease(self) -> ControllerTaskLease:
        if self.lease is None or self.lease.released:
            raise RuntimeError("自动动作执行前必须取得有效的伊机控任务租约。")
        return self.lease

    def _latest_frame_id(self) -> int | None:
        if self.frames is None:
            return None
        try:
            return self.frames.snapshot_frame().frame_id
        except RuntimeError:
            return None
