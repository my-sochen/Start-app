using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

// Opening execution and cancellation. Public bindings remain on the shared workspace session.
public sealed partial class MainViewModel
{
    private List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>? _pendingOpen;


    public ICommand ConfirmOpenAnywayCommand { get; }
    public ICommand CancelOpenCommand { get; }
    /// <summary>Per-item problems found before opening; the user decides. Empty when nothing is pending.</summary>
    public ObservableCollection<string> OpenProblems { get; } = new();
    /// <summary>Per-item outcome of the last open (one line per item).</summary>
    public ObservableCollection<string> OpenReport { get; } = new();
    public bool OpenDecisionPending => _pendingOpen != null;
    /// <summary>Label of the confirm button. When every item is skipped by policy, confirming only shows the report, so it says so.</summary>
    public string ConfirmLabel => "המשך לפתיחה";
    /// <summary>The open panel (with Cancel) is visible while a decision is pending AND while checking or launching, so the user can always cancel.</summary>
    public bool ShowOpenPanel => _isChecking || _isLaunching;

    private void ClearPending() { if (IsChecking) { _checkCts?.Cancel(); _generation++; IsChecking = false; } if (IsLaunching) _launchCts?.Cancel(); _pendingOpen = null; SelectionPending = false; ReopenChoices.Clear(); OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }


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
        ClearPending(); OpeningChoices.Clear(); _singleOpening = true; _openWsId = "";
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
    private const int CheckWorkers = 4;
    private int _activeCheckWorkers;
    private int _generation; private CancellationTokenSource? _checkCts; private bool _isChecking;
    private CancellationTokenSource? _launchCts; private bool _isLaunching;
    public bool IsLaunching { get => _isLaunching; private set { Set(ref _isLaunching, value); RaiseCommands(); } }
    public bool IsChecking { get => _isChecking; private set { Set(ref _isChecking, value); RaiseCommands(); } }
    /// <summary>The running (or last) check-and-open operation. UI code never waits on it; tests await it.</summary>
    public Task OpenTask { get; private set; } = Task.CompletedTask;

    private string _openWsId = "";
    private void Open()
    {
        ClearPending();
        if (Selected!.Items.Count == 0) { EmptyWorkspaceOpen = true; return; }
        _openWsId = Selected.Id; _singleOpening = false;
        OpeningChoices.Clear();
        foreach (var m in Selected.Items)
        {
            _current.Items.TryGetValue(m.ItemId, out var it);
            OpeningChoices.Add(new OpeningChoice(m, it, RaiseCommands));
        }
        SelectionPending = true; RaiseCommands();
    }

    private void StartSelectedCheck(List<(Membership M, Item? Item)> snapshot)
    {
        SelectionPending = false; _pendingOpen = null; ReopenChoices.Clear();
        _checkCts?.Cancel(); _checkCts = new CancellationTokenSource(); var gen = ++_generation;
        IsChecking = true;
        OpenTask = Guard(gen, CheckThenOpenAsync(snapshot, gen, _checkCts.Token));
    }

