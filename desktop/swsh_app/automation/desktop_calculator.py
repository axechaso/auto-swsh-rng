"""Cancellable subprocess bridge to the existing protocol-2 CLI."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import threading
import time

from ..backend import PROTOCOL_VERSION, validate_event


class CalculatorCancelled(RuntimeError):
    pass


class DesktopJsonCalculator:
    def __init__(self, executable, *, timeout_seconds=180):
        self.executable = Path(executable).expanduser().resolve()
        if isinstance(timeout_seconds, bool) or timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        self.timeout_seconds = timeout_seconds

    def execute(self, request, cancel_event):
        if not self.executable.is_file():
            raise FileNotFoundError(f"RNG backend is missing: {self.executable}")
        if cancel_event.is_set():
            raise CalculatorCancelled("calculator request cancelled before startup")
        if request.get("protocolVersion") != PROTOCOL_VERSION:
            raise ValueError("calculator request must use protocol version 2")

        environment = os.environ.copy()
        runtime = Path("D:/CodexTools/auto-swsh-rng/dotnet")
        if runtime.is_dir() and not environment.get("DOTNET_ROOT"):
            environment["DOTNET_ROOT"] = str(runtime)
        kwargs = {}
        if os.name == "nt":
            kwargs["creationflags"] = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        process = subprocess.Popen(
            [str(self.executable), "desktop-json"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=environment,
            **kwargs,
        )
        stdout_lines = []
        stderr_lines = []
        readers = [
            threading.Thread(target=_read_lines, args=(process.stdout, stdout_lines), daemon=True),
            threading.Thread(target=_read_lines, args=(process.stderr, stderr_lines), daemon=True),
        ]
        for reader in readers:
            reader.start()
        payload = json.dumps(request, ensure_ascii=False, separators=(",", ":")) + "\n"
        try:
            process.stdin.write(payload)
            process.stdin.flush()
            process.stdin.close()
            deadline = time.monotonic() + self.timeout_seconds
            while process.poll() is None:
                if cancel_event.is_set():
                    process.kill()
                    process.wait()
                    for reader in readers:
                        reader.join()
                    raise CalculatorCancelled("calculator request cancelled")
                if time.monotonic() >= deadline:
                    process.kill()
                    process.wait()
                    for reader in readers:
                        reader.join()
                    raise TimeoutError("RNG backend timed out")
                cancel_event.wait(0.025)
            for reader in readers:
                reader.join()
            return self._decode_response(stdout_lines, stderr_lines, request, process.returncode)
        except Exception:
            if process.poll() is None:
                process.kill()
                process.wait()
            for reader in readers:
                reader.join()
            raise

    @staticmethod
    def _decode_response(stdout_lines, stderr_lines, request, exit_code):
        result = None
        for line in stdout_lines:
            if not line.strip():
                continue
            try:
                event = json.loads(line.lstrip("\ufeff"))
                validate_event(event, request)
            except (ValueError, TypeError, KeyError) as exc:
                raise ValueError(f"RNG backend returned an invalid protocol event: {exc}") from exc
            if event.get("type") == "error":
                raise RuntimeError(str(event.get("message", "calculator request failed")))
            if event.get("type") == "result":
                result = event
        if exit_code != 0 or result is None:
            diagnostic = "".join(stderr_lines).strip()[-4000:]
            raise RuntimeError(diagnostic or f"RNG backend exited with code {exit_code} without a result")
        return result


def _read_lines(stream, target):
    try:
        target.extend(stream.readlines())
    finally:
        stream.close()
