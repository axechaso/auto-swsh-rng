namespace AutoSwshRng.Core.Notifications;

public enum NotificationHttpMethod
{
    Get,
    Post,
}

public sealed class NotificationEndpoint
{
    private readonly Dictionary<string, string> headers;
    private readonly Dictionary<string, string> variables;

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
        if (!Uri.TryCreate(urlTemplate.Replace("{{token}}", "token"), UriKind.Absolute, out _))
        {
            throw new ArgumentException("Notification URL must be absolute.", nameof(urlTemplate));
        }

        Name = name.Trim();
        Enabled = enabled;
        Method = method;
        UrlTemplate = urlTemplate;
        Token = token ?? string.Empty;
        this.headers = headers?.ToDictionary() ?? [];
        BodyTemplate = bodyTemplate ?? string.Empty;
        this.variables = variables?.ToDictionary() ?? [];
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
    private readonly NotificationEndpoint[] endpoints;

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
        this.endpoints = endpoints.ToArray();
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
