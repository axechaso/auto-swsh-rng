"""Bounded image/video replay input for the production animation observer."""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path
import threading

from PySide6.QtGui import QImage

from ..capture import FrameSnapshot, frame_digest


REPLAY_SCHEMA = "auto-swsh-frame-replay"
REPLAY_SCHEMA_VERSION = 1
MAX_MANIFEST_BYTES = 8 * 1024 * 1024
MAX_IMAGE_BYTES = 64 * 1024 * 1024
MAX_VIDEO_BYTES = 2 * 1024 * 1024 * 1024
MAX_SEGMENT_FRAMES = 600


class ReplayValidationError(ValueError):
    pass


class VirtualClock:
    """A monotonic clock advanced by replay frame timestamps and sleep calls."""

    def __init__(self, start_ns=0):
        if isinstance(start_ns, bool) or not isinstance(start_ns, int) or start_ns < 0:
            raise ValueError("start_ns must be a non-negative integer")
        self._now_ns = start_ns
        self._lock = threading.Lock()

    def monotonic(self):
        with self._lock:
            return self._now_ns / 1_000_000_000

    def monotonic_ns(self):
        with self._lock:
            return self._now_ns

    def sleep(self, seconds):
        if seconds < 0 or not math.isfinite(seconds):
            raise ValueError("sleep duration must be finite and non-negative")
        self.advance(seconds)

    def advance(self, seconds):
        if seconds < 0 or not math.isfinite(seconds):
            raise ValueError("clock advance must be finite and non-negative")
        with self._lock:
            self._now_ns += int(seconds * 1_000_000_000)


@dataclass(frozen=True, slots=True)
class ReplayFrameRef:
    frame_id: int
    source_timestamp_ns: int
    evidence_id: str
    loader: object


@dataclass(frozen=True, slots=True)
class ReplayActionSegment:
    purpose: str
    index: int
    frames: tuple[ReplayFrameRef, ...]


