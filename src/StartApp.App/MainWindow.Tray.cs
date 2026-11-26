using System.Windows;

namespace StartApp.App;

public partial class MainWindow
{
    private SystemTraySession? _tray;
    private bool _trayExitRequested;
    internal SystemTraySession? Tray => _tray;
    internal void InitializeTray()
    {
        if (_tray is not null) return;
        try { _tray = new(this); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or System.IO.IOException or ArgumentException or InvalidOperationException)
        {
            // Fail visible: never hide the only window when the icon could not be created.
            _tray = null;
            MessageBox.Show(this, "לא ניתן להפעיל את מגש המערכת. סגירת החלון תסגור את התוכנה כרגיל.", "Start App", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
    }
    internal void RequestTrayExit()
    {
        _trayExitRequested = true;
        try { Close(); }
        finally { _trayExitRequested = false; }
        // A cancelled unsaved-change prompt must not turn the next normal close into a full exit.
    }
}
