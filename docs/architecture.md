# auto-swsh-rng 架构

## 分层边界

```text
AutoSwshRng.Core       项目自有模型、枚举、错误和服务接口；不引用任何上游或 UI 类型
AutoSwshRng.Upstream   唯一上游边界；映射参数、调用原版、转换结果和异常
AutoSwshRng.Cli        无界面验证入口与后续自动化宿主
AutoSwshRng.App        WinForms 壳；不直接引用 third_party
```

Core 接口不暴露 owoow `Frame`/`GeneratorConfig`、EasyCon 设备、PKHeX、OpenCV、WinForms 或 Avalonia 类型。算法优先直接复用原版；项目只拥有校验、分块取消、用例编排和稳定 DTO。

## 业务服务

- owoow：`IProfileStore`、`IEncounterCatalogService`、`IOverworldEncounterService`、`IXoroshiroService`、`IRetailSeedService`、`ICalibrationService`、`ISpreadFinderService`、`ISpecialRngToolService`、`IOwoowConnectionService`。
- EasyCon：`IControllerDeviceService`、`IInputSequenceService`、`ICaptureDeviceService`、`IImageRecognitionService`、`INotificationService`。
- 公共状态：`OperationProgress`、`UpstreamOperationException` 与领域状态枚举。

长范围 RNG 搜索按有界区块调用原版并在区块间检查取消；实机和串口服务通过内部 bridge 测试，不要求测试机连接 Switch。图像以 PNG 字节跨越 Core/Upstream 边界，OpenCV `Mat` 仅存在于 Upstream 内并确定性释放。

## UI 边界

UI 复刻和自动化流程编排不在本阶段继续投入。既有 EasyCon 控件作为兼容壳保存在 Upstream 的 `LegacyUi`，从而让 App 不直接依赖上游；所有新增业务能力均可在不创建窗口的情况下运行。

明确排除远程运行/停止、编译烧录、清除烧录、固件生成、脚本语法帮助、ESP32 配置和纯 UI 菜单行为。

## 无界面 CLI

```powershell
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- audit
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- catalog Sword Symbol
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- rng next 1234 5678 1
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- calibrate menu 1234 5678 0 false Normal
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- tool wailord 1234 5678 0 1
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- sequence-dry-run A:50 wait:10
```

## 验证门

```powershell
dotnet build .\AutoSwshRng.slnx --no-restore
dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj --no-restore
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj --no-restore
dotnet test .\tests\AutoSwshRng.Cli.Tests\AutoSwshRng.Cli.Tests.csproj --no-restore
dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj --no-restore
git diff --check
```
