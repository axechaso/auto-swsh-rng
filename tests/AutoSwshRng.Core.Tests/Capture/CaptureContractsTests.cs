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
}
