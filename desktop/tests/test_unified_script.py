import itertools
import os
from pathlib import Path
import sys
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtWidgets import QApplication
from swsh_app.capture import FrameStore
from swsh_app.controller import ControllerSession
from swsh_app.easycon_panel import EasyConPanel
from swsh_app.script_library import SCRIPT_DIR, apply_parameters, inspect_script, parameters
from swsh_app.unified_script import NAME
from swsh_app.vendor.easycon import EasyConScriptEngine
from test_gamepad_scripts import RecordingPad

APP = QApplication.instance() or QApplication([])
PATH = SCRIPT_DIR / NAME
SOURCE = PATH.read_text(encoding="utf-8-sig")


def prepared(mode, **values):
    return apply_parameters(SOURCE, {"_功能": str(mode), "_过帧数": "2", "_自行车次数": "2", **values})


def execute(text, getters=None):
    program = EasyConScriptEngine().compile(text)
    labels = {key: (lambda: 100) for key in program.external_labels}
    labels["同步时间"] = lambda: 0
    labels.update(getters or {})
    pad, output, trace, waits = RecordingPad(), [], [], []
    def observe(point):
        trace.append(point)
        if len(trace) > 80000:
            raise AssertionError("统合脚本未在预期步数内退出")
    result = program.run(gamepad=pad, external_getters=labels, output=output.append,
                         waiter=lambda ms, cancel: waits.append(ms), trace=observe)
    if any("当前功能已中止" in s for s in output):
        assert result == 0, "中止状态必须传给桌面界面"
    elif any("当前功能完成" in s for s in output):
        assert result == 1, "成功状态必须传给桌面界面"
    return pad.events, output, trace, waits


