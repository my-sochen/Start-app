using System.IO;
using System.Windows;

namespace StartApp.App;

/// <summary>
/// Last-resort crash evidence for the e2e gates. An unhandled exception anywhere - dispatcher, app domain, or an
/// unobserved task - is appended to crash.log next to store.json BEFORE the process dies. Nothing is marked handled:
/// the app still crashes exactly as before; this only preserves WHY, which the hosted runner cannot otherwise show
/// (WinExe has no console and the runner collects no dump). The harness wipes the store directory before each run and
/// Attach deletes any stale file, so a present crash.log always belongs to the current run. Diagnostic evidence only;
/// no product behavior change.
/// </summary>
public static class CrashLogger
{
    private static string? _path;
    private static readonly object Gate = new();

    public static void Attach(string path, Application app)
    {
        _path = path;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* stale cleanup is best effort */ }
        app.DispatcherUnhandledException += (_, e) => Write("DispatcherUnhandledException", e.Exception); // e.Handled untouched: the crash proceeds
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void Write(string source, Exception? ex)
    {
        var path = _path;
        if (path is null) return;
        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, $"[{DateTime.UtcNow:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* last-resort logging never throws */ }
    }
}
