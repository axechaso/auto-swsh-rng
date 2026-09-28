"""Versioned scenario evidence manifests, safe imports and pending reports."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
from typing import Any
from pathlib import PurePosixPath, PureWindowsPath


MANIFEST_SCHEMA = "auto-swsh-evidence-manifest"
MANIFEST_VERSION = 1
FRAMEWORK_STATUSES = {"NotImplemented", "FrameworkReady"}
HARDWARE_STATUSES = {
    "PendingHardwareValidation",
    "HardwareValidated",
    "HardwareValidationFailed",
}
DEFAULT_REQUIRED_CASES = (
    "seed", "npc", "advance", "capture", "reverse", "feedback", "save",
)
EVIDENCE_KINDS = {
    "image", "video", "frame_replay", "action_log", "experiment_report",
}
SOURCE_KINDS = {"real", "synthetic", "replay", "report"}
ALLOWED_SUFFIXES = {
    ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".mp4", ".mkv", ".avi",
    ".mov", ".json", ".jsonl", ".txt", ".log",
}
MAX_IMPORT_BYTES = 2 * 1024 * 1024 * 1024
HASH_RE = re.compile(r"^[0-9a-f]{64}$")
SCENARIO_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$")


class EvidenceValidationError(ValueError):
    pass


def initialize_manifest(
    scenario_id: str,
    *,
    scenario_revision: int,
    algorithm_commit: str,
    script_revision: str = "pending",
    required_cases=DEFAULT_REQUIRED_CASES,
) -> dict[str, Any]:
    _validate_scenario_id(scenario_id)
    if isinstance(scenario_revision, bool) or not isinstance(scenario_revision, int) or scenario_revision < 1:
        raise EvidenceValidationError("scenarioRevision must be a positive integer")
    if not isinstance(algorithm_commit, str) or not re.fullmatch(r"[0-9a-fA-F]{40}", algorithm_commit):
        raise EvidenceValidationError("algorithmCommit must be a 40-character commit hash")
    cases = list(required_cases)
    if not cases or any(not _valid_case(case) for case in cases) or len(set(cases)) != len(cases):
        raise EvidenceValidationError("requiredCases must be a non-empty list of unique case IDs")
    if not isinstance(script_revision, str) or not script_revision:
        raise EvidenceValidationError("scriptRevision is required")
    return {
        "schema": MANIFEST_SCHEMA,
        "schemaVersion": MANIFEST_VERSION,
        "scenarioId": scenario_id,
        "scenarioRevision": scenario_revision,
        "frameworkStatus": "NotImplemented",
        "hardwareStatus": "PendingHardwareValidation",
        "algorithmCommit": algorithm_commit.lower(),
        "scriptRevision": script_revision,
        "deviceProfile": None,
        "requiredCases": cases,
        "evidence": [],
        "frameworkValidationCases": [],
        "reviewedAtUtc": None,
        "reviewedBy": None,
    }


class EvidenceStore:
    def __init__(self, scenario_directory):
        self.root = Path(scenario_directory).expanduser().resolve()
        self.manifest_path = self.root / "evidence-manifest.json"
        self.evidence_dir = self.root / "evidence"

    def initialize(self, scenario_id, **kwargs):
        if self.manifest_path.exists():
            raise FileExistsError(f"evidence manifest already exists: {self.manifest_path}")
        manifest = initialize_manifest(scenario_id, **kwargs)
        self.evidence_dir.mkdir(parents=True, exist_ok=True)
        self._write_manifest(manifest)
        return manifest

    def load(self, *, verify_files=False):
        try:
            manifest = json.loads(self.manifest_path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise EvidenceValidationError(f"cannot read evidence manifest: {exc}") from exc
        validate_manifest(manifest)
        if verify_files:
            for evidence in manifest["evidence"]:
                self._verify_artifact(evidence)
        return manifest

    def import_artifact(
        self,
        source_path,
        *,
        case_id: str,
        kind: str,
        source_kind: str = "replay",
        captured_at_utc: str | None = None,
        metadata: dict[str, Any] | None = None,
    ):
        source = Path(source_path).expanduser().resolve()
        if not source.is_file():
            raise EvidenceValidationError("source artifact must be a regular file")
        if source.stat().st_size > MAX_IMPORT_BYTES:
            raise EvidenceValidationError("artifact exceeds the 2 GiB import limit")
        suffix = source.suffix.lower()
        if suffix not in ALLOWED_SUFFIXES:
            raise EvidenceValidationError(f"unsupported evidence file type: {suffix or '(no extension)'}")
        if not _valid_case(case_id):
            raise EvidenceValidationError("caseId has an invalid format")
        if kind not in EVIDENCE_KINDS or source_kind not in SOURCE_KINDS:
            raise EvidenceValidationError("unsupported evidence kind or sourceKind")
        if captured_at_utc is not None:
            _validate_utc(captured_at_utc)
        safe_metadata = _json_object(metadata or {}, "metadata")

        manifest = self.load()
        if case_id not in manifest["requiredCases"]:
            raise EvidenceValidationError(f"caseId is not required by this scenario: {case_id}")
        actual_size = source.stat().st_size
        digest = _file_sha256(source)
        evidence_id = f"{case_id}-{digest[:20]}"
        existing = next((item for item in manifest["evidence"] if item["evidenceId"] == evidence_id), None)
        if existing is not None:
            self._verify_artifact(existing)
            return existing

        self.evidence_dir.mkdir(parents=True, exist_ok=True)
        filename = f"{evidence_id}{suffix}"
        destination = self.evidence_dir / filename
        relative_path = destination.relative_to(self.root).as_posix()
        temporary_path = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="wb", prefix=f".{evidence_id}.", suffix=".tmp", dir=self.evidence_dir,
                delete=False,
            ) as temporary:
                temporary_path = Path(temporary.name)
                with source.open("rb") as input_stream:
                    shutil.copyfileobj(input_stream, temporary, length=1024 * 1024)
                temporary.flush()
                os.fsync(temporary.fileno())
            if temporary_path.stat().st_size != actual_size or _file_sha256(temporary_path) != digest:
                raise EvidenceValidationError("artifact changed during import")
            os.replace(temporary_path, destination)
        finally:
            if temporary_path is not None and temporary_path.exists():
                temporary_path.unlink()

        entry = {
            "evidenceId": evidence_id,
            "caseId": case_id,
            "kind": kind,
            "sourceKind": source_kind,
            "path": relative_path,
            "sha256": digest,
            "sizeBytes": actual_size,
            "capturedAtUtc": captured_at_utc,
            "metadata": safe_metadata,
            "versionFingerprint": _version_fingerprint(manifest),
        }
        manifest["evidence"].append(entry)
        validate_manifest(manifest)
        self._write_manifest(manifest)
        return entry

    def report(self, *, verify_files=True):
        manifest = self.load()
        valid_entries = []
        invalid_entries = []
        for entry in manifest["evidence"]:
            try:
                if verify_files:
                    self._verify_artifact(entry)
                valid_entries.append(entry)
            except EvidenceValidationError as exc:
                invalid_entries.append({"evidenceId": entry["evidenceId"], "reason": str(exc)})
        present_any = {entry["caseId"] for entry in valid_entries}
        present_real = {entry["caseId"] for entry in valid_entries if entry["sourceKind"] == "real"}
        current_fingerprint = _version_fingerprint(manifest)
        current_entries = [
            entry for entry in valid_entries
            if _same_version_fingerprint(entry.get("versionFingerprint"), current_fingerprint)
        ]
        current_any = {entry["caseId"] for entry in current_entries}
        current_real = {entry["caseId"] for entry in current_entries if entry["sourceKind"] == "real"}
        missing_evidence = [case for case in manifest["requiredCases"] if case not in present_any]
        missing_real = [case for case in manifest["requiredCases"] if case not in present_real]
        missing_current = [case for case in manifest["requiredCases"] if case not in current_any]
        missing_current_real = [case for case in manifest["requiredCases"] if case not in current_real]
        stale_evidence = [
            entry["evidenceId"] for entry in valid_entries
            if not _same_version_fingerprint(entry.get("versionFingerprint"), current_fingerprint)
        ]
        return {
            "reportSchema": "auto-swsh-evidence-report",
            "reportVersion": 1,
            "scenarioId": manifest["scenarioId"],
            "scenarioRevision": manifest["scenarioRevision"],
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "frameworkStatus": manifest["frameworkStatus"],
            "hardwareStatus": manifest["hardwareStatus"],
            "evidenceStatus": "PendingEvidence" if missing_current or invalid_entries else "EvidenceImported",
            "canStartFormalAutomation": (
                can_start_formal(manifest)
                and not missing_current_real
                and not invalid_entries
            ),
            "requiredCases": list(manifest["requiredCases"]),
            "missingEvidenceCases": missing_evidence,
            "missingRealEvidenceCases": missing_real,
            "missingCurrentEvidenceCases": missing_current,
            "missingCurrentRealEvidenceCases": missing_current_real,
            "staleVersionEvidence": stale_evidence,
            "invalidEvidence": invalid_entries,
            "evidenceCount": len(valid_entries),
            "deviceProfilePresent": isinstance(manifest["deviceProfile"], dict),
            "reviewedAtUtc": manifest["reviewedAtUtc"],
            "reviewedBy": manifest["reviewedBy"],
        }

    def mark_framework_ready(self, *, validation_cases):
        manifest = self.load(verify_files=True)
        required = set(validation_cases)
        if not required or any(not _valid_case(case) for case in required):
            raise EvidenceValidationError("validation_cases must contain framework test IDs")
        manifest["frameworkStatus"] = "FrameworkReady"
        manifest["frameworkValidationCases"] = sorted(required)
        self._write_manifest(manifest)
        return manifest

    def update_versions(self, *, algorithm_commit, script_revision, device_profile=None):
        """Record current software/device fingerprints and invalidate old reviews."""
        if not isinstance(algorithm_commit, str) or not re.fullmatch(r"[0-9a-fA-F]{40}", algorithm_commit):
            raise EvidenceValidationError("algorithm_commit must be a 40-character commit hash")
        if not isinstance(script_revision, str) or not script_revision:
            raise EvidenceValidationError("script_revision is required")
        if device_profile is not None:
            device_profile = _json_object(device_profile, "deviceProfile")
            if not device_profile:
                raise EvidenceValidationError("deviceProfile cannot be empty")
        manifest = self.load()
        algorithm_changed = manifest["algorithmCommit"].lower() != algorithm_commit.lower()
        changed = (
            algorithm_changed
            or manifest["scriptRevision"] != script_revision
            or (device_profile is not None and manifest["deviceProfile"] != device_profile)
        )
        manifest["algorithmCommit"] = algorithm_commit.lower()
        manifest["scriptRevision"] = script_revision
        if device_profile is not None:
            manifest["deviceProfile"] = device_profile
        if changed:
            manifest["hardwareStatus"] = "PendingHardwareValidation"
            manifest["reviewedAtUtc"] = None
            manifest["reviewedBy"] = None
        if algorithm_changed:
            manifest["frameworkStatus"] = "NotImplemented"
            manifest["frameworkValidationCases"] = []
        self._write_manifest(manifest)
        return manifest

    def review_hardware(self, *, reviewer, device_profile, reviewed_at_utc=None):
        manifest = self.load(verify_files=True)
        report = self.report(verify_files=True)
        if report["missingCurrentRealEvidenceCases"]:
            raise EvidenceValidationError("hardware validation is missing real evidence for required cases")
        if not isinstance(device_profile, dict) or not device_profile:
            raise EvidenceValidationError("deviceProfile is required for hardware review")
        if not isinstance(reviewer, str) or not reviewer.strip():
            raise EvidenceValidationError("reviewer is required")
        reviewed_at_utc = reviewed_at_utc or datetime.now(timezone.utc).isoformat()
        _validate_utc(reviewed_at_utc)
        manifest["deviceProfile"] = _json_object(device_profile, "deviceProfile")
        manifest["hardwareStatus"] = "HardwareValidated"
        manifest["reviewedBy"] = reviewer.strip()
        manifest["reviewedAtUtc"] = reviewed_at_utc
        validate_manifest(manifest)
        self._write_manifest(manifest)
        return manifest

    def _verify_artifact(self, entry):
        path = _safe_relative_path(self.root, entry["path"])
        if not path.is_file():
            raise EvidenceValidationError(f"evidence file is missing: {entry['path']}")
        if path.stat().st_size != entry["sizeBytes"] or _file_sha256(path) != entry["sha256"]:
            raise EvidenceValidationError(f"evidence file hash or size changed: {entry['path']}")

    def _write_manifest(self, manifest):
        validate_manifest(manifest)
        self.root.mkdir(parents=True, exist_ok=True)
        _atomic_write_json(self.manifest_path, manifest)


def validate_manifest(manifest):
    if not isinstance(manifest, dict):
        raise EvidenceValidationError("manifest must be a JSON object")
    if manifest.get("schema") != MANIFEST_SCHEMA or manifest.get("schemaVersion") != MANIFEST_VERSION:
        raise EvidenceValidationError("unsupported evidence manifest schema")
    _validate_scenario_id(manifest.get("scenarioId"))
    for key in ("scenarioRevision",):
        value = manifest.get(key)
        if isinstance(value, bool) or not isinstance(value, int) or value < 1:
            raise EvidenceValidationError(f"{key} must be a positive integer")
    if manifest.get("frameworkStatus") not in FRAMEWORK_STATUSES:
        raise EvidenceValidationError("invalid frameworkStatus")
    if manifest.get("hardwareStatus") not in HARDWARE_STATUSES:
        raise EvidenceValidationError("invalid hardwareStatus")
    if not isinstance(manifest.get("algorithmCommit"), str) or not re.fullmatch(r"[0-9a-fA-F]{40}", manifest["algorithmCommit"]):
        raise EvidenceValidationError("algorithmCommit must be a 40-character commit hash")
    if not isinstance(manifest.get("scriptRevision"), str) or not manifest["scriptRevision"]:
        raise EvidenceValidationError("scriptRevision is required")
    if manifest.get("deviceProfile") is not None and not isinstance(manifest.get("deviceProfile"), dict):
        raise EvidenceValidationError("deviceProfile must be an object or null")
    framework_cases = manifest.get("frameworkValidationCases")
    if (
        not isinstance(framework_cases, list)
        or any(not _valid_case(case) for case in framework_cases)
        or len(set(framework_cases)) != len(framework_cases)
    ):
        raise EvidenceValidationError("frameworkValidationCases must contain unique validation IDs")
    if manifest["frameworkStatus"] == "FrameworkReady" and not framework_cases:
        raise EvidenceValidationError("FrameworkReady requires framework validation cases")
    required = manifest.get("requiredCases")
    evidence = manifest.get("evidence")
    if not isinstance(required, list) or not required or any(not _valid_case(case) for case in required) or len(set(required)) != len(required):
        raise EvidenceValidationError("requiredCases must be a non-empty list of unique case IDs")
    if not isinstance(evidence, list):
        raise EvidenceValidationError("evidence must be an array")
    seen_ids = set()
    for entry in evidence:
        if not isinstance(entry, dict):
            raise EvidenceValidationError("evidence entries must be objects")
        evidence_id = entry.get("evidenceId")
        if not isinstance(evidence_id, str) or not evidence_id or evidence_id in seen_ids:
            raise EvidenceValidationError("evidenceId must be non-empty and unique")
        seen_ids.add(evidence_id)
        if entry.get("caseId") not in required:
            raise EvidenceValidationError("evidence caseId must be listed in requiredCases")
        if entry.get("kind") not in EVIDENCE_KINDS or entry.get("sourceKind") not in SOURCE_KINDS:
            raise EvidenceValidationError("invalid evidence kind or sourceKind")
        _safe_relative_path(Path("/manifest-root"), entry.get("path"), check_exists=False)
        digest = entry.get("sha256")
        if not isinstance(digest, str) or not HASH_RE.fullmatch(digest):
            raise EvidenceValidationError("evidence sha256 must be a lowercase SHA-256 digest")
        size = entry.get("sizeBytes")
        if isinstance(size, bool) or not isinstance(size, int) or size < 0:
            raise EvidenceValidationError("evidence sizeBytes must be a non-negative integer")
        timestamp = entry.get("capturedAtUtc")
        if timestamp is not None:
            _validate_utc(timestamp)
        _json_object(entry.get("metadata", {}), "metadata")
        version_fingerprint = entry.get("versionFingerprint")
        if not isinstance(version_fingerprint, dict):
            raise EvidenceValidationError("evidence versionFingerprint is required")
        if not isinstance(version_fingerprint.get("algorithmCommit"), str) or not re.fullmatch(
            r"[0-9a-fA-F]{40}", version_fingerprint["algorithmCommit"]
        ):
            raise EvidenceValidationError("evidence algorithmCommit fingerprint is invalid")
        if not isinstance(version_fingerprint.get("scriptRevision"), str) or not version_fingerprint["scriptRevision"]:
            raise EvidenceValidationError("evidence scriptRevision fingerprint is required")
        profile_hash = version_fingerprint.get("deviceProfileSha256")
        if profile_hash is not None and (not isinstance(profile_hash, str) or not HASH_RE.fullmatch(profile_hash)):
            raise EvidenceValidationError("evidence deviceProfileSha256 fingerprint is invalid")
    if manifest.get("hardwareStatus") == "HardwareValidated":
        if not manifest.get("reviewedBy") or not manifest.get("reviewedAtUtc") or not manifest.get("deviceProfile"):
            raise EvidenceValidationError("HardwareValidated requires reviewer, review time and device profile")
        _validate_utc(manifest["reviewedAtUtc"])
        current_fingerprint = _version_fingerprint(manifest)
        real_cases = {
            entry["caseId"] for entry in evidence
            if entry["sourceKind"] == "real"
            and _same_version_fingerprint(entry.get("versionFingerprint"), current_fingerprint)
        }
        if any(case not in real_cases for case in required):
            raise EvidenceValidationError(
                "HardwareValidated requires current real evidence for every required case"
            )
    return True


def can_start_formal(manifest):
    validate_manifest(manifest)
    return (
        manifest["frameworkStatus"] == "FrameworkReady"
        and manifest["hardwareStatus"] == "HardwareValidated"
    )


def _safe_relative_path(root, value, *, check_exists=True):
    if not isinstance(value, str) or not value or "\\" in value:
        raise EvidenceValidationError("evidence path must be relative")
    posix_path = PurePosixPath(value)
    windows_path = PureWindowsPath(value)
    if posix_path.is_absolute() or windows_path.is_absolute() or windows_path.drive:
        raise EvidenceValidationError("evidence path must be relative")
    if ".." in posix_path.parts or not posix_path.parts:
        raise EvidenceValidationError("evidence path cannot escape its scenario directory")
    path = Path(*posix_path.parts)
    resolved = (root / path).resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError as exc:
        raise EvidenceValidationError("evidence path escapes its scenario directory") from exc
    if check_exists and not resolved.exists():
        raise EvidenceValidationError("evidence path does not exist")
    return resolved


def _validate_scenario_id(value):
    if not isinstance(value, str) or not SCENARIO_RE.fullmatch(value):
        raise EvidenceValidationError("scenarioId has an invalid or unsafe format")


def _valid_case(value):
    return isinstance(value, str) and bool(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", value))


def _validate_utc(value):
    if not isinstance(value, str):
        raise EvidenceValidationError("UTC timestamp must be a string")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise EvidenceValidationError("invalid ISO-8601 timestamp") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise EvidenceValidationError("timestamp must include a UTC offset")


def _json_object(value, name):
    if not isinstance(value, dict):
        raise EvidenceValidationError(f"{name} must be a JSON object")
    try:
        return json.loads(json.dumps(value, ensure_ascii=False))
    except (TypeError, ValueError) as exc:
        raise EvidenceValidationError(f"{name} must be JSON-compatible") from exc


def _version_fingerprint(manifest):
    profile = manifest.get("deviceProfile")
    profile_hash = None if profile is None else hashlib.sha256(
        json.dumps(profile, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return {
        "algorithmCommit": manifest["algorithmCommit"].lower(),
        "scriptRevision": manifest["scriptRevision"],
        "deviceProfileSha256": profile_hash,
    }


def _same_version_fingerprint(left, right):
    return isinstance(left, dict) and all(left.get(key) == right.get(key) for key in right)


def _file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _atomic_write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", prefix=f".{path.name}.",
            suffix=".tmp", dir=path.parent, delete=False,
        ) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None and temporary.exists():
            temporary.unlink()
