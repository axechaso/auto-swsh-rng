# Upstream Headless Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose every in-scope owoow and EasyCon capability through project-owned, headless Core contracts with original-result parity tests and CLI proof points.

**Architecture:** Core owns immutable requests, results, enums, errors, progress, and service interfaces. Upstream is the only project that references owoow, EasyCon, PKHeX, SysBot, or OpenCV and converts those types at the boundary; App and CLI consume only project contracts.

**Tech Stack:** C# 14, .NET 10, NUnit 4, owoow.Core, EasyCon.Device, EasyCon.Script, EasyCon.Capture, System.Text.Json.

---

### Task 1: Architecture guardrails, errors, profiles, and encounter catalog

**Files:**
- Create: `src/AutoSwshRng.Core/Common/UpstreamOperationException.cs`
- Create: `src/AutoSwshRng.Core/Common/OperationProgress.cs`
- Create: `src/AutoSwshRng.Core/Profiles/ProfileContracts.cs`
- Create: `src/AutoSwshRng.Core/Encounters/EncounterCatalogContracts.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowProfileStore.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowEncounterCatalogService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Architecture/CoreBoundaryTests.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Profiles/ProfileContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowEncounterCatalogServiceTests.cs`

- [x] **Step 1: Write failing boundary and contract tests**

```csharp
[Test]
public void CoreReferencesNoUpstreamOrUiAssembly() =>
    Assert.That(typeof(RngProfile).Assembly.GetReferencedAssemblies()
        .Select(a => a.Name), Has.None.Matches<string>(n =>
            n!.Contains("owoow") || n.Contains("EasyCon") ||
            n.Contains("Windows.Forms") || n.Contains("Avalonia")));

[Test]
public async Task CatalogMatchesOriginalAreaList()
{
    var actual = await new OwoowEncounterCatalogService()
        .GetAreasAsync(GameVersion.Sword, EncounterKind.Symbol);
    Assert.That(actual, Is.EqualTo(
        owoow.Core.Encounters.GetAreaList(Game.Sword, EncounterType.Symbol)
            .OrderBy(x => x)));
}
```

- [x] **Step 2: Run tests and verify missing-type failures**

Run: `dotnet test tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj --filter "CoreBoundaryTests|ProfileContractsTests"` and the matching Upstream filter.  
Expected: compile failure because the new contracts/services do not exist.

- [x] **Step 3: Implement immutable contracts, JSON profile store, and catalog mapping**

`RngProfile` validates non-empty name and 0..65535 IDs. `IEncounterCatalogService` exposes sorted areas, weather, species, encounter slots, personal details, Dex recommendations, and lookup results; the adapter maps `Encounters`/`EncounterTable` without returning upstream interfaces.

- [x] **Step 4: Run Core and Upstream tests**

Run both project test commands.  
Expected: all tests pass.

- [x] **Step 5: Commit**

`git commit -m "feat:抽象配置档与遭遇目录服务"`

### Task 2: Xoroshiro, fixed-seed, Retail Seed Finder, and calibration

**Files:**
- Create: `src/AutoSwshRng.Core/Rng/RngContracts.cs`
- Create: `src/AutoSwshRng.Core/Rng/CalibrationContracts.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowXoroshiroService.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowRetailSeedService.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowCalibrationService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Rng/RngContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowRngServicesTests.cs`

- [x] **Step 1: Write failing tests for validation and original parity**

```csharp
[Test]
public void RetailSeedRequiresExactly128BinaryObservations() =>
    Assert.Throws<ArgumentException>(() => new RetailSeedRequest("0101"));

[Test]
public async Task MenuCloseMatchesOriginal()
{
    var request = new MenuCloseCalibrationRequest(
        new RngState(0x1234, 0x5678), 3, true, Weather.Thunderstorm);
    var result = await new OwoowCalibrationService().CalculateMenuCloseAsync(request);
    var rng = new Xoroshiro128Plus(0x1234, 0x5678);
    Assert.That(result.Advances, Is.EqualTo(
        MenuClose.GetAdvances(ref rng, 3, true, WeatherType.Thunderstorm)));
}
```

- [x] **Step 2: Verify red**

Run filtered Core and Upstream tests.  
Expected: compile failure for missing contracts/services.

- [x] **Step 3: Implement services by calling `Util`, `Fixed`, `SeedFinder`, `MenuClose`, and `Environment`**

Support Next, Previous, NextInt, FindInitial, state distance, EC/PID/IV/height, exact and ranged Retail seed search, animation generation/re-identification, NPC/menu close, rain, memory roll, area load, and combined fly calibration. Long Retail ranges check cancellation between bounded advance windows.

- [x] **Step 4: Verify green and commit**

Run Core and Upstream tests, then commit:

`git commit -m "feat:抽象种子运算与现场校准服务"`

### Task 3: Four overworld encounter generators and complete filters

