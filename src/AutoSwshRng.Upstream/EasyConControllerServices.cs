using AutoSwshRng.Core.Automation;
using AutoSwshRng.Core.Common;
using EasyDevice;

namespace AutoSwshRng.Upstream;

internal interface IEasyConControllerBridge
{
    bool IsConnected { get; }
    IReadOnlyList<string> GetPortNames();
    NintendoSwitch.ConnectResult Connect(string port);
    void Disconnect();
    void Down(ECKey key);
    void Up(ECKey key);
    void Reset();
    void StartRecording();
    void PauseRecording();
    void StopRecording();
    string GetRecording();
}

internal sealed class EasyConControllerBridge : IEasyConControllerBridge
{
    private readonly NintendoSwitch device = new();

    public bool IsConnected => device.IsConnected();
    public IReadOnlyList<string> GetPortNames() => ECDevice.GetPortNames();
    public NintendoSwitch.ConnectResult Connect(string port) => device.TryConnect(port);
    public void Disconnect() => device.Disconnect();
    public void Down(ECKey key) => device.Down(key);
    public void Up(ECKey key) => device.Up(key);
    public void Reset() => device.Reset();
    public void StartRecording() => device.StartRecord();
    public void PauseRecording() => device.PauseRecord();
    public void StopRecording() => device.StopRecord();
    public string GetRecording() => device.GetRecordScript();
}

public sealed class EasyConControllerDeviceService : IControllerDeviceService
{
    private readonly IEasyConControllerBridge bridge;
    private ControllerStatus status =
        new(ControllerConnectionState.Disconnected, "Disconnected.");

    public EasyConControllerDeviceService()
        : this(new EasyConControllerBridge())
    {
    }

    internal EasyConControllerDeviceService(IEasyConControllerBridge bridge)
    {
        this.bridge = bridge;
    }

    public ControllerStatus Status => status;

    public Task<IReadOnlyList<string>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            bridge.GetPortNames().Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ControllerStatus> ConnectAsync(
        ControllerConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        status = new ControllerStatus(
            ControllerConnectionState.Connecting,
            $"Connecting to {request.Port}.");
        var result = await Task.Run(
                () => bridge.Connect(request.Port),
                cancellationToken)
            .ConfigureAwait(false);
        status = result switch
        {
            NintendoSwitch.ConnectResult.Success => new ControllerStatus(
                ControllerConnectionState.Connected,
                $"Connected to {request.Port}."),
            NintendoSwitch.ConnectResult.InvalidArgument => new ControllerStatus(
                ControllerConnectionState.Faulted,
                "Invalid serial port."),
            NintendoSwitch.ConnectResult.Timeout => new ControllerStatus(
                ControllerConnectionState.Faulted,
                "Serial connection timed out."),
            _ => new ControllerStatus(
                ControllerConnectionState.Faulted,
                "Serial connection failed."),
        };
        if (status.State != ControllerConnectionState.Connected)
        {
            throw new UpstreamOperationException(
                result == NintendoSwitch.ConnectResult.Timeout
                    ? UpstreamErrorCode.Timeout
                    : UpstreamErrorCode.ConnectionFailed,
                status.Message);
        }

        return status;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bridge.Disconnect();
        status = new ControllerStatus(
            ControllerConnectionState.Disconnected,
            "Disconnected.");
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(
        ControllerInputAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (!bridge.IsConnected)
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.NotConnected,
                "No EasyCon controller is connected.");
        }

