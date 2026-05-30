namespace AutoSwshRng.Upstream;

public sealed record UpstreamSmokeEntry(
    string Key,
    string DisplayName,
    bool Passed,
    string Summary,
    IReadOnlyList<string> Details);

public sealed record UpstreamSmokeReport(IReadOnlyList<UpstreamSmokeEntry> Entries)
{
    public static UpstreamSmokeReport Create()
    {
        var shinyType = OwoowRngAdapter.GetShinyType(0);
        var shinyValue = OwoowRngAdapter.GetShinyValue(0x1234, 0x00FF);
        var easyConScriptResult = EasyConScriptAdapter.Evaluate("PRINT \"hello\"");
        var easyConOutput = string.Join("", easyConScriptResult.Printed).Trim();

        return new UpstreamSmokeReport(
        [
            new UpstreamSmokeEntry(
                "owoow",
                "owoow",
                shinyType == "Square",
                $"shiny xor 0 = {shinyType}",
                [
                    $"Assembly: {OwoowUpstreamInfo.AssemblyName}",
                    $"Game enum: {OwoowUpstreamInfo.GameEnumTypeName}",
                    $"shiny value 0x1234 ^ 0x00FF = 0x{shinyValue:X4}",
                ]),
            new UpstreamSmokeEntry(
                "easycon",
                "EasyCon",
                !easyConScriptResult.HasErrors && easyConOutput == "hello",
                $"script errors = {easyConScriptResult.HasErrors}, output = {easyConOutput}",
                [
                    $"Script assembly: {EasyConUpstreamInfo.ScriptAssemblyName}",
                    $"Script key enum: {EasyConUpstreamInfo.GamePadKeyTypeName}",
                    $"Device assembly: {EasyConUpstreamInfo.DeviceAssemblyName}",
                    $"Device direction enum: {EasyConUpstreamInfo.DirectionKeyTypeName}",
                ]),
        ]);
    }

    public UpstreamSmokeEntry GetRequired(string key)
    {
        return Entries.Single(entry => entry.Key == key);
    }

    public string ToDisplayText()
    {
        return string.Join(
            Environment.NewLine,
            Entries.Select(entry => $"{entry.DisplayName}: {(entry.Passed ? "OK" : "FAIL")} - {entry.Summary}"));
    }
}
