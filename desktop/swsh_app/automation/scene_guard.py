"""Fresh-frame and stable-scene guard for sensitive automation stages."""
from __future__ import annotations

from dataclasses import dataclass
import threading


class SceneGuardError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class SceneObservation:
    scene_id: str | None
    frame_id: int
    captured_at_ns: int
    context_revision: int
    confidence: float
    evidence_id: str | None = None


@dataclass(frozen=True, slots=True)
class SceneAssessment:
    accepted: bool
    stable: bool
    scene_id: str | None
    frame_id: int | None
    context_revision: int
    reason_code: str | None
    evidence_ids: tuple[str, ...] = ()


class SceneGuard:
    """Requires consecutive, fresh classifier results before a stage starts."""

    def __init__(self, *, stable_frame_count=2, max_frame_age_ns=2_000_000_000,
                 minimum_confidence=0.9):
        if isinstance(stable_frame_count, bool) or not isinstance(stable_frame_count, int) or stable_frame_count < 1:
            raise ValueError("stable_frame_count must be positive")
        if isinstance(max_frame_age_ns, bool) or not isinstance(max_frame_age_ns, int) or max_frame_age_ns < 1:
            raise ValueError("max_frame_age_ns must be positive")
        if not 0 <= minimum_confidence <= 1:
            raise ValueError("minimum_confidence must be between zero and one")
        self.stable_frame_count = stable_frame_count
        self.max_frame_age_ns = max_frame_age_ns
        self.minimum_confidence = minimum_confidence
        self._observations = []
        self._latest_frame_id = 0
        self._lock = threading.Lock()

    def observe(self, observation: SceneObservation, *, expected_context_revision: int,
                now_ns: int) -> SceneAssessment:
        if not isinstance(observation, SceneObservation):
            raise TypeError("observation must be a SceneObservation")
        if expected_context_revision < 0 or now_ns < 0:
            raise ValueError("context revision and time must be non-negative")
        with self._lock:
            if observation.frame_id <= self._latest_frame_id:
                self._observations.clear()
                return self._reject("stale_frame", observation.context_revision)
            self._latest_frame_id = observation.frame_id
            if observation.context_revision != expected_context_revision:
                self._observations.clear()
                return self._reject("stale_context", observation.context_revision)
            age = now_ns - observation.captured_at_ns
            if age < 0 or age > self.max_frame_age_ns:
                self._observations.clear()
                return self._reject("stale_frame", observation.context_revision)
            if (
                not observation.scene_id
                or observation.confidence < self.minimum_confidence
                or observation.confidence > 1
            ):
                self._observations.clear()
                return self._reject("scene_unknown", observation.context_revision)
            if self._observations:
                previous = self._observations[-1]
                if (
                    observation.frame_id != previous.frame_id + 1
                    or observation.scene_id != previous.scene_id
                    or observation.context_revision != previous.context_revision
                ):
                    self._observations.clear()
            self._observations.append(observation)
            self._observations = self._observations[-self.stable_frame_count:]
            stable = len(self._observations) >= self.stable_frame_count
            return SceneAssessment(
                accepted=True,
                stable=stable,
                scene_id=observation.scene_id,
                frame_id=observation.frame_id,
                context_revision=observation.context_revision,
                reason_code=None if stable else "awaiting_stable_frames",
                evidence_ids=tuple(dict.fromkeys(
                    item.evidence_id for item in self._observations if item.evidence_id
                )),
            )

    def require(self, scene_id: str, *, expected_context_revision: int, now_ns: int):
        with self._lock:
            if not self._observations or len(self._observations) < self.stable_frame_count:
                raise SceneGuardError("SCENE_NOT_STABLE")
            latest = self._observations[-1]
            age = now_ns - latest.captured_at_ns
            if age < 0 or age > self.max_frame_age_ns:
                self._observations.clear()
                raise SceneGuardError("SCENE_FRAME_STALE")
            if latest.context_revision != expected_context_revision:
                raise SceneGuardError("SCENE_CONTEXT_STALE")
            if latest.scene_id != scene_id:
                raise SceneGuardError("SCENE_MISMATCH")
            return SceneAssessment(
                accepted=True,
                stable=True,
                scene_id=latest.scene_id,
                frame_id=latest.frame_id,
                context_revision=latest.context_revision,
                reason_code=None,
                evidence_ids=tuple(dict.fromkeys(
                    item.evidence_id for item in self._observations if item.evidence_id
                )),
            )

    def _reject(self, reason_code, context_revision):
        return SceneAssessment(
            accepted=False,
            stable=False,
            scene_id=None,
            frame_id=None,
            context_revision=context_revision,
            reason_code=reason_code,
        )
