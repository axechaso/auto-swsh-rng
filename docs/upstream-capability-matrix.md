# 上游页面与能力矩阵

> 审计基线：owoow `a8514d654ae6c7c4ad87c3647c9911a05f6ef42a`；EasyCon `11c4b992b9bce0ff977e9c587a6c0bb0d302853e`。

`已接入`表示 App 中存在与固定上游版本一致的操作入口，并通过项目服务执行；`服务层`表示 Core/Upstream 能力可无界面调用，但 App 没有独立入口；`排除`表示入口与适配 API 均不纳入。当前没有以灰显、“未实现”或空白占位形式保留的上游入口。

## 顶层标签页

| 标签页 | 当前职责 | UI 状态 | 测试状态 |
|---|---|---|---|
| `owoow` | 野外遭遇、配置档、查询与 RNG 工具 | 已接入；复刻固定 owoow 主页面与工具窗口 | App 结构、默认值、请求映射、结果绑定和错误路径测试 |
| `伊机控` | 脚本、串口、采集、录制与手柄工具 | 已接入；复刻固定 EasyCon 的可用页面和菜单 | App 菜单、页面、行为、错误及排除入口测试 |
| `自动化流程` | 后续扩展预留 | 只有一条简洁预留说明 | App 控件数量及禁用文案测试 |

## owoow

| 上游页面/业务域 | App 处理 | 项目服务/契约 | 测试状态 |
|---|---|---|---|
| `owoow.WinForms/MainWindow`：Seed、连接、SAV Info | 已接入读取/写入 RNG 状态、训练家信息和连接错误提示 | `IOwoowConnectionService` | 控件默认值、连接成功/失败、状态同步 |
| `owoow.WinForms/MainWindow`：Static、Symbol、Hidden、Fishing | 已接入目录级联、四类搜索、独立筛选开关和原版结果列 | `IEncounterCatalogService`、`IOverworldEncounterService`、`EncounterFilter` | 页签/布局、筛选开关、雨天阶段、请求映射、结果绑定 |
| `owoow.WinForms/MainWindow`：图鉴推荐、野怪读取、时间推进 | 已接入推荐显示名/DevId 四槽映射、当前野怪读取、Days+/Days-/Adv./NTP | `DexRecommendationOption`、`IOwoowConnectionService`、`IXoroshiroService` | 四槽请求、服务调用、连接态控件、非法输入与真实错误 |
| `owoow.WinForms/MainWindow`：关菜单与雨天校准 | 已接入，结果回填到主搜索条件 | `ICalibrationService` | 菜单关闭/雨滴请求映射 |
| `owoow.WinForms/Subforms/Profiles` | 已接入增删、保存和选择配置档 | `IProfileStore`、`RngProfile` | 持久化及主页面回填 |
| `owoow.WinForms/Subforms/EncounterLookup` | 已接入游戏/宝可梦查询和结果表格 | `IEncounterCatalogService` | 选项加载、请求和结果绑定 |
| `owoow.WinForms/Subforms/SpreadFinder` | 已接入单种子与 Entire Space 搜索、IV/身高/稀有 EC 筛选 | `ISpreadFinderService` | 两种范围、筛选和结果绑定 |
| `owoow.WinForms/Subforms/Loto-ID`、`IDList` | 已接入 ID 列表增删/持久化及奖项/菜单关闭条件搜索；加载期间阻止空搜索，保存按快照串行 | `ILotoIdStore`、`ISpecialRngToolService` (`LotoId`) | 延迟加载、空值、快速重开串行保存、请求映射和结果绑定 |
| `owoow.WinForms/Subforms/Cram-o-matic` | 已接入四项输入、奖项与 Bonus 条件搜索 | `ISpecialRngToolService` (`CramOMatic`) | 请求映射和结果绑定 |
| `owoow.WinForms/Subforms/WattTrader` | 已接入槽位、天气和菜单关闭条件搜索 | `ISpecialRngToolService` (`WattTrader`) | 请求映射和结果绑定 |
| `owoow.WinForms/Subforms/DiggingPa` | 已接入最低 Watts、天气和菜单关闭条件搜索 | `ISpecialRngToolService` (`DiggingPa`) | 请求映射和结果绑定 |
| `owoow.WinForms/Subforms/SkillBro` | 已接入游戏、最低总数和 21 项奖励下限 | `ISpecialRngToolService` (`DiggingBro`) | 完整奖励字典映射和结果绑定 |
| `owoow.WinForms/Subforms/WailordRespawn` | 已接入成功条件和菜单关闭条件搜索 | `ISpecialRngToolService` (`WailordRespawn`) | 请求映射和结果绑定 |
| `owoow.WinForms/Subforms/XoroshiroTools` | 已接入推进、回退、NextInt 和初始状态计算 | `IXoroshiroService` | 操作映射、结果回填和窗口关闭取消 |
| `owoow.WinForms/Subforms/RetailSeedFinder` 与 `MainWindow` Retail 区 | 已接入精确/范围查找、动画序列生成和重新识别 | `IRetailSeedService` | 请求映射、Seed/动画回填 |
| `owoow.Core` 固定 Seed、环境校准和连接对象 | 保留为可复用服务层能力，无独立 App 入口 | `IXoroshiroService`、`ICalibrationService`、`IOwoowConnectionService` | Core/Upstream 无界面测试 |
| `owoow.Core/Discord` | 服务层；不复制 owoow 更新通知弹窗 | `INotificationService` | 通知适配测试 |
| `MainWindow` Turbo/Reset 与 `TurboSettings`、`SeedResetSettings`、`UpdateNotifPopup` | 排除；菜单和快捷入口不存在，主页面布局收紧 | 无 | App 入口缺失及布局测试 |

