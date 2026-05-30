# auto-swsh-rng architecture

## Current direction

`auto-swsh-rng` uses C# / .NET 10 as the primary stack. Both upstream references that matter most at this stage, `owoow` and EasyCon, are also .NET projects, so the first choice is direct .NET integration instead of a Python-to-.NET bridge.

The first working shape is:

```text
AutoSwshRng.Core       project-owned, portable project metadata and future domain logic
AutoSwshRng.Upstream   Windows-only boundary for upstream references
AutoSwshRng.Cli        small command-line smoke entry point
AutoSwshRng.App        WinForms desktop shell
```

## Upstream boundary

`AutoSwshRng.Upstream` intentionally holds references to upstream projects so the rest of the codebase does not accidentally become tightly coupled to every upstream dependency.

Current upstream references:

- `third_party/owoow/owoow.Core/owoow.Core.csproj`
- `third_party/EasyCon/src/EasyCon.Device/EasyCon.Device.csproj`
- `third_party/EasyCon/src/EasyCon.Script/EasyCon.Script.csproj`

`EasyCon.Core` is not referenced yet. It pulls in capture-related dependencies such as OpenCV, Tesseract, FlashCap, and native/runtime assets through `EasyCon.Capture`. That surface should be added only when the app actually needs those capabilities.

Current smoke adapters call `owoow.Core.RNG.Util` for deterministic RNG helper values and `EasyCon.Script` for in-process script evaluation without a serial device. This proves both upstreams can be used through small project-owned APIs before the final automation workflow exists.

`UpstreamSmokeReport` is the current app-facing facade for these checks. CLI and WinForms consume that report instead of formatting raw upstream calls themselves, which keeps later diagnostics and UI status panels on one project-owned contract.

## UI direction

The desktop app is a single WinForms shell with top-level tabs:

- `概览`
- `owoow`
- `伊机控`
- `自动化流程`

The tabs currently prove that the app can load project-owned code and upstream adapter data. They do not embed the original `owoow` or EasyCon windows. Embedding whole upstream applications would couple lifecycle, config files, menus, message loops, and global state too early.

## Bridge decision

A bridge is not the default plan. `auto_bdsp_rng` needed an EasyCon bridge because it is a Python/PySide6 app controlling a .NET backend. This project is already .NET-native, so direct references and small adapters are simpler until evidence shows otherwise.

Use a bridge only if a future upstream dependency becomes too heavy or unstable to host in-process.

## Verification

The current project-level gates are:

```powershell
dotnet build .\AutoSwshRng.slnx
dotnet test .\AutoSwshRng.slnx
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj
git diff --check
```
