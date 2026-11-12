using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>פופאפ עריכת כתובת, פופאפ פתח באמצעות and הזז למעלה/למטה. The address change is instance-local for EVERY
/// type, sites included; the open-with choice is saved per instance and used on a plain name click; reordering persists.</summary>
public class ItemPopupsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private sealed class RecordingLauncher : IItemLauncher
    {
        public readonly List<(Item Item, LaunchConfig? Launch)> Calls = new();
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Calls.Add((item, launch)); return new(LaunchStatus.Started); }
    }

    private static MainViewModel Vm(JsonStore repo, IItemLauncher launcher)
    {
        var vm = new MainViewModel(repo, launcher);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        return vm;
    }
    // NewItemName is NOT reset here: a duplicate add only finishes on ConfirmDuplicateCommand, which reads it then.
    private static void AddLink(MainViewModel vm, string url, string name)
    { vm.NewItemType = ItemType.Link; vm.NewTarget = url; vm.NewItemName = name; vm.AddItemCommand.Execute(null); }

    [Fact] public void Address_popup_change_is_instance_local_for_a_website_too()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        AddLink(vm, "https://a.example/", "site");
        var wsId = vm.Selected!.Id;
        // the same site in a second workspace must keep its own address
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        var otherWs = vm.Selected!.Id;
        AddLink(vm, "https://a.example/", "site");
        vm.Selected = vm.Workspaces.Single(w => w.Id == wsId);
        var row = vm.Items.Single();
        vm.EditAddressCommand.Execute(row);
        Assert.True(vm.AddressEditOpen); Assert.False(vm.AddressEditDirty);
        Assert.Equal("https://a.example/", vm.EditAddress); // the popup shows the full address
        vm.EditAddress = "https://b.example/";
        Assert.True(vm.AddressEditDirty);
        vm.SaveAddressCommand.Execute(null);
        Assert.Equal("Address saved", vm.Status); Assert.False(vm.AddressEditOpen);
        var store = repo.Load();
        var edited = store.Workspaces.Single(w => w.Id == wsId).Items.Single();
        Assert.Equal(row.InstanceId, edited.Id); // same instance id across the address change
        Assert.Equal("https://b.example/", store.Items[edited.ItemId].Target);
        var other = store.Workspaces.Single(w => w.Id == otherWs).Items.Single();
        Assert.Equal("https://a.example/", store.Items[other.ItemId].Target); // other workspace untouched
    }

    [Fact] public void Address_popup_change_leaves_a_second_instance_of_the_same_address_in_this_workspace()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        AddLink(vm, "https://a.example/", "one");
        AddLink(vm, "https://a.example/", "two");
        vm.ConfirmDuplicateCommand.Execute(null); // user-approved duplicate
        vm.NewItemName = "";
        Assert.Equal(2, vm.Items.Count);
        var row = vm.Items.Single(r => r.Name == "one");
        vm.EditAddressCommand.Execute(row);
        vm.EditAddress = "https://b.example/";
        vm.SaveAddressCommand.Execute(null);
        var items = repo.Load().Workspaces.Single().Items;
        Assert.Equal(2, items.Count);
        var store = repo.Load();
        Assert.Equal("https://b.example/", store.Items[items.Single(m => m.Name == "one").ItemId].Target);
        Assert.Equal("https://a.example/", store.Items[items.Single(m => m.Name == "two").ItemId].Target);
    }

    [Fact] public void Address_popup_copy_and_the_dirty_close_guard()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        AddLink(vm, "https://a.example/", "site");
        string? copied = null; vm.CopyAddressRequested += a => copied = a;
        vm.EditAddressCommand.Execute(vm.Items.Single());
        // clean X closes silently
        vm.CloseAddressEditCommand.Execute(null);
        Assert.False(vm.AddressEditOpen); Assert.False(vm.UnsavedOpen);
        // dirty X asks via the unsaved-exit dialog
        vm.EditAddressCommand.Execute(vm.Items.Single());
        vm.EditAddress = "https://draft.example/";
        vm.CloseAddressEditCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForAddress); Assert.True(vm.AddressEditOpen);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.AddressEditOpen);
        Assert.DoesNotContain("draft", repo.Load().Items.Values.Single().Target);
        // the copy button copies the full value as shown
        vm.EditAddressCommand.Execute(vm.Items.Single());
        vm.CopyFullAddressCommand.Execute(null);
        Assert.Equal("https://a.example/", copied); Assert.Equal("Address copied", vm.Status);
    }

    [Fact] public async Task Open_with_default_is_saved_per_instance_and_used_on_a_plain_name_click()
    {
        using var repo = new JsonStore(Tmp()); var launcher = new RecordingLauncher();
        var vm = Vm(repo, launcher);
        AddLink(vm, "https://a.example/", "one");
        AddLink(vm, "https://a.example/", "two");
        vm.ConfirmDuplicateCommand.Execute(null);
        vm.NewItemName = "";
        var one = vm.Items.Single(r => r.Name == "one");
        vm.OpenWithCommand.Execute(one);
        Assert.True(vm.OpenWithOpen); Assert.Contains("Browser", vm.OpenWithLabel);
        vm.OpenWithProgram = "C:\\Browsers\\other.exe";
        vm.OpenWithSetDefault = true;
        vm.OpenWithGoCommand.Execute(null);
        await vm.OpenTask;
        Assert.False(vm.OpenWithOpen);
        // saved for this instance only - the second instance of the same address keeps the Windows default
        var items = repo.Load().Workspaces.Single().Items;
        Assert.Equal("C:\\Browsers\\other.exe", items.Single(m => m.Name == "one").Launch?.Browser);
        Assert.Null(items.Single(m => m.Name == "two").Launch);
        // the popup's own open used the choice
        Assert.Equal("C:\\Browsers\\other.exe", launcher.Calls.Last().Launch?.Browser);
        // a plain name click now opens with the saved choice, without the popup
        vm.OpenItemCommand.Execute(one);
        await vm.OpenTask;
        Assert.Equal("C:\\Browsers\\other.exe", launcher.Calls.Last().Launch?.Browser);
        Assert.False(vm.OpenWithOpen);
        // the other instance still opens with the Windows default (null config)
        vm.OpenItemCommand.Execute(vm.Items.Single(r => r.Name == "two"));
        await vm.OpenTask;
        Assert.Null(launcher.Calls.Last().Launch);
    }

    [Fact] public async Task Clearing_the_choice_with_set_default_returns_to_the_windows_default()
    {
        using var repo = new JsonStore(Tmp()); var launcher = new RecordingLauncher();
        var vm = Vm(repo, launcher);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewItemName = "doc"; vm.AddItemCommand.Execute(null);
        var row = vm.Items.Single();
        vm.OpenWithCommand.Execute(row);
        Assert.Contains("Program", vm.OpenWithLabel);
        vm.OpenWithProgram = "C:\\Apps\\reader.exe"; vm.OpenWithSetDefault = true;
        vm.OpenWithGoCommand.Execute(null); await vm.OpenTask;
        Assert.Equal("C:\\Apps\\reader.exe", repo.Load().Workspaces.Single().Items.Single().Launch?.OpenWith);
        // reopen, clear the box, keep הגדר כברירת מחדל checked: the saved choice is cleared back to the Windows default
        vm.OpenWithCommand.Execute(vm.Items.Single());
        Assert.Equal("C:\\Apps\\reader.exe", vm.OpenWithProgram); // prefilled from the saved choice
        vm.OpenWithProgram = ""; vm.OpenWithSetDefault = true;
        vm.OpenWithGoCommand.Execute(null); await vm.OpenTask;
        Assert.Null(repo.Load().Workspaces.Single().Items.Single().Launch);
        Assert.Null(launcher.Calls.Last().Launch); // this open used the Windows default
    }

    [Fact] public void Open_with_does_not_open_for_a_program_item()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        vm.NewItemType = ItemType.Program; vm.NewTarget = "C:\\Apps\\tool.exe"; vm.NewItemName = "tool"; vm.AddItemCommand.Execute(null);
        vm.OpenWithCommand.Execute(vm.Items.Single());
        Assert.False(vm.OpenWithOpen);
        Assert.Equal("Open with does not apply to a program item (it is the program)", vm.Status);
    }

    [Fact] public void Move_up_down_reorders_within_the_group_and_persists()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        foreach (var n in new[] { "f1", "f2", "f3" })
        { vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\" + n + ".txt"; vm.NewItemName = n; vm.AddItemCommand.Execute(null); }
        AddLink(vm, "https://a.example/", "site");
        var f3 = vm.Items.Single(r => r.Name == "f3");
        vm.MoveItemUpCommand.Execute(f3);
        Assert.Equal(new[] { "f1", "f3", "f2" }, vm.Items.Where(r => r.Group == "Files & Programs").Select(r => r.Name).ToArray());
        // persisted
        var saved = repo.Load().Workspaces.Single();
        Assert.Equal(new[] { "f1", "f3", "f2" }, saved.Items.Where(m => repo.Load().Items[m.ItemId].Type == ItemType.File).Select(m => m.Name).ToArray());
        // first in the group cannot move further up: a no-op
        vm.MoveItemUpCommand.Execute(vm.Items.Single(r => r.Name == "f1"));
        Assert.Equal(new[] { "f1", "f3", "f2" }, vm.Items.Where(r => r.Group == "Files & Programs").Select(r => r.Name).ToArray());
        // the site group is untouched by file moves, and the site cannot move into the files group
        var site = vm.Items.Single(r => r.Name == "site");
        vm.MoveItemDownCommand.Execute(site);
        Assert.Equal("site", vm.Items.Single(r => r.Group == "Sites").Name);
    }

    [Fact] public void Share_entry_display_condition_follows_workspace_count()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo, new RecordingLauncher());
        Assert.False(vm.HasOtherWorkspaces); // שיתוף לסביבה אחרת מוצג רק אם קיימת סביבה אחרת
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.True(vm.HasOtherWorkspaces);
    }
}
