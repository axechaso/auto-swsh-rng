namespace AutoSwshRng.Core.Common;

public enum OperationState
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed,
}

public sealed record OperationProgress(
    OperationState State,
    ulong Completed,
    ulong Total,
    string Message)
{
    public double Fraction => Total == 0 ? 0 : Math.Min(1, (double)Completed / Total);
}
