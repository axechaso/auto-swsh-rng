using AutoSwshRng.Core.SpreadFinder;

namespace AutoSwshRng.Core.Tests.SpreadFinder;

public class SpreadSearchRequestTests
{
    [TestCase(32, 32)]
    [TestCase(0, 32)]
    public void IndividualValueRangeRejectsValuesAboveThirtyOne(byte minimum, byte maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndividualValueRange(minimum, maximum));
    }

    [Test]
    public void IndividualValueRangeRejectsReversedBounds()
    {
        Assert.Throws<ArgumentException>(() => new IndividualValueRange(20, 10));
    }

    [TestCase(5)]
    [TestCase(7)]
    public void RequestRequiresExactlySixIndividualValueRanges(int count)
    {
        var ranges = Enumerable.Repeat(IndividualValueRange.Any, count);

        Assert.Throws<ArgumentException>(() =>
            new SpreadSearchRequest(new SpreadSearchScope.Seeds([0]), ranges));
    }

    [TestCase(-1)]
    [TestCase(7)]
    public void RequestRejectsInvalidGuaranteedIndividualValueCounts(int guaranteedIndividualValues)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SpreadSearchRequest(
                new SpreadSearchScope.Seeds([0]),
                AnyIndividualValueRanges(),
                guaranteedIndividualValues));
    }

    [Test]
    public void SeedScopeRequiresAtLeastOneSeed()
    {
        Assert.Throws<ArgumentException>(() => new SpreadSearchScope.Seeds([]));
    }

    [Test]
    public void RangeScopeRejectsReversedBounds()
    {
        Assert.Throws<ArgumentException>(() => new SpreadSearchScope.Range(2, 1));
    }

    [TestCase(0)]
    [TestCase(65)]
    public void RangeScopeRejectsInvalidPartitionCounts(int partitionCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSearchScope.Range(0, 1, partitionCount));
    }

    [TestCase(0)]
    [TestCase(65)]
    public void EntireSpaceScopeRejectsInvalidPartitionCounts(int partitionCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpreadSearchScope.EntireSpace(partitionCount));
    }

    [Test]
    public void RequestCopiesIndividualValueRanges()
    {
        var ranges = AnyIndividualValueRanges();
        var request = new SpreadSearchRequest(new SpreadSearchScope.Seeds([0]), ranges);

        ranges[0] = new IndividualValueRange(31, 31);

        Assert.That(request.IndividualValueRanges[0], Is.EqualTo(IndividualValueRange.Any));
    }

    [Test]
    public void SeedScopeCopiesSeedValues()
    {
        uint[] seeds = [1, 2];
        var scope = new SpreadSearchScope.Seeds(seeds);

        seeds[0] = 99;

        Assert.That(scope.Values, Is.EqualTo(new uint[] { 1, 2 }));
    }

    [Test]
    public void ValidRequestPreservesSearchOptions()
    {
        var scope = new SpreadSearchScope.Range(10, 20, partitionCount: 2);
        var request = new SpreadSearchRequest(
            scope,
            AnyIndividualValueRanges(),
            guaranteedIndividualValues: 3,
            rareEncryptionConstant: true,
            scale: SpreadScale.XXXL);

        Assert.Multiple(() =>
        {
            Assert.That(request.Scope, Is.SameAs(scope));
            Assert.That(request.GuaranteedIndividualValues, Is.EqualTo(3));
            Assert.That(request.RareEncryptionConstant, Is.True);
            Assert.That(request.Scale, Is.EqualTo(SpreadScale.XXXL));
        });
    }

    private static IndividualValueRange[] AnyIndividualValueRanges()
    {
        return Enumerable.Repeat(IndividualValueRange.Any, 6).ToArray();
    }
}
