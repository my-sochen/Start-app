using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    private readonly IFileRevealer? _fileRevealer;
    public ICommand ShowInFolderCommand { get; private set; } = null!;

    public static bool HasContainingFolder(string type) => type is nameof(ItemType.File) or nameof(ItemType.Program);
    public Task? CurrentReveal { get; private set; }
    private bool _revealing;
    private void InitShowInFolderCommand() => ShowInFolderCommand = Cmd(p =>
    {
        if (_revealing || _fileRevealer is null || !TryRevealTarget(p, out var target)) return;
        CurrentReveal = RevealInFolderAsync(target);
    }, p => !_revealing && _fileRevealer is not null && TryRevealTarget(p, out _));

    private async Task RevealInFolderAsync(string target)
    {
        _revealing = true; RaiseCommands();
        RevealResult result;
        try { result = await _fileRevealer!.RevealAsync(target); }
        finally { _revealing = false; RaiseCommands(); }
        if (result.Status == RevealStatus.Revealed) return;
        var message = result.Status switch
        {
            RevealStatus.Missing => "הפריט לא נמצא בנתיב השמור. לא נפתח מיקום אחר.",
            RevealStatus.Invalid => "לא ניתן להציג בתיקייה: נדרש נתיב מלא ותקין לקובץ או לתוכנה.",
            _ => "לא ניתן להציג את הפריט בתיקייה כרגע."
        };
        ShowToast(message, true, result.Detail);
    }

    // Resolve live rows on every invocation, not a stale menu snapshot. Revealing never writes the store,
    // launches the item, commits an editor or changes its open-with/default program.
    private bool TryRevealTarget(object? value, out string target)
    {
        target = "";
        switch (value)
        {
            case AllItemRow row when !row.MixedTypes && HasContainingFolder(row.Type) && MatchingInstances(row).Any():
                target = row.Target; return true;
            case ItemRow row when HasContainingFolder(row.Type) && Items.Contains(row):
                target = row.Target; return true;
            case StagedRow row when IsCaptureSide && StagedRows.Contains(row) && row.HasContainingFolder:
                target = row.Target; return true;
            default: return false;
        }
    }
}