class ReplayFrameSource:
    """Implements the same frame-source surface consumed by SeedObserver.

    ``trigger`` must be called by the injected simulated action port for each
    recorded action. The source then exposes only that action's frames, in
    capture order. It never infers missing frames or reuses a prior image.
    """

    def __init__(self, *, replay_id, scenario_id, source_kind, baseline, segments,
                 clock=None, close_source=None):
        self.replay_id = replay_id
        self.scenario_id = scenario_id
        self.source_kind = source_kind
        self.clock = clock or VirtualClock()
        self._close_source = close_source
        self._closed = False
        self.baseline = baseline
        self.segments = tuple(segments)
        self._segment_index = 0
        self._active_frames = None
        self._active_offset = 0
        self._dropped_frame_ids = set()
        self._latest = self._snapshot(baseline)
        self._last_source_timestamp_ns = baseline.source_timestamp_ns
        self._lock = threading.Lock()

    def trigger(self, purpose, index, *, drop_frames=False):
        with self._lock:
            self._ensure_open()
            if self._active_frames is not None and self._active_offset < len(self._active_frames):
                raise ReplayValidationError("previous replay action still has unread frames")
            if self._segment_index >= len(self.segments):
                raise ReplayValidationError("replay contains no matching action segment")
            segment = self.segments[self._segment_index]
            if (segment.purpose, segment.index) != (purpose, index):
                raise ReplayValidationError(
                    f"replay action mismatch: expected {segment.purpose}[{segment.index}], "
                    f"received {purpose}[{index}]"
                )
            self._active_frames = segment.frames
            self._active_offset = 0
            self._segment_index += 1
            if drop_frames:
                self._dropped_frame_ids.update(frame.frame_id for frame in segment.frames)

    def snapshot_frame(self, *, after_frame_id=None, max_age_seconds=2):
        del max_age_seconds  # Replay freshness is bounded by manifest order, not wall time.
        with self._lock:
            self._ensure_open()
            snapshot = self._latest
            if after_frame_id is not None and snapshot.frame_id <= after_frame_id:
                raise RuntimeError("replay has no newer frame")
            return FrameSnapshot(
                snapshot.frame_id,
                snapshot.captured_at_ns,
                snapshot.image(),
                source_timestamp_ns=snapshot.source_timestamp_ns,
                content_sha256=snapshot.content_sha256,
                evidence_id=snapshot.evidence_id,
            )

    def frames_after(self, frame_id, *, limit=None, max_age_seconds=2):
        del max_age_seconds
        if limit is not None and (isinstance(limit, bool) or limit < 0):
            raise ValueError("limit must be non-negative")
        if limit == 0:
            return ()
        with self._lock:
            self._ensure_open()
            if self._active_frames is None or self._active_offset >= len(self._active_frames):
                return ()
            frame_ref = self._active_frames[self._active_offset]
            if frame_ref.frame_id <= frame_id:
                return ()
            if frame_ref.frame_id in self._dropped_frame_ids:
                self._dropped_frame_ids.remove(frame_ref.frame_id)
                self._active_offset += 1
                return ()
            image = frame_ref.loader()
            if image is None or image.isNull():
                raise ReplayValidationError(f"cannot decode replay frame {frame_ref.frame_id}")
            if self._last_source_timestamp_ns is not None:
                delta_ns = frame_ref.source_timestamp_ns - self._last_source_timestamp_ns
                if delta_ns < 0:
                    raise ReplayValidationError("replay source timestamps moved backwards")
                self.clock.advance(delta_ns / 1_000_000_000)
            self._last_source_timestamp_ns = frame_ref.source_timestamp_ns
            snapshot = FrameSnapshot(
                frame_ref.frame_id,
                self.clock.monotonic_ns(),
                image.copy(),
                source_timestamp_ns=frame_ref.source_timestamp_ns,
                content_sha256=frame_digest(image),
                evidence_id=frame_ref.evidence_id,
            )
            self._latest = snapshot
            self._active_offset += 1
            return (snapshot,)

    def consume_active_segment(self):
        """Consume a non-observation action's recorded frames in order.

        Action adapters use this for coarse / precise / trigger stages whose
        frames provide timing and evidence boundaries but are not RNG bits.
        Dropped frames fail the stage instead of being silently skipped.
        """
        consumed = []
        with self._lock:
            self._ensure_open()
            if self._active_frames is None:
                raise ReplayValidationError("no replay action segment is active")
            while self._active_offset < len(self._active_frames):
                frame_ref = self._active_frames[self._active_offset]
                self._active_offset += 1
                if frame_ref.frame_id in self._dropped_frame_ids:
                    self._dropped_frame_ids.remove(frame_ref.frame_id)
                    raise ReplayValidationError(f"replay stage frame {frame_ref.frame_id} was dropped")
                image = frame_ref.loader()
                if image is None or image.isNull():
                    raise ReplayValidationError(f"cannot decode replay frame {frame_ref.frame_id}")
                if self._last_source_timestamp_ns is not None:
                    delta_ns = frame_ref.source_timestamp_ns - self._last_source_timestamp_ns
                    if delta_ns < 0:
                        raise ReplayValidationError("replay source timestamps moved backwards")
                    self.clock.advance(delta_ns / 1_000_000_000)
                self._last_source_timestamp_ns = frame_ref.source_timestamp_ns
                snapshot = FrameSnapshot(
                    frame_ref.frame_id,
                    self.clock.monotonic_ns(),
                    image.copy(),
                    source_timestamp_ns=frame_ref.source_timestamp_ns,
                    content_sha256=frame_digest(image),
                    evidence_id=frame_ref.evidence_id,
                )
                self._latest = snapshot
                consumed.append(snapshot)
            return tuple(consumed)

    @property
    def remaining_actions(self):
        return len(self.segments) - self._segment_index

    def drop_frame_once(self, frame_id):
        if isinstance(frame_id, bool) or not isinstance(frame_id, int) or frame_id < 1:
            raise ValueError("frame_id must be a positive integer")
        with self._lock:
            self._ensure_open()
            self._dropped_frame_ids.add(frame_id)

    def close(self):
        with self._lock:
            if self._closed:
                return
            self._closed = True
            if self._close_source is not None:
                self._close_source()

    def _ensure_open(self):
        if self._closed:
            raise ReplayValidationError("replay source is closed")

    def _snapshot(self, frame_ref):
        image = frame_ref.loader()
        if image is None or image.isNull():
            raise ReplayValidationError(f"cannot decode replay frame {frame_ref.frame_id}")
        return FrameSnapshot(
            frame_ref.frame_id,
            self.clock.monotonic_ns(),
            image.copy(),
            source_timestamp_ns=frame_ref.source_timestamp_ns,
            content_sha256=frame_digest(image),
            evidence_id=frame_ref.evidence_id,
        )


