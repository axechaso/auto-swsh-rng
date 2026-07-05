namespace AutoSwshRng.Core.Capture;

public sealed record ImageRegion
{
    public ImageRegion(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
}

public sealed record CaptureSource(string Name, int Index);
public sealed record CaptureBackend(string Name, int Identifier);

public sealed record CaptureOpenRequest
{
    public CaptureOpenRequest(
        int sourceIndex,
        int backendIdentifier,
        int width = 1280,
        int height = 720)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        SourceIndex = sourceIndex;
        BackendIdentifier = backendIdentifier;
        Width = width;
        Height = height;
    }

    public int SourceIndex { get; }
    public int BackendIdentifier { get; }
    public int Width { get; }
    public int Height { get; }
}

public sealed class CapturedImage
{
    private readonly byte[] pngBytes;

    public CapturedImage(IEnumerable<byte> pngBytes, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        this.pngBytes = pngBytes.ToArray();
        if (this.pngBytes.Length == 0)
        {
            throw new ArgumentException("Captured image cannot be empty.", nameof(pngBytes));
        }

        Width = width;
        Height = height;
    }

    public byte[] PngBytes => pngBytes.ToArray();
    public int Width { get; }
    public int Height { get; }
}

public enum ImageMatchMethod
{
    SquareDifferenceNormalized,
    CorrelationNormalized,
    CorrelationCoefficientNormalized,
    EdgeXy,
    EdgeLaplacian,
}

public sealed record ImagePoint(int X, int Y);

public sealed class TemplateMatchRequest
{
    private readonly byte[] sourceImage;
    private readonly byte[] templateImage;

    public TemplateMatchRequest(
        IEnumerable<byte> sourceImage,
        IEnumerable<byte> templateImage,
        ImageMatchMethod method,
        ImageRegion? region = null)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(templateImage);
        this.sourceImage = sourceImage.ToArray();
        this.templateImage = templateImage.ToArray();
        if (this.sourceImage.Length == 0 || this.templateImage.Length == 0)
        {
            throw new ArgumentException("Source and template images are required.");
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method));
        }

        Method = method;
        Region = region;
    }

    public byte[] SourceImage => sourceImage.ToArray();
    public byte[] TemplateImage => templateImage.ToArray();
    public ImageMatchMethod Method { get; }
    public ImageRegion? Region { get; }
}

public sealed record TemplateMatchResult(
    IReadOnlyList<ImagePoint> Points,
    double Score);

public sealed class OcrRequest
{
    private readonly byte[] sourceImage;

    public OcrRequest(IEnumerable<byte> sourceImage, string expectedText)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedText);
        this.sourceImage = sourceImage.ToArray();
        if (this.sourceImage.Length == 0)
        {
            throw new ArgumentException("Source image is required.", nameof(sourceImage));
        }

        ExpectedText = expectedText;
    }

    public byte[] SourceImage => sourceImage.ToArray();
    public string ExpectedText { get; }
}

public sealed record OcrResult(string Text, double Score);

public interface ICaptureDeviceService : IAsyncDisposable
{
    bool IsOpen { get; }
    Task<IReadOnlyList<CaptureSource>> DiscoverSourcesAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaptureBackend>> DiscoverBackendsAsync(
        CancellationToken cancellationToken = default);
    Task OpenAsync(
        CaptureOpenRequest request,
        CancellationToken cancellationToken = default);
    Task<CapturedImage> CaptureAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}

public interface IImageRecognitionService
{
    Task<TemplateMatchResult> MatchTemplateAsync(
        TemplateMatchRequest request,
        CancellationToken cancellationToken = default);
    Task<OcrResult> RecognizeTextAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default);
}
