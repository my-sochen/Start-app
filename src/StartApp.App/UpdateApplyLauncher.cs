using System.IO;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Phase 2: launches the post-close apply helper. The helper is a full copy of the
/// install directory (self-contained multi-file publish, so the exe alone cannot run), executed
/// from the updates work root; it waits for this process to exit, swaps the staged files into
/// the install directory with rollback, and relaunches the installed exe.</summary>
internal static class UpdateApplyLauncher
{
    public static void Launch(StagedUpdate staged)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");
        var installDir = Path.GetDirectoryName(exe)!;
        var workRoot = Path.GetDirectoryName(staged.StagingDirectory)!;
        var helperDir = Path.Combine(workRoot, "helper");
        if (Directory.Exists(helperDir)) Directory.Delete(helperDir, true);
        CopyDirectory(installDir, helperDir);
        var helperExe = Path.Combine(helperDir, Path.GetFileName(exe));
        var relaunch = Path.Combine(installDir, Path.GetFileName(exe));
        var args = "--apply-update --staging \"" + staged.StagingDirectory + "\" --target \"" + installDir
            + "\" --wait-pid " + Environment.ProcessId + " --relaunch \"" + relaunch + "\"";
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helperExe, args)
        { UseShellExecute = false, CreateNoWindow = true });
        if (process is null) throw new InvalidOperationException("Helper launch failed");
        process.Dispose();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, target));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target), overwrite: true);
    }
}

/// <summary>The --apply-update CLI mode: parse, wait for the app to close, swap, relaunch.</summary>
internal static class UpdateApplyCli
{
    public static int Run(string[] args)
    {
        string? staging = null, target = null, relaunch = null; var pid = 0;
        for (var i = 1; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--staging": staging = args[i + 1]; break;
                case "--target": target = args[i + 1]; break;
                case "--wait-pid": _ = int.TryParse(args[i + 1], out pid); break;
                case "--relaunch": relaunch = args[i + 1]; break;
                default: return 3;
            }
        }
        if (staging is null || target is null || relaunch is null || pid <= 0) return 3;
        try { return UpdateSwapper.RunApplyMode(staging, target, pid, relaunch); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return 1; }
    }
}
