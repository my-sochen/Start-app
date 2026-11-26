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
            var online = await readiness.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            // Never reset a draft or another opening started during the settle/network wait.
            if (ReadOnly || CreateDirty || DetailsDirty || CaptureOpen || ItemEditOpen || AddressEditOpen || DesignEditOpen || IsChecking || IsLaunching || SelectionPending || OpenDecisionPending)
            {
                ShowToast("הפתיחה האוטומטית לא בוצעה כי כבר מתבצעת עבודה. אפשר לשגר את הסביבה ידנית.", true); return;
            }
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
            if (!online)
                ShowToast("לא זוהה חיבור לאינטרנט בזמן ההמתנה. פריטים מקומיים ייפתחו; אתרים לא נבדקו וידולגו הפעם.", true);
            if (missing) ShowToast("סביבת ברירת המחדל לא נמצאה. לא נפתחה סביבה אחרת במקומה.", true);
            if (snapshot.Count == 0) return;
            _opening.WorkspaceId = workspace?.Id ?? ""; _opening.SingleItem = workspace is null; DedicatedBrowserWindows = false;
            _automaticSignInOpening = true;
            _startupOffline = !online;
            StartSelectedCheck(snapshot);
            await OpenTask;
            if (missing) ShowToast("סביבת ברירת המחדל לא נמצאה. נבדקו רק הפריטים הגלובליים; לא נפתחה סביבה אחרת במקומה.", true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _automaticSignInOpening = false; _startupOffline = false; }
    }
    private bool _startupOffline;
}
