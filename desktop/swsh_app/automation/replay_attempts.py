"""Replay-backed action port for M4 attempt execution."""
from __future__ import annotations

import threading

from .replay import ReplayFrameSource
from .stage_executor import StageOutcome, StageResult


class ReplayAttemptActionPort:
    """Drive M4 through an ordered replay manifest without touching hardware."""

    def __init__(self, frame_source: ReplayFrameSource, *, run_id: str,
                 epoch_id: str, context_revision: int = 0, faults=None):
        if not isinstance(frame_source, ReplayFrameSource):
            raise TypeError("frame_source must be a ReplayFrameSource")
        if not run_id or not epoch_id:
            raise ValueError("run_id and epoch_id are required")
        if isinstance(context_revision, bool) or not isinstance(context_revision, int) or context_revision < 0:
            raise ValueError("context_revision must be a non-negative integer")
        self.frame_source = frame_source
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.faults = dict(faults or {})
        self.events = []
        self._coarse_index = 0
        self._precise_index = 0
        self._final_index = 0
        self._relocation_index = 0
        self._cancelled = threading.Event()

    def execute_coarse_batch(self, stage_id: str, advances: int, cancel_event: threading.Event) -> StageResult:
        index = self._coarse_index
        self._coarse_index += 1
        return self._execute_stage("coarse_batch", index, stage_id, advances, cancel_event)

    def execute_precise_advances(self, stage_id: str, advances: int,
                                 cancel_event: threading.Event) -> StageResult:
        index = self._precise_index
        self._precise_index += 1
        return self._execute_stage("precise_advances", index, stage_id, advances, cancel_event)

    def trigger_relocation_bit(self, index: int, cancel_event: threading.Event) -> None:
        if self._is_cancelled(cancel_event):
            raise RuntimeError("replay relocation was cancelled")
        replay_index = self._relocation_index
        self._relocation_index += 1
        self._maybe_fail("relocation_bit")
        self.frame_source.trigger("relocation_bit", replay_index)
        self.events.append({"kind": "relocation_bit", "index": index, "replayIndex": replay_index})

    def execute_final_trigger(self, plan, cancel_event: threading.Event) -> StageResult:
        index = self._final_index
        self._final_index += 1
        return self._execute_stage("final_trigger", index, "final-trigger", plan, cancel_event)

    def stop_scripts(self) -> None:
        self._cancelled.set()
        self.events.append({"kind": "stop_scripts"})

    def _execute_stage(self, purpose, index, stage_id, payload, cancel_event):
        started_at_ns = self.frame_source.clock.monotonic_ns()
        try:
            if self._is_cancelled(cancel_event):
                return self._stage_result(stage_id, StageOutcome.CANCELLED, started_at_ns,
                                          self._latest_frame_id(), message="replay action was cancelled")
            self._maybe_fail(purpose)
            start_frame_id = self._latest_frame_id()
            self.frame_source.trigger(purpose, index)
            consumed = self.frame_source.consume_active_segment()
            end_frame_id = self._latest_frame_id()
            self.events.append({
                "kind": purpose,
                "index": index,
                "stageId": stage_id,
                "payload": payload if isinstance(payload, int) else None,
                "startFrameId": start_frame_id,
                "endFrameId": end_frame_id,
                "consumedFrameIds": [frame.frame_id for frame in consumed],
            })
            return self._stage_result(stage_id, StageOutcome.COMPLETED, started_at_ns,
                                      start_frame_id, end_frame_id)
        except Exception as exc:
            return self._stage_result(stage_id, StageOutcome.FAILED, started_at_ns,
                                      self._latest_frame_id(), message=str(exc))

    def _stage_result(self, stage_id, outcome, started_at_ns, start_frame_id,
                      end_frame_id=None, message=None):
        return StageResult(
            self.run_id,
            self.epoch_id,
            self.context_revision,
            stage_id,
            outcome,
            started_at_ns,
            self.frame_source.clock.monotonic_ns(),
            start_frame_id,
            end_frame_id,
            message,
        )

    def _latest_frame_id(self):
        return self.frame_source.snapshot_frame().frame_id

    def _is_cancelled(self, cancel_event):
        return self._cancelled.is_set() or cancel_event.is_set()

    def _maybe_fail(self, purpose):
        remaining = self.faults.get(purpose)
        if not remaining:
            return
        if isinstance(remaining, int) and not isinstance(remaining, bool):
            if remaining <= 1:
                self.faults.pop(purpose, None)
            else:
                self.faults[purpose] = remaining - 1
        raise RuntimeError(f"injected replay action fault: {purpose}")
