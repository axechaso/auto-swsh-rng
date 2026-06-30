namespace AutoSwshRng.Core.Common;

public enum UpstreamErrorCode
{
    Validation,
    NotFound,
    NotConnected,
    ConnectionFailed,
    Timeout,
    InvalidData,
    Unsupported,
    UpstreamFailure,
}

public sealed class UpstreamOperationException : Exception
{
    public UpstreamOperationException(
        UpstreamErrorCode code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public UpstreamErrorCode Code { get; }
}
