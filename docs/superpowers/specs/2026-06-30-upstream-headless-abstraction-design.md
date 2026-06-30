# 上游功能去界面化抽象设计

## 目标与范围

本设计在不继续 UI 复刻和自动化流程编排的前提下，把 owoow 与 EasyCon 中 auto-swsh-rng 所需的上游能力收敛为项目自有、无界面、可测试的服务边界。任何 Core 消费者都不得看到 owoow、EasyCon、PKHeX、SysBot、OpenCV、WinForms 或 Avalonia 类型。

owoow 纳入配置档、遭遇数据、四类野外生成、完整筛选、Seed/RNG、校准、Finder/Lookup、六类特殊工具、Wailord、Xoroshiro、RAM 读取与有独立业务价值的连接能力。EasyCon 纳入串口、Switch 输入、可取消动作、运行反馈、采集截图、识别、必要映射/录制/通知；排除远程运行/停止、烧录、固件、脚本帮助、ESP32 和纯 UI 行为。

## 方案选择

采用按业务域划分的强类型服务：

- 优点：调用者能从接口直接理解请求、结果、错误和取消语义；各域能独立对照原版测试；不会形成上游 `GeneratorConfig` 或 Frame 的影子泄漏。
- 代价：Core 类型数量较多，但每个文件职责单一，变更范围可控。

未采用的方案：

1. 单一 `Execute(command)`/字典式网关：文件少，但把类型安全、校验和稳定性推给调用者，不满足“清晰稳定的服务接口”。
2. 全量移植 owoow/EasyCon：能完全控制取消，但重复算法、数据与 bug，难持续证明与原版一致。仅在上游无法独立调用时移植最小编排代码。

## 分层与依赖

```text
AutoSwshRng.Core
  ├─ Profiles / Encounters / Rng / Calibration / Tools / Connection
  └─ Automation / Capture / Recognition / Notifications
              ↑ 项目自有接口与模型
AutoSwshRng.Upstream
  ├─ owoow.Core + PKHeX/SysBot 映射
  └─ EasyCon.Device + EasyCon.Script + EasyCon.Capture 映射
              ↑ 仅此层可见上游类型
AutoSwshRng.Cli / AutoSwshRng.App
  └─ 只依赖 Core 接口和 Upstream 具体服务
```

App 删除对 `EasyCon2.csproj` 的直接引用。既有 EasyCon UI 外壳若需要输入事件，只调用 Core 的 `IControllerInputService`；本任务不再扩展其布局或工作流。

## Core 业务域

### 通用契约

`UpstreamOperationException` 携带稳定的 `UpstreamErrorCode`（Validation、NotFound、NotConnected、ConnectionFailed、Timeout、Cancelled、InvalidData、Unsupported、UpstreamFailure）。参数问题在请求构造时抛 `ArgumentException`；运行时上游异常统一转换。长任务接受 `CancellationToken` 和可选 `IProgress<OperationProgress>`。

### 配置与遭遇目录

- `RngProfile`、`RngApplicationSettings`、`IProfileStore`：游戏版本、TID/SID、两种护符和配置档增删改读。
- `IEncounterCatalogService`：区域、天气、宝可梦、遭遇表、特性、个人数据、图鉴推荐选项与 Encounter Lookup。
- 结果使用 `EncounterSlot`、`PokemonPersonalData`、`EncounterLookupResult`，不泄漏 owoow 接口。

### 野外搜索与筛选

- `IOverworldEncounterService.SearchAsync(OverworldSearchRequest, progress, token)`。
- 请求包含 RNG state、inclusive advance range、遭遇上下文、训练家配置、环境校准和 `EncounterFilter`。
- 筛选包含 species、IV Range/Or、shiny、mark、aura、height、nature、ability、gender、Rare EC。上游未将 ability/gender 作为 UI 筛选项时，由 Upstream 在原版生成结果转换后做项目侧后置筛选；所有 RNG 值仍来自原版。
- 结果 `OverworldEncounterResult` 完整承载 advance/jump、seed、slot/step、species、shiny/aura、level、ability/nature/gender、IV、mark、EC/PID、height、item/egg move。

### RNG、Retail、校准与连接

