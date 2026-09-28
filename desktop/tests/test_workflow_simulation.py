from __future__ import annotations

import os
import json
from pathlib import Path
import sys
import tempfile
import time
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from swsh_app.backend import find_backend
from swsh_app.window import SwshWindow


APP = QApplication.instance() or QApplication([])


def wait_until(predicate, timeout=60):
    deadline = time.monotonic() + timeout
    while not predicate() and time.monotonic() < deadline:
        APP.processEvents()
        QTest.qWait(10)
    if not predicate():
        raise AssertionError("automation simulation timed out")


@unittest.skipUnless(find_backend(), "Set SWSH_RNG_BACKEND to the built CLI for integration tests")
class WorkflowSimulationTests(unittest.TestCase):
    def make_window(self, data_dir):
        window = SwshWindow(data_dir, backend=find_backend(), auto_load=False)
        window.catalog_loaded({
            "areas": ["Route 1"], "area": "Route 1",
            "weathers": ["Normal Weather"], "weather": "Normal Weather",
            "species": ["Skwovet"],
        })
        return window

    def test_default_workflow_runs_one_synthetic_seed_and_search_epoch(self):
        with tempfile.TemporaryDirectory() as temporary:
            window = self.make_window(Path(temporary))
            window.end.setValue(500)
            self.assertTrue(window.nav["workflow"].isChecked())

            window.workflow_panel.simulation_start_button.click()

            wait_until(lambda: window.workflow_panel.simulation_task is None)
            result = window.workflow_panel.simulation_result
            self.assertIsNotNone(result, window.workflow_panel.simulation_status.text())
            self.assertTrue(result.epochs, f"{result.status}: {result.reason}")
            self.assertEqual(result.total_epochs, 1)
            self.assertEqual(result.epochs[0].observation_bits, 128)
            self.assertEqual(result.epochs[0].verification_bits, 128)
            self.assertEqual(result.epochs[0].searched_start, 0)
            self.assertEqual(result.epochs[0].searched_end, 500)
            self.assertEqual(result.status, "targets_found", result.reason)
            report = window.workflow_panel.simulation_report_data()
            self.assertEqual(report["schema"], "auto-swsh-automation-run-report")
            self.assertEqual(report["outcomeType"], "simulation_search_only")
            self.assertEqual(report["sourceKind"], "synthetic")
            self.assertEqual(len(report["algorithmCommit"]), 40)
            self.assertTrue(report["scriptRevision"].startswith("pyside6-"))
            self.assertEqual(report["scriptRevision"], window.workflow_panel.simulation_script_revision)
            self.assertEqual(report["hardwareStatus"], "PendingHardwareValidation")
            self.assertIn("同一配套计算后端", "\n".join(report["limitations"]))
            schema = json.loads((Path(__file__).resolve().parents[2] / "docs/schemas/automation-run-report.schema.json").read_text(encoding="utf-8"))
            self.assertEqual(set(schema["required"]), set(report))
            self.assertIn(report["phase"], schema["properties"]["phase"]["enum"])
            self.assertIn(report["status"], schema["properties"]["status"]["enum"])
            report_path = Path(temporary) / "simulation-report.json"
            self.assertTrue(window.workflow_panel.save_simulation_report(report_path, report))
            self.assertEqual(json.loads(report_path.read_text(encoding="utf-8"))["runId"], result.run_id)
            self.assertFalse(window.workflow_panel.formal_start_button.isEnabled())
            self.assertFalse(window.workflow_panel.simulation_stop_button.isEnabled())
            self.assertTrue(window.workflow_panel.simulation_start_button.isEnabled())
            window.close()
            APP.processEvents()
            window.deleteLater()

    def test_stop_cancels_the_offline_run_and_waits_for_thread_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            window = self.make_window(Path(temporary))
            window.workflow_panel.simulation_start_button.click()
            window.workflow_panel.stop_simulation()

            wait_until(lambda: window.workflow_panel.simulation_task is None)

            result = window.workflow_panel.simulation_result
            self.assertIsNotNone(result)
            self.assertEqual(result.status, "cancelled")
            self.assertEqual(result.epochs, ())
            self.assertTrue(window.workflow_panel.simulation_start_button.isEnabled())
            self.assertFalse(window.workflow_panel.simulation_stop_button.isEnabled())
            window.close()
            APP.processEvents()
            window.deleteLater()

    def test_preflight_fault_stops_before_seed_measurement_and_releases_run_controls(self):
        with tempfile.TemporaryDirectory() as temporary:
            window = self.make_window(Path(temporary))
            window.workflow_panel.simulation_fault.setCurrentIndex(
                window.workflow_panel.simulation_fault.findData("preflight")
            )
            window.workflow_panel.simulation_start_button.click()

            wait_until(lambda: window.workflow_panel.simulation_task is None)

            result = window.workflow_panel.simulation_result
            self.assertIsNotNone(result)
            self.assertEqual(result.status, "needs_attention")
            self.assertEqual(result.epochs, ())
            self.assertIn("PREFLIGHT_FAILED", result.reason)
            self.assertTrue(window.workflow_panel.simulation_start_button.isEnabled())
            self.assertFalse(window.workflow_panel.simulation_stop_button.isEnabled())
            self.assertFalse(window.workflow_panel.formal_start_button.isEnabled())
            window.close()
            APP.processEvents()
            window.deleteLater()

    def test_lost_seed_frame_is_reported_unknown_instead_of_verified(self):
        with tempfile.TemporaryDirectory() as temporary:
            window = self.make_window(Path(temporary))
            window.workflow_panel.simulation_fault.setCurrentIndex(
                window.workflow_panel.simulation_fault.findData("drop_first_frame")
            )
            window.workflow_panel.simulation_start_button.click()

            wait_until(lambda: window.workflow_panel.simulation_task is None)

            result = window.workflow_panel.simulation_result
            self.assertIsNotNone(result)
            self.assertEqual(result.status, "needs_attention")
            self.assertEqual(result.epochs, ())
            self.assertIn("SEED_OBSERVATION_UNKNOWN", result.reason)
            self.assertFalse(window.workflow_panel.formal_start_button.isEnabled())
            window.close()
            APP.processEvents()
            window.deleteLater()


if __name__ == "__main__":
    unittest.main()
