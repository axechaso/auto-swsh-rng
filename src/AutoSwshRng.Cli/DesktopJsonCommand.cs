using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli;

/// <summary>One read-only desktop request per process; stdout is UTF-8 JSON lines.</summary>
public static class DesktopJsonCommand
{
    public const int ProtocolVersion = 2;
    private static readonly string AlgorithmCommit = ReadAlgorithmCommit();

    private static string ReadAlgorithmCommit()
    {
        using var stream = typeof(DesktopJsonCommand).Assembly.GetManifestResourceStream("DesktopUpstream.json")!;
        using var manifest = JsonDocument.Parse(stream);
        return manifest.RootElement.GetProperty("algorithm").GetProperty("commit").GetString()!;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(TextReader input, TextWriter output)
    {
        DesktopRequest? request = null;
        try
        {
            var line = await input.ReadLineAsync() ?? throw new ArgumentException("请求为空。");
            request = JsonSerializer.Deserialize<DesktopRequest>(line, Json)
                ?? throw new ArgumentException("请求为空。");
            if (request.ProtocolVersion != ProtocolVersion)
                throw new ArgumentException($"计算协议版本不兼容：需要 {ProtocolVersion}，收到 {request.ProtocolVersion}。请更新桌面与计算服务。");
            ValidateRequestIdentity(request);
            var result = await ExecuteAsync(request, new JsonProgress(output, request));
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                type = "result",
                protocolVersion = ProtocolVersion,
                requestId = request.RequestId,
                runId = request.RunId,
                epochId = request.EpochId,
                contextRevision = request.ContextRevision,
                algorithmCommit = AlgorithmCommit,
                data = result,
            }, Json));
            return 0;
        }
        catch (Exception exception)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                type = "error",
                protocolVersion = ProtocolVersion,
                requestId = request?.RequestId,
                runId = request?.RunId,
                epochId = request?.EpochId,
                contextRevision = request?.ContextRevision,
                message = exception.Message,
            }, Json));
            return 1;
        }
    }

    private static async Task<object> ExecuteAsync(DesktopRequest r, IProgress<OperationProgress> progress)
    {
        if (r.Operation == "capabilities")
        {
            return new
            {
                protocolVersion = ProtocolVersion,
                algorithmCommit = AlgorithmCommit,
                boundarySemanticsVersion = OwoowRetailSeedService.BoundarySemanticsVersion,
                operations = new[]
                {
                    "capabilities", "catalog", "search", "rng", "rng.advance", "rng.distance",
                    "seed.solve", "seed.verify", "seed.locate", "encounter.search",
                    "calibration.probe.evaluate", "attempt.plan", "encounter.reverse",
                },
                limits = new
                {
                    seedObservationBits = 128,
                    maximumVerificationBits = 4096,
                    maximumRelocationStarts = AnimationRelocationRequest.MaximumWindowStarts,
                    maximumDistanceSearch = RngDistanceRequest.MaximumSupportedDistance,
                    maximumSearchPositions = 100_000,
                    maximumReturnedRows = 10_000,
                },
            };
        }

        if (r.Operation is "seed.solve" or "seed.verify" or "seed.locate"
            or "rng.advance" or "rng.distance")
        {
            return await ExecuteObservationOperationAsync(r).ConfigureAwait(false);
        }
        if (r.Operation == "calibration.probe.evaluate")
            return await ExecuteCalibrationProbeAsync(r).ConfigureAwait(false);
        if (r.Operation == "attempt.plan")
            return await ExecuteAttemptPlanAsync(r, progress).ConfigureAwait(false);
        if (r.Operation == "encounter.reverse")
            return await ExecuteEncounterReverseAsync(r, progress).ConfigureAwait(false);

        var catalog = new OwoowEncounterCatalogService();
        if (r.Operation == "catalog")
        {
            var areas = await catalog.GetAreasAsync(r.Game, r.Kind);
            var area = areas.Contains(r.Area) ? r.Area : areas.FirstOrDefault();
            var weathers = area is null ? [] : await catalog.GetWeatherAsync(r.Game, r.Kind, area);
            var weather = weathers.Contains(r.Weather) ? r.Weather : weathers.FirstOrDefault();
            var species = area is null || weather is null ? []
                : await catalog.GetSpeciesAsync(r.Game, r.Kind, area, weather);
            return new { areas, area, weathers, weather, species };
        }

        var state = ParseState(r.Seed0, r.Seed1);
        if (r.Operation == "rng")
        {
            if (r.Amount is < 1 or > 1_000_000)
                throw new ArgumentException("推进数必须在 1～1,000,000 之间。");
            var result = await new OwoowXoroshiroService().CalculateAsync(new XoroshiroRequest(
                state, r.Reverse ? XoroshiroOperation.Previous : XoroshiroOperation.Next, r.Amount));
            return new { seed0 = result.State.Seed0.ToString("X16"), seed1 = result.State.Seed1.ToString("X16"), result.Distance };
        }
        if (r.Operation is not ("search" or "encounter.search"))
            throw new ArgumentException("未知操作。");
        var pagedSearch = r.Operation == "encounter.search";
        if (r.End < r.Start || r.End > 1_000_000_000)
            throw new ArgumentException("结束推进数不能小于起始值；最大推进数为 1,000,000,000。");
        var searchedStart = pagedSearch ? r.ScanCursor ?? r.Start : r.Start;
        if (searchedStart < r.Start || searchedStart > r.End)
            throw new ArgumentException("scanCursor 必须位于请求搜索范围内。");
        if (pagedSearch && r.ScanLimit is < 1 or > 100_000)
            throw new ArgumentException("scanLimit 必须为 1～100,000。");
        var scanLimit = pagedSearch ? (ulong)r.ScanLimit : 100_000UL;
        var searchedEnd = pagedSearch
            ? Math.Min(r.End, searchedStart + Math.Min(scanLimit - 1, 1_000_000_000UL - searchedStart))
            : r.End;
        if (!pagedSearch && r.End - r.Start >= 100_000)
            throw new ArgumentException("旧 search 操作单次最多搜索 100,000 帧，请使用 encounter.search 分页。");
        if (pagedSearch && (r.CandidatePageSize is < 1 or > 10_000 || r.CandidateCursor < 0))
            throw new ArgumentException("candidatePageSize 必须为 1～10,000，candidateCursor 不能为负数。");
        var context = new EncounterCatalogRequest(
            r.Game, r.Kind, r.Area ?? "", r.Weather ?? "", r.LeadAbility ?? "");
        // Validate the exact context before invoking the generator, which assumes a valid table.
        await catalog.GetTableAsync(context);
        var availableSpecies = await catalog.GetSpeciesAsync(r.Game, r.Kind, context.Area, context.Weather);
        if (r.Species is not null && !availableSpecies.Contains(r.Species))
            throw new ArgumentException("所选宝可梦不在当前遭遇表中。");
        if (r.Ivs is null || r.Ivs.Length != 6 || r.Ivs.Any(pair => pair is null || pair.Length != 2))
            throw new ArgumentException("需要六项个体值范围。");
        var filter = new EncounterFilter(targetSpecies: r.Species, shiny: r.Shiny, aura: r.Aura,
            individualValues: r.Ivs.Select(pair => new IndividualValueConstraint(r.IvMatch, pair[0], pair[1])),
            targetNature: r.Nature, targetAbility: r.Ability, targetGender: r.Gender,
            rareEncryptionConstant: r.RareEncryptionConstant,
            height: r.Height,
            mark: new MarkFilter(r.Mark, r.SpecificMark));
        var profile = new RngProfile("Desktop", r.Game, r.Tid, r.Sid, r.ShinyCharm, r.MarkCharm);
        var environment = new OverworldEnvironmentSettings(
            r.ConsiderMenuClose,
            r.MenuCloseNonPlayerCharacters,
            r.HoldDirection,
            r.ConsiderFlying,
            r.AreaLoadAdvances,
            r.AreaLoadNonPlayerCharacters,
            r.ConsiderRain,
            r.RainTicksDuringAreaLoad,
            r.RainTicksBeforeEncounter);
        var results = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            state, searchedStart, searchedEnd, context, profile, filter, environment,
            auraKnockouts: r.Knockouts,
            hiddenMaximumStep: r.HiddenStep,
            dexRecommendationSlots: r.DexRecommendationSlots), progress);
        if (pagedSearch)
        {
            const string orderVersion = "advance-fields-v1";
            var ordered = OrderCandidates(results);
            var (snapshotId, requestDigest) = BuildSearchIdentity(
                r, state, searchedStart, searchedEnd, orderVersion);
            if (r.CandidateCursor > ordered.Count)
                throw new ArgumentException("candidateCursor 超出该搜索快照的候选数量。");
            if (r.CandidateCursor > 0 || r.SnapshotId is not null || r.RequestDigest is not null)
            {
                if (r.SnapshotId != snapshotId || r.RequestDigest != requestDigest)
                    throw new ArgumentException("搜索快照已变化，旧候选游标失效；请从 scanCursor 重新搜索。");
            }
            var page = ordered.Skip(r.CandidateCursor).Take(r.CandidatePageSize).ToArray();
            var candidatePageComplete = r.CandidateCursor + page.Length >= ordered.Count;
            var nextCandidateCursor = candidatePageComplete ? (int?)null : r.CandidateCursor + page.Length;
            var nextScanCursor = searchedEnd < r.End ? (ulong?)(searchedEnd + 1) : null;
            return new
            {
                searchedStart,
                searchedEnd,
                candidateStart = r.CandidateCursor,
                searchedPositions = searchedEnd - searchedStart + 1,
                scanComplete = searchedEnd == r.End,
                nextScanCursor,
                candidateTotal = ordered.Count,
                candidatePageComplete,
                nextCandidateCursor,
                snapshotId,
                requestDigest,
                orderVersion,
                rows = FormatCandidateRows(page),
            };
        }
        const int limit = 10_000;
        return new
        {
            complete = true,
            searchedStart = r.Start,
            searchedEnd = r.End,
            total = results.Count,
            truncated = results.Count > limit,
            rows = results.Take(limit).Select(row => new
            {
                row.Advance,
                row.Species,
                row.Level,
                row.Shiny,
                row.Nature,
                row.Ability,
                row.Gender,
                ivs = row.IndividualValues.ToArray().Select(value => (int)value).ToArray(),
                row.Mark,
                row.BrilliantAura,
                row.Height,
                ec = row.EncryptionConstant.ToString("X8"),
                pid = row.PersonalityId.ToString("X8"),
                seed0 = row.State.Seed0.ToString("X16"),
                seed1 = row.State.Seed1.ToString("X16"),
            }).ToArray(),
        };
    }

    private static IReadOnlyList<OverworldEncounterResult> OrderCandidates(
        IReadOnlyList<OverworldEncounterResult> results) => results
        .OrderBy(row => row.Advance)
        .ThenBy(row => row.Species, StringComparer.Ordinal)
        .ThenBy(row => row.Level)
        .ThenBy(row => row.Shiny, StringComparer.Ordinal)
        .ThenBy(row => row.Nature, StringComparer.Ordinal)
        .ThenBy(row => row.Ability, StringComparer.Ordinal)
        .ThenBy(row => row.Gender)
        .ThenBy(row => string.Join(',', row.IndividualValues.ToArray()))
        .ThenBy(row => row.Mark, StringComparer.Ordinal)
        .ThenBy(row => row.EncryptionConstant)
        .ThenBy(row => row.PersonalityId)
        .ThenBy(row => row.Height)
        .ThenBy(row => row.State.Seed0)
        .ThenBy(row => row.State.Seed1)
        .ThenBy(row => row.Jump)
        .ThenBy(row => row.Step)
        .ThenBy(row => row.Animation)
        .ToArray();

    private static object[] FormatCandidateRows(IReadOnlyList<OverworldEncounterResult> rows)
    {
        var ordinalsByAdvance = new Dictionary<ulong, int>();
        return rows.Select(row =>
        {
            ordinalsByAdvance.TryGetValue(row.Advance, out var ordinal);
            ordinalsByAdvance[row.Advance] = ordinal + 1;
            return (object)new
            {
                row.Advance,
                candidateOrdinal = ordinal,
                row.Species,
                row.Level,
                row.Shiny,
                row.Nature,
                row.Ability,
                row.Gender,
                ivs = row.IndividualValues.ToArray().Select(value => (int)value).ToArray(),
                row.Mark,
                row.BrilliantAura,
                row.Height,
                ec = row.EncryptionConstant.ToString("X8"),
                pid = row.PersonalityId.ToString("X8"),
                seed0 = row.State.Seed0.ToString("X16"),
                seed1 = row.State.Seed1.ToString("X16"),
            };
        }).ToArray();
    }

    private static (string SnapshotId, string RequestDigest) BuildSearchIdentity(
        DesktopRequest request,
        RngState state,
        ulong searchedStart,
        ulong searchedEnd,
        string orderVersion)
    {
        var identity = new
        {
            protocolVersion = ProtocolVersion,
            algorithmCommit = AlgorithmCommit,
            runId = request.RunId,
            epochId = request.EpochId,
            contextRevision = request.ContextRevision,
            scanStart = request.Start,
            scanEnd = request.End,
            searchedStart,
            searchedEnd,
            orderVersion,
            game = request.Game,
            kind = request.Kind,
            area = request.Area,
            weather = request.Weather,
            leadAbility = request.LeadAbility,
            seed0 = state.Seed0.ToString("X16"),
            seed1 = state.Seed1.ToString("X16"),
            species = request.Species,
            tid = request.Tid,
            sid = request.Sid,
            shinyCharm = request.ShinyCharm,
            markCharm = request.MarkCharm,
            shiny = request.Shiny,
            aura = request.Aura,
            height = request.Height,
            mark = request.Mark,
            specificMark = request.SpecificMark,
            ivMatch = request.IvMatch,
            ivs = request.Ivs,
            rareEncryptionConstant = request.RareEncryptionConstant,
            nature = request.Nature,
            ability = request.Ability,
            gender = request.Gender,
            knockouts = request.Knockouts,
            hiddenStep = request.HiddenStep,
            dexRecommendationSlots = request.DexRecommendationSlots,
            considerMenuClose = request.ConsiderMenuClose,
            menuCloseNonPlayerCharacters = request.MenuCloseNonPlayerCharacters,
            holdDirection = request.HoldDirection,
            considerFlying = request.ConsiderFlying,
            areaLoadAdvances = request.AreaLoadAdvances,
            areaLoadNonPlayerCharacters = request.AreaLoadNonPlayerCharacters,
            considerRain = request.ConsiderRain,
            rainTicksDuringAreaLoad = request.RainTicksDuringAreaLoad,
            rainTicksBeforeEncounter = request.RainTicksBeforeEncounter,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(identity, Json);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return ($"encounter-{digest[..32]}", digest);
    }

    private static async Task<object> ExecuteObservationOperationAsync(DesktopRequest r)
    {
        var seedService = new OwoowRetailSeedService();
        var positionService = new OwoowXoroshiroService();

        switch (r.Operation)
        {
            case "seed.solve":
            {
                var solved = await seedService.SolveObservationAsync(
                    new RetailSeedRequest(r.Observations)).ConfigureAwait(false);
                return new
                {
                    rawAlgorithmState = FormatState(solved.RawAlgorithmState),
                    stateBeforeObservations = FormatState(solved.StateBeforeObservations),
                    stateAfterObservations = FormatState(solved.StateAfterObservations),
                    solved.ObservationCount,
                    rawStateBoundary = solved.RawStateBoundary.ToString(),
                    solved.BoundarySemanticsVersion,
                    experimentReport = CreatePendingDiagnosticReport("seed.solve", r,
                        ValidateDiagnosticSourceKind(r.SourceKind)),
                };
            }
            case "seed.verify":
            {
                var state = ParseState(r.Seed0, r.Seed1);
                var verified = await seedService.VerifyObservationsAsync(
                    new AnimationVerificationRequest(state, r.Observations)).ConfigureAwait(false);
                return new
                {
                    verified.Matches,
                    verified.PredictedObservations,
                    verified.FirstMismatchIndex,
                    stateAfterObservations = FormatState(verified.StateAfterObservations),
                    experimentReport = CreatePendingDiagnosticReport("seed.verify", r,
                        ValidateDiagnosticSourceKind(r.SourceKind)),
                };
            }
            case "seed.locate":
            {
                var anchor = ParseState(r.Seed0, r.Seed1);
                var located = await seedService.LocateAsync(
                    new AnimationRelocationRequest(anchor, r.Start, r.End, r.Observations))
                    .ConfigureAwait(false);
                return new
                {
                    windowStart = located.WindowStart,
                    windowEnd = located.WindowEnd,
                    located.ObservationLength,
                    candidateCount = located.Candidates.Count,
                    located.Complete,
                    experimentReport = CreatePendingDiagnosticReport("seed.locate", r,
                        ValidateDiagnosticSourceKind(r.SourceKind)),
                    candidates = located.Candidates.Select(candidate => new
                    {
                        firstObservedAdvance = candidate.FirstObservedAdvance,
                        stateAfterObservedAdvance = candidate.StateAfterObservedAdvance,
                        stateBeforeObservations = FormatState(candidate.StateBeforeObservations),
                        stateAfterObservations = FormatState(candidate.StateAfterObservations),
                    }).ToArray(),
                };
            }
            case "rng.advance":
            {
                var state = ParseState(r.Seed0, r.Seed1);
                var advanced = await positionService.AdvanceAsync(state, r.Amount).ConfigureAwait(false);
                return new { state = FormatState(advanced), amount = r.Amount };
            }
            case "rng.distance":
            {
                var before = ParseState(r.Seed0, r.Seed1);
                var after = ParseState(r.OtherSeed0, r.OtherSeed1);
                var distance = await positionService.MeasureDistanceAsync(
                    new RngDistanceRequest(before, after, r.DistanceLimit)).ConfigureAwait(false);
                return new
                {
                    distance.Found,
                    distance.Distance,
                    maximumDistance = distance.MaximumDistance,
                    verifiedEndState = FormatState(distance.VerifiedEndState),
                };
            }
            default:
                throw new ArgumentException("未知的观测或位置操作。");
        }
    }

    private static async Task<object> ExecuteCalibrationProbeAsync(DesktopRequest request)
    {
        var state = ParseState(request.Seed0, request.Seed1);
        if (request.ProbeActions is null || request.ProbeActions.Length is < 1 or > 8)
            throw new ArgumentException("probeActions 必须包含 1～8 个动作。");
        ValidateProbeActions(request.ProbeActions);
        if (request.ProbeCandidates is null || request.ProbeCandidates.Length is < 1 or > 512)
            throw new ArgumentException("probeCandidates 必须包含 1～512 组参数。");
        if ((request.ObservedSeed0 is null) != (request.ObservedSeed1 is null))
            throw new ArgumentException("observedSeed0 和 observedSeed1 必须同时提供。");
        var observedState = request.ObservedSeed0 is null
            ? (RngState?)null
            : ParseState(request.ObservedSeed0, request.ObservedSeed1!);
        var sourceKind = ValidateDiagnosticSourceKind(request.SourceKind);
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var evaluated = new List<object>();
        var matchedCount = 0;

        foreach (var candidate in request.ProbeCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId) || !candidateIds.Add(candidate.CandidateId))
                throw new ArgumentException("probe candidate IDs must be non-empty and unique.");
            ValidateProbeCandidate(candidate);
            var simulation = await SimulateProbeCandidateAsync(state, candidate, request.ProbeActions)
                .ConfigureAwait(false);
            var current = simulation.EndState;
            var totalAdvances = simulation.Advances;
            var actionResults = simulation.Actions;

            bool? matchesState = observedState is null ? null : current == observedState.Value;
            bool? matchesAdvances = request.ObservedAdvances is null
                ? null
                : totalAdvances == request.ObservedAdvances.Value;
            bool? matches = observedState is null && request.ObservedAdvances is null
                ? null
                : matchesState != false && matchesAdvances != false;
            if (matches is true) matchedCount++;
            evaluated.Add(new
            {
                candidate.CandidateId,
                parameters = new
                {
                    candidate.MenuCloseNonPlayerCharacters,
                    candidate.AreaLoadAdvances,
                    candidate.AreaLoadNonPlayerCharacters,
                    candidate.RainTicks,
                    candidate.RainTicksBeforeMap,
                    candidate.RainTicksAfterMenu,
                    candidate.RainTicksDuringAreaLoad,
                    candidate.RainTicksBeforeEncounter,
                    candidate.InterferenceAdvances,
                },
                predictedAdvances = totalAdvances,
                predictedEndState = FormatState(current),
                matchesState,
                matchesAdvances,
                matches,
                actions = actionResults,
            });
        }

        var status = observedState is null && request.ObservedAdvances is null
            ? "NoObservation"
            : matchedCount switch { 0 => "NoMatch", 1 => "Unique", _ => "Ambiguous" };
        return new
        {
            status,
            initialState = FormatState(state),
            observedEndState = observedState is null ? null : FormatState(observedState.Value),
            request.ObservedAdvances,
            matchedCandidateCount = matchedCount,
            candidates = evaluated,
            experimentReport = CreatePendingDiagnosticReport(
                "calibration.probe.evaluate", request, sourceKind),
        };
    }

    private static async Task<ProbeSimulation> SimulateProbeCandidateAsync(
        RngState initialState,
        CalibrationProbeCandidate candidate,
        CalibrationProbeAction[] actions)
    {
        var current = initialState;
        uint totalAdvances = 0;
        var actionResults = new List<object>();
        var calibration = new OwoowCalibrationService();
        var positions = new OwoowXoroshiroService();
        foreach (var action in actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Kind))
                throw new ArgumentException("probeActions[].kind is required.");
            if (!Enum.IsDefined(action.Weather))
                throw new ArgumentException("probeActions[].weather is invalid.");
            ValidateProbeAction(action);
            var before = current;
            uint advances;
            switch (action.Kind.Trim().ToLowerInvariant())
            {
                case "menuclose":
                {
                    var result = await calibration.CalculateMenuCloseAsync(new MenuCloseCalibrationRequest(
                        current,
                        action.MenuCloseNonPlayerCharacters ?? candidate.MenuCloseNonPlayerCharacters,
                        action.HoldDirection,
                        action.Weather)).ConfigureAwait(false);
                    current = result.State;
                    advances = result.Advances;
                    break;
                }
                case "rain":
                {
                    var result = await calibration.CalculateRainAsync(new RainCalibrationRequest(
                        current, action.Ticks ?? candidate.RainTicks)).ConfigureAwait(false);
                    current = result.State;
                    advances = result.Advances;
                    break;
                }
                case "areaload":
                {
                    var result = await calibration.CalculateAreaLoadAsync(new AreaLoadCalibrationRequest(
                        current,
                        action.AreaRolls ?? candidate.AreaLoadAdvances,
                        action.AreaLoadNonPlayerCharacters ?? candidate.AreaLoadNonPlayerCharacters))
                        .ConfigureAwait(false);
                    current = result.State;
                    advances = result.Advances;
                    break;
                }
                case "fly":
                {
                    var result = await calibration.CalculateFlyAsync(new FlyCalibrationRequest(
                        current,
                        action.RainTicksBeforeMap ?? candidate.RainTicksBeforeMap,
                        action.RainTicksAfterMenu ?? candidate.RainTicksAfterMenu,
                        action.AreaRolls ?? candidate.AreaLoadAdvances,
                        action.AreaLoadNonPlayerCharacters ?? candidate.AreaLoadNonPlayerCharacters,
                        action.RainTicksDuringAreaLoad ?? candidate.RainTicksDuringAreaLoad,
                        action.MenuCloseNonPlayerCharacters ?? candidate.MenuCloseNonPlayerCharacters,
                        action.HoldDirection,
                        action.Weather,
                        action.RainTicksBeforeEncounter ?? candidate.RainTicksBeforeEncounter))
                        .ConfigureAwait(false);
                    current = result.State;
                    advances = result.Advances;
                    break;
                }
                case "interference":
                {
                    advances = checked((uint)candidate.InterferenceAdvances);
                    current = await positions.AdvanceAsync(current, advances).ConfigureAwait(false);
                    break;
                }
                default:
                    throw new ArgumentException($"unsupported probe action kind: {action.Kind}");
            }
            totalAdvances = checked(totalAdvances + advances);
            actionResults.Add(new
            {
                kind = action.Kind,
                before = FormatState(before),
                after = FormatState(current),
                advances,
            });
        }

        return new ProbeSimulation(current, totalAdvances, actionResults);
    }

    private static async Task<object> ExecuteAttemptPlanAsync(
        DesktopRequest request,
        IProgress<OperationProgress> progress)
    {
        var anchor = ParseState(request.Seed0, request.Seed1);
        if (request.CurrentAdvance is null or > 1_000_000_000)
            throw new ArgumentException("currentAdvance 必须位于 0～1,000,000,000。");
        if (request.TargetCandidates is null || request.TargetCandidates.Length is < 1 or > 512)
            throw new ArgumentException("targetCandidates 必须包含 1～512 个搜索结果。");
        if (request.ProbeActions is null || request.ProbeCandidates is null)
            throw new ArgumentException("探测动作和已校准参数不能为空。");
        ValidateProbeActions(request.ProbeActions);
        if (request.ProbeCandidates.Length != 1)
            throw new ArgumentException("attempt.plan 需要一个已校准且唯一的 NPC 参数候选。");
        var probeCandidate = request.ProbeCandidates[0];
        ValidateProbeCandidate(probeCandidate);
        if (request.BoundaryAdjustment is null || request.AttemptPlanning is null)
            throw new ArgumentException("boundaryAdjustment 和 attemptPlanning 必须提供。");
        var boundary = request.BoundaryAdjustment;
        if (string.IsNullOrWhiteSpace(boundary.BoundaryId) || boundary.BoundaryId.Length > 128
            || string.IsNullOrWhiteSpace(boundary.ModelVersion) || boundary.ModelVersion.Length > 128
            || boundary.Revision < 0 || boundary.Offset is < -100_000 or > 100_000)
            throw new ArgumentException("boundaryAdjustment 的边界、版本、修订号或偏移量无效。");
        if (string.IsNullOrWhiteSpace(request.TargetSnapshotId) || request.TargetSnapshotId.Length > 128)
            throw new ArgumentException("targetSnapshotId 必须引用一份完整搜索快照。");
        var requestDigest = request.TargetRequestDigest;
        if (requestDigest is null || requestDigest.Length != 64
            || requestDigest.Any(value => !Uri.IsHexDigit(value))
            || requestDigest != requestDigest.ToLowerInvariant())
            throw new ArgumentException("targetRequestDigest 必须是小写 SHA-256。");
        var settings = request.AttemptPlanning;
        if (settings.MaximumPlanEvaluations is < 1 or > 100_000
            || settings.CoarseBatchSize is < 1 or > 100_000
            || settings.MaximumCoarseBatches is < 1 or > 1_000
            || settings.RelocationReserve > 100_000
            || settings.PreciseReserve > 100_000)
            throw new ArgumentException("attemptPlanning 超出安全上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in request.TargetCandidates)
        {
            if (target is null || string.IsNullOrWhiteSpace(target.CandidateId)
                || !ids.Add(target.CandidateId) || target.GenerationAdvance > 1_000_000_000)
                throw new ArgumentException("targetCandidates 必须使用唯一 ID 和合法推进位置。");
        }

        var currentAdvance = request.CurrentAdvance.Value;
        var searchRanges = new List<(PlannedTargetCandidate Target, ulong End)>();
        ulong totalWork = 0;
        var targetStatuses = new List<object>();
        var allTargetsPassed = true;
        var anyInsufficientBudget = false;
        foreach (var target in request.TargetCandidates)
        {
            if (target.GenerationAdvance < currentAdvance)
            {
                targetStatuses.Add(new
                {
                    candidateId = target.CandidateId,
                    status = "TargetAlreadyPassed",
                    reason = "The confirmed physical position is beyond this generation position.",
                });
                continue;
            }
            allTargetsPassed = false;

            var maximumTrigger = (long)target.GenerationAdvance - boundary.Offset;
            if (maximumTrigger < (long)currentAdvance)
            {
                targetStatuses.Add(new
                {
                    candidateId = target.CandidateId,
                    status = "NoTriggerRange",
                    reason = "No trigger position remains at or after the confirmed position.",
                });
                continue;
            }
            var cappedEnd = (ulong)Math.Min(maximumTrigger, 1_000_000_000L);
            var evaluations = cappedEnd - currentAdvance + 1;
            totalWork = checked(totalWork + evaluations);
            if (totalWork > (ulong)settings.MaximumPlanEvaluations)
                throw new ArgumentException("有界枚举超过 maximumPlanEvaluations；缩小目标窗口后重新规划。");
            searchRanges.Add((target, cappedEnd));
        }

        var positionService = new OwoowXoroshiroService();
        var plans = new List<(ulong Trigger, ulong Generation, string CandidateId, object Payload)>();
        ulong completed = 0;
        var underBudgetCount = 0;
        foreach (var (target, searchEnd) in searchRanges)
        {
            var position = await positionService.AdvanceAsync(anchor, currentAdvance).ConfigureAwait(false);
            var matchingModels = 0;
            var targetPlanStartCount = plans.Count;
            var targetUnderBudgetCount = 0;
            for (var triggerAdvance = currentAdvance; triggerAdvance <= searchEnd; triggerAdvance++)
            {
                var simulated = await SimulateProbeCandidateAsync(
                    position, probeCandidate, request.ProbeActions).ConfigureAwait(false);
                var predictedGeneration = (long)triggerAdvance + simulated.Advances + boundary.Offset;
                if (predictedGeneration >= 0 && (ulong)predictedGeneration == target.GenerationAdvance)
                {
                    matchingModels++;
                    var reserve = checked(settings.RelocationReserve + settings.PreciseReserve);
                    var distance = triggerAdvance - currentAdvance;
                    if (distance < reserve)
                    {
                        underBudgetCount++;
                        targetUnderBudgetCount++;
                    }
                    else
                    {
                        var coarseTarget = triggerAdvance - reserve;
                        var coarseDistance = coarseTarget - currentAdvance;
                        var batchCount = coarseDistance == 0
                            ? 0UL
                            : (coarseDistance + settings.CoarseBatchSize - 1) / settings.CoarseBatchSize;
                        if (batchCount > (ulong)settings.MaximumCoarseBatches)
                            throw new ArgumentException("粗推进分段数超过 maximumCoarseBatches；调整批次大小后重新规划。");
                        if (plans.Count >= 10_000)
                            throw new ArgumentException("可行计划超过 10,000 条；缩小搜索窗口后重新规划。");
                        var batches = new List<object>();
                        var batchStart = currentAdvance;
                        while (batchStart < coarseTarget)
                        {
                            var amount = Math.Min(settings.CoarseBatchSize, coarseTarget - batchStart);
                            batches.Add(new
                            {
                                startAdvance = batchStart,
                                requestedAdvances = amount,
                                plannedEndAdvance = batchStart + amount,
                                requiresRelocationBeforeNextBatch = true,
                            });
                            batchStart += amount;
                        }

                        plans.Add((triggerAdvance, target.GenerationAdvance, target.CandidateId, new
                        {
                            targetCandidateId = target.CandidateId,
                            targetGenerationAdvance = target.GenerationAdvance,
                            triggerAdvance,
                            modeledPreTriggerAdvances = simulated.Advances,
                            predictedGenerationAdvance = (ulong)predictedGeneration,
                            boundaryId = boundary.BoundaryId,
                            boundaryOffset = boundary.Offset,
                            boundaryModelVersion = boundary.ModelVersion,
                            boundaryRevision = boundary.Revision,
                            probeCandidateId = probeCandidate.CandidateId,
                            probeActions = request.ProbeActions,
                            coarseTargetAdvance = coarseTarget,
                            coarseAdvances = coarseDistance,
                            relocationReserve = settings.RelocationReserve,
                            preciseReserve = settings.PreciseReserve,
                            coarseBatches = batches,
                            checkpointRequiredAfterEachBatch = true,
                        }));
                    }
                }

                completed++;
                if (completed % 256 == 0 || completed == totalWork)
                {
                    progress.Report(new OperationProgress(
                        OperationState.Running,
                        completed,
                        totalWork,
                        "枚举状态相关的 NPC / 天气动作模型。"));
                }
                if (triggerAdvance < searchEnd)
                    position = await positionService.AdvanceAsync(position, 1).ConfigureAwait(false);
            }
            targetStatuses.Add(new
            {
                candidateId = target.CandidateId,
                status = plans.Count > targetPlanStartCount
                    ? "Feasible"
                    : matchingModels > 0 && targetUnderBudgetCount > 0
                        ? "InsufficientExecutionBudget"
                    : "NoFeasibleTriggerPosition",
                feasiblePlanCount = plans.Count(plan => plan.CandidateId == target.CandidateId),
            });
            if (matchingModels > 0 && plans.Count == targetPlanStartCount && targetUnderBudgetCount > 0)
                anyInsufficientBudget = true;
        }

        if (completed > 0)
        {
            progress.Report(new OperationProgress(
                OperationState.Completed,
                completed,
                totalWork,
                "有界规划枚举完成。"));
        }
        var orderedPlans = plans
            .OrderBy(plan => plan.Trigger)
            .ThenBy(plan => plan.Generation)
            .ThenBy(plan => plan.CandidateId, StringComparer.Ordinal)
            .Select(plan => plan.Payload)
            .ToArray();
        var overallStatus = orderedPlans.Length > 0
            ? "Feasible"
            : anyInsufficientBudget
                ? "InsufficientExecutionBudget"
                : allTargetsPassed ? "TargetAlreadyPassed" : "NoFeasibleTriggerPosition";
        return new
        {
            reportSchema = "auto-swsh-attempt-plan",
            reportVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            status = overallStatus,
            targetSnapshotId = request.TargetSnapshotId,
            targetRequestDigest = request.TargetRequestDigest,
            currentAdvance,
            boundary = new
            {
                boundary.BoundaryId,
                boundary.Offset,
                boundary.ModelVersion,
                boundary.Revision,
            },
            probeCandidateId = probeCandidate.CandidateId,
            evaluationCount = completed,
            underBudgetPositionCount = underBudgetCount,
            targetStatuses,
            plans = orderedPlans,
            reason = overallStatus switch
            {
                "TargetAlreadyPassed" => "All requested generation positions are behind the confirmed physical position.",
                "InsufficientExecutionBudget" => "A modeled trigger exists but the relocation and precise reserves do not fit.",
                "NoFeasibleTriggerPosition" => "No trigger position satisfies the stateful action model and boundary equation.",
                _ => null,
            },
            hardwareStatus = "PendingHardwareValidation",
            canStartFormalAutomation = false,
        };
    }

    private static void ValidateProbeActions(CalibrationProbeAction[] actions)
    {
        if (actions.Length is < 1 or > 8)
            throw new ArgumentException("probeActions 必须包含 1～8 个动作。");
        foreach (var action in actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Kind))
                throw new ArgumentException("probeActions[].kind is required.");
            if (!Enum.IsDefined(action.Weather))
                throw new ArgumentException("probeActions[].weather is invalid.");
            ValidateProbeAction(action);
            if (action.Kind.Trim().ToLowerInvariant() is not ("menuclose" or "rain" or "areaload" or "fly" or "interference"))
                throw new ArgumentException($"unsupported probe action kind: {action.Kind}");
        }
    }

    private static void ValidateProbeCandidate(CalibrationProbeCandidate candidate)
    {
        const uint maximumN = 100_000;
        foreach (var (name, value) in new[]
        {
            ("menuCloseNonPlayerCharacters", candidate.MenuCloseNonPlayerCharacters),
            ("areaLoadAdvances", candidate.AreaLoadAdvances),
            ("areaLoadNonPlayerCharacters", candidate.AreaLoadNonPlayerCharacters),
            ("rainTicks", candidate.RainTicks),
            ("rainTicksBeforeMap", candidate.RainTicksBeforeMap),
            ("rainTicksAfterMenu", candidate.RainTicksAfterMenu),
            ("rainTicksDuringAreaLoad", candidate.RainTicksDuringAreaLoad),
            ("rainTicksBeforeEncounter", candidate.RainTicksBeforeEncounter),
        })
        {
            if (value > maximumN)
                throw new ArgumentOutOfRangeException(name, "Probe parameters are bounded at 100,000.");
        }
        if (candidate.InterferenceAdvances > maximumN)
            throw new ArgumentOutOfRangeException(nameof(candidate.InterferenceAdvances));
    }

    private static void ValidateProbeAction(CalibrationProbeAction action)
    {
        const uint maximumN = 100_000;
        foreach (var (name, value) in new[]
        {
            ("ticks", action.Ticks),
            ("areaRolls", action.AreaRolls),
            ("menuCloseNonPlayerCharacters", action.MenuCloseNonPlayerCharacters),
            ("areaLoadNonPlayerCharacters", action.AreaLoadNonPlayerCharacters),
            ("rainTicksBeforeMap", action.RainTicksBeforeMap),
            ("rainTicksAfterMenu", action.RainTicksAfterMenu),
            ("rainTicksDuringAreaLoad", action.RainTicksDuringAreaLoad),
            ("rainTicksBeforeEncounter", action.RainTicksBeforeEncounter),
        })
        {
            if (value > maximumN)
                throw new ArgumentOutOfRangeException(name, "Probe action parameters are bounded at 100,000.");
        }
    }

    private static async Task<object> ExecuteEncounterReverseAsync(
        DesktopRequest request,
        IProgress<OperationProgress> progress)
    {
        var sourceKind = ValidateDiagnosticSourceKind(request.SourceKind);
        if (request.End < request.Start || request.End - request.Start >= 100_000
            || request.End > 1_000_000_000)
            throw new ArgumentException("反查窗口必须为闭区间，且最多包含 100,000 个推进位置。");
        if (request.LevelMinimum is < 1 or > 100 || request.LevelMaximum is < 1 or > 100
            || request.LevelMaximum < request.LevelMinimum)
            throw new ArgumentException("反查等级范围必须位于 1～100。");
        if (request.Ivs is null || request.Ivs.Length != 6 || request.Ivs.Any(pair => pair is null || pair.Length != 2))
            throw new ArgumentException("反查需要六项 IV 范围；未知项请使用 [0,31]。");
        var state = ParseState(request.Seed0, request.Seed1);
        var context = new EncounterCatalogRequest(
            request.Game, request.Kind, request.Area ?? "", request.Weather ?? "", request.LeadAbility ?? "");
        var catalog = new OwoowEncounterCatalogService();
        await catalog.GetTableAsync(context).ConfigureAwait(false);
        var observedSpecies = request.Species;
        if (request.Kind == EncounterKind.Static && string.IsNullOrWhiteSpace(observedSpecies))
            throw new ArgumentException("静态遭遇反查必须提供已确认的实际物种。");
        if (observedSpecies is not null
            && !(await catalog.GetSpeciesAsync(request.Game, request.Kind, context.Area, context.Weather)
                .ConfigureAwait(false)).Contains(observedSpecies))
            throw new ArgumentException("实际物种不在当前遭遇表中。");

        var filter = new EncounterFilter(
            targetSpecies: observedSpecies,
            shiny: request.Shiny,
            aura: request.Aura,
            mark: new MarkFilter(request.Mark, request.SpecificMark),
            height: request.Height,
            individualValues: request.Ivs.Select(pair =>
                new IndividualValueConstraint(request.IvMatch, pair[0], pair[1])),
            rareEncryptionConstant: request.RareEncryptionConstant,
            targetNature: request.Nature,
            targetAbility: request.Ability,
            targetGender: request.Gender);
        var profile = new RngProfile(
            "Reverse diagnostic", request.Game, request.Tid, request.Sid,
            request.ShinyCharm, request.MarkCharm);
        var environment = new OverworldEnvironmentSettings(
            request.ConsiderMenuClose,
            request.MenuCloseNonPlayerCharacters,
            request.HoldDirection,
            request.ConsiderFlying,
            request.AreaLoadAdvances,
            request.AreaLoadNonPlayerCharacters,
            request.ConsiderRain,
            request.RainTicksDuringAreaLoad,
            request.RainTicksBeforeEncounter);
        var results = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            state,
            request.Start,
            request.End,
            context,
            profile,
            filter,
            environment,
            auraKnockouts: request.Knockouts,
            hiddenMaximumStep: request.HiddenStep,
            dexRecommendationSlots: request.DexRecommendationSlots), progress).ConfigureAwait(false);
        var matched = results.Where(row => row.Level >= request.LevelMinimum && row.Level <= request.LevelMaximum)
            .ToArray();
        var advances = matched.Select(row => row.Advance).Distinct().Order().ToArray();
        var status = advances.Length switch { 0 => "no_match", 1 => "unique", _ => "multiple" };
        var unresolved = request.UnresolvedFields?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var evidenceIds = request.EvidenceIds?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var matchedFields = MatchedObservationFields(request);
        var eligible = status == "unique"
            && request.DiagnosticWindowComplete
            && request.CaptureConfirmed
            && request.IdentityVerified
            && request.SceneMatch
            && sourceKind == "real"
            && evidenceIds.Length > 0;
        var unresolvedOutput = unresolved
            .Concat(UnresolvedObservationFields(request, matchedFields))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new
        {
            status,
            sourceKind,
            complete = true,
            window = new { start = request.Start, end = request.End, request.DiagnosticWindowComplete },
            candidateGenerationAdvances = advances,
            candidates = matched.Select(row => new
            {
                generationAdvance = row.Advance,
                row.Species,
                row.Level,
                shiny = NormalizeShinyType(row.Shiny),
                row.Nature,
                row.Ability,
                row.Gender,
                row.BrilliantAura,
                ivs = row.IndividualValues.ToArray().Select(value => (int)value).ToArray(),
                mark = NormalizeMarkName(row.Mark),
                height = HeightBucket(row.Height),
                row.HeightDescription,
                rareEncryptionConstant = row.EncryptionConstant % 100 == 0,
                ec = row.EncryptionConstant.ToString("X8"),
                pid = row.PersonalityId.ToString("X8"),
            }).ToArray(),
            plannedGenerationAdvance = request.PlannedGenerationAdvance,
            residualAdvances = advances.Length == 1 && request.PlannedGenerationAdvance is { } planned
                ? (long)advances[0] - (long)planned
                : (long?)null,
            matchedFields,
            unresolvedFields = unresolvedOutput,
            evidenceIds,
            verificationLevel = advances.Length == 1 ? "unique_observed_constraints" : "insufficient_or_nonunique",
            eligibleForCalibration = eligible,
            reasonCode = status switch
            {
                "no_match" => "NO_COMPATIBLE_GENERATION",
                "unique" when eligible => "UNIQUE_TRUSTED_MATCH",
                "unique" => "UNIQUE_MATCH_PENDING_EVIDENCE_OR_WINDOW_REVIEW",
                _ => "MULTIPLE_COMPATIBLE_GENERATIONS",
            },
            experimentReport = CreatePendingDiagnosticReport("encounter.reverse", request, sourceKind),
        };
    }

    private static string NormalizeShinyType(string shiny) =>
        shiny.StartsWith("Star", StringComparison.OrdinalIgnoreCase) ? "Star"
        : string.Equals(shiny, "Square", StringComparison.OrdinalIgnoreCase) ? "Square"
        : "None";

    private static string NormalizeMarkName(string mark) => mark switch
    {
        "Time" => "Lunchtime",
        "Weather" => "Cloudy",
        _ => mark,
    };

    private static string HeightBucket(byte height) => height switch
    {
        0 => "XXXS",
        <= 24 => "XXS",
        <= 59 => "XS",
        <= 99 => "Small",
        <= 155 => "Medium",
        <= 195 => "Large",
        <= 230 => "XL",
        <= 254 => "XXL",
        _ => "XXXL",
    };

    private static string[] MatchedObservationFields(DesktopRequest request)
    {
        var fields = new List<string>();
        if (request.Species is not null) fields.Add("species");
        if (request.LevelMinimum != 1 || request.LevelMaximum != 100) fields.Add("level");
        if (request.Shiny != ShinyFilter.Any) fields.Add("shiny");
        if (request.Nature is not null) fields.Add("nature");
        if (request.Ability is not null) fields.Add("ability");
        if (request.Gender is not null) fields.Add("gender");
        if (request.Aura != AuraFilter.Any) fields.Add("aura");
        if (request.Mark != MarkFilterMode.Ignore) fields.Add("mark");
        if (request.Ivs.Any(pair => pair[0] != 0 || pair[1] != 31)) fields.Add("statConstraints");
        if (request.RareEncryptionConstant) fields.Add("rareEncryptionConstant");
        return fields.ToArray();
    }

    private static string[] UnresolvedObservationFields(DesktopRequest request, IReadOnlyCollection<string> matchedFields)
    {
        var unresolved = new List<string>();
        foreach (var (inputName, outputName) in new[]
        {
            ("species", "species"),
            ("level", "level"),
            ("shiny", "shiny"),
            ("nature", "nature"),
            ("ability", "ability"),
            ("gender", "gender"),
            ("aura", "aura"),
            ("mark", "mark"),
            ("statConstraints", "exactIVs"),
        })
        {
            if (!matchedFields.Contains(inputName)) unresolved.Add(outputName);
        }
        if (request.Ivs.Any(pair => pair[0] != pair[1]) && !unresolved.Contains("exactIVs"))
            unresolved.Add("exactIVs");
        unresolved.Add("encryptionConstant");
        unresolved.Add("personalityId");
        unresolved.Add("height");
        return unresolved.ToArray();
    }

    private static string ValidateDiagnosticSourceKind(string? sourceKind)
    {
        if (sourceKind is null)
            return "unknown";
        if (sourceKind is not ("synthetic" or "replay" or "real"))
            throw new ArgumentException("sourceKind must be synthetic, replay or real.");
        return sourceKind;
    }

    private static object CreatePendingDiagnosticReport(string operation, DesktopRequest request, string sourceKind) => new
    {
        reportSchema = "auto-swsh-diagnostic-report",
        reportVersion = 1,
        generatedAtUtc = DateTimeOffset.UtcNow,
        operation,
        runId = request.RunId,
        epochId = request.EpochId,
        contextRevision = request.ContextRevision,
        sourceKind,
        evidenceStatus = request.EvidenceIds is { Length: > 0 } ? "EvidenceReferenced" : "PendingEvidence",
        evidenceIds = request.EvidenceIds ?? [],
        frameworkStatus = "FrameworkDiagnosticComplete",
        hardwareStatus = "PendingHardwareValidation",
        canStartFormalAutomation = false,
    };

    private static object FormatState(RngState state) => new
    {
        seed0 = state.Seed0.ToString("X16"),
        seed1 = state.Seed1.ToString("X16"),
    };

    private static void ValidateRequestIdentity(DesktopRequest request)
    {
        static void Require(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                throw new ArgumentException($"{name} 必须为 1～128 个字符。", name);
            }
        }

        Require(request.RequestId, "requestId");
        Require(request.RunId, "runId");
        Require(request.EpochId, "epochId");
        if (request.ContextRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ContextRevision),
                "contextRevision 不能为负数。");
        }
    }

    private static RngState ParseState(string seed0, string seed1)
    {
        static ulong Parse(string value)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
            if (value.Length is < 1 or > 16 || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException("Seed 必须是 1～16 位十六进制数。");
            return parsed;
        }
        var state = new RngState(Parse(seed0), Parse(seed1));
        if (state is { Seed0: 0, Seed1: 0 }) throw new ArgumentException("两个 Seed 不能同时为零。");
        return state;
    }

    private sealed record ProbeSimulation(
        RngState EndState,
        uint Advances,
        IReadOnlyList<object> Actions);

    private sealed class JsonProgress(TextWriter output, DesktopRequest request) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                type = "progress",
                protocolVersion = ProtocolVersion,
                requestId = request.RequestId,
                runId = request.RunId,
                epochId = request.EpochId,
                contextRevision = request.ContextRevision,
                completed = value.Completed,
                total = value.Total,
            }, Json));
            output.Flush();
        }
    }
}

