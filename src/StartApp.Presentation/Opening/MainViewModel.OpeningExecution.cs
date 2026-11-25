using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

// Opening execution and cancellation. Public bindings remain on the shared workspace session.
public sealed partial class MainViewModel
{
    private readonly OpeningSessionState _opening = new();


    public ICommand ConfirmOpenAnywayCommand { get; private set; } = null!;
    public ICommand CancelOpenCommand { get; private set; } = null!;
    /// <summary>Per-item problems found before opening; the user decides. Empty when nothing is pending.</summary>
    public ObservableCollection<string> OpenProblems { get; } = new();
    /// <summary>Per-item outcome of the last open (one line per item).</summary>
    public ObservableCollection<string> OpenReport { get; } = new();
    public bool OpenDecisionPending => _opening.DecisionPending;
    /// <summary>Label of the confirm button. When every item is skipped by policy, confirming only shows the report, so it says so.</summary>
    public string ConfirmLabel => "המשך לפתיחה";
    /// <summary>The open panel (with Cancel) is visible while a decision is pending AND while checking or launching, so the user can always cancel.</summary>
    public bool ShowOpenPanel => _opening.ShowOpenPanel;

    private void ClearPending() { if (IsChecking) { _openOperation.CancelCheckAndInvalidate(); IsChecking = false; } if (IsLaunching) _openOperation.CancelLaunch(); _opening.PendingPlan = null; SelectionPending = false; ReopenChoices.Clear(); OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }


