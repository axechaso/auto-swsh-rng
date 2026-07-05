using System.Runtime.CompilerServices;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Rng;
using owoow.Core.Connection;
using PKHeX.Core;
using SysBot.Base;

namespace AutoSwshRng.Upstream;

internal sealed record OwoowTrainerData(
    ushort TrainerId,
    ushort SecretId,
    bool HasShinyCharm,
    bool HasMarkCharm);

internal sealed class OwoowDexRecommendationData(
    IEnumerable<ushort> speciesIds,
    string? location,
    ulong? seed)
{
    public ushort[] SpeciesIds { get; } = speciesIds.ToArray();
    public string? Location { get; } = location;
    public ulong? Seed { get; } = seed;
}

internal sealed record OwoowFieldObjectData(
    float X,
    float Y,
    float Z,
    uint FixedSeed,
    PK8 Pokemon);

internal sealed class OwoowWorldData(
    ulong saveLocation,
    float playerX,
    float playerY,
    float playerZ,
    IEnumerable<OwoowFieldObjectData> objects)
{
    public ulong SaveLocation { get; } = saveLocation;
    public float PlayerX { get; } = playerX;
    public float PlayerY { get; } = playerY;
    public float PlayerZ { get; } = playerZ;
    public IReadOnlyList<OwoowFieldObjectData> Objects { get; } = objects.ToArray();
}

internal interface IOwoowConnectionBridgeFactory
{
    IOwoowConnectionBridge Create(
        OwoowConnectionSettings settings,
        Action<string> statusUpdate);
}

internal interface IOwoowConnectionBridge
{
    bool Connected { get; }
    Task<(bool Success, string Error)> ConnectAsync(CancellationToken token);
    Task<(bool Success, string Error)> DisconnectAsync(CancellationToken token);
    Task<RngState> ReadRngStateAsync(CancellationToken token);
    Task WriteRngStateAsync(RngState state, CancellationToken token);
    OwoowTrainerData ReadTrainer();
    Task<OwoowDexRecommendationData> ReadDexRecommendationAsync(
        bool full,
        CancellationToken token);
    Task<PK8> ReadWildPokemonAsync(CancellationToken token);
    Task<OwoowWorldData> ReadWorldAsync(CancellationToken token);
    Task SkipDayAsync(CancellationToken token);
    Task SkipDayBackAsync(CancellationToken token);
    Task ResetNetworkTimeAsync(CancellationToken token);
    Task<ulong> GetCurrentTimeAsync(CancellationToken token);
    Task SetCurrentTimeAsync(ulong value, CancellationToken token);
}

internal sealed class OwoowConnectionBridgeFactory : IOwoowConnectionBridgeFactory
{
    public IOwoowConnectionBridge Create(
        OwoowConnectionSettings settings,
        Action<string> statusUpdate)
    {
        var config = new SwitchConnectionConfig
        {
            IP = settings.Host ?? string.Empty,
            Port = settings.Port,
            Protocol = settings.Protocol == ConnectionProtocol.Usb
                ? SwitchProtocol.USB
                : SwitchProtocol.WiFi,
        };
        return new OwoowConnectionBridge(
            new ConnectionWrapperAsync(config, statusUpdate));
    }
}

internal sealed class OwoowConnectionBridge(ConnectionWrapperAsync wrapper)
    : IOwoowConnectionBridge
{
    public bool Connected => wrapper.Connected;

    public Task<(bool Success, string Error)> ConnectAsync(CancellationToken token) =>
        wrapper.Connect(token);

    public Task<(bool Success, string Error)> DisconnectAsync(CancellationToken token) =>
        wrapper.DisconnectAsync(token);

    public async Task<RngState> ReadRngStateAsync(CancellationToken token)
    {
        var state = await wrapper.ReadRNGState(token).ConfigureAwait(false);
        return new RngState(state.Item1, state.Item2);
    }

    public Task WriteRngStateAsync(RngState state, CancellationToken token) =>
        wrapper.WriteRNGState(state.Seed0, state.Seed1, token);

    public OwoowTrainerData ReadTrainer()
    {
        var (trainerId, secretId) = wrapper.GetIDs();
        return new OwoowTrainerData(
            ushort.Parse(trainerId, System.Globalization.CultureInfo.InvariantCulture),
            ushort.Parse(secretId, System.Globalization.CultureInfo.InvariantCulture),
            wrapper.GetHasShinyCharm(),
            wrapper.GetHasMarkCharm());
    }

    public async Task<OwoowDexRecommendationData> ReadDexRecommendationAsync(
        bool full,
        CancellationToken token)
    {
        if (!full)
        {
            var species = await wrapper.ReadDexRecommendation(token).ConfigureAwait(false);
            return new OwoowDexRecommendationData(species, null, null);
        }

        var recommendation = await wrapper.ReadDexRecommendationFull(token)
            .ConfigureAwait(false);
        return new OwoowDexRecommendationData(
            [
                recommendation.Species1,
                recommendation.Species2,
                recommendation.Species3,
                recommendation.Species4,
            ],
            recommendation.Location,
            recommendation.Seed);
    }

    public Task<PK8> ReadWildPokemonAsync(CancellationToken token) =>
        wrapper.ReadWildPokemon(token);

    public async Task<OwoowWorldData> ReadWorldAsync(CancellationToken token)
    {
        await wrapper.ReadKCoordinatesAsync(token).ConfigureAwait(false);
        var saveLocation = await wrapper.ReadSaveLocation(token).ConfigureAwait(false);
        var (fieldObjects, playerX, playerY, playerZ) = wrapper.ParseCoordinatesBlock();
        return new OwoowWorldData(
            saveLocation,
            playerX,
            playerY,
            playerZ,
            fieldObjects
                .Where(fieldObject => fieldObject.PK8 is not null)
                .Select(fieldObject => new OwoowFieldObjectData(
                    fieldObject.X,
                    fieldObject.Y,
                    fieldObject.Z,
                    fieldObject.FixedSeed,
                    fieldObject.PK8!)));
    }

    public Task SkipDayAsync(CancellationToken token) => wrapper.DaySkip(token);
    public Task SkipDayBackAsync(CancellationToken token) => wrapper.DaySkipBack(token);
    public Task ResetNetworkTimeAsync(CancellationToken token) => wrapper.ResetTimeNTP(token);
    public Task<ulong> GetCurrentTimeAsync(CancellationToken token) =>
        wrapper.GetCurrentTime(token);
    public Task SetCurrentTimeAsync(ulong value, CancellationToken token) =>
        wrapper.SetCurrentTime(value, token);
}

