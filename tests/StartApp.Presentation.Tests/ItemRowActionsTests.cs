using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>שורה של אייטם: clicking the name opens that one item, the row actions menu holds עריכה / מחיקה / העתק כתובת,
/// and the item edit form (טופס עריכת אייטם) edits one instance only, with the unsaved-exit dialog on a dirty cancel.</summary>
public class ItemRowActionsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private sealed class RecordingLauncher : IItemLauncher
    {
        public readonly List<(Item Item, LaunchConfig? Launch)> Calls = new();
        public LaunchResult Next = new(LaunchStatus.Started);
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Calls.Add((item, launch)); return Next; }
    }

    private static MainViewModel VmWithItem(JsonStore repo, IItemLauncher launcher, out string wsId)
    {
        var vm = new MainViewModel(repo, launcher);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewItemName = "first";
        vm.AddItemCommand.Execute(null);
        wsId = vm.Selected!.Id;
        return vm;
    }

    [Fact] public async Task Clicking_the_item_name_opens_that_one_item_only()
    {
        using var repo = new JsonStore(Tmp()); var launcher = new RecordingLauncher();
        var vm = VmWithItem(repo, launcher, out var wsId);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://x.example/"; vm.NewItemName = "second";
        vm.AddItemCommand.Execute(null);
        var row = vm.Items.Single(r => r.Name == "first");
        vm.OpenItemCommand.Execute(row);
        await vm.OpenTask;
        var call = Assert.Single(launcher.Calls);
        Assert.Equal("C:\\a.txt", call.Item.Target);
        Assert.Equal("first: open request sent (not proof it loaded)", vm.Status);
        // פתיחה אחרונה is the workspace's last launch (שגר); a single-item open does not stamp it.
        Assert.Null(repo.Load().Workspaces.Single(w => w.Id == wsId).LastOpenedAt);
    }

    [Fact] public async Task A_failed_single_open_is_reported_honestly()
    {
        using var repo = new JsonStore(Tmp()); var launcher = new RecordingLauncher { Next = new(LaunchStatus.Failed, Detail: "file not found") };
        var vm = VmWithItem(repo, launcher, out _);
        vm.OpenItemCommand.Execute(vm.Items.Single());
        await vm.OpenTask;
        Assert.Equal("first: failed (file not found)", vm.Status);
        Assert.Null(repo.Load().Workspaces.Single().LastOpenedAt);
    }

    [Fact] public void Copy_address_raises_the_copy_request_with_the_full_target()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out _);
        string? copied = null; vm.CopyAddressRequested += a => copied = a;
        vm.CopyAddressCommand.Execute(vm.Items.Single());
        Assert.Equal("C:\\a.txt", copied); Assert.Equal("Address copied", vm.Status);
    }

    [Fact] public void Editing_one_instance_changes_only_that_instance()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out var wsId);
        // a second, independent instance of the same target (allowed after the duplicate warning)
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        vm.ConfirmDuplicateCommand.Execute(null);
        Assert.Equal(2, vm.Items.Count);
        var row = vm.Items.Single(r => r.Name == "first");
        vm.EditItemCommand.Execute(row);
        Assert.True(vm.ItemEditOpen); Assert.False(vm.ItemEditDirty);
        vm.EditItemName = "renamed"; vm.EditItemDescription = "only mine";
        Assert.True(vm.ItemEditDirty);
        vm.SaveItemEditCommand.Execute(null);
        Assert.False(vm.ItemEditOpen); Assert.Equal("Item saved", vm.Status);
        var saved = repo.Load().Workspaces.Single(w => w.Id == wsId);
        Assert.Equal(2, saved.Items.Count);
        var edited = saved.Items.Single(m => m.Name == "renamed");
        Assert.Equal("only mine", edited.Description);
        Assert.DoesNotContain(saved.Items, m => m.Id != edited.Id && m.Name == "renamed");
    }

    [Fact] public void The_edit_form_also_edits_the_address_locally()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out var wsId);
        // the same item in a second workspace must keep its own address
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        var otherWs = vm.Selected!.Id;
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        var w1 = vm.Workspaces.Single(w => w.Id == wsId);
        vm.Selected = w1;
        var row = vm.Items.Single();
        var instanceId = row.InstanceId;
        vm.EditItemCommand.Execute(row);
        Assert.Equal("C:\\a.txt", vm.EditItemTarget);
        vm.EditItemTarget = "C:\\b.txt";
        Assert.True(vm.ItemEditDirty);
        vm.SaveItemEditCommand.Execute(null);
        Assert.Equal("Item saved", vm.Status);
        var store = repo.Load();
        var edited = store.Workspaces.Single(w => w.Id == wsId).Items.Single();
        Assert.Equal(instanceId, edited.Id); // id stable across the address change
        Assert.Equal("C:\\b.txt", store.Items[edited.ItemId].Target);
        var other = store.Workspaces.Single(w => w.Id == otherWs).Items.Single();
        Assert.Equal("C:\\a.txt", store.Items[other.ItemId].Target); // other workspace untouched
    }

    [Fact] public void Dirty_cancel_asks_via_the_unsaved_dialog_and_save_and_exit_applies()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out _);
        var row = vm.Items.Single();
        vm.EditItemCommand.Execute(row);
        vm.EditItemName = "changed";
        vm.CancelItemEditCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForItemEdit); Assert.False(vm.UnsavedForCreate);
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(vm.UnsavedOpen); Assert.False(vm.ItemEditOpen);
        Assert.Equal("changed", repo.Load().Workspaces.Single().Items.Single().Name);
    }

    [Fact] public void Exit_without_saving_discards_the_item_edit()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out _);
        var row = vm.Items.Single();
        vm.EditItemCommand.Execute(row);
        vm.EditItemDescription = "draft";
        vm.CancelItemEditCommand.Execute(null);
        Assert.True(vm.UnsavedForItemEdit);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.UnsavedOpen); Assert.False(vm.ItemEditOpen);
        Assert.Null(repo.Load().Workspaces.Single().Items.Single().Description);
    }

    [Fact] public void A_dirty_item_edit_blocks_app_close_and_back_navigation()
    {
        using var repo = new JsonStore(Tmp());
        var vm = VmWithItem(repo, new RecordingLauncher(), out _);
        vm.EditItemCommand.Execute(vm.Items.Single());
        vm.EditItemName = "unsaved";
        Assert.False(vm.RequestAppClose()); // app close guarded
        Assert.True(vm.UnsavedForItemEdit);
        vm.CloseUnsavedCommand.Execute(null); // X: back to editing
        Assert.True(vm.ItemEditOpen);
        vm.BackCommand.Execute(null); // in-app navigation guarded too
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForItemEdit); Assert.True(vm.InDetails);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.True(vm.InOverview); Assert.False(vm.ItemEditOpen);
    }
}