    /// <summary>לחיצה על שם האייטם: opens that one item after the same honest preflight the full open uses; the report line
    /// never claims more than a sent request. A real request also stamps the card's last-opened time.</summary>
    private void OpenOne(ItemRow row)
    {
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = row.Name + ": not opened (the saved item record is missing)"; return; }
        // After הגדר כברירת מחדל a plain name click opens with the saved choice directly, without reopening the popup.
        OpenOneCore(row.Name, it, m.Launch);
    }
    private void OpenOneCore(string name, Item it, LaunchConfig? launch)
    {
        ClearPending(); DedicatedBrowserWindows = false; OpeningChoices.Clear(); _opening.SingleItem = true; _opening.WorkspaceId = "";
        var copy = it with { Name = name };
        StartSelectedCheck(new() { (new Membership(it.Id, Launch: launch), copy) });
    }


    /// <summary>Every item is checked first (environment and already-open state). Anything that is not clean is listed with its reason and nothing starts until the
    /// user chooses open anyway or cancel. Already-open is Unknown unless a detector has reliable exact-target evidence: Unknown is reported as "cannot tell whether
    /// this is already open, opening may create a duplicate", never as a duplicate and never as safe. After "anyway", Unsupported items (alias, non-http(s) link,
    /// bad config) are never launched; the rest are attempted exactly as saved, with no substitute app. Nothing is closed. "Started" only means the open request was
    /// sent successfully, not that the document loaded.</summary>
    /// <summary>Overall time allowed for the pre-open checks. Items still unchecked after this are reported as "could not be checked", never as fine.</summary>
    public TimeSpan OpenCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
    private readonly OpeningCheckRunner _checkRunner = new(4);
    private readonly OpenOperationLifetime _openOperation = new();
    public bool IsLaunching { get => _opening.Launching; private set { if (_opening.Launching != value) { _opening.Launching = value; Raise(nameof(IsLaunching)); } RaiseCommands(); } }
    public bool IsChecking { get => _opening.Checking; private set { if (_opening.Checking != value) { _opening.Checking = value; Raise(nameof(IsChecking)); } RaiseCommands(); } }
    /// <summary>The running (or last) check-and-open operation. UI code never waits on it; tests await it.</summary>
    public Task OpenTask { get; private set; } = Task.CompletedTask;

    private void Open()
    {
        ClearPending();
        if (Selected!.Items.Count == 0) { EmptyWorkspaceOpen = true; return; }
        _opening.WorkspaceId = Selected.Id; _opening.SingleItem = false; DedicatedBrowserWindows = false;
        OpeningChoices.Clear();
        foreach (var m in Selected.Items)
        {
            _current.Items.TryGetValue(m.ItemId, out var it);
            OpeningChoices.Add(new OpeningChoice(m, it, RaiseCommands));
        }
        Raise(nameof(HasOpeningSites)); SelectionPending = true; RaiseCommands();
    }

    private void StartSelectedCheck(List<(Membership M, Item? Item)> snapshot)
    {
        SelectionPending = false; _opening.PendingPlan = null; ReopenChoices.Clear();
        var (gen, token) = _openOperation.BeginCheck();
        IsChecking = true;
        OpenTask = Guard(gen, CheckThenOpenAsync(snapshot, gen, token));
    }

    /// <summary>Checks run on at most four background workers, never on the caller's thread, with one overall deadline. A newer Open, a
    /// selection change or Cancel makes this run stale: its result is dropped. Probes already blocked in the OS cannot be cancelled but are bounded (see FileSystemProbe).</summary>
    private async Task CheckThenOpenAsync(List<(Membership M, Item? Item)> snapshot, int gen, CancellationToken token)
    {
        var (done, stop) = await _checkRunner.RunAsync(snapshot, _preflight, _detector, OpenCheckTimeout, token, ReportLate).ConfigureAwait(true);
        if (!_openOperation.IsCurrent(gen) || token.IsCancellationRequested) return;
        stop();
        var plan = new List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var r = done[i];
            if (r is { } ok) plan.Add((ok.Item, ok.Launch, ok.Finding, ok.Dup));
            else plan.Add((snapshot[i].Item ?? new Item(snapshot[i].M.ItemId, ItemType.File, "פריט חסר", ""), snapshot[i].M.Launch, new(PreflightState.Unavailable, "check did not finish in time"), OpenState.Unknown));
        }
        // A detector timeout means Unknown, never failed preflight. Observe only items already found Ready,
        // once, with a separate bounded wait. This may add time but cannot turn detector uncertainty into a skipped valid item.
        if (_detector is IOpenSnapshotSource source && plan.Any(p => p.Finding.State == PreflightState.Ready))
        {
            using var observationStop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var observation = source.ObserveAsync(plan.Where(p => p.Finding.State == PreflightState.Ready).Select(p => p.Item).ToArray(), observationStop.Token);
            IOpenStateDetector observed;
            if (await Task.WhenAny(observation, Task.Delay(OpenCheckTimeout, token)).ConfigureAwait(true) == observation)
                observed = await observation.ConfigureAwait(true);
            else
            {
                observationStop.Cancel(); observed = new NoOpenStateDetection();
                _ = observation.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            if (!_openOperation.IsCurrent(gen) || token.IsCancellationRequested) return;
            plan = plan.Select(p => (p.Item, p.Launch, p.Finding, p.Finding.State == PreflightState.Ready ? observed.Detect(p.Item, p.Launch) : OpenState.Unknown)).ToList();
        }
        IsChecking = false;
        var confirmed = plan.Select((p, i) => (p, i)).Where(x => x.p.Finding.State == PreflightState.Ready && x.p.Dup == OpenState.Open).ToList();
        if (confirmed.Count == 0) { await LaunchAsync(plan); return; }
        _opening.PendingPlan = plan;
        foreach (var x in confirmed)
        {
            ReopenChoices.Add(new ReopenChoice(x.i, x.p.Item));
            OpenProblems.Add(x.p.Item.Name + ": כבר פתוח");
        }
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f, OpenState dup)
    {
        var parts = new List<string>();
        if (f.State == PreflightState.Missing) parts.Add("not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        else if (f.State == PreflightState.Unsupported) return "will not be opened (" + f.Detail + ")";
        else if (f.State == PreflightState.NeedsDecision) return "will be skipped, not opened (" + f.Detail + ")";
        else if (f.State == PreflightState.Unavailable) parts.Add("could not be checked" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        if (dup == OpenState.Open) parts.Add("already open (confirmed), opening again will create a duplicate");
        else if (dup == OpenState.Unknown) parts.Add("cannot tell whether it is already open, opening may create a duplicate");
        return string.Join("; ", parts);
    }

    private void ConfirmOpen()
    {
        var plan = _opening.PendingPlan!;
        var declined = ReopenChoices.Where(x => !x.IsChecked).Select(x => x.PlanIndex).ToHashSet();
        _opening.PendingPlan = null; ReopenChoices.Clear(); OpenProblems.Clear(); RaiseCommands();
        var gen = _openOperation.NextGeneration(); OpenTask = Guard(gen, LaunchAsync(plan, declined));
    }

    /// <summary>The last unexpected error from a worker after the preflight deadline publication, with no <see cref="UnexpectedError"/> subscriber.
    /// Other work may still be active. Faults caught around Evaluate report without waiting for another worker or launch call;
    /// infrastructure faults escaping that catch have a WhenAll fallback and may wait for the other workers.</summary>
    public Exception? LateError { get; private set; }
    private void ReportLate(Exception e) { var h = UnexpectedError; if (h is null) LateError = e; else h(e); }

    /// <summary>Raised for an unexpected (programming) error in an open operation, after the view model has reset its busy state and published what it knows.
    /// The host decides how to surface it (the App rethrows it on the UI dispatcher so it is not silent). With no subscriber an operation error is rethrown into <see cref="OpenTask"/>.
    /// Exception: an Evaluate fault caught by a worker after preflight publication is reported immediately, independently of OpenTask or any still-blocked work.
    /// Infrastructure faults escaping the worker catch are observed only when all workers complete.
    /// Such a late or stale report does not reset current busy state or overwrite another operation's results; with no subscriber it is stored in <see cref="LateError"/>.</summary>
    public event Action<Exception>? UnexpectedError;

    /// <summary>A fire-and-forget command must not leave a silently faulted task. Resets busy flags (only if this operation is still the current one),
    /// shows a status, then hands the error to the host; it is never swallowed.</summary>
    private async Task Guard(int gen, Task t)
    {
        try { await t; }
        catch (Exception e)
        {
            if (_openOperation.IsCurrent(gen)) { IsChecking = false; IsLaunching = false; Status = "Open stopped by an unexpected error: " + e.GetType().Name + ". See the report for what was already sent."; }
            var h = UnexpectedError;
            if (h is null) throw;
            h(e);
        }
    }

    /// <summary>Starts items one after another on a background worker (Process.Start can block). Cancel stops FURTHER items only: requests already sent cannot be undone
    /// and are reported. A start call that is blocked in the OS cannot be interrupted; only the single launch worker is ever held by it.</summary>
    private async Task LaunchAsync(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan, HashSet<int>? declined = null)
    {
        var cts = _openOperation.BeginLaunch(); IsLaunching = true;
        var (results, fault) = DedicatedBrowserWindows
            ? await DedicatedWindowLaunchRunner.RunAsync(plan, _launcher, cts.Token, declined).ConfigureAwait(true)
            : await OpeningLaunchRunner.RunAsync(plan, _launcher, cts.Token, declined).ConfigureAwait(true);
        OpenReport.Clear(); LastOpeningResults.Clear();
        foreach (var r in results.OrderBy(r => r.IsProblem ? 0 : 1)) { LastOpeningResults.Add(r); OpenReport.Add(r.Name + ": " + r.Message); }
        var sent = results.Count(r => r.Outcome == OpeningOutcome.RequestSent);
        var stampSaved = sent == 0 || _opening.SingleItem || Apply(c => c.TouchLastOpened(_opening.WorkspaceId));
        IsLaunching = false; Raise(nameof(HasLastOpeningResults));
        var hasProblems = results.Any(r => r.IsProblem);
        if (!stampSaved)
            ShowToast($"נשלחו {sent} בקשות פתיחה, אך זמן הפתיחה האחרונה לא נשמר. בדקו הרשאות ונסו לשמור שוב.", true, ToastTechnicalDetails, opening: true);
        else ShowToast(hasProblems ? $"נשלחו {sent} בקשות פתיחה מתוך {plan.Count}. יש פריטים שלא נפתחו; אפשר לעיין בפרטים." : $"נשלחו {sent} בקשות פתיחה.", hasProblems, opening: true);
        if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
    }

}
