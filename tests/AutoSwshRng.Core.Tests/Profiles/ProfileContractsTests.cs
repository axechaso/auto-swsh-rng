using AutoSwshRng.Core.Profiles;

namespace AutoSwshRng.Core.Tests.Profiles;

public class ProfileContractsTests
{
    [Test]
    public void ProfilePreservesTrainerConfiguration()
    {
        var profile = new RngProfile("Sword save", GameVersion.Sword, 1337, 1390, true, true);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("Sword save"));
            Assert.That(profile.Game, Is.EqualTo(GameVersion.Sword));
            Assert.That(profile.TrainerId, Is.EqualTo(1337));
            Assert.That(profile.SecretId, Is.EqualTo(1390));
            Assert.That(profile.HasShinyCharm, Is.True);
            Assert.That(profile.HasMarkCharm, Is.True);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ProfileRequiresName(string name)
    {
        Assert.Throws<ArgumentException>(
            () => new RngProfile(name, GameVersion.Sword, 0, 0, false, false));
    }

    [TestCase(-1)]
    [TestCase(65536)]
    public void ProfileRejectsTrainerIdOutsideUnsignedSixteenBitRange(int trainerId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RngProfile("save", GameVersion.Sword, trainerId, 0, false, false));
    }

    [TestCase(-1)]
    [TestCase(65536)]
    public void ProfileRejectsSecretIdOutsideUnsignedSixteenBitRange(int secretId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RngProfile("save", GameVersion.Sword, 0, secretId, false, false));
    }

    [Test]
    public void SettingsCopyProfiles()
    {
        var profiles = new List<RngProfile>
        {
            new("save", GameVersion.Shield, 1, 2, false, true),
        };

        var settings = new RngApplicationSettings("save", profiles);
        profiles.Clear();

        Assert.That(settings.Profiles, Has.Count.EqualTo(1));
    }
}
