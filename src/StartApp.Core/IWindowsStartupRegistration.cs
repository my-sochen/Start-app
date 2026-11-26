namespace StartApp.Core;

/// <summary>Per-user startup registration, read from the operating system rather than a second JSON flag.</summary>
public interface IWindowsStartupRegistration
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
