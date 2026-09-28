"""Qt controller drawing, adapted from the local FireRed/LeafGreen layout.

Input is scoped to this widget: editing an ECS file never drives the controller.
"""
from PySide6.QtCore import QEvent, QRect, QRectF, QSize, Qt, Signal
from PySide6.QtGui import QColor, QKeySequence, QPainter, QPainterPath, QPen
from PySide6.QtWidgets import QApplication, QPushButton, QWidget

MAPPING = {
    "A": "C", "B": "X", "X": "V", "Y": "Z", "L": "Q", "R": "E", "ZL": "R", "ZR": "T",
    "MINUS": "O", "PLUS": "P", "CAPTURE": "N", "HOME": "B", "LCLICK": "F", "RCLICK": "G",
    "TOP": "Up", "DOWN": "Down", "LEFT": "Left", "RIGHT": "Right",
    "LS_UP": "W", "LS_DOWN": "S", "LS_LEFT": "A", "LS_RIGHT": "D",
    "RS_UP": "I", "RS_DOWN": "K", "RS_LEFT": "J", "RS_RIGHT": "L",
}
POSITIONS = {
    "ZL": (180, 28), "ZR": (600, 28), "L": (180, 78), "R": (600, 78),
    "LS_UP": (180, 134), "LS_LEFT": (114, 184), "LCLICK": (180, 184), "LS_RIGHT": (246, 184), "LS_DOWN": (180, 234),
    "X": (600, 134), "Y": (534, 184), "A": (666, 184), "B": (600, 234),
    "MINUS": (324, 134), "PLUS": (456, 134), "CAPTURE": (346, 204), "HOME": (434, 204),
    "TOP": (282, 284), "LEFT": (216, 334), "RIGHT": (348, 334), "DOWN": (282, 384),
    "RS_UP": (494, 262), "RS_LEFT": (428, 312), "RCLICK": (494, 312), "RS_RIGHT": (560, 312), "RS_DOWN": (494, 362),
}
LABELS = {"MINUS": "−", "PLUS": "+", "CAPTURE": "截图", "HOME": "主页", "LCLICK": "L 按下", "RCLICK": "R 按下",
          "TOP": "↑", "DOWN": "↓", "LEFT": "←", "RIGHT": "→"}
for prefix in ("LS", "RS"):
    LABELS.update({f"{prefix}_{direction}": arrow for direction, arrow in (("UP", "↑"), ("DOWN", "↓"), ("LEFT", "←"), ("RIGHT", "→"))})


