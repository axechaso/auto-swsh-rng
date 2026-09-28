"""Replay-driven M5 capture, reverse lookup, verification, and save commit.

The replay adapter consumes ordered video segments and reviewed sidecar reads.
It is an offline integration path; it does not classify game screens or issue
hardware actions on its own.
"""
from __future__ import annotations

from collections import defaultdict
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
import json
import threading
from types import MappingProxyType
from typing import Any, Callable, Mapping, Protocol, Sequence

from .attempt_execution import AttemptExecutionResult, AttemptExecutionStatus
from .capture_workflow import (
    CaptureEvent,
    CaptureEvidenceError,
    CapturePhase,
    CaptureWorkflow,
    EncounterAttemptSnapshot,
    FieldObservation,
    FieldRead,
    ObservationStatus,
    RosterMember,
    TargetVerificationResult,
    aggregate_field_reads,
    identify_new_roster_entity,
    verify_capture_target,
)
from .replay import ReplayFrameSource
from .save_commit import SaveCommitError, SaveCommitStore, SaveReceipt


class CaptureAttemptError(ValueError):
    pass


class CaptureAttemptCancelled(RuntimeError):
    pass


class CaptureActionPort(Protocol):
    def peek_event_kind(self) -> str: ...
    def next_capture_event(self, expected_kind: str | None = None) -> CaptureEvent: ...
    def read_roster_snapshot(self, point: str) -> tuple[tuple[RosterMember, ...], tuple[str, ...]]: ...
    def read_field_reads(self, field_id: str) -> tuple[FieldRead, ...]: ...
    def perform_save_action(self) -> tuple[str, ...]: ...
    def read_save_confirmation(self) -> tuple[str, ...]: ...
    def read_post_save_identity(self) -> tuple[str, str, tuple[str, ...]]: ...
    def stop_scripts(self) -> None: ...


@dataclass(frozen=True, slots=True)
class ReplayCaptureStep:
    kind: str
    details: Mapping[str, Any]


class ReplayCaptureFixture:
    """Reviewed offline data that accompanies a capture video replay."""

    def __init__(self, *, events: Sequence[ReplayCaptureStep | Mapping[str, Any]],
                 roster_before: Sequence[RosterMember], roster_after: Sequence[RosterMember],
                 field_reads: Mapping[str, Sequence[FieldRead]],
                 post_save_identity: str, post_save_slot: str):
        normalized_events = []
        for item in events:
            if isinstance(item, ReplayCaptureStep):
                step = item
            elif isinstance(item, Mapping):
                kind = item.get("kind")
                details = item.get("details", {})
                if not isinstance(kind, str) or not isinstance(details, Mapping):
                    raise CaptureAttemptError("replay events require kind and details")
                step = ReplayCaptureStep(kind, MappingProxyType(_json_object(details, "event details")))
            else:
                raise CaptureAttemptError("replay events must be objects")
            if not step.kind:
                raise CaptureAttemptError("replay event kind is required")
            normalized_events.append(step)
        if not normalized_events:
            raise CaptureAttemptError("replay capture fixture requires capture events")
        before = tuple(roster_before)
        after = tuple(roster_after)
        if any(not isinstance(item, RosterMember) for item in (*before, *after)):
            raise CaptureAttemptError("roster snapshots must contain RosterMember values")
        if not isinstance(field_reads, Mapping):
            raise CaptureAttemptError("field_reads must be keyed by field identifier")
        normalized_reads = {}
        for field_id, reads in field_reads.items():
            values = tuple(reads)
            if not field_id or any(not isinstance(item, FieldRead) or item.field_id != field_id for item in values):
                raise CaptureAttemptError("field read groups must contain matching FieldRead values")
            normalized_reads[field_id] = values
        if not isinstance(post_save_identity, str) or not post_save_identity.strip():
            raise CaptureAttemptError("post_save_identity is required")
        if not isinstance(post_save_slot, str) or not post_save_slot.strip():
            raise CaptureAttemptError("post_save_slot is required")
        self.events = tuple(normalized_events)
        self.roster_before = before
        self.roster_after = after
        self.field_reads = MappingProxyType(normalized_reads)
        self.post_save_identity = post_save_identity
        self.post_save_slot = post_save_slot


