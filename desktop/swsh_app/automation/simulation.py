"""Non-hardware device port for deterministic replay and fault injection."""
from __future__ import annotations

from dataclasses import dataclass
import threading
import uuid


class SimulationFault(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class SimulationTaskLease:
    run_id: str
    token: str


class SimulationDevicePort:
    """Implements the runner device port without opening a physical device."""

    execution_mode = "simulation"

    def __init__(self, *, frame_source, faults=None, drop_frame_ids=()):
        if frame_source is None:
            raise ValueError("frame_source is required")
        self.frame_source = frame_source
        self.faults = dict(faults or {})
        self.drop_frame_ids = set(drop_frame_ids)
        self.events = []
        self.restart_count = 0
        self._lease = None
        self._cancelled = threading.Event()
        self._lock = threading.Lock()

    def acquire_task_lease(self):
        with self._lock:
            if self._lease is not None:
                raise SimulationFault("a simulated task already owns the device port")
            run_id = str(self.faults.get("run_id", "simulation-run"))
            self._lease = SimulationTaskLease(run_id, uuid.uuid4().hex)
            self.events.append(("lease_acquired", self._lease.token))
            return self._lease

    def preflight(self, config, lease, cancel_event):
        self._require_lease(lease)
        self._check("preflight")
        self._check_cancel(cancel_event)
        self.events.append(("preflight", config.scenario_id))

    def restore_scene(self, config, lease, epoch_id, *, after_restart, cancel_event):
        self._require_lease(lease)
        self._check("restore_scene")
        self._check_cancel(cancel_event)
        self.events.append(("restore_scene", epoch_id, after_restart))
        return self.faults.get("restore_result", True)

    def trigger_seed_bit(self, config, lease, epoch_id, purpose, index, cancel_event):
        self._require_lease(lease)
        self._check_cancel(cancel_event)
        self._check("trigger_seed_bit")
        self.events.append(("trigger_seed_bit", epoch_id, purpose, index))
        if self.faults.get("stale_frame_once"):
            self.faults["stale_frame_once"] = False
            self.frame_source.trigger(purpose, index, drop_frames=True)
            return
        self.frame_source.trigger(purpose, index)
        frame_id = self.faults.pop("drop_next_frame_id", None)
        if frame_id is not None:
            self.frame_source.drop_frame_once(frame_id)
        elif self.drop_frame_ids:
            self.frame_source.drop_frame_once(self.drop_frame_ids.pop())

    def restart_game(self, config, lease, epoch_id, cancel_event):
        self._require_lease(lease)
        self._check("restart_game")
        self._check_cancel(cancel_event)
        self.restart_count += 1
        self.events.append(("restart_game", epoch_id))
        return self.faults.get("restart_result", True)

    def cancel(self):
        self._cancelled.set()
        self.events.append(("cancel",))

    def stop_scripts(self):
        self.events.append(("stop_scripts",))

    def release_task_lease(self, lease):
        self._require_lease(lease)
        self._check("release_task_lease")
        with self._lock:
            self._lease = None
        self.events.append(("lease_released", lease.token))

    def _require_lease(self, lease):
        if lease is None or lease is not self._lease:
            raise SimulationFault("simulation action requires the active task lease")

    def _check(self, point):
        remaining = self.faults.get(point)
        if remaining:
            if isinstance(remaining, int) and not isinstance(remaining, bool):
                if remaining <= 1:
                    self.faults.pop(point, None)
                else:
                    self.faults[point] = remaining - 1
            raise SimulationFault(f"injected simulation fault: {point}")

    def _check_cancel(self, cancel_event):
        if self._cancelled.is_set() or cancel_event.is_set():
            raise SimulationFault("simulated action cancelled")
