"""Offline NPC calibration, independent revalidation, and scoped cache.

This module coordinates the existing ``calibration.probe.evaluate`` calculator
operation. It does not execute device actions; callers provide timestamped
experiments collected by a simulator, replay, or a separately authorized device
adapter. Every report remains diagnostic until the evidence workflow validates
real hardware captures.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime, timezone
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import tempfile
import threading
import uuid
from types import MappingProxyType
from typing import Any, Mapping, Protocol, Sequence

from ..backend import PROTOCOL_VERSION, validate_event


MAX_CANDIDATES_PER_REQUEST = 512
MAX_PROBE_PARAMETER = 100_000
PROBE_PARAMETERS = (
    "menuCloseNonPlayerCharacters",
    "areaLoadAdvances",
    "areaLoadNonPlayerCharacters",
    "rainTicks",
    "rainTicksBeforeMap",
    "rainTicksAfterMenu",
    "rainTicksDuringAreaLoad",
    "rainTicksBeforeEncounter",
    "interferenceAdvances",
)
PROBE_ACTIONS = {"menuclose", "rain", "areaload", "fly", "interference"}
CALIBRATION_CACHE_SCHEMA = "auto-swsh-npc-calibration-cache"
CALIBRATION_CACHE_VERSION = 1


class NpcCalibrationError(ValueError):
    pass


class NpcCalibrationNeedsAttention(RuntimeError):
    pass


class CalculatorPort(Protocol):
    def execute(self, request: dict[str, Any], cancel_event: threading.Event) -> dict[str, Any]: ...


@dataclass(frozen=True, slots=True)
class NpcCalibrationContext:
    game: str
    region: str
    subregion: str
    route_version: str
    weather: str
    menu_action: str
    hold_direction: bool
    start_position: Mapping[str, Any]
    camera_state: Mapping[str, Any]
    lead_setup: Mapping[str, Any]
    algorithm_commit: str
    _canonical_json: str = field(init=False, repr=False, compare=False)

    def __post_init__(self):
        for name in ("game", "region", "subregion", "route_version", "weather", "menu_action"):
            if not isinstance(getattr(self, name), str) or not getattr(self, name).strip():
                raise NpcCalibrationError(f"{name} is required for NPC cache applicability")
        if not isinstance(self.hold_direction, bool):
            raise NpcCalibrationError("hold_direction must be boolean")
        if not isinstance(self.algorithm_commit, str) or len(self.algorithm_commit) != 40 or any(
            value not in "0123456789abcdefABCDEF" for value in self.algorithm_commit
        ):
            raise NpcCalibrationError("algorithm_commit must be a 40-character hash")
        context = {
            "game": self.game,
            "region": self.region,
            "subregion": self.subregion,
            "routeVersion": self.route_version,
            "weather": self.weather,
            "menuAction": self.menu_action,
            "holdDirection": self.hold_direction,
            "startPosition": _json_object(self.start_position, "start_position"),
            "cameraState": _json_object(self.camera_state, "camera_state"),
            "leadSetup": _json_object(self.lead_setup, "lead_setup"),
            "algorithmCommit": self.algorithm_commit.lower(),
        }
        for name in ("start_position", "camera_state", "lead_setup"):
            value = _json_object(getattr(self, name), name)
            object.__setattr__(self, name, MappingProxyType(value))
        object.__setattr__(self, "algorithm_commit", self.algorithm_commit.lower())
        object.__setattr__(self, "_canonical_json", json.dumps(
            context, sort_keys=True, ensure_ascii=False, separators=(",", ":")
        ))

    def as_dict(self) -> dict[str, Any]:
        return json.loads(self._canonical_json)

    @property
    def cache_key(self) -> str:
        return _digest(self.as_dict())


@dataclass(frozen=True, slots=True)
class ProbeCandidate:
    candidate_id: str
    parameters: Mapping[str, int]

    def __post_init__(self):
        if not isinstance(self.candidate_id, str) or not self.candidate_id.strip():
            raise NpcCalibrationError("candidate_id is required")
        if not isinstance(self.parameters, Mapping) or not self.parameters:
            raise NpcCalibrationError("candidate parameters are required")
        if set(self.parameters) - set(PROBE_PARAMETERS):
            raise NpcCalibrationError("candidate contains an unsupported probe parameter")
        normalized: dict[str, int] = {}
        for name, value in self.parameters.items():
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= MAX_PROBE_PARAMETER:
                raise NpcCalibrationError(f"{name} must be an integer from 0 to {MAX_PROBE_PARAMETER}")
            normalized[name] = value
        object.__setattr__(self, "parameters", MappingProxyType(normalized))

    def to_protocol(self) -> dict[str, Any]:
        return {"candidateId": self.candidate_id, **dict(self.parameters)}

    def as_dict(self) -> dict[str, Any]:
        return {"candidateId": self.candidate_id, **dict(self.parameters)}


@dataclass(frozen=True, slots=True)
class ProbeExperiment:
    experiment_id: str
    seed0: str
    seed1: str
    observed_seed0: str
    observed_seed1: str
    probe_actions: Sequence[Mapping[str, Any]]
    source_kind: str = "replay"
    evidence_ids: Sequence[str] = ()
    observation_advances: int = 0
    observed_advances: int | None = None
    action_log: Sequence[Mapping[str, Any]] = ()

    def __post_init__(self):
        if not isinstance(self.experiment_id, str) or not self.experiment_id.strip():
            raise NpcCalibrationError("experiment_id is required")
        for name in ("seed0", "seed1", "observed_seed0", "observed_seed1"):
            object.__setattr__(self, name, _seed(getattr(self, name), name))
        object.__setattr__(self, "probe_actions", _validate_actions(self.probe_actions))
        if self.source_kind not in {"synthetic", "replay", "real"}:
            raise NpcCalibrationError("source_kind must be synthetic, replay, or real")
        evidence_ids = _string_tuple(self.evidence_ids, "evidence_ids", allow_empty=True)
        if self.source_kind == "real" and not evidence_ids:
            raise NpcCalibrationError("real probe experiments require evidence IDs")
        object.__setattr__(self, "evidence_ids", evidence_ids)
        for name, value in (("observation_advances", self.observation_advances),
                            ("observed_advances", self.observed_advances)):
            if value is not None and (isinstance(value, bool) or not isinstance(value, int) or value < 0):
                raise NpcCalibrationError(f"{name} must be a non-negative integer")
        object.__setattr__(self, "action_log", _validate_json_rows(self.action_log, "action_log"))

    @property
    def start_state(self) -> tuple[str, str]:
        return self.seed0, self.seed1


@dataclass(frozen=True, slots=True)
class ProbeDesign:
    design_id: str
    seed0: str
    seed1: str
    probe_actions: Sequence[Mapping[str, Any]]

    def __post_init__(self):
        if not isinstance(self.design_id, str) or not self.design_id.strip():
            raise NpcCalibrationError("design_id is required")
        object.__setattr__(self, "seed0", _seed(self.seed0, "seed0"))
        object.__setattr__(self, "seed1", _seed(self.seed1, "seed1"))
        object.__setattr__(self, "probe_actions", _validate_actions(self.probe_actions))


@dataclass(frozen=True, slots=True)
class CalibrationAccountingEntry:
    experiment_id: str
    role: str
    source_kind: str
    starting_state: Mapping[str, str]
    observed_after_probe: Mapping[str, str]
    candidate_advances: Mapping[str, int]
    observation_advances: int
    evidence_ids: tuple[str, ...]
    action_log: tuple[Mapping[str, Any], ...]

    def as_dict(self) -> dict[str, Any]:
        return {
            "experimentId": self.experiment_id,
            "role": self.role,
            "sourceKind": self.source_kind,
            "startingState": dict(self.starting_state),
            "observedAfterProbe": dict(self.observed_after_probe),
            "candidatePredictedProbeAdvances": dict(self.candidate_advances),
            "observationAdvances": self.observation_advances,
            "evidenceIds": list(self.evidence_ids),
            "actionLog": [dict(row) for row in self.action_log],
        }


@dataclass(frozen=True, slots=True)
class NpcCalibrationResult:
    status: str
    candidate_ids: tuple[str, ...]
    candidate: Mapping[str, Any] | None
    training_experiment_ids: tuple[str, ...]
    validation_experiment_ids: tuple[str, ...]
    accounting: tuple[CalibrationAccountingEntry, ...]
    context_key: str
    hardware_status: str = "PendingHardwareValidation"
    reason: str | None = None

    def as_report(self) -> dict[str, Any]:
        return {
            "schema": "auto-swsh-npc-calibration-report",
            "schemaVersion": 1,
            "status": self.status,
            "candidateIds": list(self.candidate_ids),
            "candidate": None if self.candidate is None else dict(self.candidate),
            "trainingExperimentIds": list(self.training_experiment_ids),
            "independentValidationExperimentIds": list(self.validation_experiment_ids),
            "accounting": [entry.as_dict() for entry in self.accounting],
            "contextKey": self.context_key,
            "hardwareStatus": self.hardware_status,
            "reason": self.reason,
        }


def build_probe_candidates(**parameter_values: Sequence[int]) -> tuple[ProbeCandidate, ...]:
    """Expand parameter dimensions into deterministic, stable candidate IDs."""
    unknown = set(parameter_values) - set(PROBE_PARAMETERS)
    if unknown:
        raise NpcCalibrationError(f"unsupported candidate dimensions: {sorted(unknown)}")
    dimensions = []
    for name in PROBE_PARAMETERS:
        values = parameter_values.get(name, (0,))
        if isinstance(values, range):
            choices = tuple(values)
        elif isinstance(values, Sequence) and not isinstance(values, (str, bytes)):
            choices = tuple(values)
        else:
            raise NpcCalibrationError(f"candidate dimension {name} must be a sequence")
        if not choices:
            raise NpcCalibrationError(f"candidate dimension {name} cannot be empty")
        for value in choices:
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= MAX_PROBE_PARAMETER:
                raise NpcCalibrationError(f"{name} candidate is outside the supported integer range")
        dimensions.append((name, tuple(dict.fromkeys(choices))))
    count = 1
    for _, values in dimensions:
        count *= len(values)
        if count > 100_000:
            raise NpcCalibrationError("candidate grid is too large to enumerate safely")

    candidates = []
    def expand(index: int, current: dict[str, int]):
        if index == len(dimensions):
            digest = _digest(current)[:16]
            candidates.append(ProbeCandidate(f"npc-{digest}", dict(current)))
            return
        name, values = dimensions[index]
        for value in values:
            current[name] = value
            expand(index + 1, current)

    expand(0, {})
    if len(candidates) > 100_000:
        raise NpcCalibrationError("candidate grid exceeds 100,000 combinations")
    return tuple(candidates)


class NpcCalibrationCache:
    """Atomic local cache keyed by the full scenario and algorithm context."""

    def __init__(self, path):
        self.path = Path(path).expanduser().resolve()

    def get(self, context: NpcCalibrationContext) -> dict[str, Any] | None:
        data = self._read()
        entry = data["entries"].get(context.cache_key)
        if entry is None:
            return None
        if entry.get("context") != context.as_dict():
            raise NpcCalibrationError("calibration cache context fingerprint is corrupt")
        return json.loads(json.dumps(entry, ensure_ascii=False))

    def put(self, context: NpcCalibrationContext, candidate: ProbeCandidate, *, experiment_ids):
        ids = _string_tuple(experiment_ids, "experiment_ids")
        data = self._read()
        data["entries"][context.cache_key] = {
            "context": context.as_dict(),
            "candidate": candidate.as_dict(),
            "experimentIds": list(ids),
            "hardwareStatus": "PendingHardwareValidation",
            "updatedAtUtc": _utc_now(),
            "lastRevalidatedAtUtc": None,
        }
        self._write(data)

    def mark_revalidated(self, context: NpcCalibrationContext, experiment_id: str):
        data = self._read()
        entry = data["entries"].get(context.cache_key)
        if entry is None:
            raise NpcCalibrationError("no applicable NPC calibration cache entry")
        entry["lastRevalidatedAtUtc"] = _utc_now()
        entry["experimentIds"] = list(dict.fromkeys([*entry["experimentIds"], experiment_id]))
        self._write(data)

    def invalidate(self, context: NpcCalibrationContext, reason: str):
        if not isinstance(reason, str) or not reason.strip():
            raise NpcCalibrationError("cache invalidation reason is required")
        data = self._read()
        if data["entries"].pop(context.cache_key, None) is not None:
            data["invalidations"].append({
                "contextKey": context.cache_key,
                "reason": reason.strip(),
                "invalidatedAtUtc": _utc_now(),
            })
            data["invalidations"] = data["invalidations"][-100:]
            self._write(data)

    def _read(self):
        if not self.path.exists():
            return {"schema": CALIBRATION_CACHE_SCHEMA, "schemaVersion": CALIBRATION_CACHE_VERSION,
                    "entries": {}, "invalidations": []}
        try:
            data = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise NpcCalibrationError(f"cannot read NPC calibration cache: {exc}") from exc
        if (not isinstance(data, dict) or data.get("schema") != CALIBRATION_CACHE_SCHEMA
                or data.get("schemaVersion") != CALIBRATION_CACHE_VERSION
                or not isinstance(data.get("entries"), dict)
                or not isinstance(data.get("invalidations"), list)):
            raise NpcCalibrationError("unsupported or malformed NPC calibration cache")
        for key, entry in data["entries"].items():
            if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{64}", key) or not isinstance(entry, dict):
                raise NpcCalibrationError("NPC calibration cache contains an invalid entry")
            if not isinstance(entry.get("context"), dict) or _digest(entry["context"]) != key:
                raise NpcCalibrationError("NPC calibration cache context fingerprint is invalid")
            raw_candidate = entry.get("candidate")
            if not isinstance(raw_candidate, dict) or not isinstance(raw_candidate.get("candidateId"), str):
                raise NpcCalibrationError("NPC calibration cache candidate is invalid")
            try:
                ProbeCandidate(raw_candidate["candidateId"], {
                    name: value for name, value in raw_candidate.items() if name != "candidateId"
                })
            except NpcCalibrationError as exc:
                raise NpcCalibrationError("NPC calibration cache candidate is invalid") from exc
            experiment_ids = entry.get("experimentIds")
            if (not isinstance(experiment_ids, list) or not experiment_ids
                    or any(not isinstance(item, str) or not item.strip() for item in experiment_ids)
                    or len(set(experiment_ids)) != len(experiment_ids)):
                raise NpcCalibrationError("NPC calibration cache experiment IDs are invalid")
            if entry.get("hardwareStatus") != "PendingHardwareValidation":
                raise NpcCalibrationError("NPC calibration cache cannot claim hardware validation")
            _validate_cache_timestamp(entry.get("updatedAtUtc"), allow_none=False)
            _validate_cache_timestamp(entry.get("lastRevalidatedAtUtc"), allow_none=True)
        for invalidation in data["invalidations"]:
            if (not isinstance(invalidation, dict)
                    or not isinstance(invalidation.get("contextKey"), str)
                    or not re.fullmatch(r"[0-9a-f]{64}", invalidation["contextKey"])
                    or not isinstance(invalidation.get("reason"), str)
                    or not invalidation["reason"].strip()):
                raise NpcCalibrationError("NPC calibration cache invalidation record is invalid")
            _validate_cache_timestamp(invalidation.get("invalidatedAtUtc"), allow_none=False)
        return data

    def _write(self, value):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", newline="\n", prefix=f".{self.path.name}.",
                suffix=".tmp", dir=self.path.parent, delete=False,
            ) as stream:
                temporary = Path(stream.name)
                json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.path)
        finally:
            if temporary is not None and temporary.exists():
                temporary.unlink()


class NpcCalibrationSession:
    def __init__(self, *, context: NpcCalibrationContext, candidates: Sequence[ProbeCandidate],
                 calculator: CalculatorPort, run_id: str, epoch_id: str, context_revision: int,
                 cache: NpcCalibrationCache | None = None, cancel_event: threading.Event | None = None,
                 minimum_training_experiments: int = 3, minimum_validation_experiments: int = 2,
                 candidate_batch_size: int = MAX_CANDIDATES_PER_REQUEST):
        if not candidates or len({item.candidate_id for item in candidates}) != len(candidates):
            raise NpcCalibrationError("calibration candidates must be non-empty with unique IDs")
        if not 1 <= candidate_batch_size <= MAX_CANDIDATES_PER_REQUEST:
            raise NpcCalibrationError("candidate_batch_size must be between 1 and 512")
        if isinstance(context_revision, bool) or not isinstance(context_revision, int) or context_revision < 0:
            raise NpcCalibrationError("context_revision must be a non-negative integer")
        if not run_id or not epoch_id:
            raise NpcCalibrationError("run_id and epoch_id are required")
        for name, value in (("minimum_training_experiments", minimum_training_experiments),
                            ("minimum_validation_experiments", minimum_validation_experiments)):
            if isinstance(value, bool) or not isinstance(value, int) or value < 1:
                raise NpcCalibrationError(f"{name} must be a positive integer")
        self.context = context
        self.candidates = tuple(candidates)
        self.calculator = calculator
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_revision = context_revision
        self.cache = cache
        self.cancel_event = cancel_event or threading.Event()
        self.minimum_training_experiments = minimum_training_experiments
        self.minimum_validation_experiments = minimum_validation_experiments
        self.candidate_batch_size = candidate_batch_size

    def calibrate(self, training_experiments: Sequence[ProbeExperiment],
                  validation_experiments: Sequence[ProbeExperiment] = ()) -> NpcCalibrationResult:
        training = tuple(training_experiments)
        validation = tuple(validation_experiments)
        self._validate_experiment_sets(training, validation)
        if not training:
            raise NpcCalibrationError("at least one training experiment is required")
        if len(training) > 8 or len(validation) > 5:
            raise NpcCalibrationError("experiment count exceeds the configured safe bound")

        active = {candidate.candidate_id: candidate for candidate in self.candidates}
        accounting: list[CalibrationAccountingEntry] = []
        used_training: list[str] = []
        used_validation: list[str] = []
        all_candidates = active.copy()
        for experiment in training:
            self._check_cancel()
            rows = self._evaluate(experiment, tuple(active.values()))
            matching = {candidate_id for candidate_id, row in rows.items() if row.get("matches") is True}
            accounting.append(self._account(experiment, "training", rows))
            used_training.append(experiment.experiment_id)
            active = {candidate_id: all_candidates[candidate_id] for candidate_id in active if candidate_id in matching}
            if not active:
                return self._result("ModelMismatch", (), None, used_training, (), accounting,
                                    "no candidate matches all training experiments")

        candidate_ids = tuple(active)
        if len(training) < self.minimum_training_experiments:
            status = "NeedsMoreExperiments"
        elif len(candidate_ids) > 1:
            status = "Ambiguous"
        else:
            status = "UniqueAwaitingIndependentValidation"
        if status != "UniqueAwaitingIndependentValidation":
            return self._result(status, candidate_ids, None, used_training, (), accounting)

        candidate_id = candidate_ids[0]
        candidate = active[candidate_id]
        if len(validation) < self.minimum_validation_experiments:
            return self._result(status, candidate_ids, candidate.as_dict(), used_training, (), accounting,
                                "unique training candidate still requires independent validation")

        for experiment in validation:
            self._check_cancel()
            rows = self._evaluate(experiment, (candidate,))
            accounting.append(self._account(experiment, "independent-validation", rows))
            used_validation.append(experiment.experiment_id)
            if rows[candidate_id].get("matches") is not True:
                if self.cache is not None:
                    self.cache.invalidate(self.context, "independent validation failed")
                return self._result(
                    "IndependentValidationFailed", (), None,
                    used_training, used_validation, accounting,
                    f"cached or trained candidate {candidate_id} failed independent validation",
                )

        result = self._result("CalibratedOffline", candidate_ids, candidate.as_dict(),
                              used_training, used_validation, accounting)
        if self.cache is not None:
            self.cache.put(self.context, candidate,
                           experiment_ids=(*used_training, *used_validation))
        return result

    def choose_discriminating_design(self, designs: Sequence[ProbeDesign]) -> dict[str, Any]:
        if not designs:
            raise NpcCalibrationError("at least one probe design is required")
        if len({item.design_id for item in designs}) != len(designs):
            raise NpcCalibrationError("probe design IDs must be unique")
        candidate_groups = []
        for design in designs:
            self._check_cancel()
            rows = self._evaluate_design(design)
            outcome_to_candidates: dict[tuple[str, int], list[str]] = {}
            for candidate_id, row in rows.items():
                outcome = (_digest(row["predictedEndState"]), row["predictedAdvances"])
                outcome_to_candidates.setdefault(outcome, []).append(candidate_id)
            pair_count = len(self.candidates) * (len(self.candidates) - 1) // 2
            unresolved_pairs = sum(len(group) * (len(group) - 1) // 2 for group in outcome_to_candidates.values())
            candidate_groups.append({
                "designId": design.design_id,
                "distinctOutcomes": len(outcome_to_candidates),
                "distinguishedPairs": pair_count - unresolved_pairs,
                "groups": [
                    {"predictedEndState": rows[ids[0]]["predictedEndState"], "predictedAdvances": advances,
                     "candidateIds": sorted(ids)}
                    for (_, advances), ids in sorted(outcome_to_candidates.items())
                ],
            })
        best = max(candidate_groups, key=lambda item: (item["distinguishedPairs"], item["distinctOutcomes"]))
        return {
            "status": "DiscriminatingDesignFound" if best["distinctOutcomes"] > 1 else "UnidentifiableWithProvidedDesigns",
            "recommendedDesignId": best["designId"] if best["distinctOutcomes"] > 1 else None,
            "designs": candidate_groups,
        }

    def revalidate_cached_candidate(self, experiment: ProbeExperiment) -> dict[str, Any]:
        if self.cache is None:
            return {"status": "CacheUnavailable", "candidate": None}
        entry = self.cache.get(self.context)
        if entry is None:
            return {"status": "CacheMiss", "candidate": None}
        raw = entry["candidate"]
        candidate = ProbeCandidate(raw["candidateId"], {
            key: value for key, value in raw.items() if key != "candidateId"
        })
        rows = self._evaluate(experiment, (candidate,))
        match = rows[candidate.candidate_id].get("matches") is True
        if match:
            self.cache.mark_revalidated(self.context, experiment.experiment_id)
            return {"status": "CacheRevalidated", "candidate": candidate.as_dict(),
                    "experimentId": experiment.experiment_id}
        self.cache.invalidate(self.context, "cached candidate failed a fresh applicability check")
        return {"status": "CacheInvalidated", "candidate": None,
                "experimentId": experiment.experiment_id}

    def _evaluate(self, experiment: ProbeExperiment,
                  candidates: Sequence[ProbeCandidate]) -> dict[str, Mapping[str, Any]]:
        rows: dict[str, Mapping[str, Any]] = {}
        for start in range(0, len(candidates), self.candidate_batch_size):
            batch = candidates[start:start + self.candidate_batch_size]
            data = self._request(
                seed0=experiment.seed0,
                seed1=experiment.seed1,
                observed_seed0=experiment.observed_seed0,
                observed_seed1=experiment.observed_seed1,
                observed_advances=experiment.observed_advances,
                probe_actions=experiment.probe_actions,
                probe_candidates=batch,
                source_kind=experiment.source_kind,
                evidence_ids=experiment.evidence_ids,
            )
            returned = data.get("candidates")
            if not isinstance(returned, list) or len(returned) != len(batch):
                raise NpcCalibrationNeedsAttention("probe calculator returned an incomplete candidate page")
            expected = {candidate.candidate_id for candidate in batch}
            for row in returned:
                if not isinstance(row, Mapping) or row.get("candidateId") not in expected:
                    raise NpcCalibrationNeedsAttention("probe calculator changed candidate identities")
                if row["candidateId"] in rows:
                    raise NpcCalibrationNeedsAttention("probe calculator duplicated a candidate")
                if not isinstance(row.get("predictedEndState"), Mapping):
                    raise NpcCalibrationNeedsAttention("probe calculator omitted predictedEndState")
                _seed(row["predictedEndState"].get("seed0"), "predictedEndState.seed0")
                _seed(row["predictedEndState"].get("seed1"), "predictedEndState.seed1")
                advances = row.get("predictedAdvances")
                if isinstance(advances, bool) or not isinstance(advances, int) or advances < 0:
                    raise NpcCalibrationNeedsAttention("probe calculator returned invalid predictedAdvances")
                matches = row.get("matches")
                if matches is not True and matches is not False:
                    raise NpcCalibrationNeedsAttention("probe calculator did not compare the observed state")
                rows[row["candidateId"]] = MappingProxyType(dict(row))
            if set(row["candidateId"] for row in returned) != expected:
                raise NpcCalibrationNeedsAttention("probe calculator omitted candidate identities")
        return rows

    def _evaluate_design(self, design: ProbeDesign) -> dict[str, Mapping[str, Any]]:
        rows: dict[str, Mapping[str, Any]] = {}
        for start in range(0, len(self.candidates), self.candidate_batch_size):
            batch = self.candidates[start:start + self.candidate_batch_size]
            data = self._request(seed0=design.seed0, seed1=design.seed1,
                                 probe_actions=design.probe_actions, probe_candidates=batch,
                                 source_kind="synthetic", evidence_ids=())
            returned = data.get("candidates")
            if not isinstance(returned, list) or len(returned) != len(batch):
                raise NpcCalibrationNeedsAttention("probe calculator returned an incomplete design evaluation")
            expected = {item.candidate_id for item in batch}
            for row in returned:
                if not isinstance(row, Mapping) or row.get("candidateId") not in expected:
                    raise NpcCalibrationNeedsAttention("probe calculator changed design candidate identities")
                if row["candidateId"] in rows:
                    raise NpcCalibrationNeedsAttention("probe calculator duplicated a design candidate")
                if row.get("matches") is not None:
                    raise NpcCalibrationNeedsAttention("design prediction unexpectedly included an observation")
                if not isinstance(row.get("predictedEndState"), Mapping):
                    raise NpcCalibrationNeedsAttention("design prediction omitted predictedEndState")
                _seed(row["predictedEndState"].get("seed0"), "predictedEndState.seed0")
                _seed(row["predictedEndState"].get("seed1"), "predictedEndState.seed1")
                advances = row.get("predictedAdvances")
                if isinstance(advances, bool) or not isinstance(advances, int) or advances < 0:
                    raise NpcCalibrationNeedsAttention("design prediction returned invalid predictedAdvances")
                rows[row["candidateId"]] = MappingProxyType(dict(row))
            if {row["candidateId"] for row in returned} != expected:
                raise NpcCalibrationNeedsAttention("probe calculator omitted design candidate identities")
        return rows

    def _request(self, *, seed0, seed1, probe_actions, probe_candidates, source_kind,
                 evidence_ids, observed_seed0=None, observed_seed1=None, observed_advances=None):
        self._check_cancel()
        request = {
            "operation": "calibration.probe.evaluate",
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": uuid.uuid4().hex,
            "runId": self.run_id,
            "epochId": self.epoch_id,
            "contextRevision": self.context_revision,
            "seed0": seed0,
            "seed1": seed1,
            "sourceKind": source_kind,
            "evidenceIds": list(evidence_ids),
            "probeActions": [dict(action) for action in probe_actions],
            "probeCandidates": [candidate.to_protocol() for candidate in probe_candidates],
        }
        if observed_seed0 is not None:
            request["observedSeed0"] = observed_seed0
            request["observedSeed1"] = observed_seed1
        if observed_advances is not None:
            request["observedAdvances"] = observed_advances
        response = self.calculator.execute(request, self.cancel_event)
        self._check_cancel()
        try:
            validate_event(response, request)
        except Exception as exc:
            raise NpcCalibrationNeedsAttention(f"probe response identity mismatch: {exc}") from exc
        if response.get("type") != "result" or not isinstance(response.get("data"), Mapping):
            raise NpcCalibrationNeedsAttention("probe calculator returned an invalid result")
        return response["data"]

    @staticmethod
    def _account(experiment, role, rows):
        return CalibrationAccountingEntry(
            experiment_id=experiment.experiment_id,
            role=role,
            source_kind=experiment.source_kind,
            starting_state=MappingProxyType({"seed0": experiment.seed0, "seed1": experiment.seed1}),
            observed_after_probe=MappingProxyType({
                "seed0": experiment.observed_seed0, "seed1": experiment.observed_seed1,
            }),
            candidate_advances=MappingProxyType({key: row["predictedAdvances"] for key, row in rows.items()}),
            observation_advances=experiment.observation_advances,
            evidence_ids=tuple(experiment.evidence_ids),
            action_log=tuple(MappingProxyType(dict(row)) for row in experiment.action_log),
        )

    def _result(self, status, candidate_ids, candidate, training_ids, validation_ids,
                accounting, reason=None):
        return NpcCalibrationResult(
            status=status,
            candidate_ids=tuple(candidate_ids),
            candidate=None if candidate is None else MappingProxyType(dict(candidate)),
            training_experiment_ids=tuple(training_ids),
            validation_experiment_ids=tuple(validation_ids),
            accounting=tuple(accounting),
            context_key=self.context.cache_key,
            reason=reason,
        )

    def _validate_experiment_sets(self, training, validation):
        all_experiments = (*training, *validation)
        ids = [item.experiment_id for item in all_experiments]
        if len(ids) != len(set(ids)):
            raise NpcCalibrationError("experiment IDs must be unique")
        starts = [item.start_state for item in all_experiments]
        if len(starts) != len(set(starts)):
            raise NpcCalibrationError("training and independent validation require distinct known RNG start states")

    def _check_cancel(self):
        if self.cancel_event.is_set():
            raise NpcCalibrationNeedsAttention("NPC calibration cancelled")


def _validate_actions(actions):
    if not isinstance(actions, Sequence) or isinstance(actions, (str, bytes)) or not 1 <= len(actions) <= 8:
        raise NpcCalibrationError("probe_actions must contain 1 to 8 actions")
    rows = []
    for action in actions:
        if not isinstance(action, Mapping):
            raise NpcCalibrationError("probe action must be a JSON object")
        value = _json_object(action, "probe action")
        kind = value.get("kind")
        if not isinstance(kind, str) or kind.strip().lower() not in PROBE_ACTIONS:
            raise NpcCalibrationError("probe action kind is unsupported")
        rows.append(MappingProxyType(value))
    return tuple(rows)


def _validate_json_rows(rows, name):
    if not isinstance(rows, Sequence) or isinstance(rows, (str, bytes)):
        raise NpcCalibrationError(f"{name} must be a sequence of JSON objects")
    if any(not isinstance(row, Mapping) for row in rows):
        raise NpcCalibrationError(f"{name} must contain only JSON objects")
    return tuple(MappingProxyType(_json_object(row, name)) for row in rows)


def _json_object(value, name):
    if not isinstance(value, Mapping):
        raise NpcCalibrationError(f"{name} must be a JSON object")
    try:
        result = json.loads(json.dumps(dict(value), ensure_ascii=False, sort_keys=True))
    except (TypeError, ValueError) as exc:
        raise NpcCalibrationError(f"{name} must contain JSON-compatible values") from exc
    if not isinstance(result, dict):
        raise NpcCalibrationError(f"{name} must be a JSON object")
    return result


def _seed(value, name):
    if not isinstance(value, str) or len(value) != 16 or any(char not in "0123456789abcdefABCDEF" for char in value):
        raise NpcCalibrationError(f"{name} must be exactly 16 hexadecimal characters")
    return value.lower()


def _string_tuple(values, name, *, allow_empty=False):
    if not isinstance(values, Sequence) or isinstance(values, (str, bytes)):
        raise NpcCalibrationError(f"{name} must be a sequence of strings")
    if any(not isinstance(item, str) or not item.strip() for item in values):
        raise NpcCalibrationError(f"{name} must contain non-empty strings")
    result = tuple(dict.fromkeys(item.strip() for item in values))
    if not result and not allow_empty:
        raise NpcCalibrationError(f"{name} cannot be empty")
    return result


def _digest(value):
    raw = json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return sha256(raw).hexdigest()


def _utc_now():
    return datetime.now(timezone.utc).isoformat()


def _validate_cache_timestamp(value, *, allow_none):
    if value is None and allow_none:
        return
    if not isinstance(value, str):
        raise NpcCalibrationError("NPC cache timestamps must be ISO-8601 strings")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise NpcCalibrationError("NPC cache timestamp is invalid") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise NpcCalibrationError("NPC cache timestamp must include a UTC offset")
