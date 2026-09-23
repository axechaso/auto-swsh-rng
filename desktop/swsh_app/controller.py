"""One persistent native EasyCon session shared by scripts and manual input."""
from __future__ import annotations

import threading
from .vendor.easycon import ScriptCancelled
from .vendor.easycon.device import DeviceCancelledError, NintendoSwitchDevice, NativeGamePadAdapter, SwitchReport, to_ec_key


def manual_report(keys):
    """A complete snapshot preserves combinations and cancels opposing axes."""
    report = SwitchReport()
    directions = set()
    for key in keys:
        if key.startswith(("LS_", "RS_")):
            continue
        if key in ("TOP", "DOWN", "LEFT", "RIGHT"):
            directions.add(key)
        else:
            to_ec_key(key).down(report)
    x = int("RIGHT" in directions) - int("LEFT" in directions)
    y = int("DOWN" in directions) - int("TOP" in directions)
    report.hat = {(0, -1): 0, (1, -1): 1, (1, 0): 2, (1, 1): 3,
                  (0, 1): 4, (-1, 1): 5, (-1, 0): 6, (-1, -1): 7}.get((x, y), 8)
    for prefix, attr in (("LS", "l"), ("RS", "r")):
        for axis, positive, negative in (("x", "RIGHT", "LEFT"), ("y", "DOWN", "UP")):
            direction = int(f"{prefix}_{positive}" in keys) - int(f"{prefix}_{negative}" in keys)
            setattr(report, attr + axis, 255 if direction == 1 else 0 if direction == -1 else 128)
    return report


class ControllerSession:
    def __init__(self, frame_provider, *, device=None, log=None):
        self.device = device or NintendoSwitchDevice()
        self.gamepad = NativeGamePadAdapter(self.device)
        self.frame_provider = frame_provider
        self.log = log or (lambda text: None)
        self.gate = threading.Lock()
        self.cancel = threading.Event()

    @property
    def connected(self):
        return self.device.is_connected

    def _acquire(self):
        if not self.gate.acquire(blocking=False):
            raise RuntimeError("伊机控正在执行其他操作，请先停止或等待完成。")

    def connect(self, port):
        self._acquire()
        try:
            if str(port).lower() in ("mock", "memory", "none"):
                raise ValueError("请选择真实串口。模拟传输仅用于自动化测试。")
            if not self.device.connect(port):
                raise RuntimeError(f"无法连接 {port}：{self.device.failure or '伊机控握手失败'}")
            try:
                self.device.reset()
            except Exception:
                self.device.disconnect(release=True)
                raise
            self.log(f"伊机控已连接：{port} @ {self.device.connected_baudrate}")
        finally:
            self.gate.release()

    def disconnect(self):
        self._acquire()
        try:
            if not self.device.disconnect(release=True, timeout=2):
                raise RuntimeError("伊机控释放或断开失败，请检查设备连接。")
            self.log("伊机控已断开，按键已释放。")
        finally:
            self.gate.release()

    def press(self, key, duration=100):
        self._acquire()
        try:
            if not self.connected:
                raise RuntimeError("请先连接伊机控。")
            self.cancel.clear()
            self.gamepad.click_buttons(key, duration, self.cancel)
        finally:
            try:
                if self.connected:
                    self.device.reset()
            finally:
                self.gate.release()

    def stop(self):
        self.cancel.set()

    def manual(self, keys):
        self._acquire()
        try:
            if not self.connected:
                raise RuntimeError("请先连接伊机控。")
            # The device owns a FIFO report thread; no serial I/O runs in Qt.
            # Script startup/reset waits behind these queued press/release reports.
            self.device.apply_report(manual_report(keys), wait=False)
        finally:
            self.gate.release()

    def run(self, text, path=None, *, cancel=None, label_root=None):
        self._acquire()
        self.cancel = cancel if cancel is not None else threading.Event()
        try:
            if self.cancel.is_set():
                return "cancelled"
            from .script_library import inspect_script
            program, labels, missing = inspect_script(text, path, label_root)
            if missing:
                raise RuntimeError("缺少搜图模板：" + "、".join(missing) + "。请选择包含这些 .IL 文件的 ImgLabel 目录。")
            if program.has_gamepad_actions and not self.connected:
                raise RuntimeError("脚本含按键操作，请先连接伊机控。")
            getters = {}
            if program.requires_image_search:
                self.frame_provider()  # Fail before sending any buttons if capture is off.
                getters = labels.external_getters(self.frame_provider)
            if self.connected:
                self.device.reset()
            program.run(gamepad=self.gamepad if program.has_gamepad_actions else None,
                        external_getters=getters, output=self.log, cancel_event=self.cancel)
            return "completed"
        except (ScriptCancelled, DeviceCancelledError):
            return "cancelled"
        finally:
            try:
                if self.connected:
                    self.device.reset(wait=True)
            finally:
                self.gate.release()
