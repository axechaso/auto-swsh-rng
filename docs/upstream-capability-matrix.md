# 上游功能清单与去界面化状态

> 审计基线：owoow `a8514d654ae6c7c4ad87c3647c9911a05f6ef42a`；EasyCon `11c4b992b9bce0ff977e9c587a6c0bb0d302853e`。

`完成`表示项目自有模型/接口、Upstream 适配、校验、取消/状态/错误处理和无界面测试入口均已具备；`排除`表示任务明确不纳入。

## owoow

| 业务域 | 上游源码入口 | 项目自有入口 | 状态 |
|---|---|---|---|
| 配置档、游戏、TID/SID、闪耀/证章护符 | `ClientConfig.cs`、`IProfile.cs` | `IProfileStore`、`RngProfile` | 完成 |
| 区域、天气、宝可梦、遭遇表、特性、个人数据 | `Encounters.cs`、`EncounterTable.cs`、`IPersonal.cs` | `IEncounterCatalogService` | 完成 |
| 图鉴推荐与 Encounter Lookup | `PokedexRecommendation.cs`、`Encounters.GetEncounterLookupForGame` | `IEncounterCatalogService`、`IOwoowConnectionService` | 完成 |
| 定点、符号、隐藏、垂钓生成 | `Overworld/Static.cs`、`Symbol.cs`、`Hidden.cs`、`Fishing.cs` | `IOverworldEncounterService` | 完成 |
| IV、异色、证章、气场、身高、稀有 EC 筛选 | `GeneratorConfig.cs`、`Validators/*`、`Fixed.cs` | `EncounterFilter` | 完成 |
| 特性、性格、性别筛选 | `Common.GenerateAbility/GenerateNature/GenerateGender` | `EncounterFilter` | 完成 |
| Seed 读取、写入、变更监听 | `ConnectionWrapper.ReadRNGState/WriteRNGState` | `IOwoowConnectionService` | 完成 |
| 推进、回退、跳跃、NextInt、初始状态 | `RNG/Util/Util.cs`、`XoroshiroTools.cs` | `IXoroshiroService` | 完成 |
| EC/PID/IV/身高与固定种子 | `Overworld/Fixed.cs`、`FixedSeed.cs` | `IXoroshiroService` | 完成 |
| Retail Seed Finder 与动画重定位 | `Misc/SeedFinder.cs`、`RetailSeedFinder.cs` | `IRetailSeedService` | 完成 |
| NPC、关菜单、保持方向 | `Misc/MenuClose.cs` | `ICalibrationService` | 完成 |
| 飞翔、区域加载、雨滴计数 | `Misc/Environment.cs` | `ICalibrationService` | 完成 |
| Spread Finder | `Misc/SpreadFinder.cs` | `ISpreadFinderService` | 完成 |
| Loto-ID | `Item/LotoID.cs` | `ISpecialRngToolService` | 完成 |
| Cram-o-matic | `Item/Cramomatic.cs` | `ISpecialRngToolService` | 完成 |
| Watt Trader | `Item/WattTrader.cs` | `ISpecialRngToolService` | 完成 |
| Digging Pa | `Item/DiggingPa.cs` | `ISpecialRngToolService` | 完成 |
| Digging Bro（21 项奖励） | `Item/SkillBro.cs` | `ISpecialRngToolService` | 完成 |
| Wailord Respawn | `Item/Wailord.cs` | `ISpecialRngToolService` | 完成 |
| 野怪、KCoordinates、场景对象、主机时间 | `ConnectionWrapper.cs`、`FieldObject.cs` | `IOwoowConnectionService` | 完成 |
| Webhook/HTTP 通知 | `Discord/WebhookHandler.cs` | 统一 `INotificationService` | 完成 |

## EasyCon

| 业务域 | 上游源码入口 | 项目自有入口 | 状态 |
|---|---|---|---|
| 串口发现、连接、断开、状态 | `ECDevice.cs`、`NintendoSwitch*.cs` | `IControllerDeviceService` | 完成 |
| Switch 按键、D-pad、双摇杆、Reset | `ECKeyUtil.cs`、`SwitchCommand.cs` | `ControllerInputAction`、`IControllerDeviceService` | 完成 |
| 等待与可取消动作序列 | `ICGamePad.cs`、`CustomSleep.cs` | `IInputSequenceService` | 完成 |
| 运行状态、错误、进度、日志 | `ScriptService.cs` | `SequenceExecutionResult`、`OperationProgress` | 完成 |
| 采集源/后端发现与连接 | `Capture.cs`、`CVCapture.cs` | `ICaptureDeviceService` | 完成 |
| 截图与 PNG 字节边界 | `OpenCVCapture.GetMatFrame` | `ICaptureDeviceService`、`CapturedImage` | 完成 |
| 模板、边缘与 OCR 识别 | `Search.cs`、`OCRDetect.cs` | `IImageRecognitionService` | 完成 |
| 自动化所需手柄映射 | `ECKeyUtil.cs`、`GamePadAdapter.cs` | 项目枚举到 `ECKey` 的 Upstream 映射 | 完成 |
| 输入录制结果 | `OperationRecords.cs`、`NintendoSwitch.Start/Pause/StopRecord` | `IControllerDeviceService` | 完成 |
| 自动化通知 | `AlertDispatcher.cs`、`AlertConfig.cs` | `INotificationService` | 完成 |
| 远程运行/停止 | `EasyCon.Core/Assist` | 无 | 排除 |
| 编译、烧录、清除烧录、固件生成 | `EasyCon.Script/Assembly`、固件服务 | 无 | 排除 |
| 脚本语法帮助 | UI Common/Avalonia Editor | 无 | 排除 |
| ESP32 配置与纯 UI 菜单行为 | `EasyCon2/Forms/ESPConfig*` 等 | 无 | 排除 |

上述排除项不在 App 中创建入口，`AutoSwshRng.Upstream` 也不公开对应适配 API；`third_party` 中的原始实现仅作为可核验的上游源码快照保留。

## 无界面证据

- Core、Upstream、CLI 服务测试不创建 `Form`、`UserControl` 或 Avalonia `Window`。
- `HeadlessCliCommand` 提供 `audit`、`catalog`、`rng`、`calibrate`、`tool`、`sequence-dry-run` 和 `spread` 入口。
- `ProjectReferenceBoundaryTests` 验证 Core 不引用上游/UI 程序集，App 不直接引用 `third_party`。
- 原算法对照测试覆盖四类野外遭遇、Spread Finder、六类特殊工具、Xoroshiro/Retail/校准、目录、采集发现和模板匹配。
