using System.Text.Json;
using AutoSwshRng.Core.Encounters;
using AutoSwshRng.Core.Profiles;
using AutoSwshRng.Core.Rng;
using AutoSwshRng.Upstream;

namespace AutoSwshRng.Cli.Tests;

public class DesktopJsonCommandTests
{
    private static async Task<(int Code, JsonElement Event)> Run(object request)
    {
        using var output = new StringWriter();
        var code = await DesktopJsonCommand.RunAsync(new StringReader(JsonSerializer.Serialize(request)), output);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            using var item = JsonDocument.Parse(line); // Every progress line is valid JSON.
            Assert.That(item.RootElement.GetProperty("type").GetString(), Is.AnyOf("result", "progress", "error"));
        }
        using var document = JsonDocument.Parse(lines[^1]);
        return (code, document.RootElement.Clone());
    }

    [TestCase(EncounterKind.Static)]
    [TestCase(EncounterKind.Symbol)]
    [TestCase(EncounterKind.Hidden)]
    [TestCase(EncounterKind.Fishing)]
    public async Task SearchMatchesExistingService(EncounterKind kind)
    {
        var (_, options) = await Run(new { operation = "catalog", game = "Sword", kind = kind.ToString() });
        var catalog = options.GetProperty("data");
        var area = catalog.GetProperty("area").GetString()!;
        var weather = catalog.GetProperty("weather").GetString()!;
        var species = kind == EncounterKind.Static ? catalog.GetProperty("species")[0].GetString() : null;
        var expected = await new OwoowOverworldEncounterService().SearchAsync(new OverworldSearchRequest(
            new RngState(0x123456789ABCDEF0, 0x0FEDCBA987654321), 0, 30,
            new EncounterCatalogRequest(GameVersion.Sword, kind, area, weather),
            new RngProfile("Desktop", GameVersion.Sword, 1337, 1390, true, false),
            new EncounterFilter(targetSpecies: species), OverworldEnvironmentSettings.None, auraKnockouts: 0));
        var (code, result) = await Run(new { operation = "search", game = "Sword", kind = kind.ToString(), area, weather, species,
            seed0 = "123456789ABCDEF0", seed1 = "0FEDCBA987654321", start = 0, end = 30, tid = 1337, sid = 1390, shinyCharm = true });
        Assert.That(code, Is.Zero, result.ToString());
        var actual = result.GetProperty("data").GetProperty("rows").EnumerateArray().ToArray();
        Assert.That(actual, Has.Length.EqualTo(expected.Count));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[i].GetProperty("advance").GetUInt64(), Is.EqualTo(expected[i].Advance));
                Assert.That(actual[i].GetProperty("pid").GetString(), Is.EqualTo(expected[i].PersonalityId.ToString("X8")));
                Assert.That(actual[i].GetProperty("ivs").EnumerateArray().Select(v => v.GetByte()), Is.EqualTo(expected[i].IndividualValues.ToArray()));
                Assert.That(actual[i].GetProperty("seed0").GetString(), Is.EqualTo(expected[i].State.Seed0.ToString("X16")));
            });
        }
    }

    [Test]
    public async Task ForwardAndReversePreserveFull64BitState()
    {
        var (_, forward) = await Run(new { operation = "rng", seed0 = "FFFFFFFFFFFFFFFF", seed1 = "FEDCBA9876543210", amount = 123 });
        var state = forward.GetProperty("data");
        var (code, reverse) = await Run(new { operation = "rng", seed0 = state.GetProperty("seed0").GetString(), seed1 = state.GetProperty("seed1").GetString(), amount = 123, reverse = true });
        Assert.That(code, Is.Zero);
        Assert.That(reverse.GetProperty("data").GetProperty("seed0").GetString(), Is.EqualTo("FFFFFFFFFFFFFFFF"));
        Assert.That(reverse.GetProperty("data").GetProperty("seed1").GetString(), Is.EqualTo("FEDCBA9876543210"));
    }

    [TestCase("0", "0")]
    [TestCase("xyz", "1")]
    [TestCase("10000000000000000", "1")]
    public async Task RejectsInvalidSeed(string seed0, string seed1)
    {
        var (code, result) = await Run(new { operation = "rng", seed0, seed1 });
        Assert.That(code, Is.EqualTo(1));
        Assert.That(result.GetProperty("type").GetString(), Is.EqualTo("error"));
    }

    [Test]
    public async Task RejectsOversizedSearchBeforeStartingGenerator()
    {
        var (code, result) = await Run(new { operation = "search", seed0 = "1", seed1 = "2", start = 0, end = 100000 });
        Assert.That(code, Is.EqualTo(1));
        Assert.That(result.GetProperty("message").GetString(), Does.Contain("100,000"));
    }

    [Test]
    public async Task MalformedRequestIsStructuredError()
    {
        using var output = new StringWriter();
        Assert.That(await DesktopJsonCommand.RunAsync(new StringReader("{bad"), output), Is.EqualTo(1));
        using var doc = JsonDocument.Parse(output.ToString());
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("error"));
    }
}
