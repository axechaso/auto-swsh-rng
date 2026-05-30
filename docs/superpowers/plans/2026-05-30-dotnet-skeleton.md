# Dotnet Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first .NET 10 solution skeleton for `auto-swsh-rng` with Core, CLI, and tests.

**Architecture:** Keep the first milestone small: a library owns project metadata and the CLI consumes it. This proves the repository can build, test, and run as a C#/.NET workspace before deeper `owoow` or EasyCon integration.

**Tech Stack:** C# 13 / .NET 10, console CLI, NUnit tests, Windows PowerShell commands.

---

### Task 1: Create Solution And Projects

**Files:**
- Create: `AutoSwshRng.slnx`
- Create: `src/AutoSwshRng.Core/AutoSwshRng.Core.csproj`
- Create: `src/AutoSwshRng.Cli/AutoSwshRng.Cli.csproj`
- Create: `tests/AutoSwshRng.Core.Tests/AutoSwshRng.Core.Tests.csproj`

- [x] **Step 1: Generate the solution and project shells**

Run:

```powershell
dotnet new sln -n AutoSwshRng
dotnet new classlib -n AutoSwshRng.Core -o .\src\AutoSwshRng.Core -f net10.0
dotnet new console -n AutoSwshRng.Cli -o .\src\AutoSwshRng.Cli -f net10.0
dotnet new nunit -n AutoSwshRng.Core.Tests -o .\tests\AutoSwshRng.Core.Tests -f net10.0
dotnet sln .\AutoSwshRng.slnx add .\src\AutoSwshRng.Core\AutoSwshRng.Core.csproj
dotnet sln .\AutoSwshRng.slnx add .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj
dotnet sln .\AutoSwshRng.slnx add .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj
dotnet add .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj reference .\src\AutoSwshRng.Core\AutoSwshRng.Core.csproj
dotnet add .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj reference .\src\AutoSwshRng.Core\AutoSwshRng.Core.csproj
```

Expected: all commands exit 0 and create project files.

### Task 2: Add Project Metadata With TDD

**Files:**
- Modify: `tests/AutoSwshRng.Core.Tests/UnitTest1.cs`
- Create: `src/AutoSwshRng.Core/ProjectInfo.cs`

- [x] **Step 1: Write the failing test**

```csharp
namespace AutoSwshRng.Core.Tests;

public class ProjectInfoTests
{
    [Test]
    public void MetadataDescribesTheProject()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectInfo.Name, Is.EqualTo("auto-swsh-rng"));
            Assert.That(ProjectInfo.Description, Is.EqualTo("尝试剑盾乱数自动化"));
        });
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj`

Expected: fails because `ProjectInfo` is not defined.

- [x] **Step 3: Write minimal implementation**

```csharp
namespace AutoSwshRng.Core;

public static class ProjectInfo
{
    public const string Name = "auto-swsh-rng";
    public const string Description = "尝试剑盾乱数自动化";
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj`

Expected: pass.

### Task 3: Wire CLI To Core

**Files:**
- Modify: `src/AutoSwshRng.Cli/Program.cs`
- Modify: `README.md`

- [x] **Step 1: Implement minimal CLI output**

```csharp
using AutoSwshRng.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"{ProjectInfo.Name}: {ProjectInfo.Description}");
```

- [x] **Step 2: Verify CLI output**

Run: `dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj`

Expected: prints `auto-swsh-rng: 尝试剑盾乱数自动化`.

- [x] **Step 3: Document first developer commands**

Add build, test, and CLI run commands to `README.md`.

### Task 4: Final Verification

**Files:**
- Verify all changed files.

- [x] **Step 1: Run full build**

Run: `dotnet build .\AutoSwshRng.slnx`

Expected: build succeeds.

- [x] **Step 2: Run full tests**

Run: `dotnet test .\AutoSwshRng.slnx`

Expected: tests pass.

- [x] **Step 3: Run whitespace check**

Run: `git diff --check`

Expected: no output and exit 0.
