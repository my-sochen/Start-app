using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class SavedItemTransferTests
{
    private static JsonStore Repo() => new(Path.Combine(Path.GetTempPath(), "transfer-" + Guid.NewGuid(), "store.json"));
    private static MainViewModel Setup(JsonStore repo)
    {
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "source"; vm.CreateWorkspaceCommand.Execute(null); vm.NewTarget = "https://example.com/";
        vm.NewItemName = "local name"; vm.NewDescription = "local description"; vm.AddItemCommand.Execute(null);
        return vm;
    }
    [Fact] public void Copy_to_new_environment_is_atomic_and_keeps_source_values()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.True(vm.CreateWorkspaceFromSavedItem(source, "copy")); var saved = repo.Load(); Assert.Equal(2, saved.Workspaces.Count);
        Assert.All(saved.Workspaces, w => { var m = Assert.Single(w.Items); Assert.Equal("local name", m.Name); Assert.Equal("local description", m.Description); });
        Assert.Single(saved.Items); Assert.NotEqual(saved.Workspaces[0].Items[0].Id, saved.Workspaces[1].Items[0].Id);
    }
    [Fact] public void Invalid_new_environment_name_changes_nothing()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.False(vm.CreateWorkspaceFromSavedItem(source, " ")); Assert.Single(repo.Load().Workspaces);
    }
    [Fact] public void Existing_destination_rejects_duplicate_and_stale_destination_atomically()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        var id = ""; repo.Mutate(c => id = c.CreateWorkspace("target").Id); vm.Reload();
        Assert.Equal(id, Assert.Single(vm.SavedItemDestinations(source)).Id);
        Assert.True(vm.AddSavedItemToWorkspaces(source, new[] { id })); Assert.Empty(vm.SavedItemDestinations(source));
        Assert.False(vm.AddSavedItemToWorkspaces(source, new[] { id })); Assert.Single(repo.Load().Workspaces.Single(w => w.Id == id).Items);
    }
    [Fact] public void Stale_source_rejected_without_creating_new_environment()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        repo.Mutate(c => c.UpdateInstance(source.WorkspaceId!, source.InstanceId!, "changed", "changed"));
        Assert.False(vm.CreateWorkspaceFromSavedItem(source, "copy")); Assert.Single(repo.Load().Workspaces);
    }
    [Fact] public void List_conflict_requires_confirmed_move_and_does_not_remove_workspace()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.True(vm.AddSavedItemToList(source, GlobalListKind.Whitelist));
        Assert.False(vm.AddSavedItemToList(source, GlobalListKind.Blacklist)); Assert.Equal(Catalog.BlacklistConflictError, vm.SavedItemListConflict);
        Assert.Single(repo.Load().CaptureWhitelist);
        Assert.True(vm.AddSavedItemToList(source, GlobalListKind.Blacklist, true)); Assert.Empty(repo.Load().CaptureWhitelist);
        Assert.Single(repo.Load().CaptureBlacklist.Where(e => !e.IsDefault)); Assert.Single(repo.Load().Workspaces.Single().Items);
        Assert.True(vm.AddSavedItemToList(source, GlobalListKind.GlobalItems)); Assert.Single(repo.Load().GlobalItems);
    }
    [Fact] public void Global_saved_source_can_copy_to_environment_without_launch_preferences()
    {
        using var repo = Repo(); repo.Mutate(c => c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "global", "https://example.com/global"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.OpenGlobalEnvCommand.Execute(null);
        var source = vm.SavedTransferFromGlobal(vm.GlobalItemRows.Single())!;
        Assert.True(vm.CreateWorkspaceFromSavedItem(source, "from global")); var saved = repo.Load();
        Assert.Single(saved.GlobalItems); var m = Assert.Single(saved.Workspaces.Single().Items); Assert.Null(m.Launch); Assert.Equal("global", m.Name);
    }
    [Fact] public void Effective_opening_preference_is_copied_not_shared_mutably()
    {
        using var repo = Repo(); var vm = Setup(repo); var row = vm.Items.Single();
        repo.Mutate(c => c.SetLaunch(vm.Selected!.Id, row.InstanceId, new LaunchConfig(Browser: @"C:\Browser\browser.exe"))); vm.Reload();
        var source = vm.SavedTransferFromDetail(vm.Items.Single())!; Assert.True(vm.CreateWorkspaceFromSavedItem(source, "copy"));
        Assert.All(repo.Load().Workspaces, w => Assert.Equal(@"C:\Browser\browser.exe", w.Items.Single().Launch!.Browser));
    }
    [Theory]
    [InlineData(GlobalListKind.Whitelist, GlobalListKind.Blacklist)]
    [InlineData(GlobalListKind.Blacklist, GlobalListKind.Whitelist)]
    public void Saved_global_list_source_can_confirm_move_in_both_directions(GlobalListKind first, GlobalListKind second)
    {
        using var repo=Repo();repo.Mutate(c=>c.AddGlobalEntry(first,ItemType.Link,"listed","https://example.com/listed"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.OpenGlobalEnvCommand.Execute(null);
        var row=(first==GlobalListKind.Whitelist?vm.WhitelistRows:vm.BlacklistRows).Single(e=>e.Target=="https://example.com/listed");
        var source=vm.SavedTransferFromGlobal(row)!;
        Assert.False(vm.AddSavedItemToList(source,second));Assert.True(vm.AddSavedItemToList(source,second,true));
        Assert.DoesNotContain(new Catalog(repo.Load()).GlobalEntries(first),e=>e.Target==source.Target);
        Assert.Single(new Catalog(repo.Load()).GlobalEntries(second).Where(e=>e.Target==source.Target));
    }
    [Fact] public void Multiple_destinations_are_all_or_nothing_when_one_is_duplicate()
    {
        using var repo=Repo();var vm=Setup(repo);var source=vm.SavedTransferFromDetail(vm.Items.Single())!;var empty="";
        repo.Mutate(c=>empty=c.CreateWorkspace("empty").Id);vm.Reload();
        Assert.False(vm.AddSavedItemToWorkspaces(source,new[]{empty,source.WorkspaceId!}));Assert.Empty(repo.Load().Workspaces.Single(w=>w.Id==empty).Items);
    }

}
