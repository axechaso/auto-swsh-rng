"""One raw-frame source for preview, OCR and EasyCon image labels."""
import threading
import time

import numpy as np
from PySide6.QtGui import QImage


class FrameStore:
    def __init__(self):
        self.lock = threading.Lock()
        self.image = None
        self.updated = 0.0

    def receive(self, frame):
        image = frame.toImage()
        if not image.isNull():
            self.put(image)

    def put(self, image):
        with self.lock:
            self.image = image.copy()
            self.updated = time.monotonic()

    def clear(self):
        with self.lock:
            self.image = None
            self.updated = 0

    def snapshot(self):
        with self.lock:
            if self.image is None or time.monotonic() - self.updated > 2:
                raise RuntimeError("共享视频源没有新鲜画面，请先开启采集预览。")
            return self.image.copy()

    def bgr(self):
        image = self.snapshot().convertToFormat(QImage.Format.Format_RGB888)
        # Respect Qt row alignment, and copy before the QImage is destroyed.
        raw = np.frombuffer(image.constBits(), dtype=np.uint8).reshape(image.height(), image.bytesPerLine())
        rgb = raw[:, :image.width() * 3].reshape(image.height(), image.width(), 3)
        return np.ascontiguousarray(rgb[:, :, ::-1])