def load_replay_manifest(path, *, clock=None):
    """Load and validate a versioned replay package without trusting its paths."""
    manifest_path = Path(path).expanduser().resolve()
    try:
        if manifest_path.stat().st_size > MAX_MANIFEST_BYTES:
            raise ReplayValidationError("replay manifest exceeds the 8 MiB limit")
        raw = manifest_path.read_text(encoding="utf-8")
        manifest = json.loads(raw)
    except ReplayValidationError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ReplayValidationError(f"cannot read replay manifest: {exc}") from exc
    if not isinstance(manifest, dict):
        raise ReplayValidationError("replay manifest must be a JSON object")
    if manifest.get("schema") != REPLAY_SCHEMA or manifest.get("schemaVersion") != REPLAY_SCHEMA_VERSION:
        raise ReplayValidationError("unsupported replay schema")
    replay_id = _required_text(manifest, "replayId")
    scenario_id = _required_text(manifest, "scenarioId")
    source_kind = manifest.get("sourceKind")
    if source_kind not in ("real", "synthetic"):
        raise ReplayValidationError("sourceKind must be 'real' or 'synthetic'")
    source = manifest.get("source")
    actions = manifest.get("actions")
    if not isinstance(source, dict) or not isinstance(actions, list) or not actions:
        raise ReplayValidationError("source and non-empty actions are required")
    root = manifest_path.parent.resolve()
    frame_refs, close_source = _load_frames(root, replay_id, source, actions)
    try:
        baseline_id = _positive_id(manifest.get("baselineFrameId"), "baselineFrameId")
        if baseline_id not in frame_refs:
            raise ReplayValidationError("baselineFrameId is not present in source")
        if source.get("type") == "video":
            baseline_index = source.get("baselineFrameIndex")
            if baseline_id != baseline_index + 1:
                raise ReplayValidationError("baselineFrameId must match baselineFrameIndex + 1")
        baseline = frame_refs[baseline_id]
        segments = []
        prior_frame_id = baseline_id
        seen_actions = set()
        for action in actions:
            if not isinstance(action, dict):
                raise ReplayValidationError("each action must be an object")
            purpose = _required_text(action, "purpose")
            index = _positive_id(action.get("index"), "action.index", allow_zero=True)
            key = (purpose, index)
            if key in seen_actions:
                raise ReplayValidationError("replay action identities must be unique")
            seen_actions.add(key)
            ids = action.get("frameIds")
            if not isinstance(ids, list) or not ids or len(ids) > MAX_SEGMENT_FRAMES:
                raise ReplayValidationError("each action must reference 1 to 600 frames")
            if any(isinstance(value, bool) or not isinstance(value, int) for value in ids):
                raise ReplayValidationError("frameIds must be integers")
            if ids != sorted(set(ids)):
                raise ReplayValidationError("frameIds must be unique and increasing")
            if ids[0] <= prior_frame_id:
                raise ReplayValidationError("action frames must follow the baseline and prior actions")
            if any(frame_id not in frame_refs for frame_id in ids):
                raise ReplayValidationError("action references a frame absent from its source")
            segment = ReplayActionSegment(
                purpose,
                index,
                tuple(frame_refs[frame_id] for frame_id in ids),
            )
            segments.append(segment)
            prior_frame_id = ids[-1]
        return ReplayFrameSource(
            replay_id=replay_id,
            scenario_id=scenario_id,
            source_kind=source_kind,
            baseline=baseline,
            segments=segments,
            clock=clock,
            close_source=close_source,
        )
    except Exception:
        if close_source is not None:
            close_source()
        raise


