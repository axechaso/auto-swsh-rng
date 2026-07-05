using AutoSwshRng.Core.Automation;
using AutoSwshRng.Core.Common;
using EasyDevice;

namespace AutoSwshRng.Upstream.Tests;

public class EasyConControllerServicesTests
{
    [Test]
    public async Task MapsButtonsDpadAndSticksToEasyConKeys()
    {
        var bridge = new RecordingControllerBridge();
        var service = new EasyConControllerDeviceService(bridge);
        await service.ConnectAsync(new ControllerConnectionRequest("COM7"));

        await service.ApplyAsync(new ButtonInputAction(ControllerButton.A, true));
        await service.ApplyAsync(new ButtonInputAction(ControllerButton.A, false));
        await service.ApplyAsync(new DpadInputAction(DpadDirection.UpRight, true));
        await service.ApplyAsync(new StickInputAction(ControllerStick.Left, 0, 255, true));

        Assert.That(
            bridge.Commands,
            Is.EqualTo(new[]
            {
                "Down:A",
                "Up:A",
                "Down:HAT.TOP_RIGHT",
                "Down:LStick(0,255)",
            }));
    }

    [Test]
    public async Task CancellationAlwaysResetsController()
    {
        var bridge = new RecordingControllerBridge();
        var device = new EasyConControllerDeviceService(bridge);
        await device.ConnectAsync(new ControllerConnectionRequest("COM7"));
        var sequence = new EasyConInputSequenceService(device);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(10));

        var result = await sequence.ExecuteAsync(
            new InputSequence(
            [
                new ButtonInputAction(ControllerButton.A, true),
                new WaitInputAction(TimeSpan.FromSeconds(5)),
            ]),
            cancellationToken: cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(SequenceState.Cancelled));
            Assert.That(bridge.Commands.Last(), Is.EqualTo("Reset"));
        });
    }

    [Test]
    public async Task ExposesDiscoveryConnectionAndRecording()
    {
        var bridge = new RecordingControllerBridge { Ports = ["COM2", "COM7"] };
        var service = new EasyConControllerDeviceService(bridge);

        var ports = await service.DiscoverAsync();
        var status = await service.ConnectAsync(new ControllerConnectionRequest("COM7"));
        service.StartRecording();
        service.PauseRecording();
        service.StopRecording();
        var recording = service.GetRecording();
        await service.DisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ports, Is.EqualTo(new[] { "COM2", "COM7" }));
            Assert.That(status.State, Is.EqualTo(ControllerConnectionState.Connected));
            Assert.That(recording, Is.EqualTo("A DOWN"));
            Assert.That(service.Status.State, Is.EqualTo(ControllerConnectionState.Disconnected));
        });
    }

    [Test]
    public void ConvertsSynchronousDiscoveryFailure()
    {
        var bridge = new RecordingControllerBridge
        {
            DiscoveryException = new InvalidOperationException("serial unavailable"),
        };
        var service = new EasyConControllerDeviceService(bridge);

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await service.DiscoverAsync());

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.UpstreamFailure));
            Assert.That(error.InnerException, Is.SameAs(bridge.DiscoveryException));
        });
    }

    [Test]
    public void ConvertsSynchronousConnectionFailureAndFaultsStatus()
    {
        var bridge = new RecordingControllerBridge
        {
            ConnectionException = new InvalidOperationException("port failed"),
        };
        var service = new EasyConControllerDeviceService(bridge);

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await service.ConnectAsync(
                new ControllerConnectionRequest("COM7")));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.ConnectionFailed));
            Assert.That(error.InnerException, Is.SameAs(bridge.ConnectionException));
            Assert.That(
                service.Status.State,
                Is.EqualTo(ControllerConnectionState.Faulted));
        });
    }

    private sealed class RecordingControllerBridge : IEasyConControllerBridge
    {
        public IReadOnlyList<string> Ports { get; set; } = [];
        public List<string> Commands { get; } = [];
        public bool IsConnected { get; private set; }
        public Exception? DiscoveryException { get; set; }
        public Exception? ConnectionException { get; set; }

        public IReadOnlyList<string> GetPortNames() =>
            DiscoveryException is null ? Ports : throw DiscoveryException;
        public NintendoSwitch.ConnectResult Connect(string port)
        {
            if (ConnectionException is not null)
            {
                throw ConnectionException;
            }

            IsConnected = true;
            return NintendoSwitch.ConnectResult.Success;
        }
        public void Disconnect() => IsConnected = false;
        public void Down(ECKey key) => Commands.Add($"Down:{key.Name}");
        public void Up(ECKey key) => Commands.Add($"Up:{key.Name}");
        public void Reset() => Commands.Add("Reset");
        public void StartRecording() { }
        public void PauseRecording() { }
        public void StopRecording() { }
        public string GetRecording() => "A DOWN";
    }
}
