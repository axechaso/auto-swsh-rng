from __future__ import annotations

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from swsh_app.automation import AutomationConfig, AutomationRunStatus
from swsh_app.automation.simulation_session import DesktopSimulationSession


class DesktopSimulationSessionTests(unittest.TestCase):
    def test_cancel_before_start_does_not_spawn_calculator_or_acquire_a_lease(self):
        config = AutomationConfig(
            run_id="cancel-before-start",
            context_revision=0,
            scenario_id="test-simulation",
            min_advance=0,
            max_advance=1,
            search_request={},
            execution_mode="simulation",
        )
        session = DesktopSimulationSession(config, Path("missing-backend.exe"))
        session.cancel()

        result = session.run()

        self.assertEqual(result.status, AutomationRunStatus.CANCELLED)
        self.assertEqual(result.epochs, ())
        self.assertEqual(result.reason, "用户请求停止。")


if __name__ == "__main__":
    unittest.main()
