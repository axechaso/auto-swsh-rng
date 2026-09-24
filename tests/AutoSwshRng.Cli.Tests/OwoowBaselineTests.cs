using System.Text.Json.Nodes;

namespace AutoSwshRng.Cli.Tests;

public class OwoowBaselineTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "owoow-baseline.json")))!;
        return fixture["cases"]!.AsArray().Select(item => new TestCaseData(
            item!["request"]!.ToJsonString(), item["expected"]!.ToJsonString()).SetName("PinnedOwoow_" + item["name"]!.GetValue<string>()));
    }

    [TestCaseSource(nameof(Cases))]
    public async Task MatchesReviewedFixedSeedBaseline(string request, string expected)
    {
        using var output = new StringWriter();
        var code = await DesktopJsonCommand.RunAsync(new StringReader(request), output);
        Assert.That(code, Is.Zero, output.ToString());
        var result = JsonNode.Parse(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1])!;
        Assert.That(result["protocolVersion"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(JsonNode.DeepEquals(result["data"], JsonNode.Parse(expected)), Is.True,
            "owoow 计算结果发生变化。需审核算法、遭遇表及适配层差异，不能直接重录基线。\n" + output);
    }

    [Test]
    public async Task RejectsUnknownProtocolBeforeCalculation()
    {
        using var output = new StringWriter();
        var code = await DesktopJsonCommand.RunAsync(new StringReader("{\"protocolVersion\":999,\"operation\":\"catalog\"}"), output);
        Assert.That(code, Is.EqualTo(1));
        var result = JsonNode.Parse(output.ToString())!;
        Assert.That(result["protocolVersion"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(result["message"]!.GetValue<string>(), Does.Contain("协议版本不兼容"));
    }
}
