using StartApp.App;

public class WindowUpdateSessionTests
{
    [Fact] public void Unloaded_or_approved_owner_does_not_construct_subscribe_or_show()
    {
        var s = new WindowUpdateSession<object>();
        void Show(bool loaded) => s.ShowStartup(loaded, () => throw new Exception(), (_, _) => throw new Exception(), _ => throw new Exception());
        Show(false); s.ApproveClose(); Show(true); Assert.True(s.CloseApproved); Assert.Null(s.Current);
    }
    [Fact] public void Assigns_identity_then_subscribes_before_showing_and_ignores_duplicate_request()
    {
        var s = new WindowUpdateSession<object>(); var window = new object(); var calls = new List<string>();
        void Show() => s.ShowStartup(true, () => { calls.Add("create"); return window; }, (w, _) => { Assert.Same(window, w); Assert.Same(w, s.Current); calls.Add("subscribe"); }, w => { Assert.Same(window, w); calls.Add("show"); });
        Show(); Show(); Assert.Equal(new[] { "create", "subscribe", "show" }, calls); Assert.False(s.CloseApproved);
    }
    [Fact] public void Closing_clears_reference_and_allows_a_new_nonmodal_window()
    {
        var s = new WindowUpdateSession<object>(); Action? closed = null; var first = new object(); var second = new object();
        s.ShowStartup(true, () => first, (_, callback) => closed = callback, _ => { });
        closed!(); Assert.Null(s.Current); Assert.False(s.CloseApproved);
        s.ShowStartup(true, () => second, (_, _) => { }, _ => { }); Assert.Same(second, s.Current);
    }
    [Fact] public void Approval_is_one_way_without_closing_or_discarding_existing_window()
    {
        var s = new WindowUpdateSession<object>(); var window = new object();
        s.ShowStartup(true, () => window, (_, _) => { }, _ => { }); s.ApproveClose(); s.ApproveClose();
        Assert.True(s.CloseApproved); Assert.Same(window, s.Current);
    }
    [Fact] public void Factory_failure_stays_loud_and_leaves_reference_empty()
    {
        var s = new WindowUpdateSession<object>();
        Assert.Throws<InvalidOperationException>(() => s.ShowStartup(true, () => throw new InvalidOperationException(), (_, _) => { }, _ => { }));
        Assert.Null(s.Current); Assert.False(s.CloseApproved);
    }
    [Fact] public void Show_failure_stays_loud_and_keeps_assigned_window_as_original_host_did()
    {
        var s = new WindowUpdateSession<object>(); var window = new object();
        Assert.Throws<InvalidOperationException>(() => s.ShowStartup(true, () => window, (_, _) => { }, _ => throw new InvalidOperationException()));
        Assert.Same(window, s.Current); Assert.False(s.CloseApproved);
    }
    [Fact] public void Separate_owners_do_not_share_window_or_close_state()
    {
        var a = new WindowUpdateSession<object>(); var b = new WindowUpdateSession<object>();
        a.ShowStartup(true, () => new object(), (_, _) => { }, _ => { }); a.ApproveClose();
        Assert.Null(b.Current); Assert.False(b.CloseApproved);
    }
}
