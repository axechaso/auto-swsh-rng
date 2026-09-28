import os
from pathlib import Path
import sys
import threading
import time
import unittest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from PySide6.QtCore import QPoint, Qt
from PySide6.QtGui import QColor, QImage
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from swsh_app.capture import FrameStore
from swsh_app.controller import ControllerSession
from swsh_app.ocr_panel import RegionPreview
from swsh_app.ocr_worker import crop_bounds, parse_prediction
from swsh_app.automation import (
    AnimationFramePhase,
    AnimationFrameScore,
    AutomationStageExecutor,
    SeedObserver,
    StageOutcome,
)
from swsh_app.vendor.easycon import EasyConScriptEngine
from swsh_app.vendor.easycon.device import NintendoSwitchDevice, SwitchButton, SwitchReport

APP = QApplication.instance() or QApplication([])


class ControllerTests(unittest.TestCase):
    def setUp(self):
        self.device = NintendoSwitchDevice(report_interval=0)
        self.assertTrue(self.device.connect("mock"))
        self.logs = []
        self.session = ControllerSession(lambda: None, device=self.device, log=self.logs.append)

    def tearDown(self):
        self.session.stop()
        self.device.disconnect()

    def test_protocol_vectors_match_reference(self):
        self.assertEqual(SwitchReport().to_bytes(), bytes.fromhex("00 00 01 08 04 02 01 80"))
        self.assertEqual(SwitchReport(button=SwitchButton.A).to_bytes(), bytes.fromhex("00 01 01 08 04 02 01 80"))

    def test_script_executes_loop_and_releases_held_keys(self):
        result = self.session.run('FOR $i = 1 TO 2\nA 30\nNEXT\nB DOWN\nPRINT "done"')
        self.assertEqual(result, "completed")
        self.assertIn("done\n", self.logs)
        self.assertEqual(self.device.get_report(), SwitchReport())
        self.assertTrue(any(r.button == SwitchButton.A for r in self.device.report_history))

    def test_cancel_blocks_manual_inputs_and_releases(self):
        completed = []
        thread = threading.Thread(target=lambda: completed.append(self.session.run("A DOWN\nWAIT 10000")))
        thread.start()
        try:
            deadline = time.monotonic() + 2
            while self.device.get_report().button == 0 and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertEqual(self.device.get_report().button, SwitchButton.A)
            with self.assertRaisesRegex(RuntimeError, "其他操作"):
                self.session.press("B")
            self.session.stop()
            thread.join(2)
            self.assertFalse(thread.is_alive())
            self.assertEqual(completed, ["cancelled"])
            self.assertEqual(self.device.get_report(), SwitchReport())
        finally:
            self.session.stop()
            thread.join(2)

    def test_failure_and_pre_cancel_do_not_leave_keys_held(self):
        with self.assertRaises(Exception):
            self.session.run("A DOWN\n$value = 1 / 0")
        self.assertEqual(self.device.get_report(), SwitchReport())
        event = threading.Event()
        event.set()
        self.assertEqual(self.session.run("A 50", cancel=event), "cancelled")
        self.assertEqual(self.device.get_report(), SwitchReport())

    def test_disconnected_key_script_is_rejected_but_print_runs(self):
        self.session.disconnect()
        with self.assertRaisesRegex(RuntimeError, "先连接"):
            self.session.run("A")
        self.assertEqual(self.session.run('PRINT "offline"'), "completed")

    def test_task_lease_blocks_manual_and_competing_controller_actions(self):
        lease = self.session.acquire_task_lease()
        try:
            with self.assertRaisesRegex(RuntimeError, "自动任务占用"):
                self.session.press("B", 10)
            with self.assertRaisesRegex(RuntimeError, "自动任务占用"):
                self.session.acquire_task_lease()
            self.assertEqual(
                self.session.run('PRINT "leased"', lease=lease),
                "completed",
            )
        finally:
            lease.release()

        self.session.press("A", 10)
        self.assertEqual(self.device.get_report(), SwitchReport())

    def test_stage_executor_returns_identity_and_frame_boundaries(self):
        frames = FrameStore()
        image = QImage(4, 4, QImage.Format.Format_RGB32)
        image.fill(QColor(10, 20, 30))
        before = frames.put(image)
        executor = AutomationStageExecutor(
            self.session,
            run_id="run-1",
            epoch_id="epoch-2",
            context_revision=3,
            frames=frames,
        )
        executor.acquire()
        try:
            result = executor.execute("diagnostic", 'PRINT "stage"')
        finally:
            executor.release()

        self.assertEqual(result.outcome, StageOutcome.COMPLETED)
        self.assertEqual((result.run_id, result.epoch_id, result.context_revision), ("run-1", "epoch-2", 3))
        self.assertEqual(result.stage_id, "diagnostic")
        self.assertEqual(result.start_frame_id, before.frame_id)
        self.assertEqual(result.end_frame_id, before.frame_id)
        self.assertGreaterEqual(result.finished_at_ns, result.started_at_ns)


class CaptureAndOcrTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == "win32", "The bundled EasyCon DLL targets Windows")
    def test_bundled_tesseract_loads_language_data_and_reads_digits(self):
        import cv2
        import numpy as np
        from swsh_app.vendor.easycon.tesseract import read_tesseract
        # Fixed pixels: font/rasterizer versions must not change the OCR input.
        data = np.fromfile(Path(__file__).parent / "fixtures/ocr-digits.png", dtype=np.uint8)
        frame = cv2.imdecode(data, cv2.IMREAD_COLOR)
        text, confidence = read_tesseract(frame)
        self.assertIn("123", text)
        self.assertGreater(confidence, 0.8)

    def test_shared_frame_copy_channels_and_stale_frame(self):
        frames = FrameStore()
        image = QImage(5, 3, QImage.Format.Format_RGB888)
        image.fill(QColor(10, 20, 30))
        frames.put(image)
        array = frames.bgr()
        self.assertEqual(array.shape, (3, 5, 3))
        self.assertEqual(array[0, 0].tolist(), [30, 20, 10])
        array[:] = 0
        self.assertEqual(frames.bgr()[0, 0].tolist(), [30, 20, 10])
        frames.updated -= 3
        with self.assertRaisesRegex(RuntimeError, "新鲜画面"):
            frames.snapshot()

    def test_frame_snapshots_have_new_ids_and_detached_images(self):
        frames = FrameStore(history_size=2)
        image = QImage(4, 2, QImage.Format.Format_RGB32)
        image.fill(QColor(20, 40, 60))
        first = frames.put(image)
        self.assertIsNotNone(first)
        first_copy = frames.snapshot_frame()
        self.assertEqual(first_copy.frame_id, first.frame_id)
        self.assertGreater(first_copy.captured_at_ns, 0)
        first_copy.image().fill(QColor(255, 0, 0))
        self.assertEqual(frames.snapshot().pixelColor(0, 0), QColor(20, 40, 60))
        with self.assertRaisesRegex(RuntimeError, "动作之后的新采集帧"):
            frames.snapshot_frame(after_frame_id=first.frame_id)

        image.fill(QColor(80, 100, 120))
        second = frames.put(image)
        image.fill(QColor(140, 160, 180))
        third = frames.put(image)
        self.assertGreater(second.frame_id, first.frame_id)
        self.assertGreater(third.frame_id, second.frame_id)
        self.assertEqual(
            [frame.frame_id for frame in frames.frames_after(0)],
            [second.frame_id, third.frame_id],
        )
        self.assertEqual(
            frames.frames_after(first.frame_id, limit=1)[0].frame_id,
            third.frame_id,
        )

    def test_seed_observer_uses_only_new_complete_and_nonfrozen_animations(self):
        frames = FrameStore()
        idle = QImage(4, 4, QImage.Format.Format_RGB32)
        idle.fill(QColor(0, 0, 0))
        frames.put(idle)

        def classify(snapshot):
            red = snapshot.image().pixelColor(0, 0).red()
            if red == 200:
                return AnimationFrameScore(AnimationFramePhase.ANIMATION, 0.1, 0.95, 0.6)
            if red == 100:
                return AnimationFrameScore(AnimationFramePhase.ANIMATION, 0.95, 0.1, 0.6)
            return AnimationFrameScore(AnimationFramePhase.IDLE)

        observer = SeedObserver(
            frames,
            classify,
            timeout_seconds=0.04,
            poll_seconds=0.001,
            minimum_animation_frames=2,
        )

        def trigger_one():
            first_animation = QImage(4, 4, QImage.Format.Format_RGB32)
            first_animation.fill(QColor(200, 0, 0))
            second_animation = first_animation.copy()
            second_animation.setPixelColor(3, 3, QColor(20, 20, 20))
            frames.put(first_animation)
            frames.put(second_animation)
            frames.put(idle)

        sample = observer.observe_bit(trigger_one)
        self.assertEqual(sample.bit, 1)
        self.assertEqual(sample.frame_ids, (2, 3))

        stale = observer.observe_bit(lambda: None, timeout_seconds=0.01)
        self.assertIsNone(stale.bit)
        self.assertEqual(stale.reason, "animation_not_seen")

        def trigger_frozen():
            animation = QImage(4, 4, QImage.Format.Format_RGB32)
            animation.fill(QColor(100, 0, 0))
            frames.put(animation)
            frames.put(animation)
            frames.put(idle)

        frozen_classifier = lambda snapshot: (
            AnimationFrameScore(AnimationFramePhase.ANIMATION, 0.95, 0.1, 0.0)
            if snapshot.image().pixelColor(0, 0).red() == 100
            else AnimationFrameScore(AnimationFramePhase.IDLE)
        )
        frozen = SeedObserver(
            frames,
            frozen_classifier,
            timeout_seconds=0.04,
            poll_seconds=0.001,
            minimum_animation_frames=2,
        ).observe_bit(trigger_frozen)
        self.assertIsNone(frozen.bit)
        self.assertEqual(frozen.reason, "frozen_animation")

    def test_region_mapping_and_validation(self):
        self.assertEqual(crop_bounds([0.25, 0.25, 0.5, 0.5], 1920, 1080), (480, 270, 1440, 810))
        for value in ([0, 0, 0, 1], [-0.1, 0, 1, 1], [0, 0, 2, 1], [float("nan"), 0, 1, 1]):
            with self.assertRaises(ValueError):
                crop_bounds(value, 100, 100)

    def test_preview_drag_respects_letterboxing(self):
        preview = RegionPreview()
        preview.resize(600, 400)
        preview.image = QImage(1600, 900, QImage.Format.Format_RGB32)
        preview.show()
        APP.processEvents()
        rect = preview.image_rect()
        regions = []
        preview.region_changed.connect(regions.append)
        QTest.mousePress(preview, Qt.MouseButton.LeftButton, pos=QPoint(round(rect.x() + rect.width() * 0.25), round(rect.y() + rect.height() * 0.25)))
        QTest.mouseRelease(preview, Qt.MouseButton.LeftButton, pos=QPoint(round(rect.x() + rect.width() * 0.75), round(rect.y() + rect.height() * 0.75)))
        self.assertEqual(len(regions), 1)
        for actual, expected in zip(regions[0], [0.25, 0.25, 0.5, 0.5]):
            self.assertAlmostEqual(actual, expected, delta=0.005)
        preview.close()

    def test_paddle_result_keeps_low_confidence_and_raw_text(self):
        rows = parse_prediction([{"rec_texts": ["123", "???"], "rec_scores": [0.99, 0.12]}])
        self.assertEqual(rows[1], {"text": "???", "confidence": 0.12})
        self.assertEqual(parse_prediction([]), [])


if __name__ == "__main__":
    unittest.main()
