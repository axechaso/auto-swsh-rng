using AutoSwshRng.Core.Rng;
using owoow.Core.Enums;
using owoow.Core.RNG.Generators.Misc;
using PKHeX.Core;
using OwoowEnvironment = owoow.Core.RNG.Generators.Misc.Environment;
using OwoowFixed = owoow.Core.RNG.Generators.Fixed;
using OwoowUtil = owoow.Core.RNG.Util;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowRngServicesTests
{
    [Test]
    public async Task ForwardJumpMatchesOriginal()
    {
        var state = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        var actual = await new OwoowXoroshiroService().CalculateAsync(
            new XoroshiroRequest(state, XoroshiroOperation.Next, 1234));
        var expected = OwoowUtil.XoroshiroJump(state.Seed0, state.Seed1, 1234);

        Assert.That(actual.State, Is.EqualTo(new RngState(expected.s0, expected.s1)));
    }

    [Test]
    public async Task PreviousJumpMatchesOriginal()
    {
        var state = new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321);
        var actual = await new OwoowXoroshiroService().CalculateAsync(
            new XoroshiroRequest(state, XoroshiroOperation.Previous, 7));
        var expected = OwoowUtil.XoroshiroLongJump(
            state.Seed0,
            state.Seed1,
            UInt128.MaxValue - 7);

        Assert.That(actual.State, Is.EqualTo(new RngState(expected.s0, expected.s1)));
    }

    [Test]
    public async Task NextIntegerMatchesOriginal()
    {
        var state = new RngState(11, 22);
        var original = new Xoroshiro128Plus(state.Seed0, state.Seed1);
        var expectedValue = original.NextInt(100);
        var expectedState = original.GetState();

        var actual = await new OwoowXoroshiroService().CalculateAsync(
            new XoroshiroRequest(state, XoroshiroOperation.NextInteger, 100));

        Assert.Multiple(() =>
        {
            Assert.That(actual.Value, Is.EqualTo(expectedValue));
            Assert.That(
                actual.State,
                Is.EqualTo(new RngState(expectedState.s0, expectedState.s1)));
        });
    }

    [Test]
    public async Task FixedSeedValuesMatchOriginal()
    {
        const uint seed = 0x17033091;
        var request = new FixedSeedRequest(seed, true, 42, 2);
        var actual = await new OwoowXoroshiroService().GenerateFixedAsync(request);
        var original = new Xoroshiro128Plus(seed);
        var ec = OwoowFixed.GenerateEC(ref original);
        var pid = OwoowFixed.GeneratePID(ref original, true, 42);
        var (_, ivs) = OwoowFixed.GenerateIVs(
            ref original,
            0,
            new owoow.Core.RNG.GeneratorConfig { GuaranteedIVs = 2 });
        var height = OwoowFixed.GenerateHeightWeightScale(ref original);

        Assert.Multiple(() =>
        {
            Assert.That(actual.EncryptionConstant, Is.EqualTo(ec));
            Assert.That(actual.PersonalityId, Is.EqualTo(pid));
            Assert.That(actual.IndividualValues.ToArray(), Is.EqualTo(ivs));
            Assert.That(actual.Height, Is.EqualTo(height));
        });
    }

    [Test]
    public async Task RetailSeedMatchesOriginal()
    {
        var observations = string.Concat(
            Enumerable.Range(0, 128).Select(index => index % 3 == 0 ? '1' : '0'));

        var actual = await new OwoowRetailSeedService().FindExactAsync(
            new RetailSeedRequest(observations));
        var expected = SeedFinder.CalculateRetailSeed(observations);

        Assert.That(actual, Is.EqualTo(new RngState(expected.Item1, expected.Item2)));
    }

    [Test]
    public async Task AnimationGenerationAndReidentificationMatchOriginal()
    {
        var state = new RngState(123, 456);
        var service = new OwoowRetailSeedService();
        var actualSequence = await service.GenerateAnimationSequenceAsync(
            new AnimationSequenceRequest(state, 5, 100));
        var expectedSequence = SeedFinder.GenerateAnimationSequence(123, 456, 5, 100);
        var pattern = string.Concat(expectedSequence.sequence.Skip(20).Take(10));

        var actualIdentity = await service.ReidentifyAsync(
            new ReidentifySeedRequest(
                actualSequence.Observations,
                state,
                pattern));
        var expectedIdentity = SeedFinder.ReidentifySeed(
            expectedSequence.sequence,
            123,
            456,
            pattern);

        Assert.Multiple(() =>
        {
            Assert.That(actualSequence.Observations, Is.EqualTo(expectedSequence.sequence));
            Assert.That(actualIdentity.Hits, Is.EqualTo(expectedIdentity.hits));
            Assert.That(actualIdentity.Advances, Is.EqualTo(expectedIdentity.advances));
            Assert.That(
                actualIdentity.State,
                Is.EqualTo(new RngState(expectedIdentity.s0, expectedIdentity.s1)));
        });
    }

    [Test]
    public async Task MenuCloseMatchesOriginal()
    {
        var request = new MenuCloseCalibrationRequest(
            new RngState(0x1234, 0x5678),
            3,
            true,
            AutoSwshRng.Core.Rng.Weather.Thunderstorm);
        var actual = await new OwoowCalibrationService().CalculateMenuCloseAsync(request);
        var original = new Xoroshiro128Plus(0x1234, 0x5678);
        var expected = MenuClose.GetAdvances(
            ref original,
            3,
            true,
            WeatherType.Thunderstorm);

        Assert.That(actual.Advances, Is.EqualTo(expected));
    }

    [Test]
    public async Task EnvironmentCalibrationMatchesOriginal()
    {
        var state = new RngState(12, 34);
        var service = new OwoowCalibrationService();
        var rain = await service.CalculateRainAsync(new RainCalibrationRequest(state, 6));
        var area = await service.CalculateAreaLoadAsync(
            new AreaLoadCalibrationRequest(state, 4, 3));
        var rainRng = new Xoroshiro128Plus(12, 34);
        var expectedRain = OwoowEnvironment.GetRainAdvances(ref rainRng, 6);
        var areaRng = new Xoroshiro128Plus(12, 34);
        var expectedArea = OwoowEnvironment.GetAreaLoadAdvances(ref areaRng, 4)
            + OwoowEnvironment.GetAreaLoadNPCAdvances(ref areaRng, 3);

        Assert.Multiple(() =>
        {
            Assert.That(rain.Advances, Is.EqualTo(expectedRain));
            Assert.That(area.Advances, Is.EqualTo(expectedArea));
        });
    }

    [Test]
    public void ServicesHonorPreCancelledToken()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await new OwoowCalibrationService().CalculateRainAsync(
                new RainCalibrationRequest(new RngState(1, 2), 1),
                source.Token));
    }
}
