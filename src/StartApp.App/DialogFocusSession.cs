namespace StartApp.App;

/// <summary>Owns a dialog's active transition, captured return focus and entry mode.
/// UI callbacks decide focus eligibility and perform actual focus changes. No WPF or view-model dependency.</summary>
internal sealed class DialogFocusSession<T>(
    Func<T?> readFocus, Func<bool> isKeyboardEntry,
    Action<bool> focusDialog, Action<T, bool> restoreFocus) where T : class
{
    private T? _returnFocus;
    private bool _active, _keyboardEntry;

    public void Update(bool open)
    {
        if (open)
        {
            if (_active) return;
            _active = true;
            _returnFocus = readFocus();
            _keyboardEntry = isKeyboardEntry();
            focusDialog(_keyboardEntry);
        }
        else if (_active)
        {
            _active = false;
            if (_returnFocus is { } target) restoreFocus(target, _keyboardEntry);
            _returnFocus = null;
        }
    }
}
