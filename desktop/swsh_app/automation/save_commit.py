"""Crash-safe local success commit and save receipt recovery state machine."""
from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import tempfile
from typing import Any


SAVE_COMMIT_SCHEMA = "auto-swsh-save-commit"
SAVE_COMMIT_VERSION = 2
SAVE_STAGES = ("SuccessDetected", "SaveRequested", "SaveConfirmed", "Completed")


class SaveCommitError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class SaveReceipt:
    attempt_id: str
    target_identity: str
    target_slot: str
    save_page_evidence_id: str
    post_save_identity: str
    post_save_identity_evidence_ids: tuple[str, ...]
    algorithm_commit: str
    script_revision: str
    confirmed_at_utc: str


@dataclass(frozen=True, slots=True)
class SaveCommit:
    run_id: str
    epoch_id: str
    attempt_id: str
    stage: str
    target_identity: str
    target_slot: str
    target_evidence_ids: tuple[str, ...]
    algorithm_commit: str
    script_revision: str
    created_at_utc: str
    updated_at_utc: str
    save_evidence_ids: tuple[str, ...] = ()
    receipt: SaveReceipt | None = None


class SaveCommitStore:
    """Persists every protocol boundary before the caller may continue."""

    def __init__(self, path):
        self.path = Path(path).expanduser().resolve()

    def start(self, *, run_id, epoch_id, attempt_id, target_identity, target_slot,
              target_evidence_ids, algorithm_commit, script_revision, created_at_utc=None):
        if self.path.exists():
            prior = self.load()
            if prior.stage != "Completed":
                raise SaveCommitError("an unfinished save commit must be recovered first")
        now = created_at_utc or _utc_now()
        commit = SaveCommit(
            run_id=_required(run_id, "run_id"),
            epoch_id=_required(epoch_id, "epoch_id"),
            attempt_id=_required(attempt_id, "attempt_id"),
            stage="SuccessDetected",
            target_identity=_required(target_identity, "target_identity"),
            target_slot=_required(target_slot, "target_slot"),
            target_evidence_ids=_required_ids(target_evidence_ids, "target_evidence_ids"),
            algorithm_commit=_validate_commit(algorithm_commit),
            script_revision=_required(script_revision, "script_revision"),
            created_at_utc=_validate_utc(now),
            updated_at_utc=now,
        )
        self._write(commit)
        return commit

    def load(self):
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise SaveCommitError(f"cannot read save commit: {exc}") from exc
        return _decode_commit(raw)

    def request_save(self):
        commit = self.load()
        if commit.stage != "SuccessDetected":
            return commit, False
        updated = _replace_stage(commit, "SaveRequested")
        self._write(updated)
        return updated, True

    def confirm_save(self, *, save_evidence_ids):
        commit = self.load()
        if commit.stage != "SaveRequested":
            raise SaveCommitError("save can only be confirmed after SaveRequested")
        updated = _replace_stage(
            commit,
            "SaveConfirmed",
            save_evidence_ids=_required_ids(save_evidence_ids, "save_evidence_ids"),
        )
        self._write(updated)
        return updated

    def complete(self, receipt: SaveReceipt):
        commit = self.load()
        if commit.stage != "SaveConfirmed":
            raise SaveCommitError("completion requires SaveConfirmed")
        _validate_receipt(receipt, commit)
        updated = _replace_stage(commit, "Completed", receipt=receipt)
        self._write(updated)
        return updated

    def recovery_decision(self):
        commit = self.load()
        return {
            "stage": commit.stage,
            "decision": {
                "SuccessDetected": "verify_target_then_request_save",
                "SaveRequested": "hold_scene_and_verify_save_before_any_retry",
                "SaveConfirmed": "verify_post_save_identity_then_complete",
                "Completed": "already_completed",
            }[commit.stage],
            "allow_ordinary_restart": False,
            "allow_save_action": False,
            "attemptId": commit.attempt_id,
        }

    def _write(self, commit):
        _encode_commit(commit)  # Validate before touching the last durable record.
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="w", encoding="utf-8", newline="\n", prefix=f".{self.path.name}.",
                suffix=".tmp", dir=self.path.parent, delete=False,
            ) as stream:
                temporary = Path(stream.name)
                json.dump(_encode_commit(commit), stream, ensure_ascii=False, indent=2, sort_keys=True)
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.path)
        finally:
            if temporary is not None and temporary.exists():
                temporary.unlink()


