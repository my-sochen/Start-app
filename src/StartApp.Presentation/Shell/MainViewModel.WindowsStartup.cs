using StartApp.Core;
namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    private IWindowsStartupRegistration? _startup;
    private bool _startsWithWindows;
    private string _startupError = "";
    public string StartupError => _startupError;
    public bool CanChangeWindowsStartup => _startup is not null;
    public bool StartsWithWindows
    {
        get => _startsWithWindows;
        set
        {
            if (_startup is null || value == _startsWithWindows) return;
            _startupError = "";
            try { _startup.SetEnabled(value); _startsWithWindows = _startup.IsEnabled(); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { _startupError = "לא ניתן לשנות הפעלה עם Windows. בדקו הרשאות ונסו שוב. בדקו שוב את המצב לפני ניסיון נוסף."; }
            Raise(nameof(StartsWithWindows)); Raise(nameof(StartupError));
        }
    }
    public string StartupWorkspaceDisplay => _current.Workspaces.FirstOrDefault(w => w.Id == DefaultWorkspaceId)?.Name ?? "ללא סביבת ברירת מחדל";
    public void ConfigureWindowsStartup(IWindowsStartupRegistration registration)
    {
        _startup = registration;
        RefreshWindowsStartup(); Raise(nameof(CanChangeWindowsStartup));
    }
    public void RefreshWindowsStartup()
    {
        if (_startup is null) return;
        _startupError = "";
        try { _startsWithWindows = _startup.IsEnabled(); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { _startupError = "לא ניתן לקרוא את הגדרת ההפעלה של Windows. בדקו הרשאות ונסו שוב."; }
        Raise(nameof(StartsWithWindows)); Raise(nameof(StartupError));
    }
}