- `IXoroshiroService`：前进、后退、NextInt、查找初始 state、距离、固定 seed 的 EC/PID/IV/height。
- `IRetailSeedService`：128 动画 seed、范围 seed finder、动画序列与 re-identification。
- `ICalibrationService`：NPC、关菜单、保持方向、天气、雨滴、地图 memory roll、飞翔/区域加载组合推进。
- `IOwoowConnectionService`：连接/断开/状态、读取和写入 RNG state、读取训练家配置、图鉴推荐、野生宝可梦、KCoordinates/地图对象、系统时间与日期推进。服务实现可连接 SysBot，但接口只使用项目模型。
- 连接监控由显式 `ReadRngStateAsync` 和可取消 `WatchRngStateAsync` 提供，不保留 Form 中的忙循环。

### Finder、Lookup 与特殊工具

- 保留并强化 `ISpreadFinderService`。
- Retail Seed Finder 归入 `IRetailSeedService`，Encounter Lookup 归入目录服务。
- `ISpecialRngToolService` 使用 `SpecialToolKind` 和强类型请求字段覆盖 Loto-ID、Cram-o-matic、Watt Trader、Digging Pa、Digging Bro、Wailord Respawn；每种结果转换为项目自有 `SpecialToolResult`，奖励明细为项目枚举字典。
- 各生成器按小块调用原版实现，在块间检查取消并汇报进度。

## EasyCon 业务域

### 设备与输入

- `IControllerDeviceService`：发现串口、连接、断开、连接快照和状态事件。
- `IControllerInputService`：按键 down/up/click、D-pad、左右摇杆、reset；按钮、摇杆均为 Core 枚举/值对象。
- `IInputSequenceService`：执行 `ButtonAction`、`StickAction`、`WaitAction`、`ResetAction`；串行、可取消、每步状态/日志/错误明确，取消或失败时保证 reset。
- 录制只保留 Start/Pause/Stop/GetResult；结果转换为项目自有动作列表和兼容脚本文本。

### 采集、截图与识别

- `ICaptureDeviceService`：发现采集源/后端、连接、断开、按字节获取 PNG/JPEG frame。
- `IImageRecognitionService`：模板、边缘、OCR 搜索；输入是字节数组、项目矩形和项目算法枚举，结果含位置、匹配度、识别文字。
- 标签文件通过项目自有 `RecognitionTemplate` 加载；Upstream 内部映射 EasyCon `ImgLabel`/OpenCV Mat，并统一释放 native 资源。

### 映射与通知

- `IControllerMappingService` 仅返回/验证自动化输入所需的默认映射。
- `IAutomationNotificationService` 只保留可选 HTTP 通知的请求、取消和逐提供方结果；不包含 UI 表单。

## 数据流与异常

1. 调用者构造 Core 请求，请求构造器完成范围、数量、枚举组合和空值校验。
2. Upstream 映射为原版配置/枚举，调用原算法或设备。
3. 长范围被切成有界块，块间检查 token；设备等待和脚本 delay 直接传 token。
4. 原版 Frame、PK8、Mat、NintendoSwitch 等在适配器内立即转换并释放。
5. 已知输入/连接/超时/数据/上游异常映射为稳定错误码；`OperationCanceledException` 原样保留。

## 测试策略

- Core：每个请求模型的有效/无效边界、不可变拷贝、错误类型和状态机。
- Upstream 一致性：固定输入同时调用原版和适配器，逐字段比较目录、遭遇、校准、RNG、Spread、Retail 和六类特殊工具结果。
- EasyCon：串口列表与原版相等；设备用内部 bridge 验证映射和状态，不需要真实硬件；动作序列验证取消后 reset；采集源列表与原版相等；识别用生成的内存图片与原版 ECSearch 对照。
- 架构：反射/项目文件测试保证 Core 无上游/UI 引用，App 无 third_party 引用，服务构造和调用不创建 Form/UserControl/Window。
- CLI：`audit`、`catalog`、`rng`、`calibrate`、`tool`、`sequence-dry-run` 至少各有一个无 UI 测试入口。

## 完成判据

功能矩阵每一项必须为“完成”或明确“排除”；Core、Upstream、CLI 项目测试和 solution build 全部新鲜通过；CLI smoke 能运行；架构引用测试通过；分支提交全部推送。