**Files:**
- Create: `src/AutoSwshRng.Core/Encounters/OverworldSearchContracts.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowOverworldEncounterService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Encounters/OverworldSearchRequestTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowOverworldEncounterServiceTests.cs`

- [x] **Step 1: Write failing tests**

Tests cover six IV constraints, ID ranges, advance ranges, hidden max step, static species requirement, cancellation, and one fixed-input parity case for Static, Symbol, Hidden, and Fishing.

```csharp
Assert.That(actual.Select(ResultProjection), Is.EqualTo(
    (await Symbol.Generate(s0, s1, table, start, end, config))
        .Select(FrameProjection)));
```

- [x] **Step 2: Verify red**

Run filtered tests and confirm missing types.

- [x] **Step 3: Implement request mapping and result conversion**

Map every `GeneratorConfig` field from explicit Core models. Split inclusive ranges into chunks no larger than 25,000 advances; check cancellation and report progress between chunks. Apply project-owned ability/nature/gender post-filters after original generation where owoow lacks pre-filter fields.

- [x] **Step 4: Verify all four parity cases, cancellation, and commit**

`git commit -m "feat:抽象四类野外遭遇搜索服务"`

### Task 4: Spread Finder hardening and all special RNG tools

**Files:**
- Modify: `src/AutoSwshRng.Upstream/OwoowSpreadFinderService.cs`
- Create: `src/AutoSwshRng.Core/Rng/SpecialToolContracts.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowSpecialRngToolService.cs`
- Modify: `tests/AutoSwshRng.Upstream.Tests/OwoowSpreadFinderServiceTests.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Rng/SpecialToolContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowSpecialRngToolServiceTests.cs`

- [x] **Step 1: Write failing tests**

Add prompt-cancellation coverage for Spread Finder and fixed-state parity tests for Loto-ID, Cram-o-matic, Watt Trader, Digging Pa, Digging Bro, and Wailord Respawn.

- [x] **Step 2: Verify red**

Run filtered tests; cancellation test must fail against the existing large-partition behavior and special-tool tests must not compile.

- [x] **Step 3: Implement bounded chunks and special-tool mappings**

Use one stable `SpecialToolSearchRequest` discriminated by `SpecialToolKind`; validate kind-specific fields and map every upstream frame property, including Digging Bro reward counts.

- [x] **Step 4: Verify and commit**

`git commit -m "feat:抽象特殊乱数工具并强化搜索取消"`

### Task 5: owoow live connection, RAM data, and state watching

**Files:**
- Create: `src/AutoSwshRng.Core/Connection/OwoowConnectionContracts.cs`
- Create: `src/AutoSwshRng.Upstream/OwoowConnectionService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Connection/OwoowConnectionContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowConnectionServiceTests.cs`

- [x] **Step 1: Write failing contract and bridge tests**

Use an internal test bridge to exercise connect/disconnect status, RNG read/write conversion, trainer/dex/wild/field-object mapping, cancellation, and exception conversion without Switch hardware.

- [x] **Step 2: Verify red**

Run filtered tests and observe missing contracts.

- [x] **Step 3: Implement `ConnectionWrapperAsync` adapter and internal bridge**

Expose Wi-Fi/USB settings as Core models; convert PK8/FieldObject into project records; implement `WatchRngStateAsync` with cancellable polling delay instead of the upstream UI busy loop; include date/time primitives needed by automation.

- [x] **Step 4: Verify and commit**

`git commit -m "feat:抽象实机连接与内存读取服务"`

### Task 6: EasyCon serial device, inputs, sequences, recording, and mapping

**Files:**
- Create: `src/AutoSwshRng.Core/Automation/ControllerContracts.cs`
- Create: `src/AutoSwshRng.Core/Automation/InputSequenceContracts.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConControllerDeviceService.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConInputSequenceService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Automation/InputSequenceContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/EasyConControllerServicesTests.cs`

- [x] **Step 1: Write failing tests**

Compare adapter port discovery to `ECDevice.GetPortNames`, verify every button/D-pad/stick mapping through a recording bridge, verify status/error conversion, and verify cancellation/failure always sends reset.

- [x] **Step 2: Verify red**

Run filtered tests; missing contracts/services must fail compilation.

- [x] **Step 3: Implement device/input/sequence services**

Wrap only `TryConnect`, `Disconnect`, status, Down/Up/Reset, and recording APIs. Execute strong-typed actions serially with `Task.Delay(..., token)` and progress/log events; do not expose Flash, RemoteStart, RemoteStop, firmware, or UI hooks.

- [x] **Step 4: Verify and commit**

`git commit -m "feat:抽象手柄设备与可取消动作序列"`

### Task 7: EasyCon capture, screenshots, recognition, and notifications

