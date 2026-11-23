using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Session-only UI state. A checkmark is consent to attempt this item, not proof it is safe or available.</summary>
public sealed class OpeningChoice : ObservableBase
{
    internal Membership Membership { get; }
    internal Item? Item { get; }
    private bool _checked = true;
    private readonly Action _changed;
    public OpeningChoice(Membership membership, Item? item, Action changed) { Membership = membership; Item = item; _changed = changed; }
    public string Name => Membership.Name ?? Item?.Name ?? "פריט חסר";
    public string Target => Item?.Target ?? "";
    public string Type => Item?.Type.ToString() ?? "File";
    public bool IsChecked { get => _checked; set { Set(ref _checked, value); _changed(); } }
}

public sealed class ReopenChoice : ObservableBase
{
    internal int PlanIndex { get; }
    public Item Item { get; }
    public string Name => Item.Name;
    public string Target => Item.Target;
    public string Type => Item.Type.ToString();
    private bool _checked;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    internal ReopenChoice(int index, Item item) { PlanIndex = index; Item = item; }
}

public enum OpeningOutcome { RequestSent, Skipped, Failed, DeclinedReopen, Cancelled }
public sealed record OpeningResult(Item Item, OpeningOutcome Outcome, string Message, string? TechnicalDetails = null)
{
    public string Name => Item.Name;
    public string Target => Item.Target;
    public string Type => Item.Type.ToString();
    public bool IsProblem => Outcome is OpeningOutcome.Skipped or OpeningOutcome.Failed or OpeningOutcome.Cancelled;
}

public sealed partial class MainViewModel
{
    private bool _singleOpening;
    public ObservableCollection<OpeningChoice> OpeningChoices { get; } = new();
    public ObservableCollection<ReopenChoice> ReopenChoices { get; } = new();
    public ObservableCollection<OpeningResult> LastOpeningResults { get; } = new();
    private bool _selectionPending;
    public bool SelectionPending { get => _selectionPending; private set { Set(ref _selectionPending, value); RaiseCommands(); } }
    public bool CanBackToOpeningSelection => OpenDecisionPending && !_singleOpening;
    private bool _resultsOpen;
    public bool LastResultsOpen { get => _resultsOpen; private set { Set(ref _resultsOpen, value); RaiseCommands(); } }
    public bool HasLastOpeningResults => LastOpeningResults.Count > 0;
    public ICommand ConfirmSelectionCommand { get; private set; } = null!;
    public ICommand BackToOpeningSelectionCommand { get; private set; } = null!;
    public ICommand ShowLastOpeningResultsCommand { get; private set; } = null!;
    public ICommand CloseLastOpeningResultsCommand { get; private set; } = null!;
    public ICommand DismissToastCommand { get; private set; } = null!;
    public ICommand ToastDetailsCommand { get; private set; } = null!;
    public ICommand CopyTechnicalDetailsCommand { get; private set; } = null!;
    private readonly ToastSession _toastSession = new();
    public string ToastMessage { get => _toastSession.Message; private set { if (_toastSession.Message != value) { _toastSession.Message = value; Raise(nameof(ToastMessage)); } } }
    public string ToastTechnicalDetails { get => _toastSession.TechnicalDetails; private set { if (_toastSession.TechnicalDetails != value) { _toastSession.TechnicalDetails = value; Raise(nameof(ToastTechnicalDetails)); } } }
    public bool ToastVisible { get => _toastSession.Visible; private set { if (_toastSession.Visible != value) { _toastSession.Visible = value; Raise(nameof(ToastVisible)); } } }
    public bool ToastHasOpeningResults { get => _toastSession.HasOpeningResults; private set => _toastSession.HasOpeningResults = value; }
    public bool ToastPersistent { get => _toastSession.Persistent; private set { if (_toastSession.Persistent != value) { _toastSession.Persistent = value; Raise(nameof(ToastPersistent)); } } }
    private bool _saveFailed;
    public bool SaveBlocked => ReadOnly || _saveFailed;
    public string SaveBlockedMessage => ReadOnly ? "השמירה חסומה: לא ניתן לקרוא את קובץ הנתונים בבטחה. הקובץ לא שונה. בדקו הרשאות ותקינות ואז פתחו את התוכנה מחדש." : "השמירה לא הצליחה והשינויים לא נשמרו. בדקו הרשאות וזמינות של קובץ הנתונים ונסו לשמור שוב.";

