namespace AutoSwshRng.Upstream;

public static class OwoowRngAdapter
{
    public static uint GetShinyValue(uint tid, uint sid) => owoow.Core.RNG.Util.GetShinyValue(tid, sid);

    public static string GetShinyType(uint xor) => owoow.Core.RNG.Util.GetShinyType(xor);

    public static string GetHeightString(uint height) => owoow.Core.RNG.Util.GetHeightString(height);
}
