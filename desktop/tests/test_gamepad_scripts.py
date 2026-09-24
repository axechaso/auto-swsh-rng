import os
from pathlib import Path
import sys
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtCore import QEvent, Qt
from PySide6.QtGui import QFocusEvent
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from swsh_app.controller import ControllerSession, manual_report
from swsh_app.gamepad import Gamepad
from swsh_app.script_library import SCRIPT_DIR, apply_parameters, inspect_script, parameters
from swsh_app.vendor.easycon import EasyConScriptEngine
from swsh_app.vendor.easycon.device import NintendoSwitchDevice, SwitchButton, SwitchReport

APP = QApplication.instance() or QApplication([])


class GamepadTests(unittest.TestCase):
    def test_axes_diagonals_and_opposite_directions(self):
        report = manual_report({"A", "B", "TOP", "RIGHT", "LS_UP", "RS_LEFT"})
        self.assertEqual(report, SwitchReport(SwitchButton.A | SwitchButton.B, 1, 128, 0, 0, 128))
        self.assertEqual(manual_report({"TOP", "DOWN", "LEFT", "RIGHT", "LS_UP", "LS_DOWN"}), SwitchReport())

    def test_mouse_and_keyboard_share_key_without_early_release(self):
        pad = Gamepad()
        changes = []
        pad.changed.connect(changes.append)
        pad.show()
        pad.activateWindow()
        APP.processEvents()
        try:
            QTest.mousePress(pad.controls["A"], Qt.MouseButton.LeftButton)
            QTest.keyPress(pad, Qt.Key.Key_C)
            QTest.mouseRelease(pad.controls["A"], Qt.MouseButton.LeftButton)
            self.assertEqual(pad.pressed, {"A"})
            QTest.keyPress(pad, Qt.Key.Key_W)
            self.assertEqual(pad.pressed, {"A", "LS_UP"})
            QTest.keyRelease(pad, Qt.Key.Key_C)
            self.assertEqual(pad.pressed, {"LS_UP"})
            APP.sendEvent(pad, QFocusEvent(QEvent.Type.FocusOut))
            self.assertEqual(changes[-1], set())
            QTest.keyPress(pad, Qt.Key.Key_C)
            pad.hide()
            self.assertEqual(pad.pressed, set())
        finally:
            pad.close()

    def test_script_takes_over_after_manual_reports_and_clears_axes(self):
        device = NintendoSwitchDevice(report_interval=0)
        device.connect("mock")
        session = ControllerSession(lambda: None, device=device)
        try:
            session.manual({"A", "LS_UP"})
            session.run('PRINT "take over"')
            self.assertEqual(device.get_report(), SwitchReport())
            self.assertTrue(any(r.button == SwitchButton.A and r.ly == 0 for r in device.report_history))
            self.assertEqual(device.report_history[-1], SwitchReport())
        finally:
            device.disconnect()


class RecordingPad:
    def __init__(self):
        self.events = []

    def click_buttons(self, key, duration_ms, cancel_event=None):
        self.events.append(("click", key, duration_ms))

    def press_buttons(self, key):
        self.events.append(("down", key))

    def release_buttons(self, key):
        self.events.append(("up", key))

    def set_stick(self, key, x, y):
        self.events.append(("stick", key, x, y))

    def click_stick(self, key, x, y, duration_ms, cancel_event=None):
        self.events.append(("stick_click", key, x, y, duration_ms))


def simulate(name, overrides=None, getters=None):
    path = SCRIPT_DIR / (name + ".ecs")
    text = path.read_text(encoding="utf-8-sig")
    values = {p.name: p.default or "2" for p in parameters(text)}
    values.update(overrides or {})
    source = apply_parameters(text, values)
    program = EasyConScriptEngine().compile(source, source=str(path))
    labels = {key: (lambda: 100) for key in program.external_labels}
    labels["同步时间"] = lambda: 0
    labels.update(getters or {})
    pad, output, waits, points = RecordingPad(), [], [], []

    def trace(point):
        points.append(point)
        if len(points) > 30000:
            raise AssertionError("模拟超过步数上限，可能存在不退出的循环")

    program.run(gamepad=pad, external_getters=labels, output=output.append,
                waiter=lambda ms, cancel: waits.append(ms), trace=trace)
    return pad.events, output, waits, points


