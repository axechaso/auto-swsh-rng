"""One-time application and bounded update of the generation-boundary offset b."""
from __future__ import annotations

from dataclasses import dataclass
import math


@dataclass(frozen=True, slots=True)
class BoundaryAdjustment:
    boundary_id: str
    offset: int
    model_version: str
    revision: int = 0

    def __post_init__(self):
        if not self.boundary_id or not self.model_version:
            raise ValueError("boundary_id and model_version are required")
        if isinstance(self.offset, bool) or not isinstance(self.offset, int):
            raise ValueError("offset must be an integer number of RNG advances")
        if isinstance(self.revision, bool) or not isinstance(self.revision, int) or self.revision < 0:
            raise ValueError("revision must be a non-negative integer")


class BoundaryApplicationLedger:
    """Prevents applying one boundary adjustment twice within an attempt."""

    def __init__(self):
        self._applied = set()

    def predict_generation(self, *, attempt_id, boundary_id, trigger_advance,
                           deterministic_consumption, adjustment: BoundaryAdjustment):
        if not attempt_id or not boundary_id:
            raise ValueError("attempt_id and boundary_id are required")
        if boundary_id != adjustment.boundary_id:
            raise ValueError("adjustment is bound to a different action boundary")
        for name, value in (("trigger_advance", trigger_advance),
                            ("deterministic_consumption", deterministic_consumption)):
            if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                raise ValueError(f"{name} must be a non-negative integer")
        key = (attempt_id, boundary_id, adjustment.model_version, adjustment.revision)
        if key in self._applied:
            raise ValueError("boundary adjustment was already applied for this attempt")
        self._applied.add(key)
        return trigger_advance + deterministic_consumption + adjustment.offset


def solve_trigger_advance(target_generation_advance, deterministic_consumption,
                          adjustment: BoundaryAdjustment):
    for name, value in (("target_generation_advance", target_generation_advance),
                        ("deterministic_consumption", deterministic_consumption)):
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise ValueError(f"{name} must be a non-negative integer")
    trigger = target_generation_advance - deterministic_consumption - adjustment.offset
    if trigger < 0:
        return None
    return trigger


def update_boundary_adjustment(current: BoundaryAdjustment, residual_advances: int | float, *,
                               alpha=0.5, minimum_offset=-100, maximum_offset=100,
                               maximum_step=4, model_version: str):
    if (isinstance(residual_advances, bool) or not isinstance(residual_advances, (int, float))
            or not math.isfinite(residual_advances)):
        raise ValueError("residual_advances must be a finite number")
    if isinstance(alpha, bool) or not isinstance(alpha, (int, float)) or not math.isfinite(alpha) or not 0 <= alpha <= 1:
        raise ValueError("alpha must be between zero and one")
    if minimum_offset > maximum_offset:
        raise ValueError("minimum_offset cannot exceed maximum_offset")
    if isinstance(maximum_step, bool) or not isinstance(maximum_step, int) or maximum_step < 0:
        raise ValueError("maximum_step must be a non-negative integer")
    if not model_version:
        raise ValueError("model_version is required")

    raw_delta = alpha * residual_advances
    delta = math.floor(raw_delta + 0.5) if raw_delta >= 0 else math.ceil(raw_delta - 0.5)
    delta = max(-maximum_step, min(maximum_step, delta))
    updated = max(minimum_offset, min(maximum_offset, current.offset + delta))
    return BoundaryAdjustment(
        boundary_id=current.boundary_id,
        offset=updated,
        model_version=model_version,
        revision=current.revision + 1,
    )
