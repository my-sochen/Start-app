using StartApp.Core;

namespace StartApp.Platform.Windows;

public sealed partial class WindowsLauncher
{
    public BrowserWindowTarget ResolveBrowser(Item item, LaunchConfig? launch)
    {
        if (item.Type != ItemType.Link || PreflightRules.CheckForm(item, launch) is { })
            return new(null, "Invalid website or launch configuration");
        try
        {
            var path = launch?.Browser ?? WindowsProtocolAssociations.ExecutableFor(new Uri(item.Target).Scheme);
            if (!SupportedBrowser(path)) return new(null, "Dedicated windows support Chrome and Edge only; the saved or default browser was not changed");
            if (!File.Exists(path)) return new(null, "The selected browser executable was not found");
            return new(path);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, "Could not read the default browser: " + e.GetType().Name); }
    }

    public LaunchResult LaunchWindow(string executable, IReadOnlyList<Item> sites)
    {
        if (!SupportedBrowser(executable) || sites.Count == 0 || sites.Any(s => s.Type != ItemType.Link || PreflightRules.CheckForm(s, new(Browser: executable)) is not null))
            return new(LaunchStatus.Unavailable, Detail: "Unsupported browser or invalid website batch");
        // Conservative guard below Windows' 32767-character limit, including quoting and switches.
        // Never split into extra windows or retry individually, which could duplicate already submitted tabs.
        if (executable.Length + sites.Sum(s => (long)s.Target.Length + 3) + 100 > 30000)
            return new(LaunchStatus.Unavailable, Detail: "The website list is too long for one browser request; select fewer sites");
        try
        {
            starter.Start(new(executable, new[] { "--new-window" }.Concat(sites.Select(s => s.Target)).ToArray(), false));
            return new(LaunchStatus.Started, Ownership.Unknown);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return new(LaunchStatus.Failed, Detail: e.GetType().Name + ": " + e.Message); }
    }

    public static bool SupportedBrowser(string path)
    {
        if (!Catalog.IsAbsoluteWindowsPath(path) || Catalog.ValidateLaunch(ItemType.Link, new(Browser: path)) is not null) return false;
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase);
    }
}
