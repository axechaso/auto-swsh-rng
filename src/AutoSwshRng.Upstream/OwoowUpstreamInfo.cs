using owoow.Core.Enums;

namespace AutoSwshRng.Upstream;

public static class OwoowUpstreamInfo
{
    public static string? AssemblyName => typeof(Game).Assembly.GetName().Name;

    public static string? GameEnumTypeName => typeof(Game).FullName;

    public static uint CalculateShinyValue(uint tid, uint sid) => owoow.Core.RNG.Util.GetShinyValue(tid, sid);
}