def _replace_stage(commit, stage, *, save_evidence_ids=None, receipt=None):
    if stage not in SAVE_STAGES or SAVE_STAGES.index(stage) != SAVE_STAGES.index(commit.stage) + 1:
        raise SaveCommitError("save commit stage transition is invalid")
    return SaveCommit(
        **{
            **asdict(commit),
            "stage": stage,
            "updated_at_utc": _utc_now(),
            "save_evidence_ids": commit.save_evidence_ids if save_evidence_ids is None else save_evidence_ids,
            "receipt": receipt,
        }
    )


def _encode_commit(commit: SaveCommit) -> dict[str, Any]:
    if commit.stage not in SAVE_STAGES:
        raise SaveCommitError("save commit stage is invalid")
    payload = asdict(commit)
    payload["schema"] = SAVE_COMMIT_SCHEMA
    payload["schemaVersion"] = SAVE_COMMIT_VERSION
    payload["targetEvidenceIds"] = list(payload.pop("target_evidence_ids"))
    payload["saveEvidenceIds"] = list(payload.pop("save_evidence_ids"))
    payload["algorithmCommit"] = payload.pop("algorithm_commit")
    payload["scriptRevision"] = payload.pop("script_revision")
    payload["runId"] = payload.pop("run_id")
    payload["epochId"] = payload.pop("epoch_id")
    payload["attemptId"] = payload.pop("attempt_id")
    payload["targetIdentity"] = payload.pop("target_identity")
    payload["targetSlot"] = payload.pop("target_slot")
    payload["createdAtUtc"] = payload.pop("created_at_utc")
    payload["updatedAtUtc"] = payload.pop("updated_at_utc")
    payload["receipt"] = _encode_receipt(commit.receipt)
    return payload


def _decode_commit(raw):
    if (not isinstance(raw, dict) or raw.get("schema") != SAVE_COMMIT_SCHEMA
            or isinstance(raw.get("schemaVersion"), bool)
            or raw.get("schemaVersion") not in (1, SAVE_COMMIT_VERSION)):
        raise SaveCommitError("unsupported save commit schema")
    legacy = raw.get("schemaVersion") == 1
    stage = raw.get("stage")
    if stage not in SAVE_STAGES:
        raise SaveCommitError("save commit stage is invalid")
    receipt = _decode_receipt(raw.get("receipt"), legacy=legacy)
    commit = SaveCommit(
        run_id=_required(raw.get("runId"), "runId"),
        epoch_id=_required(raw.get("epochId"), "epochId"),
        attempt_id=_required(raw.get("attemptId"), "attemptId"),
        stage=stage,
        target_identity=_required(raw.get("targetIdentity"), "targetIdentity"),
        target_slot=_required(raw.get("targetSlot"), "targetSlot"),
        target_evidence_ids=_required_ids(raw.get("targetEvidenceIds"), "targetEvidenceIds"),
        algorithm_commit=_validate_commit(raw.get("algorithmCommit")),
        script_revision=_required(raw.get("scriptRevision"), "scriptRevision"),
        created_at_utc=_validate_utc(raw.get("createdAtUtc")),
        updated_at_utc=_validate_utc(raw.get("updatedAtUtc")),
        save_evidence_ids=_required_ids(raw.get("saveEvidenceIds", []), "saveEvidenceIds", allow_empty=True),
        receipt=receipt,
    )
    requires_save_evidence = stage in ("SaveConfirmed", "Completed")
    if requires_save_evidence != bool(commit.save_evidence_ids):
        raise SaveCommitError("SaveConfirmed and Completed require save evidence")
    if (stage == "Completed") != (receipt is not None):
        raise SaveCommitError("Completed requires a SaveReceipt and earlier stages cannot carry one")
    if stage == "Completed" and not receipt.post_save_identity_evidence_ids:
        raise SaveCommitError("legacy Completed receipt lacks post-save identity evidence")
    return commit


