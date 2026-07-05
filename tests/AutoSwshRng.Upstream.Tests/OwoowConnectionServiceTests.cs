using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Connection;
using AutoSwshRng.Core.Rng;
using PKHeX.Core;

namespace AutoSwshRng.Upstream.Tests;

public class OwoowConnectionServiceTests
{
    [Test]
    public async Task ConnectsAndMapsTrainerAndMemoryDataWithoutHardware()
    {
        var bridge = new RecordingConnectionBridge
        {
            Trainer = new OwoowTrainerData(12345, 54321, true, false),
            Dex = new OwoowDexRecommendationData([1, 25, 150, 898], "Rolling Fields", 77),
            Wild = CreatePokemon(),
            World = new OwoowWorldData(
                0x1234,
                1.5f,
                2.5f,
                3.5f,
                [new OwoowFieldObjectData(4, 5, 6, 0xABCDEF01, CreatePokemon())]),
            State = new RngState(11, 22),
        };
        IOwoowConnectionService service = new OwoowConnectionService(
            new SingleBridgeFactory(bridge));
        var settings = new OwoowConnectionSettings(
            ConnectionProtocol.Wifi,
            "192.168.1.10",
            6000);

        var connected = await service.ConnectAsync(settings);
        var trainer = await service.ReadTrainerAsync();
        var state = await service.ReadRngStateAsync();
        var dex = await service.ReadDexRecommendationAsync(full: true);
        var wild = await service.ReadWildPokemonAsync();
        var world = await service.ReadWorldAsync();

        Assert.Multiple(() =>
        {
            Assert.That(connected.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(service.Status.State, Is.EqualTo(ConnectionState.Connected));
            Assert.That(trainer, Is.EqualTo(new TrainerSnapshot(12345, 54321, true, false)));
            Assert.That(state, Is.EqualTo(new RngState(11, 22)));
            Assert.That(dex.SpeciesIds, Is.EqualTo(new ushort[] { 1, 25, 150, 898 }));
            Assert.That(wild.SpeciesId, Is.EqualTo(25));
            Assert.That(wild.IndividualValues.ToArray(), Is.EqualTo(new byte[] { 31, 30, 29, 28, 27, 26 }));
            Assert.That(world.SaveLocation, Is.EqualTo(0x1234));
            Assert.That(world.Objects.Single().FixedSeed, Is.EqualTo(0xABCDEF01));
        });
    }

    [Test]
    public async Task WritesStateAndExposesDateTimePrimitives()
    {
        var bridge = new RecordingConnectionBridge { CurrentTime = 123 };
        IOwoowConnectionService service = new OwoowConnectionService(
            new SingleBridgeFactory(bridge));
        await service.ConnectAsync(new OwoowConnectionSettings(
            ConnectionProtocol.Usb,
            null,
            0));

        await service.WriteRngStateAsync(new RngState(33, 44));
        await service.SkipDayAsync();
        await service.SkipDayBackAsync();
        await service.ResetNetworkTimeAsync();
        var currentTime = await service.GetCurrentTimeAsync();
        await service.SetCurrentTimeAsync(456);
        await service.DisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(bridge.WrittenState, Is.EqualTo(new RngState(33, 44)));
            Assert.That(bridge.DaySkipCalls, Is.EqualTo(1));
            Assert.That(bridge.DaySkipBackCalls, Is.EqualTo(1));
            Assert.That(bridge.ResetTimeCalls, Is.EqualTo(1));
            Assert.That(currentTime, Is.EqualTo(123));
            Assert.That(bridge.SetTime, Is.EqualTo(456));
            Assert.That(service.Status.State, Is.EqualTo(ConnectionState.Disconnected));
        });
    }

    [Test]
    public async Task WatchesChangedStatesAndHonorsCancellation()
    {
        var bridge = new RecordingConnectionBridge();
        bridge.States.Enqueue(new RngState(1, 2));
        bridge.States.Enqueue(new RngState(1, 2));
        bridge.States.Enqueue(new RngState(3, 4));
        IOwoowConnectionService service = new OwoowConnectionService(
            new SingleBridgeFactory(bridge));
        await service.ConnectAsync(new OwoowConnectionSettings(
            ConnectionProtocol.Usb,
            null,
            0));
        using var cancellation = new CancellationTokenSource();
        var observed = new List<RngState>();

        await foreach (var update in service.WatchRngStateAsync(
                           new RngWatchRequest(TimeSpan.FromMilliseconds(1)),
                           cancellation.Token))
        {
            observed.Add(update.State);
            if (observed.Count == 2)
            {
                cancellation.Cancel();
            }
        }

        Assert.That(observed, Is.EqualTo(new[] { new RngState(1, 2), new RngState(3, 4) }));
    }

    [Test]
    public void ConvertsConnectionFailure()
    {
        var bridge = new RecordingConnectionBridge
        {
            ConnectResult = (false, "socket unavailable"),
        };
        var service = new OwoowConnectionService(new SingleBridgeFactory(bridge));

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await service.ConnectAsync(new OwoowConnectionSettings(
                ConnectionProtocol.Wifi,
                "192.168.1.10",
                6000)));

        Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.ConnectionFailed));
    }

    [Test]
    public async Task DisconnectStatusCallbackDoesNotRegressToConnecting()
    {
        var bridge = new RecordingConnectionBridge
        {
            EmitDisconnectStatus = true,
        };
        var service = new OwoowConnectionService(new SingleBridgeFactory(bridge));
        var observed = new List<ConnectionState>();
        service.StatusChanged += (_, args) => observed.Add(args.Status.State);
        await service.ConnectAsync(new OwoowConnectionSettings(
            ConnectionProtocol.Usb,
            null,
            0));
        observed.Clear();

        await service.DisconnectAsync();

        Assert.That(
            observed,
            Is.EqualTo(new[]
            {
                ConnectionState.Disconnecting,
                ConnectionState.Disconnecting,
                ConnectionState.Disconnected,
            }));
    }

    [Test]
    public async Task ConvertsSynchronousTrainerReadFailure()
    {
        var bridge = new RecordingConnectionBridge
        {
            TrainerException = new InvalidOperationException("invalid trainer data"),
        };
        var service = new OwoowConnectionService(new SingleBridgeFactory(bridge));
        await service.ConnectAsync(new OwoowConnectionSettings(
            ConnectionProtocol.Usb,
            null,
            0));

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await service.ReadTrainerAsync());

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.UpstreamFailure));
            Assert.That(error.InnerException, Is.SameAs(bridge.TrainerException));
        });
    }

    private static PK8 CreatePokemon()
    {
        return new PK8
        {
            Species = 25,
            Form = 1,
            CurrentLevel = 50,
            TID16 = 12345,
            SID16 = 54321,
            EncryptionConstant = 0x11223344,
            PID = 0x55667788,
            Nature = Nature.Jolly,
            Gender = 0,
            Ability = 9,
            HeldItem = 1,
            IV_HP = 31,
            IV_ATK = 30,
            IV_DEF = 29,
            IV_SPA = 28,
            IV_SPD = 27,
            IV_SPE = 26,
            HeightScalar = 127,
        };
    }

    private sealed class SingleBridgeFactory(IOwoowConnectionBridge bridge)
        : IOwoowConnectionBridgeFactory
    {
        public IOwoowConnectionBridge Create(
            OwoowConnectionSettings settings,
            Action<string> statusUpdate)
        {
            if (bridge is RecordingConnectionBridge recording)
            {
                recording.StatusUpdate = statusUpdate;
            }

            return bridge;
        }
    }

    private sealed class RecordingConnectionBridge : IOwoowConnectionBridge
    {
        public bool Connected { get; private set; }
        public Action<string> StatusUpdate { get; set; } = _ => { };
        public (bool, string) ConnectResult { get; set; } = (true, string.Empty);
        public bool EmitDisconnectStatus { get; set; }
        public Exception? TrainerException { get; set; }
        public OwoowTrainerData Trainer { get; set; } = new(1, 2, false, false);
        public OwoowDexRecommendationData Dex { get; set; } = new([0, 0, 0, 0], null, null);
        public PK8 Wild { get; set; } = CreatePokemon();
        public OwoowWorldData World { get; set; } = new(0, 0, 0, 0, []);
        public RngState State { get; set; }
        public Queue<RngState> States { get; } = new();
        public RngState? WrittenState { get; private set; }
        public int DaySkipCalls { get; private set; }
        public int DaySkipBackCalls { get; private set; }
        public int ResetTimeCalls { get; private set; }
        public ulong CurrentTime { get; set; }
        public ulong? SetTime { get; private set; }

        public Task<(bool Success, string Error)> ConnectAsync(CancellationToken token)
        {
            StatusUpdate("Connecting...");
            Connected = ConnectResult.Item1;
            return Task.FromResult((ConnectResult.Item1, ConnectResult.Item2));
        }

        public Task<(bool Success, string Error)> DisconnectAsync(CancellationToken token)
        {
            if (EmitDisconnectStatus)
            {
                StatusUpdate("Disconnecting upstream...");
            }

            Connected = false;
            return Task.FromResult((true, string.Empty));
        }

        public Task<RngState> ReadRngStateAsync(CancellationToken token) =>
            Task.FromResult(States.Count > 0 ? States.Dequeue() : State);

        public Task WriteRngStateAsync(RngState state, CancellationToken token)
        {
            WrittenState = state;
            return Task.CompletedTask;
        }

        public OwoowTrainerData ReadTrainer() =>
            TrainerException is null ? Trainer : throw TrainerException;
        public Task<OwoowDexRecommendationData> ReadDexRecommendationAsync(
            bool full,
            CancellationToken token) => Task.FromResult(Dex);
        public Task<PK8> ReadWildPokemonAsync(CancellationToken token) =>
            Task.FromResult(Wild);
        public Task<OwoowWorldData> ReadWorldAsync(CancellationToken token) =>
            Task.FromResult(World);

        public Task SkipDayAsync(CancellationToken token)
        {
            DaySkipCalls++;
            return Task.CompletedTask;
        }

        public Task SkipDayBackAsync(CancellationToken token)
        {
            DaySkipBackCalls++;
            return Task.CompletedTask;
        }

        public Task ResetNetworkTimeAsync(CancellationToken token)
        {
            ResetTimeCalls++;
            return Task.CompletedTask;
        }

        public Task<ulong> GetCurrentTimeAsync(CancellationToken token) =>
            Task.FromResult(CurrentTime);

        public Task SetCurrentTimeAsync(ulong value, CancellationToken token)
        {
            SetTime = value;
            return Task.CompletedTask;
        }
    }
}
