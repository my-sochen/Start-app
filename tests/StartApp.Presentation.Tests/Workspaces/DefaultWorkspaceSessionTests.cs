using StartApp.Presentation;
using StartApp.Storage.Json;

public class DefaultWorkspaceSessionTests
{
    [Fact] public void Closed_default_is_not_a_pending_removal_even_though_target_is_null()
    {
        var s = new DefaultWorkspaceSession(); Assert.False(s.IsOpen); Assert.Null(s.PendingId); Assert.True(s.IsRemoval);
    }
    [Fact] public void Open_null_target_represents_removal_and_close_clears_both_values()
    {
        var s = new DefaultWorkspaceSession { IsOpen = true }; Assert.True(s.IsRemoval);
        Assert.Equal("להסיר את ברירת המחדל?", s.Title); Assert.Equal("כן, הסר ברירת מחדל", s.AcceptLabel);
        s.Close(); Assert.False(s.IsOpen); Assert.Null(s.PendingId);
    }
    [Fact] public void Replacement_keeps_exact_identity_until_closed()
    {
        var s = new DefaultWorkspaceSession { IsOpen = true, PendingId = " target " }; Assert.False(s.IsRemoval);
        Assert.Equal(" target ", s.PendingId); Assert.Equal("כבר מוגדרת סביבת ברירת מחדל", s.Title); Assert.Equal("כן, הגדר כברירת מחדל", s.AcceptLabel);
        s.Close(); Assert.Null(s.PendingId); Assert.False(s.IsOpen);
    }
    [Fact] public void Sessions_do_not_share_pending_choice()
    {
        var a = new DefaultWorkspaceSession { IsOpen = true, PendingId = "a" }; var b = new DefaultWorkspaceSession();
        a.Close(); Assert.False(b.IsOpen); Assert.Null(b.PendingId);
    }
    [Fact] public void Host_confirms_replacement_after_dialog_close_and_cancel_writes_nothing()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "default-session-" + Guid.NewGuid(), "store.json"));
        repo.Mutate(c => { c.CreateWorkspace("first"); c.CreateWorkspace("second"); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); var first = vm.Workspaces[0].Id; var second = vm.Workspaces[1].Id;
        vm.SetDefaultWorkspace(first); Assert.Equal(first, repo.Load().DefaultWorkspaceId); Assert.False(vm.DefaultConfirmOpen);
        vm.SetDefaultWorkspace(second); Assert.True(vm.DefaultConfirmOpen); Assert.Equal(first, repo.Load().DefaultWorkspaceId);
        vm.CancelDefaultReplaceCommand.Execute(null); Assert.False(vm.DefaultConfirmOpen); Assert.Equal(first, repo.Load().DefaultWorkspaceId);
        vm.SetDefaultWorkspace(second); var openWhenWritten = true;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.DefaultWorkspaceId)) openWhenWritten = vm.DefaultConfirmOpen; };
        vm.ConfirmDefaultReplaceCommand.Execute(null); Assert.False(openWhenWritten); Assert.Equal(second, repo.Load().DefaultWorkspaceId); Assert.False(vm.DefaultConfirmOpen);
    }
    [Fact] public void Host_removal_requires_confirmation_and_same_default_is_a_noop()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "default-session-" + Guid.NewGuid(), "store.json"));
        repo.Mutate(c => c.CreateWorkspace("first")); var vm = new MainViewModel(repo, new NotImplementedLauncher()); var id = vm.Workspaces.Single().Id;
        vm.SetDefaultWorkspace(id); vm.SetDefaultWorkspace(id); Assert.False(vm.DefaultConfirmOpen);
        vm.SetDefaultWorkspace(null); Assert.True(vm.DefaultConfirmOpen); Assert.Equal(id, repo.Load().DefaultWorkspaceId);
        Assert.Equal("כן, הסר ברירת מחדל", vm.DefaultConfirmAcceptLabel); vm.ConfirmDefaultReplaceCommand.Execute(null);
        Assert.False(vm.DefaultConfirmOpen); Assert.Null(repo.Load().DefaultWorkspaceId);
    }
}
