namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    public bool CanChangeTrayPreference => !ReadOnly;
    private string _trayPreferenceError = "";
    public string TrayPreferenceError => _trayPreferenceError;
    public bool KeepRunningInTray
    {
        get => _current.KeepRunningInTray;
        set
        {
            if (value == KeepRunningInTray) return;
            _trayPreferenceError = "";
            try
            {
                if (ReadOnly) throw new InvalidOperationException("Store is read-only");
                // Persist only the preference; Show() would reload the selected editor and discard an in-progress draft.
                var saved = _repo.Mutate(c => c.SetKeepRunningInTray(value));
                _current.KeepRunningInTray = saved.KeepRunningInTray;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or StartApp.Core.StoreLockedException or StartApp.Core.StoreCorruptException or StartApp.Core.StoreTooNewException)
            {
                _trayPreferenceError = "ההגדרה לא נשמרה. סגירת החלון לא השתנתה. בדקו הרשאות ונסו שוב.";
            }
            Raise(nameof(KeepRunningInTray)); Raise(nameof(TrayPreferenceError));
        }
    }
}
