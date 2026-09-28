import json
import os
import sys
from pathlib import Path

from PySide6.QtCore import QObject, QProcess, QProcessEnvironment, QTimer, Signal


def default_ocr_python():
    local = Path("D:/CodexTools/auto-swsh-rng/ocr-venv/Scripts/python.exe")
    return str(local if local.is_file() else Path(sys.executable))


class OcrProcess(QObject):
    result = Signal(object)
    failed = Signal(str)
    settled = Signal()
    closed = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.process = QProcess(self)
        self.process.readyReadStandardOutput.connect(self.read)
        self.process.readyReadStandardError.connect(self.read_error)
        self.process.started.connect(self.send)
        self.process.finished.connect(self.finished)
        self.process.errorOccurred.connect(self.process_error)
        self.buffer = bytearray()
        self.errors = ""
        self.busy = False
        self.stopping = False
        self.request = None
        self.timer = QTimer(self)
        self.timer.setSingleShot(True)
        self.timer.timeout.connect(self.timeout)

    def start(self, request, python, cache):
        if self.busy or self.stopping:
            raise RuntimeError("OCR 正在识别，请等待或停止。")
        self.request = request
        self.busy = True
        self.errors = ""
        self.timer.start(180_000)
        if self.process.state() != QProcess.ProcessState.NotRunning:
            self.send()
            return
        self.buffer.clear()
        env = QProcessEnvironment.systemEnvironment()
        env.insert("PYTHONIOENCODING", "utf-8")
        env.insert("PADDLE_PDX_CACHE_HOME", str(cache))
        self.process.setProcessEnvironment(env)
        self.process.setProgram(python)
        self.process.setArguments([str(Path(__file__).with_name("ocr_worker.py"))])
        self.process.start()

    def send(self):
        if self.request:
            self.process.write((json.dumps(self.request, ensure_ascii=False) + "\n").encode("utf-8"))
            self.request = None

    def read_error(self):
        self.errors = (self.errors + bytes(self.process.readAllStandardError()).decode("utf-8", errors="replace"))[-3000:]

    def read(self):
        self.buffer.extend(bytes(self.process.readAllStandardOutput()))
        while b"\n" in self.buffer:
            line, _, self.buffer = self.buffer.partition(b"\n")
            if not self.busy:
                continue
            try:
                event = json.loads(line)
                self.busy = False
                self.timer.stop()
                if event["type"] == "result":
                    self.result.emit(event["data"])
                else:
                    self.failed.emit(event["message"])
            except (ValueError, KeyError, TypeError) as exc:
                self.busy = False
                self.timer.stop()
                self.failed.emit(f"OCR 返回数据无效：{exc}")
            self.settled.emit()

    def process_error(self, error):
        if error == QProcess.ProcessError.FailedToStart:
            self.failed.emit(f"无法启动 OCR Python：{self.process.errorString()}")
            self.busy = False
            self.timer.stop()
            self.settled.emit()
            self.closed.emit()

    def timeout(self):
        self.failed.emit("OCR 超时。首次加载需要下载模型，请检查网络或配置本地模型缓存。")
        self.stop()

    def stop(self):
        self.stopping = self.process.state() != QProcess.ProcessState.NotRunning
        self.busy = False
        self.request = None
        self.timer.stop()
        self.process.kill()
        self.settled.emit()

    def finished(self, code, *_):
        self.stopping = False
        self.read_error()
        if self.busy:
            self.failed.emit(f"OCR 进程已退出（{code}）：{self.errors or '请安装 requirements-ocr.txt'}")
            self.busy = False
            self.timer.stop()
            self.settled.emit()
        self.settled.emit()
        self.closed.emit()