    private void InitOpeningCommands()
    {
        ConfirmSelectionCommand = Cmd(_ => StartSelectedCheck(OpeningChoices.Where(x => x.IsChecked).Select(x =>
            (x.Membership, x.Item is null ? null : x.Item with { Name = x.Name })).ToList()), _ => SelectionPending && OpeningChoices.Any(x => x.IsChecked));
        BackToOpeningSelectionCommand = Cmd(_ => { _pendingOpen = null; ReopenChoices.Clear(); OpenProblems.Clear(); SelectionPending = true; RaiseCommands(); }, _ => CanBackToOpeningSelection);
        ShowLastOpeningResultsCommand = Cmd(_ => LastResultsOpen = true, _ => HasLastOpeningResults);
        CloseLastOpeningResultsCommand = Cmd(_ => LastResultsOpen = false, _ => LastResultsOpen);
        DismissToastCommand = Cmd(_ => { ToastVisible = false; _toastSession.NextGeneration(); }, _ => ToastVisible);
        ToastDetailsCommand = Cmd(_ => { ToastVisible = false; LastResultsOpen = ToastHasOpeningResults && HasLastOpeningResults; }, _ => ToastHasOpeningResults && HasLastOpeningResults);
        CopyTechnicalDetailsCommand = Cmd(p => CopyAddressRequested?.Invoke(p as string ?? ""), p => p is string);
    }

    private static string SkipExplanation(PreflightFinding f) => f.State switch
    {
        PreflightState.Missing => "לא נמצא בכתובת השמורה. בדקו את הכתובת ונסו שוב.",
        PreflightState.Unavailable => "לא ניתן היה לבדוק את הפריט בגלל גישה או זמן המתנה. לא נפתח; בדקו הרשאות ונסו שוב.",
        PreflightState.NeedsDecision => "הפריט דורש בדיקה נוספת לפני פתיחה. בדקו את הכתובת והתוכנה שנבחרה.",
        _ => "הפריט או צורת הפתיחה אינם נתמכים. עדכנו את הכתובת או את התוכנה שנבחרה."
    };

    private string _inlineError = "", _inlineHost = "";
    public string InlineError { get => _inlineError; private set => Set(ref _inlineError, value); }
    private void FieldError(string host, string message, string technical)
    {
        _inlineHost = host; InlineError = message;
        foreach (var n in new[] { nameof(AddItemError), nameof(CreateError), nameof(DetailsError), nameof(ItemEditError), nameof(AddressError), nameof(DesignError), nameof(CategoryError) }) Raise(n);
        // Diagnostic compatibility only. Field errors do not create a second toast.
        _status = technical; Raise(nameof(Status));
    }
    private void ClearFieldErrors() => FieldError("", "", "");
    public string AddItemError => _inlineHost == "add" ? InlineError : "";
    public string CreateError => _inlineHost == "create" ? InlineError : "";
    public string DetailsError => _inlineHost == "details" ? InlineError : "";
    public string ItemEditError => _inlineHost == "item" ? InlineError : "";
    public string AddressError => _inlineHost == "address" ? InlineError : "";
    public string DesignError => _inlineHost == "design" ? InlineError : "";
    public string CategoryError => _inlineHost == "category" ? InlineError : "";

    private void SaveFailure(string technical, string? message = null)
    {
        _saveFailed = true; Raise(nameof(SaveBlocked)); Raise(nameof(SaveBlockedMessage));
        ShowToast(message ?? "השמירה נכשלה. השינויים לא נשמרו. בדקו הרשאות וזמינות ונסו שוב.", true, technical);
    }

    private void ShowToast(string message, bool persistent, string? technical = null, bool opening = false)
    {
        var gen = _toastSession.NextGeneration();
        ToastHasOpeningResults = opening; Raise(nameof(ToastHasOpeningResults));
        ToastMessage = message; ToastTechnicalDetails = technical ?? ""; ToastPersistent = persistent; ToastVisible = true;
        RaiseCommands();
        if (!persistent) _ = HideToastLater(gen);
    }
    private async Task HideToastLater(int gen)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (_toastSession.IsCurrent(gen)) ToastVisible = false;
    }
    private void PresentLegacyStatus(string value)
    {
        // Ordinary successful edits, copies and scan progress are deliberately silent. Keep Status for diagnostics/tests only.
        if (value.Contains("failed", StringComparison.OrdinalIgnoreCase) || value.StartsWith("Not ", StringComparison.Ordinal)
            || value.StartsWith("Save or cancel", StringComparison.OrdinalIgnoreCase) || value.Contains("unexpected error", StringComparison.OrdinalIgnoreCase) || value.Contains("left untouched", StringComparison.OrdinalIgnoreCase))
            ShowToast("הפעולה לא הושלמה. בדקו את הפרטים וההרשאות ונסו שוב. אפשר לפתוח פרטים טכניים.", true, value);
        Raise(nameof(SaveBlocked));
    }
}
