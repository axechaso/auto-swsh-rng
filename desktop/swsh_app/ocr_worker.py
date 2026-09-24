"""Persistent PaddleOCR process; stdout is reserved for JSON, model logs go to stderr."""
from __future__ import annotations

import contextlib
import json
import math
import os
import sys
from pathlib import Path

DETECTION = "PP-OCRv5_mobile_det"
RECOGNITION = "PP-OCRv5_mobile_rec"


def crop_bounds(roi, width, height):
    if not isinstance(roi, list) or len(roi) != 4 or not all(isinstance(v, (float, int)) and math.isfinite(v) for v in roi):
        raise ValueError("OCR 区域必须是四个有效比例值。")
    x, y, w, h = roi
    if min(x, y) < 0 or min(w, h) <= 0 or x + w > 1.000001 or y + h > 1.000001:
        raise ValueError("OCR 区域超出画面。")
    left, top = round(x * width), round(y * height)
    right, bottom = min(width, round((x + w) * width)), min(height, round((y + h) * height))
    if right <= left or bottom <= top:
        raise ValueError("OCR 区域过小。")
    return left, top, right, bottom


def parse_prediction(raw):
    rows = []
    for item in raw:
        if not isinstance(item, dict):
            raise ValueError("OCR 返回格式不受支持，需要 PaddleOCR 3.x。")
        texts = item.get("rec_texts", [])
        scores = item.get("rec_scores", [])
        for text, confidence in zip(texts, scores):
            rows.append({"text": str(text), "confidence": float(confidence)})
    return rows


def main():
    for key in ("OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS", "NUMEXPR_NUM_THREADS"):
        os.environ[key] = "2"
    os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"
    # Keep native-library chatter off the JSON pipe too.
    wire = os.fdopen(os.dup(sys.stdout.fileno()), "w", encoding="utf-8", buffering=1)
    os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
    engine = None
    for line in sys.stdin:
        try:
            request = json.loads(line)
            with contextlib.redirect_stdout(sys.stderr):
                if engine is None:
                    from paddleocr import PaddleOCR
                    kwargs = dict(text_detection_model_name=DETECTION, text_recognition_model_name=RECOGNITION,
                                  use_doc_orientation_classify=False, use_doc_unwarping=False,
                                  use_textline_orientation=False, cpu_threads=2, enable_mkldnn=True,
                                  mkldnn_cache_capacity=1, device="cpu")
                    model_root = Path(os.environ["PADDLE_PDX_CACHE_HOME"]) / "official_models"
                    for name, option in ((DETECTION, "text_detection_model_dir"), (RECOGNITION, "text_recognition_model_dir")):
                        directory = model_root / name
                        if all((directory / f).is_file() for f in ("inference.json", "inference.pdiparams")):
                            kwargs[option] = str(directory)
                    engine = PaddleOCR(**kwargs)
                import cv2
                import numpy as np
                image = cv2.imdecode(np.fromfile(request["image"], dtype=np.uint8), cv2.IMREAD_COLOR)
                if image is None:
                    raise ValueError("无法读取 OCR 图像。")
                results = []
                for name, roi in request["regions"].items():
                    left, top, right, bottom = crop_bounds(roi, image.shape[1], image.shape[0])
                    rows = parse_prediction(engine.predict(image[top:bottom, left:right].copy()))
                    results.append({"field": name, "text": " ".join(row["text"] for row in rows),
                                    "confidence": min((row["confidence"] for row in rows), default=0.0), "rows": rows})
            event = {"type": "result", "data": results}
        except Exception as exc:
            event = {"type": "error", "message": str(exc)}
        wire.write(json.dumps(event, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main()