**Files:**
- Modify: `src/AutoSwshRng.Upstream/AutoSwshRng.Upstream.csproj`
- Create: `src/AutoSwshRng.Core/Capture/CaptureContracts.cs`
- Create: `src/AutoSwshRng.Core/Notifications/NotificationContracts.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConCaptureDeviceService.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConImageRecognitionService.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConNotificationService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Capture/CaptureContractsTests.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/EasyConCaptureServicesTests.cs`

- [x] **Step 1: Write failing tests**

Test rectangle/template validation; compare source/backend discovery with ECCapture; compare byte-based template match with direct ECSearch on generated in-memory PNGs; verify invalid image and cancellation errors; test notification request conversion with an in-memory HTTP handler.

- [x] **Step 2: Verify red**

Run filtered tests; missing Capture reference/types must fail.

- [x] **Step 3: Add only `EasyCon.Capture` and implement byte-boundary services**

Convert image bytes to Mat inside `using`, return encoded bytes and project points only, dispose capture/native resources deterministically, and map OCR text/match confidence. Notification accepts cancellation and returns provider results.

- [x] **Step 4: Verify and commit**

`git commit -m "feat:抽象采集识别与自动化通知服务"`

### Task 8: Remove App upstream leakage and add headless CLI commands

**Files:**
- Modify: `src/AutoSwshRng.App/AutoSwshRng.App.csproj`
- Modify: `src/AutoSwshRng.App/Controls/EasyConTabControl.cs`
- Modify: `src/AutoSwshRng.Cli/Program.cs`
- Create: `src/AutoSwshRng.Cli/HeadlessCliCommand.cs`
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`
- Create: `tests/AutoSwshRng.Cli.Tests/HeadlessCliCommandTests.cs`
- Create: `tests/AutoSwshRng.Core.Tests/Architecture/ProjectReferenceBoundaryTests.cs`

- [x] **Step 1: Write failing architecture and CLI tests**

```csharp
[Test]
public void AppProjectHasNoThirdPartyProjectReference() =>
    Assert.That(File.ReadAllText(AppProjectPath), Does.Not.Contain("third_party"));

[TestCase("audit")]
[TestCase("catalog Sword Symbol")]
[TestCase("rng next 1234 5678 1")]
[TestCase("calibrate menu 1234 5678 0 false Normal")]
[TestCase("tool wailord 1234 5678 0 1")]
[TestCase("sequence-dry-run A:50 wait:10")]
public async Task HeadlessCommandsRunWithoutUi(string command)
{
    var exitCode = await HeadlessCliCommand.RunAsync(
        command.Split(' '),
        TextWriter.Null,
        TextWriter.Null,
        HeadlessCliServices.CreateDefaults());
    Assert.That(exitCode, Is.Zero);
}
```

- [x] **Step 2: Verify red**

Run architecture and CLI tests; App reference test and commands must fail.

- [x] **Step 3: Replace direct EasyCon UI dependencies with project contracts**

Preserve the existing tab shell without extending UI behavior, remove all EasyCon/Avalonia compile-time types from App, and route in-scope device/capture/actions through project services. Explicitly disable legacy excluded controls instead of adding new abstraction work.

- [x] **Step 4: Implement CLI dispatcher and commands**

Use injected Core interfaces for tests and concrete Upstream services in `Program`. Output stable text/JSON-friendly rows; never instantiate Form/UserControl/Window.

- [x] **Step 5: Run App, CLI, and architecture tests; commit**

`git commit -m "refactor:移除界面对上游类型的直接依赖"`

### Task 9: Final documentation, capability audit, and full verification

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/upstream-capability-matrix.md`
- Modify: `README.md`
- Create: `tests/AutoSwshRng.Upstream.Tests/HeadlessConstructionTests.cs`
- Modify: `task_plan.md`
- Modify: `progress.md`

- [x] **Step 1: Write final audit test**

Reflect over every Core service interface, instantiate each concrete Upstream service, and assert no loaded type derives from Form/UserControl/Avalonia Window. Assert every matrix in-scope row is `完成`.

- [x] **Step 2: Verify the audit test fails while statuses are incomplete**

Run the filtered audit test and confirm expected failure.

- [x] **Step 3: Update architecture, matrix, README, and working logs**

Document source entry, dependency, project interface, adapter, parity test, CLI/test proof, exclusions, and known hardware-only test limitations for every row.

- [x] **Step 4: Run fresh final gates**

```powershell
dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj
dotnet test .\tests\AutoSwshRng.Cli.Tests\AutoSwshRng.Cli.Tests.csproj
dotnet build .\AutoSwshRng.slnx
dotnet test .\AutoSwshRng.slnx --no-build
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- audit
git diff --check
```

Expected: every command exits 0; tests report zero failures; CLI audit reports all included capabilities complete.

- [x] **Step 5: Commit and push**

```powershell
git commit -m "docs:完成上游去界面化能力审计"
git push -u origin codex/upstream-headless-abstraction
```
