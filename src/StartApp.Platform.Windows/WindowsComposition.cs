using StartApp.Core;
namespace StartApp.Platform.Windows;

/// <summary>The normal (non-test) wiring of the Windows adapters: real file system probe and preflight, the Windows launcher and, unless a test passes its own,
/// the real process starter. The app must always pass BOTH results to the view model: a launcher without the preflight would open items unchecked.
/// Creating this object starts nothing.</summary>
public static class WindowsComposition
{
    public static (IItemLauncher Launcher, IItemPreflight Preflight) Create(IProcessStarter? starter = null) =>
        (new WindowsLauncher(starter ?? new ProcessStarter()), new FileSystemPreflight(new FileSystemProbe()));
}