class Gamepad(QWidget):
    changed = Signal(object)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.sources = set()
        self.controls = {}
        self.key_codes = {QKeySequence(binding)[0].key(): key for key, binding in MAPPING.items()}
        self.setMinimumSize(540, 313)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setAttribute(Qt.WidgetAttribute.WA_InputMethodEnabled, False)
        for key in POSITIONS:
            control = QPushButton(f"{LABELS.get(key, key)}\n{MAPPING[key]}", self)
            control.setFocusPolicy(Qt.FocusPolicy.NoFocus)
            control.setAccessibleName(key)
            control.setToolTip(f"{key} · 键盘 {MAPPING[key]} · 按住保持，松开释放")
            control.setCursor(Qt.CursorShape.PointingHandCursor)
            control.pressed.connect(lambda k=key: self.transition(k, "mouse", True))
            control.released.connect(lambda k=key: self.transition(k, "mouse", False))
            self.controls[key] = control
        QApplication.instance().installEventFilter(self)
        self.restyle()

    @property
    def pressed(self):
        return {key for key, source in self.sources}

    def transition(self, key, source, down):
        if down and not self.isEnabled():
            return
        before = self.pressed
        if down:
            self.setFocus(Qt.FocusReason.MouseFocusReason)
            self.sources.add((key, source))
        else:
            self.sources.discard((key, source))
        if before != self.pressed:
            self.restyle()
            self.changed.emit(self.pressed)

    def release_all(self):
        if self.sources:
            self.sources.clear()
            for control in self.controls.values():
                control.setDown(False)
            self.restyle()
            self.changed.emit(set())

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_Escape:
            self.release_all()
            return
        key = self.key_codes.get(event.key())
        if key and not event.modifiers() & (Qt.KeyboardModifier.ControlModifier | Qt.KeyboardModifier.AltModifier | Qt.KeyboardModifier.MetaModifier):
            if not event.isAutoRepeat():
                self.transition(key, "keyboard", True)
            event.accept()
        else:
            super().keyPressEvent(event)

    def keyReleaseEvent(self, event):
        key = self.key_codes.get(event.key())
        if key:
            if not event.isAutoRepeat():
                self.transition(key, "keyboard", False)
            event.accept()
        else:
            super().keyReleaseEvent(event)

    def focusOutEvent(self, event):
        self.release_all()
        super().focusOutEvent(event)

    def hideEvent(self, event):
        self.release_all()
        super().hideEvent(event)

    def changeEvent(self, event):
        if event.type() == QEvent.Type.EnabledChange and not self.isEnabled():
            self.release_all()
        super().changeEvent(event)

    def eventFilter(self, obj, event):
        if event.type() == QEvent.Type.ApplicationDeactivate or (
                event.type() == QEvent.Type.WindowDeactivate and obj is self.window()):
            self.release_all()
        return False

    def sizeHint(self):
        return QSize(760, 440)

    def transform(self):
        scale = min(self.width() / 760, self.height() / 440, 1.35)
        return scale, (self.width() - 760 * scale) / 2, (self.height() - 440 * scale) / 2

    def resizeEvent(self, event):
        scale, dx, dy = self.transform()
        for key, (x, y) in POSITIONS.items():
            width = 94 if key in ("L", "R", "ZL", "ZR") else 56
            height = 38 if key in ("L", "R", "ZL", "ZR") else 46
            self.controls[key].setGeometry(QRect(round(dx + (x - width / 2) * scale), round(dy + (y - height / 2) * scale), round(width * scale), round(height * scale)))
        self.restyle()
        super().resizeEvent(event)

    def restyle(self):
        scale = self.transform()[0]
        for key, control in self.controls.items():
            active = key in self.pressed
            radius = round((23 if key in ("A", "B", "X", "Y", "LCLICK", "RCLICK") else 8) * scale)
            control.setStyleSheet(
                f"QPushButton {{ padding:0; border:1px solid {'#596fe5' if active else '#bbc8dc'}; border-radius:{radius}px; "
                f"font-size:{max(10, round(12 * scale))}px; background:{'#596fe5' if active else '#ffffff'}; color:{'white' if active else '#334560'}; }}"
                "QPushButton:hover { border-color:#657de9; } QPushButton:disabled { color:#8b98ac; background:#f2f5fa; border-color:#d7dfec; }")

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        scale, dx, dy = self.transform()
        p.translate(dx, dy)
        p.scale(scale, scale)
        body = QPainterPath()
        body.moveTo(170, 95)
        body.cubicTo(110, 95, 86, 125, 68, 210)
        body.lineTo(35, 355)
        body.cubicTo(18, 431, 105, 453, 143, 395)
        body.lineTo(171, 353)
        body.lineTo(589, 353)
        body.lineTo(617, 395)
        body.cubicTo(655, 453, 742, 431, 725, 355)
        body.lineTo(692, 210)
        body.cubicTo(674, 125, 650, 95, 590, 95)
        body.closeSubpath()
        p.setPen(QPen(QColor("#b9c7df"), 2))
        p.setBrush(QColor("#e7edf8"))
        p.drawPath(body)
        p.setBrush(QColor("#d8e1f1"))
        for x, y in ((180, 184), (494, 312)):
            p.drawEllipse(QRectF(x - 49, y - 49, 98, 98))
        p.drawRoundedRect(QRectF(265, 297, 34, 74), 5, 5)
        p.drawRoundedRect(QRectF(245, 317, 74, 34), 5, 5)
        p.setPen(QColor("#8190a8"))
        p.drawText(QRectF(330, 248, 100, 30), Qt.AlignmentFlag.AlignCenter, "S W S H")
        p.end()
