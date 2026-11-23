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
    [Fact] public void Already_populated_other_environment_is_selectable_and_gets_independent_copy()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        var id = ""; repo.Mutate(c => id = c.CreateWorkspace("target").Id); vm.Reload();
        Assert.True(vm.AddSavedItemToWorkspaces(source, new[] { id }));
        Assert.Contains(vm.SavedItemDestinations(source), w => w.Id == id);
        Assert.True(vm.AddSavedItemToWorkspaces(source, new[] { id }));
        var members = repo.Load().Workspaces.Single(w => w.Id == id).Items; Assert.Equal(2, members.Count);
        Assert.NotEqual(members[0].Id, members[1].Id);
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
    [Fact] public void Multiple_destinations_are_all_or_nothing_when_one_is_missing()
    {
        using var repo=Repo();var vm=Setup(repo);var source=vm.SavedTransferFromDetail(vm.Items.Single())!;var empty="";
        repo.Mutate(c=>empty=c.CreateWorkspace("empty").Id);vm.Reload();
        Assert.False(vm.AddSavedItemToWorkspaces(source,new[]{empty,"missing-destination"}));Assert.Empty(repo.Load().Workspaces.Single(w=>w.Id==empty).Items);
    }

    [Fact] public void Capture_blocks_all_saved_transfer_mutations_even_with_prior_source_snapshot()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        var empty = ""; repo.Mutate(c => empty = c.CreateWorkspace("empty").Id); vm.Reload();
        vm.AddFromOpenCommand.Execute(null); Assert.True(vm.CaptureOpen); Assert.False(vm.SavedItemTransferAllowed);
        Assert.False(vm.CreateWorkspaceFromSavedItem(source, "not created"));
        Assert.False(vm.AddSavedItemToWorkspaces(source, new[] { empty }));
        Assert.False(vm.AddSavedItemToList(source, GlobalListKind.GlobalItems));
        Assert.False(vm.AddSavedItemToList(source, GlobalListKind.Whitelist));
        Assert.False(vm.AddSavedItemToList(source, GlobalListKind.Blacklist));
        var saved = repo.Load(); Assert.Equal(2, saved.Workspaces.Count);
        Assert.Empty(saved.Workspaces.Single(w => w.Id == empty).Items); Assert.Empty(saved.GlobalItems); Assert.Empty(saved.CaptureWhitelist);
        Assert.Empty(saved.CaptureBlacklist.Where(e => !e.IsDefault));
    }

    [Fact] public void Source_environment_remains_eligible_and_receives_independent_copy()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.Equal(source.WorkspaceId, Assert.Single(vm.SavedItemDestinations(source)).Id);
        Assert.True(vm.AddSavedItemToWorkspaces(source, new[] { source.WorkspaceId! }));
        var members = repo.Load().Workspaces.Single().Items; Assert.Equal(2, members.Count);
        Assert.NotEqual(members[0].Id, members[1].Id); Assert.Equal(members[0].Name, members[1].Name);
        Assert.Equal(members[0].Description, members[1].Description); Assert.Equal(members[0].Launch, members[1].Launch);
    }

    [Theory]
    [InlineData(GlobalListKind.GlobalItems)]
    [InlineData(GlobalListKind.Whitelist)]
    [InlineData(GlobalListKind.Blacklist)]
    public void Only_clicked_source_list_is_disabled_not_equal_items_in_other_places(GlobalListKind kind)
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.True(vm.AddSavedItemToList(source, kind)); Assert.False(vm.SavedItemIsSourceList(source, kind));
        vm.OpenGlobalEnvCommand.Execute(null);
        var row = (kind == GlobalListKind.GlobalItems ? vm.GlobalItemRows : kind == GlobalListKind.Whitelist ? vm.WhitelistRows : vm.BlacklistRows).Single(e => e.Target == source.Target);
        var listed = vm.SavedTransferFromGlobal(row)!; Assert.True(vm.SavedItemIsSourceList(listed, kind));
        foreach (var other in Enum.GetValues<GlobalListKind>().Where(k => k != kind)) Assert.False(vm.SavedItemIsSourceList(listed, other));
    }
    [Fact] public void Global_startup_source_can_idempotently_add_existing_list_item_then_confirm_opposite_move()
    {
        using var repo = Repo(); var vm = Setup(repo); var source = vm.SavedTransferFromDetail(vm.Items.Single())!;
        Assert.True(vm.AddSavedItemToList(source, GlobalListKind.GlobalItems)); Assert.True(vm.AddSavedItemToList(source, GlobalListKind.Blacklist));
        vm.OpenGlobalEnvCommand.Execute(null); var global = vm.SavedTransferFromGlobal(vm.GlobalItemRows.Single())!;
        Assert.False(vm.SavedItemIsSourceList(global, GlobalListKind.Blacklist)); Assert.True(vm.AddSavedItemToList(global, GlobalListKind.Blacklist));
        Assert.Single(repo.Load().CaptureBlacklist.Where(e => !e.IsDefault));
        Assert.False(vm.AddSavedItemToList(global, GlobalListKind.Whitelist)); Assert.Single(repo.Load().CaptureBlacklist.Where(e => !e.IsDefault));
        Assert.True(vm.AddSavedItemToList(global, GlobalListKind.Whitelist, true)); Assert.Empty(repo.Load().CaptureBlacklist.Where(e => !e.IsDefault));
        Assert.Single(repo.Load().CaptureWhitelist); Assert.Single(repo.Load().GlobalItems);
    }
    [Theory]
    [InlineData(false,GlobalListKind.GlobalItems)] [InlineData(true,GlobalListKind.GlobalItems)]
    [InlineData(false,GlobalListKind.Whitelist)] [InlineData(true,GlobalListKind.Whitelist)]
    [InlineData(false,GlobalListKind.Blacklist)] [InlineData(true,GlobalListKind.Blacklist)]
    public void Changed_store_is_reported_without_throwing_or_overwriting(bool newer,GlobalListKind destination)
    {
        var path=Path.Combine(Path.GetTempPath(),"transfer-error-"+Guid.NewGuid(),"store.json");
        using var repo=new JsonStore(path);var vm=Setup(repo);var source=vm.SavedTransferFromDetail(vm.Items.Single())!;
        var bytes=newer?"{\"schemaVersion\":999}":"not json";File.WriteAllText(path,bytes);
        Assert.False(vm.AddSavedItemToList(source,destination));
        Assert.True(vm.ReadOnly);Assert.False(vm.SavedItemTransferAllowed);Assert.True(vm.ToastVisible);
        Assert.Contains("האייטם לא נוסף",vm.ToastMessage);Assert.NotEmpty(vm.ToastTechnicalDetails);
        string? copied=null;vm.CopyAddressRequested+=text=>copied=text;vm.CopyTechnicalDetailsCommand.Execute(vm.ToastTechnicalDetails);
        Assert.Equal(vm.ToastTechnicalDetails,copied);
        if(!newer)Assert.Contains(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!,"store.json.diag-*")),copied);
        Assert.Equal("",vm.SavedItemListConflict);Assert.Equal(bytes,File.ReadAllText(path));
        Assert.False(vm.AddSavedItemToList(source,destination));Assert.Equal(bytes,File.ReadAllText(path));
    }
    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void Other_saved_item_destinations_already_fail_closed(bool newer,bool create)
    {
        var path=Path.Combine(Path.GetTempPath(),"transfer-other-"+Guid.NewGuid(),"store.json");using var repo=new JsonStore(path);var vm=Setup(repo);
        var source=vm.SavedTransferFromDetail(vm.Items.Single())!;var destination=repo.Mutate(c=>c.CreateWorkspace("destination")).Workspaces.Last().Id;
        var bytes=newer?"{\"schemaVersion\":999}":"invalid json";File.WriteAllText(path,bytes);
        Assert.False(create?vm.CreateWorkspaceFromSavedItem(source,"new"):vm.AddSavedItemToWorkspaces(source,new[]{destination}));
        Assert.True(vm.ReadOnly);Assert.True(vm.ToastVisible);Assert.Equal(bytes,File.ReadAllText(path));
    }

}
