using System.IO;
using System.Windows;
using StartApp.Core;
using StartApp.Platform.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.App;

// CI-ONLY composition (branch ci/smoke-open-launch, never part of the PR): real FileSystemProbe/Preflight/WindowsLauncher, but the process starter only
// RECORDS the request to a log file and never starts anything. "slow" preflight delay is synthetic (a sleep), not OS-level blocking.
public partial class App : Application
{
    private JsonStore? _store;
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var dir = Environment.GetEnvironmentVariable("STARTAPP_STORE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartApp");
        _store = new JsonStore(Path.Combine(dir, "store.json"));
        var delay = int.TryParse(Environment.GetEnvironmentVariable("STARTAPP_CI_LAUNCH_DELAY_MS"), out var d) ? d : 0;
        var pre = new CiSlowPreflight(new FileSystemPreflight(new FileSystemProbe()));
        var vm = new MainViewModel(_store, new WindowsLauncher(new CiRecordingStarter(Environment.GetEnvironmentVariable("STARTAPP_CI_LOG") ?? "launch-log.txt", delay)), pre);
        vm.UnexpectedError += ex => Dispatcher.BeginInvoke(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw());
        new MainWindow { DataContext = vm }.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _store?.Dispose(); base.OnExit(e); }
}

sealed class CiRecordingStarter(string log, int delayMs) : IProcessStarter
{
    public void Start(StartRequest r) { if (delayMs > 0) Thread.Sleep(delayMs); File.AppendAllText(log, $"{r.FileName} | shell={r.UseShell} | args=[{string.Join(",", r.Arguments)}]{Environment.NewLine}"); }
}
sealed class CiSlowPreflight(IItemPreflight inner) : IItemPreflight
{
    public PreflightFinding Check(Item i, LaunchConfig? l) { if (i.Target.Contains("slow-item")) Thread.Sleep(2500); return inner.Check(i, l); }
}