def _encode_receipt(receipt):
    if receipt is None:
        return None
    return {
        "attemptId": receipt.attempt_id,
        "targetIdentity": receipt.target_identity,
        "targetSlot": receipt.target_slot,
        "savePageEvidenceId": receipt.save_page_evidence_id,
        "postSaveIdentity": receipt.post_save_identity,
        "postSaveIdentityEvidenceIds": list(receipt.post_save_identity_evidence_ids),
        "algorithmCommit": receipt.algorithm_commit,
        "scriptRevision": receipt.script_revision,
        "confirmedAtUtc": receipt.confirmed_at_utc,
    }


def _decode_receipt(raw, *, legacy=False):
    if raw is None:
        return None
    if not isinstance(raw, dict):
        raise SaveCommitError("receipt must be an object or null")
    return SaveReceipt(
        attempt_id=_required(raw.get("attemptId"), "receipt.attemptId"),
        target_identity=_required(raw.get("targetIdentity"), "receipt.targetIdentity"),
        target_slot=_required(raw.get("targetSlot"), "receipt.targetSlot"),
        save_page_evidence_id=_required(raw.get("savePageEvidenceId"), "receipt.savePageEvidenceId"),
        post_save_identity=_required(raw.get("postSaveIdentity"), "receipt.postSaveIdentity"),
        post_save_identity_evidence_ids=(
            _required_ids(raw.get("postSaveIdentityEvidenceIds"), "receipt.postSaveIdentityEvidenceIds")
            if raw.get("postSaveIdentityEvidenceIds") is not None
            else () if legacy else _required_ids(None, "receipt.postSaveIdentityEvidenceIds")
        ),
        algorithm_commit=_validate_commit(raw.get("algorithmCommit")),
        script_revision=_required(raw.get("scriptRevision"), "receipt.scriptRevision"),
        confirmed_at_utc=_validate_utc(raw.get("confirmedAtUtc")),
    )


def _validate_receipt(receipt, commit):
    if not isinstance(receipt, SaveReceipt):
        raise SaveCommitError("receipt must be a SaveReceipt")
    if receipt.attempt_id != commit.attempt_id:
        raise SaveCommitError("receipt attempt does not match the active commit")
    if receipt.target_identity != commit.target_identity or receipt.post_save_identity != commit.target_identity:
        raise SaveCommitError("post-save identity does not match the detected target")
    if receipt.target_slot != commit.target_slot:
        raise SaveCommitError("receipt slot does not match the expected target slot")
    if receipt.save_page_evidence_id not in commit.save_evidence_ids:
        raise SaveCommitError("receipt must reference confirmed save evidence")
    _required_ids(receipt.post_save_identity_evidence_ids, "receipt.postSaveIdentityEvidenceIds")
    if receipt.algorithm_commit != commit.algorithm_commit or receipt.script_revision != commit.script_revision:
        raise SaveCommitError("receipt version fingerprint changed during save")
    _validate_utc(receipt.confirmed_at_utc)


def _required(value, name):
    if not isinstance(value, str) or not value.strip():
        raise SaveCommitError(f"{name} is required")
    return value.strip()


def _required_ids(values, name, *, allow_empty=False):
    if not isinstance(values, (list, tuple)) or any(not isinstance(value, str) or not value.strip() for value in values):
        raise SaveCommitError(f"{name} must contain evidence IDs")
    normalized = tuple(dict.fromkeys(value.strip() for value in values))
    if not normalized and not allow_empty:
        raise SaveCommitError(f"{name} cannot be empty")
    return normalized


def _validate_commit(value):
    if not isinstance(value, str) or len(value) != 40 or any(char not in "0123456789abcdefABCDEF" for char in value):
        raise SaveCommitError("algorithmCommit must be a 40-character hash")
    return value.lower()


def _validate_utc(value):
    if not isinstance(value, str):
        raise SaveCommitError("timestamp must be a string")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise SaveCommitError("timestamp must be ISO-8601") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise SaveCommitError("timestamp must include a UTC offset")
    return parsed.astimezone(timezone.utc).isoformat()


def _utc_now():
    return datetime.now(timezone.utc).isoformat()
