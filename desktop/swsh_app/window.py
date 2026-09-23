from __future__ import annotations

from datetime import datetime
from pathlib import Path

from PySide6.QtCore import (
    QAbstractTableModel, QModelIndex, QProcess, QSignalBlocker, QSortFilterProxyModel,
    QTimer, Qt,
)
from PySide6.QtGui import QIcon
from PySide6.QtWidgets import (
    QAbstractItemView, QAbstractSpinBox, QApplication, QCheckBox, QComboBox, QFileDialog,
    QFrame, QGridLayout, QHBoxLayout, QHeaderView, QLineEdit, QMainWindow,
    QPlainTextEdit, QProgressBar, QScrollArea, QSplitter, QStackedWidget,
    QTableView, QVBoxLayout, QWidget,
)

from .backend import JsonJob, find_backend
from .capture import FrameStore
from .easycon_panel import EasyConPanel
from .ocr_panel import OcrPanel
from .storage import HEADERS, NATURES, WEATHER_ZH, SettingsStore, export_csv, result_values, seed_hex
from .widgets import STYLE, Card, button, combo, form, label, spin

PAGES = [
    ("search", "◆", "野生 / 静态", "按种子预测遭遇，筛选你的目标宝可梦。"),
    ("seed", "◎", "种子工具", "计算 Xoroshiro128+ 的前进与回退状态。"),
    ("monitor", "▣", "采集画面", "查看采集卡画面，准备实机操作。"),
    ("easycon", "◈", "伊机控", "共享串口连接，执行本地脚本和手动按键。"),
    ("ocr", "▧", "OCR 识别", "从同一视频源取帧，框选文字区域并检查识别结果。"),
    ("profiles", "◇", "存档管理", "保存训练家身份和护符状态，供所有搜索共用。"),
    ("logs", "≡", "运行日志", "查看本次会话的搜索、设备和错误记录。"),
    ("settings", "⚙", "共通设置", "管理计算服务和设备选择。"),
]


class ResultsModel(QAbstractTableModel):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.rows = []
        self.values = []

    def replace(self, rows):
        self.beginResetModel()
        self.rows = rows
        self.values = [result_values(row) for row in rows]
        self.endResetModel()

    def rowCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(self.rows)

    def columnCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(HEADERS)

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):
        if not index.isValid():
            return None
        if role in (Qt.ItemDataRole.DisplayRole, Qt.ItemDataRole.UserRole):
            return self.values[index.row()][index.column()]
        if role == Qt.ItemDataRole.TextAlignmentRole:
            return Qt.AlignmentFlag.AlignCenter

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):
        if role == Qt.ItemDataRole.DisplayRole and orientation == Qt.Orientation.Horizontal:
            return HEADERS[section]


