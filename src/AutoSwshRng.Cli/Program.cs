using AutoSwshRng.Cli;
using AutoSwshRng.Core;
using AutoSwshRng.Upstream;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length > 0)
{
    Environment.ExitCode = await HeadlessCliCommand.RunAsync(
        args,
        Console.Out,
        Console.Error,
        HeadlessCliServices.CreateDefaults());
    return;
}

Console.WriteLine($"{ProjectInfo.Name}: {ProjectInfo.Description}");
Console.WriteLine(UpstreamSmokeReport.Create().ToDisplayText());
