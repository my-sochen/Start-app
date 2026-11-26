using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;
public class SignInStartupTests
{
    sealed class Ready(bool online=true) : ISignInReadiness { public Task<bool> WaitAsync(CancellationToken token)=>Task.FromResult(online); }
    sealed class Launcher : IItemLauncher { public List<string> Sent=new();public LaunchResult Launch(Item item,LaunchConfig? config){Sent.Add(item.Name);return new(LaunchStatus.Started);} }
    sealed class Preflight : IItemPreflight { public PreflightFinding Check(Item i,LaunchConfig? c)=>new(i.Name=="missing"?PreflightState.Missing:PreflightState.Ready); }
    static JsonStore Repo()=>new(Path.Combine(Path.GetTempPath(),"startup-"+Guid.NewGuid().ToString("N"),"store.json"));
    static void Fill(JsonStore repo) => repo.Mutate(c=>{var w=c.CreateWorkspace("chosen");c.Store.DefaultWorkspaceId=w.Id;c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,"site","https://example.org/").Id);c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.File,"local",@"C:\test.txt").Id);c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.File,"missing",@"C:\missing.txt").Id);});
    [Fact] public async Task Signin_automatically_checks_opens_ready_reports_missing_and_runs_once()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());
        vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.False(vm.SelectionPending);Assert.Equal(new[]{"site","local"},launcher.Sent);Assert.Equal(3,vm.LastOpeningResults.Count);Assert.Contains(vm.LastOpeningResults,x=>x.Name=="missing"&&x.Outcome==OpeningOutcome.Skipped);
        vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Equal(2,launcher.Sent.Count);
    }
    [Fact] public async Task Offline_timeout_keeps_local_launch_and_reports_sites_skipped()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());vm.StartSignInStartup(new Ready(false));await vm.SignInTask;
        Assert.Equal(new[]{"local"},launcher.Sent);Assert.Contains(vm.LastOpeningResults,x=>x.Name=="site"&&x.Outcome==OpeningOutcome.Skipped&&x.TechnicalDetails!.Contains("אינטרנט"));
    }
    [Fact] public async Task Missing_chosen_environment_never_falls_back_and_manual_start_stays_idle()
    {
        using var repo=Repo();Fill(repo);repo.Mutate(c=>c.Store.DefaultWorkspaceId="deleted");var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());Assert.Empty(launcher.Sent);
        vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Empty(launcher.Sent);
    }
    [Fact] public async Task Work_started_during_delay_is_not_discarded()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());vm.NewWorkspaceDescription="draft";vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Empty(launcher.Sent);Assert.True(vm.CreateDirty);
    }

    sealed class Waiting : ISignInReadiness {public TaskCompletionSource<bool> Gate=new();public async Task<bool> WaitAsync(CancellationToken token)=>await Gate.Task.WaitAsync(token);}
    [Fact] public async Task Closing_during_network_wait_cancels_all_launches()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());var wait=new Waiting();vm.StartSignInStartup(wait);vm.CancelSignInStartup();await vm.SignInTask;Assert.Empty(launcher.Sent);
    }
    [Fact] public async Task Global_items_and_chosen_environment_share_report_without_fallback()
    {
        using var repo=Repo();Fill(repo);repo.Mutate(c=>c.AddGlobalEntry(GlobalListKind.GlobalItems,ItemType.File,"global",@"C:\global.txt"));var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Equal(new[]{"global","site","local"},launcher.Sent);Assert.Equal(4,vm.LastOpeningResults.Count);
    }
    sealed class Registration : IWindowsStartupRegistration {public bool Enabled;public bool Fail; public bool IsEnabled()=>Enabled;public void SetEnabled(bool value){if(Fail)throw new UnauthorizedAccessException();Enabled=value;} }
    [Fact] public void Windows_toggle_reads_actual_state_and_failed_change_stays_visible()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new Launcher());var r=new Registration {Enabled=true};vm.ConfigureWindowsStartup(r);Assert.True(vm.StartsWithWindows);vm.StartsWithWindows=false;Assert.False(r.Enabled);r.Fail=true;vm.StartsWithWindows=true;Assert.False(vm.StartsWithWindows);Assert.NotEmpty(vm.StartupError);
    }
    sealed class BrokenReadiness : ISignInReadiness { public Task<bool> WaitAsync(CancellationToken token)=>Task.FromException<bool>(new InvalidOperationException("test")); }
    [Fact] public async Task Unexpected_readiness_failure_is_reported_to_host_without_launch()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());Exception? got=null;vm.UnexpectedError+=e=>got=e;vm.StartSignInStartup(new BrokenReadiness());await vm.SignInTask;Assert.IsType<InvalidOperationException>(got);Assert.Empty(launcher.Sent);
    }
    sealed class OpenDetector : IOpenStateDetector {public OpenState Detect(Item i,LaunchConfig? c)=>OpenState.Open;}
    [Fact] public async Task Confirmed_already_open_items_are_skipped_without_selection_or_claiming_user_declined()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight(),new OpenDetector());vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Empty(launcher.Sent);Assert.False(vm.SelectionPending);Assert.False(vm.OpenDecisionPending);Assert.All(vm.LastOpeningResults,x=>Assert.DoesNotContain("בחרת",x.Message));
    }
    [Fact] public async Task Turning_off_startup_during_wait_prevents_pending_automatic_launch()
    {
        using var repo=Repo();Fill(repo);var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());var r=new Registration{Enabled=true};vm.ConfigureWindowsStartup(r);var wait=new Waiting();vm.StartSignInStartup(wait);vm.StartsWithWindows=false;wait.Gate.SetResult(true);await vm.SignInTask;Assert.Empty(launcher.Sent);
    }
    [Fact] public async Task Deleted_default_during_wait_keeps_only_global_items_and_never_uses_another_workspace()
    {
        using var repo=Repo();Fill(repo);repo.Mutate(c=>{c.CreateWorkspace("other");c.AddGlobalEntry(GlobalListKind.GlobalItems,ItemType.File,"global",@"C:\global.txt");});var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());var wait=new Waiting();vm.StartSignInStartup(wait);repo.Mutate(c=>c.DeleteWorkspace(vm.DefaultWorkspaceId!));vm.Reload();wait.Gate.SetResult(true);await vm.SignInTask;Assert.Equal(new[]{"global"},launcher.Sent);
    }
    sealed class PartialRegistration : IWindowsStartupRegistration {public bool Enabled;public bool IsEnabled()=>Enabled;public void SetEnabled(bool enabled){Enabled=enabled;throw new IOException("verification failed");}}
    [Fact] public void Partial_registration_failure_refreshes_the_actual_state()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new Launcher());vm.ConfigureWindowsStartup(new PartialRegistration());vm.StartsWithWindows=true;Assert.True(vm.StartsWithWindows);Assert.NotEmpty(vm.StartupError);
    }
    [Fact] public async Task Empty_chosen_workspace_does_not_fall_back_or_open_a_selection_dialog()
    {
        using var repo=Repo();repo.Mutate(c=>{var empty=c.CreateWorkspace("empty");c.Store.DefaultWorkspaceId=empty.Id;var other=c.CreateWorkspace("other");c.AddToWorkspace(other.Id,c.GetOrAddItem(ItemType.File,"wrong",@"C:\wrong.txt").Id);});var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Preflight());vm.StartSignInStartup(new Ready());await vm.SignInTask;Assert.Empty(launcher.Sent);Assert.False(vm.SelectionPending);
    }
}
