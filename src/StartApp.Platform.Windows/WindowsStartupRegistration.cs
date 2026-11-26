using Microsoft.Win32;
using StartApp.Core;

namespace StartApp.Platform.Windows;

/// <summary>Shares the installer-owned HKCU Run entry. Does not enable a machine-wide task or request elevation.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistration : IWindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Start App";
    private readonly string _executable;
    public WindowsStartupRegistration(string executable) => _executable = executable;
    public static string Command(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || executable.Any(char.IsControl))
            throw new ArgumentException("Startup requires an absolute executable path");
        return "\"" + executable + "\" --autostart";
    }
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && string.Equals(value, Command(_executable), StringComparison.OrdinalIgnoreCase);
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            if (!File.Exists(_executable)) throw new FileNotFoundException("Startup executable is missing", _executable);
            key.SetValue(ValueName, Command(_executable), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        if (IsEnabled() != enabled) throw new IOException("Windows startup registration could not be verified");
    }
}
