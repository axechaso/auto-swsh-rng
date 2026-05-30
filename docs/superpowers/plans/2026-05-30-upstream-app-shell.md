# Upstream App Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove direct .NET references to `owoow` and EasyCon, then create the first unified WinForms shell with separate tabs.

**Architecture:** Keep `AutoSwshRng.Core` pure and add `AutoSwshRng.Upstream` as the Windows-only adapter boundary for upstream references. Add `AutoSwshRng.App` as a simple WinForms host with tabs for overview, owoow, EasyCon, and later automation.

**Tech Stack:** C# / .NET 10, NUnit, WinForms, project references to `third_party/owoow` and `third_party/EasyCon`.

---

### Task 1: Add Upstream Adapter Project

**Files:**
- Create: `src/AutoSwshRng.Upstream/AutoSwshRng.Upstream.csproj`
- Create: `src/AutoSwshRng.Upstream/OwoowUpstreamInfo.cs`
- Create: `src/AutoSwshRng.Upstream/EasyConUpstreamInfo.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/AutoSwshRng.Upstream.Tests.csproj`
- Create: `tests/AutoSwshRng.Upstream.Tests/UpstreamInfoTests.cs`
- Modify: `AutoSwshRng.slnx`

- [x] Write tests that assert stable assembly/type names from both upstreams.
- [x] Verify tests fail before adapter implementation.
- [x] Implement minimal upstream info adapters.
- [x] Verify upstream tests pass.

### Task 2: Add WinForms App Shell

**Files:**
- Create: `src/AutoSwshRng.App/AutoSwshRng.App.csproj`
- Create: `src/AutoSwshRng.App/Program.cs`
- Create: `src/AutoSwshRng.App/MainForm.cs`
- Modify: `AutoSwshRng.slnx`

- [x] Create a Windows Forms app project.
- [x] Build a `TabControl` with overview, `owoow`, EasyCon, and automation tabs.
- [x] Show adapter data in upstream tabs without launching the original upstream UI.
- [x] Verify the app builds.

### Task 3: Document The Current Architecture

**Files:**
- Create: `docs/architecture.md`
- Modify: `README.md`

- [x] Document direct .NET integration as the first-choice strategy.
- [x] Document `AutoSwshRng.Upstream` as the dependency boundary.
- [x] Document build/test/run commands including the WinForms app.

### Task 4: Final Verification

**Files:**
- Verify all changed files.

- [x] Run `dotnet build .\AutoSwshRng.slnx`.
- [x] Run `dotnet test .\AutoSwshRng.slnx`.
- [x] Run `dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj`.
- [x] Run `git diff --check`.