def _load_frames(root, replay_id, source, actions):
    source_kind = source.get("type")
    if source_kind == "images":
        frames = source.get("frames")
        if not isinstance(frames, list) or not frames:
            raise ReplayValidationError("image source must include frames")
        result = {}
        previous_timestamp = -1
        total_bytes = 0
        for item in frames:
            if not isinstance(item, dict):
                raise ReplayValidationError("image frame entries must be objects")
            frame_id = _positive_id(item.get("frameId"), "frame.frameId")
            timestamp = _positive_id(item.get("sourceTimestampNs"), "frame.sourceTimestampNs", allow_zero=True)
            if (result and frame_id <= next(reversed(result))) or timestamp < previous_timestamp:
                raise ReplayValidationError("image frame IDs must be unique and timestamps non-decreasing")
            previous_timestamp = timestamp
            file_path = _safe_asset_path(root, item.get("path"))
            if file_path.stat().st_size > MAX_IMAGE_BYTES:
                raise ReplayValidationError("an image asset exceeds the 64 MiB limit")
            total_bytes += file_path.stat().st_size
            if total_bytes > MAX_VIDEO_BYTES:
                raise ReplayValidationError("replay image assets exceed the 2 GiB limit")
            expected_hash = _required_text(item, "sha256").lower()
            actual_hash = _file_sha256(file_path)
            if actual_hash != expected_hash:
                raise ReplayValidationError(f"image hash mismatch for {item.get('path')}")
            image_path = file_path
            result[frame_id] = ReplayFrameRef(
                frame_id,
                timestamp,
                f"{replay_id}:frame:{frame_id}",
                lambda image_path=image_path: _load_qimage(image_path),
            )
        return result, None

    if source_kind == "video":
        video_path = _safe_asset_path(root, source.get("path"))
        if video_path.stat().st_size > MAX_VIDEO_BYTES:
            raise ReplayValidationError("video asset exceeds the 2 GiB limit")
        expected_hash = _required_text(source, "sha256").lower()
        if _file_sha256(video_path) != expected_hash:
            raise ReplayValidationError("video asset hash mismatch")
        fps = source.get("fps")
        if isinstance(fps, bool) or not isinstance(fps, (int, float)) or not math.isfinite(fps) or not 1 <= fps <= 240:
            raise ReplayValidationError("video fps must be between 1 and 240")
        try:
            import cv2
        except ImportError as exc:
            raise ReplayValidationError("video replay requires the optional OpenCV runtime") from exc
        capture = cv2.VideoCapture(str(video_path))
        if not capture.isOpened():
            raise ReplayValidationError("cannot decode the replay video")

        def loader_for(frame_index):
            def load():
                capture.set(cv2.CAP_PROP_POS_FRAMES, frame_index)
                ok, bgr = capture.read()
                if not ok:
                    raise ReplayValidationError(f"cannot decode video frame {frame_index}")
                rgba = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGBA)
                height, width, _ = rgba.shape
                return QImage(rgba.data, width, height, int(rgba.strides[0]), QImage.Format.Format_RGBA8888).copy()
            return load

        requests = set()
        baseline_index = source.get("baselineFrameIndex")
        if isinstance(baseline_index, bool) or not isinstance(baseline_index, int) or baseline_index < 0:
            capture.release()
            raise ReplayValidationError("video baselineFrameIndex must be a non-negative integer")
        requests.add(baseline_index)
        for action in actions:
            if not isinstance(action, dict) or not isinstance(action.get("frameIds"), list):
                capture.release()
                raise ReplayValidationError("video actions must list frameIds")
            if len(action["frameIds"]) > MAX_SEGMENT_FRAMES:
                capture.release()
                raise ReplayValidationError("video action segment exceeds 600 frames")
            for frame_id in action["frameIds"]:
                if isinstance(frame_id, bool) or not isinstance(frame_id, int) or frame_id < 1:
                    capture.release()
                    raise ReplayValidationError("video frameIds must be positive integers")
                requests.add(frame_id - 1)
        count = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
        if any(index >= count for index in requests):
            capture.release()
            raise ReplayValidationError("video action references a frame beyond the clip")
        result = {}
        for frame_index in sorted(requests):
            frame_id = frame_index + 1
            timestamp = int(frame_index * 1_000_000_000 / fps)
            result[frame_id] = ReplayFrameRef(
                frame_id,
                timestamp,
                f"{replay_id}:video:{expected_hash[:16]}:frame:{frame_id}",
                loader_for(frame_index),
            )
        # The source wrapper owns the open decoder through the closures.
        return result, capture.release

    raise ReplayValidationError("source.type must be 'images' or 'video'")


def _safe_asset_path(root, value):
    if not isinstance(value, str) or not value.strip():
        raise ReplayValidationError("asset path is required")
    candidate = (root / value).resolve()
    try:
        candidate.relative_to(root)
    except ValueError as exc:
        raise ReplayValidationError("asset path escapes the replay directory") from exc
    if not candidate.is_file():
        raise ReplayValidationError(f"asset does not exist: {value}")
    return candidate


def _load_qimage(path):
    image = QImage(str(path))
    if image.isNull():
        raise ReplayValidationError(f"cannot load image asset: {path.name}")
    return image


def _file_sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _required_text(value, key):
    result = value.get(key)
    if not isinstance(result, str) or not result.strip():
        raise ReplayValidationError(f"{key} is required")
    return result


def _positive_id(value, name, *, allow_zero=False):
    minimum = 0 if allow_zero else 1
    if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
        raise ReplayValidationError(f"{name} must be an integer >= {minimum}")
    return value
