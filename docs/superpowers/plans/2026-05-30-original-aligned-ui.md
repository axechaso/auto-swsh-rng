# Original-Aligned UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current smoke-test UI with an original-aligned WinForms shell: top-level tabs for `owoow`, `伊机控`, and `自动化流程`; an `owoow` page with a left vertical version of the original owoow tool menu; and an `伊机控` page that restores the original EasyCon layout without AutoSwshRng linkage panels for now.

**Architecture:** Keep `MainForm` as the top-level host, but move large tab contents into focused controls under `src/AutoSwshRng.App/Controls`. Keep UI strings and stable control names in project-owned code so tests can verify original-layout alignment without reaching into upstream WinForms internals.

**Tech Stack:** C# / .NET 10, WinForms, NUnit, existing `AutoSwshRng.Upstream` smoke adapters.

---

## Files

- Modify: `src/AutoSwshRng.App/MainForm.cs`
- Create: `src/AutoSwshRng.App/Controls/OwoowTabControl.cs`
- Create: `src/AutoSwshRng.App/Controls/EasyConTabControl.cs`
- Create: `src/AutoSwshRng.App/Controls/AutomationFlowTabControl.cs`
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`
- Create: `tests/AutoSwshRng.App.Tests/ControlTestHelpers.cs`
- Modify: `README.md`
- Modify: `docs/architecture.md`

## Alignment References

- owoow original: `third_party/owoow/owoow.WinForms/MainWindow.Designer.cs`
- EasyCon original: `third_party/EasyCon/src/EasyCon2/App/MainForm.Designer.cs`
- Older EasyCon form reference: `third_party/EasyCon/src/EasyCon2/App/EasyConForm.Designer.cs`

## Translation Policy

- Use Chinese labels for user-facing terms where the translation is stable: `配置档`, `遭遇查询`, `个体帧搜索`, `ID抽奖`, `机器鹕`, `瓦特商店`, `挖挖伯`, `挖洞兄弟（技巧型）`, `吼鲸王再出现`, `闪耀护符`, `证章护符`, `牙牙湖之眼`.
- Keep technical RNG identifiers in English or short-form where translating would reduce clarity: `Seed`, `TID`, `SID`, `PID`, `EC`, `Adv`.
- Keep EasyCon original menu words as they already appear in upstream Chinese UI: `文件`, `编辑`, `脚本`, `搜图`, `设置`, `蓝牙`, `ESP32`, `画图`, `帮助`.

---

### Task 1: Top-Level Tab Contract

**Files:**
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`
- Modify: `src/AutoSwshRng.App/MainForm.cs`

- [x] **Step 1: Write failing test**

Assert that `MainForm.TabTitles` is exactly `owoow`, `伊机控`, `自动化流程`, and no longer contains `概览`.

- [x] **Step 2: Run test**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

Expected: FAIL because the current UI still includes `概览`.

- [x] **Step 3: Implement minimal top-level tabs**

Update `MainForm` to host only the three top-level tabs and move smoke text out of the top-level structure.

- [x] **Step 4: Verify**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

- [x] **Step 5: Commit**

Commit message: `refactor:调整主界面标签结构`

### Task 2: owoow Original-Aligned Page

**Files:**
- Create: `src/AutoSwshRng.App/Controls/OwoowTabControl.cs`
- Modify: `src/AutoSwshRng.App/MainForm.cs`
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`
- Create or modify: `tests/AutoSwshRng.App.Tests/ControlTestHelpers.cs`

- [x] **Step 1: Write failing tests**

Assert that the `owoow` tab contains a left navigation named `owoowToolMenu` with labels:
`配置档`, `遭遇查询`, `个体帧搜索`, `ID抽奖`, `机器鹕`, `瓦特商店`, `挖挖伯`, `挖洞兄弟（技巧型）`, `吼鲸王再出现`, `Xoroshiro 工具`.

Assert that the work area contains original-aligned control labels:
`Seed[0]:`, `Seed[1]:`, `Switch IP:`, `闪耀护符?`, `证章护符?`, `游戏:`, `遭遇设置 - 定点`, `区域:`, `天气:`, `目标:`, `宝可梦图鉴“现在推荐”`, `高级设置`, `异色:`, `证章:`, `CFW 工具`, `实机工具`.

- [x] **Step 2: Run test**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

Expected: FAIL because `OwoowTabControl` and these named controls do not exist yet.

- [x] **Step 3: Implement `OwoowTabControl`**

Build a `UserControl` with:
- left vertical navigation for the original owoow menu;
- central dense original-aligned work area;
- bottom result grid with columns matching original concepts: `推进数`, `跳跃`, `步数`, `动画`, `宝可梦`, `异色`, `气场`, `等级`, `特性`, `性格`, `性别`, `HP`, `攻击`, `防御`, `特攻`, `特防`, `速度`, `证章`, `EC`, `PID`, `身高`.

- [x] **Step 4: Verify**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

- [x] **Step 5: Commit**

Commit message: `feat:复刻owoow主界面结构`

### Task 3: EasyCon Original-Restore Page

**Files:**
- Create: `src/AutoSwshRng.App/Controls/EasyConTabControl.cs`
- Modify: `src/AutoSwshRng.App/MainForm.cs`
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`

- [ ] **Step 1: Write failing tests**

Assert that the `伊机控` tab contains original EasyCon menu labels:
`文件`, `编辑`, `脚本`, `搜图`, `设置`, `蓝牙`, `ESP32`, `画图`, `帮助`.

Assert that it contains original functional areas:
`easyConScriptEditor`, `easyConLogBox`, `easyConSerialPanel`, `easyConCapturePanel`, `easyConRecordPanel`, `easyConControllerPanel`, `easyConFirmwarePanel`, and status text for `串口状态` and `采集状态`.

- [ ] **Step 2: Run test**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

Expected: FAIL because the current EasyCon tab is only a small diagnostic panel.

- [ ] **Step 3: Implement `EasyConTabControl`**

Build a `UserControl` that restores the original EasyCon layout shape:
- horizontal EasyCon menu;
- central script editor;
- right-side run/serial/capture/record/controller/firmware controls;
- bottom log and status strip;
- no AutoSwshRng linkage panel in this task.

- [ ] **Step 4: Verify**

Run: `dotnet test .\tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj`

- [ ] **Step 5: Commit**

Commit message: `feat:还原伊机控标签页结构`

### Task 4: Automation Placeholder And Documentation

**Files:**
- Create: `src/AutoSwshRng.App/Controls/AutomationFlowTabControl.cs`
- Modify: `README.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: Write failing test**

Assert that `自动化流程` tab exists and contains a placeholder named `automationFlowPlaceholder` explaining that workflow UI will be designed after user approval.

- [ ] **Step 2: Implement placeholder control**

Add a minimal `AutomationFlowTabControl` with stable naming and no premature workflow behavior.

- [ ] **Step 3: Update docs**

Document that `owoow` and `伊机控` are now separate top-level tabs and that EasyCon linkage is intentionally deferred.

- [ ] **Step 4: Full verification**

Run:

```powershell
dotnet build .\AutoSwshRng.slnx
dotnet test .\AutoSwshRng.slnx
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj
git diff --check
```

- [ ] **Step 5: Commit**

Commit message: `docs:记录界面重构方向`
