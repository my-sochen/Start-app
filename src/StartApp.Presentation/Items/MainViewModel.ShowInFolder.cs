using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    private readonly IFileRevealer? _fileRevealer;
    public ICommand ShowInFolderCommand { get; private set; } = null!;

    public static bool HasContainingFolder(string type) => type is nameof(ItemType.File) or nameof(ItemType.Program);
    private void InitShowInFolderCommand() => ShowInFolderCommand = Cmd(p =>
    {
        if (_fileRevealer is null || !TryRevealTarget(p, out var target)) return;
        var result = _fileRevealer.Reveal(target);
        if (result.Status == RevealStatus.Revealed) return;
        var message = result.Status switch
        {
            RevealStatus.Missing => "הפריט לא נמצא בנתיב השמור. לא נפתח מיקום אחר.",
            RevealStatus.Invalid => "לא ניתן להציג בתיקייה: נדרש נתיב מלא ותקין לקובץ או לתוכנה.",
            _ => "לא ניתן להציג את הפריט בתיקייה כרגע."
        };
        ShowToast(message, true, result.Detail);
    }, p => _fileRevealer is not null && TryRevealTarget(p, out _));

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