    private (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup) Evaluate(Membership m, Item? it, IOpenStateDetector detector)
    {
        if (it is null) return (new Item(m.ItemId, ItemType.File, "(missing item " + m.ItemId + ")", ""), m.Launch, new(PreflightState.Unsupported, "the saved item record is missing from the catalog"), OpenState.NotOpen);
        var f = PreflightRules.CheckForm(it, m.Launch) ?? _preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready);
        var dup = f.State != PreflightState.Ready ? OpenState.Unknown : detector.Detect(it, m.Launch);
        return (it, m.Launch, f, dup);
    }

    /// <summary>Checks run on at most <see cref="CheckWorkers"/> background workers, never on the caller's thread, with one overall deadline. A newer Open, a
    /// selection change or Cancel makes this run stale: its result is dropped. Probes already blocked in the OS cannot be cancelled but are bounded (see FileSystemProbe).</summary>
    private async Task CheckThenOpenAsync(List<(Membership M, Item? Item)> snapshot, int gen, CancellationToken token)
    {
        var detector = _detector is IOpenSnapshotSource ? new NoOpenStateDetection() : _detector;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token); // deliberately not disposed: a late worker may still call stop.Cancel(), and the parent token source is per operation, so nothing outlives the operation // the deadline stops workers from taking NEW items; a call already running finishes by itself
        var results = new (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[snapshot.Count];
        var next = -1; Exception? fault = null; var gate = new object(); var published = false; var reported = false;
        // Publication is also the fault-ownership handoff. Before it, Guard owns the
        // error and OpenTask includes its delivery. Afterwards, report immediately:
        // another worker or even a launch call may remain blocked indefinitely.
        void ObserveFault(Exception error)
        {
            Exception? late = null;
            lock (gate)
            {
                fault ??= error;
                if (published && !reported) { reported = true; late = fault; }
            }
            if (late is not null) ReportLate(late);
        }
        Task Worker() => Task.Factory.StartNew(() => // dedicated threads: these calls block, and must not starve the thread pool (or the deadline timer)
        {
            try { while (!stop.Token.IsCancellationRequested)
            {
                var i = Interlocked.Increment(ref next); if (i >= snapshot.Count) return;
                try { var r = Evaluate(snapshot[i].M, snapshot[i].Item, detector); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                { var r = (snapshot[i].Item ?? new Item(snapshot[i].M.ItemId, ItemType.File, snapshot[i].M.ItemId, ""), snapshot[i].M.Launch, new PreflightFinding(PreflightState.Unavailable, e.GetType().Name), OpenState.Unknown); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) { ObserveFault(e); stop.Cancel(); return; } // a programming bug is not "unavailable": stop and surface it
            } }
            finally { Interlocked.Decrement(ref _activeCheckWorkers); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var workers = new List<Task>();
        for (int i = 0; i < Math.Min(CheckWorkers, snapshot.Count); i++)
        {
            if (Interlocked.Increment(ref _activeCheckWorkers) > CheckWorkers) { Interlocked.Decrement(ref _activeCheckWorkers); break; }
            workers.Add(Worker());
        }
        var all = Task.WhenAll(workers);
        await Task.WhenAny(all, Task.Delay(OpenCheckTimeout, token)).ConfigureAwait(true);
        // Observe infrastructure faults too, without competing for pre-publication
        // error delivery. This fallback waits for all workers: only errors caught
        // by a worker's Evaluate catch are guaranteed prompt late reporting.
        _ = all.ContinueWith(t => ObserveFault(t.Exception!.GetBaseException()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[] done;
        Exception? f0;
        lock (gate)
        {
            published = true;
            done = ((Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[])results.Clone();
            f0 = fault;
            if (f0 is not null) reported = true; // Guard owns delivery, including no-subscriber rethrow.
        }
        if (f0 is not null) ExceptionDispatchInfo.Capture(f0).Throw();
        if (gen != _generation || token.IsCancellationRequested) return;
        stop.Cancel();
        var plan = new List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var r = done[i];
            if (r is { } ok) plan.Add(ok);
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
            if (gen != _generation || token.IsCancellationRequested) return;
            plan = plan.Select(p => (p.Item, p.Launch, p.Finding, p.Finding.State == PreflightState.Ready ? observed.Detect(p.Item, p.Launch) : OpenState.Unknown)).ToList();
        }
        IsChecking = false;
        var confirmed = plan.Select((p, i) => (p, i)).Where(x => x.p.Finding.State == PreflightState.Ready && x.p.Dup == OpenState.Open).ToList();
        if (confirmed.Count == 0) { await LaunchAsync(plan); return; }
        _pendingOpen = plan;
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
        var plan = _pendingOpen!;
        var declined = ReopenChoices.Where(x => !x.IsChecked).Select(x => x.PlanIndex).ToHashSet();
        _pendingOpen = null; ReopenChoices.Clear(); OpenProblems.Clear(); RaiseCommands();
        var gen = ++_generation; OpenTask = Guard(gen, LaunchAsync(plan, declined));
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
            if (gen == _generation) { IsChecking = false; IsLaunching = false; Status = "Open stopped by an unexpected error: " + e.GetType().Name + ". See the report for what was already sent."; }
            var h = UnexpectedError;
            if (h is null) throw;
            h(e);
        }
    }

    private static bool NeverLaunched(PreflightState s) => s is PreflightState.Missing or PreflightState.Unavailable or PreflightState.Unsupported or PreflightState.NeedsDecision;

    /// <summary>Starts items one after another on a background worker (Process.Start can block). Cancel stops FURTHER items only: requests already sent cannot be undone
    /// and are reported. A start call that is blocked in the OS cannot be interrupted; only the single launch worker is ever held by it.</summary>
    private async Task LaunchAsync(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan, HashSet<int>? declined = null)
    {
        _launchCts = new CancellationTokenSource(); var cts = _launchCts; IsLaunching = true;
        var results = new List<OpeningResult>(); Exception? fault = null;
        await Task.Run(() =>
        {
            try { for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                if (cts.IsCancellationRequested) { results.Add(new(p.Item, OpeningOutcome.Cancelled, "לא נשלחה בקשת פתיחה כי הפעולה בוטלה.")); continue; }
                if (declined?.Contains(i) == true) { results.Add(new(p.Item, OpeningOutcome.DeclinedReopen, "כבר פתוח. בחרת שלא לפתוח שוב.")); continue; }
                if (NeverLaunched(p.Finding.State)) { results.Add(new(p.Item, OpeningOutcome.Skipped, SkipExplanation(p.Finding), p.Finding.Detail)); continue; }
                var r = _launcher.Launch(p.Item, p.Launch);
                results.Add(r.Status == LaunchStatus.Started
                    ? new(p.Item, OpeningOutcome.RequestSent, "בקשת הפתיחה נשלחה. אין בכך אישור שהפריט נטען.")
                    : new(p.Item, OpeningOutcome.Failed, "בקשת הפתיחה נכשלה. בדקו את הכתובת, ההרשאות והתוכנה שנבחרה ונסו שוב.", r.Detail ?? r.Status.ToString()));
            } }
            catch (Exception e) { fault = e; }
        }).ConfigureAwait(true);
        if (fault != null) foreach (var p in plan.Skip(results.Count)) results.Add(new(p.Item, OpeningOutcome.Failed, "לא נפתח: הפעולה נעצרה בגלל תקלה.", fault.GetType().Name));
        OpenReport.Clear(); LastOpeningResults.Clear();
        foreach (var r in results.OrderBy(r => r.IsProblem ? 0 : 1)) { LastOpeningResults.Add(r); OpenReport.Add(r.Name + ": " + r.Message); }
        var sent = results.Count(r => r.Outcome == OpeningOutcome.RequestSent);
        var stampSaved = sent == 0 || _singleOpening || Apply(c => c.TouchLastOpened(_openWsId));
        IsLaunching = false; Raise(nameof(HasLastOpeningResults));
        var hasProblems = results.Any(r => r.IsProblem);
        if (!stampSaved)
            ShowToast($"נשלחו {sent} בקשות פתיחה, אך זמן הפתיחה האחרונה לא נשמר. בדקו הרשאות ונסו לשמור שוב.", true, ToastTechnicalDetails, opening: true);
        else ShowToast(hasProblems ? $"נשלחו {sent} בקשות פתיחה מתוך {plan.Count}. יש פריטים שלא נפתחו; אפשר לעיין בפרטים." : $"נשלחו {sent} בקשות פתיחה.", hasProblems, opening: true);
        if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
    }

}
