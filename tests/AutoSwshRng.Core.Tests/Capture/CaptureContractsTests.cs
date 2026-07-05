using AutoSwshRng.Core.Capture;
using AutoSwshRng.Core.Notifications;

namespace AutoSwshRng.Core.Tests.Capture;

public class CaptureContractsTests
{
    [Test]
    public void RectangleRequiresPositiveSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ImageRegion(0, 0, 0, 10));
    }

    [Test]
    public void TemplateRequestRequiresImageBytes()
    {
        Assert.Throws<ArgumentException>(() => new TemplateMatchRequest(
            [],
            [1],
            ImageMatchMethod.CorrelationCoefficientNormalized));
    }

    [Test]
    public void TemplateRequestRejectsUnknownMatchMethod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TemplateMatchRequest(
            [1],
            [2],
            (ImageMatchMethod)999));
    }

    [Test]
    public void NotificationEndpointRequiresAbsoluteUrl()
    {
        Assert.Throws<ArgumentException>(() => new NotificationEndpoint(
            "test",
            true,
            NotificationHttpMethod.Post,
            "relative",
            null,
            null,
            null));
    }

    [Test]
    public void NotificationContractsRejectUnknownMethodAndProtectCollections()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationEndpoint(
            "test",
            true,
            (NotificationHttpMethod)999,
            "https://example.test",
            null,
            null,
            null));

        var endpoint = new NotificationEndpoint(
            "test",
            true,
            NotificationHttpMethod.Get,
            "https://example.test",
            null,
            new Dictionary<string, string> { ["X-Test"] = "value" },
            null,
            new Dictionary<string, string> { ["custom"] = "value" });
        var request = new NotificationRequest(
            "content",
            "title",
            [endpoint],
            TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(
                () => ((IDictionary<string, string>)endpoint.Headers)["X-Test"] = "changed");
            Assert.Throws<NotSupportedException>(
                () => ((IDictionary<string, string>)endpoint.Variables)["custom"] = "changed");
            Assert.Throws<NotSupportedException>(
                () => ((IList<NotificationEndpoint>)request.Endpoints).Clear());
        });
    }

    [Test]
    public void NotificationRequestRejectsNullEndpointEntriesAsValidationErrors()
    {
        Assert.Throws<ArgumentException>(() => new NotificationRequest(
            "content",
            "title",
            [null!],
            TimeSpan.FromSeconds(1)));
    }
}
