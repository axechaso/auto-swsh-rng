using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Rng;
using PKHeX.Core;
using OwoowSeedFinder = owoow.Core.RNG.Generators.Misc.SeedFinder;
using OwoowUtil = owoow.Core.RNG.Util;

namespace AutoSwshRng.Upstream;

public sealed class OwoowRetailSeedService : IRetailSeedService, IRetailSeedObservationService
{
    public const string BoundarySemanticsVersion = "retail-seed-observation-v1";

    public Task<RngState> FindExactAsync(
        RetailSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OwoowSeedFinder.CalculateRetailSeed(request.Observations);
        return Task.FromResult(new RngState(result.Item1, result.Item2));
    }

    public async Task<RetailSeedObservationResult> SolveObservationAsync(
        RetailSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stateAfter = await FindExactAsync(request, cancellationToken).ConfigureAwait(false);
        var reverse = new Xoroshiro128Plus(stateAfter.Seed0, stateAfter.Seed1);
        for (var index = 0; index < request.Observations.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reverse.Prev();
        }

        var stateBeforeTuple = reverse.GetState();
        var stateBefore = new RngState(stateBeforeTuple.s0, stateBeforeTuple.s1);
        return new RetailSeedObservationResult(
            stateAfter,
            stateBefore,
            stateAfter,
            request.Observations.Length,
            SeedObservationBoundary.StateAfterLastObservation,
            BoundarySemanticsVersion);
    }

    public Task<AnimationVerificationResult> VerifyObservationsAsync(
        AnimationVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var expected = request.Observations.Select(value => (byte)(value - '0')).ToArray();
        var generated = OwoowSeedFinder.GenerateAnimationSequence(
            request.StateBeforeObservations.Seed0,
            request.StateBeforeObservations.Seed1,
            0,
            (ulong)expected.Length);
        var mismatch = -1;
        for (var index = 0; index < expected.Length; index++)
        {
            if (generated.sequence[index] != expected[index])
            {
                mismatch = index;
                break;
            }
        }
        var afterTuple = OwoowUtil.XoroshiroJump(
            request.StateBeforeObservations.Seed0,
            request.StateBeforeObservations.Seed1,
            (ulong)expected.Length);
        return Task.FromResult(
            new AnimationVerificationResult(
                mismatch < 0,
                string.Concat(generated.sequence.Select(value => value.ToString())),
                mismatch,
                new RngState(afterTuple.s0, afterTuple.s1)));
    }

    public Task<AnimationRelocationResult> LocateAsync(
        AnimationRelocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var lastOffset = checked(request.MaximumStartAdvance - request.MinimumStartAdvance);
        var observationLength = request.Observations.Length;
        var generatedCount = checked((int)(lastOffset + (ulong)observationLength));
        var generated = OwoowSeedFinder.GenerateAnimationSequence(
            request.AnchorState.Seed0,
            request.AnchorState.Seed1,
            request.MinimumStartAdvance,
            (ulong)generatedCount);
        var pattern = request.Observations.Select(value => (byte)(value - '0')).ToArray();
        var prefix = BuildPrefixTable(pattern);
        var candidates = new List<AnimationRelocationCandidate>();
        var matched = 0;

        for (var index = 0; index < generated.sequence.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            while (matched > 0 && generated.sequence[index] != pattern[matched])
            {
                matched = prefix[matched - 1];
            }

            if (generated.sequence[index] == pattern[matched])
            {
                matched++;
            }

            if (matched != pattern.Length)
            {
                continue;
            }

            var offset = checked((ulong)(index - pattern.Length + 1));
            if (offset <= lastOffset)
            {
                var firstAdvance = checked(request.MinimumStartAdvance + offset);
                var afterAdvance = checked(firstAdvance + (ulong)observationLength);
                var beforeTuple = OwoowUtil.XoroshiroJump(
                    request.AnchorState.Seed0,
                    request.AnchorState.Seed1,
                    firstAdvance);
                var afterTuple = OwoowUtil.XoroshiroJump(
                    request.AnchorState.Seed0,
                    request.AnchorState.Seed1,
                    afterAdvance);
                candidates.Add(
                    new AnimationRelocationCandidate(
                        firstAdvance,
                        afterAdvance,
                        new RngState(beforeTuple.s0, beforeTuple.s1),
                        new RngState(afterTuple.s0, afterTuple.s1)));
            }

            // Keep the longest suffix that can begin an overlapping match.
            matched = prefix[matched - 1];
        }

        return Task.FromResult(
            new AnimationRelocationResult(
                request.MinimumStartAdvance,
                request.MaximumStartAdvance,
                observationLength,
                candidates,
                complete: true));
    }

    public async Task<IReadOnlyList<RngState>> FindRangeAsync(
        RetailRangeSeedRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observations = request.Observations.Select(value => (byte)(value - '0')).ToArray();
        var total = GetAdvanceCount(
            request.MinimumAdvance,
            request.MaximumAdvance);
        ulong completed = 0;
        var results = new List<RngState>();
        progress?.Report(new OperationProgress(OperationState.Running, 0, total, "Searching retail seeds."));

        foreach (var advance in EnumerateAdvances(
                     request.MinimumAdvance,
                     request.MaximumAdvance))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = await Task.Run(
                    () => OwoowSeedFinder.GetInitialSeedFromRange(
                        advance,
                        advance,
                        observations),
                    cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(matches.Select(match => new RngState(match.s0, match.s1)));
            completed++;
            progress?.Report(
                new OperationProgress(
                    OperationState.Running,
                    completed,
                    total,
                    "Searching retail seeds."));
        }

        progress?.Report(new OperationProgress(OperationState.Completed, total, total, "Retail seed search complete."));
        return results;
    }

    public Task<AnimationSequenceResult> GenerateAnimationSequenceAsync(
        AnimationSequenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OwoowSeedFinder.GenerateAnimationSequence(
            request.State.Seed0,
            request.State.Seed1,
            request.InitialAdvance,
            (ulong)request.Count);
        var initialState = OwoowUtil.XoroshiroJump(
            request.State.Seed0,
            request.State.Seed1,
            request.InitialAdvance);
        return Task.FromResult(
            new AnimationSequenceResult(
                result.sequence,
                new RngState(initialState.s0, initialState.s1)));
    }

    public Task<ReidentifySeedResult> ReidentifyAsync(
        ReidentifySeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OwoowSeedFinder.ReidentifySeed(
            request.Observations.ToArray(),
            request.InitialState.Seed0,
            request.InitialState.Seed1,
            request.Pattern);
        return Task.FromResult(
            new ReidentifySeedResult(
                result.hits,
                result.advances,
                new RngState(result.s0, result.s1)));
    }

    internal static ulong GetAdvanceCount(int minimumAdvance, int maximumAdvance) =>
        checked((ulong)((long)maximumAdvance - minimumAdvance + 1));

    internal static IEnumerable<int> EnumerateAdvances(
        int minimumAdvance,
        int maximumAdvance)
    {
        for (var advance = (long)minimumAdvance;
             advance <= maximumAdvance;
             advance++)
        {
            yield return checked((int)advance);
        }
    }

    private static int[] BuildPrefixTable(IReadOnlyList<byte> pattern)
    {
        var prefix = new int[pattern.Count];
        for (var index = 1; index < pattern.Count; index++)
        {
            var length = prefix[index - 1];
            while (length > 0 && pattern[index] != pattern[length])
            {
                length = prefix[length - 1];
            }

            if (pattern[index] == pattern[length])
            {
                length++;
            }

            prefix[index] = length;
        }

        return prefix;
    }
}
