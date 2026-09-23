from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QComboBox, QFrame, QGridLayout, QLabel, QPushButton, QSpinBox,
    QVBoxLayout, QWidget,
)

STYLE = """
* { font-family: 'Microsoft YaHei UI'; font-size: 13px; color: #172033; }
QMainWindow, QWidget#root, QScrollArea, QWidget#canvas { background: #f3f6fb; }
QScrollArea { border: none; }
QFrame#sidebar { background: #17213a; border: none; }
QLabel#brand { color: white; font-size: 23px; font-weight: 800; }
QLabel#sideMuted { color: #9baac6; font-size: 12px; }
QPushButton#nav { background: transparent; color: #b4c0d7; border: none; text-align: left; padding: 15px; border-radius: 9px; }
QPushButton#nav:hover { background: #202e4c; color: white; }
QPushButton#nav:checked { background: #2a3b63; color: white; border-left: 3px solid #8193ff; }
QFrame#card { background: white; border: 1px solid #e1e7f1; border-radius: 12px; }
QLabel { background: transparent; }
QLabel#title { font-size: 24px; font-weight: 800; color: #14203a; }
QLabel#cardTitle { font-size: 16px; font-weight: 700; }
QLabel#muted { color: #73829a; font-size: 12px; }
QLabel#eyebrow { color: #5f72df; font-size: 11px; font-weight: 700; }
QLabel#metric { font-size: 28px; font-weight: 800; color: #526be1; }
QLabel#notice { background: #ebefff; color: #4c61b5; border: 1px solid #dce3ff; border-radius: 8px; padding: 10px; }
QLabel#error { color: #a93647; background: #fff1f3; border-radius: 7px; padding: 10px; }
QLineEdit, QSpinBox, QComboBox { background: #fbfcff; border: 1px solid #dce3ee; border-radius: 6px; min-height: 30px; padding: 2px 9px; selection-background-color: #657de9; }
QLineEdit:focus, QSpinBox:focus, QComboBox:focus { border-color: #788ceb; background: white; }
QComboBox { padding-right: 24px; }
QComboBox::drop-down { border: none; width: 25px; }
QComboBox::down-arrow { image: url(@DOWN@); width: 12px; height: 12px; }
QSpinBox { padding-right: 21px; }
QSpinBox[ivRange="true"] { padding: 2px 4px; }
QSpinBox::up-button { subcontrol-origin: border; subcontrol-position: top right; width: 20px; border: none; }
QSpinBox::down-button { subcontrol-origin: border; subcontrol-position: bottom right; width: 20px; border: none; }
QSpinBox::up-arrow { image: url(@UP@); width: 12px; height: 12px; }
QSpinBox::down-arrow { image: url(@DOWN@); width: 12px; height: 12px; }
QComboBox QAbstractItemView { background: white; selection-background-color: #e7edff; selection-color: #172033; padding: 5px; }
QPushButton { background: white; border: 1px solid #dce3ee; border-radius: 7px; padding: 8px 14px; font-weight: 600; }
QPushButton:hover { background: #edf1ff; border-color: #b8c5f4; }
QPushButton#primary { background: #596fe5; border-color: #596fe5; color: white; }
QPushButton#primary:hover { background: #475cd0; }
QPushButton:disabled, QPushButton#primary:disabled { background: #eef1f6; color: #9aa6ba; border-color: #e2e7ef; }
QCheckBox { spacing: 7px; min-height: 26px; }
QTableView { background: white; alternate-background-color: #f7f9fd; border: none; gridline-color: #edf0f6; selection-background-color: #e5ecff; selection-color: #172033; }
QHeaderView::section { background: #f2f5fb; color: #66768f; border: none; padding: 9px; font-weight: 600; }
QPlainTextEdit { background: #17213a; color: #c6d3ec; border: none; border-radius: 8px; padding: 12px; font-family: Consolas; }
QProgressBar { border: none; border-radius: 3px; background: #e6ebf5; max-height: 6px; }
QProgressBar::chunk { background: #6d82ef; border-radius: 3px; }
QSplitter::handle { background: #edf1f7; width: 6px; }
QToolTip { background: #17213a; color: white; border: none; padding: 8px; }
"""
STYLE = STYLE.replace("@UP@", (Path(__file__).parent / "assets/chevron-up.svg").as_posix())
STYLE = STYLE.replace("@DOWN@", (Path(__file__).parent / "assets/chevron-down.svg").as_posix())


def label(text, name=None, wrap=False):
    widget = QLabel(text)
    if name:
        widget.setObjectName(name)
    widget.setWordWrap(wrap)
    return widget


def button(text, handler=None, primary=False):
    widget = QPushButton(text)
    widget.setCursor(Qt.CursorShape.PointingHandCursor)
    if primary:
        widget.setObjectName("primary")
    if handler:
        widget.clicked.connect(handler)
    return widget


def combo(items):
    widget = QComboBox()
    for item in items:
        title, value = item if isinstance(item, tuple) else (item, item)
        widget.addItem(title, value)
    widget.setMinimumWidth(0)
    widget.setSizeAdjustPolicy(QComboBox.SizeAdjustPolicy.AdjustToMinimumContentsLengthWithIcon)
    return widget


def spin(value=0, maximum=1_000_000_000, minimum=0):
    widget = QSpinBox()
    widget.setRange(minimum, maximum)
    widget.setValue(value)
    widget.setGroupSeparatorShown(True)
    return widget


class Card(QFrame):
    def __init__(self, title, subtitle=""):
        super().__init__()
        self.setObjectName("card")
        self.body = QVBoxLayout(self)
        self.body.setContentsMargins(18, 15, 18, 17)
        self.body.setSpacing(10)
        if title:
            self.body.addWidget(label(title, "cardTitle"))
        if subtitle:
            self.body.addWidget(label(subtitle, "muted", True))


def form(card, fields, columns=2):
    grid = QGridLayout()
    grid.setHorizontalSpacing(14)
    grid.setVerticalSpacing(10)
    for i, (title, widget) in enumerate(fields):
        field = QWidget()
        layout = QVBoxLayout(field)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(4)
        layout.addWidget(label(title, "muted"))
        layout.addWidget(widget)
        widget.setAccessibleName(title)
        grid.addWidget(field, i // columns, i % columns)
    for col in range(columns):
        grid.setColumnStretch(col, 1)
    card.body.addLayout(grid)
