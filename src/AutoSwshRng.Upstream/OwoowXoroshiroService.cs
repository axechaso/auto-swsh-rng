using AutoSwshRng.Core.Rng;
using PKHeX.Core;
using OwoowFixed = owoow.Core.RNG.Generators.Fixed;
using OwoowUtil = owoow.Core.RNG.Util;

namespace AutoSwshRng.Upstream;

public sealed class OwoowXoroshiroService : IXoroshiroService, IRngStatePositionService
{
    public Task<XoroshiroResult> CalculateAsync(
        XoroshiroRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var state = request.State;
        ulong? value = null;
        ulong? distance = request.Amount;
        var found = true;

        switch (request.Operation)
        {
            case XoroshiroOperation.Next:
                state = Map(OwoowUtil.XoroshiroJump(state.Seed0, state.Seed1, request.Amount));
                break;
            case XoroshiroOperation.Previous:
                if (request.Amount > 0)
                {
                    state = Map(
                        OwoowUtil.XoroshiroLongJump(
                            state.Seed0,
                            state.Seed1,
                            UInt128.MaxValue - request.Amount));
                }
                break;
            case XoroshiroOperation.NextInteger:
            {
                var rng = new Xoroshiro128Plus(state.Seed0, state.Seed1);
                value = rng.NextInt(request.Amount);
                var next = rng.GetState();
                var measuredDistance = OwoowUtil.GetAdvancesPassed(
                    state.Seed0,
                    state.Seed1,
                    next.s0,
                    next.s1,
                    0xFFFF);
                var measuredEnd = OwoowUtil.XoroshiroJump(
                    state.Seed0,
                    state.Seed1,
                    measuredDistance);
                found = measuredEnd.s0 == next.s0 && measuredEnd.s1 == next.s1;
                distance = found ? measuredDistance : null;
                state = new RngState(next.s0, next.s1);
                break;
            }
            case XoroshiroOperation.FindInitial:
            {
                var rng = new Xoroshiro128Plus(state.Seed0, state.Seed1);
                found = false;
                for (ulong index = 1; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    rng.Prev();
                    var previous = rng.GetState();
                    if (previous.s1 == OwoowUtil.XOROSHIRO_CONST)
                    {
                        state = new RngState(previous.s0, previous.s1);
                        distance = index;
                        found = true;
                        break;
                    }

                    if (index == request.Amount)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    distance = null;
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Operation,
                    "Unknown Xoroshiro operation.");
        }

        return Task.FromResult(new XoroshiroResult(state, distance, value, found));
    }

    public Task<RngState> AdvanceAsync(
        RngState state,
        ulong amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount == 0)
        {
            return Task.FromResult(state);
        }

        var advanced = OwoowUtil.XoroshiroJump(state.Seed0, state.Seed1, amount);
        return Task.FromResult(new RngState(advanced.s0, advanced.s1));
    }

    public Task<RngDistanceResult> MeasureDistanceAsync(
        RngDistanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Before == request.After)
        {
            return Task.FromResult(
                new RngDistanceResult(
                    Found: true,
                    Distance: 0,
                    request.MaximumDistance,
                    request.After));
        }

        if (request.MaximumDistance == 0)
        {
            return Task.FromResult(
                new RngDistanceResult(
                    Found: false,
                    Distance: null,
                    request.MaximumDistance,
                    request.Before));
        }

        var measured = OwoowUtil.GetAdvancesPassed(
            request.Before.Seed0,
            request.Before.Seed1,
            request.After.Seed0,
            request.After.Seed1,
            request.MaximumDistance);
        cancellationToken.ThrowIfCancellationRequested();
        var verifiedTuple = OwoowUtil.XoroshiroJump(
            request.Before.Seed0,
            request.Before.Seed1,
            measured);
        var verifiedEnd = new RngState(verifiedTuple.s0, verifiedTuple.s1);
        var found = verifiedEnd == request.After;
        return Task.FromResult(
            new RngDistanceResult(
                found,
                found ? measured : null,
                request.MaximumDistance,
                found ? verifiedEnd : request.Before));
    }

    public Task<FixedSeedResult> GenerateFixedAsync(
        FixedSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var rng = new Xoroshiro128Plus(request.Seed);
        var encryptionConstant = OwoowFixed.GenerateEC(ref rng);
        var personalityId = OwoowFixed.GeneratePID(
            ref rng,
            request.Shiny,
            request.TrainerShinyValue);
        var (_, individualValues) = OwoowFixed.GenerateIVs(
            ref rng,
            0,
            new owoow.Core.RNG.GeneratorConfig
            {
                GuaranteedIVs = request.GuaranteedIndividualValues,
            });
        var height = OwoowFixed.GenerateHeightWeightScale(ref rng);

        return Task.FromResult(
            new FixedSeedResult(
                encryptionConstant,
                personalityId,
                new RngIndividualValues(individualValues),
                (byte)height));
    }

    private static RngState Map((ulong s0, ulong s1) state) =>
        new(state.s0, state.s1);
}
