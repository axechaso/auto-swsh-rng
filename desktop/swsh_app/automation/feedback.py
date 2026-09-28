"""Evidence-gated, bounded feedback for generation and timing models."""
from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
import json
import math
import os
import re
import statistics
import tempfile
from typing import Any, Mapping

from .boundary import BoundaryAdjustment, update_boundary_adjustment


FEEDBACK_ARCHIVE_SCHEMA = "auto-swsh-feedback-archive"
FEEDBACK_ARCHIVE_VERSION = 1
MAX_FEEDBACK_RECORDS = 10_000
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class FeedbackError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class CalibrationSample:
    sample_id: str
    attempt_id: str
    source_kind: str
    context_fingerprint: str
    target_advance: int
    boundary_id: str
    boundary_model_version: str
    boundary_model_revision: int
    reverse_candidate_advances: tuple[int, ...]
    reverse_complete: bool
    reverse_truncated: bool
    reverse_eligible_for_calibration: bool
    relocation_checkpoint_matches: bool
    npc_and_scene_valid: bool
    new_entity_identity_confirmed: bool
    generation_boundary_confirmed: bool
    evidence_ids: tuple[str, ...]

    def __post_init__(self):
        for name in ("sample_id", "attempt_id", "context_fingerprint", "boundary_id",
                     "boundary_model_version"):
            value = getattr(self, name)
            if not isinstance(value, str) or not value.strip():
                raise FeedbackError(f"{name} is required")
        if not SHA256_RE.fullmatch(self.context_fingerprint):
            raise FeedbackError("context_fingerprint must be a SHA-256 digest")
        if self.source_kind not in {"synthetic", "replay", "real"}:
            raise FeedbackError("source_kind is invalid")
        for name in ("target_advance",):
            value = getattr(self, name)
            if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 1_000_000_000:
                raise FeedbackError(f"{name} must be a valid RNG advance")
        if (isinstance(self.boundary_model_revision, bool)
                or not isinstance(self.boundary_model_revision, int)
                or self.boundary_model_revision < 0):
            raise FeedbackError("boundary_model_revision must be non-negative")
        candidates = tuple(self.reverse_candidate_advances)
        if any(isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 1_000_000_000
               for value in candidates):
            raise FeedbackError("reverse candidates must be valid RNG advances")
        object.__setattr__(self, "reverse_candidate_advances", candidates)
        for name in ("reverse_complete", "reverse_truncated", "reverse_eligible_for_calibration",
                     "relocation_checkpoint_matches", "npc_and_scene_valid",
                     "new_entity_identity_confirmed", "generation_boundary_confirmed"):
            if not isinstance(getattr(self, name), bool):
                raise FeedbackError(f"{name} must be boolean")
        evidence = tuple(dict.fromkeys(self.evidence_ids))
        if any(not isinstance(value, str) or not value.strip() for value in evidence):
            raise FeedbackError("evidence_ids must contain non-empty identifiers")
        object.__setattr__(self, "evidence_ids", evidence)


@dataclass(frozen=True, slots=True)
class FeedbackDecision:
    sample_id: str
    accepted_for_model: bool
    residual_advances: int | None
    reason_code: str
    boundary_before: BoundaryAdjustment
    boundary_after: BoundaryAdjustment
    recalibration_required: bool
    archive_revision: int

    def as_dict(self):
        return {
            "sampleId": self.sample_id,
            "acceptedForModel": self.accepted_for_model,
            "residualAdvances": self.residual_advances,
            "reasonCode": self.reason_code,
            "boundaryBefore": _boundary_dict(self.boundary_before),
            "boundaryAfter": _boundary_dict(self.boundary_after),
            "recalibrationRequired": self.recalibration_required,
            "archiveRevision": self.archive_revision,
        }


