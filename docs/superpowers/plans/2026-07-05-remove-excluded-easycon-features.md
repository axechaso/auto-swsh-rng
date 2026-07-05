# Remove Excluded EasyCon Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove excluded EasyCon firmware, flashing, and remote-operation behavior from the App and project adapters without affecting headless automation services.

**Architecture:** Keep `EasyConTabControl` only as a compatibility UI for retained serial, controller, recording, capture, and local script behavior. Remove excluded controls at construction time and delete their private implementation paths, then enforce the boundary with absence tests and API reflection tests.

**Tech Stack:** C# 14, .NET 10, WinForms, NUnit, PowerShell 5.1.

---

### Task 1: Lock the App exclusion boundary

**Files:**
- Modify: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`

- [ ] **Step 1: Write the failing App test**

Add `EasyConExcludedFeaturesAreNotExposed` and assert that control names
`btnRemoteStart`, `btnRemoteStop`, `btnPageBurn`, `btnFlash`, `btnFlashClear`,
`btnGenFirmware`, `chkAutoRunAfterFlash`, and `btnESPConfig` cannot be found.
Also assert that tool-strip names `autoRunAfterFlashMenuItem`,
`menuItemFirmwareMode`, `menuItemFlashMode`, `menuItemScriptSyntax`, and
`espConfigMenuItem` cannot be found.

- [ ] **Step 2: Verify RED**

Run:
`dotnet test tests\AutoSwshRng.App.Tests\AutoSwshRng.App.Tests.csproj --no-restore --filter FullyQualifiedName~EasyConExcludedFeaturesAreNotExposed`

Expected: FAIL because the controls and menu items still exist.

- [ ] **Step 3: Remove obsolete UI tests**

Delete tests whose only purpose is firmware assembly, firmware generation,
flashing, flash clearing, remote start/stop, excluded help entries, ESP32
configuration, or flash-auto-run settings. Update structural assertions so the
remaining UI describes retained automation capabilities.

### Task 2: Remove excluded Legacy UI implementation

**Files:**
- Modify: `src/AutoSwshRng.Upstream/LegacyUi/EasyConTabControl.cs`
- Test: `tests/AutoSwshRng.App.Tests/MainFormTests.cs`

- [ ] **Step 1: Delete excluded event wiring and controls**

Remove the excluded control creation calls, menu item creation, page button,
settings checkbox, and corresponding event subscriptions.

- [ ] **Step 2: Delete unreachable implementation**

Remove firmware constants and delegates, remote/flash/generate methods,
firmware-board population, firmware-file helpers, and excluded dialog/help
delegates. Preserve retained serial, capture, controller, recording, and local
script paths.

- [ ] **Step 3: Verify GREEN**

Run the filtered App test and then the complete App test project. Expected:
all tests pass without interactive windows.

- [ ] **Step 4: Commit**

Commit with `refactor:移除EasyCon烧录固件与远程操作入口`.

### Task 3: Remove firmware adapter API

**Files:**
- Modify: `tests/AutoSwshRng.Upstream.Tests/EasyConScriptAdapterTests.cs`
- Modify: `src/AutoSwshRng.Upstream/EasyConScriptAdapter.cs`

- [ ] **Step 1: Write the failing API absence test**

Reflect over public static methods on `EasyConScriptAdapter` and assert no
method named `AssembleFirmwareScript` exists; assert the Upstream assembly has
no `EasyConFirmwareAssemblyResult` type.

- [ ] **Step 2: Verify RED**

Run the filtered Upstream test. Expected: FAIL because both API artifacts exist.

- [ ] **Step 3: Delete firmware-only adapter code and old tests**

Remove `AssembleFirmwareScript` overloads, firmware assembly exception
translation, and `EasyConFirmwareAssemblyResult`. Preserve `Compile`, `Format`,
and `Evaluate`.

- [ ] **Step 4: Verify GREEN and commit**

Run all Upstream tests and commit with
`refactor:移除EasyCon固件汇编适配能力`.

### Task 4: Update audit evidence and complete verification

**Files:**
- Modify: `docs/upstream-capability-matrix.md`

- [ ] **Step 1: Update exclusion evidence**

State that excluded features have no App entry or project adapter API.

- [ ] **Step 2: Run full verification**

Run restore if required, solution build, Core/Upstream/CLI/App tests, seven CLI
smoke commands, `git diff --check`, and `git status`.

- [ ] **Step 3: Commit and push**

Commit documentation with `docs:更新EasyCon排除功能审计证据`, push `main`,
and verify `origin/main...main` is `0 0`.

