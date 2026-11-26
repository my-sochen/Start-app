namespace StartApp.Core;

/// <summary>Resolution is read-only. A missing executable is reported, never substituted with another browser.</summary>
public sealed record BrowserWindowTarget(string? Executable, string? Detail = null);

/// <summary>Optional launch capability for an explicitly selected dedicated-window operation. URLs are supplied in
/// workspace order. A submitted request is not proof of loading, a window handle, or ownership for later closing.</summary>
public interface IBrowserWindowLauncher
{
    BrowserWindowTarget ResolveBrowser(Item item, LaunchConfig? launch);
    LaunchResult LaunchWindow(string executable, IReadOnlyList<Item> sites);
}