public sealed class OwoowConnectionService : IOwoowConnectionService
{
    private readonly IOwoowConnectionBridgeFactory bridgeFactory;
    private IOwoowConnectionBridge? bridge;
    private ConnectionStatus status =
        new(ConnectionState.Disconnected, "Disconnected.");

    public OwoowConnectionService()
        : this(new OwoowConnectionBridgeFactory())
    {
    }

    internal OwoowConnectionService(IOwoowConnectionBridgeFactory bridgeFactory)
    {
        this.bridgeFactory = bridgeFactory;
    }

    public ConnectionStatus Status => status;

    public event EventHandler<ConnectionStatusChangedEventArgs>? StatusChanged;

    public async Task<ConnectionStatus> ConnectAsync(
        OwoowConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        if (bridge is { Connected: true })
        {
            return Status;
        }

        SetStatus(ConnectionState.Connecting, "Connecting.");
        try
        {
            bridge = bridgeFactory.Create(
                settings,
                message => SetStatus(ConnectionState.Connecting, message));
            var (success, error) = await bridge.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!success)
            {
                SetStatus(ConnectionState.Faulted, error);
                throw new UpstreamOperationException(
                    UpstreamErrorCode.ConnectionFailed,
                    string.IsNullOrWhiteSpace(error)
                        ? "owoow connection failed."
                        : error);
            }

            SetStatus(ConnectionState.Connected, "Connected.");
            return Status;
        }
        catch (OperationCanceledException)
        {
            SetStatus(ConnectionState.Disconnected, "Connection cancelled.");
            throw;
        }
        catch (UpstreamOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetStatus(ConnectionState.Faulted, exception.Message);
            throw new UpstreamOperationException(
                UpstreamErrorCode.ConnectionFailed,
                "owoow connection failed.",
                exception);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (bridge is null)
        {
            SetStatus(ConnectionState.Disconnected, "Disconnected.");
            return;
        }

        SetStatus(ConnectionState.Disconnecting, "Disconnecting.");
        try
        {
            var (success, error) = await bridge.DisconnectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!success)
            {
                SetStatus(ConnectionState.Faulted, error);
                throw new UpstreamOperationException(
                    UpstreamErrorCode.ConnectionFailed,
                    string.IsNullOrWhiteSpace(error)
                        ? "owoow disconnect failed."
                        : error);
            }

            bridge = null;
            SetStatus(ConnectionState.Disconnected, "Disconnected.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UpstreamOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetStatus(ConnectionState.Faulted, exception.Message);
            throw ConvertFailure("owoow disconnect failed.", exception);
        }
    }

