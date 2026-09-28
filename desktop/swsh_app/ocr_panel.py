from pathlib import Path
import tempfile

from PySide6.QtCore import QPointF, QRectF, Qt, Signal
from PySide6.QtGui import QColor, QImage, QPainter, QPen
from PySide6.QtWidgets import (
    QFileDialog, QHBoxLayout, QHeaderView, QLineEdit, QTableWidget,
    QTableWidgetItem, QVBoxLayout, QWidget,
)

from .ocr_process import OcrProcess, default_ocr_python
from .ocr_worker import crop_bounds
from .widgets import Card, button, combo, form, label, spin

FIELDS = ["自由文本", "性格", "特性", "HP", "攻击", "防御", "特攻", "特防", "速度", "训练家 ID"]


class RegionPreview(QWidget):
    region_changed = Signal(object)

    def __init__(self):
        super().__init__()
        self.image = QImage()
        self.roi = None
        self.anchor = None
        self.setMinimumHeight(240)
        self.setCursor(Qt.CursorShape.CrossCursor)

    def image_rect(self):
        if self.image.isNull():
            return QRectF()
        scale = min(self.width() / self.image.width(), self.height() / self.image.height())
        w, h = self.image.width() * scale, self.image.height() * scale
        return QRectF((self.width() - w) / 2, (self.height() - h) / 2, w, h)

    def paintEvent(self, event):
        p = QPainter(self)
        p.fillRect(self.rect(), QColor("#17213a"))
        target = self.image_rect()
        if target.isEmpty():
            p.setPen(QColor("#b4c0d7"))
            p.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "从采集画面取帧，或打开截图\n然后拖动鼠标框选识别区域")
            return
        p.drawImage(target, self.image)
        if self.roi:
            x, y, w, h = self.roi
            p.setPen(QPen(QColor("#799aff"), 2))
            p.setBrush(QColor(90, 130, 240, 35))
            p.drawRect(QRectF(target.x() + x * target.width(), target.y() + y * target.height(), w * target.width(), h * target.height()))

    def normalized(self, point):
        rect = self.image_rect()
        return QPointF(max(0, min(1, (point.x() - rect.x()) / rect.width())), max(0, min(1, (point.y() - rect.y()) / rect.height())))

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self.image_rect().contains(event.position()):
            self.anchor = self.normalized(event.position())

    def mouseMoveEvent(self, event):
        if self.anchor is not None:
            end = self.normalized(event.position())
            self.roi = [min(self.anchor.x(), end.x()), min(self.anchor.y(), end.y()), abs(end.x() - self.anchor.x()), abs(end.y() - self.anchor.y())]
            self.update()

    def mouseReleaseEvent(self, event):
        if self.anchor is not None:
            self.mouseMoveEvent(event)
            self.anchor = None
            if self.roi and self.roi[2] > 0.002 and self.roi[3] > 0.002:
                self.region_changed.emit(self.roi)


