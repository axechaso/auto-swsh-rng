import json
import os
import sys
import tempfile
import time
import unittest
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from swsh_app.backend import JsonJob, find_backend
from swsh_app.storage import SettingsStore, export_csv, seed_hex
from swsh_app.window import SwshWindow

APP = QApplication.instance() or QApplication([])


def wait_until(predicate, timeout=20):
    deadline = time.monotonic() + timeout
    while not predicate() and time.monotonic() < deadline:
        APP.processEvents()
        QTest.qWait(10)
    if not predicate():
        raise AssertionError("Qt operation timed out")


class StorageTests(unittest.TestCase):
    def test_seed_precision_and_invalid_values(self):
        self.assertEqual(seed_hex("0xffffffffffffffff"), "FFFFFFFFFFFFFFFF")
        for value in ("", "-1", "G", "10000000000000000"):
            with self.assertRaises(ValueError):
                seed_hex(value)

    def test_atomic_save_roundtrip_and_corrupt_file_preserved(self):
        with tempfile.TemporaryDirectory() as tmp:
            store = SettingsStore(Path(tmp))
            data = {"version": 1, "profiles": {"盾": {"game": "Shield", "tid": 65535, "sid": 1, "shinyCharm": True, "markCharm": False}}}
            store.save(data)
            self.assertEqual(store.load(), data)
            self.assertEqual(list(Path(tmp).glob("*.tmp")), [])
            store.path.write_text("broken", encoding="utf-8")
            with self.assertRaises(ValueError):
                store.load()
            self.assertEqual(store.path.read_text(), "broken")


class WindowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.window = SwshWindow(Path(self.temp.name), auto_load=False)
        self.window.show()
        APP.processEvents()

    def tearDown(self):
        self.window.close()
        wait_until(lambda: not self.window.jobs)
        APP.processEvents()
        self.window.deleteLater()
        APP.processEvents()
        self.temp.cleanup()

    def test_invalid_search_never_starts_job(self):
        self.window.catalog_ready = True
        self.window.search()
        self.assertIsNone(self.window.active_job)
        self.assertIn("Seed", self.window.error_label.text())
        self.window.load_example()
        self.window.start.setValue(20)
        self.window.end.setValue(10)
        self.window.search()
        self.assertIn("结束推进", self.window.error_label.text())
        self.window.end.setValue(100)
        self.window.ivs[0][0].setValue(31)
        self.window.ivs[0][1].setValue(0)
        self.window.search()
        self.assertIn("个体值", self.window.error_label.text())

    def test_profile_save_and_explicit_reload(self):
        self.window.profile_name.setText("我的存档")
        self.window.tid.setValue(65535)
        self.window.sid.setValue(4321)
        self.window.shiny_charm.setChecked(True)
        self.window.save_profile()
        other = SwshWindow(Path(self.temp.name), auto_load=False)
        self.assertEqual(other.tid.value(), 65535)
        self.assertEqual(other.sid.value(), 4321)
        self.assertTrue(other.shiny_charm.isChecked())
        self.assertEqual(other.jobs, set())
        other.close()
        other.deleteLater()

    def test_missing_backend_releases_busy_state(self):
        self.window.backend = Path(self.temp.name) / "missing.exe"
        self.window.tool_seed0.setText("1")
        self.window.tool_seed1.setText("2")
        self.window.calculate_rng()
        wait_until(lambda: not self.window.jobs)
        self.assertTrue(self.window.rng_button.isEnabled())
        self.assertFalse(self.window.stop_button.isEnabled())
        self.assertIn("无法启动", self.window.error_label.text())


@unittest.skipUnless(find_backend(), "Set SWSH_RNG_BACKEND to the built CLI for integration tests")
class IntegrationTests(WindowTests):
    def ready(self):
        self.window.backend = find_backend()
        self.window.refresh_catalog()
        wait_until(lambda: not self.window.jobs)
        self.assertTrue(self.window.catalog_ready, self.window.error_label.text())
        self.window.load_example()

    def test_search_sort_export_and_seed_roundtrip(self):
        self.ready()
        self.window.end.setValue(30)
        self.window.search()
        wait_until(lambda: not self.window.jobs)
        rows = self.window.model.rows
        self.assertGreater(len(rows), 0, self.window.error_label.text())
        self.window.proxy.sort(0, Qt.SortOrder.DescendingOrder)
        self.window.table.selectRow(0)
        APP.processEvents()
        self.assertEqual(self.window.selected_row()["advance"], max(r["advance"] for r in rows))
        output = Path(self.temp.name) / "result.csv"
        export_csv(output, rows)
        self.assertTrue(output.read_bytes().startswith(b"\xef\xbb\xbf"))
        self.assertIn(rows[0]["pid"], output.read_text(encoding="utf-8-sig"))
        self.window.read_search_seeds()
        self.window.amount.setValue(3)
        self.window.calculate_rng()
        wait_until(lambda: not self.window.jobs)
        result = self.window.rng_result
        self.window.tool_seed0.setText(result["seed0"])
        self.window.tool_seed1.setText(result["seed1"])
        self.window.direction.setCurrentIndex(1)
        self.window.calculate_rng()
        wait_until(lambda: not self.window.jobs)
        self.assertEqual(self.window.rng_result["seed0"], "123456789ABCDEF0")
        self.assertEqual(self.window.rng_result["seed1"], "0FEDCBA987654321")

    def test_cancel_then_search_again_and_close(self):
        self.ready()
        self.window.end.setValue(99999)
        self.window.search()
        self.window.cancel_search()
        wait_until(lambda: not self.window.jobs)
        self.assertTrue(self.window.search_button.isEnabled())
        self.window.end.setValue(5)
        self.window.search()
        wait_until(lambda: not self.window.jobs)
        self.assertGreater(len(self.window.model.rows), 0)
        self.window.search()
        self.window.close()
        wait_until(lambda: not self.window.jobs)

    def test_rapid_catalog_changes_keep_latest_selection(self):
        self.ready()
        self.window.kind.setCurrentIndex(1)
        self.window.kind.setCurrentIndex(2)
        self.window.kind.setCurrentIndex(3)
        wait_until(lambda: not self.window.jobs)
        self.assertTrue(self.window.catalog_ready)
        self.assertEqual(self.window.kind.currentData(), "Fishing")
        self.window.end.setValue(5)
        self.window.search()
        wait_until(lambda: not self.window.jobs)
        self.assertGreater(len(self.window.model.rows), 0, self.window.error_label.text())


if __name__ == "__main__":
    unittest.main()