class ScriptLibraryTests(unittest.TestCase):
    def test_all_nine_scripts_compile_and_finish_with_matching_labels(self):
        paths = [p for p in SCRIPT_DIR.glob("*.ecs") if p.stem != "剑盾乱数统合"]
        self.assertEqual(len(paths), 9)
        for path in paths:
            with self.subTest(script=path.name):
                events, output, waits, points = simulate(path.stem)
                self.assertTrue(events)
                self.assertTrue(points)

    def test_seed_scripts_emit_128_unbroken_bits(self):
        for name in ("测种", "剑盾测种", "自行车", "雷雨自行车", "剑盾光速过帧"):
            with self.subTest(script=name):
                _, output, _, _ = simulate(name)
                bits = [x.strip() for x in output if len(x.strip()) == 128 and set(x.strip()) <= {"0", "1"}]
                self.assertEqual(bits, ["0" * 128])

    def test_in_place_one_frame_and_batch_boundaries_terminate(self):
        for count in (1, 2, 61, 62, 121):
            with self.subTest(count=count):
                _, output, _, _ = simulate("剑盾原地过帧", {"_过多少次": str(count)})
                self.assertEqual(sum(x.startswith("已过") for x in output), count - 1)

    def test_in_place_recovery_keeps_movement_delay(self):
        _, _, _, points = simulate("剑盾原地过帧", {"_过多少次": "62"}, {"现在的日期与时间": lambda: 0})
        source = (SCRIPT_DIR / "剑盾原地过帧.ecs").read_text(encoding="utf-8-sig").splitlines()
        waits = [p.duration_ms for p in points if source[p.location.line - 1].strip() == "WAIT $1" and p.duration_ms is not None]
        self.assertTrue(waits)
        self.assertEqual(set(waits), {530})

    def test_precision_negative_date_delta_runs_up_loop(self):
        events, _, _, _ = simulate("精准过帧", {"_过几帧": "10", "_雷雨雨天乱数": "1", "_当前月": "8", "_雷雨月": "8", "_当前日": "1", "_雷雨日": "5", "_需要保存": "0", "_撞帧方向": "0"})
        self.assertEqual(sum(e[:2] == ("click", "UP") for e in events), 4)

    def test_bad_parameters_rejected_and_originals_unchanged(self):
        path = SCRIPT_DIR / "精准过帧.ecs"
        source = path.read_text(encoding="utf-8-sig")
        for values in ({}, {"_过几帧": "0"}, {"_过几帧": "5", "_需要预留": "1"}, {"_过几帧": "5", "_当前月": "13"}):
            with self.assertRaises(ValueError):
                apply_parameters(source, values)
        original = (SCRIPT_DIR / "originals/剑盾原地过帧.txt").read_text(encoding="utf-8-sig")
        self.assertNotIn("$5 = 0", original)

    def test_missing_labels_are_reported_before_buttons(self):
        path = SCRIPT_DIR / "剑盾测种.ecs"
        program, _, missing = inspect_script(path.read_text(encoding="utf-8-sig"), path)
        self.assertEqual(missing, ["YY", "铁甲物理"])
        device = NintendoSwitchDevice(report_interval=0)
        device.connect("mock")
        session = ControllerSession(lambda: None, device=device)
        try:
            with self.assertRaisesRegex(RuntimeError, "缺少搜图模板"):
                session.run(path.read_text(encoding="utf-8-sig"), path)
            self.assertTrue(all(r == SwitchReport() for r in device.report_history))
        finally:
            device.disconnect()


if __name__ == "__main__":
    unittest.main()
