"""One raw-frame source for preview, OCR and EasyCon image labels."""
from collections import deque
from dataclasses import dataclass, field
import hashlib
import threading
import time

import numpy as np
from PySide6.QtGui import QImage


@dataclass(frozen=True, slots=True)
class FrameSnapshot:
    """A frame identity and timestamp paired with a private image copy."""

    frame_id: int
    captured_at_ns: int
    _image: QImage = field(repr=False, compare=False)
    source_timestamp_ns: int | None = None
    content_sha256: str = ""
    evidence_id: str | None = None

    @property
    def width(self):
        return self._image.width()

    @property
    def height(self):
        return self._image.height()

    def image(self) -> QImage:
        """Return a detached copy so consumers cannot mutate the stored frame."""
        return self._image.copy()


class FrameStore:
    def __init__(self, *, history_size=180, max_history_bytes=32 * 1024 * 1024):
        if history_size < 1:
            raise ValueError("history_size must be positive")
        if max_history_bytes < 1:
            raise ValueError("max_history_bytes must be positive")
        self.lock = threading.Lock()
        self.image = None
        self.updated = 0.0
        self.updated_ns = 0
        self.frame_id = 0
        self.history = deque(maxlen=history_size)
        self.max_history_bytes = max_history_bytes
        self.history_bytes = 0

    def receive(self, frame):
        image = frame.toImage()
        if not image.isNull():
            source_timestamp_ns = None
            try:
                source_timestamp = frame.startTime()
                if source_timestamp >= 0:
                    source_timestamp_ns = int(source_timestamp) * 1_000
            except (AttributeError, RuntimeError, TypeError, ValueError):
                pass
            self.put(image, source_timestamp_ns=source_timestamp_ns)

    def put(self, image, *, source_timestamp_ns=None, evidence_id=None):
        if image is None or image.isNull():
            return None
        copied = image.copy()
        digest = frame_digest(copied)
        with self.lock:
            captured_at_ns = time.monotonic_ns()
            self.frame_id += 1
            self.image = copied
            self.updated_ns = captured_at_ns
            self.updated = captured_at_ns / 1_000_000_000
            snapshot = FrameSnapshot(
                self.frame_id,
                captured_at_ns,
                copied.copy(),
                source_timestamp_ns=source_timestamp_ns,
                content_sha256=digest,
                evidence_id=evidence_id,
            )
            if len(self.history) == self.history.maxlen:
                self.history_bytes -= self.history[0]._image.sizeInBytes()
            self.history.append(snapshot)
            self.history_bytes += snapshot._image.sizeInBytes()
            while self.history_bytes > self.max_history_bytes and len(self.history) > 1:
                removed = self.history.popleft()
                self.history_bytes -= removed._image.sizeInBytes()
            return snapshot

    def clear(self):
        with self.lock:
            self.image = None
            self.updated = 0
            self.updated_ns = 0
            self.history.clear()
            self.history_bytes = 0

    def snapshot_frame(self, *, after_frame_id=None, max_age_seconds=2):
        if max_age_seconds <= 0:
            raise ValueError("max_age_seconds must be positive")
        with self.lock:
            if (
                self.image is None
                or time.monotonic() - self.updated > max_age_seconds
            ):
                raise RuntimeError("共享视频源没有新鲜画面，请先开启采集预览。")
            if after_frame_id is not None and self.frame_id <= after_frame_id:
                raise RuntimeError("尚未收到动作之后的新采集帧。")
            latest = self.history[-1] if self.history else None
            return FrameSnapshot(
                self.frame_id,
                self.updated_ns,
                self.image.copy(),
                source_timestamp_ns=latest.source_timestamp_ns if latest else None,
                content_sha256=latest.content_sha256 if latest else frame_digest(self.image),
                evidence_id=latest.evidence_id if latest else None,
            )

    def frames_after(self, frame_id, *, limit=None, max_age_seconds=2):
        """Return retained snapshots newer than ``frame_id`` in capture order."""
        if max_age_seconds <= 0:
            raise ValueError("max_age_seconds must be positive")
        with self.lock:
            if (
                self.image is None
                or time.monotonic() - self.updated > max_age_seconds
            ):
                raise RuntimeError("共享视频源没有新鲜画面，请先开启采集预览。")
            frames = [frame for frame in self.history if frame.frame_id > frame_id]
            if limit is not None:
                if limit < 0:
                    raise ValueError("limit must be non-negative")
                frames = frames[-limit:] if limit else []
            return tuple(
                FrameSnapshot(
                    frame.frame_id,
                    frame.captured_at_ns,
                    frame.image(),
                    source_timestamp_ns=frame.source_timestamp_ns,
                    content_sha256=frame.content_sha256,
                    evidence_id=frame.evidence_id,
                )
                for frame in frames
            )

    def snapshot(self):
        """Compatibility accessor for existing preview, OCR and label callers."""
        return self.snapshot_frame().image()

    def bgr(self):
        image = self.snapshot().convertToFormat(QImage.Format.Format_RGB888)
        # Respect Qt row alignment, and copy before the QImage is destroyed.
        raw = np.frombuffer(image.constBits(), dtype=np.uint8).reshape(image.height(), image.bytesPerLine())
        rgb = raw[:, :image.width() * 3].reshape(image.height(), image.width(), 3)
        return np.ascontiguousarray(rgb[:, :, ::-1])


def frame_digest(image):
    """Hash canonical RGBA pixels, independent of QImage storage padding."""
    rgba = image.convertToFormat(QImage.Format.Format_RGBA8888)
    buffer = memoryview(rgba.constBits()).cast("B")
    return hashlib.sha256(buffer[:rgba.sizeInBytes()]).hexdigest()