class FeedbackArchiveStore:
    """Atomic durable archive shared by boundary, timing, and retry state."""

    def __init__(self, path):
        self.path = Path(path).expanduser().resolve()

    def load(self):
        try:
            document = json.loads(self.path.read_text(encoding="utf-8"))
        except FileNotFoundError:
            return {
                "schema": FEEDBACK_ARCHIVE_SCHEMA,
                "schemaVersion": FEEDBACK_ARCHIVE_VERSION,
                "archiveRevision": 0,
                "contextFingerprint": None,
                "boundaryModel": None,
                "timingModel": None,
                "retryWorkflow": None,
            }
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise FeedbackError(f"cannot read feedback archive: {exc}") from exc
        if (not isinstance(document, dict) or document.get("schema") != FEEDBACK_ARCHIVE_SCHEMA
                or document.get("schemaVersion") != FEEDBACK_ARCHIVE_VERSION
                or isinstance(document.get("archiveRevision"), bool)
                or not isinstance(document.get("archiveRevision"), int)
                or document["archiveRevision"] < 0):
            raise FeedbackError("feedback archive schema or revision is invalid")
        return document

    def save_section(self, section: str, value: Mapping[str, Any], *, context_fingerprint: str):
        if section not in {"boundaryModel", "timingModel", "retryWorkflow"}:
            raise FeedbackError("feedback archive section is invalid")
        if not isinstance(context_fingerprint, str) or not SHA256_RE.fullmatch(context_fingerprint):
            raise FeedbackError("context_fingerprint must be a SHA-256 digest")
        document = self.load()
        current_context = document.get("contextFingerprint")
        if current_context is not None and current_context != context_fingerprint:
            raise FeedbackError("feedback archive context changed; old model is invalidated")
        try:
            normalized = json.loads(json.dumps(dict(value), ensure_ascii=False, allow_nan=False))
        except (TypeError, ValueError) as exc:
            raise FeedbackError("feedback archive section must be JSON data") from exc
        document["contextFingerprint"] = context_fingerprint
        document[section] = normalized
        document["archiveRevision"] += 1
        self._write(document)
        return document["archiveRevision"]

    def _write(self, document):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", newline="\n", prefix=f".{self.path.name}.",
                suffix=".tmp", dir=self.path.parent, delete=False,
            ) as stream:
                temporary = Path(stream.name)
                json.dump(document, stream, ensure_ascii=False, indent=2, sort_keys=True, allow_nan=False)
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.path)
        finally:
            if temporary is not None and temporary.exists():
                temporary.unlink()


def attribute_calibration_sample(sample: CalibrationSample, *, expected_context_fingerprint: str):
    """Apply §13.1's trust checks in order before deriving e = h - g."""
    if not isinstance(sample, CalibrationSample):
        raise FeedbackError("sample must be a CalibrationSample")
    if not sample.evidence_ids:
        return False, None, "OBSERVATION_EVIDENCE_MISSING"
    if not sample.reverse_complete or sample.reverse_truncated:
        return False, None, "REVERSE_WINDOW_INCOMPLETE"
    if len(sample.reverse_candidate_advances) != 1:
        reason = "REVERSE_NO_UNIQUE_CANDIDATE" if sample.reverse_candidate_advances else "REVERSE_NO_CANDIDATE"
        return False, None, reason
    if not sample.reverse_eligible_for_calibration:
        return False, None, "REVERSE_RESULT_NOT_CALIBRATION_ELIGIBLE"
    if not sample.relocation_checkpoint_matches:
        return False, None, "PRECISE_CHECKPOINT_MISMATCH"
    if not sample.npc_and_scene_valid:
        return False, None, "NPC_OR_SCENE_INVALID"
    if not sample.new_entity_identity_confirmed:
        return False, None, "NEW_ENTITY_IDENTITY_UNCONFIRMED"
    if not sample.generation_boundary_confirmed:
        return False, None, "GENERATION_BOUNDARY_UNCONFIRMED"
    if sample.context_fingerprint != expected_context_fingerprint:
        return False, None, "MODEL_CONTEXT_CHANGED"
    if sample.source_kind != "real":
        return False, None, "NON_REAL_EVIDENCE_CANNOT_CALIBRATE"
    return True, sample.reverse_candidate_advances[0] - sample.target_advance, "TRUSTED_UNIQUE_RESIDUAL"