class OcrPanel(QWidget):
    def __init__(self, frames, saved, persist, log, fail, parent=None):
        super().__init__(parent)
        self.frames, self.saved, self.persist, self.log, self.fail = frames, saved, persist, log, fail
        self.regions = {}
        for name, roi in saved.get("ocrRegions", {}).items():
            try:
                crop_bounds(roi, 1920, 1080)
                if name in FIELDS:
                    self.regions[name] = roi
            except (ValueError, TypeError):
                pass
        self.worker = OcrProcess(self)
        self.worker.result.connect(self.show_result)
        self.worker.failed.connect(self.ocr_failed)
        self.worker.settled.connect(self.refresh_state)
        self.workspace = tempfile.TemporaryDirectory(prefix="swsh-ocr-")
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        card = Card("OCR 区域与测试", "使用 PaddleOCR v5 移动模型。能力值是面板数值，不会自动填入个体值筛选。")
        row = QHBoxLayout()
        self.capture_button = button("从采集画面取帧", self.take_frame)
        self.open_button = button("打开截图", self.open_image)
        row.addWidget(self.capture_button)
        row.addWidget(self.open_button)
        self.field = combo(FIELDS)
        self.field.currentIndexChanged.connect(self.select_region)
        row.addWidget(self.field, 1)
        self.full_button = button("选择整张图", self.select_full)
        row.addWidget(self.full_button)
        card.body.addLayout(row)
        self.preview = RegionPreview()
        self.preview.region_changed.connect(self.region_selected)
        card.body.addWidget(self.preview, 1)
        self.region_status = label("尚未取帧。剑盾区域需要按当前画面框选。", "muted", True)
        card.body.addWidget(self.region_status)
        row = QHBoxLayout()
        self.run_button = button("识别当前区域", self.recognize, True)
        self.all_button = button("测试全部已设区域", lambda: self.recognize(all_regions=True))
        self.stop_button = button("停止 OCR", self.stop)
        self.save_button = button("保存区域", self.save_regions)
        for b in (self.run_button, self.all_button, self.stop_button, self.save_button):
            row.addWidget(b)
        card.body.addLayout(row)
        self.results = QTableWidget(0, 4)
        self.results.setHorizontalHeaderLabels(["区域", "识别文字", "置信度", "状态"])
        self.results.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        self.results.setMinimumHeight(100)
        self.results.setMaximumHeight(220)
        self.results.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.results.verticalHeader().hide()
        card.body.addWidget(self.results)
        layout.addWidget(card, 1)
        settings = Card("OCR 运行环境", "首次识别会下载模型，后续复用已加载模型。源码模式可使用独立 Python 环境。")
        self.python = QLineEdit(saved.get("ocrPython") or default_ocr_python())
        self.cache = QLineEdit(saved.get("ocrCache") or "D:/CodexTools/auto-swsh-rng/paddlex-cache")
        self.threshold = spin(int(saved.get("ocrThreshold", 80)), 100)
        form(settings, [("OCR Python", self.python), ("模型缓存目录", self.cache), ("最低置信度（%）", self.threshold)], 3)
        self.python.editingFinished.connect(self.reset_engine)
        self.cache.editingFinished.connect(self.reset_engine)
        settings.body.addWidget(label("缺少依赖时，在所选 Python 环境安装 desktop/requirements-ocr.txt。低置信度会标为待核对，保留原始文字。", "muted", True))
        layout.addWidget(settings)
        self.select_region()
        self.refresh_state()

    def refresh_state(self):
        busy = self.worker.busy or self.worker.stopping
        for widget in (self.run_button, self.all_button, self.capture_button, self.open_button, self.full_button,
                       self.field, self.preview, self.python, self.cache, self.threshold):
            widget.setEnabled(not busy)
        self.stop_button.setEnabled(self.worker.busy)

    def set_image(self, image, source):
        if image.isNull():
            raise ValueError("截图无法读取。")
        self.preview.image = image.copy()
        self.preview.update()
        self.region_status.setText(f"{source} · {image.width()} × {image.height()}；拖动鼠标框选。")

    def take_frame(self):
        try:
            self.set_image(self.frames.snapshot(), "采集卡原始画面")
        except Exception as exc:
            self.fail(str(exc))

    def open_image(self):
        path, _ = QFileDialog.getOpenFileName(self, "打开 OCR 截图", "", "图像 (*.png *.jpg *.jpeg *.bmp)")
        if path:
            try:
                self.set_image(QImage(path), f"本地截图：{Path(path).name}")
            except ValueError as exc:
                self.fail(str(exc))

    def select_region(self, *_):
        self.preview.roi = self.regions.get(self.field.currentText())
        self.preview.update()

    def region_selected(self, roi):
        self.regions[self.field.currentText()] = roi
        self.region_status.setText(f"已设置「{self.field.currentText()}」；共 {len(self.regions)} 个区域。")

    def select_full(self):
        if not self.preview.image.isNull():
            self.region_selected([0, 0, 1, 1])
            self.select_region()

    def save_regions(self):
        self.saved.update(ocrRegions=self.regions.copy(), ocrPython=self.python.text().strip(),
                          ocrCache=self.cache.text().strip(), ocrThreshold=self.threshold.value())
        if self.persist():
            self.region_status.setText("OCR 区域与运行设置已保存。")
            self.log("保存 OCR 区域配置。")

    def recognize(self, checked=False, *, all_regions=False):
        if self.worker.busy or self.worker.stopping:
            return
        if self.preview.image.isNull():
            self.fail("请先从采集画面取帧或打开截图。")
            return
        regions = self.regions.copy() if all_regions else {self.field.currentText(): self.regions.get(self.field.currentText())}
        if not regions or any(roi is None for roi in regions.values()):
            self.fail("请先框选要识别的区域。")
            return
        image_path = Path(self.workspace.name) / "frame.png"
        if not self.preview.image.save(str(image_path)):
            self.fail("无法写入 OCR 临时图像。")
            return
        self.results.setRowCount(0)
        self.region_status.setText("正在识别…首次加载模型可能需要较长时间。")
        self.log(f"开始 OCR：{', '.join(regions)}")
        self.worker.start({"image": str(image_path), "regions": regions}, self.python.text().strip(), self.cache.text().strip())
        self.refresh_state()

    def show_result(self, data):
        self.results.setRowCount(len(data))
        for row, result in enumerate(data):
            confidence = result["confidence"]
            state = "通过" if result["text"] and confidence >= self.threshold.value() / 100 else "待核对"
            for col, value in enumerate([result["field"], result["text"], f"{confidence:.1%}", state]):
                self.results.setItem(row, col, QTableWidgetItem(value))
        self.region_status.setText(f"识别完成，共 {len(data)} 个区域。")
        self.log(self.region_status.text())

    def ocr_failed(self, message):
        self.region_status.setText("识别失败，请检查运行环境或区域设置。")
        self.fail(f"OCR：{message}")

    def stop(self):
        self.worker.stop()
        self.region_status.setText("OCR 已停止，下一次识别将重新加载模型。")

    def reset_engine(self):
        if not self.worker.busy:
            self.worker.stop()
