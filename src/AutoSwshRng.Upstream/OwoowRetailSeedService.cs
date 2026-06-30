using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Rng;
using OwoowSeedFinder = owoow.Core.RNG.Generators.Misc.SeedFinder;

namespace AutoSwshRng.Upstream;

public sealed class OwoowRetailSeedService : IRetailSeedService
{
    public Task<RngState> FindExactAsync(
        RetailSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var result = OwoowSeedFinder.CalculateRetailSeed(request.Observations);
        return Task.FromResult(new RngState(result.Item1, result.Item2));
    }

    public async Task<IReadOnlyList<RngState>> FindRangeAsync(
        RetailRangeSeedRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var observations = request.Observations.Select(value => (byte)(value - '0')).ToArray();
        var total = (ulong)(request.MaximumAdvance - request.MinimumAdvance + 1);
        var results = new List<RngState>();
        progress?.Report(new OperationProgress(OperationState.Running, 0, total, "Searching retail seeds."));

        for (var advance = request.MinimumAdvance; advance <= request.MaximumAdvance; advance++)
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
            progress?.Report(
                new OperationProgress(
                    OperationState.Running,
                    (ulong)(advance - request.MinimumAdvance + 1),
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
        return Task.FromResult(
            new AnimationSequenceResult(
                result.sequence,
                new RngState(result.s0, result.s1)));
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
}