class ReplayCaptureActionPort:
    """Consume each capture/save action's exact frame segment in manifest order."""

    def __init__(self, frame_source: ReplayFrameSource, fixture: ReplayCaptureFixture):
        if not isinstance(frame_source, ReplayFrameSource):
            raise TypeError("frame_source must be a ReplayFrameSource")
        if not isinstance(fixture, ReplayCaptureFixture):
            raise TypeError("fixture must be a ReplayCaptureFixture")
        self.frame_source = frame_source
        self.fixture = fixture
        self._event_offset = 0
        self._action_counts: dict[str, int] = defaultdict(int)
        self.events: list[dict[str, Any]] = []
        self._stopped = False

    def stop_scripts(self) -> None:
        self._stopped = True
        self.events.append({"kind": "stop_scripts"})

    def peek_event_kind(self) -> str:
        if self._event_offset >= len(self.fixture.events):
            raise CaptureAttemptError("replay event script ended unexpectedly")
        return self.fixture.events[self._event_offset].kind

    def next_capture_event(self, expected_kind: str | None = None) -> CaptureEvent:
        kind = self.peek_event_kind()
        if expected_kind is not None and kind != expected_kind:
            raise CaptureAttemptError(f"expected replay event {expected_kind}, received {kind}")
        step = self.fixture.events[self._event_offset]
        self._event_offset += 1
        evidence_ids = self._consume(kind)
        self.events.append({"kind": kind, "evidenceIds": list(evidence_ids)})
        return CaptureEvent(kind, evidence_ids, step.details)

    def read_roster_snapshot(self, point: str) -> tuple[tuple[RosterMember, ...], tuple[str, ...]]:
        if point not in {"before", "after"}:
            raise CaptureAttemptError("roster point must be before or after")
        evidence_ids = self._consume(f"roster_{point}")
        roster = self.fixture.roster_before if point == "before" else self.fixture.roster_after
        self.events.append({"kind": f"roster_{point}", "evidenceIds": list(evidence_ids)})
        return roster, evidence_ids

    def read_field_reads(self, field_id: str) -> tuple[FieldRead, ...]:
        evidence_ids = self._consume(f"observe_{field_id}")
        reads = self.fixture.field_reads.get(field_id, ())
        frame_ids = set(self._last_frame_ids)
        evidence_set = set(evidence_ids)
        if not reads:
            raise CaptureAttemptError(f"replay is missing reviewed reads for {field_id}")
        if any(read.frame_id not in frame_ids or not set(read.evidence_ids) <= evidence_set for read in reads):
            raise CaptureAttemptError(f"{field_id} reads do not reference their consumed replay frames")
        self.events.append({"kind": f"observe_{field_id}", "evidenceIds": list(evidence_ids)})
        return reads

    def perform_save_action(self) -> tuple[str, ...]:
        evidence_ids = self._consume("save_action")
        self.events.append({"kind": "save_action", "evidenceIds": list(evidence_ids)})
        return evidence_ids

    def read_save_confirmation(self) -> tuple[str, ...]:
        evidence_ids = self._consume("save_confirmation")
        self.events.append({"kind": "save_confirmation", "evidenceIds": list(evidence_ids)})
        return evidence_ids

    def read_post_save_identity(self) -> tuple[str, str, tuple[str, ...]]:
        evidence_ids = self._consume("post_save_identity")
        self.events.append({"kind": "post_save_identity", "evidenceIds": list(evidence_ids)})
        return self.fixture.post_save_identity, self.fixture.post_save_slot, evidence_ids

    def _consume(self, purpose: str) -> tuple[str, ...]:
        if self._stopped:
            raise CaptureAttemptCancelled("replay capture actions were stopped")
        index = self._action_counts[purpose]
        self._action_counts[purpose] += 1
        self.frame_source.trigger(purpose, index)
        frames = self.frame_source.consume_active_segment()
        if not frames:
            raise CaptureAttemptError(f"replay action {purpose}[{index}] has no evidence frames")
        self._last_frame_ids = tuple(frame.frame_id for frame in frames)
        return tuple(dict.fromkeys(frame.evidence_id for frame in frames))


