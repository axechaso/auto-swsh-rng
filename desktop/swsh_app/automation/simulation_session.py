"""One cancellable desktop simulation of seed measurement and M2 search."""
from __future__ import annotations

import secrets
import threading
import uuid

from ..backend import PROTOCOL_VERSION
from .desktop_calculator import CalculatorCancelled, DesktopJsonCalculator
from .models import AutomationConfig, AutomationPhase, AutomationRunResult, AutomationRunStatus
from .runner import AutomationRunner
from .seed_observer import SeedObserver
from .simulation import SimulationDevicePort
from .synthetic_replay import build_synthetic_seed_replay


class DesktopSimulationSession:
    """Build internally consistent synthetic observations, then run M2 as-is."""

    def __init__(self, config: AutomationConfig, backend, *, on_event=None, faults=None):
        if not isinstance(config, AutomationConfig):
            raise TypeError("config must be an AutomationConfig")
        if config.execution_mode != "simulation":
            raise ValueError("desktop simulation requires execution_mode='simulation'")
        self.config = config
        self.calculator = DesktopJsonCalculator(backend)
        self.on_event = on_event
        self.faults = dict(faults or {})
        self.cancel_event = threading.Event()
        self.runner: AutomationRunner | None = None

    def run(self):
        try:
            if self.cancel_event.is_set():
                raise CalculatorCancelled("simulation cancelled before startup")
            run_observations = f"{secrets.randbits(128):0128b}"
            solved = self.calculator.execute(
                self._request(
                    {"operation": "seed.solve", "observations": run_observations},
                    "synthetic-seed-source",
                ),
                self.cancel_event,
            )["data"]
            if (
                solved.get("observationCount") != 128
                or solved.get("boundarySemanticsVersion") != "retail-seed-observation-v1"
            ):
                raise ValueError("backend returned unsupported synthetic seed semantics")
            state = solved.get("stateAfterObservations")
            if not isinstance(state, dict) or not all(
                isinstance(state.get(part), str) and len(state[part]) == 16
                for part in ("seed0", "seed1")
            ):
                raise ValueError("backend returned an invalid synthetic seed state")
            predicted = self.calculator.execute(
                self._request({
                    "operation": "seed.verify",
                    "seed0": state["seed0"],
                    "seed1": state["seed1"],
                    "observations": "0" * 128,
                }, "synthetic-verify-preview"),
                self.cancel_event,
            )["data"].get("predictedObservations")
            if not isinstance(predicted, str) or len(predicted) != 128 or any(bit not in "01" for bit in predicted):
                raise ValueError("backend did not provide a valid next observation sequence")

            replay, classify = build_synthetic_seed_replay(
                run_observations,
                verification_observations=predicted,
                replay_id=f"synthetic-{self.config.run_id}",
            )
            try:
                device = SimulationDevicePort(frame_source=replay, faults=self.faults)
                observer = SeedObserver(
                    replay,
                    classify,
                    timeout_seconds=1,
                    poll_seconds=0.001,
                    clock=replay.clock,
                )
                self.runner = AutomationRunner(
                    self.config,
                    device=device,
                    observer=observer,
                    calculator=self.calculator,
                    on_event=self.on_event,
                    cancel_event=self.cancel_event,
                )
                return self.runner.run()
            finally:
                replay.close()
        except CalculatorCancelled:
            return AutomationRunResult(
                run_id=self.config.run_id,
                status=AutomationRunStatus.CANCELLED,
                phase=AutomationPhase.CLEANUP,
                epochs=(),
                reason="用户请求停止。",
            )

    def cancel(self):
        self.cancel_event.set()
        if self.runner is not None:
            self.runner.cancel()

    def _request(self, fields, epoch_id):
        return {
            **fields,
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": self.config.run_id,
            "epochId": epoch_id,
            "contextRevision": self.config.context_revision,
        }
