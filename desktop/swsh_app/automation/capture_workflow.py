"""Evidence-first capture, observation, reverse lookup, and target verification.

The module models workflow decisions only. It does not press buttons, capture a
Pokemon, or assert that replay data represents a real console session.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
import json
import math
from types import MappingProxyType
from typing import Any, Mapping, Sequence


OBSERVABLE_FIELDS = {
    "species", "level", "gender", "nature", "ability", "ivs", "shiny", "mark",
    "height", "rareEncryptionConstant", "aura", "certificate",
}
REVERSE_RESULT_FIELDS = {
    "species": "species", "level": "level", "gender": "gender", "nature": "nature",
    "ability": "ability", "ivs": "ivs", "shiny": "shiny", "mark": "mark",
    "height": "height", "rareEncryptionConstant": "rareEncryptionConstant", "aura": "brilliantAura",
}
POST_CAPTURE_PROMPTS = {"experience", "dex", "nickname", "party", "box"}
REVERSE_CONTEXT_FIELDS = {
    "knockouts", "hiddenStep", "dexRecommendationSlots", "considerMenuClose",
    "menuCloseNonPlayerCharacters", "holdDirection", "considerFlying", "areaLoadAdvances",
    "areaLoadNonPlayerCharacters", "considerRain", "rainTicksDuringAreaLoad", "rainTicksBeforeEncounter",
}
REVERSE_WEATHERS = {
    "Normal Weather", "Overcast", "Raining", "Thunderstorm", "Intense Sun",
    "Snowing", "Snowstorm", "Heavy Fog",
}


class CaptureEvidenceError(ValueError):
    pass


class ObservationStatus(StrEnum):
    KNOWN = "known"
    AMBIGUOUS = "ambiguous"
    UNKNOWN = "unknown"


class CapturePhase(StrEnum):
    BATTLE_ENTRY = "battle_entry"
    OPPONENT_DISPLAY = "opponent_display"
    ACTION_MENU = "action_menu"
    MOVE_OR_ITEM = "move_or_item"
    BALL_SELECTION = "ball_selection"
    BALL_ANIMATION = "ball_animation"
    POST_CAPTURE_PROMPTS = "post_capture_prompts"
    ENTITY_IDENTIFICATION = "entity_identification"
    SUMMARY_OBSERVATION = "summary_observation"
    REVERSE_LOOKUP = "reverse_lookup"
    TARGET_VERIFICATION = "target_verification"
    SAVE_COMMIT = "save_commit"
    COMPLETED = "completed"
    NEEDS_ATTENTION = "needs_attention"


@dataclass(frozen=True, slots=True)
class FieldRead:
    field_id: str
    value: Any
    frame_id: int
    context_revision: int
    confidence: float
    raw_value: str
    evidence_ids: Sequence[str]
    page_stable: bool = True

    def __post_init__(self):
        if self.field_id not in OBSERVABLE_FIELDS:
            raise CaptureEvidenceError(f"unsupported observation field: {self.field_id}")
        if isinstance(self.frame_id, bool) or not isinstance(self.frame_id, int) or self.frame_id < 0:
            raise CaptureEvidenceError("frame_id must be a non-negative integer")
        if (isinstance(self.context_revision, bool) or not isinstance(self.context_revision, int)
                or self.context_revision < 0):
            raise CaptureEvidenceError("context_revision must be a non-negative integer")
        if not isinstance(self.page_stable, bool):
            raise CaptureEvidenceError("page_stable must be boolean")
        _confidence(self.confidence)
        if not isinstance(self.raw_value, str):
            raise CaptureEvidenceError("raw_value must preserve OCR text")
        object.__setattr__(self, "value", _json_value(self.value, "field value"))
        object.__setattr__(self, "evidence_ids", _ids(self.evidence_ids, "evidence_ids", allow_empty=True))


@dataclass(frozen=True, slots=True)
class FieldObservation:
    field_id: str
    status: ObservationStatus
    value: Any = None
    candidates: Sequence[Any] = ()
    confidence: float | None = None
    raw_values: Sequence[str] = ()
    frame_ids: Sequence[int] = ()
    evidence_ids: Sequence[str] = ()

    def __post_init__(self):
        if self.field_id not in OBSERVABLE_FIELDS:
            raise CaptureEvidenceError(f"unsupported observation field: {self.field_id}")
        try:
            status = ObservationStatus(self.status)
        except ValueError as exc:
            raise CaptureEvidenceError("observation status must be known, ambiguous, or unknown") from exc
        object.__setattr__(self, "status", status)
        candidate_values = tuple(_json_value(value, "candidate") for value in self.candidates)
        if status == ObservationStatus.KNOWN:
            if self.value is None or candidate_values:
                raise CaptureEvidenceError("known observations require one value and no alternatives")
            if len(set(self.frame_ids)) < 2:
                raise CaptureEvidenceError("known OCR observations require at least two distinct video frames")
            if not self.evidence_ids:
                raise CaptureEvidenceError("known observations require evidence references")
            if self.confidence is None:
                raise CaptureEvidenceError("known observations require a confidence score")
            object.__setattr__(self, "value", _json_value(self.value, "value"))
        elif status == ObservationStatus.AMBIGUOUS:
            if self.value is not None or len(set(map(_canonical, candidate_values))) < 2:
                raise CaptureEvidenceError("ambiguous observations require at least two distinct candidates")
            object.__setattr__(self, "value", None)
        else:
            if self.value is not None or candidate_values:
                raise CaptureEvidenceError("unknown observations cannot contain a guessed value")
            object.__setattr__(self, "value", None)
        if self.confidence is not None:
            _confidence(self.confidence)
        object.__setattr__(self, "candidates", candidate_values)
        object.__setattr__(self, "raw_values", tuple(str(value) for value in self.raw_values))
        frames = tuple(dict.fromkeys(self.frame_ids))
        if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in frames):
            raise CaptureEvidenceError("frame_ids must be non-negative integers")
        object.__setattr__(self, "frame_ids", frames)
        object.__setattr__(self, "evidence_ids", _ids(self.evidence_ids, "evidence_ids", allow_empty=True))


def aggregate_field_reads(reads: Sequence[FieldRead], *, minimum_confidence=0.85) -> FieldObservation:
    """Accept a value only after two stable reads from distinct frames."""
    _confidence(minimum_confidence)
    reads = tuple(reads)
    if not reads:
        raise CaptureEvidenceError("at least one field read is required")
    if any(not isinstance(read, FieldRead) for read in reads):
        raise CaptureEvidenceError("field reads must contain FieldRead values")
    field_id = reads[0].field_id
    if any(read.field_id != field_id for read in reads):
        raise CaptureEvidenceError("field reads must refer to one field")
    frame_ids = tuple(dict.fromkeys(read.frame_id for read in reads))
    evidence_ids = tuple(dict.fromkeys(
        evidence_id for read in reads for evidence_id in read.evidence_ids
    ))
    raw_values = tuple(read.raw_value for read in reads)
    if (len(frame_ids) < 2 or any(not read.page_stable for read in reads)
            or len({read.context_revision for read in reads}) != 1):
        return FieldObservation(field_id, ObservationStatus.UNKNOWN, raw_values=raw_values,
                                frame_ids=frame_ids, evidence_ids=evidence_ids)

    value_groups: dict[str, list[FieldRead]] = {}
    for read in reads:
        if read.value is not None:
            value_groups.setdefault(_canonical(read.value), []).append(read)
    stable_known = [
        items for items in value_groups.values()
        if len({item.frame_id for item in items}) >= 2
        and all(item.confidence >= minimum_confidence for item in items)
    ]
    if len(stable_known) == 1 and len(value_groups) == 1:
        accepted = stable_known[0]
        return FieldObservation(
            field_id,
            ObservationStatus.KNOWN,
            value=accepted[0].value,
            confidence=min(item.confidence for item in accepted),
            raw_values=raw_values,
            frame_ids=frame_ids,
            evidence_ids=evidence_ids,
        )

    candidates = [items[0].value for items in value_groups.values()]
    if len(candidates) >= 2:
        return FieldObservation(field_id, ObservationStatus.AMBIGUOUS, candidates=candidates,
                                confidence=min(read.confidence for read in reads),
                                raw_values=raw_values, frame_ids=frame_ids, evidence_ids=evidence_ids)
    return FieldObservation(field_id, ObservationStatus.UNKNOWN,
                            confidence=min(read.confidence for read in reads),
                            raw_values=raw_values, frame_ids=frame_ids, evidence_ids=evidence_ids)


@dataclass(frozen=True, slots=True)
class CaptureEvent:
    kind: str
    evidence_ids: Sequence[str]
    details: Mapping[str, Any] | None = None

    def __post_init__(self):
        if not isinstance(self.kind, str) or not self.kind:
            raise CaptureEvidenceError("capture event kind is required")
        ids = _ids(self.evidence_ids, "event evidence_ids")
        object.__setattr__(self, "evidence_ids", ids)
        object.__setattr__(self, "details", MappingProxyType(_json_object(self.details or {}, "event details")))


class CaptureWorkflow:
    """Evidence-driven phase machine; transitions never issue device actions."""

    def __init__(self, *, attempt_id: str, max_ball_throws: int = 8):
        if not attempt_id:
            raise CaptureEvidenceError("attempt_id is required")
        if isinstance(max_ball_throws, bool) or not isinstance(max_ball_throws, int) or max_ball_throws < 1:
            raise CaptureEvidenceError("max_ball_throws must be positive")
        self.attempt_id = attempt_id
        self.max_ball_throws = max_ball_throws
        self.phase = CapturePhase.BATTLE_ENTRY
        self.capture_succeeded = False
        self.identity_confirmed = False
        self.scene_match = False
        self.target_verified = False
        self.ball_throws = 0
        self.active_prompt: str | None = None
        self.events: list[dict[str, Any]] = []
        self.identity_key: str | None = None
        self._event_count = 0

    def apply(self, event: CaptureEvent) -> CapturePhase:
        if self.phase in (CapturePhase.COMPLETED, CapturePhase.NEEDS_ATTENTION):
            raise CaptureEvidenceError("capture workflow is terminal")
        if not isinstance(event, CaptureEvent):
            raise CaptureEvidenceError("event must be a CaptureEvent")
        old_phase = self.phase
        details = dict(event.details)
        next_phase = self._transition(event.kind, details)
        if event.kind == "ball_thrown":
            self.ball_throws += 1
        elif event.kind == "capture_confirmed":
            self.capture_succeeded = True
        elif event.kind == "new_entity_confirmed":
            if not self.capture_succeeded:
                raise CaptureEvidenceError("capture success and new-entity identity are separate checks")
            if not details.get("identityKey"):
                raise CaptureEvidenceError("identityKey is required to confirm the new entity")
            self.identity_key = str(details["identityKey"])
            self.identity_confirmed = True
            self.scene_match = details.get("sceneMatch") is True
        elif event.kind == "target_verified":
            if details.get("verified") is not True:
                raise CaptureEvidenceError("target_verified event requires a successful evidence result")
            self.target_verified = True
        elif event.kind == "stop":
            next_phase = CapturePhase.NEEDS_ATTENTION

        if event.kind == "capture_failed" and self.ball_throws >= self.max_ball_throws:
            next_phase = CapturePhase.NEEDS_ATTENTION
            details["reasonCode"] = "CAPTURE_RETRY_LIMIT_REACHED"
        self.phase = next_phase
        self._event_count += 1
        self.events.append({
            "attemptId": self.attempt_id,
            "sequence": self._event_count,
            "from": old_phase.value,
            "to": next_phase.value,
            "event": event.kind,
            "evidenceIds": list(event.evidence_ids),
            "details": details,
        })
        return self.phase

    def as_report(self) -> dict[str, Any]:
        status = {
            CapturePhase.COMPLETED: "completed",
            CapturePhase.NEEDS_ATTENTION: "needs_attention",
        }.get(self.phase, "pending")
        return {
            "reportSchema": "auto-swsh-capture-workflow",
            "reportVersion": 1,
            "attemptId": self.attempt_id,
            "phase": self.phase.value,
            "status": status,
            "captureSucceeded": self.capture_succeeded,
            "identityConfirmed": self.identity_confirmed,
            "identityKey": self.identity_key,
            "sceneMatch": self.scene_match,
            "targetVerified": self.target_verified,
            "ballThrows": self.ball_throws,
            "events": json.loads(json.dumps(self.events, ensure_ascii=False)),
        }

    def _transition(self, kind: str, details: Mapping[str, Any]) -> CapturePhase:
        transitions = {
            (CapturePhase.BATTLE_ENTRY, "battle_entered"): CapturePhase.OPPONENT_DISPLAY,
            (CapturePhase.OPPONENT_DISPLAY, "opponent_observed"): CapturePhase.ACTION_MENU,
            (CapturePhase.ACTION_MENU, "prepare_move_or_item"): CapturePhase.MOVE_OR_ITEM,
            (CapturePhase.MOVE_OR_ITEM, "open_ball_selection"): CapturePhase.BALL_SELECTION,
            (CapturePhase.BALL_SELECTION, "ball_thrown"): CapturePhase.BALL_ANIMATION,
            (CapturePhase.BALL_ANIMATION, "capture_failed"): CapturePhase.ACTION_MENU,
            (CapturePhase.BALL_ANIMATION, "capture_confirmed"): CapturePhase.POST_CAPTURE_PROMPTS,
            (CapturePhase.POST_CAPTURE_PROMPTS, "post_capture_complete"): CapturePhase.ENTITY_IDENTIFICATION,
            (CapturePhase.ENTITY_IDENTIFICATION, "new_entity_confirmed"): CapturePhase.SUMMARY_OBSERVATION,
            (CapturePhase.ENTITY_IDENTIFICATION, "entity_ambiguous"): CapturePhase.NEEDS_ATTENTION,
            (CapturePhase.SUMMARY_OBSERVATION, "observations_complete"): CapturePhase.REVERSE_LOOKUP,
            (CapturePhase.REVERSE_LOOKUP, "reverse_needs_more_observation"): CapturePhase.SUMMARY_OBSERVATION,
            (CapturePhase.REVERSE_LOOKUP, "reverse_complete"): CapturePhase.TARGET_VERIFICATION,
            (CapturePhase.TARGET_VERIFICATION, "target_verified"): CapturePhase.SAVE_COMMIT,
            (CapturePhase.TARGET_VERIFICATION, "target_not_verified"): CapturePhase.NEEDS_ATTENTION,
            (CapturePhase.SAVE_COMMIT, "save_completed"): CapturePhase.COMPLETED,
            (CapturePhase.SAVE_COMMIT, "save_unconfirmed"): CapturePhase.NEEDS_ATTENTION,
        }
        if kind == "stop":
            return CapturePhase.NEEDS_ATTENTION
        if self.phase == CapturePhase.POST_CAPTURE_PROMPTS:
            if kind == "prompt_opened":
                prompt = details.get("promptKind")
                if prompt not in POST_CAPTURE_PROMPTS or self.active_prompt is not None:
                    raise CaptureEvidenceError("post-capture prompt sequence is invalid")
                self.active_prompt = prompt
                return self.phase
            if kind == "prompt_resolved":
                if self.active_prompt is None or details.get("promptKind") != self.active_prompt:
                    raise CaptureEvidenceError("prompt resolution does not match the active prompt")
                self.active_prompt = None
                return self.phase
            if kind == "post_capture_complete" and self.active_prompt is not None:
                raise CaptureEvidenceError("all visible post-capture prompts must be resolved")
        if (self.phase, kind) not in transitions:
            raise CaptureEvidenceError(f"event {kind} is not valid during {self.phase.value}")
        if kind == "capture_failed" and self.ball_throws < 1:
            raise CaptureEvidenceError("capture_failed requires a recorded ball throw")
        if kind == "reverse_complete":
            if details.get("complete") is not True or details.get("truncated") is True:
                raise CaptureEvidenceError("reverse lookup must finish its full diagnostic window")
        return transitions[(self.phase, kind)]


@dataclass(frozen=True, slots=True)
class RosterMember:
    slot: str
    identity_signature: str
    species: str
    level: int

    def __post_init__(self):
        for name in ("slot", "identity_signature", "species"):
            if not isinstance(getattr(self, name), str) or not getattr(self, name).strip():
                raise CaptureEvidenceError(f"roster {name} is required")
        if isinstance(self.level, bool) or not isinstance(self.level, int) or not 1 <= self.level <= 100:
            raise CaptureEvidenceError("roster level must be between 1 and 100")


@dataclass(frozen=True, slots=True)
class EntityIdentityResult:
    status: str
    identity_key: str | None
    storage_slot: str | None
    evidence_ids: tuple[str, ...]
    reason_code: str


def identify_new_roster_entity(*, before: Sequence[RosterMember], after: Sequence[RosterMember],
                               before_evidence_ids: Sequence[str], after_evidence_ids: Sequence[str],
                               storage_mode: str, expected_slot: str | None = None,
                               box_navigation_evidence_ids: Sequence[str] = (),
                               expected_species: str | None = None,
                               expected_level: int | None = None) -> EntityIdentityResult:
    """Confirm a newly captured individual by comparing explicit roster slots."""
    before = tuple(before)
    after = tuple(after)
    if any(not isinstance(member, RosterMember) for member in (*before, *after)):
        raise CaptureEvidenceError("roster snapshots must contain RosterMember values")
    if len({member.slot for member in before}) != len(before) or len({member.slot for member in after}) != len(after):
        raise CaptureEvidenceError("roster slot identities must be unique")
    before_ids = _ids(before_evidence_ids, "before_evidence_ids")
    after_ids = _ids(after_evidence_ids, "after_evidence_ids")
    if storage_mode not in {"reserved_party_slot", "explicit_box_slot"}:
        raise CaptureEvidenceError("storage_mode must be reserved_party_slot or explicit_box_slot")
    navigation_ids = _ids(box_navigation_evidence_ids, "box_navigation_evidence_ids", allow_empty=True)
    if storage_mode == "explicit_box_slot" and (not expected_slot or not navigation_ids):
        raise CaptureEvidenceError("box capture identity requires an explicit slot and navigation evidence")
    if expected_species is not None and (not isinstance(expected_species, str) or not expected_species.strip()):
        raise CaptureEvidenceError("expected_species must be a non-empty species identifier")
    if expected_level is not None and (
        isinstance(expected_level, bool) or not isinstance(expected_level, int) or not 1 <= expected_level <= 100
    ):
        raise CaptureEvidenceError("expected_level must be between 1 and 100")
    prior_by_slot = {member.slot: member for member in before}
    new_members = [
        member for member in after
        if (member.slot not in prior_by_slot
            or prior_by_slot[member.slot].identity_signature != member.identity_signature)
    ]
    evidence = tuple(dict.fromkeys((*before_ids, *after_ids, *navigation_ids)))
    if expected_slot is not None and (len(new_members) != 1 or new_members[0].slot != expected_slot):
        return EntityIdentityResult("unknown", None, None, evidence, "EXPECTED_STORAGE_SLOT_NOT_CONFIRMED")
    if len(new_members) != 1:
        return EntityIdentityResult(
            status="ambiguous" if new_members else "unknown",
            identity_key=None,
            storage_slot=None,
            evidence_ids=evidence,
            reason_code="MULTIPLE_NEW_ENTITIES" if new_members else "NEW_ENTITY_NOT_IDENTIFIED",
        )
    identified = new_members[0]
    if ((expected_species is not None and identified.species != expected_species)
            or (expected_level is not None and identified.level != expected_level)):
        return EntityIdentityResult("unknown", None, None, evidence, "NEW_ENTITY_DETAILS_MISMATCH")
    if storage_mode == "reserved_party_slot" and identified.slot in prior_by_slot:
        return EntityIdentityResult("unknown", None, None, evidence, "RESERVED_SLOT_WAS_NOT_EMPTY")
    return EntityIdentityResult(
        status="known",
        identity_key=f"{identified.slot}|{identified.identity_signature}",
        storage_slot=identified.slot,
        evidence_ids=evidence,
        reason_code="NEW_ENTITY_CONFIRMED",
    )


@dataclass(frozen=True, slots=True)
class EncounterAttemptSnapshot:
    seed0: str
    seed1: str
    game: str
    kind: str
    area: str
    weather: str
    source_kind: str
    target_range: tuple[int, int]
    diagnostic_window: tuple[int, int]
    diagnostic_window_complete: bool
    context: Mapping[str, Any]
    tid: int = 0
    sid: int = 0
    shiny_charm: bool = False
    mark_charm: bool = False
    lead_ability: str | None = None
    planned_generation_advance: int | None = None
    evidence_ids: Sequence[str] = ()

    def __post_init__(self):
        object.__setattr__(self, "seed0", _seed(self.seed0, "seed0"))
        object.__setattr__(self, "seed1", _seed(self.seed1, "seed1"))
        for name in ("game", "kind", "area", "weather"):
            if not isinstance(getattr(self, name), str) or not getattr(self, name).strip():
                raise CaptureEvidenceError(f"{name} is required in the frozen encounter context")
        if self.weather not in REVERSE_WEATHERS:
            raise CaptureEvidenceError("weather must use a supported internal weather name")
        if self.source_kind not in {"synthetic", "replay", "real"}:
            raise CaptureEvidenceError("source_kind must be synthetic, replay, or real")
        _range(self.target_range, "target_range", maximum=1_000_000_000)
        _range(self.diagnostic_window, "diagnostic_window", maximum=1_000_000_000,
               maximum_width=100_000)
        if not isinstance(self.diagnostic_window_complete, bool):
            raise CaptureEvidenceError("diagnostic_window_complete must be boolean")
        if not 0 <= self.tid <= 65_535 or not 0 <= self.sid <= 65_535:
            raise CaptureEvidenceError("TID and SID must be between 0 and 65,535")
        if self.planned_generation_advance is not None:
            _position(self.planned_generation_advance, "planned_generation_advance")
        object.__setattr__(self, "context", MappingProxyType(_json_object(self.context, "encounter context")))
        if set(self.context) - REVERSE_CONTEXT_FIELDS:
            raise CaptureEvidenceError("encounter context contains non-context or target-filter fields")
        object.__setattr__(self, "evidence_ids", _ids(self.evidence_ids, "evidence_ids", allow_empty=True))

    def build_reverse_request(self, *, observations: Mapping[str, FieldObservation],
                              workflow: CaptureWorkflow) -> dict[str, Any]:
        if not isinstance(workflow, CaptureWorkflow):
            raise CaptureEvidenceError("a capture workflow is required")
        if not workflow.capture_succeeded or not workflow.identity_confirmed:
            raise CaptureEvidenceError("capture success and new-entity identity must both be confirmed")
        if not workflow.scene_match:
            raise CaptureEvidenceError("captured entity does not match the frozen encounter scene")
        observation_map = dict(observations)
        if any(not isinstance(value, FieldObservation) or key != value.field_id
               for key, value in observation_map.items()):
            raise CaptureEvidenceError("observations must be keyed by their FieldObservation field IDs")
        species = observation_map.get("species")
        if self.kind.lower() == "static" and (
            species is None or species.status != ObservationStatus.KNOWN
        ):
            raise CaptureEvidenceError("static encounter reverse lookup requires a confirmed actual species")
        payload = {
            **dict(self.context),
            "operation": "encounter.reverse",
            "game": self.game,
            "kind": self.kind,
            "area": self.area,
            "weather": self.weather,
            "leadAbility": self.lead_ability,
            "seed0": self.seed0,
            "seed1": self.seed1,
            "start": self.diagnostic_window[0],
            "end": self.diagnostic_window[1],
            "tid": self.tid,
            "sid": self.sid,
            "shinyCharm": self.shiny_charm,
            "markCharm": self.mark_charm,
            "sourceKind": self.source_kind,
            "diagnosticWindowComplete": self.diagnostic_window_complete,
            "captureConfirmed": workflow.capture_succeeded,
            "identityVerified": workflow.identity_confirmed,
            "sceneMatch": workflow.scene_match,
            "plannedGenerationAdvance": self.planned_generation_advance,
            "evidenceIds": list(dict.fromkeys([
                *self.evidence_ids,
                *[evidence_id for observation in observation_map.values()
                  for evidence_id in observation.evidence_ids],
            ])),
        }
        unresolved = []
        for field_name in OBSERVABLE_FIELDS - {"certificate"}:
            observation = observation_map.get(field_name)
            if observation is None or observation.status != ObservationStatus.KNOWN:
                unresolved.append(field_name)
        payload.update(_reverse_observation_filters(observation_map, unresolved))
        payload["unresolvedFields"] = sorted(set(unresolved))
        # Desired target filters never enter this request. It contains only the
        # captured entity's actual observations and its frozen scene context.
        return payload


@dataclass(frozen=True, slots=True)
class TargetVerificationResult:
    status: str
    verified: bool
    verification_level: str
    candidate_advances: tuple[int, ...]
    unresolved_fields: tuple[str, ...]
    calibration_sample_eligible: bool
    reason_code: str

    def as_dict(self):
        return {
            "status": self.status,
            "verified": self.verified,
            "verificationLevel": self.verification_level,
            "candidateAdvances": list(self.candidate_advances),
            "unresolvedFields": list(self.unresolved_fields),
            "calibrationSampleEligible": self.calibration_sample_eligible,
            "reasonCode": self.reason_code,
        }


def verify_capture_target(*, capture_succeeded: bool, identity_confirmed: bool, scene_match: bool,
                          source_kind: str, user_range: tuple[int, int],
                          requirements: Mapping[str, Any], observations: Mapping[str, FieldObservation],
                          reverse_result: Mapping[str, Any]) -> TargetVerificationResult:
    """Require full-window evidence and prove every candidate meets the goal."""
    _range(user_range, "user_range", maximum=1_000_000_000)
    if not isinstance(requirements, Mapping) or not isinstance(observations, Mapping):
        raise CaptureEvidenceError("requirements and observations must be mappings")
    if not requirements:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), (),
                             "TARGET_REQUIREMENTS_EMPTY")
    if not capture_succeeded or not identity_confirmed or not scene_match:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "CAPTURE_IDENTITY_OR_SCENE_UNCONFIRMED")
    if source_kind not in {"synthetic", "replay", "real"}:
        raise CaptureEvidenceError("source_kind is invalid")
    if not isinstance(reverse_result, Mapping) or reverse_result.get("complete") is not True:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_SEARCH_INCOMPLETE")
    if reverse_result.get("sourceKind") != source_kind:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_SOURCE_KIND_MISMATCH")
    if reverse_result.get("truncated") is True:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_SEARCH_TRUNCATED")
    window = reverse_result.get("window")
    if not isinstance(window, Mapping) or window.get("diagnosticWindowComplete") is not True:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_WINDOW_NOT_COMPLETE")
    evidence = reverse_result.get("evidenceIds")
    try:
        _ids(evidence, "reverse evidenceIds")
    except CaptureEvidenceError:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_EVIDENCE_MISSING")
    candidates = reverse_result.get("candidates")
    if not isinstance(candidates, list) or not candidates:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                             "REVERSE_NO_CANDIDATES")
    advances = []
    for row in candidates:
        if not isinstance(row, Mapping):
            return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                                 "REVERSE_CANDIDATE_INVALID")
        advance = row.get("generationAdvance")
        try:
            advances.append(_position(advance, "generationAdvance"))
        except CaptureEvidenceError:
            return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", (), requirements,
                                 "REVERSE_CANDIDATE_INVALID")
    low, high = user_range
    if any(value < low or value > high for value in advances):
        return _verification("TARGET_POSITION_UNPROVEN", False, "unknown", tuple(advances), requirements,
                             "CANDIDATES_CROSS_USER_RANGE_BOUNDARY")

    observation_map = dict(observations)
    if any(not isinstance(value, FieldObservation) or key != value.field_id
           for key, value in observation_map.items()):
        raise CaptureEvidenceError("observations must be keyed by their FieldObservation field IDs")
    unresolved = []
    direct_fields = set()
    inferred_fields = set()
    for field_name, expected in requirements.items():
        observation = observation_map.get(field_name)
        if observation is not None and observation.status == ObservationStatus.KNOWN:
            if not _matches_requirement(observation.value, expected):
                return _verification("TARGET_NOT_MET", False, "direct_observation", tuple(advances),
                                     (field_name,), "OBSERVED_TARGET_MISMATCH")
            direct_fields.add(field_name)
            continue
        candidate_values = [row.get(REVERSE_RESULT_FIELDS.get(field_name, field_name)) for row in candidates]
        if any(value is None for value in candidate_values):
            unresolved.append(field_name)
            continue
        if not all(_matches_requirement(value, expected) for value in candidate_values):
            return _verification("TARGET_NOT_MET", False, "reverse_inference", tuple(advances),
                                 (field_name,), "REVERSE_CANDIDATES_DO_NOT_ALL_MEET_TARGET")
        inferred_fields.add(field_name)

    if unresolved:
        return _verification("POSSIBLE_TARGET_UNVERIFIED", False, "unknown", tuple(advances),
                             unresolved, "TARGET_FIELDS_REMAIN_UNKNOWN")
    unique = len(set(advances)) == 1
    level = "unique_reverse_inference" if unique else (
        "candidate_set_satisfies_target" if inferred_fields else "direct_observation_and_candidate_set"
    )
    verified = True
    calibration_eligible = (
        verified and unique and source_kind == "real"
        and reverse_result.get("eligibleForCalibration") is True
    )
    status = "TARGET_VERIFIED" if unique else "TARGET_VERIFIED_WITH_MULTIPLE_CANDIDATES"
    return TargetVerificationResult(
        status=status,
        verified=True,
        verification_level=level,
        candidate_advances=tuple(advances),
        unresolved_fields=(),
        calibration_sample_eligible=calibration_eligible,
        reason_code="TARGET_VERIFIED" if unique else "ALL_COMPATIBLE_CANDIDATES_MEET_TARGET",
    )


def _verification(status, verified, level, advances, fields, reason):
    return TargetVerificationResult(status, verified, level, tuple(advances), tuple(fields), False, reason)


def _reverse_observation_filters(observations, unresolved):
    values = {name: observation.value for name, observation in observations.items()
              if observation.status == ObservationStatus.KNOWN}
    payload: dict[str, Any] = {
        "species": values.get("species"),
        "nature": values.get("nature"),
        "ability": values.get("ability"),
        "gender": values.get("gender"),
        "levelMinimum": 1,
        "levelMaximum": 100,
        "shiny": "Any",
        "aura": "Any",
        "mark": "Ignore",
        "specificMark": None,
        "height": "Any",
        "rareEncryptionConstant": False,
        "ivMatch": "Range",
        "ivs": [[0, 31] for _ in range(6)],
    }
    if "level" in values:
        level = values["level"]
        if isinstance(level, int) and not isinstance(level, bool) and 1 <= level <= 100:
            payload["levelMinimum"] = payload["levelMaximum"] = level
        elif isinstance(level, Mapping) and "minimum" in level and "maximum" in level:
            payload["levelMinimum"], payload["levelMaximum"] = level["minimum"], level["maximum"]
        else:
            unresolved.append("level")
    if "ivs" in values:
        ivs = values["ivs"]
        if _valid_ivs(ivs):
            payload["ivs"] = [list(pair) for pair in ivs]
        else:
            unresolved.append("ivs")
    if "shiny" in values:
        shiny = values["shiny"]
        if isinstance(shiny, str) and shiny.casefold() == "star":
            payload["shiny"] = "Star"
        elif isinstance(shiny, str) and shiny.casefold() == "square":
            payload["shiny"] = "Square"
        elif isinstance(shiny, str) and shiny.casefold() in {"none", "no"}:
            payload["shiny"] = "None"
        elif shiny is True:
            payload["shiny"] = "Either"
        elif shiny is False:
            payload["shiny"] = "None"
        else:
            unresolved.append("shiny")
    if "aura" in values:
        if values["aura"] is True:
            payload["aura"] = "Brilliant"
        elif values["aura"] is False:
            payload["aura"] = "None"
        else:
            unresolved.append("aura")
    if "mark" in values:
        if values["mark"] in (None, "None", "none", ""):
            payload["mark"] = "None"
        elif isinstance(values["mark"], str):
            payload["mark"] = "Specific"
            mark_aliases = {"Time": "Lunchtime", "Weather": "Cloudy"}
            payload["specificMark"] = mark_aliases.get(values["mark"], values["mark"])
        else:
            unresolved.append("mark")
    if "height" in values:
        if isinstance(values["height"], str):
            height_aliases = {
                "S": "Small", "M": "Medium", "L": "Large",
                "XXXS": "XXXS", "XXS": "XXS", "XS": "XS", "Small": "Small",
                "Medium": "Medium", "Large": "Large", "XL": "XL", "XXL": "XXL",
                "XXXL": "XXXL", "MinOrMax": "MinOrMax",
            }
            normalized = values["height"].strip()
            payload["height"] = height_aliases.get(normalized, "Any")
            if normalized not in height_aliases:
                unresolved.append("height")
        else:
            unresolved.append("height")
    if "rareEncryptionConstant" in values:
        if isinstance(values["rareEncryptionConstant"], bool):
            payload["rareEncryptionConstant"] = values["rareEncryptionConstant"]
        else:
            unresolved.append("rareEncryptionConstant")
    return payload


def _matches_requirement(value, expected):
    if isinstance(expected, Mapping) and "minimum" in expected and "maximum" in expected:
        return isinstance(value, (int, float)) and not isinstance(value, bool) and expected["minimum"] <= value <= expected["maximum"]
    if (isinstance(expected, Sequence) and not isinstance(expected, (str, bytes))
            and len(expected) == 2
            and all(isinstance(bound, (int, float)) and not isinstance(bound, bool)
                    for bound in expected)
            and isinstance(value, (int, float)) and not isinstance(value, bool)):
        return expected[0] <= value <= expected[1]
    if isinstance(expected, bool) and isinstance(value, str):
        shiny = value.casefold()
        return (shiny in {"star", "square"}) if expected else shiny in {"none", "no"}
    if isinstance(expected, str) and isinstance(value, str) and expected.casefold() in {"star", "square", "none"}:
        return value.casefold() == expected.casefold()
    if isinstance(expected, str) and isinstance(value, str) and expected.casefold() == "either":
        return value.casefold() in {"star", "square"}
    if isinstance(expected, Sequence) and not isinstance(expected, (str, bytes)):
        if isinstance(value, Sequence) and not isinstance(value, (str, bytes)):
            if len(value) != len(expected):
                return False
            return all(_matches_requirement(actual, condition) for actual, condition in zip(value, expected))
        return value in expected
    return value == expected


def _valid_ivs(value):
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes)) or len(value) != 6:
        return False
    return all(
        isinstance(pair, Sequence) and not isinstance(pair, (str, bytes)) and len(pair) == 2
        and all(isinstance(item, int) and not isinstance(item, bool) for item in pair)
        and 0 <= pair[0] <= pair[1] <= 31
        for pair in value
    )


def _json_object(value, name):
    if not isinstance(value, Mapping):
        raise CaptureEvidenceError(f"{name} must be an object")
    try:
        result = json.loads(json.dumps(dict(value), ensure_ascii=False, allow_nan=False))
    except (TypeError, ValueError) as exc:
        raise CaptureEvidenceError(f"{name} must be JSON-compatible") from exc
    if not isinstance(result, dict):
        raise CaptureEvidenceError(f"{name} must be an object")
    return result


def _json_value(value, name):
    try:
        return json.loads(json.dumps(value, ensure_ascii=False, allow_nan=False))
    except (TypeError, ValueError) as exc:
        raise CaptureEvidenceError(f"{name} must be JSON-compatible") from exc


def _canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":"))


def _ids(values, name, *, allow_empty=False):
    if not isinstance(values, Sequence) or isinstance(values, (str, bytes)):
        raise CaptureEvidenceError(f"{name} must be a sequence of strings")
    if any(not isinstance(value, str) or not value.strip() for value in values):
        raise CaptureEvidenceError(f"{name} must contain non-empty strings")
    result = tuple(dict.fromkeys(value.strip() for value in values))
    if not result and not allow_empty:
        raise CaptureEvidenceError(f"{name} cannot be empty")
    return result


def _confidence(value):
    if isinstance(value, bool) or not isinstance(value, (float, int)) or not math.isfinite(value) or not 0 <= value <= 1:
        raise CaptureEvidenceError("confidence must be between 0 and 1")


def _seed(value, name):
    if not isinstance(value, str) or len(value) != 16 or any(char not in "0123456789abcdefABCDEF" for char in value):
        raise CaptureEvidenceError(f"{name} must be a 16-character hexadecimal state")
    return value.lower()


def _position(value, name):
    if isinstance(value, bool) or not isinstance(value, int) or not 0 <= value <= 1_000_000_000:
        raise CaptureEvidenceError(f"{name} must be between 0 and 1,000,000,000")
    return value


def _range(value, name, *, maximum, maximum_width=None):
    if (not isinstance(value, Sequence) or isinstance(value, (str, bytes)) or len(value) != 2
            or any(isinstance(item, bool) or not isinstance(item, int) for item in value)):
        raise CaptureEvidenceError(f"{name} must be a two-integer inclusive range")
    low, high = value
    if low < 0 or high < low or high > maximum:
        raise CaptureEvidenceError(f"{name} is outside the supported range")
    if maximum_width is not None and high - low >= maximum_width:
        raise CaptureEvidenceError(f"{name} cannot exceed {maximum_width:,} positions")
