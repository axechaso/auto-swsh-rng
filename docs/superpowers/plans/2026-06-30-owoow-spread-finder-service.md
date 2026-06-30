# owoow Spread Finder Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose owoow's Spread Finder as a UI-independent AutoSwshRng service with project-owned requests, results, tests, and a runnable CLI path.

**Architecture:** `AutoSwshRng.Core` owns the domain contracts and has no owoow dependency. `AutoSwshRng.Upstream` maps those contracts to `owoow.Core.RNG.GeneratorConfig` and `owoow.Core.RNG.Generators.Misc.SpreadFinder`, while the CLI demonstrates a complete headless search without referencing WinForms.

**Tech Stack:** C# / .NET 10, NUnit, owoow.Core, existing AutoSwshRng Core/Upstream/CLI projects.

---

### Task 1: Add Project-Owned Spread Finder Contracts

**Files:**
- Create: `src/AutoSwshRng.Core/SpreadFinder/IndividualValueRange.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/IndividualValues.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/SpreadScale.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/SpreadSearchScope.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/SpreadSearchRequest.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/SpreadSearchResult.cs`
- Create: `src/AutoSwshRng.Core/SpreadFinder/ISpreadFinderService.cs`
- Create: `tests/AutoSwshRng.Core.Tests/SpreadFinder/SpreadSearchRequestTests.cs`

- [ ] **Step 1: Write failing domain tests**

Add tests that construct a six-stat request and assert that invalid IV ranges, an IV count other than six, invalid guaranteed-IV counts, empty explicit seed sets, reversed ranges, and invalid partition counts throw `ArgumentOutOfRangeException` or `ArgumentException`. Also assert that mutating source arrays after construction does not mutate a request or seed scope.

- [ ] **Step 2: Run the Core tests and verify RED**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj --filter SpreadSearchRequestTests
```

Expected: compilation fails because `AutoSwshRng.Core.SpreadFinder` contracts do not exist.

- [ ] **Step 3: Implement the domain contracts**

Use these public shapes:

```csharp
public readonly record struct IndividualValueRange
{
    public static IndividualValueRange Any => new(0, 31);

    public byte Minimum { get; }
    public byte Maximum { get; }

    public IndividualValueRange(byte minimum, byte maximum)
    {
        if (minimum > 31)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        if (maximum > 31)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (minimum > maximum)
            throw new ArgumentException("Minimum IV cannot exceed maximum IV.");

        Minimum = minimum;
        Maximum = maximum;
    }
}

public sealed record IndividualValues(byte HP, byte Attack, byte Defense, byte SpecialAttack, byte SpecialDefense, byte Speed);

public enum SpreadScale
{
    Any,
    XXXS,
    XXS,
    XS,
    Small,
    Medium,
    Large,
    XL,
    XXL,
    XXXL,
    MinOrMax,
}

public abstract record SpreadSearchScope
{
    private protected SpreadSearchScope()
    {
    }

    public sealed record Seeds : SpreadSearchScope
    {
        public IReadOnlyList<uint> Values { get; }

        public Seeds(IEnumerable<uint> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            Values = values.ToArray();
            if (Values.Count == 0)
                throw new ArgumentException("At least one seed is required.", nameof(values));
        }
    }

    public sealed record Range : SpreadSearchScope
    {
        public uint Start { get; }
        public uint End { get; }
        public int PartitionCount { get; }

        public Range(uint start, uint end, int partitionCount = 1)
        {
            if (start > end)
                throw new ArgumentException("Start seed cannot exceed end seed.");
            if (partitionCount is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(partitionCount));

            Start = start;
            End = end;
            PartitionCount = partitionCount;
        }
    }

    public sealed record EntireSpace : SpreadSearchScope
    {
        public int PartitionCount { get; }

        public EntireSpace(int partitionCount)
        {
            if (partitionCount is < 1 or > 64)
                throw new ArgumentOutOfRangeException(nameof(partitionCount));

            PartitionCount = partitionCount;
        }
    }
}

