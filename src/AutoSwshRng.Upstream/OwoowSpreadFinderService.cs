using System.Globalization;
using AutoSwshRng.Core.SpreadFinder;
using owoow.Core.Enums;
using owoow.Core.Interfaces;
using owoow.Core.RNG;
using OwoowSpreadFinder = owoow.Core.RNG.Generators.Misc.SpreadFinder;
using OwoowScale = owoow.Core.Enums.ScaleType;

namespace AutoSwshRng.Upstream;

public sealed class OwoowSpreadFinderService : ISpreadFinderService
{
    public async Task<IReadOnlyList<SpreadSearchResult>> SearchAsync(
        SpreadSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var config = CreateGeneratorConfig(request);
        var frames = request.Scope switch
        {
            SpreadSearchScope.Seeds seeds => await OwoowSpreadFinder
                .Generate(seeds.Values.ToList(), config)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException($"Search scope '{request.Scope.GetType().Name}' is not supported yet."),
        };

        cancellationToken.ThrowIfCancellationRequested();
        return SortResults(frames.Select(MapResult));
    }

    private static GeneratorConfig CreateGeneratorConfig(SpreadSearchRequest request)
    {
        return new GeneratorConfig
        {
            TargetMinIVs = request.IndividualValueRanges
                .Select(range => (uint)range.Minimum)
                .ToArray(),
            TargetMaxIVs = request.IndividualValueRanges
                .Select(range => (uint)range.Maximum)
                .ToArray(),
            GuaranteedIVs = request.GuaranteedIndividualValues,
            RareEC = request.RareEncryptionConstant,
            TargetScale = MapScaleFilter(request.Scale),
            FiltersEnabled = true,
        };
    }

    private static SpreadSearchResult MapResult(SpreadFinderFrame frame)
    {
        var height = ParseHeight(frame.Height);
        return new SpreadSearchResult(
            ParseHex(frame.Seed),
            ParseHex(frame.EC),
            new IndividualValues(frame.H, frame.A, frame.B, frame.C, frame.D, frame.S),
            height,
            GetScale(height));
    }

    private static IReadOnlyList<SpreadSearchResult> SortResults(IEnumerable<SpreadSearchResult> results)
    {
        return results
            .OrderBy(result => result.Seed)
            .ThenByDescending(result => result.IndividualValues.HP)
            .ThenByDescending(result => result.IndividualValues.Attack)
            .ThenByDescending(result => result.IndividualValues.Defense)
            .ThenByDescending(result => result.IndividualValues.SpecialAttack)
            .ThenByDescending(result => result.IndividualValues.SpecialDefense)
            .ThenByDescending(result => result.IndividualValues.Speed)
            .ToArray();
    }

    private static uint ParseHex(string value)
    {
        return uint.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static byte ParseHeight(string value)
    {
        var openingParenthesis = value.LastIndexOf('(');
        var closingParenthesis = value.LastIndexOf(')');
        if (openingParenthesis < 0 || closingParenthesis <= openingParenthesis + 1)
        {
            throw new FormatException($"Unexpected owoow height value '{value}'.");
        }

        return byte.Parse(
            value.AsSpan(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }

    private static OwoowScale MapScaleFilter(SpreadScale scale)
    {
        return scale switch
        {
            SpreadScale.Any => OwoowScale.Any,
            SpreadScale.XXXS => OwoowScale.XXXS,
            SpreadScale.XXS => OwoowScale.XXS,
            SpreadScale.XS => OwoowScale.XS,
            SpreadScale.Small => OwoowScale.S,
            SpreadScale.Medium => OwoowScale.M,
            SpreadScale.Large => OwoowScale.L,
            SpreadScale.XL => OwoowScale.XL,
            SpreadScale.XXL => OwoowScale.XXL,
            SpreadScale.XXXL => OwoowScale.XXXL,
            SpreadScale.MinOrMax => OwoowScale.MinOrMax,
            _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Unknown spread scale."),
        };
    }

    private static SpreadScale GetScale(byte height)
    {
        return height switch
        {
            0 => SpreadScale.XXXS,
            <= 24 => SpreadScale.XXS,
            <= 59 => SpreadScale.XS,
            <= 99 => SpreadScale.Small,
            <= 155 => SpreadScale.Medium,
            <= 195 => SpreadScale.Large,
            <= 230 => SpreadScale.XL,
            <= 254 => SpreadScale.XXL,
            _ => SpreadScale.XXXL,
        };
    }
}