class UnifiedScriptTests(unittest.TestCase):
    def test_nine_modes_finish_and_seed_modes_output_one_sequence(self):
        for mode in range(1, 10):
            with self.subTest(mode=mode):
                events, output, _, _ = execute(prepared(mode))
                self.assertTrue(events)
                self.assertIn("当前功能完成。\n", output)
                bits = [s.strip() for s in output if len(s.strip()) == 128 and set(s.strip()) <= {"0", "1"}]
                self.assertEqual(bits, ["0" * 128] if mode in (1, 2, 4, 5, 6, 7) else [])

    def test_help_runs_without_device_capture_or_labels(self):
        session = ControllerSession(lambda: self.fail("帮助不应请求采集画面"))
        self.assertEqual(session.run(SOURCE, PATH), "completed")
        program, _, missing = inspect_script(SOURCE, PATH)
        self.assertFalse(program.has_gamepad_actions)
        self.assertFalse(program.requires_image_search)
        self.assertEqual(missing, [])

    def test_dependencies_follow_selected_mode_and_post_seed_flag(self):
        expected = {1: {"YY", "铁甲物理"}, 2: {"YY", "铁甲物理"},
                    3: {"YY", "联网", "匹配中", "匹配成功"},
                    6: {"YY", "铁甲物理"}, 7: {"YY", "结束露营", "铁甲物理"},
                    8: set(), 9: set()}
        for mode, labels in expected.items():
            with self.subTest(mode=mode):
                program, _, _ = inspect_script(prepared(mode), PATH)
                self.assertEqual(program.external_labels, labels)
        for mode in (4, 5, 6, 7):
            program, _, _ = inspect_script(prepared(mode, _推进后测种="0"), PATH)
            self.assertNotIn("铁甲物理", program.external_labels)

    def test_edited_dynamic_conditions_keep_dependencies(self):
        text = SOURCE + '\n$开关 = 1\nIF $开关 == 1\n    $分数 = @新增标签\nENDIF\n'
        program, _, missing = inspect_script(text, PATH)
        self.assertIn("新增标签", program.external_labels)
        self.assertIn("新增标签", missing)

    def test_progress_boundaries_and_recovery_do_not_change_delays(self):
        for mode in (4, 5):
            for count in (1, 2, 50, 51, 52, 60, 61, 62, 121):
                with self.subTest(mode=mode, count=count):
                    text = prepared(mode, _过帧数=str(count), _推进后测种="0")
                    _, output, trace, _ = execute(text, {"现在的日期与时间": lambda: 0})
                    self.assertEqual(sum(s.startswith("循环推进：") for s in output), count - 1)
                    lines = text.splitlines()
                    if mode == 5 and count == 121:
                        durations = [p.duration_ms for p in trace if lines[p.location.line - 1].strip() == "WAIT _原地移动延迟" and p.duration_ms is not None]
                        self.assertEqual(set(durations), {530})

    def test_timeouts_stop_without_falling_through_to_next_stage(self):
        cases = [(1, "YY"), (2, "YY"), (3, "联网"), (3, "匹配中"), (3, "匹配成功"),
                 (4, "同步时间"), (4, "时区2"), (7, "结束露营")]
        for mode, label in cases:
            with self.subTest(mode=mode, label=label):
                getters = {label: (lambda: 100) if label == "同步时间" else (lambda: 0)}
                if label == "时区2":
                    getters.update({"现在的日期与时间": lambda: 0, "时区": lambda: 0})
                events, output, _, _ = execute(prepared(mode, _识别次数上限="2", _光速判定间隔="1", _过帧数="3"), getters)
                self.assertTrue(any("中止" in s for s in output))
                self.assertFalse(any(e[:2] == ("click", "RCLICK") for e in events))
                if mode == 3:
                    self.assertNotIn(("down", "HOME"), events)

    def test_seed_bits_keep_mixed_readings_and_all_one_warning(self):
        readings = itertools.cycle([100, 0])
        _, output, _, _ = execute(prepared(1), {"铁甲物理": lambda: next(readings)})
        self.assertIn("01" * 64 + "\n", output)
        _, output, _, _ = execute(prepared(2), {"铁甲物理": lambda: 0})
        self.assertIn("1" * 128 + "\n", output)
        self.assertTrue(any("几乎全为1" in s for s in output))

    def test_bike_multiplier_and_precision_reserve(self):
        for mode, multiplier in ((6, 6), (7, 10)):
            events, _, _, _ = execute(prepared(mode, _自行车次数="1", _推进后测种="0"))
            self.assertEqual(sum(e[:2] == ("click", "PLUS") for e in events), multiplier)
        events, _, _, _ = execute(prepared(8, _过帧数="50", _需要预留="1", _雷雨雨天乱数="1", _撞帧方向="0", _需要保存="0"))
        self.assertEqual(sum(e[:2] == ("click", "RCLICK") for e in events), 6)

    def test_negative_date_differences_and_party_positions(self):
        for delta, direction in ((-4, "UP"), (3, "DOWN"), (0, "UP")):
            events, _, _, _ = execute(SOURCE + f"\nCALL 日期数值({delta}, 200)\n")
            self.assertEqual(events, [("click", direction, 200)] * abs(delta))
        for position in (1, 2, 6):
            events, _, _, _ = execute(prepared(9, _迷人身躯=str(position)))
            self.assertEqual(sum(e[:2] == ("click", "UP") for e in events), max(0, position - 1))
            self.assertEqual(sum(e[:2] == ("click", "DOWN") for e in events), max(0, position - 1))

    def test_invalid_parameters_are_rejected_before_buttons(self):
        for mode, overrides in [(4, {"_过帧数": "0"}), (6, {"_自行车次数": "0"}),
                                (8, {"_需要预留": "1"}), (5, {"_原地移动延迟": "0"}),
                                (4, {"_光速判定间隔": "0"}), (9, {"_迷人身躯": "7"})]:
            with self.subTest(mode=mode, overrides=overrides):
                with self.assertRaises(ValueError):
                    prepared(mode, **overrides)
        # A standalone ECS host still receives a no-button refusal when counts
        # have not been filled; it does not depend on the Python parameter form.
        raw = SOURCE.replace("_功能 = 0\n", "_功能 = 4\n")
        events, output, _, _ = execute(raw)
        self.assertFalse(events)
        self.assertTrue(any("过帧数" in s for s in output))

    def test_form_selects_mode_and_preserves_edits(self):
        errors = []
        panel = EasyConPanel(FrameStore(), {}, lambda _: None, errors.append)
        try:
            self.assertEqual(panel.library.count(), 1)
            mode = panel.parameter_inputs["_功能"]
            mode.setCurrentIndex(mode.findData("8"))
            self.assertTrue(panel.parameters_form.isRowVisible(panel.parameter_inputs["_过帧数"]))
            self.assertFalse(panel.parameters_form.isRowVisible(panel.parameter_inputs["_原地移动延迟"]))
            panel.parameter_inputs["_过帧数"].setText("10")
            panel.editor.appendPlainText("# 保留当前参数")
            text = panel.prepared_text()
            self.assertIn("_功能 = 8", text)
            self.assertIn("_过帧数 = 10", text)
            panel.check_script()
            self.assertIn("图像依赖：无", panel.script_status.text())
            panel.script_finished("aborted")
            self.assertIn("未完成", panel.script_status.text())
            self.assertEqual(errors, [])
        finally:
            panel.close()
            panel.deleteLater()
            APP.processEvents()


if __name__ == "__main__":
    unittest.main()
