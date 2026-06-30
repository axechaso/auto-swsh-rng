# 上游功能清单与去界面化状态

> 审计基线：owoow `a8514d654ae6c7c4ad87c3647c9911a05f6ef42a`；EasyCon `11c4b992b9bce0ff977e9c587a6c0bb0d302853e`。

状态定义：

- `待抽象`：已确认存在上游入口，尚无完整项目自有接口。
- `部分完成`：已有接口或适配器，但模型、校验、取消、错误、测试、CLI/文档中至少一项缺失。
- `完成`：Core 模型与接口、Upstream 适配器、无界面对照测试和 CLI/测试入口均齐备。
- `排除`：按任务明确不纳入，且不存在自动化必需的共用依赖。

## owoow

| 业务域 | 上游源码入口 | 主要依赖 | 项目自有接口 | 状态 |
|---|---|---|---|---|
| 配置档、游戏、TID/SID、护符 | `owoow.WinForms/ClientConfig.cs`；`owoow.Core/Interfaces/IProfile.cs`；`Subforms/Profiles.cs` | JSON、owoow Profile | `IProfileStore`（计划） | 待抽象 |
| 区域/天气/宝可梦目录 | `owoow.Core/Encounters/Encounters.cs` | Sword/Shield 遭遇 JSON | `IEncounterCatalogService`（计划） | 待抽象 |
| 遭遇表、特性与个人数据 | `EncounterTable.cs`；`IPersonal.cs` | personal.json、PKHeX | `IEncounterCatalogService`（计划） | 待抽象 |
| 图鉴推荐与 Encounter Lookup | `PokedexRecommendation.cs`；`Encounters.GetEncounterLookupForGame/GetDexRecOptions` | 遭遇 JSON、RAM（可选） | `IEncounterCatalogService`（计划） | 待抽象 |
| 定点遭遇 | `RNG/Generators/Overworld/Static.cs` | Common、Fixed、EncounterTable | `IOverworldEncounterService`（计划） | 待抽象 |
| 符号遭遇 | `RNG/Generators/Overworld/Symbol.cs` | Common、Fixed、EncounterTable | 同上 | 待抽象 |
| 隐藏遭遇 | `RNG/Generators/Overworld/Hidden.cs` | Common、Fixed、EncounterTable | 同上 | 待抽象 |
| 垂钓遭遇 | `RNG/Generators/Overworld/Fishing.cs` | Common、Fixed、EncounterTable | 同上 | 待抽象 |
| IV/异色/证章/气场/身高筛选 | `GeneratorConfig.cs`；`Validators/*`；`Fixed.GenerateIVs` | PKHeX Nature/Ribbon | `EncounterFilter`（计划） | 待抽象 |
| 特性/性格/性别/EC 筛选 | `Common.GenerateAbility/GenerateNature/GenerateGender`；`Validator.CheckEC` | PKHeX | `EncounterFilter`（计划） | 待抽象 |
| Seed 读取/写入/监控 | `ConnectionWrapper.ReadRNGState/WriteRNGState`；`MainWindow.Connect` | SysBot.Base | `IOwoowConnectionService`（计划） | 待抽象 |
| 推进、跳跃、EC/PID、固定种子 | `RNG/Util/Util.cs`；`Overworld/Fixed.cs`；`FixedSeed.cs` | PKHeX Xoroshiro | `IXoroshiroService`（计划） | 部分完成 |
| 实机/Retail Seed | `Misc/SeedFinder.cs`；`Subforms/RetailSeedFinder.cs` | MatrixUtil、Xoroshiro | `IRetailSeedService`（计划） | 待抽象 |
| NPC/关菜单/保持方向 | `Misc/MenuClose.cs` | Xoroshiro | `ICalibrationService`（计划） | 待抽象 |
| 飞翔/区域加载/雨滴 | `Misc/Environment.cs`；MainWindow rain/fly 配置 | Xoroshiro | `ICalibrationService`（计划） | 待抽象 |
| Spread Finder | `Misc/SpreadFinder.cs` | Fixed、Validator | `ISpreadFinderService` | 部分完成 |
| Loto-ID | `Item/LotoID.cs` | MenuClose、Validator | `ISpecialRngToolService`（计划） | 待抽象 |
| Cram-o-matic | `Item/Cramomatic.cs` | MenuClose、Validator | 同上 | 待抽象 |
| Watt Trader | `Item/WattTrader.cs` | MenuClose | 同上 | 待抽象 |
| Digging Pa | `Item/DiggingPa.cs` | MenuClose | 同上 | 待抽象 |
| Digging Bro | `Item/SkillBro.cs` | MenuClose、Game | 同上 | 待抽象 |
| Wailord Respawn | `Item/Wailord.cs` | MenuClose、Validator | 同上 | 待抽象 |
| Xoroshiro Tools | `Subforms/XoroshiroTools.cs` 调用 `Util`/PKHeX RNG | Xoroshiro | `IXoroshiroService`（计划） | 待抽象 |
| 野生宝可梦/KCoordinates/时间 | `ConnectionWrapper.cs`；`FieldObject.cs` | SysBot、PKHeX | `IOwoowConnectionService`（计划） | 待抽象 |
| Webhook 通知 | `Discord/WebhookHandler.cs` | PKHeX、HTTP | 统一由自动化通知服务覆盖 | 待抽象 |

