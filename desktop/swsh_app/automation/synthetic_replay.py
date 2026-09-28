"""Deterministic image frames for offline exercise of the real seed observer."""
from __future__ import annotations

from PySide6.QtGui import QColor, QImage

from ..capture import frame_digest
from .replay import ReplayActionSegment, ReplayFrameRef, ReplayFrameSource, VirtualClock
from .seed_observer import AnimationFramePhase, AnimationFrameScore


def build_synthetic_seed_replay(
    observations: str, *, verification_observations: str | None = None,
    replay_id="synthetic-seed-run",
):
    """Build solve and verify segments whose pixels encode the supplied bits.

    These generated frames let the production ``SeedObserver`` and M2 runner
    run without a capture card. The returned classifier recognizes only this
    synthetic color encoding; it is not a game animation classifier.
    """
    if not isinstance(observations, str) or len(observations) != 128 or any(bit not in "01" for bit in observations):
        raise ValueError("observations must contain exactly 128 binary digits")
    verification_observations = verification_observations or observations
    if (
        not isinstance(verification_observations, str)
        or len(verification_observations) != 128
        or any(bit not in "01" for bit in verification_observations)
    ):
        raise ValueError("verification_observations must contain exactly 128 binary digits")
    if not isinstance(replay_id, str) or not replay_id:
        raise ValueError("replay_id is required")

    frame_id = 1
    timestamp_ns = 0
    idle = _image(QColor(25, 220, 25))
    baseline = _frame_ref(frame_id, timestamp_ns, replay_id, idle)
    segments = []
    frame_id += 1
    timestamp_ns += 10_000_000

    for purpose, bits in (("solve", observations), ("verify", verification_observations)):
        for index, bit in enumerate(bits):
            animation_frames = []
            for variant in range(2):
                if bit == "0":
                    color = QColor(160 + (index % 70), 25 + variant * 17, 30)
                else:
                    color = QColor(25, 30 + variant * 17, 160 + (index % 70))
                image = _image(color)
                animation_frames.append(_frame_ref(frame_id, timestamp_ns, replay_id, image))
                frame_id += 1
                timestamp_ns += 10_000_000
            idle_image = _image(QColor(25, 220, 25 + ((index + (0 if purpose == "solve" else 1)) % 20)))
            idle_frame = _frame_ref(frame_id, timestamp_ns, replay_id, idle_image)
            frame_id += 1
            timestamp_ns += 10_000_000
            segments.append(ReplayActionSegment(purpose, index, (*animation_frames, idle_frame)))

    source = ReplayFrameSource(
        replay_id=replay_id,
        scenario_id="synthetic-seed-observer",
        source_kind="synthetic",
        baseline=baseline,
        segments=segments,
        clock=VirtualClock(),
    )

    def classify(snapshot):
        color = snapshot.image().pixelColor(0, 0)
        if color.green() > 180 and color.red() < 80 and color.blue() < 80:
            return AnimationFrameScore(AnimationFramePhase.IDLE)
        bit = 0 if color.red() > color.blue() else 1
        return AnimationFrameScore(
            AnimationFramePhase.ANIMATION,
            zero_score=0.97 if bit == 0 else 0.03,
            one_score=0.97 if bit == 1 else 0.03,
            motion_score=0.95,
        )

    return source, classify


def _image(color):
    image = QImage(16, 16, QImage.Format.Format_RGBA8888)
    image.fill(color)
    return image


def _frame_ref(frame_id, timestamp_ns, replay_id, image):
    stable_image = image.copy()
    digest = frame_digest(stable_image)
    return ReplayFrameRef(
        frame_id=frame_id,
        source_timestamp_ns=timestamp_ns,
        evidence_id=f"{replay_id}:frame:{frame_id}:{digest[:12]}",
        loader=lambda stable_image=stable_image: stable_image.copy(),
    )
