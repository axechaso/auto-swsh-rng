using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Tests.Rng;

public class RngContractsTests
{
    [Test]
    public void XoroshiroRequestRejectsUnknownOperation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new XoroshiroRequest(
                new RngState(1, 2),
                (XoroshiroOperation)999,
                1));
    }

    [Test]
    public void CalibrationRequestsRejectUnknownWeather()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MenuCloseCalibrationRequest(
                    new RngState(1, 2),
                    0,
                    false,
                    (Weather)999));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FlyCalibrationRequest(
                    new RngState(1, 2),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false,
                    (Weather)999,
                    0));
        });
    }

    [Test]
    public void CalibrationRequestsRejectSpecialToolAnyWeatherSentinel()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new MenuCloseCalibrationRequest(
                    new RngState(1, 2),
                    3,
                    false,
                    Weather.Any));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FlyCalibrationRequest(
                    new RngState(1, 2),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    false,
                    Weather.Any,
                    0));
        });
    }

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

    [Test]
    public void AnimationObservationsCannotBeMutatedThroughPublicContract()
    {
        var result = new AnimationSequenceResult([0, 1], new RngState(1, 2));

        Assert.Throws<NotSupportedException>(
            () => ((IList<byte>)result.Observations)[0] = 1);
    }
}
