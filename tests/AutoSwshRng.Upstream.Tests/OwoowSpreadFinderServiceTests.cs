using System.Globalization;
using AutoSwshRng.Core.SpreadFinder;
using owoow.Core.RNG;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowSpreadFinderServiceTests
{
    private const uint ZeroIvSeed = 0x17033091;

    [Test]
    public async Task SearchesExplicitSeedsThroughProjectOwnedContracts()
    {
        ISpreadFinderService service = new OwoowSpreadFinderService();
        var request = CreateRequest(
            new SpreadSearchScope.Seeds([ZeroIvSeed]),
            Enumerable.Repeat(new IndividualValueRange(0, 0), 6));
        var expectedFrame = AssertSingle(
            await owoow.Core.RNG.Generators.Misc.SpreadFinder.Generate(
                [ZeroIvSeed],
                CreateOwoowConfig(request)));

        var results = await service.SearchAsync(request);

        var result = AssertSingle(results);
        Assert.Multiple(() =>
        {
            Assert.That(result.Seed, Is.EqualTo(ZeroIvSeed));
            Assert.That(result.EncryptionConstant, Is.EqualTo(ParseHex(expectedFrame.EC)));
            Assert.That(
                result.IndividualValues,
                Is.EqualTo(new IndividualValues(0, 0, 0, 0, 0, 0)));
            Assert.That(OwoowRngAdapter.GetHeightString(result.Height), Is.EqualTo(expectedFrame.Height));
            Assert.That(result.Scale, Is.EqualTo(GetExpectedScale(result.Height)));
        });
    }

    [Test]
    public void HonorsPreCancelledSearches()
    {
        ISpreadFinderService service = new OwoowSpreadFinderService();
        var request = CreateRequest(
            new SpreadSearchScope.Seeds([ZeroIvSeed]),
            Enumerable.Repeat(IndividualValueRange.Any, 6));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.SearchAsync(request, cancellation.Token));
    }

    [Test]
    public async Task SearchesInclusiveRangesAcrossPartitionsInNumericOrder()
    {
        ISpreadFinderService service = new OwoowSpreadFinderService();
        var request = CreateRequest(
            new SpreadSearchScope.Range(ZeroIvSeed - 1, ZeroIvSeed + 1, partitionCount: 2),
            Enumerable.Repeat(IndividualValueRange.Any, 6));

        var results = await service.SearchAsync(request);

        Assert.That(
            results.Select(result => result.Seed),
            Is.EqualTo(new[] { ZeroIvSeed - 1, ZeroIvSeed, ZeroIvSeed + 1 }));
    }

    [Test]
    public async Task PartitionsRangesAtUIntMaximumWithoutOverflow()
    {
        ISpreadFinderService service = new OwoowSpreadFinderService();
        var request = CreateRequest(
            new SpreadSearchScope.Range(uint.MaxValue - 1, uint.MaxValue, partitionCount: 2),
            Enumerable.Repeat(IndividualValueRange.Any, 6));

        var results = await service.SearchAsync(request);

        Assert.That(
            results.Select(result => result.Seed),
            Is.EqualTo(new[] { uint.MaxValue - 1, uint.MaxValue }));
    }

    [Test]
    public void CancelsLargeRangesWithoutWaitingForOneHugeUpstreamPartition()
    {
        ISpreadFinderService service = new OwoowSpreadFinderService();
        var request = CreateRequest(
            new SpreadSearchScope.Range(0, 100_000_000, partitionCount: 1),
            Enumerable.Repeat(IndividualValueRange.Any, 6));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.SearchAsync(request, cancellation.Token));
    }

    private static SpreadSearchRequest CreateRequest(
        SpreadSearchScope scope,
        IEnumerable<IndividualValueRange> ranges)
    {
        return new SpreadSearchRequest(scope, ranges);
    }

    private static GeneratorConfig CreateOwoowConfig(SpreadSearchRequest request)
    {
        return new GeneratorConfig
        {
            TargetMinIVs = request.IndividualValueRanges.Select(range => (uint)range.Minimum).ToArray(),
            TargetMaxIVs = request.IndividualValueRanges.Select(range => (uint)range.Maximum).ToArray(),
            GuaranteedIVs = request.GuaranteedIndividualValues,
            RareEC = request.RareEncryptionConstant,
            FiltersEnabled = true,
        };
    }

    private static T AssertSingle<T>(IEnumerable<T> values)
    {
        var materialized = values.ToArray();
        Assert.That(materialized, Has.Length.EqualTo(1));
        return materialized[0];
    }

    private static uint ParseHex(string value)
    {
        return uint.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static SpreadScale GetExpectedScale(byte height)
    {
        return height switch
        {
            0 => SpreadScale.XXXS,
            <= 24 => SpreadScale.XXS,
            <= 59 => SpreadScale.XS,
            <= 99 => SpreadScale.Small,
            <= 155 => SpreadScale.Medium,
            <= 195 => SpreadScale.Large,
            <= 230 => SpreadScale.XL,
            <= 254 => SpreadScale.XXL,
            _ => SpreadScale.XXXL,
        };
    }
}
