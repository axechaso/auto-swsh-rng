using System.Collections.ObjectModel;

namespace AutoSwshRng.Core.Notifications;

public enum NotificationHttpMethod
{
    Get,
    Post,
}

public sealed class NotificationEndpoint
{
    private readonly ReadOnlyDictionary<string, string> headers;
    private readonly ReadOnlyDictionary<string, string> variables;

    public NotificationEndpoint(
        string name,
        bool enabled,
        NotificationHttpMethod method,
        string urlTemplate,
        string? token,
        IReadOnlyDictionary<string, string>? headers,
        string? bodyTemplate,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method));
        }

        if (!Uri.TryCreate(urlTemplate.Replace("{{token}}", "token"), UriKind.Absolute, out _))
        {
            throw new ArgumentException("Notification URL must be absolute.", nameof(urlTemplate));
        }

        Name = name.Trim();
        Enabled = enabled;
        Method = method;
        UrlTemplate = urlTemplate;
        Token = token ?? string.Empty;
        this.headers = new ReadOnlyDictionary<string, string>(
            headers?.ToDictionary() ?? []);
        BodyTemplate = bodyTemplate ?? string.Empty;
        this.variables = new ReadOnlyDictionary<string, string>(
            variables?.ToDictionary() ?? []);
    }

    public string Name { get; }
    public bool Enabled { get; }
    public NotificationHttpMethod Method { get; }
    public string UrlTemplate { get; }
    public string Token { get; }
    public IReadOnlyDictionary<string, string> Headers => headers;
    public string BodyTemplate { get; }
    public IReadOnlyDictionary<string, string> Variables => variables;
}

public sealed class NotificationRequest
{
    private readonly IReadOnlyList<NotificationEndpoint> endpoints;

    public NotificationRequest(
        string content,
        string title,
        IEnumerable<NotificationEndpoint> endpoints,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(endpoints);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Content = content;
        Title = title;
        var endpointCopy = endpoints.ToArray();
        if (endpointCopy.Any(endpoint => endpoint is null))
        {
            throw new ArgumentException(
                "Notification endpoints cannot contain null entries.",
                nameof(endpoints));
        }

        this.endpoints = Array.AsReadOnly(endpointCopy);
        Timeout = timeout;
    }

    public string Content { get; }
    public string Title { get; }
    public IReadOnlyList<NotificationEndpoint> Endpoints => endpoints;
    public TimeSpan Timeout { get; }
}

public sealed record NotificationProviderResult(
    string Name,
    bool Succeeded,
    int? StatusCode,
    string Message);

public sealed record NotificationResult(
    IReadOnlyList<NotificationProviderResult> Results);

public interface INotificationService
{
    Task<NotificationResult> SendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default);
}
