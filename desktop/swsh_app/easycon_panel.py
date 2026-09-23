from pathlib import Path
import threading

from PySide6.QtCore import QTimer, Qt, Signal
from PySide6.QtWidgets import (
    QFileDialog, QFormLayout, QHBoxLayout, QLayout, QLineEdit, QMessageBox, QPlainTextEdit,
    QScrollArea, QSplitter, QTabWidget, QVBoxLayout, QWidget,
)

from .controller import ControllerSession
from .gamepad import Gamepad
from .script_library import SCRIPT_DIR, adapted_source, apply_parameters, inspect_script, parameters
from .tasks import Task
from .vendor.easycon.device import list_ports
from .widgets import Card, button, combo, label


class EasyConPanel(QWidget):
    log_message = Signal(str)

    def __init__(self, frames, saved, log, fail, parent=None):
        super().__init__(parent)
        self.fail = fail
        self.log_message.connect(log)
        self.session = ControllerSession(frames.bgr, log=self.log_message.emit)
        self.task = None
        self.path = None
        self.closing = False
        self.run_cancel = None
        self.parameter_inputs = {}
        self.parameter_source = ""
        self.label_root = saved.get("labelRoot", "")
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        device = Card("伊机控连接")
        row = QHBoxLayout()
        port = saved.get("port", "")
        self.ports = combo([(port or "请选择串口", port)])
        row.addWidget(self.ports, 1)
        self.detect = button("刷新串口", self.refresh_ports)
        self.connect_button = button("连接", self.connect_device, True)
        self.disconnect_button = button("断开", self.disconnect_device)
        self.state = label("○  未连接", "muted")
        for b in (self.detect, self.connect_button, self.disconnect_button, self.state):
            row.addWidget(b)
        device.body.addLayout(row)
        layout.addWidget(device)
        self.tabs = QTabWidget()
        self.tabs.setStyleSheet("QTabWidget::pane { border:0; } QTabBar::tab { padding:11px 24px; background:#e8edf7; color:#687892; border-top-left-radius:8px; border-top-right-radius:8px; margin-right:4px; } QTabBar::tab:selected { background:white; color:#4c62d5; font-weight:700; }")
        layout.addWidget(self.tabs, 1)
        manual = Card("手柄", "按住按键保持输入，松开即释放。点击手柄后可用键盘控制；切换页面或窗口会自动松开。")
        self.pad = Gamepad()
        self.pad.changed.connect(self.manual_changed)
        manual.body.addWidget(self.pad, 1)
        row = QHBoxLayout()
        self.pad_status = label("连接伊机控后启用手柄", "muted")
        row.addWidget(self.pad_status, 1)
        self.release_button = button("释放全部 · Esc", self.release_manual)
        row.addWidget(self.release_button)
        manual.body.addLayout(row)
        manual.body.addWidget(label("左摇杆 WASD  ·  右摇杆 IJKL  ·  十字键 ↑↓←→  ·  ABXY 对应 C / X / V / Z", "muted", True))
        self.tabs.addTab(manual, "手柄控制")
        self.build_scripts()
        self.ports.currentIndexChanged.connect(self.refresh_state)
        self.health_timer = QTimer(self)
        self.health_timer.setInterval(500)
        self.health_timer.timeout.connect(self.refresh_state)
        self.health_timer.start()
        self.refresh_state()

    def build_scripts(self):
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 10, 0, 0)
        split = QSplitter(Qt.Orientation.Horizontal)
        params = Card("脚本库", "载入脚本、填写参数，再检查依赖。")
        self.library = combo([(path.stem, str(path)) for path in sorted(SCRIPT_DIR.glob("*.ecs"))])
        params.body.addWidget(self.library)
        self.load_button = button("载入所选脚本", self.load_selected)
        params.body.addWidget(self.load_button)
        self.parse_button = button("从编辑内容刷新参数", self.update_parameters)
        params.body.addWidget(self.parse_button)
        self.parameters_widget = QWidget()
        self.parameters_form = QFormLayout(self.parameters_widget)
        self.parameters_form.setContentsMargins(0, 0, 0, 0)
        self.parameters_form.setRowWrapPolicy(QFormLayout.RowWrapPolicy.WrapAllRows)
        self.parameters_form.setSizeConstraint(QLayout.SizeConstraint.SetMinimumSize)
        parameter_scroll = QScrollArea()
        parameter_scroll.setWidgetResizable(True)
        parameter_scroll.setWidget(self.parameters_widget)
        parameter_scroll.setMinimumHeight(120)
        params.body.addWidget(parameter_scroll, 1)
        self.template_button = button("选择 ImgLabel 目录", self.choose_templates)
        params.body.addWidget(self.template_button)
        self.template_status = label(self.label_root or "默认读取脚本同目录的 ImgLabel。", "muted", True)
        params.body.addWidget(self.template_status)
        params.setMinimumWidth(245)
        split.addWidget(params)
        script = Card("ECS 脚本", "搜图与 OCR 共用采集画面；运行前检查参数与模板。")
        self.editor = QPlainTextEdit()
        self.editor.setPlainText('# 无按键示例：可以直接运行\nPRINT "剑盾伊机控已就绪"\nWAIT 100\n')
        self.editor.document().setModified(False)
        script.body.addWidget(self.editor, 3)
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setPlaceholderText("脚本输出 · 测种的 0 / 1 序列会连续显示在这里")
        self.console.setMaximumHeight(130)
        self.console.setMinimumHeight(70)
        self.console.setMaximumBlockCount(5000)
        script.body.addWidget(self.console, 1)
        self.log_message.connect(self.append_output)
        split.addWidget(script)
        split.setStretchFactor(1, 1)
        split.setSizes([270, 750])
        layout.addWidget(split, 1)
        row = QHBoxLayout()
        self.open_button = button("打开", self.open_script)
        self.save_button = button("另存为", self.save_script)
        self.check_button = button("检查脚本", self.check_script)
        self.run_button = button("运行脚本", self.run_script, True)
        self.stop_button = button("停止并释放", self.stop)
        for b in (self.open_button, self.save_button, self.check_button, self.run_button, self.stop_button):
            row.addWidget(b)
        row.addStretch()
        layout.addLayout(row)
        self.script_status = label("未运行 · 附带 9 份剑盾脚本", "muted", True)
        layout.addWidget(self.script_status)
        self.tabs.addTab(page, "脚本库 / 执行")

    def refresh_ports(self):
        previous = self.ports.currentData()
        self.ports.clear()
        for port in list_ports():
            self.ports.addItem(port, port)
        index = self.ports.findData(previous)
        if index >= 0:
            self.ports.setCurrentIndex(index)
        self.refresh_state()

    def refresh_state(self):
        busy = self.task is not None
        connected = self.session.connected
        self.state.setText(f"●  已连接 {self.session.device.port}" if connected else "○  未连接")
        self.ports.setEnabled(not busy and not connected)
        self.detect.setEnabled(not busy and not connected)
        self.connect_button.setEnabled(not busy and not connected and bool(self.ports.currentData()))
        self.disconnect_button.setEnabled(not busy and connected)
        self.run_button.setEnabled(not busy)
        self.stop_button.setEnabled(busy and self.run_cancel is not None)
        self.editor.setReadOnly(busy)
        for control in (self.open_button, self.save_button, self.load_button, self.library,
                        self.parse_button, self.check_button, self.parameters_widget, self.template_button):
            control.setEnabled(not busy)
        self.pad.setEnabled(not busy and connected)
        self.release_button.setEnabled(not busy and connected)
        if not connected:
            self.pad_status.setText("连接伊机控后启用手柄")
        elif busy:
            self.pad_status.setText("脚本运行中，手动输入已暂停")
        elif not self.pad.pressed:
            self.pad_status.setText("已就绪 · 点击手柄启用键盘控制")

    def manual_changed(self, keys):
        if self.task or not self.session.connected:
            return
        try:
            self.session.manual(keys)
            self.pad_status.setText("按住：" + " + ".join(sorted(keys)) if keys else "已释放全部按键")
        except Exception as exc:
            self.fail(str(exc))

    def release_manual(self):
        self.pad.release_all()
        if not self.task and self.session.connected:
            try:
                self.session.manual(set())
            except Exception as exc:
                self.fail(str(exc))

    def submit(self, function, done=None):
        if self.task:
            return
        self.release_manual()
        self.task = Task(function, self)
        if done:
            self.task.result.connect(done)
        self.task.failed.connect(self.operation_failed)
        self.task.finished.connect(self.task_finished)
        self.refresh_state()
        self.task.start()

    def operation_failed(self, message):
        self.script_status.setText(f"操作失败：{message}")
        self.fail(message)

    def task_finished(self):
        old = self.task
        self.task = None
        self.run_cancel = None
        old.deleteLater()
        self.refresh_state()
        if self.closing:
            QTimer.singleShot(0, self.window().close)

    def connect_device(self):
        port = self.ports.currentData()
        if port:
            self.submit(lambda: self.session.connect(port))

    def disconnect_device(self):
        self.submit(self.session.disconnect)

    def append_output(self, text):
        from PySide6.QtGui import QTextCursor
        cursor = self.console.textCursor()
        cursor.movePosition(QTextCursor.MoveOperation.End)
        cursor.insertText(text)
        self.console.setTextCursor(cursor)
        self.console.ensureCursorVisible()

    def update_parameters(self, checked=False, *, preserve=False):
        text = self.editor.toPlainText()
        previous = {p.name: (p.default, self.parameter_inputs[p.name].text())
                    for p in parameters(self.parameter_source) if p.name in self.parameter_inputs}
        while self.parameters_form.rowCount():
            self.parameters_form.removeRow(0)
        self.parameter_inputs = {}
        for param in parameters(text):
            value = previous.get(param.name)
            field = QLineEdit(value[1] if preserve and value and value[0] == param.default else param.default)
            field.setPlaceholderText("必填")
            field.setToolTip(param.description)
            field.setAccessibleName(param.name[1:])
            self.parameters_form.addRow(param.name[1:], field)
            self.parameter_inputs[param.name] = field
        self.parameter_source = text

    def prepared_text(self):
        text = self.editor.toPlainText()
        if text != self.parameter_source:
            self.update_parameters(preserve=True)
        return apply_parameters(text, {key: field.text() for key, field in self.parameter_inputs.items()})

    def check_script(self):
        try:
            program, labels, missing = inspect_script(self.prepared_text(), self.path, self.label_root)
            deps = "、".join(sorted(program.external_labels)) or "无"
            if missing:
                self.script_status.setText("语法通过；缺少模板：" + "、".join(missing))
            else:
                self.script_status.setText(f"语法与模板检查通过 · 图像依赖：{deps} · 尚未执行")
        except Exception as exc:
            self.script_status.setText(f"检查未通过：{exc}")

    def run_script(self):
        if self.task:
            return
        try:
            text = self.prepared_text()
        except ValueError as exc:
            self.script_status.setText(str(exc))
            return
        path, label_root = self.path, self.label_root
        self.run_cancel = threading.Event()
        cancel = self.run_cancel
        self.console.clear()
        self.script_status.setText("正在检查并运行…")
        self.submit(lambda: self.session.run(text, path, cancel=cancel, label_root=label_root), self.script_finished)

    def script_finished(self, state):
        text = "脚本已停止，按键已释放。" if state == "cancelled" else "脚本执行完成，按键已释放。"
        self.script_status.setText(text)
        self.log_message.emit("\n" + text + "\n")

    def stop(self):
        if self.run_cancel:
            self.run_cancel.set()
        self.session.stop()
        self.script_status.setText("正在停止并释放按键…")

    def can_replace(self):
        changed = self.editor.document().isModified() or any(
            self.parameter_inputs[p.name].text() != p.default
            for p in parameters(self.parameter_source) if p.name in self.parameter_inputs)
        return not changed or QMessageBox.question(self, "替换编辑内容", "当前编辑内容或参数尚未保存。要载入另一份脚本吗？") == QMessageBox.StandardButton.Yes

    def load_selected(self):
        if self.library.currentData() and self.can_replace():
            self.load_path(Path(self.library.currentData()))

    def load_path(self, path):
        try:
            text, note = adapted_source(path)
            self.path = path
            self.library.setCurrentIndex(self.library.findData(str(path)))
            self.editor.setPlainText(text)
            self.editor.document().setModified(False)
            self.update_parameters()
            self.script_status.setText(f"已载入：{path.name}。{note}")
        except (OSError, UnicodeError) as exc:
            self.fail(str(exc))

    def open_script(self):
        if not self.can_replace():
            return
        path, _ = QFileDialog.getOpenFileName(self, "打开 ECS 脚本", "", "ECS 脚本 (*.ecs *.txt);;所有文件 (*)")
        if path:
            self.load_path(Path(path))

    def save_script(self):
        try:
            text = self.prepared_text()
        except ValueError as exc:
            self.script_status.setText(str(exc))
            return
        path, _ = QFileDialog.getSaveFileName(self, "另存为 ECS 脚本", str(Path.home() / (self.path.stem + ".ecs" if self.path else "剑盾脚本.ecs")), "ECS 脚本 (*.ecs);;文本 (*.txt)")
        if path:
            try:
                if Path(path).resolve().parent == SCRIPT_DIR.resolve():
                    raise ValueError("请另存到工作目录，保留脚本库原稿。")
                Path(path).write_text(text, encoding="utf-8-sig")
                self.path = Path(path)
                self.editor.setPlainText(text)
                self.editor.document().setModified(False)
                self.update_parameters()
                self.script_status.setText(f"已保存：{path}")
            except (OSError, ValueError) as exc:
                self.fail(str(exc))

    def choose_templates(self):
        path = QFileDialog.getExistingDirectory(self, "选择 ImgLabel 或其上级目录", self.label_root)
        if path:
            self.label_root = path
            self.template_status.setText(path)
            self.check_script()

    def shutdown(self):
        self.closing = True
        self.pad.release_all()
        if self.task:
            self.stop()
            return False
        if self.session.device.transport is not None:
            self.submit(self.session.disconnect)
            return False
        return True