public sealed class SpreadSearchRequest
{
    public SpreadSearchScope Scope { get; }
    public IReadOnlyList<IndividualValueRange> IndividualValueRanges { get; }
    public int GuaranteedIndividualValues { get; }
    public bool RareEncryptionConstant { get; }
    public SpreadScale Scale { get; }

    public SpreadSearchRequest(
        SpreadSearchScope scope,
        IEnumerable<IndividualValueRange> individualValueRanges,
        int guaranteedIndividualValues = 0,
        bool rareEncryptionConstant = false,
        SpreadScale scale = SpreadScale.Any)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(individualValueRanges);

        var ranges = individualValueRanges.ToArray();
        if (ranges.Length != 6)
            throw new ArgumentException("Exactly six IV ranges are required.", nameof(individualValueRanges));
        if (guaranteedIndividualValues is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(guaranteedIndividualValues));

        Scope = scope;
        IndividualValueRanges = ranges;
        GuaranteedIndividualValues = guaranteedIndividualValues;
        RareEncryptionConstant = rareEncryptionConstant;
        Scale = scale;
    }
}

public sealed record SpreadSearchResult(
    uint Seed,
    uint EncryptionConstant,
    IndividualValues IndividualValues,
    byte Height,
    SpreadScale Scale);

public interface ISpreadFinderService
{
    Task<IReadOnlyList<SpreadSearchResult>> SearchAsync(
        SpreadSearchRequest request,
        CancellationToken cancellationToken = default);
}
```

`SpreadSearchRequest` validates exactly six IV filters, IV values in `0..31`, `Minimum <= Maximum`, guaranteed IVs in `0..6`, and a non-null scope. Scope constructors validate their own seed/range/partition invariants and copy seed collections so callers cannot mutate an active request.

- [ ] **Step 4: Run Core tests and verify GREEN**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj
```

Expected: all Core tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src\AutoSwshRng.Core\SpreadFinder tests\AutoSwshRng.Core.Tests\SpreadFinder
git commit -m "feat:定义个体帧搜索领域模型"
```

### Task 2: Adapt owoow Seed Searches

**Files:**
- Create: `src/AutoSwshRng.Upstream/OwoowSpreadFinderService.cs`
- Create: `tests/AutoSwshRng.Upstream.Tests/OwoowSpreadFinderServiceTests.cs`

- [ ] **Step 1: Write a failing explicit-seed integration test**

Search the known owoow `FixedSeed.Hex0` seed `0x17033091` with all six IV filters fixed at zero. Assert one project-owned result with seed `0x17033091`, six zero IVs, a parsed hexadecimal encryption constant, and a height/scale matching the original owoow frame.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj --filter OwoowSpreadFinderServiceTests
```

Expected: compilation fails because `OwoowSpreadFinderService` does not exist.

- [ ] **Step 3: Implement explicit seed mapping**

Map project filters to a fresh owoow `GeneratorConfig`:

```csharp
new GeneratorConfig
{
    TargetMinIVs = request.IndividualValueRanges.Select(range => (uint)range.Minimum).ToArray(),
    TargetMaxIVs = request.IndividualValueRanges.Select(range => (uint)range.Maximum).ToArray(),
    GuaranteedIVs = request.GuaranteedIndividualValues,
    RareEC = request.RareEncryptionConstant,
    TargetScale = MapScale(request.Scale),
    FiltersEnabled = true,
};
```

For `SpreadSearchScope.Seeds`, call owoow's list overload and map every `SpreadFinderFrame` into `SpreadSearchResult`. Parse `Seed` and `EC` as invariant hexadecimal values. Parse the numeric height from owoow's final parenthesized value, derive the project-owned `SpreadScale`, sort by numeric seed and descending IVs, and check cancellation before and after the upstream call.

