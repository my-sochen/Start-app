using StartApp.Core;
using StartApp.Platform.Windows;

// Disposable Windows runner only. Exercise the product adapter, while isolating the fixture's browser profile.
if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || !OperatingSystem.IsWindows()) return 2;
var starter = new FixtureStarter(args[1]);
var launcher = new WindowsLauncher(starter);
var sites = args.Skip(2).Select((url, i) => new Item(i.ToString(), ItemType.Link, i.ToString(), url)).ToArray();
var result = launcher.LaunchWindow(args[0], sites);
Console.WriteLine(result);
return result.Status == LaunchStatus.Started && result.Ownership == Ownership.Unknown ? 0 : 1;

sealed class FixtureStarter(string profile) : IProcessStarter
{
    public void Start(StartRequest request) => new ProcessStarter().Start(request with
    {
        Arguments = new[] { "--no-first-run", "--no-default-browser-check", "--force-renderer-accessibility", "--user-data-dir=" + profile }.Concat(request.Arguments).ToArray()
    });
}
