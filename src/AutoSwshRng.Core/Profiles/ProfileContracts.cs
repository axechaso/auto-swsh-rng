namespace AutoSwshRng.Core.Profiles;

public enum GameVersion
{
    Sword,
    Shield,
}

public sealed record RngProfile
{
    public RngProfile(
        string name,
        GameVersion game,
        int trainerId,
        int secretId,
        bool hasShinyCharm,
        bool hasMarkCharm)
    {
        if (!Enum.IsDefined(game))
        {
            throw new ArgumentOutOfRangeException(nameof(game));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name is required.", nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(trainerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trainerId, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(secretId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secretId, ushort.MaxValue);

        Name = name.Trim();
        Game = game;
        TrainerId = (ushort)trainerId;
        SecretId = (ushort)secretId;
        HasShinyCharm = hasShinyCharm;
        HasMarkCharm = hasMarkCharm;
    }

    public string Name { get; }
    public GameVersion Game { get; }
    public ushort TrainerId { get; }
    public ushort SecretId { get; }
    public bool HasShinyCharm { get; }
    public bool HasMarkCharm { get; }
}

public sealed class RngApplicationSettings : IEquatable<RngApplicationSettings>
{
    private readonly IReadOnlyList<RngProfile> profiles;

    public RngApplicationSettings(
        string? activeProfileName,
        IEnumerable<RngProfile> profiles,
        int maxSearchTasksPowerOfTwo = 2)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSearchTasksPowerOfTwo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSearchTasksPowerOfTwo, 6);

        var profileCopy = profiles.ToArray();
        if (profileCopy.Any(profile => profile is null))
        {
            throw new ArgumentException(
                "Profile list cannot contain null entries.",
                nameof(profiles));
        }

        if (profileCopy.Select(profile => profile.Name).Distinct(StringComparer.Ordinal).Count()
            != profileCopy.Length)
        {
            throw new ArgumentException("Profile names must be unique.", nameof(profiles));
        }

        if (activeProfileName is not null
            && !profileCopy.Any(profile => profile.Name == activeProfileName))
        {
            throw new ArgumentException("The active profile must exist in the profile list.", nameof(activeProfileName));
        }

        this.profiles = Array.AsReadOnly(profileCopy);
        ActiveProfileName = activeProfileName;
        MaxSearchTasksPowerOfTwo = maxSearchTasksPowerOfTwo;
    }

    public string? ActiveProfileName { get; }
    public IReadOnlyList<RngProfile> Profiles => profiles;
    public int MaxSearchTasksPowerOfTwo { get; }

    public bool Equals(RngApplicationSettings? other)
    {
        return other is not null
            && ActiveProfileName == other.ActiveProfileName
            && MaxSearchTasksPowerOfTwo == other.MaxSearchTasksPowerOfTwo
            && profiles.SequenceEqual(other.profiles);
    }

    public override bool Equals(object? obj) => obj is RngApplicationSettings other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ActiveProfileName);
        hash.Add(MaxSearchTasksPowerOfTwo);
        foreach (var profile in profiles)
        {
            hash.Add(profile);
        }

        return hash.ToHashCode();
    }
}

public interface IProfileStore
{
    Task<RngApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        RngApplicationSettings settings,
        CancellationToken cancellationToken = default);
}