class FeedbackController:
    """Persist samples and apply b updates only from trusted unique real hits."""

    def __init__(self, *, initial_adjustment: BoundaryAdjustment,
                 context_fingerprint: str, archive: FeedbackArchiveStore,
                 update_window: int = 3, evaluation_window: int = 3,
                 alpha: float = 0.5, minimum_offset: int = -100,
                 maximum_offset: int = 100, maximum_step: int = 4,
                 maximum_residual: int = 64, worsening_margin: float = 1.0):
        if not isinstance(initial_adjustment, BoundaryAdjustment):
            raise FeedbackError("initial_adjustment must be a BoundaryAdjustment")
        if not isinstance(archive, FeedbackArchiveStore):
            raise FeedbackError("archive must be a FeedbackArchiveStore")
        if not SHA256_RE.fullmatch(context_fingerprint):
            raise FeedbackError("context_fingerprint must be a SHA-256 digest")
        if isinstance(update_window, bool) or not isinstance(update_window, int) or update_window < 1:
            raise FeedbackError("update_window must be positive")
        if isinstance(evaluation_window, bool) or not isinstance(evaluation_window, int) or evaluation_window < 2:
            raise FeedbackError("evaluation_window must be at least two")
        if isinstance(alpha, bool) or not isinstance(alpha, (int, float)) or not math.isfinite(alpha) or not 0 <= alpha <= 1:
            raise FeedbackError("alpha must be between zero and one")
        if isinstance(maximum_residual, bool) or not isinstance(maximum_residual, int) or maximum_residual < 1:
            raise FeedbackError("maximum_residual must be positive")
        for name, value in (("minimum_offset", minimum_offset), ("maximum_offset", maximum_offset),
                            ("maximum_step", maximum_step)):
            if isinstance(value, bool) or not isinstance(value, int):
                raise FeedbackError(f"{name} must be an integer")
        if minimum_offset > maximum_offset or maximum_step < 0:
            raise FeedbackError("boundary update limits are invalid")
        if not minimum_offset <= initial_adjustment.offset <= maximum_offset:
            raise FeedbackError("initial boundary offset is outside configured limits")
        if not math.isfinite(worsening_margin) or worsening_margin < 0:
            raise FeedbackError("worsening_margin must be finite and non-negative")
        self.archive = archive
        self.context_fingerprint = context_fingerprint
        self.update_window = update_window
        self.evaluation_window = evaluation_window
        self.alpha = alpha
        self.minimum_offset = minimum_offset
        self.maximum_offset = maximum_offset
        self.maximum_step = maximum_step
        self.maximum_residual = maximum_residual
        self.worsening_margin = worsening_margin
        self.initial_adjustment = initial_adjustment
        self.current_adjustment = initial_adjustment
        self.stable_adjustment = initial_adjustment
        self.records: list[dict[str, Any]] = []
        self.pending_residuals: list[int] = []
        self.update_basis_residuals: tuple[int, ...] = ()
        self.post_update_residuals: list[int] = []
        self.recalibration_required = False
        self.status = "Stable"
        self.archive_revision = 0
        self._seen_sample_ids = set()
        self._restore()

    def ingest(self, sample: CalibrationSample) -> FeedbackDecision:
        before = self.current_adjustment
        if sample.sample_id in self._seen_sample_ids:
            return FeedbackDecision(sample.sample_id, False, None, "DUPLICATE_SAMPLE_REJECTED",
                                    before, before, self.recalibration_required, self.archive_revision)
        eligible, residual, reason = attribute_calibration_sample(
            sample, expected_context_fingerprint=self.context_fingerprint,
        )
        if eligible and (
            sample.boundary_id != self.current_adjustment.boundary_id
            or sample.boundary_model_version != self.current_adjustment.model_version
            or sample.boundary_model_revision != self.current_adjustment.revision
        ):
            eligible, residual, reason = False, None, "SAMPLE_MODEL_VERSION_MISMATCH"
        if eligible and abs(residual) > self.maximum_residual:
            eligible, reason = False, "RESIDUAL_OUTSIDE_UPDATE_BOUND"
            self.status = "NeedsRecalibration"
            self.recalibration_required = True
        row = {
            "sampleId": sample.sample_id,
            "attemptId": sample.attempt_id,
            "timestampUtc": datetime.now(timezone.utc).isoformat(),
            "acceptedForModel": eligible,
            "residualAdvances": residual,
            "reasonCode": reason,
            "evidenceIds": list(sample.evidence_ids),
            "sourceKind": sample.source_kind,
            "contextFingerprint": sample.context_fingerprint,
            "boundaryId": sample.boundary_id,
            "boundaryModelVersion": sample.boundary_model_version,
            "boundaryModelRevision": sample.boundary_model_revision,
            "modelBefore": _boundary_dict(before),
            "modelAfter": _boundary_dict(before),
        }
        if len(self.records) >= MAX_FEEDBACK_RECORDS:
            raise FeedbackError("feedback record limit reached; archive rotation is required")
        self.records.append(row)
        self._seen_sample_ids.add(sample.sample_id)
        if eligible:
            self._apply_residual(residual, row)
            eligible = row["acceptedForModel"] is True
        self.archive_revision = self.archive.save_section(
            "boundaryModel", self.as_dict(), context_fingerprint=self.context_fingerprint,
        )
        return FeedbackDecision(
            sample.sample_id, eligible, residual if eligible else None, row["reasonCode"],
            before, self.current_adjustment, self.recalibration_required, self.archive_revision,
        )

    def as_dict(self):
        return {
            "status": self.status,
            "initialAdjustment": _boundary_dict(self.initial_adjustment),
            "currentAdjustment": _boundary_dict(self.current_adjustment),
            "stableAdjustment": _boundary_dict(self.stable_adjustment),
            "pendingResiduals": list(self.pending_residuals),
            "updateBasisResiduals": list(self.update_basis_residuals),
            "postUpdateResiduals": list(self.post_update_residuals),
            "recalibrationRequired": self.recalibration_required,
            "records": self.records,
        }

    def _apply_residual(self, residual, row):
        if self.recalibration_required:
            row["acceptedForModel"] = False
            row["reasonCode"] = "MODEL_REQUIRES_RECALIBRATION"
            row["residualAdvances"] = None
            return
        if self.current_adjustment.revision == self.stable_adjustment.revision:
            self.pending_residuals.append(residual)
            row["reasonCode"] = "COLLECTING_RESIDUAL_WINDOW"
            if len(self.pending_residuals) >= self.update_window:
                basis = tuple(self.pending_residuals[-self.update_window:])
                median_residual = statistics.median(basis)
                previous = self.current_adjustment
                self.current_adjustment = update_boundary_adjustment(
                    previous, median_residual,
                    alpha=self.alpha,
                    minimum_offset=self.minimum_offset,
                    maximum_offset=self.maximum_offset,
                    maximum_step=self.maximum_step,
                    model_version=f"{previous.model_version}+feedback-{previous.revision + 1}",
                )
                self.update_basis_residuals = basis
                self.pending_residuals.clear()
                row["reasonCode"] = "BOUNDARY_OFFSET_UPDATED"
                row["modelAfter"] = _boundary_dict(self.current_adjustment)
            return

        self.post_update_residuals.append(residual)
        if len(self.post_update_residuals) < self.evaluation_window:
            row["reasonCode"] = "UPDATED_MODEL_UNDER_REVIEW"
            return
        baseline_error = statistics.median(abs(value) for value in self.update_basis_residuals)
        observed_error = statistics.median(abs(value) for value in self.post_update_residuals[-self.evaluation_window:])
        signs = [1 if value > 0 else -1 if value < 0 else 0
                 for value in self.post_update_residuals[-self.evaluation_window:]]
        oscillating = all(signs[index] * signs[index + 1] == -1
                          for index in range(len(signs) - 1))
        if observed_error > baseline_error + self.worsening_margin or (
            oscillating and observed_error >= baseline_error and baseline_error > 0
        ):
            previous = self.current_adjustment
            self.current_adjustment = BoundaryAdjustment(
                boundary_id=self.stable_adjustment.boundary_id,
                offset=self.stable_adjustment.offset,
                model_version=f"rollback-{self.stable_adjustment.model_version}-r{previous.revision + 1}",
                revision=previous.revision + 1,
            )
            self.recalibration_required = True
            self.status = "RolledBackNeedsRecalibration"
            row["acceptedForModel"] = False
            row["reasonCode"] = "UPDATE_ROLLED_BACK_RESIDUALS_WORSENED"
            row["residualAdvances"] = None
            row["modelAfter"] = _boundary_dict(self.current_adjustment)
            self.pending_residuals.clear()
            self.post_update_residuals.clear()
        else:
            self.stable_adjustment = self.current_adjustment
            self.update_basis_residuals = tuple(self.post_update_residuals[-self.evaluation_window:])
            self.post_update_residuals.clear()
            row["reasonCode"] = "UPDATED_MODEL_STABLE"
            row["modelAfter"] = _boundary_dict(self.current_adjustment)

    def _restore(self):
        document = self.archive.load()
        if document.get("contextFingerprint") not in (None, self.context_fingerprint):
            raise FeedbackError("feedback archive context changed; old model requires revalidation")
        model = document.get("boundaryModel")
        if model is None:
            return
        try:
            initial = _boundary_from_dict(model["initialAdjustment"])
            current = _boundary_from_dict(model["currentAdjustment"])
            stable = _boundary_from_dict(model["stableAdjustment"])
            records = model["records"]
            if (initial.boundary_id != self.initial_adjustment.boundary_id
                    or initial.offset != self.initial_adjustment.offset
                    or initial.model_version != self.initial_adjustment.model_version
                    or not isinstance(records, list) or len(records) > MAX_FEEDBACK_RECORDS):
                raise ValueError("archived boundary model does not match configured baseline")
        except (KeyError, TypeError, ValueError) as exc:
            raise FeedbackError(f"feedback boundary archive is invalid: {exc}") from exc
        self.initial_adjustment = initial
        self.current_adjustment = current
        self.stable_adjustment = stable
        self.records = records
        self._seen_sample_ids = {
            row.get("sampleId") for row in records
            if isinstance(row, Mapping) and isinstance(row.get("sampleId"), str)
        }
        self.pending_residuals = list(model.get("pendingResiduals", ()))
        self.update_basis_residuals = tuple(model.get("updateBasisResiduals", ()))
        self.post_update_residuals = list(model.get("postUpdateResiduals", ()))
        self.recalibration_required = model.get("recalibrationRequired") is True
        self.status = model.get("status", "Stable")
        self.archive_revision = document["archiveRevision"]