    public Task<RngState> ReadRngStateAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.ReadRngStateAsync(cancellationToken),
            "Unable to read the RNG state.");

    public Task WriteRngStateAsync(
        RngState state,
        CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.WriteRngStateAsync(state, cancellationToken),
            "Unable to write the RNG state.");

    public Task<TrainerSnapshot> ReadTrainerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = GetConnectedBridge().ReadTrainer();
        return Task.FromResult(new TrainerSnapshot(
            value.TrainerId,
            value.SecretId,
            value.HasShinyCharm,
            value.HasMarkCharm));
    }

    public async Task<DexRecommendationSnapshot> ReadDexRecommendationAsync(
        bool full,
        CancellationToken cancellationToken = default)
    {
        var value = await ExecuteConnectedAsync(
                bridge => bridge.ReadDexRecommendationAsync(full, cancellationToken),
                "Unable to read Pokédex recommendations.")
            .ConfigureAwait(false);
        return new DexRecommendationSnapshot(
            value.SpeciesIds,
            value.Location,
            value.Seed);
    }

    public async Task<PokemonSnapshot> ReadWildPokemonAsync(
        CancellationToken cancellationToken = default)
    {
        var pokemon = await ExecuteConnectedAsync(
                bridge => bridge.ReadWildPokemonAsync(cancellationToken),
                "Unable to read the wild Pokémon.")
            .ConfigureAwait(false);
        return MapPokemon(pokemon);
    }

    public async Task<WorldSnapshot> ReadWorldAsync(
        CancellationToken cancellationToken = default)
    {
        var world = await ExecuteConnectedAsync(
                bridge => bridge.ReadWorldAsync(cancellationToken),
                "Unable to read field objects.")
            .ConfigureAwait(false);
        return new WorldSnapshot(
            world.SaveLocation,
            new WorldPosition(world.PlayerX, world.PlayerY, world.PlayerZ),
            world.Objects.Select(value => new FieldObjectSnapshot(
                new WorldPosition(value.X, value.Y, value.Z),
                value.FixedSeed,
                MapPokemon(value.Pokemon))));
    }

    public async IAsyncEnumerable<RngStateUpdate> WatchRngStateAsync(
        RngWatchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RngState? previous = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            RngState current;
            try
            {
                current = await ReadRngStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (!request.ChangedOnly || current != previous)
            {
                previous = current;
                yield return new RngStateUpdate(current, DateTimeOffset.UtcNow);
            }

            try
            {
                await Task.Delay(request.Interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
        }
    }

    public Task SkipDayAsync(CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.SkipDayAsync(cancellationToken),
            "Unable to skip the day.");

    public Task SkipDayBackAsync(CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.SkipDayBackAsync(cancellationToken),
            "Unable to move the date back.");

    public Task ResetNetworkTimeAsync(CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.ResetNetworkTimeAsync(cancellationToken),
            "Unable to reset network time.");

    public Task<ulong> GetCurrentTimeAsync(CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            value => value.GetCurrentTimeAsync(cancellationToken),
            "Unable to read the console time.");

    public Task SetCurrentTimeAsync(
        ulong value,
        CancellationToken cancellationToken = default) =>
        ExecuteConnectedAsync(
            bridge => bridge.SetCurrentTimeAsync(value, cancellationToken),
            "Unable to set the console time.");

    private IOwoowConnectionBridge GetConnectedBridge()
    {
        if (bridge is not { Connected: true })
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.NotConnected,
                "No owoow Switch connection is active.");
        }

        return bridge;
    }

    private async Task<T> ExecuteConnectedAsync<T>(
        Func<IOwoowConnectionBridge, Task<T>> action,
        string message)
    {
        try
        {
            return await action(GetConnectedBridge()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UpstreamOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ConvertFailure(message, exception);
        }
    }

    private async Task ExecuteConnectedAsync(
        Func<IOwoowConnectionBridge, Task> action,
        string message)
    {
        try
        {
            await action(GetConnectedBridge()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UpstreamOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ConvertFailure(message, exception);
        }
    }

    private void SetStatus(ConnectionState state, string message)
    {
        status = new ConnectionStatus(state, message);
        StatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs(status));
    }

    private static UpstreamOperationException ConvertFailure(
        string message,
        Exception exception) =>
        new(UpstreamErrorCode.UpstreamFailure, message, exception);

    private static PokemonSnapshot MapPokemon(PK8 pokemon)
    {
        if (pokemon.Species == 0)
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                "The upstream Pokémon data does not contain a species.");
        }

        string? mark = null;
        if (owoow.Core.Utils.HasMark(pokemon, out var ribbon))
        {
            mark = ribbon.ToString().Replace("Mark", string.Empty, StringComparison.Ordinal);
        }

        return new PokemonSnapshot(
            pokemon.Species,
            ((Species)pokemon.Species).ToString(),
            pokemon.Form,
            checked((byte)pokemon.CurrentLevel),
            pokemon.Gender switch
            {
                0 => PokemonGender.Male,
                1 => PokemonGender.Female,
                _ => PokemonGender.Genderless,
            },
            pokemon.ShinyXor switch
            {
                0 => PokemonShinyType.Square,
                < 16 => PokemonShinyType.Star,
                _ => PokemonShinyType.None,
            },
            pokemon.Nature.ToString(),
            checked((ushort)pokemon.Ability),
            checked((ushort)pokemon.HeldItem),
            pokemon.EncryptionConstant,
            pokemon.PID,
            new RngIndividualValues(
            [
                checked((byte)pokemon.IV_HP),
                checked((byte)pokemon.IV_ATK),
                checked((byte)pokemon.IV_DEF),
                checked((byte)pokemon.IV_SPA),
                checked((byte)pokemon.IV_SPD),
                checked((byte)pokemon.IV_SPE),
            ]),
            pokemon.HeightScalar,
            mark,
            pokemon.Moves.Where(move => move != 0));
    }
}
