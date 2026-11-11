using System.Diagnostics;
using StartApp.Core;
namespace StartApp.Platform.Windows;

/// <summary>What will be started. Arguments are separate strings (ProcessStartInfo.ArgumentList, no escaping or parsing by us). UseShell is only ever
/// true for a File/Folder/Link with no explicit program, where FileName is the single saved path or http(s) URL.</summary>
public sealed record StartRequest(string FileName, IReadOnlyList<string> Arguments, bool UseShell);
public interface IProcessStarter { void Start(StartRequest request); }

public sealed class ProcessStarter : IProcessStarter
{
    public void Start(StartRequest r)
    {
        var psi = new ProcessStartInfo(r.FileName) { UseShellExecute = r.UseShell };
        foreach (var a in r.Arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi); // handle is released immediately: process identity is not window/resource ownership
    }
}

/// <summary>Opens one item. Never builds a command line, never uses cmd.exe, never searches PATH. Ownership is always Unknown, so nothing started here can ever be closed by the app.</summary>
public sealed class WindowsLauncher(IProcessStarter starter) : IItemLauncher
{
    public LaunchResult Launch(Item item, LaunchConfig? launch)
    {
        var form = PreflightRules.CheckForm(item, launch);
        if (form != null) return new(LaunchStatus.Unavailable, Detail: form.Detail);
        var req = item.Type switch
        {
            ItemType.Program => new StartRequest(item.Target, [], false),
            ItemType.Link when launch?.Browser is { } b => new StartRequest(b, [item.Target], false),
            ItemType.File or ItemType.Folder when launch?.OpenWith is { } w => new StartRequest(w, [item.Target], false),
            _ => new StartRequest(item.Target, [], true),
        };
        try { starter.Start(req); return new(LaunchStatus.Started, Ownership.Unknown); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException or IOException or UnauthorizedAccessException)
        { return new(LaunchStatus.Failed, Detail: e.GetType().Name + ": " + e.Message); }
    }
}