## EasyCon

| 上游页面/业务域 | App 处理 | 项目服务/契约 | 测试状态 |
|---|---|---|---|
| `EasyCon2/App/EasyConForm`、`Views/Editor`：File/Edit 与 Editor | 已接入新建、打开、保存、关闭、查找替换、注释、格式化和运行；按键脚本在伊机控页直接执行 | `EasyConScriptAdapter`、内部 `GamePadAdapter` 及 Core 输入服务 | 文件/编辑/按键执行/格式化成功与真实错误路径 |
| `EasyCon2/App/MainForm`：Log | 已接入启动、运行和错误日志以及清除操作 | `SequenceExecutionResult`、`OperationProgress` | 页面切换、日志内容和清除 |
| `EasyCon2/App/EasyConForm`、`MainForm`：Settings/Bluetooth/Device/Drawing/Help | 已接入现有配置、通知、蓝牙、解绑、绘图板、鼠标摇杆、更新、关于和源码入口 | Upstream 配置/对话框适配、`INotificationService` | 默认值、持久化、菜单和对话框行为 |
| `EasyCon2/App/EasyConForm`：设备面板 | 已接入端口刷新、自动/手动连接、状态和用户可读错误 | `IControllerDeviceService` | 成功/失败、状态、默认服务委托 |
| `EasyCon2/App/EasyConForm`：采集面板 | 已接入后端/视频源发现、连接/断开和搜图控制台 | `ICaptureDeviceService`、`IImageRecognitionService` | 发现、配置持久化、连接状态和控制台入口 |
| `EasyCon2/App/EasyConForm`：录制与 Controller | 已接入录制/暂停/继续/停止、虚拟手柄和按键映射；继续录制不清空暂停前动作 | `IControllerDeviceService`、`ControllerInputAction` | 连接前置条件、暂停续录、空录制回填、完整生命周期、映射 |
| `EasyCon.Core`：Switch 输入与等待 | 由脚本执行、虚拟手柄和 Core 服务共用 | `IControllerDeviceService`、`IInputSequenceService` | Core/Upstream 映射和序列测试 |
| `EasyCon.Capture/Capture`、`Search`、`OCRDetect` | 通过采集/搜图功能及服务层使用；OpenCV 类型不越过 Upstream | `ICaptureDeviceService`、`CapturedImage`、`IImageRecognitionService` | PNG 边界、采集和识别测试 |
| `EasyCon.Core/Assist`：远程运行/停止 | 排除；按钮、菜单、快捷入口和对话框入口不存在 | 无 | App 名称与文本缺失测试 |
| `EasyCon.Script/Assembly` 与 Burn 页 | 排除面向固件的编译/汇编、烧录、清除烧录、固件生成和烧录后自动运行；侧栏无空洞 | 无 | App 入口缺失和侧栏位置测试 |
| `EasyCon2/Forms/ESPConfig*` | 排除；设置项和对话框入口不存在 | 无 | App 名称与文本缺失测试 |
| UI Common/Avalonia Editor：脚本语法帮助、代码补全与折叠 | 排除；当前 TextBox 编辑器无法真实提供的入口不保留为空菜单项 | 无 | App 控件/菜单/文本缺失测试 |

上述排除项不在 App 中创建入口，`AutoSwshRng.Upstream` 也不公开对应适配 API；`third_party` 中的原始实现仅作为可核验的上游源码快照保留。删除 Burn 入口后仍保留 Editor、Log 和 Settings 页，侧栏按钮连续排列。

## 无界面证据

- Core、Upstream、CLI 服务测试不创建 `Form`、`UserControl` 或 Avalonia `Window`。
- `HeadlessCliCommand` 提供 `audit`、`catalog`、`rng`、`calibrate`、`tool`、`sequence-dry-run` 和 `spread` 入口。
- `ProjectReferenceBoundaryTests` 验证 Core 不引用上游/UI 程序集，App 不直接引用 `third_party`。
- 原算法对照测试覆盖四类野外遭遇、Spread Finder、六类特殊工具、Xoroshiro/Retail/校准、目录、采集发现和模板匹配。
- App 行为测试逐项验证保留控件存在、请求映射/结果绑定或用户可读错误，同时验证排除入口及其文本完全不存在。