        switch (action)
        {
            case ButtonInputAction button:
                Apply(Map(button.Button), button.Pressed);
                break;
            case DpadInputAction dpad:
                Apply(ECKeyUtil.HAT(Map(dpad.Direction)), dpad.Pressed);
                break;
            case StickInputAction stick:
                var key = stick.Stick == ControllerStick.Left
                    ? ECKeyUtil.LStick(stick.X, stick.Y)
                    : ECKeyUtil.RStick(stick.X, stick.Y);
                Apply(key, stick.Pressed);
                break;
            case WaitInputAction wait:
                await Task.Delay(wait.Duration, cancellationToken).ConfigureAwait(false);
                break;
            case ResetInputAction:
                bridge.Reset();
                break;
            default:
                throw new UpstreamOperationException(
                    UpstreamErrorCode.Unsupported,
                    $"Unsupported controller action {action.GetType().Name}.");
        }
    }

    public void StartRecording() => bridge.StartRecording();
    public void PauseRecording() => bridge.PauseRecording();
    public void StopRecording() => bridge.StopRecording();
    public string GetRecording() => bridge.GetRecording();

    private void Apply(ECKey key, bool pressed)
    {
        if (pressed)
        {
            bridge.Down(key);
        }
        else
        {
            bridge.Up(key);
        }
    }

    private static ECKey Map(ControllerButton button) =>
        ECKeyUtil.Button(button switch
        {
            ControllerButton.A => SwitchButton.A,
            ControllerButton.B => SwitchButton.B,
            ControllerButton.X => SwitchButton.X,
            ControllerButton.Y => SwitchButton.Y,
            ControllerButton.L => SwitchButton.L,
            ControllerButton.R => SwitchButton.R,
            ControllerButton.ZL => SwitchButton.ZL,
            ControllerButton.ZR => SwitchButton.ZR,
            ControllerButton.Plus => SwitchButton.PLUS,
            ControllerButton.Minus => SwitchButton.MINUS,
            ControllerButton.LeftStick => SwitchButton.LCLICK,
            ControllerButton.RightStick => SwitchButton.RCLICK,
            ControllerButton.Home => SwitchButton.HOME,
            ControllerButton.Capture => SwitchButton.CAPTURE,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        });

    private static SwitchHAT Map(DpadDirection direction) => direction switch
    {
        DpadDirection.Up => SwitchHAT.TOP,
        DpadDirection.UpRight => SwitchHAT.TOP_RIGHT,
        DpadDirection.Right => SwitchHAT.RIGHT,
        DpadDirection.DownRight => SwitchHAT.BOTTOM_RIGHT,
        DpadDirection.Down => SwitchHAT.BOTTOM,
        DpadDirection.DownLeft => SwitchHAT.BOTTOM_LEFT,
        DpadDirection.Left => SwitchHAT.LEFT,
        DpadDirection.UpLeft => SwitchHAT.TOP_LEFT,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}

public sealed class EasyConInputSequenceService(IControllerDeviceService controller)
    : IInputSequenceService
{
    public async Task<SequenceExecutionResult> ExecuteAsync(
        InputSequence sequence,
        IProgress<OperationProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var total = checked((ulong)sequence.Actions.Count);
        var completed = 0;
        progress?.Report(new OperationProgress(
            OperationState.Running,
            0,
            total,
            "Executing controller sequence."));
        try
        {
            foreach (var action in sequence.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                log?.Invoke($"Executing {action}.");
                await controller.ApplyAsync(action, cancellationToken).ConfigureAwait(false);
                completed++;
                progress?.Report(new OperationProgress(
                    OperationState.Running,
                    (ulong)completed,
                    total,
                    "Executing controller sequence."));
            }

            progress?.Report(new OperationProgress(
                OperationState.Completed,
                total,
                total,
                "Controller sequence complete."));
            return new SequenceExecutionResult(SequenceState.Completed, completed, null);
        }
        catch (OperationCanceledException)
        {
            await ResetSafelyAsync().ConfigureAwait(false);
            progress?.Report(new OperationProgress(
                OperationState.Cancelled,
                (ulong)completed,
                total,
                "Controller sequence cancelled."));
            return new SequenceExecutionResult(SequenceState.Cancelled, completed, null);
        }
        catch (Exception exception)
        {
            await ResetSafelyAsync().ConfigureAwait(false);
            progress?.Report(new OperationProgress(
                OperationState.Failed,
                (ulong)completed,
                total,
                exception.Message));
            return new SequenceExecutionResult(
                SequenceState.Failed,
                completed,
                exception.Message);
        }
    }

    private async Task ResetSafelyAsync()
    {
        try
        {
            await controller.ApplyAsync(new ResetInputAction(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original cancellation or action failure.
        }
    }
}
