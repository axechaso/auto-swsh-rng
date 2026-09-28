"""Frame-causal animation sampling with explicit unknown outcomes."""
from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
import threading
import time


class AnimationFramePhase(StrEnum):
    IDLE = "idle"
    ANIMATION = "animation"
    UNKNOWN = "unknown"


@dataclass(frozen=True, slots=True)
class AnimationFrameScore:
    phase: AnimationFramePhase
    zero_score: float = 0.0
    one_score: float = 0.0
    motion_score: float = 0.0

    def __post_init__(self):
        for name, value in (
            ("zero_score", self.zero_score),
            ("one_score", self.one_score),
            ("motion_score", self.motion_score),
        ):
            if not 0.0 <= value <= 1.0:
                raise ValueError(f"{name} must be between zero and one")


@dataclass(frozen=True, slots=True)
class SeedBitObservation:
    bit: int | None
    reason: str | None
    frame_ids: tuple[int, ...]
    zero_score: float
    one_score: float
    motion_score: float
    started_at_ns: int
    ended_at_ns: int
    evidence_ids: tuple[str, ...] = ()


@dataclass(frozen=True, slots=True)
class SeedSequenceObservation:
    observations: str
    samples: tuple[SeedBitObservation, ...]
    complete: bool
    unknown_index: int | None