public sealed record DesktopRequest
{
    public int ProtocolVersion { get; init; } = DesktopJsonCommand.ProtocolVersion;
    public string? RequestId { get; init; }
    public string? RunId { get; init; }
    public string? EpochId { get; init; }
    public long ContextRevision { get; init; }
    public string Operation { get; init; } = "";
    public GameVersion Game { get; init; }
    public EncounterKind Kind { get; init; } = EncounterKind.Symbol;
    public string? Area { get; init; }
    public string? Weather { get; init; }
    public string? LeadAbility { get; init; }
    public string? Species { get; init; }
    public string Seed0 { get; init; } = "";
    public string Seed1 { get; init; } = "";
    public string OtherSeed0 { get; init; } = "";
    public string OtherSeed1 { get; init; } = "";
    public string Observations { get; init; } = "";
    public uint DistanceLimit { get; init; }
    public ulong Start { get; init; }
    public ulong End { get; init; }
    public ulong Amount { get; init; } = 1;
    public bool Reverse { get; init; }
    public int Tid { get; init; }
    public int Sid { get; init; }
    public bool ShinyCharm { get; init; }
    public bool MarkCharm { get; init; }
    public int Knockouts { get; init; }
    public int HiddenStep { get; init; }
    public ShinyFilter Shiny { get; init; }
    public MarkFilterMode Mark { get; init; }
    public string? SpecificMark { get; init; }
    public AuraFilter Aura { get; init; }
    public HeightFilter Height { get; init; }
    public IndividualValueMatch IvMatch { get; init; } = IndividualValueMatch.Range;
    public bool RareEncryptionConstant { get; init; }
    public string? Nature { get; init; }
    public string? Ability { get; init; }
    public PokemonGender? Gender { get; init; }
    public int[][] Ivs { get; init; } = Enumerable.Range(0, 6).Select(_ => new[] { 0, 31 }).ToArray();
    public short[] DexRecommendationSlots { get; init; } = [0, 0, 0, 0];
    public bool ConsiderMenuClose { get; init; }
    public uint MenuCloseNonPlayerCharacters { get; init; }
    public bool HoldDirection { get; init; }
    public bool ConsiderFlying { get; init; }
    public uint AreaLoadAdvances { get; init; }
    public uint AreaLoadNonPlayerCharacters { get; init; }
    public bool ConsiderRain { get; init; }
    public uint RainTicksDuringAreaLoad { get; init; }
    public uint RainTicksBeforeEncounter { get; init; }
    public ulong? ScanCursor { get; init; }
    public int ScanLimit { get; init; } = 100_000;
    public int CandidateCursor { get; init; }
    public int CandidatePageSize { get; init; } = 10_000;
    public string? SnapshotId { get; init; }
    public string? RequestDigest { get; init; }
    public string? SourceKind { get; init; }
    public string? ObservedSeed0 { get; init; }
    public string? ObservedSeed1 { get; init; }
    public ulong? ObservedAdvances { get; init; }
    public CalibrationProbeAction[] ProbeActions { get; init; } = [];
    public CalibrationProbeCandidate[] ProbeCandidates { get; init; } = [];
    public ulong? PlannedGenerationAdvance { get; init; }
    public ulong? CurrentAdvance { get; init; }
    public PlannedTargetCandidate[] TargetCandidates { get; init; } = [];
    public GenerationBoundaryAdjustment? BoundaryAdjustment { get; init; }
    public AttemptPlanningSettings? AttemptPlanning { get; init; }
    public string? TargetSnapshotId { get; init; }
    public string? TargetRequestDigest { get; init; }
    public int LevelMinimum { get; init; } = 1;
    public int LevelMaximum { get; init; } = 100;
    public bool DiagnosticWindowComplete { get; init; }
    public bool CaptureConfirmed { get; init; }
    public bool IdentityVerified { get; init; }
    public bool SceneMatch { get; init; }
    public string[] EvidenceIds { get; init; } = [];
    public string[] UnresolvedFields { get; init; } = [];
}

