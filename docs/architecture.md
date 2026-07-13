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

- owoow：`IProfileStore`、`ILotoIdStore`、`IEncounterCatalogService`、`IOverworldEncounterService`、`IXoroshiroService`、`IRetailSeedService`、`ICalibrationService`、`ISpreadFinderService`、`ISpecialRngToolService`、`IOwoowConnectionService`。
- EasyCon：`IControllerDeviceService`、`IInputSequenceService`、`ICaptureDeviceService`、`IImageRecognitionService`、`INotificationService`。
- 公共状态：`OperationProgress`、`UpstreamOperationException` 与领域状态枚举。

长范围 RNG 搜索按有界区块调用原版并在区块间检查取消；owoow 工具窗口拥有独立取消源，关闭后不会继续绑定已释放控件。Loto-ID 延迟加载期间锁定搜索入口，关闭编辑器时以快照串行持久化。实机和串口服务通过内部 bridge 测试，不要求测试机连接 Switch。图像以 PNG 字节跨越 Core/Upstream 边界，OpenCV `Mat` 仅存在于 Upstream 内并确定性释放。

## UI 边界

`AutoSwshRng.App` 固定提供三个顶层标签页：

1. `owoow` 复刻固定 owoow 版本的主搜索页、水平工具菜单和服务支持的子窗口。项目自有 WinForms 控件只依赖 `OwoowUiServices` 聚合的 Core 接口；连接、搜索、图鉴推荐显示名/DevId、校准及工具请求在边界内转换为 Core DTO。
2. `伊机控` 复刻固定 EasyCon 版本的菜单、编辑器、日志、设置、设备、采集、录制和手柄操作路径。按键脚本通过内部 GamePad 适配在本页执行；兼容控件位于 Upstream 的 `LegacyUi`，以隔离 EasyCon、OpenCV 和其他上游 UI 类型。
3. `自动化流程` 当前只有预留说明，不承载业务、向导、执行计划或真实设备编排。

远程运行/停止、面向固件的编译与烧录、清除烧录、固件生成、烧录后自动运行、脚本语法帮助、无真实编辑器实现的代码补全/折叠和 ESP32 配置均已从伊机控入口中移除；移除局部控件后仍保留其所在页面并收紧布局。owoow 中没有项目服务契约的 Turbo、Turbo Controls、Reset for Seed 及其设置入口也不出现在 App 中。

`third_party` 只保存固定上游源码快照，不能成为 App 绕过适配层的依赖。Core 接口始终使用项目自有类型，因此两个上游 UI 的控件、窗口和配置类型都不会泄漏到领域边界。

`docs/superpowers/specs/2026-07-07-automation-flow-wizard-ui-design.md` 仅是历史探索草案，不是当前 UI 的实现依据。

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
