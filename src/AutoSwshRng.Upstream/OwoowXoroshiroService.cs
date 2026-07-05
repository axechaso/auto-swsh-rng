using AutoSwshRng.Core.Rng;
using PKHeX.Core;
using OwoowFixed = owoow.Core.RNG.Generators.Fixed;
using OwoowUtil = owoow.Core.RNG.Util;

namespace AutoSwshRng.Upstream;

public sealed class OwoowXoroshiroService : IXoroshiroService
{
    public Task<XoroshiroResult> CalculateAsync(
        XoroshiroRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var state = request.State;
        ulong? value = null;
        var distance = request.Amount;
        var found = true;

        switch (request.Operation)
        {
            case XoroshiroOperation.Next:
                state = Map(OwoowUtil.XoroshiroJump(state.Seed0, state.Seed1, request.Amount));
                break;
            case XoroshiroOperation.Previous:
                state = Map(
                    OwoowUtil.XoroshiroLongJump(
                        state.Seed0,
                        state.Seed1,
                        UInt128.MaxValue - request.Amount));
                break;
            case XoroshiroOperation.NextInteger:
            {
                var rng = new Xoroshiro128Plus(state.Seed0, state.Seed1);
                value = rng.NextInt(request.Amount);
                var next = rng.GetState();
                distance = OwoowUtil.GetAdvancesPassed(
                    state.Seed0,
                    state.Seed1,
                    next.s0,
                    next.s1,
                    0xFFFF);
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