@dataclass(frozen=True, slots=True)
class TimingUpdate:
    status: str
    current_delay_ms: int
    proposed_delay_ms: int
    slope_advances_per_ms: float | None
    reason_code: str

    def as_dict(self):
        return asdict(self)


class TimingSensitivityEstimator:
    """Enable a timing update only after repeated stable one-variable probes."""

    def __init__(self, *, parameter_id: str, context_fingerprint: str,
                 archive: FeedbackArchiveStore | None = None,
                 minimum_pairs: int = 3, minimum_slope: float = 0.02,
                 maximum_relative_variation: float = 0.25):
        if not parameter_id or not SHA256_RE.fullmatch(context_fingerprint):
            raise FeedbackError("parameter_id and SHA-256 context_fingerprint are required")
        if isinstance(minimum_pairs, bool) or not isinstance(minimum_pairs, int) or minimum_pairs < 2:
            raise FeedbackError("minimum_pairs must be at least two")
        if isinstance(minimum_slope, bool) or not isinstance(minimum_slope, (int, float)) or not math.isfinite(minimum_slope) or minimum_slope <= 0:
            raise FeedbackError("minimum_slope must be positive")
        if (isinstance(maximum_relative_variation, bool)
                or not isinstance(maximum_relative_variation, (int, float))
                or not math.isfinite(maximum_relative_variation)
                or not 0 <= maximum_relative_variation <= 1):
            raise FeedbackError("maximum_relative_variation must be between zero and one")
        self.parameter_id = parameter_id
        self.context_fingerprint = context_fingerprint
        self.archive = archive
        self.minimum_pairs = minimum_pairs
        self.minimum_slope = minimum_slope
        self.maximum_relative_variation = maximum_relative_variation
        self.pairs: list[dict[str, Any]] = []
        self.updates: list[dict[str, Any]] = []
        self._pair_ids = set()
        self._sample_ids = set()
        self._restore()

    def add_pair(self, *, pair_id: str, delay_minus_ms: int, delay_plus_ms: int,
                 sample_minus: CalibrationSample, sample_plus: CalibrationSample,
                 control_fingerprint: str):
        if not pair_id or pair_id in self._pair_ids:
            raise FeedbackError("timing probe pair_id must be unique")
        if not isinstance(sample_minus, CalibrationSample) or not isinstance(sample_plus, CalibrationSample):
            raise FeedbackError("timing probes must include two CalibrationSample values")
        eligible_minus, residual_minus, reason_minus = attribute_calibration_sample(
            sample_minus, expected_context_fingerprint=self.context_fingerprint,
        )
        eligible_plus, residual_plus, reason_plus = attribute_calibration_sample(
            sample_plus, expected_context_fingerprint=self.context_fingerprint,
        )
        if not eligible_minus or not eligible_plus:
            raise FeedbackError(
                "timing sensitivity requires two trusted samples: "
                f"{reason_minus}; {reason_plus}"
            )
        if (sample_minus.boundary_id != sample_plus.boundary_id
                or sample_minus.boundary_model_revision != sample_plus.boundary_model_revision
                or sample_minus.boundary_model_version != sample_plus.boundary_model_version):
            raise FeedbackError("timing sensitivity pairs must use one fixed boundary model")
        if not control_fingerprint or control_fingerprint != self.context_fingerprint:
            raise FeedbackError("timing probe controls or context changed")
        if any(isinstance(value, bool) or not isinstance(value, int)
               for value in (delay_minus_ms, delay_plus_ms)):
            raise FeedbackError("timing probe delays must be integers")
        values = (delay_minus_ms, residual_minus, delay_plus_ms, residual_plus)
        if any(isinstance(value, bool) or not isinstance(value, int) for value in values):
            raise FeedbackError("timing probe delays and residuals must be integers")
        delta_delay = delay_plus_ms - delay_minus_ms
        if delta_delay == 0:
            raise FeedbackError("paired probes must change the selected timing parameter")
        sample_ids = {sample_minus.sample_id, sample_plus.sample_id}
        if len(sample_ids) != 2 or sample_ids & self._sample_ids:
            raise FeedbackError("timing sensitivity must use distinct, unreused attempt samples")
        boundary_model_revision = sample_minus.boundary_model_revision
        evidence = tuple(dict.fromkeys((*sample_minus.evidence_ids, *sample_plus.evidence_ids)))
        if self.pairs and any(row["boundaryModelRevision"] != boundary_model_revision for row in self.pairs):
            raise FeedbackError("timing slope pairs must use one fixed boundary model revision")
        slope = (residual_plus - residual_minus) / delta_delay
        self._pair_ids.add(pair_id)
        self._sample_ids.update(sample_ids)
        self.pairs.append({
            "pairId": pair_id,
            "sampleIds": sorted(sample_ids),
            "delayMinusMs": delay_minus_ms,
            "residualMinus": residual_minus,
            "delayPlusMs": delay_plus_ms,
            "residualPlus": residual_plus,
            "controlFingerprint": control_fingerprint,
            "boundaryId": sample_minus.boundary_id,
            "boundaryModelVersion": sample_minus.boundary_model_version,
            "boundaryModelRevision": boundary_model_revision,
            "evidenceIds": list(evidence),
            "slopeAdvancesPerMs": slope,
        })
        self._persist()
        return slope

    def stable_slope(self):
        slopes = [row["slopeAdvancesPerMs"] for row in self.pairs]
        if len(slopes) < self.minimum_pairs:
            return None, "TIMING_SLOPE_NEEDS_MORE_PAIRS"
        if any(abs(value) < self.minimum_slope for value in slopes):
            return None, "TIMING_SLOPE_NEAR_ZERO"
        median = statistics.median(slopes)
        if any(value * median <= 0 for value in slopes):
            return None, "TIMING_SLOPE_SIGN_UNSTABLE"
        variation = max(abs(value - median) for value in slopes) / abs(median)
        if variation > self.maximum_relative_variation:
            return None, "TIMING_SLOPE_VARIATION_TOO_HIGH"
        return median, "TIMING_SLOPE_STABLE"

    def propose_update(self, *, current_delay_ms: int, corrected_residual: int,
                       alpha: float, minimum_delay_ms: int, maximum_delay_ms: int,
                       maximum_step_ms: int) -> TimingUpdate:
        if any(isinstance(value, bool) or not isinstance(value, int) for value in (
            current_delay_ms, corrected_residual, minimum_delay_ms, maximum_delay_ms, maximum_step_ms,
        )):
            raise FeedbackError("delay bounds, current delay, step, and residual must be integers")
        if minimum_delay_ms > maximum_delay_ms or maximum_step_ms < 0:
            raise FeedbackError("delay bounds or maximum_step_ms are invalid")
        if isinstance(alpha, bool) or not isinstance(alpha, (int, float)) or not math.isfinite(alpha) or not 0 <= alpha <= 1:
            raise FeedbackError("alpha must be between zero and one")
        slope, reason = self.stable_slope()
        if slope is None:
            update = TimingUpdate("disabled", current_delay_ms, current_delay_ms, None, reason)
            self.updates.append(update.as_dict())
            self._persist()
            return update
        raw_delta = -alpha * corrected_residual / slope
        delta = math.floor(raw_delta + 0.5) if raw_delta >= 0 else math.ceil(raw_delta - 0.5)
        delta = max(-maximum_step_ms, min(maximum_step_ms, delta))
        proposed = max(minimum_delay_ms, min(maximum_delay_ms, current_delay_ms + delta))
        if proposed == current_delay_ms:
            update = TimingUpdate("unchanged", current_delay_ms, proposed, slope, "TIMING_UPDATE_CLIPPED")
        else:
            update = TimingUpdate("proposed", current_delay_ms, proposed, slope, "TIMING_UPDATE_PROPOSED")
        self.updates.append(update.as_dict())
        self._persist()
        return update

    def as_dict(self):
        slope, reason = self.stable_slope()
        return {
            "parameterId": self.parameter_id,
            "status": reason,
            "stableSlopeAdvancesPerMs": slope,
            "pairs": self.pairs,
            "updates": self.updates,
        }

    def _persist(self):
        if self.archive is not None:
            self.archive.save_section(
                "timingModel", self.as_dict(), context_fingerprint=self.context_fingerprint,
            )

    def _restore(self):
        if self.archive is None:
            return
        document = self.archive.load()
        if document.get("contextFingerprint") not in (None, self.context_fingerprint):
            raise FeedbackError("timing archive context changed; old probes are invalid")
        model = document.get("timingModel")
        if model is None:
            return
        if (model.get("parameterId") != self.parameter_id or not isinstance(model.get("pairs"), list)
                or not isinstance(model.get("updates", []), list)):
            raise FeedbackError("timing archive belongs to another parameter or has invalid pairs")
        self.pairs = list(model["pairs"])
        self.updates = list(model.get("updates", ()))
        if any(not isinstance(row, dict) or not isinstance(row.get("pairId"), str)
               or not isinstance(row.get("slopeAdvancesPerMs"), (int, float))
               or not isinstance(row.get("evidenceIds"), list) or not row["evidenceIds"]
               for row in self.pairs):
            raise FeedbackError("timing archive contains an invalid probe pair")
        self._pair_ids = {row["pairId"] for row in self.pairs}
        self._sample_ids = {
            sample_id for row in self.pairs for sample_id in row.get("sampleIds", ())
        }
        if len(self._pair_ids) != len(self.pairs):
            raise FeedbackError("timing archive has duplicate probe pairs")
        if any(not isinstance(sample_id, str) or not sample_id for sample_id in self._sample_ids):
            raise FeedbackError("timing archive contains invalid sample references")
        if sum(len(row.get("sampleIds", ())) for row in self.pairs) != len(self._sample_ids):
            raise FeedbackError("timing archive reuses a sample across probe pairs")
        revisions = {row.get("boundaryModelRevision") for row in self.pairs}
        if len(revisions) > 1:
            raise FeedbackError("timing archive mixes boundary model revisions")
        if any(row.get("boundaryModelVersion") != self.pairs[0].get("boundaryModelVersion")
               or row.get("boundaryId") != self.pairs[0].get("boundaryId")
               for row in self.pairs):
            raise FeedbackError("timing archive mixes boundary identities or versions")


def _boundary_dict(value):
    return {
        "boundaryId": value.boundary_id,
        "offset": value.offset,
        "modelVersion": value.model_version,
        "revision": value.revision,
    }


def _boundary_from_dict(value):
    if not isinstance(value, Mapping):
        raise ValueError("boundary adjustment must be an object")
    return BoundaryAdjustment(
        boundary_id=value["boundaryId"],
        offset=value["offset"],
        model_version=value["modelVersion"],
        revision=value["revision"],
    )
