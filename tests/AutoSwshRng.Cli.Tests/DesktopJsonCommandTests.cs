using System.Text.Json;
using System.Text.Json.Nodes;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli.Tests;

public class DesktopJsonCommandTests
{
    private static async Task<(int Code, JsonElement Event)> Run(object request)
    {
        var serialized = JsonSerializer.SerializeToNode(request)!.AsObject();
        var requestId = Guid.NewGuid().ToString("N");
        var runId = serialized["runId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
        var epochId = serialized["epochId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
        var contextRevision = serialized["contextRevision"]?.GetValue<long>() ?? 0;
        serialized["protocolVersion"] = DesktopJsonCommand.ProtocolVersion;
        serialized["requestId"] = requestId;
        serialized["runId"] = runId;
        serialized["epochId"] = epochId;
        serialized["contextRevision"] = contextRevision;
        using var output = new StringWriter();
        var code = await DesktopJsonCommand.RunAsync(new StringReader(serialized.ToJsonString()), output);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            using var item = JsonDocument.Parse(line); // Every progress line is valid JSON.
            Assert.That(item.RootElement.GetProperty("type").GetString(), Is.AnyOf("result", "progress", "error"));
            Assert.That(item.RootElement.GetProperty("requestId").GetString(), Is.EqualTo(requestId));
            Assert.That(item.RootElement.GetProperty("runId").GetString(), Is.EqualTo(runId));
            Assert.That(item.RootElement.GetProperty("epochId").GetString(), Is.EqualTo(epochId));
            Assert.That(item.RootElement.GetProperty("contextRevision").GetInt64(), Is.EqualTo(contextRevision));
        }
        using var document = JsonDocument.Parse(lines[^1]);
        return (code, document.RootElement.Clone());
    }

    [TestCase(EncounterKind.Static)]
    [TestCase(EncounterKind.Symbol)]
    [TestCase(EncounterKind.Hidden)]
    [TestCase(EncounterKind.Fishing)]
    public async Task SearchMatchesExistingService(EncounterKind kind)
    {
        var (_, options) = await Run(new { operation = "catalog", game = "Sword", kind = kind.ToString() });
        var catalog = options.GetProperty("data");
        var area = catalog.GetProperty("area").GetString()!;
        var weather = catalog.GetProperty("weather").GetString()!;
        var species = kind == EncounterKind.Static ? catalog.GetProperty("species")[0].GetString() : null;
        var expected = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321), 0, 30,
            new EncounterCatalogRequest(GameVersion.Sword, kind, area, weather),
            new RngProfile("Desktop", GameVersion.Sword, 1337, 1390, true, false),
            new EncounterFilter(targetSpecies: species), OverworldEnvironmentSettings.None, auraKnockouts: 0));
        var (code, result) = await Run(new { operation = "search", game = "Sword", kind = kind.ToString(), area, weather, species,
            seed0 = "123456789ABCDEF0", seed1 = "0FEDCBA987654321", start = 0, end = 30, tid = 1337, sid = 1390, shinyCharm = true });
        Assert.That(code, Is.Zero, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("data").GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("data").GetProperty("searchedStart").GetUInt64(), Is.Zero);
            Assert.That(result.GetProperty("data").GetProperty("searchedEnd").GetUInt64(), Is.EqualTo(30));
        });
        var actual = result.GetProperty("data").GetProperty("rows").EnumerateArray().ToArray();
        Assert.That(actual, Has.Length.EqualTo(expected.Count));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[i].GetProperty("advance").GetUInt64(), Is.EqualTo(expected[i].Advance));
                Assert.That(actual[i].GetProperty("pid").GetString(), Is.EqualTo(expected[i].PersonalityId.ToString("X8")));
                Assert.That(actual[i].GetProperty("ivs").EnumerateArray().Select(v => v.GetByte()), Is.EqualTo(expected[i].IndividualValues.ToArray()));
                Assert.That(actual[i].GetProperty("seed0").GetString(), Is.EqualTo(expected[i].State.Seed0.ToString("X16")));
            });
        }
    }

    [Test]
    public async Task ForwardAndReversePreserveFull64BitState()
    {
        var (_, forward) = await Run(new { operation = "rng", seed0 = "FFFFFFFFFFFFFFFF", seed1 = "FEDCBA9876543210", amount = 123 });
        var state = forward.GetProperty("data");
        var (code, reverse) = await Run(new { operation = "rng", seed0 = state.GetProperty("seed0").GetString(), seed1 = state.GetProperty("seed1").GetString(), amount = 123, reverse = true });
        Assert.That(code, Is.Zero);
        Assert.That(reverse.GetProperty("data").GetProperty("seed0").GetString(), Is.EqualTo("FFFFFFFFFFFFFFFF"));
        Assert.That(reverse.GetProperty("data").GetProperty("seed1").GetString(), Is.EqualTo("FEDCBA9876543210"));
    }

    [TestCase("0", "0")]
    [TestCase("xyz", "1")]
    [TestCase("10000000000000000", "1")]
    public async Task RejectsInvalidSeed(string seed0, string seed1)
    {
        var (code, result) = await Run(new { operation = "rng", seed0, seed1 });
        Assert.That(code, Is.EqualTo(1));
        Assert.That(result.GetProperty("type").GetString(), Is.EqualTo("error"));
    }

    [Test]
    public async Task RejectsOversizedSearchBeforeStartingGenerator()
    {
        var (code, result) = await Run(new { operation = "search", seed0 = "1", seed1 = "2", start = 0, end = 100000 });
        Assert.That(code, Is.EqualTo(1));
        Assert.That(result.GetProperty("message").GetString(), Does.Contain("100,000"));
    }

    [Test]
    public async Task MalformedRequestIsStructuredError()
    {
        using var output = new StringWriter();
        Assert.That(await DesktopJsonCommand.RunAsync(new StringReader("{bad"), output), Is.EqualTo(1));
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("error"));
    }

    [Test]
    public async Task CapabilitiesAdvertiseProtocolAndObservationSemantics()
    {
        var (code, result) = await Run(new { operation = "capabilities" });
        var data = result.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero);
            Assert.That(result.GetProperty("protocolVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(data.GetProperty("protocolVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(
                data.GetProperty("boundarySemanticsVersion").GetString(),
                Is.EqualTo(OwoowRetailSeedService.BoundarySemanticsVersion));
            Assert.That(
                data.GetProperty("operations").EnumerateArray().Select(value => value.GetString()),
                Does.Contain("seed.locate").And.Contain("encounter.search")
                    .And.Contain("calibration.probe.evaluate").And.Contain("encounter.reverse"));
        });
    }

    [Test]
    public async Task PagedSearchKeepsCandidateCursorBoundToStableSnapshot()
    {
        var (_, catalogEvent) = await Run(new { operation = "catalog", game = "Sword", kind = "Symbol" });
        var catalog = catalogEvent.GetProperty("data");
        var area = catalog.GetProperty("area").GetString()!;
        var weather = catalog.GetProperty("weather").GetString()!;
        var common = new
        {
            operation = "encounter.search",
            game = "Sword",
            kind = "Symbol",
            area,
            weather,
            seed0 = "123456789ABCDEF0",
            seed1 = "0FEDCBA987654321",
            start = 0,
            end = 10_000,
            scanCursor = 0,
            scanLimit = 10_000,
            candidatePageSize = 5,
            runId = "stable-run",
            epochId = "stable-epoch",
            contextRevision = 7,
        };
        var (firstCode, firstEvent) = await Run(common);
        var first = firstEvent.GetProperty("data");
        Assert.That(firstCode, Is.Zero, firstEvent.ToString());
        Assert.That(first.GetProperty("candidatePageComplete").GetBoolean(), Is.False, first.ToString());
        var nextCandidate = first.GetProperty("nextCandidateCursor").GetInt32();
        var (secondCode, secondEvent) = await Run(new
        {
            common.operation,
            common.game,
            common.kind,
            common.area,
            common.weather,
            common.seed0,
            common.seed1,
            common.start,
            common.end,
            common.scanCursor,
            common.scanLimit,
            common.candidatePageSize,
            common.runId,
            common.epochId,
            common.contextRevision,
            candidateCursor = nextCandidate,
            snapshotId = first.GetProperty("snapshotId").GetString(),
            requestDigest = first.GetProperty("requestDigest").GetString(),
        });
        var second = secondEvent.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(secondCode, Is.Zero, secondEvent.ToString());
            Assert.That(second.GetProperty("snapshotId").GetString(), Is.EqualTo(first.GetProperty("snapshotId").GetString()));
            Assert.That(second.GetProperty("requestDigest").GetString(), Is.EqualTo(first.GetProperty("requestDigest").GetString()));
            Assert.That(second.GetProperty("candidateStart").GetInt32(), Is.EqualTo(nextCandidate));
            Assert.That(second.GetProperty("rows").GetArrayLength(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task CalibrationProbeReportsExactSyntheticParameterMatchAsPendingHardware()
    {
        var initial = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        var expected = await new OwoowXoroshiroService().AdvanceAsync(initial, 7);
        var (code, result) = await Run(new
        {
            operation = "calibration.probe.evaluate",
            seed0 = initial.Seed0.ToString("X16"),
            seed1 = initial.Seed1.ToString("X16"),
            observedSeed0 = expected.Seed0.ToString("X16"),
            observedSeed1 = expected.Seed1.ToString("X16"),
            sourceKind = "synthetic",
            probeActions = new[] { new { kind = "interference" } },
            probeCandidates = new[]
            {
                new { candidateId = "n0", interferenceAdvances = 3UL },
                new { candidateId = "n1", interferenceAdvances = 7UL },
            },
        });
        var data = result.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, result.ToString());
            Assert.That(data.GetProperty("status").GetString(), Is.EqualTo("Unique"));
            Assert.That(data.GetProperty("matchedCandidateCount").GetInt32(), Is.EqualTo(1));
            Assert.That(data.GetProperty("candidates")[1].GetProperty("matches").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("experimentReport").GetProperty("evidenceStatus").GetString(), Is.EqualTo("PendingEvidence"));
            Assert.That(data.GetProperty("experimentReport").GetProperty("hardwareStatus").GetString(), Is.EqualTo("PendingHardwareValidation"));
        });
    }

    [TestCase(1, 5UL)]
    [TestCase(-1, 7UL)]
    public async Task AttemptPlanSolvesBoundaryEquationAndReservesCheckpoints(int boundaryOffset, ulong expectedTrigger)
    {
        var (code, result) = await Run(new
        {
            operation = "attempt.plan",
            seed0 = "123456789ABCDEF0",
            seed1 = "0FEDCBA987654321",
            currentAdvance = 2UL,
            targetCandidates = new[]
            {
                new { candidateId = "target-8", generationAdvance = 8UL },
            },
            targetSnapshotId = "snapshot-verified",
            targetRequestDigest = new string('a', 64),
            boundaryAdjustment = new
            {
                boundaryId = "battle.entry",
                offset = boundaryOffset,
                modelVersion = "boundary-v1",
                revision = 3,
            },
            attemptPlanning = new
            {
                maximumPlanEvaluations = 100,
                coarseBatchSize = 2UL,
                maximumCoarseBatches = 10,
                relocationReserve = 1UL,
                preciseReserve = 1UL,
            },
            probeActions = new[] { new { kind = "interference" } },
            probeCandidates = new[]
            {
                new { candidateId = "npc-calibrated", interferenceAdvances = 2UL },
            },
        });
        var data = result.GetProperty("data");
        var plan = data.GetProperty("plans")[0];

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, result.ToString());
            Assert.That(data.GetProperty("status").GetString(), Is.EqualTo("Feasible"));
            Assert.That(data.GetProperty("targetSnapshotId").GetString(), Is.EqualTo("snapshot-verified"));
            Assert.That(data.GetProperty("targetRequestDigest").GetString(), Is.EqualTo(new string('a', 64)));
            Assert.That(plan.GetProperty("triggerAdvance").GetUInt64(), Is.EqualTo(expectedTrigger));
            Assert.That(plan.GetProperty("predictedGenerationAdvance").GetUInt64(), Is.EqualTo(8));
            Assert.That(plan.GetProperty("boundaryOffset").GetInt32(), Is.EqualTo(boundaryOffset));
            Assert.That(plan.GetProperty("coarseAdvances").GetUInt64(), Is.EqualTo(expectedTrigger - 4));
            Assert.That(plan.GetProperty("checkpointRequiredAfterEachBatch").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("hardwareStatus").GetString(), Is.EqualTo("PendingHardwareValidation"));
        });
    }

    [Test]
    public async Task AttemptPlanReevaluatesMenuCloseAtEachCandidateRngState()
    {
        var anchor = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        const ulong expectedTrigger = 5;
        const uint npcCount = 4;
        var triggerState = await new OwoowXoroshiroService().AdvanceAsync(anchor, expectedTrigger);
        var menu = await new OwoowCalibrationService().CalculateMenuCloseAsync(
            new MenuCloseCalibrationRequest(triggerState, npcCount, false, Weather.Normal));
        const int boundaryOffset = 1;
        var generationAdvance = expectedTrigger + menu.Advances + (ulong)boundaryOffset;
        var (code, result) = await Run(new
        {
            operation = "attempt.plan",
            seed0 = anchor.Seed0.ToString("X16"),
            seed1 = anchor.Seed1.ToString("X16"),
            currentAdvance = 2UL,
            targetCandidates = new[]
            {
                new { candidateId = "target-menu", generationAdvance },
            },
            targetSnapshotId = "snapshot-menu",
            targetRequestDigest = new string('c', 64),
            boundaryAdjustment = new
            {
                boundaryId = "menu.close",
                offset = boundaryOffset,
                modelVersion = "menu-model-v1",
                revision = 1,
            },
            attemptPlanning = new
            {
                maximumPlanEvaluations = 100,
                coarseBatchSize = 2UL,
                maximumCoarseBatches = 10,
                relocationReserve = 1UL,
                preciseReserve = 1UL,
            },
            probeActions = new[] { new { kind = "menuclose", weather = Weather.Normal.ToString() } },
            probeCandidates = new[]
            {
                new { candidateId = "npc-4", menuCloseNonPlayerCharacters = npcCount },
            },
        });
        var data = result.GetProperty("data");
        var plan = data.GetProperty("plans").EnumerateArray()
            .SingleOrDefault(candidate => candidate.GetProperty("triggerAdvance").GetUInt64() == expectedTrigger);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, result.ToString());
            Assert.That(plan.ValueKind, Is.Not.EqualTo(JsonValueKind.Undefined));
            Assert.That(plan.GetProperty("modeledPreTriggerAdvances").GetUInt32(), Is.EqualTo(menu.Advances));
            Assert.That(plan.GetProperty("predictedGenerationAdvance").GetUInt64(), Is.EqualTo(generationAdvance));
        });
    }

    [Test]
    public async Task AttemptPlanReportsBudgetFailureAndRejectsIncompleteEnumeration()
    {
        var request = new
        {
            operation = "attempt.plan",
            seed0 = "123456789ABCDEF0",
            seed1 = "0FEDCBA987654321",
            currentAdvance = 4UL,
            targetCandidates = new[] { new { candidateId = "target-8", generationAdvance = 8UL } },
            targetSnapshotId = "snapshot-verified",
            targetRequestDigest = new string('b', 64),
            boundaryAdjustment = new { boundaryId = "battle.entry", offset = 1, modelVersion = "v1", revision = 0 },
            attemptPlanning = new
            {
                maximumPlanEvaluations = 100,
                coarseBatchSize = 2UL,
                maximumCoarseBatches = 10,
                relocationReserve = 2UL,
                preciseReserve = 1UL,
            },
            probeActions = new[] { new { kind = "interference" } },
            probeCandidates = new[] { new { candidateId = "npc", interferenceAdvances = 2UL } },
        };

        var (code, result) = await Run(request);
        var capped = await Run(new
        {
            operation = request.operation,
            seed0 = request.seed0,
            seed1 = request.seed1,
            currentAdvance = request.currentAdvance,
            targetCandidates = request.targetCandidates,
            targetSnapshotId = request.targetSnapshotId,
            targetRequestDigest = request.targetRequestDigest,
            boundaryAdjustment = request.boundaryAdjustment,
            attemptPlanning = new
            {
                maximumPlanEvaluations = 2,
                coarseBatchSize = 2UL,
                maximumCoarseBatches = 10,
                relocationReserve = 2UL,
                preciseReserve = 1UL,
            },
            probeActions = request.probeActions,
            probeCandidates = request.probeCandidates,
        });

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, result.ToString());
            Assert.That(result.GetProperty("data").GetProperty("status").GetString(), Is.EqualTo("InsufficientExecutionBudget"));
            Assert.That(result.GetProperty("data").GetProperty("plans").GetArrayLength(), Is.Zero);
            Assert.That(capped.Code, Is.EqualTo(1));
            Assert.That(capped.Event.GetProperty("message").GetString(), Does.Contain("maximumPlanEvaluations"));
        });
    }

    [Test]
    public async Task ReverseLookupUsesObservedFieldsAndNeverClaimsSyntheticCalibrationEvidence()
    {
        var (_, catalogEvent) = await Run(new { operation = "catalog", game = "Sword", kind = "Symbol" });
        var catalog = catalogEvent.GetProperty("data");
        var area = catalog.GetProperty("area").GetString()!;
        var weather = catalog.GetProperty("weather").GetString()!;
        var state = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        var context = new EncounterCatalogRequest(GameVersion.Sword, EncounterKind.Symbol, area, weather);
        var profile = new RngProfile("Reverse diagnostic", GameVersion.Sword, 0, 0, false, false);
        var generated = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            state, 0, 50, context, profile, EncounterFilter.Any,
            OverworldEnvironmentSettings.None, auraKnockouts: 0));
        var observed = generated.First();
        var exactIvs = observed.IndividualValues.ToArray().Select(value => new[] { (int)value, (int)value }).ToArray();
        var (code, result) = await Run(new
        {
            operation = "encounter.reverse",
            game = "Sword",
            kind = "Symbol",
            area,
            weather,
            seed0 = state.Seed0.ToString("X16"),
            seed1 = state.Seed1.ToString("X16"),
            start = observed.Advance,
            end = observed.Advance,
            species = observed.Species,
            levelMinimum = (int)observed.Level,
            levelMaximum = (int)observed.Level,
            nature = observed.Nature,
            ability = observed.Ability,
            gender = observed.Gender.ToString(),
            ivs = exactIvs,
            sourceKind = "synthetic",
            evidenceIds = new[] { "replay:frame:9" },
        });
        var data = result.GetProperty("data");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero, result.ToString());
            Assert.That(data.GetProperty("sourceKind").GetString(), Is.EqualTo("synthetic"));
            Assert.That(data.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("candidateGenerationAdvances")[0].GetUInt64(), Is.EqualTo(observed.Advance));
            var reverseCandidate = data.GetProperty("candidates")[0];
            Assert.That(reverseCandidate.GetProperty("height").GetString(), Is.Not.Empty);
            Assert.That(reverseCandidate.GetProperty("heightDescription").GetString(), Is.EqualTo(observed.HeightDescription));
            Assert.That(reverseCandidate.GetProperty("shiny").GetString(), Is.AnyOf("Star", "Square", "None"));
            Assert.That(data.GetProperty("eligibleForCalibration").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("experimentReport").GetProperty("hardwareStatus").GetString(), Is.EqualTo("PendingHardwareValidation"));
        });
    }

    [Test]
    public async Task SeedSolveAndVerifyPreservePost128ObservationBoundary()
    {
        var seeds = new OwoowRetailSeedService();
        var anchor = new RngState(0x123456789ABCDEF0, owoow.Core.RNG.Util.XOROSHIRO_CONST);
        var first = await seeds.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 0, 128));
        var second = await seeds.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, 128, 32));
        var bits = string.Concat(first.Observations.Select(value => value.ToString()));
        var (solveCode, solvedEvent) = await Run(new { operation = "seed.solve", observations = bits });
        var solved = solvedEvent.GetProperty("data");
        var after = solved.GetProperty("stateAfterObservations");
        var (verifyCode, verifiedEvent) = await Run(new
        {
            operation = "seed.verify",
            seed0 = after.GetProperty("seed0").GetString(),
            seed1 = after.GetProperty("seed1").GetString(),
            observations = string.Concat(second.Observations.Select(value => value.ToString())),
        });

        Assert.Multiple(() =>
        {
            Assert.That(solveCode, Is.Zero);
            Assert.That(solved.GetProperty("observationCount").GetInt32(), Is.EqualTo(128));
            Assert.That(solved.GetProperty("rawStateBoundary").GetString(), Is.EqualTo("StateAfterLastObservation"));
            Assert.That(after.GetProperty("seed0").GetString(), Is.EqualTo(second.InitialState.Seed0.ToString("X16")));
            Assert.That(after.GetProperty("seed1").GetString(), Is.EqualTo(second.InitialState.Seed1.ToString("X16")));
            Assert.That(verifyCode, Is.Zero);
            Assert.That(verifiedEvent.GetProperty("data").GetProperty("matches").GetBoolean(), Is.True);
            Assert.That(solved.GetProperty("experimentReport").GetProperty("sourceKind").GetString(), Is.EqualTo("unknown"));
            Assert.That(solved.GetProperty("experimentReport").GetProperty("hardwareStatus").GetString(), Is.EqualTo("PendingHardwareValidation"));
        });
    }

    [Test]
    public async Task RelocationWireContractReturnsObservedEndPosition()
    {
        var seeds = new OwoowRetailSeedService();
        var anchor = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        const int firstAdvance = 17;
        const int length = 32;
        var sample = await seeds.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(anchor, firstAdvance, length));
        var bits = string.Concat(sample.Observations.Select(value => value.ToString()));
        var (code, result) = await Run(new
        {
            operation = "seed.locate",
            seed0 = anchor.Seed0.ToString("X16"),
            seed1 = anchor.Seed1.ToString("X16"),
            start = firstAdvance,
            end = firstAdvance,
            observations = bits,
        });
        var data = result.GetProperty("data");
        var candidate = data.GetProperty("candidates")[0];

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.Zero);
            Assert.That(data.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("candidateCount").GetInt32(), Is.EqualTo(1));
            Assert.That(candidate.GetProperty("firstObservedAdvance").GetUInt64(), Is.EqualTo(firstAdvance));
            Assert.That(candidate.GetProperty("stateAfterObservedAdvance").GetUInt64(), Is.EqualTo(firstAdvance + length));
            Assert.That(
                candidate.GetProperty("stateBeforeObservations").GetProperty("seed0").GetString(),
                Is.EqualTo(sample.InitialState.Seed0.ToString("X16")));
        });
    }
}
