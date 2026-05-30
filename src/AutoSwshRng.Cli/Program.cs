using AutoSwshRng.Core;
using AutoSwshRng.Upstream;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"{ProjectInfo.Name}: {ProjectInfo.Description}");
Console.WriteLine($"owoow smoke: shiny xor 0 = {OwoowRngAdapter.GetShinyType(0)}");

var easyConScriptResult = EasyConScriptAdapter.Evaluate("PRINT \"hello\"");
Console.WriteLine($"EasyCon smoke: errors = {easyConScriptResult.HasErrors}, output = {string.Join("", easyConScriptResult.Printed).Trim()}");