## EasyCon

| 业务域 | 上游源码入口 | 主要依赖 | 项目自有接口 | 状态 |
|---|---|---|---|---|
| 串口发现、连接、断开、状态 | `EasyCon.Device/ECDevice.cs`；`NintendoSwitch*.cs`；`Connection/*` | System.IO.Ports | `IControllerDeviceService`（计划） | 待抽象 |
| Switch 按键/D-pad/摇杆/reset | `ECKeyUtil.cs`；`SwitchCommand.cs`；`GamePadAdapter.cs` | EasyCon.Device | `IControllerInputService`（计划） | 待抽象 |
| 等待与可取消动作序列 | `EasyCon.Script/ICGamePad.cs`；`CustomSleep.cs`；`Compilation.Evaluate` | EasyCon.Script | `IInputSequenceService`（计划） | 部分完成 |
| 运行状态、错误、日志 | `EasyCon2*/Services/ScriptService.cs` | 服务编排 | `IInputSequenceService`（计划） | 待抽象 |
| 采集源发现与连接 | `EasyCon.Capture/Capture.cs`；`CVCapture.cs` | FlashCap、OpenCV | `ICaptureDeviceService`（计划） | 待抽象 |
| 截图 | `OpenCVCapture.GetMatFrame`；`MatExtensions` | OpenCV | `ICaptureDeviceService`（计划） | 待抽象 |
| 模板/边缘/OCR 识别 | `Search.cs`；`ImgLabel.cs`；`OCRDetect.cs` | OpenCV、Tesseract | `IImageRecognitionService`（计划） | 待抽象 |
| 必要手柄映射 | `KeyMappingConfig.cs`；`GamepadMappingConfig.cs` | 无/WinInput | `IControllerMappingService`（计划） | 待抽象 |
| 输入录制结果 | `OperationRecords.cs`；`NintendoSwitch.Start/Pause/StopRecord` | EasyCon.Device | `IControllerInputService`（计划） | 待抽象 |
| 自动化通知 | `AlertDispatcher.cs`；`AlertConfig.cs` | HttpClient | `IAutomationNotificationService`（计划） | 待抽象 |
| 远程运行/停止 | `EasyCon.Core/Assist` 等 | 排除 | 无 | 排除 |
| 编译、烧录、固件生成 | `EasyCon.Script/Assembly`、固件服务 | 排除 | 无 | 排除 |
| 脚本语法帮助与编辑器 | UI Common/Avalonia Editor | 排除 | 无 | 排除 |
| ESP32 配置与纯 UI 行为 | `EasyCon2/Forms/ESPConfig*` 等 | 排除 | 无 | 排除 |
