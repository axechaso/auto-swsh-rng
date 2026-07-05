# auto-swsh-rng

尝试剑盾乱数自动化

## 上游参考

本仓库使用 `third_party` 固定上游参考代码，项目自身实现应放在 `src/AutoSwshRng.*` 等主项目目录中。

- owoow: https://github.com/LegoFigure11/owoow
- EasyCon: https://github.com/EasyConNS/EasyCon

首次克隆后初始化子模块：

```powershell
git submodule update --init --recursive
```

## 开发

本项目优先使用 C# / .NET 10 构建。当前仓库已经提供：

- `AutoSwshRng.Core`: 项目自有的基础信息和后续纯领域逻辑入口
- `AutoSwshRng.Upstream`: 对 `owoow` 与 EasyCon 的 Windows-only 适配边界
- `AutoSwshRng.Cli`: 命令行烟测入口
- `AutoSwshRng.App`: WinForms 桌面壳，包含 `owoow`、`伊机控`、`自动化流程` 三个顶层标签页

```powershell
dotnet build .\AutoSwshRng.slnx
dotnet test .\AutoSwshRng.slnx
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj
dotnet run --project .\src\AutoSwshRng.App\AutoSwshRng.App.csproj
```

## 无界面上游能力

`AutoSwshRng.Core` 定义全部项目自有模型与接口；`AutoSwshRng.Upstream` 是唯一直接引用 owoow、EasyCon、PKHeX、OpenCV 和 UI 兼容类型的边界。配置、目录、四类野外遭遇、筛选、Seed/Retail/校准、Spread Finder、特殊工具、实机内存、串口输入、动作序列、采集识别和通知均可脱离 UI 调用。

可以在不创建任何 WinForms 控件的情况下搜索单个固定种子：

```powershell
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- spread 17033091 0
```

参数分别是 8 位十六进制固定种子和保底 31 个体值数量。统一 CLI 还支持：

```powershell
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- audit
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- catalog Sword Symbol
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- rng next 1234 5678 1
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- calibrate menu 1234 5678 0 false Normal
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- tool wailord 1234 5678 0 1
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- sequence-dry-run A:50 wait:10
```

完整能力清单见 `docs/upstream-capability-matrix.md`，架构说明见 `docs/architecture.md`。
