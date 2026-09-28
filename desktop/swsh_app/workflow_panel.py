"""Evidence and recording workspace for the staged automation workflow."""
from __future__ import annotations

import hashlib
import json
import os
import uuid
from datetime import datetime, timezone
from pathlib import Path

from PySide6.QtCore import Qt, QUrl, Signal
from PySide6.QtGui import QDesktopServices, QPixmap
from PySide6.QtWidgets import (
    QFileDialog, QHBoxLayout, QLabel, QTableWidget, QTableWidgetItem,
    QVBoxLayout, QWidget,
)

from .automation import EvidenceStore, EvidenceValidationError, load_replay_manifest
from .localization import BASELINE
from .tasks import Task
from .widgets import Card, button, combo, form, label, spin


FRAMEWORK_LABELS = {
    "NotImplemented": "框架未验收",
    "FrameworkReady": "框架已验收",
}
HARDWARE_LABELS = {
    "PendingHardwareValidation": "待实机验收",
    "HardwareValidated": "实机已验收",
    "HardwareValidationFailed": "实机验收失败",
}


def current_script_revision():
    package_root = Path(__file__).resolve().parent
    automation_root = package_root / "automation"
    sources = list(package_root.glob("*.py"))
    sources.extend(sorted(automation_root.rglob("*.py")))
    digest = hashlib.sha256()
    for source in sorted(set(path.resolve() for path in sources)):
        relative = source.relative_to(package_root).as_posix()
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        digest.update(source.read_bytes())
        digest.update(b"\0")
    return f"pyside6-{digest.hexdigest()}"


