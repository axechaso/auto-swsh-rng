# 上游 UI 精简复刻设计

## 背景与基线

主程序必须稳定为 `owoow`、`伊机控`、`自动化流程` 三个顶层标签页。视觉、布局、控件名称、默认值和交互路径只以固定子模块为准：owoow `a8514d654ae6c7c4ad87c3647c9911a05f6ef42a`，EasyCon `11c4b992b9bce0ff977e9c587a6c0bb0d302853e`。

当前 owoow 页是没有导航和服务调用的静态近似布局；EasyCon 已具备较完整的兼容 UI 和行为测试；自动化流程已经是一个占位标签，但测试还没有阻止历史五步向导文案回归。

## 方案比较

### 方案 A：直接引用并嵌入上游 WinForms 窗体

视觉最接近，但上游窗体直接依赖自己的配置、连接包装器、PKHeX 和 UI 类型。裁掉未实现入口会形成长期分叉，也会破坏 App 不直接引用 `third_party`、Core 不暴露上游类型的边界。因此不采用。

### 方案 B：服务适配的原版控件复刻（采用）

在 App 中按固定 owoow Designer 重建主工作区、顶部菜单和工具对话框，控件保持上游名称、文本、顺序和默认值；事件只调用项目现有 Core 契约与 Upstream 适配器。EasyCon 保留已验证的兼容壳，只补精确裁剪和布局回归。该方案能同时满足原版操作习惯、服务边界和入口删除要求。

### 方案 C：仅补当前静态骨架的少数按钮

改动最小，但保留了不存在于上游的中文竖向导航，也无法覆盖 Profiles、Encounter Lookup、特殊工具和 Xoroshiro 等已有服务能力，不满足逐页复刻。因此不采用。

## 架构

`OwoowTabControl` 负责上游 `MainWindow` 的集成版布局、状态和主搜索路径。一个项目自有的 `OwoowUiServices` 依赖集合只包含 Core 接口；默认值由 `AutoSwshRng.Upstream` 具体服务构成，测试可注入替身。`OwoowToolWindowFactory` 负责构建上游菜单打开的工具窗体，并复用统一的 Seed、范围、菜单关闭和结果表辅助控件。

```text
MainForm
  -> OwoowTabControl
       -> OwoowUiServices (Core interfaces only)
            -> AutoSwshRng.Upstream adapters
       -> OwoowToolWindowFactory
  -> EasyConTabControl (existing upstream compatibility shell)
  -> AutomationFlowTabControl (single placeholder label)
```

App 不直接引用 `third_party`。Core 接口继续不包含 WinForms、owoow、EasyCon、PKHeX、OpenCV 或 Avalonia 类型。工具窗体是 App 自有 UI 类型，不进入 Core。

## owoow 主页面

- 用上游水平 `MS_SubWindows` 菜单替换当前中文竖向假菜单，保持十个菜单项的英文原文、顺序和入口。
- 保留 Seed、Connection、SAV Info、四类 encounter tab、Pokédex Recommendation、Advanced Settings、Filters、Retail Tools、结果表和右键操作。
- Connect/Disconnect、读取/写入 Seed、训练家信息、Dex 推荐、野怪/世界读取、目录联动、搜索、校准、Retail、日期推进和 NTP 均调用现有服务。
- `Turbo`、`Turbo Controls`、`Reset for Seed` 和对应 `Settings` 没有项目服务契约，完全删除；CFW 区域缩短，不保留灰色按钮或空行。
- 连接相关按钮只因真实状态暂时不可用；这类状态控制不是“未实现”占位。错误显示操作名称和真实异常消息，不显示堆栈。

## owoow 工具窗体

保留并接通 Profiles、Encounter Lookup、Spread Finder、Loto-ID、Cram-o-matic、Watt Trader、Digging Pa、Digging Bro (Skill)、Wailord Respawn、Xoroshiro Tools。窗体使用固定 Designer 的标题、字段、默认 Seed、范围和筛选项。

特殊工具共享 `ISpecialRngToolService`，但分别构造对应 `SpecialToolSearchRequest`，结果表显示该工具的真实字段。Profiles 使用 `IProfileStore`；Encounter Lookup 使用 `IEncounterCatalogService`；Spread Finder 使用 `ISpreadFinderService` 和 `IXoroshiroService`；Xoroshiro Tools 使用 `IXoroshiroService`。

未纳入的更新通知、Turbo 配置和 Seed Reset 自动操作没有菜单或对话框入口。

## EasyCon

保留现有可用的文件、编辑、搜图、设置、蓝牙、设备、画图和帮助操作，以及 Log、Editor、Settings 三页、右侧脚本/串口/视频/录制/手柄区域和状态栏。既有服务行为和测试继续作为回归基线。

Burn 页被删除后，侧栏按钮保持 10/50/90 的连续位置。远程运行、远程停止、烧录、清除烧录、固件生成、烧录后自动运行、固件/烧录帮助、脚本语法和 ESP32 配置的控件、菜单项、文本和对话框入口全部不存在。

## 自动化流程

第三个顶层标签页只有一个居中的 `automationFlowPlaceholder` 标签，说明未来再设计。该页不包含步骤导航、执行计划、dry-run、实机、设备状态、编排、预览或执行按钮。现有 CLI 的无界面 `sequence-dry-run` 是独立能力，不在本次 UI 删除范围内。

## 状态、错误与生命周期

- 长搜索和批量日期操作使用 `CancellationTokenSource`；再次操作或释放控件时安全取消。
- UI 事件捕获参数错误、上游错误和设备错误，恢复按钮状态，并显示可读标题与原始消息。
- 主窗体关闭时继续走 EasyCon 的保存确认；owoow 连接与取消源在控件释放时清理，不在关闭阶段静默写设备。
- 工具窗体由统一显示委托打开，生产环境保持上游的 modal/modeless习惯，测试捕获窗体而不阻塞。

## 测试策略

每批生产改动前先新增失败测试并验证失败原因：

1. 主壳精确三标签、自动化页只有占位且不含禁用文案。
2. owoow 顶部菜单、主布局、原版名称/默认值和 unsupported 入口缺失。
3. 连接、目录、搜索、结果绑定、错误反馈与连接后按钮状态。
4. 每个工具菜单打开正确窗体；Profiles、Lookup、Spread、特殊工具和 Xoroshiro 至少各有真实服务调用测试。
5. EasyCon 排除名称、排除文本和侧栏无空洞。
6. Core 边界测试继续证明 Core 无 UI/上游引用，App 无直接 third-party 引用。

最终运行 App、Core、Upstream、CLI 全部测试，构建解决方案，启动 WinForms 做视觉核对，并执行 `git diff --check`。

## 完成标准

- 一个应用内可以进入并使用服务已支持的 owoow 与伊机控功能。
- owoow 导航、主页面和工具窗体贴近固定上游版本，不再是静态骨架。
- 所有无服务且不需要的入口被删除，剩余页面自然收紧。
- 自动化流程只是预留说明。
- 文档、能力矩阵、测试和实现一致，相关测试全部通过。
