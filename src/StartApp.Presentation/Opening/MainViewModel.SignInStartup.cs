using StartApp.Core;
namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    private readonly CancellationTokenSource _signInStop = new();
    private bool _signInStarted;
    private bool _automaticSignInOpening;
    public Task SignInTask { get; private set; } = Task.CompletedTask;
    public void CancelSignInStartup()
    {
        _signInStop.Cancel();
        if (_automaticSignInOpening) ClearPending();
    }
    public void StartSignInStartup(ISignInReadiness readiness)
    {
        if (_signInStarted) return;
        _signInStarted = true;
        SignInTask = RunSignInStartup(readiness, _signInStop.Token);
    }
    private async Task RunSignInStartup(ISignInReadiness readiness, CancellationToken token)
    {
        try
        {
            var schedule = readiness.Start(token);
            _ = schedule.SitesReady.ContinueWith(t => ReportLate(t.Exception!.GetBaseException()), TaskContinuationOptions.OnlyOnFaulted);
            // Check saved items during the initial delay, not after it. Launch waits are independent of internet.
            token.ThrowIfCancellationRequested();
            var workspace = _current.Workspaces.FirstOrDefault(w => w.Id == DefaultWorkspaceId);
            var missing = DefaultWorkspaceId is { Length: > 0 } && workspace is null;
            var snapshot = new List<(Membership M, Item? Item)>();
            // Preserve the existing global startup contract and add the saved environment, without switching to a fallback.
            foreach (var entry in _current.GlobalItems)
                snapshot.Add((new Membership(entry.Id), new Item(entry.Id, entry.Type, entry.Name, entry.Target)));
            if (workspace is not null)
                foreach (var membership in workspace.Items)
                {
                    _current.Items.TryGetValue(membership.ItemId, out var item);
                    snapshot.Add((membership, item is null ? null : item with { Name = membership.Name ?? item.Name }));
                }
            if (missing) ShowToast("סביבת ברירת המחדל לא נמצאה. לא נפתחה סביבה אחרת במקומה.", true);
            if (snapshot.Count == 0) return;
            _opening.WorkspaceId = workspace?.Id ?? ""; _opening.SingleItem = workspace is null; DedicatedBrowserWindows = false;
            _automaticSignInOpening = true;
            async Task BeforeLaunch(Item item, CancellationToken launchToken)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, launchToken);
                if (item.Type == ItemType.Link) await schedule.SitesReady.WaitAsync(linked.Token);
                else await schedule.LocalReady.WaitAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (_startup is not null)
                {
                    RefreshWindowsStartup();
                    if (!_startsWithWindows || _startupError.Length > 0) throw new OperationCanceledException(launchToken);
                }
                if (!_current.GlobalItems.Any(e=>e.Id==item.Id && e.Target==item.Target && e.Type==item.Type) &&
                    (workspace is null || DefaultWorkspaceId != workspace.Id || !_current.Workspaces.Any(w=>w.Id==workspace.Id && w.Items.Any(m=>m.ItemId==item.Id)) || !_current.Items.TryGetValue(item.Id,out var currentItem) || currentItem.Target!=item.Target || currentItem.Type!=item.Type))
                    throw new OperationCanceledException(launchToken);
                if (ReadOnly || CreateDirty || DetailsDirty || CaptureOpen || ItemEditOpen || AddressEditOpen || DesignEditOpen || UnsavedOpen)
                    throw new OperationCanceledException(launchToken);
            }
            // Deleted or changed selections must never open a stale environment after the wait.
            // Local targets first, so a slow internet indication never holds them behind sites.
            snapshot = snapshot.OrderBy(x => x.Item?.Type == ItemType.Link ? 1 : 0).ToList();
            StartSelectedCheck(snapshot, automaticSignIn: true, beforeLaunch: BeforeLaunch);
            await OpenTask;
            if (missing) ShowToast("סביבת ברירת המחדל לא נמצאה. נבדקו רק הפריטים הגלובליים; לא נפתחה סביבה אחרת במקומה.", true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            ShowToast("הפתיחה האוטומטית נעצרה בגלל תקלה. אפשר לנסות פתיחה ידנית.", true);
            var handler = UnexpectedError;
            if (handler is null) throw;
            handler(e);
        }
        finally { _automaticSignInOpening = false; }
    }
}
