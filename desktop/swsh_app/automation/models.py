"""Immutable identity and result models for one automation search run."""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
import json
from types import MappingProxyType
from typing import Any, Mapping


class AutomationRunStatus(StrEnum):
    TARGETS_FOUND = "targets_found"
    CANCELLED = "cancelled"
    NEEDS_ATTENTION = "needs_attention"
    FAILED = "failed"


class AutomationPhase(StrEnum):
    PREFLIGHT = "preflight"
    RESTORE_SCENE = "restore_scene"
    OBSERVE_SEED = "observe_seed"
    VERIFY_SEED = "verify_seed"
    SEARCH_TARGETS = "search_targets"
    RESTART = "restart"
    CLEANUP = "cleanup"


@dataclass(frozen=True, slots=True)
class AutomationConfig:
    """Frozen search inputs for the M2 seed/search/restart loop.

    ``search_request`` contains the exact ordinary encounter filters used by
    the desktop search form. A private canonical copy prevents edits made by
    the caller after ``run`` starts from changing later search chunks.
    """

    run_id: str
    context_revision: int
    scenario_id: str
    min_advance: int
    max_advance: int
    search_request: Mapping[str, Any]
    chunk_size: int = 100_000
    max_seed_rounds: int = 2
    search_retries: int = 1
    max_epochs: int | None = None
    execution_mode: str = "simulation"
    framework_status: str = "NotImplemented"
    hardware_status: str = "PendingHardwareValidation"
    _search_request_json: str = field(init=False, repr=False, compare=False)

    def __post_init__(self):
        if not isinstance(self.run_id, str) or not self.run_id or len(self.run_id) > 128:
            raise ValueError("run_id must be a non-empty string of at most 128 characters")
        if isinstance(self.context_revision, bool) or not isinstance(self.context_revision, int) or self.context_revision < 0:
            raise ValueError("context_revision must be a non-negative integer")
        if not isinstance(self.scenario_id, str) or not self.scenario_id.strip():
            raise ValueError("scenario_id is required")
        for name, value in (("min_advance", self.min_advance), ("max_advance", self.max_advance)):
            if isinstance(value, bool) or not isinstance(value, int) or value < 0 or value > 1_000_000_000:
                raise ValueError(f"{name} must be an integer between 0 and 1,000,000,000")
        if self.max_advance < self.min_advance:
            raise ValueError("max_advance cannot be less than min_advance")
        if isinstance(self.chunk_size, bool) or not isinstance(self.chunk_size, int) or not 1 <= self.chunk_size <= 100_000:
            raise ValueError("chunk_size must be between 1 and 100,000")
        if isinstance(self.max_seed_rounds, bool) or not isinstance(self.max_seed_rounds, int) or not 1 <= self.max_seed_rounds <= 5:
            raise ValueError("max_seed_rounds must be between 1 and 5")
        if isinstance(self.search_retries, bool) or not isinstance(self.search_retries, int) or not 0 <= self.search_retries <= 3:
            raise ValueError("search_retries must be between 0 and 3")
        if self.max_epochs is not None and (
            isinstance(self.max_epochs, bool)
            or not isinstance(self.max_epochs, int)
            or self.max_epochs < 1
        ):
            raise ValueError("max_epochs must be positive when specified")
        if self.execution_mode not in {"simulation", "replay", "diagnostic", "formal"}:
            raise ValueError("execution_mode must be simulation, replay, diagnostic or formal")
        if self.framework_status not in {"NotImplemented", "FrameworkReady"}:
            raise ValueError("framework_status is invalid")
        if self.hardware_status not in {
            "PendingHardwareValidation", "HardwareValidated", "HardwareValidationFailed",
        }:
            raise ValueError("hardware_status is invalid")

        try:
            template = json.dumps(dict(self.search_request), ensure_ascii=False, separators=(",", ":"))
            frozen_template = json.loads(template)
        except (TypeError, ValueError) as exc:
            raise ValueError("search_request must contain JSON-compatible values") from exc
        if not isinstance(frozen_template, dict):
            raise ValueError("search_request must be a JSON object")
        object.__setattr__(self, "_search_request_json", template)
        # This public view is informational. The runner reconstructs requests
        # from the private JSON copy, so nested mutations cannot alter a run.
        object.__setattr__(self, "search_request", MappingProxyType(frozen_template))

    def search_request_copy(self) -> dict[str, Any]:
        return json.loads(self._search_request_json)

    @property
    def formal_start_allowed(self) -> bool:
        return (
            self.framework_status == "FrameworkReady"
            and self.hardware_status == "HardwareValidated"
        )


@dataclass(frozen=True, slots=True)
class AutomationEvent:
    run_id: str
    epoch_id: str | None
    phase: AutomationPhase
    message: str
    timestamp_ns: int
    details: Mapping[str, Any] = field(default_factory=dict)


@dataclass(frozen=True, slots=True)
class SeedEpochSummary:
    epoch_id: str
    seed0: str
    seed1: str
    observation_bits: int
    verification_bits: int
    searched_positions: int = 0
    candidate_count: int = 0
    candidate_rows_truncated: bool = False


@dataclass(frozen=True, slots=True)
class AutomationRunResult:
    run_id: str
    status: AutomationRunStatus
    phase: AutomationPhase
    epochs: tuple[SeedEpochSummary, ...]
    candidate_rows: tuple[Mapping[str, Any], ...] = ()
    reason: str | None = None
    events: tuple[AutomationEvent, ...] = ()
    total_epochs: int = 0
    total_events: int = 0
    event_history_truncated: bool = False
