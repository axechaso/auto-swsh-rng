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

CLI 当前会输出上游烟测状态；桌面壳当前优先对齐原版界面：`owoow` 标签页使用左侧竖向工具菜单承载原版功能，`伊机控` 标签页先按 EasyCon 原版脚本/设备控制布局还原，`自动化流程` 暂留稳定占位等待后续 UI 需求。

当前架构说明见 `docs/architecture.md`。
