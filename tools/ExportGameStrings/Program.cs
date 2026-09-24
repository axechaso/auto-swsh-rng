using System.Text.Encodings.Web;
using System.Text.Json;
using PKHeX.Core;

var english = GameInfo.GetStrings("en");
var chinese = GameInfo.GetStrings("zh-Hans");
static Dictionary<string, string> Pair(IReadOnlyList<string> source, IReadOnlyList<string> target, bool marks = false)
{
    if (source.Count != target.Count) throw new InvalidDataException("Game string lengths differ.");
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < source.Count; i++)
    {
        if (string.IsNullOrWhiteSpace(source[i]) || source[i] == "---" || string.IsNullOrWhiteSpace(target[i])) continue;
        if (marks)
        {
            // PKHeX stores ribbons as "property\tlabel", unlike its other arrays.
            var en = source[i].Split('\t');
            var zh = target[i].Split('\t');
            if (en.Length != 2 || zh.Length != 2 || en[0] != zh[0])
                throw new InvalidDataException("Ribbon string schema changed.");
            values.TryAdd(en[1], zh[1]);
            if (en[1].EndsWith(" Mark", StringComparison.OrdinalIgnoreCase))
                values.TryAdd(en[1][..^5], zh[1]);
            if (en[0].StartsWith("RibbonMark", StringComparison.Ordinal))
                values.TryAdd(en[0][10..], zh[1]);
        }
        else values.TryAdd(source[i], target[i]);
    }
    return values;
}
var data = new
{
    species = Pair(english.Species, chinese.Species),
    ability = Pair(english.Ability, chinese.Ability),
    nature = Pair(english.Natures, chinese.Natures),
    mark = Pair(english.ribbons, chinese.ribbons, true),
    move = Pair(english.Move, chinese.Move),
    item = Pair(english.Item, chinese.Item),
    types = Pair(english.Types, chinese.Types),
    speciesById = chinese.Species,
};
File.WriteAllText(args[0], JsonSerializer.Serialize(data, new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));
