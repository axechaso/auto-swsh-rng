"""Asynchronous, read-only JSON bridge to the existing .NET RNG services."""
from __future__ import annotations

import json
import os
from pathlib import Path

from PySide6.QtCore import QObject, QProcess, QProcessEnvironment, QTimer, Signal


def find_backend(explicit: str | None = None) -> Path | None:
    root = Path(__file__).resolve().parents[1]
    configured = explicit or os.environ.get("SWSH_RNG_BACKEND")
    if configured:
        path = Path(configured).expanduser()
        return path.resolve() if path.is_file() else None
    candidates = [
        root / "backend/AutoSwshRng.Cli.exe",
        root.parent / "artifacts/bin/AutoSwshRng.Cli/release/AutoSwshRng.Cli.exe",
        root.parent / "artifacts/bin/AutoSwshRng.Cli/debug/AutoSwshRng.Cli.exe",
        root.parent / "src/AutoSwshRng.Cli/bin/Debug/net10.0-windows/AutoSwshRng.Cli.exe",
        Path("D:/CodexTools/auto-swsh-rng/artifacts/bin/AutoSwshRng.Cli/debug/AutoSwshRng.Cli.exe"),
    ]
    return next((p.resolve() for p in candidates if p.is_file()), None)


class JsonJob(QObject):
    progress = Signal(int, int)
    result = Signal(object)
    failed = Signal(str)
    done = Signal()

    def __init__(self, executable: Path, request: dict, parent=None):
        super().__init__(parent)
        self.request = request
        self.cancelled = False
        self.settled = False
        self.payload = None
        self.error = ""
        self.buffer = bytearray()
        self.process = QProcess(self)
        environment = QProcessEnvironment.systemEnvironment()
        # The locally provisioned SDK also contains the desktop runtime.
        runtime = Path("D:/CodexTools/auto-swsh-rng/dotnet")
        if runtime.is_dir() and not environment.contains("DOTNET_ROOT"):
            environment.insert("DOTNET_ROOT", str(runtime))
        self.process.setProcessEnvironment(environment)
        self.process.setProgram(str(executable))
        self.process.setArguments(["desktop-json"])
        self.process.started.connect(self._write_request)
        self.process.readyReadStandardOutput.connect(self._read)
        self.process.readyReadStandardError.connect(self._read_error)
        self.process.errorOccurred.connect(self._process_error)
        self.process.finished.connect(self._finish)
        self.timer = QTimer(self)
        self.timer.setSingleShot(True)
        self.timer.timeout.connect(self._timeout)

    def start(self):
        self.process.start()
        self.timer.start(180_000 if self.request.get("operation") == "search" else 30_000)

    def _write_request(self):
        self.process.write((json.dumps(self.request, ensure_ascii=False) + "\n").encode("utf-8"))
        self.process.closeWriteChannel()

    def _read_error(self):
        self.error = (self.error + bytes(self.process.readAllStandardError()).decode("utf-8", errors="replace"))[-4000:]

    def _read(self):
        self.buffer.extend(bytes(self.process.readAllStandardOutput()))
        while b"\n" in self.buffer:
            line, _, self.buffer = self.buffer.partition(b"\n")
            if not line.strip():
                continue
            try:
                event = json.loads(line.decode("utf-8-sig"))
                if event["type"] == "progress":
                    self.progress.emit(event["completed"], event["total"])
                elif event["type"] == "result":
                    self.payload = event["data"]
                elif event["type"] == "error":
                    self.error = event["message"]
                else:
                    raise ValueError("未知消息类型")
            except (ValueError, KeyError, TypeError) as exc:
                self.error = f"乱数服务返回了无效数据：{exc}。请重新构建后端。"

    def _process_error(self, error):
        if error == QProcess.ProcessError.FailedToStart:
            self.error = f"无法启动乱数服务：{self.process.errorString()}"
            self._finish(-1)

    def _timeout(self):
        self.error = "乱数服务超时，请缩小搜索范围后重试。"
        self.process.kill()

    def cancel(self):
        self.cancelled = True
        self.timer.stop()
        self.process.kill()  # This process owns no device and makes no persistent writes.

    def _finish(self, code, *_):
        if self.settled:
            return
        self.settled = True
        self.timer.stop()
        self._read()
        self._read_error()
        if not self.cancelled:
            if code == 0 and self.payload is not None and not self.error:
                self.result.emit(self.payload)
            else:
                self.failed.emit(self.error.strip() or "乱数服务未返回结果；请检查 .NET 10 Desktop Runtime 并重新构建后端。")
        self.done.emit()
