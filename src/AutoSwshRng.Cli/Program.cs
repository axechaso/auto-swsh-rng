using AutoSwshRng.Core;
using AutoSwshRng.Upstream;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"{ProjectInfo.Name}: {ProjectInfo.Description}");
Console.WriteLine(UpstreamSmokeReport.Create().ToDisplayText());
