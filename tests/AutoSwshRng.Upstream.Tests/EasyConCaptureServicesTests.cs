using System.Net;
using AutoSwshRng.Core.Capture;
using AutoSwshRng.Core.Common;
using AutoSwshRng.Core.Notifications;
using EasyCon.Capture;
using OpenCvSharp;

namespace AutoSwshRng.Upstream.Tests;

public class EasyConCaptureServicesTests
{
    [Test]
    public async Task SourceAndBackendDiscoveryMatchesEasyCon()
    {
        var service = new EasyConCaptureDeviceService();

        var sources = await service.DiscoverSourcesAsync();
        var backends = await service.DiscoverBackendsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                sources.Select(value => (value.Name, value.Index)),
                Is.EqualTo(ECCapture.GetCaptureCamera()));
            Assert.That(
                backends.Select(value => (value.Name, value.Identifier)),
                Is.EqualTo(ECCapture.GetCaptureTypes()));
        });
    }

    [Test]
    public async Task TemplateMatchingMatchesDirectEasyConResult()
    {
        using var source = new Mat(40, 50, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(source, new Rect(17, 11, 8, 9), Scalar.White, -1);
        using var template = new Mat(source, new Rect(17, 11, 8, 9)).Clone();
        Cv2.ImEncode(".png", source, out var sourceBytes);
        Cv2.ImEncode(".png", template, out var templateBytes);
        var expectedPoints = ECSearch.FindPic(
            source,
            template,
            SearchMethod.CCoeffNormed,
            out var expectedScore);

        var actual = await new EasyConImageRecognitionService().MatchTemplateAsync(
            new TemplateMatchRequest(
                sourceBytes,
                templateBytes,
                ImageMatchMethod.CorrelationCoefficientNormalized));

        Assert.Multiple(() =>
        {
            Assert.That(actual.Score, Is.EqualTo(expectedScore).Within(0.000001));
            Assert.That(actual.Points.Select(point => (point.X, point.Y)),
                Is.EqualTo(expectedPoints.Select(point => (point.X, point.Y))));
        });
    }

    [Test]
    public void InvalidImageIsConvertedToProjectError()
    {
        var error = Assert.ThrowsAsync<AutoSwshRng.Core.Common.UpstreamOperationException>(
            async () => await new EasyConImageRecognitionService().MatchTemplateAsync(
                new TemplateMatchRequest(
                    [1, 2, 3],
                    [4, 5, 6],
                    ImageMatchMethod.CorrelationCoefficientNormalized)));

        Assert.That(
            error!.Code,
            Is.EqualTo(AutoSwshRng.Core.Common.UpstreamErrorCode.InvalidData));
    }

    [Test]
    public void OverflowingRecognitionRegionIsRejectedAsProjectValidationError()
    {
        using var source = new Mat(10, 10, MatType.CV_8UC3, Scalar.Black);
        using var template = new Mat(1, 1, MatType.CV_8UC3, Scalar.White);
        Cv2.ImEncode(".png", source, out var sourceBytes);
        Cv2.ImEncode(".png", template, out var templateBytes);

        var error = Assert.ThrowsAsync<UpstreamOperationException>(
            async () => await new EasyConImageRecognitionService().MatchTemplateAsync(
                new TemplateMatchRequest(
                    sourceBytes,
                    templateBytes,
                    ImageMatchMethod.CorrelationCoefficientNormalized,
                    new ImageRegion(int.MaxValue, 0, 1, 1))));

        Assert.That(error!.Code, Is.EqualTo(UpstreamErrorCode.Validation));
    }

    [Test]
    public async Task NotificationConvertsRequestAndHonorsHttpResult()
    {
        var handler = new RecordingHttpHandler();
        var service = new EasyConNotificationService(new HttpClient(handler));
        var endpoint = new NotificationEndpoint(
            "hook",
            true,
            NotificationHttpMethod.Post,
            "https://example.test/{{token}}",
            "secret",
            new Dictionary<string, string> { ["X-Title"] = "{{title}}" },
            "{\"message\":\"{{content}}\"}");

        var result = await service.SendAsync(new NotificationRequest(
            "Hello world",
            "RNG hit",
            [endpoint],
            TimeSpan.FromSeconds(2)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.Single().Succeeded, Is.True);
            Assert.That(handler.Request!.RequestUri!.AbsolutePath, Is.EqualTo("/secret"));
            Assert.That(handler.Body, Does.Contain("Hello+world"));
        });
    }

    [Test]
    public async Task NotificationTimeoutReturnsProviderFailure()
    {
        var service = new EasyConNotificationService(
            new HttpClient(new NeverCompletingHttpHandler()));
        var endpoint = new NotificationEndpoint(
            "slow",
            true,
            NotificationHttpMethod.Get,
            "https://example.test",
            null,
            null,
            null);

        var result = await service.SendAsync(new NotificationRequest(
            "content",
            "title",
            [endpoint],
            TimeSpan.FromMilliseconds(20)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.Single().Succeeded, Is.False);
            Assert.That(result.Results.Single().Message, Does.Contain("timed out"));
        });
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            };
        }
    }

    private sealed class NeverCompletingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