class SeedObserver:
    """Collects one bit per distinct, post-action animation cycle.

    ``classify`` must report IDLE / ANIMATION / UNKNOWN and confidence scores
    for both visual animation classes. It must return low motion scores for a
    frozen image. Missing frames, ambiguous scores and incomplete cycles never
    get coerced into a bit.
    """

    def __init__(
        self,
        frames,
        classify,
        *,
        timeout_seconds=3.0,
        poll_seconds=0.01,
        minimum_animation_frames=2,
        minimum_class_score=0.75,
        minimum_score_gap=0.12,
        minimum_motion_score=0.05,
        clock=None,
    ):
        if timeout_seconds <= 0 or poll_seconds <= 0:
            raise ValueError("observer timeouts must be positive")
        if minimum_animation_frames < 1:
            raise ValueError("minimum_animation_frames must be positive")
        for name, value in (
            ("minimum_class_score", minimum_class_score),
            ("minimum_score_gap", minimum_score_gap),
            ("minimum_motion_score", minimum_motion_score),
        ):
            if not 0.0 <= value <= 1.0:
                raise ValueError(f"{name} must be between zero and one")
        self.frames = frames
        self.classify = classify
        self.timeout_seconds = timeout_seconds
        self.poll_seconds = poll_seconds
        self.minimum_animation_frames = minimum_animation_frames
        self.minimum_class_score = minimum_class_score
        self.minimum_score_gap = minimum_score_gap
        self.minimum_motion_score = minimum_motion_score
        self.clock = clock or SystemClock()

    def observe_bit(self, trigger, *, cancel_event=None, timeout_seconds=None):
        cancel_event = cancel_event or threading.Event()
        timeout = self.timeout_seconds if timeout_seconds is None else timeout_seconds
        if timeout <= 0:
            raise ValueError("timeout_seconds must be positive")
        started_at_ns = self.clock.monotonic_ns()
        try:
            baseline = self.frames.snapshot_frame()
        except Exception as exc:
            return self._unknown(
                f"capture_unavailable:{exc}", [], [], [], [], started_at_ns,
                self.clock.monotonic_ns(), (),
            )
        deadline = self.clock.monotonic() + timeout
        last_frame_id = baseline.frame_id
        animation_started = False
        animation_ended = False
        frame_ids = []
        zero_scores = []
        one_scores = []
        motion_scores = []
        content_hashes = []
        evidence_ids = []
        reason = None

        try:
            trigger()
        except Exception as exc:
            return self._unknown(
                f"trigger_failed:{exc}", frame_ids, zero_scores, one_scores,
                motion_scores, started_at_ns, self.clock.monotonic_ns(), evidence_ids,
            )

        while self.clock.monotonic() < deadline:
            if cancel_event.is_set():
                reason = "cancelled"
                break
            try:
                snapshots = self.frames.frames_after(last_frame_id)
            except RuntimeError as exc:
                reason = f"capture_unavailable:{exc}"
                break
            if not snapshots:
                self.clock.sleep(self.poll_seconds)
                continue

            for snapshot in snapshots:
                if snapshot.frame_id != last_frame_id + 1:
                    reason = "capture_gap"
                    break
                last_frame_id = snapshot.frame_id
                if snapshot.evidence_id:
                    evidence_ids.append(snapshot.evidence_id)
                try:
                    result = self.classify(snapshot)
                except Exception as exc:
                    reason = f"classification_failed:{exc}"
                    break
                if not isinstance(result, AnimationFrameScore):
                    reason = "invalid_classification_result"
                    break
                if result.phase == AnimationFramePhase.ANIMATION:
                    animation_started = True
                    frame_ids.append(snapshot.frame_id)
                    if snapshot.content_sha256:
                        content_hashes.append(snapshot.content_sha256)
                    zero_scores.append(result.zero_score)
                    one_scores.append(result.one_score)
                    motion_scores.append(result.motion_score)
                elif result.phase == AnimationFramePhase.IDLE and animation_started:
                    animation_ended = True
                    break

            if reason or animation_ended:
                break
            self.clock.sleep(self.poll_seconds)

        if reason is None and not animation_started:
            reason = "animation_not_seen"
        elif reason is None and not animation_ended:
            reason = "animation_did_not_end"
        elif reason is None and len(frame_ids) < self.minimum_animation_frames:
            reason = "insufficient_animation_frames"
        elif reason is None and max(motion_scores, default=0.0) < self.minimum_motion_score:
            reason = "frozen_animation"
        elif reason is None and content_hashes and len(set(content_hashes)) < 2:
            reason = "frozen_animation"

        average_zero = sum(zero_scores) / len(zero_scores) if zero_scores else 0.0
        average_one = sum(one_scores) / len(one_scores) if one_scores else 0.0
        average_motion = sum(motion_scores) / len(motion_scores) if motion_scores else 0.0
        if reason is None:
            gap = abs(average_one - average_zero)
            score = max(average_zero, average_one)
            if score < self.minimum_class_score or gap < self.minimum_score_gap:
                reason = "ambiguous_animation"
            else:
                bit = 1 if average_one > average_zero else 0
                return SeedBitObservation(
                    bit,
                    None,
                    tuple(frame_ids),
                    average_zero,
                    average_one,
                    average_motion,
                    started_at_ns,
                    self.clock.monotonic_ns(),
                    tuple(dict.fromkeys(evidence_ids)),
                )

        return self._unknown(
            reason,
            frame_ids,
            zero_scores,
            one_scores,
            motion_scores,
            started_at_ns,
            self.clock.monotonic_ns(),
            evidence_ids,
        )

    def observe_sequence(self, count, trigger, *, cancel_event=None):
        if count < 1:
            raise ValueError("count must be positive")
        samples = []
        bits = []
        for index in range(count):
            sample = self.observe_bit(
                lambda index=index: trigger(index),
                cancel_event=cancel_event,
            )
            samples.append(sample)
            if sample.bit is None:
                return SeedSequenceObservation(
                    "".join(str(value) for value in bits),
                    tuple(samples),
                    False,
                    index,
                )
            bits.append(sample.bit)
        return SeedSequenceObservation("".join(str(value) for value in bits), tuple(samples), True, None)

    @staticmethod
    def _unknown(reason, frame_ids, zero_scores, one_scores, motion_scores, started_at_ns,
                 ended_at_ns, evidence_ids):
        return SeedBitObservation(
            None,
            reason,
            tuple(frame_ids),
            sum(zero_scores) / len(zero_scores) if zero_scores else 0.0,
            sum(one_scores) / len(one_scores) if one_scores else 0.0,
            sum(motion_scores) / len(motion_scores) if motion_scores else 0.0,
            started_at_ns,
            ended_at_ns,
            tuple(dict.fromkeys(evidence_ids)),
        )


class SystemClock:
    def monotonic(self):
        return time.monotonic()

    def monotonic_ns(self):
        return time.monotonic_ns()

    def sleep(self, seconds):
        time.sleep(seconds)