- [ ] **Step 4: Run Upstream tests and verify GREEN**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj --filter OwoowSpreadFinderServiceTests
```

Expected: the explicit-seed test passes.

- [ ] **Step 5: Commit**

```powershell
git add src\AutoSwshRng.Upstream\OwoowSpreadFinderService.cs tests\AutoSwshRng.Upstream.Tests\OwoowSpreadFinderServiceTests.cs
git commit -m "feat:适配owoow个体帧搜索"
```

### Task 3: Support Bounded And Entire-Space Searches

**Files:**
- Modify: `src/AutoSwshRng.Upstream/OwoowSpreadFinderService.cs`
- Modify: `tests/AutoSwshRng.Upstream.Tests/OwoowSpreadFinderServiceTests.cs`

- [ ] **Step 1: Write failing range and sorting tests**

Assert that a range containing `0x17033091` yields the same result as an explicit seed search, that multiple partitions do not duplicate or omit boundary seeds, and that results are sorted by numeric seed followed by descending IVs.

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj --filter OwoowSpreadFinderServiceTests
```

Expected: range tests fail because only explicit seed scopes are supported.

- [ ] **Step 3: Implement range partitioning**

Split inclusive ranges using `ulong` arithmetic so `uint.MaxValue` does not overflow. Call owoow's range overload once per partition, await `Task.WhenAll`, flatten, map, and sort results. `EntireSpace` delegates to range `0..uint.MaxValue`; its explicit partition count makes the CPU cost visible instead of hiding a full-space scan behind a default.

- [ ] **Step 4: Run Upstream tests and verify GREEN**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj
```

Expected: all Upstream tests pass with the existing `System.Drawing.Common` warning only.

- [ ] **Step 5: Commit**

```powershell
git add src\AutoSwshRng.Upstream\OwoowSpreadFinderService.cs tests\AutoSwshRng.Upstream.Tests\OwoowSpreadFinderServiceTests.cs
git commit -m "feat:支持个体帧范围搜索"
```

### Task 4: Add A Headless CLI Search

**Files:**
- Modify: `src/AutoSwshRng.Cli/Program.cs`
- Modify: `tests/AutoSwshRng.Upstream.Tests/OwoowSpreadFinderServiceTests.cs`
- Modify: `README.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: Assert the service is UI-independent**

In `OwoowSpreadFinderServiceTests`, build the explicit-seed request through an `ISpreadFinderService` reference and project-owned models, run it without creating any WinForms control, and assert that the zero-IV fixture returns seed, EC, IV spread, height, and scale.

- [ ] **Step 2: Run the headless integration test**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj --filter OwoowSpreadFinderServiceTests
```

Expected: all service integration tests pass without constructing a `Form` or any other UI type.

- [ ] **Step 3: Implement the CLI command**

Support:

```powershell
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- spread 17033091 0
```

The second argument is an eight-digit hexadecimal fixed seed and the third is the guaranteed-IV count. The command searches that seed with IV ranges `0..31` and prints project-owned result fields. Invalid command arguments return a non-zero exit code and a concise usage line. The existing no-argument smoke output remains unchanged.

- [ ] **Step 4: Document the functional boundary**

README and architecture documentation state that Spread Finder now works headlessly through `ISpreadFinderService`, that owoow is confined to the Upstream adapter, and that UI binding remains intentionally deferred.

- [ ] **Step 5: Verify the headless closure**

Run:

```powershell
dotnet test .\tests\AutoSwshRng.Core.Tests\AutoSwshRng.Core.Tests.csproj
dotnet test .\tests\AutoSwshRng.Upstream.Tests\AutoSwshRng.Upstream.Tests.csproj
dotnet run --project .\src\AutoSwshRng.Cli\AutoSwshRng.Cli.csproj -- spread 17033091 0
git diff --check
```

Expected: tests pass and CLI prints one result for seed `17033091`.

- [ ] **Step 6: Commit**

```powershell
git add src\AutoSwshRng.Cli tests\AutoSwshRng.Upstream.Tests\OwoowSpreadFinderServiceTests.cs README.md docs\architecture.md
git commit -m "feat:增加无界面个体帧搜索入口"
```
