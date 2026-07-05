using System.Net;
using System.Text;
using AutoSwshRng.Core.Capture;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Notifications;
using EasyCon.Capture;
using OpenCvSharp;

namespace AutoSwshRng.Upstream;

public sealed class EasyConCaptureDeviceService : ICaptureDeviceService
{
    private OpenCVCapture? capture;

    public bool IsOpen => capture?.IsOpened == true;

    public Task<IReadOnlyList<CaptureSource>> DiscoverSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CaptureSource>>(
            ECCapture.GetCaptureCamera()
                .Select(value => new CaptureSource(value.name, value.index))
                .ToArray());
    }

    public Task<IReadOnlyList<CaptureBackend>> DiscoverBackendsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CaptureBackend>>(
            ECCapture.GetCaptureTypes()
                .Select(value => new CaptureBackend(value.Item1, value.Item2))
                .ToArray());
    }

    public Task OpenAsync(
        CaptureOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        capture?.Dispose();
        capture = new OpenCVCapture();
        if (!capture.Open(request.SourceIndex, request.BackendIdentifier))
        {
            capture.Dispose();
            capture = null;
            throw new UpstreamOperationException(
                UpstreamErrorCode.ConnectionFailed,
                $"Unable to open capture source {request.SourceIndex}.");
        }

        capture.SetProperties(request.Width, request.Height);
        return Task.CompletedTask;
    }

    public Task<CapturedImage> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture is not { IsOpened: true })
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.NotConnected,
                "No capture source is open.");
        }

        using var frame = capture.GetMatFrame();
        if (frame.Empty() || !Cv2.ImEncode(".png", frame, out var bytes))
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                "Capture source returned an empty frame.");
        }

        return Task.FromResult(new CapturedImage(bytes, frame.Width, frame.Height));
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture?.Release();
        capture?.Dispose();
        capture = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }
}

public sealed class EasyConImageRecognitionService : IImageRecognitionService
{
    public Task<TemplateMatchResult> MatchTemplateAsync(
        TemplateMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var decodedSource = Cv2.ImDecode(request.SourceImage, ImreadModes.Color);
        using var template = Cv2.ImDecode(request.TemplateImage, ImreadModes.Color);
        if (decodedSource.Empty() || template.Empty())
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                "Source or template image could not be decoded.");
        }

        var offsetX = 0;
        var offsetY = 0;
        Mat source = decodedSource;
        Mat? cropped = null;
        try
        {
            if (request.Region is { } region)
            {
                if (region.X + region.Width > source.Width
                    || region.Y + region.Height > source.Height)
                {
                    throw new UpstreamOperationException(
                        UpstreamErrorCode.Validation,
                        "Recognition region exceeds the source image.");
                }

                offsetX = region.X;
                offsetY = region.Y;
                cropped = new Mat(source, new Rect(
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height));
                source = cropped;
            }

            if (template.Width > source.Width || template.Height > source.Height)
            {
                throw new UpstreamOperationException(
                    UpstreamErrorCode.Validation,
                    "Template cannot be larger than the source image.");
            }

            var points = ECSearch.FindPic(
                source,
                template,
                Map(request.Method),
                out var score);
            return Task.FromResult(new TemplateMatchResult(
                points.Select(point => new ImagePoint(
                    point.X + offsetX,
                    point.Y + offsetY)).ToArray(),
                score));
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    public Task<OcrResult> RecognizeTextAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var source = Cv2.ImDecode(request.SourceImage, ImreadModes.Color);
        if (source.Empty())
        {
            throw new UpstreamOperationException(
                UpstreamErrorCode.InvalidData,
                "OCR source image could not be decoded.");
        }

        var text = ECSearch.FindOCR(request.ExpectedText, source, out var score);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OcrResult(text, score));
    }

    private static SearchMethod Map(ImageMatchMethod method) => method switch
    {
        ImageMatchMethod.SquareDifferenceNormalized => SearchMethod.SqDiffNormed,
        ImageMatchMethod.CorrelationNormalized => SearchMethod.CCorrNormed,
        ImageMatchMethod.CorrelationCoefficientNormalized => SearchMethod.CCoeffNormed,
        ImageMatchMethod.EdgeXy => SearchMethod.EdgeDetectXY,
        ImageMatchMethod.EdgeLaplacian => SearchMethod.EdgeDetectLaplacian,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}

public sealed class EasyConNotificationService : INotificationService
{
    private readonly HttpClient httpClient;

    public EasyConNotificationService()
        : this(new HttpClient())
    {
    }

    internal EasyConNotificationService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<NotificationResult> SendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var tasks = request.Endpoints
            .Where(endpoint => endpoint.Enabled)
            .Select(endpoint => SendEndpointAsync(
                endpoint,
                request,
                timeout.Token));
        return new NotificationResult(await Task.WhenAll(tasks).ConfigureAwait(false));
    }

    private async Task<NotificationProviderResult> SendEndpointAsync(
        NotificationEndpoint endpoint,
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = ReplaceVariables(
                endpoint.UrlTemplate,
                endpoint,
                request);
            using var message = new HttpRequestMessage(
                endpoint.Method == NotificationHttpMethod.Post
                    ? HttpMethod.Post
                    : HttpMethod.Get,
                url);
            var contentType = "application/json";
            foreach (var (name, value) in endpoint.Headers)
            {
                var replaced = ReplaceVariables(value, endpoint, request);
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = replaced;
                }
                else
                {
                    message.Headers.TryAddWithoutValidation(name, replaced);
                }
            }

            if (endpoint.Method == NotificationHttpMethod.Post
                && endpoint.BodyTemplate.Length > 0)
            {
                message.Content = new StringContent(
                    ReplaceVariables(endpoint.BodyTemplate, endpoint, request),
                    Encoding.UTF8,
                    contentType);
            }

            using var response = await httpClient.SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            return new NotificationProviderResult(
                endpoint.Name,
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new NotificationProviderResult(
                endpoint.Name,
                false,
                null,
                exception.Message);
        }
    }

    private static string ReplaceVariables(
        string template,
        NotificationEndpoint endpoint,
        NotificationRequest request)
    {
        var value = template
            .Replace("{{token}}", endpoint.Token, StringComparison.Ordinal)
            .Replace(
                "{{content}}",
                WebUtility.UrlEncode(request.Content),
                StringComparison.Ordinal)
            .Replace(
                "{{title}}",
                WebUtility.UrlEncode(request.Title),
                StringComparison.Ordinal);
        foreach (var (name, replacement) in endpoint.Variables)
        {
            value = value.Replace(
                $"{{{{{name}}}}}",
                replacement,
                StringComparison.Ordinal);
        }

        return value;
    }
}
