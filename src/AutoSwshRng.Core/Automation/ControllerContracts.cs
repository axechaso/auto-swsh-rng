using AutoSwshRng.Core.Common;

namespace AutoSwshRng.Core.Automation;

public enum ControllerConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted,
}

public enum ControllerButton
{
    A, B, X, Y, L, R, ZL, ZR, Plus, Minus, LeftStick, RightStick, Home, Capture,
}

public enum DpadDirection
{
    Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft,
}

public enum ControllerStick
{
    Left,
    Right,
}

public sealed record ControllerConnectionRequest
{
    public ControllerConnectionRequest(string port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        Port = port.Trim();
    }

    public string Port { get; }
}

public sealed record ControllerStatus(
    ControllerConnectionState State,
    string Message);

public abstract record ControllerInputAction;

public sealed record ButtonInputAction(
    ControllerButton Button,
    bool Pressed) : ControllerInputAction;

public sealed record DpadInputAction(
    DpadDirection Direction,
    bool Pressed) : ControllerInputAction;

public sealed record StickInputAction(
    ControllerStick Stick,
    byte X,
    byte Y,
    bool Pressed) : ControllerInputAction;

public sealed record WaitInputAction : ControllerInputAction
{
    public WaitInputAction(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

public sealed record ResetInputAction : ControllerInputAction;

public sealed class InputSequence
{
    private readonly ControllerInputAction[] actions;

    public InputSequence(IEnumerable<ControllerInputAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        this.actions = actions.ToArray();
        if (this.actions.Any(action => action is null))
        {
            throw new ArgumentException("Sequence actions cannot contain null.", nameof(actions));
        }
    }

    public IReadOnlyList<ControllerInputAction> Actions => actions;
}

public enum SequenceState
{
    Completed,
    Cancelled,
    Failed,
}

public sealed record SequenceExecutionResult(
    SequenceState State,
    int CompletedActions,
    string? Error);

public interface IControllerDeviceService
{
    ControllerStatus Status { get; }
    Task<IReadOnlyList<string>> DiscoverAsync(
        CancellationToken cancellationToken = default);
    Task<ControllerStatus> ConnectAsync(
        ControllerConnectionRequest request,
        CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task ApplyAsync(
        ControllerInputAction action,
        CancellationToken cancellationToken = default);
    void StartRecording();
    void PauseRecording();
    void StopRecording();
    string GetRecording();
}

public interface IInputSequenceService
{
    Task<SequenceExecutionResult> ExecuteAsync(
        InputSequence sequence,
        IProgress<OperationProgress>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);
}
