using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Tests.Rng;

public class RngContractsTests
{
    [Test]
    public void RetailSeedRequiresExactlyOneHundredTwentyEightObservations()
    {
        Assert.Throws<ArgumentException>(() => new RetailSeedRequest("0101"));
    }

    [TestCase('2')]
    [TestCase('P')]
    public void RetailSeedRejectsNonBinaryObservation(char invalid)
    {
        var observations = new string('0', 127) + invalid;
        Assert.Throws<ArgumentException>(() => new RetailSeedRequest(observations));
    }

    [Test]
    public void RetailRangeRequiresAtLeastSixtyFourObservations()
    {
        Assert.Throws<ArgumentException>(
            () => new RetailRangeSeedRequest(new string('0', 63), 0, 10));
    }

    [Test]
    public void RetailRangeRejectsReversedBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RetailRangeSeedRequest(new string('0', 64), 11, 10));
    }

    [TestCase(-1)]
    [TestCase(7)]
    public void FixedSeedRejectsInvalidGuaranteedIndividualValueCount(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FixedSeedRequest(1, false, 0, count));
    }

    [Test]
    public void AnimationSequenceCopiesObservations()
    {
        var observations = new byte[] { 0, 1, 0, 1 };
        var result = new AnimationSequenceResult(observations, new RngState(1, 2));
        observations[0] = 1;

        Assert.That(result.Observations[0], Is.Zero);
    }
}