@dataclass(frozen=True, slots=True)
class CaptureAttemptResult:
    status: str
    attempt_id: str
    workflow_report: Mapping[str, Any]
    identity: Mapping[str, Any] | None
    observations: Mapping[str, Mapping[str, Any]]
    reverse_result: Mapping[str, Any] | None
    target_verification: Mapping[str, Any] | None
    save_commit: Mapping[str, Any] | None
    hardware_status: str
    reason_code: str | None
    reason: str | None

    def as_dict(self) -> dict[str, Any]:
        return {
            "reportSchema": "auto-swsh-capture-attempt",
            "reportVersion": 1,
            "status": self.status,
            "attemptId": self.attempt_id,
            "workflow": dict(self.workflow_report),
            "identity": dict(self.identity) if self.identity else None,
            "observations": {key: dict(value) for key, value in self.observations.items()},
            "reverseLookup": _json_value(self.reverse_result),
            "targetVerification": dict(self.target_verification) if self.target_verification else None,
            "saveCommit": _json_value(self.save_commit),
            "hardwareStatus": self.hardware_status,
            "canStartFormalAutomation": False,
            "reasonCode": self.reason_code,
            "reason": self.reason,
        }


class CaptureAttemptCoordinator:
    """Continue one triggered M4 attempt through identity, reverse, and T40."""

    def __init__(self, *, attempt_result: AttemptExecutionResult,
                 encounter_snapshot: EncounterAttemptSnapshot,
                 actions: CaptureActionPort,
                 reverse_lookup: Callable[[dict[str, Any]], Mapping[str, Any]],
                 save_store: SaveCommitStore,
                 requirements: Mapping[str, Any],
                 observation_fields: Sequence[str],
                 algorithm_commit: str, script_revision: str,
                 storage_mode: str = "reserved_party_slot",
                 expected_slot: str | None = None,
                 box_navigation_evidence_ids: Sequence[str] = (),
                 maximum_ball_throws: int = 8,
                 cancel_event: threading.Event | None = None):
        if not isinstance(attempt_result, AttemptExecutionResult):
            raise CaptureAttemptError("attempt_result must be an AttemptExecutionResult")
        if not isinstance(encounter_snapshot, EncounterAttemptSnapshot):
            raise CaptureAttemptError("encounter_snapshot must be an EncounterAttemptSnapshot")
        if attempt_result.status != AttemptExecutionStatus.TRIGGER_EXECUTED:
            raise CaptureAttemptError("capture requires a successfully executed M4 trigger")
        if (encounter_snapshot.planned_generation_advance is None
                or encounter_snapshot.planned_generation_advance != attempt_result.target_generation_advance):
            raise CaptureAttemptError("encounter snapshot must match the selected M4 generation advance")
        if attempt_result.attempt_id == "" or not callable(reverse_lookup):
            raise CaptureAttemptError("attempt identity and reverse_lookup are required")
        if not isinstance(save_store, SaveCommitStore):
            raise CaptureAttemptError("save_store must be a SaveCommitStore")
        if not isinstance(requirements, Mapping) or not requirements:
            raise CaptureAttemptError("target requirements must be a non-empty mapping")
        observation_fields = tuple(observation_fields)
        if not observation_fields or any(not isinstance(field, str) or not field for field in observation_fields):
            raise CaptureAttemptError("observation field identifiers must be non-empty strings")
        if len(set(observation_fields)) != len(observation_fields):
            raise CaptureAttemptError("observation_fields must be unique")
        if (isinstance(actions, ReplayCaptureActionPort)
                and actions.frame_source.source_kind != encounter_snapshot.source_kind):
            raise CaptureAttemptError("capture replay source kind does not match the frozen encounter snapshot")
        self.attempt_result = attempt_result
        self.snapshot = encounter_snapshot
        self.actions = actions
        self.reverse_lookup = reverse_lookup
        self.save_store = save_store
        self.requirements = MappingProxyType(_json_object(requirements, "target requirements"))
        self.observation_fields = tuple(observation_fields)
        self.algorithm_commit = algorithm_commit
        self.script_revision = script_revision
        self.storage_mode = storage_mode
        self.expected_slot = expected_slot
        self.box_navigation_evidence_ids = tuple(box_navigation_evidence_ids)
        self.maximum_ball_throws = maximum_ball_throws
        self.cancel_event = cancel_event or threading.Event()
        self._has_run = False

    def cancel(self):
        self.cancel_event.set()
        stop = getattr(self.actions, "stop_scripts", None)
        if callable(stop):
            try:
                stop()
            except Exception:
                pass

    def run(self) -> CaptureAttemptResult:
        if self._has_run:
            raise RuntimeError("a capture attempt coordinator can run only once")
        self._has_run = True
        workflow = CaptureWorkflow(
            attempt_id=self.attempt_result.attempt_id,
            max_ball_throws=self.maximum_ball_throws,
        )
        identity = None
        observations: dict[str, FieldObservation] = {}
        reverse_result = None
        verification = None
        reason_code = None
        reason = None
        try:
            self._check_cancelled()
            before, before_ids = self.actions.read_roster_snapshot("before")
            for event_kind in ("battle_entered", "opponent_observed"):
                event = self.actions.next_capture_event(event_kind)
                workflow.apply(event)
                if event_kind == "opponent_observed":
                    opponent = dict(event.details)
            expected_species = opponent.get("species")
            expected_level = opponent.get("level")
            if (not isinstance(expected_species, str) or not expected_species
                    or isinstance(expected_level, bool) or not isinstance(expected_level, int)):
                raise CaptureAttemptError("battle evidence must identify opponent species and level")
            scene_match = all(
                opponent.get(key) == getattr(self.snapshot, snapshot_field)
                for key, snapshot_field in (("kind", "kind"), ("area", "area"), ("weather", "weather"))
            )
            self._capture_until_complete(workflow)
            if workflow.phase == CapturePhase.NEEDS_ATTENTION:
                reason_code = workflow.events[-1]["details"].get(
                    "reasonCode", workflow.events[-1]["event"].upper(),
                )
                reason = "capture ended without a confirmed catch"
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, reason_code, reason)
            self._resolve_post_capture_prompts(workflow)
            self._check_cancelled()
            after, after_ids = self.actions.read_roster_snapshot("after")
            identity = identify_new_roster_entity(
                before=before,
                after=after,
                before_evidence_ids=before_ids,
                after_evidence_ids=after_ids,
                storage_mode=self.storage_mode,
                expected_slot=self.expected_slot,
                box_navigation_evidence_ids=self.box_navigation_evidence_ids,
                expected_species=expected_species,
                expected_level=expected_level,
            )
            if identity.status != "known":
                workflow.apply(CaptureEvent("stop", (), {
                    "reasonCode": identity.reason_code,
                    "reason": "new captured entity could not be uniquely identified",
                }))
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, identity.reason_code,
                                    "new captured entity could not be uniquely identified")
            workflow.apply(CaptureEvent(
                "new_entity_confirmed",
                tuple(dict.fromkeys((*identity.evidence_ids, *self._opponent_evidence_ids))),
                {"identityKey": identity.identity_key, "sceneMatch": scene_match},
            ))
            if not scene_match:
                workflow.apply(CaptureEvent("stop", (), {
                    "reasonCode": "CAPTURE_SCENE_MISMATCH",
                    "reason": "battle display did not match the frozen encounter scene",
                }))
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, "CAPTURE_SCENE_MISMATCH",
                                    "battle display did not match the frozen encounter scene")

            new_member = next(member for member in after if member.slot == identity.storage_slot)
            for field_id in self.observation_fields:
                self._check_cancelled()
                reads = self.actions.read_field_reads(field_id)
                observation = aggregate_field_reads(reads)
                observations[field_id] = observation
                if (field_id == "species" and observation.status == ObservationStatus.KNOWN
                        and observation.value != new_member.species):
                    raise CaptureAttemptError("summary species does not match the identified roster entity")
                if (field_id == "level" and observation.status == ObservationStatus.KNOWN
                        and observation.value != new_member.level):
                    raise CaptureAttemptError("summary level does not match the identified roster entity")
            observation_ids = tuple(dict.fromkeys(
                evidence_id for observation in observations.values()
                for evidence_id in observation.evidence_ids
            ))
            workflow.apply(CaptureEvent("observations_complete", observation_ids))
            self._check_cancelled()
            request = self.snapshot.build_reverse_request(observations=observations, workflow=workflow)
            reverse_result = dict(self.reverse_lookup(request))
            if reverse_result.get("complete") is not True or reverse_result.get("truncated") is True:
                reason_code = "REVERSE_SEARCH_INCOMPLETE"
                reason = "reverse lookup did not complete the full diagnostic window"
                workflow.apply(CaptureEvent("stop", (), {"reasonCode": reason_code, "reason": reason}))
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, reason_code, reason)
            workflow.apply(CaptureEvent("reverse_complete", tuple(reverse_result.get("evidenceIds", ())), {
                "complete": True, "truncated": False,
            }))
            verification = verify_capture_target(
                capture_succeeded=workflow.capture_succeeded,
                identity_confirmed=workflow.identity_confirmed,
                scene_match=workflow.scene_match,
                source_kind=self.snapshot.source_kind,
                user_range=self.snapshot.target_range,
                requirements=self.requirements,
                observations=observations,
                reverse_result=reverse_result,
            )
            if not verification.verified:
                workflow.apply(CaptureEvent("target_not_verified", tuple(reverse_result.get("evidenceIds", ())), {
                    "reasonCode": verification.reason_code,
                }))
                reason_code = verification.reason_code
                reason = "captured entity did not meet every required target condition"
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, reason_code, reason)
            workflow.apply(CaptureEvent("target_verified", tuple(dict.fromkeys((
                *identity.evidence_ids, *observation_ids, *reverse_result.get("evidenceIds", ()),
            ))), {"verified": True, "verificationLevel": verification.verification_level}))

            target_evidence_ids = tuple(dict.fromkeys((
                *identity.evidence_ids, *observation_ids, *reverse_result.get("evidenceIds", ()),
            )))
            self._check_cancelled()
            self.save_store.start(
                run_id=self.attempt_result.run_id,
                epoch_id=self.attempt_result.epoch_id,
                attempt_id=self.attempt_result.attempt_id,
                target_identity=identity.identity_key,
                target_slot=identity.storage_slot,
                target_evidence_ids=target_evidence_ids,
                algorithm_commit=self.algorithm_commit,
                script_revision=self.script_revision,
            )
            _, should_issue_save = self.save_store.request_save()
            if not should_issue_save:
                raise SaveCommitError("recovered SaveRequested state blocks another save action")
            self._check_cancelled()
            save_action_ids = self.actions.perform_save_action()
            if not save_action_ids:
                raise CaptureAttemptError("save action has no replay evidence")
            self._check_cancelled()
            save_evidence_ids = self.actions.read_save_confirmation()
            if not save_evidence_ids:
                raise CaptureAttemptError("save completion has no replay evidence")
            self.save_store.confirm_save(save_evidence_ids=save_evidence_ids)
            self._check_cancelled()
            post_identity, post_slot, post_ids = self.actions.read_post_save_identity()
            if (post_identity != identity.identity_key or post_slot != identity.storage_slot or not post_ids):
                reason_code = "POST_SAVE_IDENTITY_MISMATCH"
                reason = "post-save roster check did not confirm the target identity and slot"
                workflow.apply(CaptureEvent("save_unconfirmed", tuple(post_ids), {
                    "reasonCode": reason_code,
                }))
                return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                    reverse_result, verification, reason_code, reason)
            receipt = SaveReceipt(
                attempt_id=self.attempt_result.attempt_id,
                target_identity=identity.identity_key,
                target_slot=identity.storage_slot,
                save_page_evidence_id=save_evidence_ids[0],
                post_save_identity=post_identity,
                post_save_identity_evidence_ids=post_ids,
                algorithm_commit=self.algorithm_commit,
                script_revision=self.script_revision,
                confirmed_at_utc=datetime.now(timezone.utc).isoformat(),
            )
            completed = self.save_store.complete(receipt)
            completed_evidence = tuple(dict.fromkeys((
                *save_action_ids, *save_evidence_ids, *post_ids,
            )))
            workflow.apply(CaptureEvent("save_completed", completed_evidence, {
                "saveCommitStage": completed.stage,
            }))
            return self._result("SAVED_SUCCESS", workflow, identity, observations,
                                reverse_result, verification, None, None)
        except CaptureAttemptCancelled as exc:
            reason_code = "USER_CANCELLED"
            reason = str(exc)
            if workflow.phase not in (CapturePhase.COMPLETED, CapturePhase.NEEDS_ATTENTION):
                workflow.apply(CaptureEvent("stop", (), {"reasonCode": reason_code, "reason": reason}))
            return self._result("CANCELLED", workflow, identity, observations,
                                reverse_result, verification, reason_code, reason)
        except Exception as exc:
            reason_code = reason_code or "CAPTURE_ATTEMPT_FAILED"
            reason = f"{type(exc).__name__}: {exc}"
            if workflow.phase not in (CapturePhase.COMPLETED, CapturePhase.NEEDS_ATTENTION):
                workflow.apply(CaptureEvent("stop", (), {"reasonCode": reason_code, "reason": reason}))
            return self._result("NEEDS_ATTENTION", workflow, identity, observations,
                                reverse_result, verification, reason_code, reason)

    def _capture_until_complete(self, workflow: CaptureWorkflow) -> None:
        while not workflow.capture_succeeded and workflow.phase != CapturePhase.NEEDS_ATTENTION:
            self._check_cancelled()
            workflow.apply(self.actions.next_capture_event("prepare_move_or_item"))
            workflow.apply(self.actions.next_capture_event("open_ball_selection"))
            workflow.apply(self.actions.next_capture_event("ball_thrown"))
            outcome = self.actions.next_capture_event()
            if outcome.kind not in {
                "capture_failed", "capture_confirmed", "opponent_escaped", "player_fainted",
                "ball_exhausted", "unexpected_battle_end",
            }:
                raise CaptureAttemptError("ball animation ended in an unsupported battle outcome")
            workflow.apply(outcome)

    def _resolve_post_capture_prompts(self, workflow: CaptureWorkflow) -> None:
        while self.actions.peek_event_kind() != "post_capture_complete":
            self._check_cancelled()
            opened = self.actions.next_capture_event("prompt_opened")
            workflow.apply(opened)
            prompt_kind = opened.details.get("promptKind")
            resolved = self.actions.next_capture_event("prompt_resolved")
            workflow.apply(resolved)
            if resolved.details.get("promptKind") != prompt_kind:
                raise CaptureAttemptError("post-capture prompt resolution changed prompt kind")
        workflow.apply(self.actions.next_capture_event("post_capture_complete"))

    def _check_cancelled(self):
        if self.cancel_event.is_set():
            raise CaptureAttemptCancelled("capture attempt cancelled by user")

    @property
    def _opponent_evidence_ids(self):
        for event in reversed(getattr(self.actions, "events", ())):
            if event.get("kind") == "opponent_observed":
                return tuple(event.get("evidenceIds", ()))
        return ()

    def _result(self, status, workflow, identity, observations, reverse_result,
                verification, reason_code, reason):
        commit = None
        if self.save_store.path.exists():
            try:
                loaded = self.save_store.load()
                if loaded.attempt_id == self.attempt_result.attempt_id:
                    commit = asdict(loaded)
            except SaveCommitError:
                pass
        serialized_observations = {
            key: _observation_dict(value) for key, value in observations.items()
        }
        return CaptureAttemptResult(
            status=status,
            attempt_id=self.attempt_result.attempt_id,
            workflow_report=workflow.as_report(),
            identity=asdict(identity) if identity is not None else None,
            observations=serialized_observations,
            reverse_result=reverse_result,
            target_verification=verification.as_dict() if verification else None,
            save_commit=commit,
            hardware_status="PendingHardwareValidation",
            reason_code=reason_code,
            reason=reason,
        )


def _observation_dict(value: FieldObservation) -> dict[str, Any]:
    return {
        "fieldId": value.field_id,
        "status": value.status.value,
        "value": value.value,
        "candidates": list(value.candidates),
        "confidence": value.confidence,
        "rawValues": list(value.raw_values),
        "frameIds": list(value.frame_ids),
        "evidenceIds": list(value.evidence_ids),
    }


def _json_object(value, name):
    if not isinstance(value, Mapping):
        raise CaptureAttemptError(f"{name} must be an object")
    try:
        result = json.loads(json.dumps(dict(value), ensure_ascii=False, allow_nan=False))
    except (TypeError, ValueError) as exc:
        raise CaptureAttemptError(f"{name} must contain JSON values") from exc
    return result


def _json_value(value):
    if value is None:
        return None
    try:
        return json.loads(json.dumps(value, ensure_ascii=False, allow_nan=False))
    except (TypeError, ValueError):
        return None