public sealed record CalibrationProbeAction
{
    public string Kind { get; init; } = "";
    public Weather Weather { get; init; } = Weather.Normal;
    public bool HoldDirection { get; init; }
    public uint? Ticks { get; init; }
    public uint? AreaRolls { get; init; }
    public uint? MenuCloseNonPlayerCharacters { get; init; }
    public uint? AreaLoadNonPlayerCharacters { get; init; }
    public uint? RainTicksBeforeMap { get; init; }
    public uint? RainTicksAfterMenu { get; init; }
    public uint? RainTicksDuringAreaLoad { get; init; }
    public uint? RainTicksBeforeEncounter { get; init; }
}

public sealed record CalibrationProbeCandidate
{
    public string CandidateId { get; init; } = "";
    public uint MenuCloseNonPlayerCharacters { get; init; }
    public uint AreaLoadAdvances { get; init; }
    public uint AreaLoadNonPlayerCharacters { get; init; }
    public uint RainTicks { get; init; }
    public uint RainTicksBeforeMap { get; init; }
    public uint RainTicksAfterMenu { get; init; }
    public uint RainTicksDuringAreaLoad { get; init; }
    public uint RainTicksBeforeEncounter { get; init; }
    public ulong InterferenceAdvances { get; init; }
}

public sealed record PlannedTargetCandidate
{
    public string CandidateId { get; init; } = "";
    public ulong GenerationAdvance { get; init; }
}

public sealed record GenerationBoundaryAdjustment
{
    public string BoundaryId { get; init; } = "";
    public int Offset { get; init; }
    public string ModelVersion { get; init; } = "";
    public int Revision { get; init; }
}

public sealed record AttemptPlanningSettings
{
    public int MaximumPlanEvaluations { get; init; } = 100_000;
    public ulong CoarseBatchSize { get; init; } = 5_000;
    public int MaximumCoarseBatches { get; init; } = 1_000;
    public ulong RelocationReserve { get; init; }
    public ulong PreciseReserve { get; init; }
}
