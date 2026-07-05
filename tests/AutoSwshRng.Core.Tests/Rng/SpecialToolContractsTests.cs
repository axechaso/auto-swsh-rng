using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Tests.Rng;

public class SpecialToolContractsTests
{
    [Test]
    public void RejectsInvalidAdvanceRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialToolSearchRequest(
            SpecialToolKind.WailordRespawn,
            new RngState(1, 2),
            10,
            9));
    }

    [Test]
    public void RejectsAdvanceRangeWhoseInclusiveLengthOverflows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialToolSearchRequest(
            SpecialToolKind.WailordRespawn,
            new RngState(1, 2),
            0,
            ulong.MaxValue));
    }

    [Test]
    public void LotoIdsMustContainSixDigits()
    {
        Assert.Throws<ArgumentException>(() => new SpecialToolSearchRequest(
            SpecialToolKind.LotoId,
            new RngState(1, 2),
            0,
            1,
            lotoIds: ["12345"]));
    }

    [Test]
    public void CramOMaticRequiresFourInputs()
    {
        Assert.Throws<ArgumentException>(() => new SpecialToolSearchRequest(
            SpecialToolKind.CramOMatic,
            new RngState(1, 2),
            0,
            1,
            cramInputs: [CramInputItem.BlackApricorn]));
    }

    [Test]
    public void RejectsUnknownFilterEnums()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialToolSearchRequest(
                SpecialToolKind.LotoId,
                new RngState(1, 2),
                0,
                1,
                lotoPrize: (LotoPrizeFilter)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialToolSearchRequest(
                SpecialToolKind.CramOMatic,
                new RngState(1, 2),
                0,
                1,
                success: (SuccessFilter)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialToolSearchRequest(
                SpecialToolKind.CramOMatic,
                new RngState(1, 2),
                0,
                1,
                cramPrize: (CramPrizeFilter)999));
        });
    }

    [Test]
    public void MenuCloseRejectsUnknownWeather()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpecialToolMenuClose(false, 0, false, (Weather)999));
    }

    [Test]
    public void LotoIdsRejectNullEntriesAsValidationErrors()
    {
        Assert.Throws<ArgumentException>(() => new SpecialToolSearchRequest(
            SpecialToolKind.LotoId,
            new RngState(1, 2),
            0,
            1,
            lotoIds: [null!]));
    }

    [Test]
    public void CopiesMutableInputs()
    {
        var inputs = Enumerable.Repeat(CramInputItem.BlackApricorn, 4).ToArray();
        var minimums = new Dictionary<DiggingBroReward, byte>
        {
            [DiggingBroReward.BottleCap] = 2,
        };
        var request = new SpecialToolSearchRequest(
            SpecialToolKind.CramOMatic,
            new RngState(1, 2),
            0,
            1,
            game: GameVersion.Shield,
            cramInputs: inputs,
            diggingBroMinimumRewards: minimums);

        inputs[0] = CramInputItem.SweetIngredient;
        minimums[DiggingBroReward.BottleCap] = 9;

        Assert.Multiple(() =>
        {
            Assert.That(request.CramInputs[0], Is.EqualTo(CramInputItem.BlackApricorn));
            Assert.That(
                request.DiggingBroMinimumRewards[DiggingBroReward.BottleCap],
                Is.EqualTo(2));
        });
    }

    [Test]
    public void ArrayInputsCannotBeMutatedThroughPublicContract()
    {
        var request = new SpecialToolSearchRequest(
            SpecialToolKind.CramOMatic,
            new RngState(1, 2),
            0,
            1,
            lotoIds: ["123456"],
            cramInputs: Enumerable.Repeat(CramInputItem.BlackApricorn, 4).ToArray());

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(
                () => ((IList<string>)request.LotoIds)[0] = "654321");
            Assert.Throws<NotSupportedException>(
                () => ((IList<CramInputItem>)request.CramInputs)[0] =
                    CramInputItem.BlueApricorn);
        });
    }
}
