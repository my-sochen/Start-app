namespace StartApp.App;

/// <summary>Owns update-window identity and approved-close state. WPF construction, events,
/// showing, apply launch and actual window close stay explicit callbacks in the host.</summary>
internal sealed class WindowUpdateSession<TWindow> where TWindow : class
{
    public TWindow? Current { get; private set; }
    public bool CloseApproved { get; private set; }
    public void ApproveClose() => CloseApproved = true;
    public void ShowStartup(bool isLoaded, Func<TWindow> create, Action<TWindow, Action> onClosed, Action<TWindow> show)
    {
        if (CloseApproved || !isLoaded || Current is not null) return;
        Current = create();
        onClosed(Current, () => Current = null);
        show(Current);
    }
}
