"""Durable no-hit restart gate and new-epoch recovery state machine."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

from .feedback import FeedbackArchiveStore, FeedbackDecision, FeedbackError, SHA256_RE


class RetryWorkflowError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class RetryWorkflowResult:
    status: str
    phase: str
    attempt_id: str
    epoch_id: str
    restart_allowed: bool
    reason_code: str
    reason: str | None
    archive_revision: int

    def as_dict(self):
        return {
            "status": self.status,
            "phase": self.phase,
            "attemptId": self.attempt_id,
            "epochId": self.epoch_id,
            "restartAllowed": self.restart_allowed,
            "reasonCode": self.reason_code,
            "reason": self.reason,
            "archiveRevision": self.archive_revision,
        }


class RetryWorkflow:
    """Allow a restart only after a definite miss and durable diagnostics."""

    def __init__(self, *, run_id: str, epoch_id: str, context_fingerprint: str,
                 archive: FeedbackArchiveStore, maximum_restarts: int = 5):
        for name, value in (("run_id", run_id), ("epoch_id", epoch_id)):
            if not isinstance(value, str) or not value.strip():
                raise RetryWorkflowError(f"{name} is required")
        if not isinstance(context_fingerprint, str) or not SHA256_RE.fullmatch(context_fingerprint):
            raise RetryWorkflowError("context_fingerprint must be a SHA-256 digest")
        if not isinstance(archive, FeedbackArchiveStore):
            raise RetryWorkflowError("archive must be a FeedbackArchiveStore")
        if isinstance(maximum_restarts, bool) or not isinstance(maximum_restarts, int) or maximum_restarts < 1:
            raise RetryWorkflowError("maximum_restarts must be positive")
        self.run_id = run_id
        self.epoch_id = epoch_id
        self.context_fingerprint = context_fingerprint
        self.archive = archive
        self.maximum_restarts = maximum_restarts
        self.attempt_id: str | None = None
        self.phase = "awaiting_attempt_result"
        self.status = "pending"
        self.restart_count = 0
        self._last_reason = "ATTEMPT_RESULT_REQUIRED"
        self._last_message = None
        self._restore()

    def decide_after_attempt(self, *, attempt_id: str, attempt_status: str,
                             target_state: str, reverse_complete: bool,
                             save_commit_stage: str | None,
                             evidence_persisted: bool,
                             feedback_decision: FeedbackDecision | None):
        if self.phase not in {
            "awaiting_attempt_result", "awaiting_new_epoch", "retain_scene",
            "hold_save_recovery", "needs_attention",
        }:
            raise RetryWorkflowError("retry workflow is not accepting an attempt result")
        if not attempt_id or not isinstance(attempt_id, str):
            raise RetryWorkflowError("attempt_id is required")
        if self.attempt_id and self.phase != "awaiting_new_epoch" and attempt_id != self.attempt_id:
            raise RetryWorkflowError("attempt identity changed before the next epoch")
        if not isinstance(reverse_complete, bool) or not isinstance(evidence_persisted, bool):
            raise RetryWorkflowError("reverse_complete and evidence_persisted must be boolean")
        self.attempt_id = attempt_id

        if save_commit_stage == "Completed":
            if attempt_status in {"SAVED_SUCCESS", "SUCCESS_SAVED"}:
                return self._record("completed", "SUCCESS_SAVED", "SAVE_RECEIPT_COMPLETED", None)
            return self._record("hold_save_recovery", "NEEDS_ATTENTION", "SAVE_STATE_MISMATCH",
                                "completed save evidence conflicts with the attempt result")
        if save_commit_stage is not None:
            return self._record("hold_save_recovery", "NEEDS_ATTENTION", "SAVE_COMMIT_RECOVERY_REQUIRED",
                                "save may have occurred; finish T40 recovery before any restart")
        if attempt_status in {"SAVED_SUCCESS", "SUCCESS_SAVED"}:
            return self._record("hold_save_recovery", "NEEDS_ATTENTION", "SAVE_RECEIPT_MISSING",
                                "success status lacks a completed SaveReceipt")
        if target_state in {"possible", "unverified", "unknown"}:
            return self._record("retain_scene", "NEEDS_ATTENTION", "POTENTIAL_TARGET_MUST_BE_RETAINED",
                                "a possible or unverified target prevents ordinary restart")
        if target_state != "definite_miss":
            return self._record("needs_attention", "NEEDS_ATTENTION", "ATTEMPT_OUTCOME_NOT_CLASSIFIED",
                                "attempt outcome does not prove success or a definite miss")
        if not reverse_complete:
            return self._record("retain_scene", "NEEDS_ATTENTION", "REVERSE_SEARCH_INCOMPLETE",
                                "finish the full diagnostic window before deciding to restart")
        if not evidence_persisted:
            return self._record("retain_scene", "NEEDS_ATTENTION", "ATTEMPT_EVIDENCE_NOT_PERSISTED",
                                "persist attempt evidence before restarting")
        if not isinstance(feedback_decision, FeedbackDecision):
            return self._record("retain_scene", "NEEDS_ATTENTION", "FEEDBACK_DECISION_MISSING",
                                "persist a model update or explicit sample rejection before restarting")
        if not self._feedback_decision_is_persisted(feedback_decision):
            return self._record("retain_scene", "NEEDS_ATTENTION", "FEEDBACK_DECISION_NOT_PERSISTED",
                                "feedback archive does not contain this sample decision")
        if self.restart_count >= self.maximum_restarts:
            return self._record("recalibration_required", "NEEDS_ATTENTION", "RESTART_LIMIT_REACHED",
                                "restart limit reached; preserve evidence and recalibrate")
        return self._record("restart_authorized", "RESTART_AUTHORIZED", "DEFINITE_MISS_PERSISTED", None)

    def confirm_restart(self, *, restarted: bool, new_epoch_id: str,
                        seed_verified: bool, seed_evidence_ids: tuple[str, ...]):
        if self.phase not in {"restart_authorized", "restart_uncertain", "awaiting_seed_verification"}:
            raise RetryWorkflowError("restart was not authorized")
        if not isinstance(restarted, bool) or not isinstance(seed_verified, bool):
            raise RetryWorkflowError("restart and seed verification states must be boolean")
        if not restarted:
            return self._record("restart_uncertain", "NEEDS_ATTENTION", "RESTART_NOT_CONFIRMED",
                                "the old epoch remains invalid until game restart is confirmed")
        if not isinstance(new_epoch_id, str) or not new_epoch_id:
            return self._record("restart_uncertain", "NEEDS_ATTENTION", "NEW_EPOCH_REQUIRED",
                                "a restarted attempt must receive a new seed epoch")
        if self.phase == "awaiting_seed_verification":
            if new_epoch_id != self.epoch_id:
                raise RetryWorkflowError("pending seed verification belongs to another epoch")
        elif new_epoch_id == self.epoch_id:
            return self._record("restart_uncertain", "NEEDS_ATTENTION", "NEW_EPOCH_REQUIRED",
                                "a restarted attempt must receive a new seed epoch")
        previous_attempt_id = self.attempt_id
        self.epoch_id = new_epoch_id
        if isinstance(seed_evidence_ids, (str, bytes)):
            raise RetryWorkflowError("seed_evidence_ids must be a sequence of evidence IDs")
        evidence = tuple(dict.fromkeys(seed_evidence_ids))
        if (any(not isinstance(item, str) or not item.strip() for item in evidence)
                or not seed_verified or not evidence):
            return self._record("awaiting_seed_verification", "NEEDS_ATTENTION", "NEW_SEED_NOT_VERIFIED",
                                "do not search until the new seed is independently verified",
                                extra={"previousAttemptId": previous_attempt_id})
        self.restart_count += 1
        self.attempt_id = None
        return self._record("awaiting_new_epoch", "READY_FOR_NEW_EPOCH", "NEW_EPOCH_VERIFIED", None,
                            extra={"seedEvidenceIds": list(evidence),
                                   "previousAttemptId": previous_attempt_id})

    def _feedback_decision_is_persisted(self, decision):
        if not decision.sample_id or decision.archive_revision < 1:
            return False
        try:
            document = self.archive.load()
        except FeedbackError:
            return False
        if document.get("contextFingerprint") != self.context_fingerprint:
            return False
        model = document.get("boundaryModel")
        if not isinstance(model, dict) or document.get("archiveRevision", 0) < decision.archive_revision:
            return False
        return any(
            isinstance(row, dict) and row.get("sampleId") == decision.sample_id
            for row in model.get("records", ())
        )

    def _record(self, phase, status, reason_code, reason, *, extra=None):
        self.phase = phase
        self.status = status
        self._last_reason = reason_code
        self._last_message = reason
        current = self.archive.load()
        section = current.get("retryWorkflow") or {
            "runId": self.run_id,
            "restartCount": self.restart_count,
            "events": [],
        }
        if section.get("runId") != self.run_id:
            raise RetryWorkflowError("retry archive belongs to a different run")
        section["restartCount"] = self.restart_count
        section.setdefault("events", []).append({
            "timestampUtc": datetime.now(timezone.utc).isoformat(),
            "phase": phase,
            "status": status,
            "attemptId": self.attempt_id,
            "epochId": self.epoch_id,
            "reasonCode": reason_code,
            "reason": reason,
            **(extra or {}),
        })
        revision = self.archive.save_section(
            "retryWorkflow", section, context_fingerprint=self.context_fingerprint,
        )
        return RetryWorkflowResult(
            status, phase, self.attempt_id or "", self.epoch_id,
            phase == "restart_authorized", reason_code, reason, revision,
        )

    def _restore(self):
        document = self.archive.load()
        if document.get("contextFingerprint") not in (None, self.context_fingerprint):
            raise RetryWorkflowError("retry archive context changed")
        section = document.get("retryWorkflow")
        if section is None:
            return
        if section.get("runId") != self.run_id:
            raise RetryWorkflowError("retry archive belongs to a different run")
        self.restart_count = section.get("restartCount", 0)
        events = section.get("events", [])
        if (isinstance(self.restart_count, bool) or not isinstance(self.restart_count, int)
                or self.restart_count < 0 or not isinstance(events, list)):
            raise RetryWorkflowError("retry archive state is invalid")
        if events:
            last = events[-1]
            self.phase = last.get("phase", "awaiting_attempt_result")
            self.status = last.get("status", "pending")
            self.epoch_id = last.get("epochId", self.epoch_id)
            self.attempt_id = last.get("attemptId")
            self._last_reason = last.get("reasonCode", "ATTEMPT_RESULT_REQUIRED")
            self._last_message = last.get("reason")
