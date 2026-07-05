using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Rng;

namespace AutoSwshRng.Core.Connection;

public enum ConnectionProtocol
{
    Wifi,
    Usb,
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Faulted,
}

public sealed record OwoowConnectionSettings
{
    public OwoowConnectionSettings(
        ConnectionProtocol protocol,
        string? host,
        int port)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol));
        }

        if (port is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (protocol == ConnectionProtocol.Wifi && string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("A host is required for Wi-Fi connections.", nameof(host));
        }

        Protocol = protocol;
        Host = host?.Trim();
        Port = port;
    }

    public ConnectionProtocol Protocol { get; }
    public string? Host { get; }
    public int Port { get; }
}

public sealed record ConnectionStatus(ConnectionState State, string Message);

public sealed class ConnectionStatusChangedEventArgs(ConnectionStatus status) : EventArgs
{
    public ConnectionStatus Status { get; } = status;
}

public sealed record TrainerSnapshot(
    ushort TrainerId,
    ushort SecretId,
    bool HasShinyCharm,
    bool HasMarkCharm);

public sealed class DexRecommendationSnapshot
{
    private readonly IReadOnlyList<ushort> speciesIds;

    public DexRecommendationSnapshot(
        IEnumerable<ushort> speciesIds,
        string? location,
        ulong? seed)
    {
        ArgumentNullException.ThrowIfNull(speciesIds);
        var speciesIdCopy = speciesIds.ToArray();
        if (speciesIdCopy.Length != 4)
        {
            throw new ArgumentException(
                "Exactly four Pokédex recommendation slots are required.",
                nameof(speciesIds));
        }

        this.speciesIds = Array.AsReadOnly(speciesIdCopy);
        Location = location;
        Seed = seed;
    }

    public IReadOnlyList<ushort> SpeciesIds => speciesIds;
    public string? Location { get; }
    public ulong? Seed { get; }
}

public enum PokemonShinyType
{
    None,
    Star,
    Square,
}

public sealed class PokemonSnapshot
{
    private readonly IReadOnlyList<ushort> moves;

    public PokemonSnapshot(
        ushort speciesId,
        string speciesName,
        byte form,
        byte level,
        PokemonGender gender,
        PokemonShinyType shiny,
        string nature,
        ushort abilityId,
        ushort heldItemId,
        uint encryptionConstant,
        uint personalityId,
        RngIndividualValues individualValues,
        byte height,
        string? mark,
        IEnumerable<ushort> moves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nature);
        ArgumentNullException.ThrowIfNull(individualValues);
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, (byte)100);

        SpeciesId = speciesId;
        SpeciesName = speciesName;
        Form = form;
        Level = level;
        Gender = gender;
        Shiny = shiny;
        Nature = nature;
        AbilityId = abilityId;
        HeldItemId = heldItemId;
        EncryptionConstant = encryptionConstant;
        PersonalityId = personalityId;
        IndividualValues = individualValues;
        Height = height;
        Mark = mark;
        this.moves = Array.AsReadOnly(moves.ToArray());
    }

    public ushort SpeciesId { get; }
    public string SpeciesName { get; }
    public byte Form { get; }
    public byte Level { get; }
    public PokemonGender Gender { get; }
    public PokemonShinyType Shiny { get; }
    public string Nature { get; }
    public ushort AbilityId { get; }
    public ushort HeldItemId { get; }
    public uint EncryptionConstant { get; }
    public uint PersonalityId { get; }
    public RngIndividualValues IndividualValues { get; }
    public byte Height { get; }
    public string? Mark { get; }
    public IReadOnlyList<ushort> Moves => moves;
}

public sealed record WorldPosition(float X, float Y, float Z);

public sealed record FieldObjectSnapshot(
    WorldPosition Position,
    uint FixedSeed,
    PokemonSnapshot Pokemon);

public sealed class WorldSnapshot
{
    private readonly IReadOnlyList<FieldObjectSnapshot> objects;

    public WorldSnapshot(
        ulong saveLocation,
        WorldPosition playerPosition,
        IEnumerable<FieldObjectSnapshot> objects)
    {
        ArgumentNullException.ThrowIfNull(playerPosition);
        ArgumentNullException.ThrowIfNull(objects);
        SaveLocation = saveLocation;
        PlayerPosition = playerPosition;
        this.objects = Array.AsReadOnly(objects.ToArray());
    }

    public ulong SaveLocation { get; }
    public WorldPosition PlayerPosition { get; }
    public IReadOnlyList<FieldObjectSnapshot> Objects => objects;
}

public sealed record RngWatchRequest
{
    public RngWatchRequest(TimeSpan interval, bool changedOnly = true)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Watch interval must be positive.");
        }

        Interval = interval;
        ChangedOnly = changedOnly;
    }

    public TimeSpan Interval { get; }
    public bool ChangedOnly { get; }
}

public sealed record RngStateUpdate(RngState State, DateTimeOffset ObservedAt);

public interface IOwoowConnectionService
{
    ConnectionStatus Status { get; }
    event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;

    Task<ConnectionStatus> ConnectAsync(
        OwoowConnectionSettings settings,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<RngState> ReadRngStateAsync(CancellationToken cancellationToken = default);

    Task WriteRngStateAsync(
        RngState state,
        CancellationToken cancellationToken = default);

    Task<TrainerSnapshot> ReadTrainerAsync(CancellationToken cancellationToken = default);

    Task<DexRecommendationSnapshot> ReadDexRecommendationAsync(
        bool full,
        CancellationToken cancellationToken = default);

    Task<PokemonSnapshot> ReadWildPokemonAsync(CancellationToken cancellationToken = default);

    Task<WorldSnapshot> ReadWorldAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<RngStateUpdate> WatchRngStateAsync(
        RngWatchRequest request,
        CancellationToken cancellationToken = default);

    Task SkipDayAsync(CancellationToken cancellationToken = default);
    Task SkipDayBackAsync(CancellationToken cancellationToken = default);
    Task ResetNetworkTimeAsync(CancellationToken cancellationToken = default);
    Task<ulong> GetCurrentTimeAsync(CancellationToken cancellationToken = default);
    Task SetCurrentTimeAsync(
        ulong value,
        CancellationToken cancellationToken = default);
}