class SwshWindow(QMainWindow):
    def __init__(self, data_dir: Path, backend: Path | None = None, *, auto_load=True):
        super().__init__()
        self.setWindowTitle("剑 / 盾乱数工具 · PySide6 初版")
        self.setWindowIcon(QIcon(str(Path(__file__).parent / "assets/icon.svg")))
        self.resize(1440, 900)
        self.setMinimumSize(1080, 720)
        self.setStyleSheet(STYLE)
        self.backend = backend
        self.jobs = set()
        self.catalog_job = None
        self.active_job = None
        self.closing = False
        self.initializing = True
        self.camera = None
        self.capture_session = None
        self.video_inputs = []
        self.frames = FrameStore()
        self.store = SettingsStore(data_dir)
        self.storage_error = ""
        try:
            self.saved = self.store.load()
        except (ValueError, OSError) as exc:
            self.saved = {"version": 1, "profiles": {}}
            self.storage_error = str(exc)
        self.backend = backend or find_backend(self.saved.get("backend"))
        self.catalog_ready = False
        self.nav = {}
        self.indices = {}
        self.build()
        self.ocr_panel.worker.closed.connect(self.ocr_closed)
        self.restore_profile_list()
        active = self.saved.get("activeProfile")
        if active in self.saved["profiles"]:
            self.profile_list.setCurrentText(active)
            self.load_profile()
        self.select_page("search")
        self.update_profile_chip()
        self.backend_path.setText(str(self.backend or ""))
        self.backend_status.setText("●  计算服务待检测" if self.backend else "○  计算服务未配置")
        self.log("剑 / 盾乱数工具 v0.1.0 已启动。")
        if self.storage_error:
            self.fail(f"配置读取失败：{self.storage_error}")
            self.save_profile_button.setEnabled(False)
            self.save_settings_button.setEnabled(False)
        self.initializing = False
        if auto_load:
            QTimer.singleShot(0, self.refresh_catalog)

    def build(self):
        root = QWidget()
        root.setObjectName("root")
        self.setCentralWidget(root)
        outer = QHBoxLayout(root)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)
        side = QFrame()
        side.setObjectName("sidebar")
        side.setFixedWidth(196)
        sl = QVBoxLayout(side)
        sl.setContentsMargins(16, 30, 16, 22)
        sl.setSpacing(8)
        sl.addWidget(label("SWSH RNG", "brand"))
        sl.addWidget(label("宝可梦  剑 / 盾", "sideMuted"))
        sl.addSpacing(32)
        sl.addWidget(label("工作区", "sideMuted"))
        for key, symbol, title, _ in PAGES:
            b = button(f"{symbol}    {title}", lambda checked=False, k=key: self.select_page(k))
            b.setObjectName("nav")
            b.setCheckable(True)
            self.nav[key] = b
            sl.addWidget(b)
        sl.addStretch()
        self.backend_status = label("○  计算服务未配置", "sideMuted", True)
        sl.addWidget(self.backend_status)
        sl.addSpacing(7)
        sl.addWidget(label("PySide6 · v0.1.0", "sideMuted"))
        outer.addWidget(side)
        workspace = QWidget()
        wl = QVBoxLayout(workspace)
        wl.setContentsMargins(24, 24, 24, 15)
        wl.setSpacing(15)
        header = QHBoxLayout()
        titles = QVBoxLayout()
        self.title = label("", "title")
        self.description = label("", "muted", True)
        titles.addWidget(self.title)
        titles.addWidget(self.description)
        header.addLayout(titles, 1)
        self.profile_chip = button("存档 · 手动输入", lambda: self.select_page("profiles"))
        header.addWidget(self.profile_chip)
        header.addWidget(button("设备设置", lambda: self.select_page("settings")))
        wl.addLayout(header)
        self.error_label = label("", "error", True)
        self.error_label.hide()
        wl.addWidget(self.error_label)
        self.pages = QStackedWidget()
        wl.addWidget(self.pages, 1)
        # Shared profile widgets must exist before the search page binds to them.
        profile_page = self.build_profiles()
        self.easycon_panel = EasyConPanel(self.frames, self.saved, self.log, self.fail, self)
        self.ocr_panel = OcrPanel(self.frames, self.saved, self.persist, self.log, self.fail, self)
        page_widgets = {"profiles": profile_page, "search": self.build_search(), "seed": self.build_seed(),
                        "monitor": self.build_monitor(), "logs": self.build_logs(), "settings": self.build_settings(),
                        "easycon": self.easycon_panel, "ocr": self.scroll(self.ocr_panel)}
        for key, *_ in PAGES:
            self.indices[key] = self.pages.addWidget(page_widgets[key])
        self.progress = QProgressBar()
        self.progress.setRange(0, 1000)
        self.progress.setValue(0)
        self.progress.setTextVisible(False)
        wl.addWidget(self.progress)
        self.status = label("填写当前种子并选择遭遇条件。", "muted")
        wl.addWidget(self.status)
        outer.addWidget(workspace, 1)

    @staticmethod
    def canvas():
        widget = QWidget()
        widget.setObjectName("canvas")
        layout = QVBoxLayout(widget)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(14)
        return widget, layout

    @staticmethod
    def scroll(widget):
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(widget)
        return scroll

    def build_search(self):
        page, layout = self.canvas()
        layout.addWidget(label("01  设置身份与种子     →     02  筛选遭遇     →     03  查看目标推进数", "notice", True))
        split = QSplitter(Qt.Orientation.Horizontal)
        left, ll = self.canvas()
        card = Card("遭遇条件", "使用当前游戏版本对应的遭遇表。")
        self.kind = combo([("明雷", "Symbol"), ("固定 / 静态", "Static"), ("暗雷", "Hidden"), ("垂钓", "Fishing")])
        self.area = combo([])
        self.weather = combo([])
        self.species = combo([])
        self.kind.currentIndexChanged.connect(self.refresh_catalog)
        self.area.currentIndexChanged.connect(self.refresh_catalog)
        self.weather.currentIndexChanged.connect(self.refresh_catalog)
        self.species.currentIndexChanged.connect(self.invalidate)
        form(card, [("遭遇类型", self.kind), ("天气", self.weather), ("区域", self.area), ("目标宝可梦", self.species)])
        ll.addWidget(card)
        card = Card("种子与搜索范围", "输入 16 位十六进制状态；推进范围包含起止两端。")
        self.seed0, self.seed1 = QLineEdit(), QLineEdit()
        self.seed0.setPlaceholderText("Seed 0 · 例如 123456789ABCDEF0")
        self.seed1.setPlaceholderText("Seed 1 · 例如 0FEDCBA987654321")
        self.start, self.end = spin(), spin(1000)
        form(card, [("Seed 0", self.seed0), ("Seed 1", self.seed1)], 1)
        form(card, [("起始推进数", self.start), ("结束推进数", self.end)])
        card.body.addWidget(button("载入示例种子", self.load_example))
        ll.addWidget(card)
        card = Card("目标筛选")
        self.shiny = combo([("不限", "Any"), ("任意闪光", "Either"), ("星闪", "Star"), ("方闪", "Square"), ("非闪光", "None")])
        self.nature = combo([("不限", None), *NATURES])
        self.gender = combo([("不限", None), ("雄性", "Male"), ("雌性", "Female"), ("无性别", "Genderless")])
        self.mark = combo([("不限", "Ignore"), ("任意证章", "Any"), ("无证章", "None"), ("性格证章", "Personality"), ("性格或稀有证章", "PersonalityOrRare")])
        form(card, [("闪光", self.shiny), ("性格", self.nature), ("性别", self.gender), ("证章", self.mark)])
        grid = QGridLayout()
        grid.setHorizontalSpacing(6)
        grid.setContentsMargins(0, 0, 0, 0)
        self.ivs = []
        for i, name in enumerate(["HP", "攻击", "防御", "特攻", "特防", "速度"]):
            low, high = spin(0, 31), spin(31, 31)
            for field in (low, high):
                field.setProperty("ivRange", True)
                field.setButtonSymbols(QAbstractSpinBox.ButtonSymbols.NoButtons)
            self.ivs.append((low, high))
            grid.addWidget(label(name, "muted"), 0, i)
            grid.addWidget(low, 1, i)
            grid.addWidget(high, 2, i)
            low.setAccessibleName(f"{name}最小个体值")
            high.setAccessibleName(f"{name}最大个体值")
            low.setToolTip("最小个体值")
            high.setToolTip("最大个体值")
            low.valueChanged.connect(self.invalidate)
            high.valueChanged.connect(self.invalidate)
        card.body.addWidget(label("个体值范围 · 上行为最小值，下行为最大值", "muted"))
        card.body.addLayout(grid)
        presets = QHBoxLayout()
        for text, values in [("不限", None), ("6V", [31] * 6), ("5V · 0 攻", [31, 0, 31, 31, 31, 31]), ("5V · 0 速", [31, 31, 31, 31, 31, 0])]:
            presets.addWidget(button(text, lambda checked=False, v=values: self.set_ivs(v)))
        card.body.addLayout(presets)
        self.knockouts, self.hidden_step = spin(0, 999999), spin(0, 1000)
        form(card, [("已击败数量（气场计算）", self.knockouts), ("暗雷最大步数", self.hidden_step)])
        card.body.addWidget(label("初版按原地遭遇计算；首发特性、菜单 / 飞行 / 天气额外推进和图鉴推荐尚未接入。", "muted", True))
        ll.addWidget(card)
        ll.addStretch()
        left_scroll = self.scroll(left)
        left_scroll.setMinimumWidth(450)
        split.addWidget(left_scroll)

        right, rl = self.canvas()
        summary = Card("搜索概览")
        metrics = QHBoxLayout()
        self.result_count, self.first_advance = label("—", "metric"), label("—", "metric")
        for title, value in [("匹配结果", self.result_count), ("首个目标推进", self.first_advance)]:
            box = QVBoxLayout()
            box.addWidget(label(title, "muted"))
            box.addWidget(value)
            metrics.addLayout(box)
        summary.body.addLayout(metrics)
        self.result_context = label("尚未搜索。选择条件后开始查找。", "muted", True)
        summary.body.addWidget(self.result_context)
        rl.addWidget(summary)
        results = Card("遭遇结果", "选中一行查看 Seed、PID 和完整个体值。表格支持排序。")
        self.model = ResultsModel(self)
        self.proxy = QSortFilterProxyModel(self)
        self.proxy.setSourceModel(self.model)
        self.proxy.setSortRole(Qt.ItemDataRole.UserRole)
        self.table = QTableView()
        self.table.setModel(self.proxy)
        self.table.setAlternatingRowColors(True)
        self.table.setSortingEnabled(True)
        self.table.sortByColumn(0, Qt.SortOrder.AscendingOrder)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table.verticalHeader().hide()
        self.table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.table.horizontalHeader().setDefaultSectionSize(85)
        self.table.setColumnWidth(1, 150)
        for col in range(7, 13):
            self.table.setColumnWidth(col, 54)
        self.table.setMinimumHeight(100)
        self.table.selectionModel().selectionChanged.connect(self.selection_changed)
        results.body.addWidget(self.table, 1)
        self.detail = label("尚未选择目标。", "muted", True)
        self.detail.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        results.body.addWidget(self.detail)
        row = QHBoxLayout()
        self.export_button = button("导出 CSV", self.export_results)
        self.copy_button = button("复制目标", self.copy_target)
        self.export_button.setEnabled(False)
        self.copy_button.setEnabled(False)
        row.addWidget(self.export_button)
        row.addWidget(self.copy_button)
        row.addStretch()
        results.body.addLayout(row)
        rl.addWidget(results, 1)
        actions = QHBoxLayout()
        self.search_button = button("开始搜索", self.search, True)
        self.search_button.setEnabled(False)
        self.stop_button = button("停止", self.cancel_search)
        self.stop_button.setEnabled(False)
        actions.addWidget(self.search_button, 1)
        actions.addWidget(self.stop_button)
        right_scroll = self.scroll(right)
        right_shell, right_layout = self.canvas()
        right_shell.setMinimumWidth(340)
        right_layout.addWidget(right_scroll, 1)
        right_layout.addLayout(actions)
        split.addWidget(right_shell)
        split.setSizes([470, 700])
        split.setChildrenCollapsible(False)
        layout.addWidget(split, 1)
        for w in (self.seed0, self.seed1):
            w.textChanged.connect(self.invalidate)
        for w in (self.start, self.end, self.knockouts, self.hidden_step):
            w.valueChanged.connect(self.invalidate)
        for w in (self.shiny, self.nature, self.gender, self.mark):
            w.currentIndexChanged.connect(self.invalidate)
        return page

    def build_profiles(self):
        page, layout = self.canvas()
        card = Card("训练家存档", "这里只保存工具参数，不读写游戏存档。TID / SID 为内部 16 位 ID，并非游戏内显示的六位 ID。")
        self.profile_list = combo([])
        self.profile_name = QLineEdit()
        self.profile_name.setPlaceholderText("例如：剑 · 主存档")
        self.game = combo([("宝可梦 剑", "Sword"), ("宝可梦 盾", "Shield")])
        self.tid, self.sid = spin(0, 65535), spin(0, 65535)
        self.shiny_charm, self.mark_charm = QCheckBox("闪耀护符"), QCheckBox("证章护符")
        form(card, [("已有存档", self.profile_list), ("存档名称", self.profile_name),
                    ("游戏版本", self.game), ("TID（0～65535）", self.tid), ("SID（0～65535）", self.sid)])
        row = QHBoxLayout()
        row.addWidget(self.shiny_charm)
        row.addWidget(self.mark_charm)
        row.addStretch()
        card.body.addLayout(row)
        row = QHBoxLayout()
        self.save_profile_button = button("保存存档", self.save_profile, True)
        row.addWidget(self.save_profile_button)
        row.addWidget(button("载入所选存档", self.load_profile))
        row.addStretch()
        card.body.addLayout(row)
        self.profile_feedback = label("尚未保存存档；当前字段会直接用于搜索。", "muted", True)
        card.body.addWidget(self.profile_feedback)
        layout.addWidget(card)
        layout.addStretch()
        self.game.currentIndexChanged.connect(self.profile_changed)
        self.tid.valueChanged.connect(self.profile_changed)
        self.sid.valueChanged.connect(self.profile_changed)
        self.shiny_charm.toggled.connect(self.profile_changed)
        self.mark_charm.toggled.connect(self.profile_changed)
        return self.scroll(page)

    def build_seed(self):
        page, layout = self.canvas()
        card = Card("Xoroshiro128+ 状态推进", "与遭遇搜索共用同一计算核心。")
        self.tool_seed0, self.tool_seed1 = QLineEdit(), QLineEdit()
        self.amount = spin(1, 1_000_000, 1)
        self.direction = combo([("向前推进", False), ("向后回退", True)])
        form(card, [("Seed 0", self.tool_seed0), ("Seed 1", self.tool_seed1), ("推进数", self.amount), ("方向", self.direction)])
        row = QHBoxLayout()
        self.rng_button = button("计算状态", self.calculate_rng, True)
        row.addWidget(self.rng_button)
        row.addWidget(button("从搜索页读取", self.read_search_seeds))
        row.addStretch()
        card.body.addLayout(row)
        self.rng_output = label("计算结果将在这里显示。", "notice", True)
        self.rng_output.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        card.body.addWidget(self.rng_output)
        self.apply_seed_button = button("应用结果到遭遇搜索", self.apply_rng)
        self.apply_seed_button.setEnabled(False)
        card.body.addWidget(self.apply_seed_button)
        layout.addWidget(card)
        layout.addWidget(label("动画反查种子、重新识别与实机自动推进将后续接入。", "muted", True))
        layout.addStretch()
        return self.scroll(page)

    def build_monitor(self):
        page, layout = self.canvas()
        card = Card("采集卡监控", "选择视频设备后开启预览。关闭页面不会停止预览，请使用停止按钮释放设备。")
        self.camera_list = combo([])
        self.camera_list.addItem("尚未检测视频设备", None)
        row = QHBoxLayout()
        row.addWidget(self.camera_list, 1)
        self.detect_video_button = button("检测设备", self.detect_devices)
        row.addWidget(self.detect_video_button)
        self.camera_button = button("开启预览", self.toggle_camera, True)
        self.camera_button.setEnabled(False)
        row.addWidget(self.camera_button)
        card.body.addLayout(row)
        self.video_container = QStackedWidget()
        placeholder = label("等待采集画面\n\n连接采集卡后，点击检测设备。", "muted", True)
        placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.video_container.addWidget(placeholder)
        self.video_container.setMinimumHeight(330)
        card.body.addWidget(self.video_container, 1)
        layout.addWidget(card, 1)
        layout.addWidget(label("视频预览、OCR 与伊机控搜图共用原始画面。剑盾动画识别与全自动命中流程尚未接入。", "notice", True))
        return page

    def build_logs(self):
        page, layout = self.canvas()
        card = Card("本次运行日志", "记录实际操作；最多保留最近 4,000 行。")
        self.log_view = QPlainTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.document().setMaximumBlockCount(4000)
        card.body.addWidget(self.log_view, 1)
        row = QHBoxLayout()
        row.addWidget(button("保存日志", self.export_log))
        row.addWidget(button("清空显示", self.log_view.clear))
        row.addStretch()
        card.body.addLayout(row)
        layout.addWidget(card, 1)
        return page

    def build_settings(self):
        page, layout = self.canvas()
        card = Card("计算服务", "选择已构建的 AutoSwshRng.Cli.exe。首次使用可运行 desktop/启动剑盾乱数.ps1 完成构建。")
        self.backend_path = QLineEdit()
        self.backend_path.setReadOnly(True)
        form(card, [("乱数服务路径", self.backend_path)], 1)
        card.body.addWidget(button("选择计算服务", self.choose_backend))
        layout.addWidget(card)
        card = Card("设备准备", "伊机控页面管理共享串口会话；采集画面页面管理共享视频源。")
        self.port_list = self.easycon_panel.ports
        card.body.addWidget(button("打开伊机控连接", lambda: self.select_page("easycon")))
        self.device_feedback = label("设备尚未检测。", "muted", True)
        card.body.addWidget(self.device_feedback)
        card.body.addWidget(button("重新检测设备", self.detect_devices))
        layout.addWidget(card)
        self.save_settings_button = button("保存共通设置", self.save_settings, True)
        layout.addWidget(self.save_settings_button)
        layout.addWidget(label(f"本机配置：{self.store.path}", "muted", True))
        layout.addStretch()
        return self.scroll(page)

    def select_page(self, key):
        self.pages.setCurrentIndex(self.indices[key])
        for k, b in self.nav.items():
            b.setChecked(k == key)
        _, _, title, description = next(p for p in PAGES if p[0] == key)
        self.title.setText(title)
        self.description.setText(description)

    def log(self, text):
        self.log_view.appendPlainText(f"[{datetime.now():%H:%M:%S}] {text}")

    def fail(self, message):
        self.error_label.setText(message)
        self.error_label.show()
        self.status.setText("操作未完成，请查看上方提示。")
        self.log(f"错误：{message}")

    def clear_error(self):
        self.error_label.hide()

    def launch(self, request, result, *, catalog=False):
        if not self.backend:
            self.fail("尚未找到乱数服务。请运行启动脚本构建，或在共通设置中选择 AutoSwshRng.Cli.exe。")
            return None
        job = JsonJob(self.backend, request, self)
        self.jobs.add(job)
        job.result.connect(result)
        job.failed.connect(self.fail)
        job.done.connect(lambda: self.job_done(job))
        if not catalog:
            self.active_job = job
            self.search_button.setEnabled(False)
            self.rng_button.setEnabled(False)
            self.stop_button.setEnabled(True)
            self.progress.setValue(0)
            job.progress.connect(lambda n, total: self.progress.setValue(int(n * 1000 / max(total, 1))))
        job.start()
        return job

    def job_done(self, job):
        self.jobs.discard(job)
        if self.active_job is job:
            self.active_job = None
            self.rng_button.setEnabled(True)
            self.stop_button.setEnabled(False)
        if self.catalog_job is job:
            self.catalog_job = None
        self.search_button.setEnabled(self.catalog_ready and self.active_job is None)
        job.deleteLater()
        if self.closing and not self.jobs:
            QTimer.singleShot(0, self.close)

    @staticmethod
    def fill(widget, values, selected=None):
        with QSignalBlocker(widget):
            widget.clear()
            for value in values:
                title, data = value if isinstance(value, tuple) else (value, value)
                widget.addItem(title, data)
            index = widget.findData(selected)
            widget.setCurrentIndex(index if index >= 0 else (0 if widget.count() else -1))

    def refresh_catalog(self, *_):
        if self.initializing or not hasattr(self, "species") or not hasattr(self, "status"):
            return
        self.invalidate()
        self.catalog_ready = False
        self.search_button.setEnabled(False)
        if self.catalog_job:
            self.catalog_job.cancel()
        request = {"operation": "catalog", "game": self.game.currentData(), "kind": self.kind.currentData(),
                   "area": self.area.currentData(), "weather": self.weather.currentData()}
        self.catalog_job = self.launch(request, self.catalog_loaded, catalog=True)

    def catalog_loaded(self, data):
        species = self.species.currentData()
        self.fill(self.area, data["areas"], data["area"])
        self.fill(self.weather, [(WEATHER_ZH.get(w, w), w) for w in data["weathers"]], data["weather"])
        options = [(s, s) for s in data["species"]]
        if self.kind.currentData() != "Static":
            options.insert(0, ("不限宝可梦", None))
        self.fill(self.species, options, species)
        self.catalog_ready = bool(data["areas"] and data["weathers"] and data["species"])
        self.search_button.setEnabled(self.catalog_ready and self.active_job is None)
        self.hidden_step.setEnabled(self.kind.currentData() == "Hidden")
        self.knockouts.setEnabled(self.kind.currentData() in ("Symbol", "Fishing"))
        self.backend_status.setText("●  计算服务就绪")
        self.status.setText("遭遇表已载入。" if self.catalog_ready else "当前条件没有可用的遭遇表。")

    def invalidate(self, *_):
        if hasattr(self, "model") and self.model.rows:
            self.result_context.setText("条件已修改，下方保留上次搜索结果；请重新搜索。")

    def load_example(self):
        self.seed0.setText("123456789ABCDEF0")
        self.seed1.setText("0FEDCBA987654321")
        self.status.setText("已载入示例种子。实机搜索前请替换为当前游戏状态。")
        self.log("载入示例种子，仅用于体验计算与界面。")

    def set_ivs(self, values):
        for i, (low, high) in enumerate(self.ivs):
            low.setValue(0 if values is None else values[i])
            high.setValue(31 if values is None else values[i])

    def profile_data(self):
        return {"game": self.game.currentData(), "tid": self.tid.value(), "sid": self.sid.value(),
                "shinyCharm": self.shiny_charm.isChecked(), "markCharm": self.mark_charm.isChecked()}

    def update_profile_chip(self):
        if hasattr(self, "profile_chip"):
            game = "剑" if self.game.currentData() == "Sword" else "盾"
            self.profile_chip.setText(f"{game} · TID {self.tid.value()} / SID {self.sid.value()}")

    def profile_changed(self, *_):
        self.update_profile_chip()
        if hasattr(self, "status"):
            self.invalidate()
            if self.sender() is self.game:
                self.refresh_catalog()

    def search_request(self):
        s0, s1 = seed_hex(self.seed0.text()), seed_hex(self.seed1.text())
        if int(s0, 16) == int(s1, 16) == 0:
            raise ValueError("两个 Seed 不能同时为零。")
        start, end = self.start.value(), self.end.value()
        if end < start or end - start >= 100_000:
            raise ValueError("结束推进数不能小于起始值；初版单次最多搜索 100,000 帧。")
        ivs = [[low.value(), high.value()] for low, high in self.ivs]
        if any(low > high for low, high in ivs):
            raise ValueError("个体值最小值不能大于最大值。")
        return {"operation": "search", **self.profile_data(), "kind": self.kind.currentData(),
                "area": self.area.currentData(), "weather": self.weather.currentData(), "species": self.species.currentData(),
                "seed0": s0, "seed1": s1, "start": start, "end": end, "ivs": ivs,
                "shiny": self.shiny.currentData(), "nature": self.nature.currentData(), "gender": self.gender.currentData(),
                "mark": self.mark.currentData(), "knockouts": self.knockouts.value(),
                "hiddenStep": self.hidden_step.value() if self.kind.currentData() == "Hidden" else 0}

    def search(self):
        if self.active_job or not self.catalog_ready:
            return
        self.clear_error()
        try:
            request = self.search_request()
        except ValueError as exc:
            self.fail(str(exc))
            return
        self.model.replace([])
        self.result_count.setText("…")
        self.first_advance.setText("—")
        self.export_button.setEnabled(False)
        self.detail.setText("正在搜索。")
        context = f"{self.game.currentText()} · {request['area']} · {self.weather.currentText()} · {request['start']:,}～{request['end']:,}"
        self.result_context.setText(context)
        self.status.setText("正在搜索，可随时停止。")
        self.log(f"开始搜索：{context}；TID={request['tid']} SID={request['sid']}")
        self.launch(request, lambda data: self.search_finished(data, request, context))

    def search_finished(self, data, request, context):
        self.model.replace(data["rows"])
        self.result_count.setText(f"{data['total']:,}")
        self.first_advance.setText(f"{data['rows'][0]['advance']:,}" if data["rows"] else "—")
        self.detail.setText("选择一行查看目标详情。" if data["rows"] else "没有匹配结果，可放宽筛选条件或更换搜索范围。")
        self.export_button.setEnabled(bool(data["rows"]))
        self.progress.setValue(1000)
        note = "；显示及导出前 10,000 条，请收窄条件" if data["truncated"] else ""
        self.result_context.setText(context + note)
        try:
            if request != self.search_request():
                self.result_context.setText(context + note + "；条件已修改，这是上次请求的结果。")
        except ValueError:
            self.result_context.setText(context + note + "；当前条件已修改。")
        self.status.setText(f"搜索完成，找到 {data['total']:,} 个结果{note}。")
        self.log(self.status.text())

    def cancel_search(self):
        if self.active_job:
            self.active_job.cancel()
            self.stop_button.setEnabled(False)
            self.status.setText("正在停止计算…")
            self.active_job.done.connect(lambda: self.status.setText("计算已停止，可调整条件重新开始。"))
            self.result_count.setText("—")
            self.log("用户停止计算。")

    def selected_row(self):
        index = self.table.currentIndex()
        if not index.isValid():
            return None
        return self.model.rows[self.proxy.mapToSource(index).row()]

    def selection_changed(self, *_):
        row = self.selected_row()
        self.copy_button.setEnabled(row is not None)
        if row is not None:
            self.detail.setText(f"推进 {row['advance']:,} · {row['species']} · IV {' / '.join(map(str, row['ivs']))}\n"
                                f"EC {row['ec']}   PID {row['pid']}\nSeed 0  {row['seed0']}\nSeed 1  {row['seed1']}")

    def copy_target(self):
        row = self.selected_row()
        if row:
            QApplication.clipboard().setText("\n".join(f"{key}: {value}" for key, value in zip(HEADERS, result_values(row))))
            self.status.setText("目标详情已复制。")

    def export_results(self):
        path, _ = QFileDialog.getSaveFileName(self, "导出当前结果", "剑盾遭遇结果.csv", "CSV (*.csv)")
        if path:
            try:
                export_csv(Path(path), self.model.rows)
                self.log(f"导出 {len(self.model.rows):,} 条结果：{path}")
                self.status.setText("CSV 已导出。")
            except OSError as exc:
                self.fail(f"导出失败：{exc}")

    def read_search_seeds(self):
        self.tool_seed0.setText(self.seed0.text())
        self.tool_seed1.setText(self.seed1.text())

    def calculate_rng(self):
        if self.active_job:
            return
        self.clear_error()
        self.apply_seed_button.setEnabled(False)
        try:
            s0, s1 = seed_hex(self.tool_seed0.text()), seed_hex(self.tool_seed1.text())
        except ValueError as exc:
            self.fail(str(exc))
            return
        self.rng_output.setText("正在计算…")
        self.launch({"operation": "rng", "seed0": s0, "seed1": s1, "amount": self.amount.value(),
                     "reverse": self.direction.currentData()}, self.rng_finished)

    def rng_finished(self, data):
        self.rng_result = data
        self.rng_output.setText(f"Seed 0   {data['seed0']}\nSeed 1   {data['seed1']}\n推进距离   {data['distance']:,}")
        self.apply_seed_button.setEnabled(True)
        self.progress.setValue(1000)
        self.status.setText("种子状态计算完成。")
        self.log(f"种子计算完成：{data['seed0']} / {data['seed1']}")

    def apply_rng(self):
        self.seed0.setText(self.rng_result["seed0"])
        self.seed1.setText(self.rng_result["seed1"])
        self.start.setValue(0)
        self.select_page("search")
        self.status.setText("已应用新种子，起始推进数重置为 0。")

    def restore_profile_list(self):
        self.fill(self.profile_list, list(self.saved["profiles"]))

    def persist(self):
        if self.storage_error:
            self.fail("原配置读取失败，禁止覆盖。请先修复配置文件。")
            return False
        try:
            self.store.save(self.saved)
            return True
        except (OSError, TypeError) as exc:
            self.fail(f"配置保存失败：{exc}")
            return False

    def save_profile(self):
        name = self.profile_name.text().strip()
        if not name:
            self.fail("请先填写存档名称。")
            return
        self.saved["profiles"][name] = self.profile_data()
        self.saved["activeProfile"] = name
        if self.persist():
            self.restore_profile_list()
            self.profile_list.setCurrentText(name)
            self.profile_feedback.setText(f"已保存存档：{name}")
            self.clear_error()
            self.log(f"保存存档：{name}")

    def load_profile(self):
        name = self.profile_list.currentText()
        profile = self.saved["profiles"].get(name)
        if not profile:
            return
        with QSignalBlocker(self.game):
            self.game.setCurrentIndex(self.game.findData(profile["game"]))
        self.tid.setValue(profile["tid"])
        self.sid.setValue(profile["sid"])
        self.shiny_charm.setChecked(profile["shinyCharm"])
        self.mark_charm.setChecked(profile["markCharm"])
        self.profile_name.setText(name)
        self.profile_feedback.setText(f"已载入存档：{name}")
        self.update_profile_chip()
        self.refresh_catalog()

    def choose_backend(self):
        path, _ = QFileDialog.getOpenFileName(self, "选择计算服务", "", "乱数服务 (AutoSwshRng.Cli.exe)")
        if path:
            self.backend = Path(path)
            self.backend_path.setText(path)
            self.clear_error()
            self.refresh_catalog()

    def save_settings(self):
        self.saved["backend"] = str(self.backend or "")
        self.saved["port"] = self.port_list.currentData() or ""
        self.saved["labelRoot"] = self.easycon_panel.label_root
        device = self.camera_list.currentData()
        if isinstance(device, int) and 0 <= device < len(self.video_inputs):
            self.saved["camera"] = bytes(self.video_inputs[device].id()).hex()
        if self.persist():
            self.status.setText("共通设置已保存。")
            self.log("保存计算服务和设备选择。")

    def detect_devices(self):
        if self.camera:
            self.fail("请先停止预览，再重新检测设备。")
            return
        try:
            from PySide6.QtSerialPort import QSerialPortInfo
            from PySide6.QtMultimedia import QMediaDevices
            ports = QSerialPortInfo.availablePorts()
            previous = self.port_list.currentData() or self.saved.get("port")
            self.fill(self.port_list, [(f"{p.portName()} · {p.description()}", p.portName()) for p in ports], previous)
            self.video_inputs = QMediaDevices.videoInputs()
            preferred = next((i for i, d in enumerate(self.video_inputs) if bytes(d.id()).hex() == self.saved.get("camera")), None)
            self.fill(self.camera_list, [(d.description(), i) for i, d in enumerate(self.video_inputs)], preferred)
            self.camera_button.setEnabled(bool(self.video_inputs))
            self.easycon_panel.refresh_state()
            message = f"检测到 {len(ports)} 个串口、{len(self.video_inputs)} 个视频设备。"
            self.device_feedback.setText(message)
            self.status.setText(message)
            self.log(message)
        except Exception as exc:
            self.fail(f"设备检测失败：{exc}")

    def toggle_camera(self):
        if self.camera:
            self.stop_camera()
            return
        index = self.camera_list.currentData()
        if index is None:
            return
        try:
            from PySide6.QtMultimedia import QCamera, QMediaCaptureSession
            from PySide6.QtMultimediaWidgets import QVideoWidget
            if not hasattr(self, "video"):
                self.video = QVideoWidget()
                self.video.videoSink().videoFrameChanged.connect(self.frames.receive)
                self.video_container.addWidget(self.video)
            self.capture_session = QMediaCaptureSession(self)
            self.camera = QCamera(self.video_inputs[index], self)
            self.camera.errorOccurred.connect(self.camera_error)
            self.capture_session.setCamera(self.camera)
            self.capture_session.setVideoOutput(self.video)
            self.video_container.setCurrentIndex(1)
            self.camera_button.setText("停止预览")
            self.camera_list.setEnabled(False)
            self.camera.start()
            self.log(f"开启视频预览：{self.camera_list.currentText()}")
        except Exception as exc:
            self.stop_camera()
            self.fail(f"无法开启视频预览：{exc}")

    def camera_error(self, error, message):
        self.fail(f"采集设备错误：{message}")
        QTimer.singleShot(0, self.stop_camera)

    def stop_camera(self):
        self.frames.clear()
        if self.camera:
            self.camera.stop()
            self.capture_session.setCamera(None)
            self.capture_session.setVideoOutput(None)
            self.camera.deleteLater()
            self.capture_session.deleteLater()
            self.camera = self.capture_session = None
            self.log("视频预览已停止，设备已释放。")
        self.video_container.setCurrentIndex(0)
        self.camera_list.setEnabled(True)
        self.camera_button.setText("开启预览")

    def export_log(self):
        path, _ = QFileDialog.getSaveFileName(self, "保存日志", "剑盾乱数日志.txt", "文本 (*.txt)")
        if path:
            try:
                Path(path).write_text(self.log_view.toPlainText(), encoding="utf-8")
                self.status.setText("日志已保存。")
            except OSError as exc:
                self.fail(f"日志保存失败：{exc}")

    def closeEvent(self, event):
        self.closing = True
        self.stop_camera()
        controller_ready = self.easycon_panel.shutdown()
        ocr_ready = self.ocr_panel.worker.process.state() == QProcess.ProcessState.NotRunning
        if not ocr_ready:
            self.ocr_panel.worker.stop()
        if self.jobs:
            for job in list(self.jobs):
                job.cancel()
        if self.jobs or not controller_ready or not ocr_ready:
            event.ignore()
            return
        self.ocr_panel.workspace.cleanup()
        event.accept()

    def ocr_closed(self):
        if self.closing:
            QTimer.singleShot(0, self.close)