class WorkflowPanel(QWidget):
    """Review evidence, browse recordings, and expose the offline M2 runner."""

    simulation_requested = Signal()
    simulation_task_stopped = Signal()
    run_event = Signal(object)

    def __init__(self, log, parent=None):
        super().__init__(parent)
        self.log = log
        self.store: EvidenceStore | None = None
        self.report_data = None
        self.replay_source = None
        self.replay_manifest_path: Path | None = None
        self.replay_frames = []
        self.replay_frame_index = 0
        self.simulation_runner = None
        self.simulation_task = None
        self.simulation_result = None
        self.simulation_config = None
        self.simulation_script_revision = None
        self._build()
        self.run_event.connect(self._show_run_event)

    def _build(self):
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(14)

        card = Card(
            "离线模拟",
            "用合成动画驱动真实测种观察器和 M2 搜索 runner；仅运行一轮，不连接设备。"
            "预期验证位由同一配套后端生成，只检查流程接线。",
        )
        row = QHBoxLayout()
        self.simulation_start_button = button("模拟测种与目标搜索", self.simulation_requested.emit, True)
        self.simulation_stop_button = button("停止并释放模拟租约", self.stop_simulation)
        self.simulation_stop_button.setEnabled(False)
        row.addWidget(self.simulation_start_button)
        row.addWidget(self.simulation_stop_button)
        self.simulation_report_button = button("导出本轮模拟报告", self.choose_simulation_report_path)
        self.simulation_report_button.setEnabled(False)
        row.addWidget(self.simulation_report_button)
        row.addStretch()
        card.body.addLayout(row)
        self.simulation_fault = combo([
            ("不注入故障", "none"),
            ("预检失败", "preflight"),
            ("测种首帧丢失", "drop_first_frame"),
        ])
        form(card, [("故障注入（仅模拟）", self.simulation_fault)], 1)
        self.simulation_status = label(
            "尚未运行模拟。模拟结果仅验证测种 / 搜索框架，不代表画面识别或实机结果。",
            "muted", True,
        )
        card.body.addWidget(self.simulation_status)
        outer.addWidget(card)

        card = Card("场景验收", "读取场景证据清单，并核对框架、实机状态及文件完整性。")
        row = QHBoxLayout()
        self.scenario_directory = self._line_edit("场景目录，目录中应包含 evidence-manifest.json")
        row.addWidget(self.scenario_directory, 1)
        row.addWidget(button("选择目录", self.choose_scenario_directory))
        row.addWidget(button("读取清单", self.load_scenario))
        card.body.addLayout(row)
        self.scenario_directory.textChanged.connect(self._scenario_path_changed)

        self.scenario_id = self._line_edit("例如 route-1-clear-weather")
        self.scenario_revision = spin(1, 1_000_000, 1)
        form(card, [("新建清单的场景 ID", self.scenario_id),
                    ("场景修订号", self.scenario_revision)])
        row = QHBoxLayout()
        row.addWidget(button("创建待验收清单", self.initialize_scenario, True))
        row.addStretch()
        card.body.addLayout(row)

        self.scenario_status = label("尚未载入场景清单。", "notice", True)
        card.body.addWidget(self.scenario_status)
        status_row = QHBoxLayout()
        self.framework_status = label("框架：—", "muted", True)
        self.hardware_status = label("实机：—", "muted", True)
        self.formal_status = label("正式自动化：不可用", "muted", True)
        status_row.addWidget(self.framework_status, 1)
        status_row.addWidget(self.hardware_status, 1)
        status_row.addWidget(self.formal_status, 1)
        card.body.addLayout(status_row)
        self.formal_start_button = button("正式自动开始", self.formal_start_unavailable, True)
        self.formal_start_button.setEnabled(False)
        self.formal_start_button.setToolTip("M3–M6 自动阶段及实机适配器尚未接入；此桌面版本不开放正式自动化。")
        card.body.addWidget(self.formal_start_button)
        outer.addWidget(card)

        card = Card("证据浏览", "导入的文件会复制到场景目录并校验 SHA-256；导入本身不会改变验收状态。")
        self.evidence_case = combo([])
        self.evidence_kind = combo([
            ("图片", "image"), ("录像", "video"), ("帧回放清单", "frame_replay"),
            ("动作日志", "action_log"), ("实验报告", "experiment_report"),
        ])
        self.evidence_source = combo([
            ("真实采集", "real"), ("录像回放", "replay"),
            ("合成素材", "synthetic"), ("报告", "report"),
        ])
        form(card, [("验收案例", self.evidence_case), ("证据类型", self.evidence_kind),
                    ("素材来源", self.evidence_source)])
        row = QHBoxLayout()
        row.addWidget(button("导入证据文件", self.choose_evidence_file, True))
        row.addWidget(button("重新核验", self.refresh_report))
        row.addWidget(button("打开场景证据目录", self.open_evidence_directory))
        row.addStretch()
        card.body.addLayout(row)
        self.evidence_table = QTableWidget(0, 7)
        self.evidence_table.setHorizontalHeaderLabels([
            "文件检查", "案例", "类型", "来源", "大小", "SHA-256", "证据 ID",
        ])
        self.evidence_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.evidence_table.setSelectionMode(QTableWidget.SelectionMode.SingleSelection)
        self.evidence_table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.evidence_table.horizontalHeader().setStretchLastSection(True)
        self.evidence_table.setMinimumHeight(155)
        self.evidence_table.cellDoubleClicked.connect(self.open_selected_evidence)
        card.body.addWidget(self.evidence_table)
        row = QHBoxLayout()
        row.addWidget(button("打开选中证据", self.open_selected_evidence))
        row.addStretch()
        card.body.addLayout(row)
        outer.addWidget(card)

        card = Card("录像 / 图片回放", "校验回放清单和素材哈希后可逐帧查看；此处不会执行按键或推断游戏状态。")
        row = QHBoxLayout()
        self.replay_path = self._line_edit("选择 frame-replay manifest.json")
        row.addWidget(self.replay_path, 1)
        row.addWidget(button("选择并校验回放", self.choose_replay, True))
        card.body.addLayout(row)
        self.replay_status = label("尚未载入回放。", "muted", True)
        card.body.addWidget(self.replay_status)
        self.replay_image = QLabel("等待回放帧")
        self.replay_image.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.replay_image.setMinimumHeight(240)
        self.replay_image.setStyleSheet("background: #17213a; color: #c6d3ec; border-radius: 8px;")
        card.body.addWidget(self.replay_image)
        row = QHBoxLayout()
        self.previous_frame_button = button("上一帧", lambda: self.step_replay(-1))
        self.next_frame_button = button("下一帧", lambda: self.step_replay(1))
        self.previous_frame_button.setEnabled(False)
        self.next_frame_button.setEnabled(False)
        row.addWidget(self.previous_frame_button)
        row.addWidget(self.next_frame_button)
        self.replay_frame_label = label("—", "muted")
        row.addWidget(self.replay_frame_label, 1)
        row.addWidget(button("关闭回放", self.close_replay))
        card.body.addLayout(row)
        outer.addWidget(card)
        outer.addWidget(label(
            "本页可单轮模拟测种 / 搜索并浏览证据回放；M3–M6 阶段协调、游戏场景分类和设备动作尚未接入。"
            "模拟验证位由同一计算后端生成，不构成算法独立验证。正式开始入口保持禁用；模拟、回放或证据导入都不会生成实机验收通过结果。",
            "notice", True,
        ))
        outer.addStretch()

    @staticmethod
    def _line_edit(placeholder):
        from PySide6.QtWidgets import QLineEdit
        widget = QLineEdit()
        widget.setPlaceholderText(placeholder)
        return widget

    def choose_scenario_directory(self):
        directory = QFileDialog.getExistingDirectory(self, "选择场景证据目录")
        if directory:
            self.scenario_directory.setText(directory)
            if (Path(directory) / "evidence-manifest.json").is_file():
                self.load_scenario()

    def initialize_scenario(self):
        directory = Path(self.scenario_directory.text().strip()).expanduser()
        if not directory.is_dir():
            self._set_error("请先选择一个已存在的场景目录。")
            return
        try:
            store = EvidenceStore(directory)
            store.initialize(
                self.scenario_id.text().strip(),
                scenario_revision=self.scenario_revision.value(),
                algorithm_commit=BASELINE["algorithm"]["commit"],
                script_revision=current_script_revision(),
            )
            self.log(f"创建待验收场景清单：{store.manifest_path}")
            self._set_store(store)
        except (OSError, ValueError, KeyError) as exc:
            self._set_error(f"无法创建场景清单：{exc}")

    def load_scenario(self):
        directory = Path(self.scenario_directory.text().strip()).expanduser()
        if not directory.is_dir():
            self._set_error("请选择有效的场景目录。")
            return
        try:
            self._set_store(EvidenceStore(directory))
        except (OSError, ValueError) as exc:
            self._set_error(f"无法读取场景清单：{exc}")

    def _set_store(self, store):
        manifest = store.load()
        if (
            manifest["algorithmCommit"].lower() != BASELINE["algorithm"]["commit"].lower()
            or manifest["scriptRevision"] != current_script_revision()
        ):
            store.update_versions(
                algorithm_commit=BASELINE["algorithm"]["commit"],
                script_revision=current_script_revision(),
            )
            manifest = store.load()
        report = store.report(verify_files=True)
        self.store = store
        self.report_data = report
        self.scenario_directory.setText(str(store.root))
        self.scenario_id.setText(manifest["scenarioId"])
        self.scenario_revision.setValue(manifest["scenarioRevision"])
        self.evidence_case.clear()
        self.evidence_case.addItems(manifest["requiredCases"])
        framework = manifest["frameworkStatus"]
        hardware = manifest["hardwareStatus"]
        self.framework_status.setText(f"框架：{FRAMEWORK_LABELS[framework]} ({framework})")
        self.hardware_status.setText(f"实机：{HARDWARE_LABELS[hardware]} ({hardware})")
        missing = report["missingCurrentEvidenceCases"]
        missing_real = report["missingCurrentRealEvidenceCases"]
        invalid = report["invalidEvidence"]
        details = [
            f"场景 {manifest['scenarioId']} · 修订 {manifest['scenarioRevision']}",
            f"当前版本缺证据：{', '.join(missing) if missing else '无'}",
            f"缺少当前版本真实素材：{', '.join(missing_real) if missing_real else '无'}",
        ]
        if invalid:
            details.append(f"文件失效：{len(invalid)} 项")
        self.scenario_status.setText("\n".join(details))
        self.formal_status.setText("正式自动化：被门禁阻止" if not report["canStartFormalAutomation"]
                                   else "正式自动化：验收证据齐全，但桌面执行器未接入")
        self.formal_start_button.setEnabled(False)
        self._populate_evidence_table(
            manifest["evidence"], invalid, report["staleVersionEvidence"],
        )
        self.log(f"读取场景清单：{store.manifest_path}；正式入口仍由桌面执行器门禁关闭。")
        if self.replay_source is not None:
            self._update_replay_scenario_warning()

    def _populate_evidence_table(self, entries, invalid, stale):
        invalid_ids = {item["evidenceId"]: item["reason"] for item in invalid}
        stale_ids = set(stale)
        self.evidence_table.setRowCount(len(entries))
        for row, entry in enumerate(entries):
            reason = invalid_ids.get(entry["evidenceId"])
            check = f"失效：{reason}" if reason else "SHA-256 正常"
            if reason is None and entry["evidenceId"] in stale_ids:
                check += " · 软件版本过期"
            values = [
                check,
                entry["caseId"], entry["kind"], entry["sourceKind"],
                f"{entry['sizeBytes']:,} B", entry["sha256"], entry["evidenceId"],
            ]
            for column, value in enumerate(values):
                item = QTableWidgetItem(value)
                item.setData(Qt.ItemDataRole.UserRole, entry["evidenceId"])
                self.evidence_table.setItem(row, column, item)
        self._entries_by_id = {entry["evidenceId"]: entry for entry in entries}

    def choose_evidence_file(self):
        if self.store is None:
            self._set_error("先载入或创建场景清单，再导入证据。")
            return
        path, _ = QFileDialog.getOpenFileName(self, "选择证据文件")
        if path:
            self.import_evidence(path)

    def import_evidence(self, source_path, *, case_id=None, kind=None, source_kind=None):
        if self.store is not None and self._selected_directory() != self.store.root:
            self._set_error("场景目录已更改，请先读取该目录中的清单。")
            return None
        if self.store is None:
            self._set_error("先载入或创建场景清单，再导入证据。")
            return None
        case_id = case_id or self.evidence_case.currentText()
        kind = kind or self.evidence_kind.currentData()
        source_kind = source_kind or self.evidence_source.currentData()
        try:
            entry = self.store.import_artifact(
                source_path, case_id=case_id, kind=kind, source_kind=source_kind,
            )
            self._set_store(self.store)
            self.log(f"导入{source_kind}证据：{entry['evidenceId']}（{entry['sha256']}）。")
            return entry
        except (OSError, ValueError) as exc:
            self._set_error(f"证据导入失败：{exc}")
            return None

    def refresh_report(self):
        if self.store is None:
            self.load_scenario()
            return
        try:
            self._set_store(self.store)
        except (OSError, ValueError) as exc:
            self._set_error(f"证据核验失败：{exc}")

    def open_selected_evidence(self, *_):
        if self.store is None or not self.evidence_table.selectedItems():
            return
        evidence_id = self.evidence_table.selectedItems()[0].data(Qt.ItemDataRole.UserRole)
        entry = getattr(self, "_entries_by_id", {}).get(evidence_id)
        invalid_ids = {item["evidenceId"] for item in (self.report_data or {}).get("invalidEvidence", [])}
        if entry is None or evidence_id in invalid_ids:
            self._set_error("该证据文件缺失或哈希不匹配，不能从清单打开。")
            return
        path = (self.store.root / entry["path"]).resolve()
        if not path.is_relative_to(self.store.root) or not path.is_file():
            self._set_error("证据路径无效或文件已不存在。")
            return
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(path)))

    def open_evidence_directory(self):
        if self.store is None:
            self._set_error("先载入场景清单。")
            return
        self.store.evidence_dir.mkdir(parents=True, exist_ok=True)
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(self.store.evidence_dir)))

    def choose_replay(self):
        path, _ = QFileDialog.getOpenFileName(self, "选择帧回放清单", "", "JSON (*.json)")
        if path:
            self.load_replay(path)

    def load_replay(self, path):
        self.close_replay()
        try:
            source = load_replay_manifest(path)
            self.replay_source = source
            self.replay_manifest_path = Path(path).expanduser().resolve()
            self.replay_frames = [source.baseline]
            self.replay_frames.extend(frame for segment in source.segments for frame in segment.frames)
            self.replay_frame_index = 0
            self.replay_path.setText(str(self.replay_manifest_path))
            self._update_replay_scenario_warning()
            self.previous_frame_button.setEnabled(len(self.replay_frames) > 1)
            self.next_frame_button.setEnabled(len(self.replay_frames) > 1)
            self.show_replay_frame(0)
            self.log(f"已校验帧回放：{source.replay_id}；动作段 {len(source.segments)}，帧 {len(self.replay_frames)}。")
            return source
        except (OSError, ValueError) as exc:
            self.close_replay()
            self.replay_status.setText(f"回放校验失败：{exc}")
            self.log(f"回放校验失败：{exc}")
            return None

    def _update_replay_scenario_warning(self):
        source = self.replay_source
        if source is None:
            return
        summary = (f"回放 {source.replay_id} · 场景 {source.scenario_id} · 来源 {source.source_kind} · "
                   f"动作段 {len(source.segments)} · 共 {len(self.replay_frames)} 帧")
        if self.store is not None:
            scenario_id = self.store.load()["scenarioId"]
            if scenario_id != source.scenario_id:
                summary += f"\n警告：当前证据场景为 {scenario_id}，回放属于 {source.scenario_id}。"
        self.replay_status.setText(summary)

    def step_replay(self, amount):
        if not self.replay_frames:
            return
        target = min(max(0, self.replay_frame_index + amount), len(self.replay_frames) - 1)
        self.show_replay_frame(target)

    def show_replay_frame(self, index):
        if not self.replay_frames:
            return
        try:
            frame = self.replay_frames[index]
            image = frame.loader()
            if image is None or image.isNull():
                raise EvidenceValidationError("回放帧无法解码。")
            pixmap = QPixmap.fromImage(image)
            self.replay_image.setPixmap(pixmap.scaled(
                self.replay_image.size(), Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            ))
            self.replay_frame_index = index
            self.replay_frame_label.setText(
                f"帧 {frame.frame_id} / {len(self.replay_frames)} · "
                f"时间戳 {frame.source_timestamp_ns:,} ns · {frame.evidence_id}"
            )
        except (OSError, ValueError) as exc:
            self.replay_image.setText(f"帧读取失败：{exc}")

    def close_replay(self):
        if self.replay_source is not None:
            self.replay_source.close()
        self.replay_source = None
        self.replay_manifest_path = None
        self.replay_frames = []
        self.replay_frame_index = 0
        self.replay_image.clear()
        self.replay_image.setText("等待回放帧")
        self.replay_frame_label.setText("—")
        self.replay_status.setText("尚未载入回放。")
        self.previous_frame_button.setEnabled(False)
        self.next_frame_button.setEnabled(False)

    def formal_start_unavailable(self):
        self.log("正式自动开始被阻止：PySide6 桌面工作流执行器尚未集成。")

    def start_simulation(self, runner, run_callable=None):
        if self.simulation_task is not None and self.simulation_task.isRunning():
            return False
        self.simulation_result = None
        self.simulation_config = getattr(runner, "config", None)
        self.simulation_script_revision = current_script_revision()
        self.simulation_runner = runner
        runner.on_event = self.run_event.emit
        task = Task(run_callable or runner.run, self)
        task.result.connect(self._simulation_finished_with_result)
        task.failed.connect(self._simulation_failed)
        task.finished.connect(lambda active=task: self._simulation_thread_stopped(active))
        self.simulation_task = task
        self.simulation_start_button.setEnabled(False)
        self.simulation_stop_button.setEnabled(True)
        self.simulation_report_button.setEnabled(False)
        self.simulation_status.setText("正在测种并完整搜索当前范围……")
        self.log("开始离线模拟：合成画面、模拟设备端口，不会连接或控制实机。")
        task.start()
        return True

    def stop_simulation(self):
        if self.simulation_runner is None:
            return
        self.simulation_runner.cancel()
        self.simulation_stop_button.setEnabled(False)
        self.simulation_status.setText("已请求停止；正在等待计算终止并释放模拟租约……")
        self.log("请求停止离线模拟；等待 runner 完成清理和租约释放。")

    def _show_run_event(self, event):
        candidate_count = event.details.get("candidateCount")
        suffix = f" · 候选 {candidate_count}" if candidate_count is not None else ""
        self.simulation_status.setText(f"{event.message}{suffix}")

    def _simulation_finished_with_result(self, result):
        self.simulation_result = result
        self.simulation_report_button.setEnabled(True)
        if result.epochs:
            epoch = result.epochs[-1]
            detail = (
                f"种子 {epoch.seed0} / {epoch.seed1}\n"
                f"完整范围 [{epoch.searched_start}, {epoch.searched_end}] · "
                f"候选 {epoch.candidate_count} · 快照 {epoch.target_snapshot_id}"
            )
            if result.candidate_rows:
                first = result.candidate_rows[0]
                detail += f"\n首个候选：推进 {first.get('advance', '—')} · {first.get('species', '—')}"
        else:
            detail = "尚未形成完整种子搜索结果。"
        self.simulation_status.setText(
            f"模拟结束：{result.status} · {result.reason or '无错误'}\n{detail}\n"
            "此结果仅覆盖模拟测种和搜索，不代表 NPC、推进、捕获或实机验收。"
        )
        self.log(f"离线模拟结束：{result.status}；{result.reason or '已完成当前模拟阶段'}")

    def _simulation_failed(self, message):
        self.simulation_status.setText(f"模拟失败：{message}")
        self.log(f"离线模拟失败：{message}")

    def simulation_report_data(self):
        result = self.simulation_result
        config = self.simulation_config
        if result is None or config is None:
            return None
        candidate_limit = 1_000
        return {
            "schema": "auto-swsh-automation-run-report",
            "schemaVersion": 1,
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "runId": result.run_id,
            "scenarioId": config.scenario_id,
            "contextRevision": config.context_revision,
            "executionMode": "simulation",
            "outcomeType": "simulation_search_only",
            "sourceKind": "synthetic",
            "algorithmCommit": BASELINE["algorithm"]["commit"],
            "scriptRevision": self.simulation_script_revision or current_script_revision(),
            "frameworkStatus": config.framework_status,
            "hardwareStatus": config.hardware_status,
            "status": str(result.status),
            "phase": str(result.phase),
            "reason": result.reason,
            "searchRange": {
                "start": config.min_advance,
                "end": config.max_advance,
            },
            "searchFilters": config.search_request_copy(),
            "epochs": [{
                "epochId": epoch.epoch_id,
                "seed0": epoch.seed0,
                "seed1": epoch.seed1,
                "observationBits": epoch.observation_bits,
                "verificationBits": epoch.verification_bits,
                "searchedPositions": epoch.searched_positions,
                "searchedStart": epoch.searched_start,
                "searchedEnd": epoch.searched_end,
                "candidateCount": epoch.candidate_count,
                "candidateRowsTruncated": epoch.candidate_rows_truncated,
                "targetSnapshotId": epoch.target_snapshot_id,
                "targetRequestDigest": epoch.target_request_digest,
                "searchOrderVersion": epoch.search_order_version,
            } for epoch in result.epochs],
            "candidateRows": [dict(row) for row in result.candidate_rows[:candidate_limit]],
            "candidateRowsTruncated": len(result.candidate_rows) > candidate_limit,
            "targetSnapshotId": result.target_snapshot_id,
            "targetRequestDigest": result.target_request_digest,
            "searchOrderVersion": result.search_order_version,
            "totalEvents": result.total_events,
            "eventHistoryTruncated": result.event_history_truncated,
            "events": [{
                "runId": event.run_id,
                "epochId": event.epoch_id,
                "phase": event.phase.value,
                "message": event.message,
                "timestampMonotonicNs": event.timestamp_ns,
                "details": dict(event.details),
            } for event in result.events],
            "limitations": [
                "合成观测帧由测试分类器读取，不代表游戏画面识别。",
                "验证位由同一配套计算后端预测，不构成算法独立验证。",
                "本报告仅覆盖测种和 M2 搜索，不代表 NPC、捕获、保存或实机验收。",
            ],
        }

    def choose_simulation_report_path(self):
        report = self.simulation_report_data()
        if report is None:
            return
        default_name = f"{report['runId']}-simulation-report.json"
        path, _ = QFileDialog.getSaveFileName(self, "保存模拟运行报告", default_name, "JSON (*.json)")
        if path:
            self.save_simulation_report(path, report)

    def save_simulation_report(self, path, report=None):
        report = report or self.simulation_report_data()
        if report is None:
            self._set_error("当前没有可导出的模拟运行报告。")
            return False
        destination = Path(path).expanduser().resolve()
        temporary = destination.with_name(f".{destination.name}.{uuid.uuid4().hex}.tmp")
        try:
            destination.parent.mkdir(parents=True, exist_ok=True)
            temporary.write_text(
                json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )
            os.replace(temporary, destination)
            self.log(f"保存模拟报告：{destination}")
            return True
        except OSError as exc:
            self._set_error(f"模拟报告保存失败：{exc}")
            return False
        finally:
            if temporary.exists():
                temporary.unlink()

    def _simulation_thread_stopped(self, task):
        if self.simulation_task is task:
            self.simulation_task = None
            self.simulation_runner = None
        task.deleteLater()
        self.simulation_start_button.setEnabled(True)
        self.simulation_stop_button.setEnabled(False)
        self.simulation_task_stopped.emit()

    def _set_error(self, message):
        self.scenario_status.setText(message)
        self.log(f"工作流页面：{message}")

    def _selected_directory(self):
        try:
            return Path(self.scenario_directory.text().strip()).expanduser().resolve()
        except OSError:
            return None

    def _scenario_path_changed(self, _):
        if self.store is None or self._selected_directory() == self.store.root:
            return
        self.store = None
        self.report_data = None
        self.evidence_case.clear()
        self.evidence_table.setRowCount(0)
        self._entries_by_id = {}
        self.framework_status.setText("框架：—")
        self.hardware_status.setText("实机：—")
        self.formal_status.setText("正式自动化：不可用")
        self.scenario_status.setText("目录已更改；请读取已有清单或创建新清单。")

    def shutdown(self):
        if self.simulation_task is not None and self.simulation_task.isRunning():
            self.stop_simulation()
            self.close_replay()
            return False
        self.close_replay()
        return True
