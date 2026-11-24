using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.Presentation.Tests;

/// <summary>The settled capture rule: an item staged on the destination side offers the FULL item action set
/// (מחיקה, העתק כתובת, עריכת כתובת, פתח באמצעות, הזז למעלה/למטה; שיתוף לסביבה אחרת stays disabled while undefined),
/// with staged semantics - a chosen default and any edit stay staged until שמור and the store is unchanged before it.</summary>
public class StagedItemActionsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private sealed class FakeCapturer : IOpenItemCapturer
    {
        public List<CapturedItem> Items = new()
        {
            new(ItemType.Program, "Notepad", "C:\\Windows\\notepad.exe"),
            new(ItemType.Folder, "proj", "D:\\proj"),
            new(ItemType.Link, "example.com", "https://example.com/"),
        };
        public CaptureScan Scan() => new(Items, Array.Empty<string>());
    }
    private sealed class RecordingLauncher : IItemLauncher
    {
        public readonly List<(Item Item, LaunchConfig? Launch)> Calls = new();
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Calls.Add((item, launch)); return new(LaunchStatus.Started); }
    }
    private static (MainViewModel vm, JsonStore repo) Make(out RecordingLauncher launcher)
    {
        var repo = new JsonStore(Tmp()); launcher = new RecordingLauncher();
        var vm = new MainViewModel(repo, launcher, capturer: new FakeCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.NewName = "A"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
        return (vm, repo);
    }
    private static void ScanDone(MainViewModel vm)
    {
        Assert.NotNull(vm.CurrentScan);
        Assert.True(vm.CurrentScan!.Wait(TimeSpan.FromSeconds(15)), "scan did not finish");
        vm.CurrentScan!.GetAwaiter().GetResult();
        Assert.False(vm.IsScanning);
    }
    private static void StageTwo(MainViewModel vm)
    {
        vm.CaptureExistingCommand.Execute(null); ScanDone(vm);
        vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "A"));
        vm.CaptureRows.Single(r => r.Name == "proj").IsChecked = true;
        vm.CaptureRows.Single(r => r.Name == "example.com").IsChecked = true;
        vm.CaptureAddCommand.Execute(null);
        Assert.Equal(2, vm.StagedRows.Count);
    }

    [Fact] public void Folder_open_with_is_disabled_but_visible_and_file_target_notifies()
    {
        var (vm,repo)=Make(out _);using var store=repo;StageTwo(vm);
        var folder=vm.StagedRows.Single(x=>x.Type==ItemType.Folder);
        Assert.True(folder.OpenWithApplicable);Assert.False(folder.OpenWithEnabled);
        Assert.False(vm.StagedOpenWithCommand.CanExecute(folder));
        var file=new MainViewModel.StagedRow(new CapturedItem(ItemType.File,"file",@"C:\a.txt"));
        Assert.True(file.OpenWithEnabled);var changes=new List<string?>();file.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName);
        file.Target=@"C:\a.pdf";Assert.Equal(@"C:\a.pdf",file.FileTarget);Assert.Contains(nameof(file.FileTarget),changes);
        var program=new MainViewModel.StagedRow(new CapturedItem(ItemType.Program,"program",@"C:\a.exe"));
        Assert.False(program.OpenWithApplicable);Assert.False(program.OpenWithEnabled);
    }

    [Fact] public void Explicit_editor_defaults_closed_and_row_selection_does_not_open_it()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm);
        Assert.All(vm.StagedRows, row => Assert.False(row.IsEditing));
        vm.CaptureRows[0].IsChecked = !vm.CaptureRows[0].IsChecked;
        Assert.All(vm.StagedRows, row => Assert.False(row.IsEditing));
        Assert.Empty(launcher.Calls);
    }
    [Fact] public void Explicit_edit_switch_and_done_preserve_unsaved_values_without_store_or_launch()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm); var before = System.Text.Json.JsonSerializer.Serialize(repo.Load());
        var first = vm.StagedRows[0]; var second = vm.StagedRows[1];
        Assert.True(vm.EditStagedCommand.CanExecute(first)); vm.EditStagedCommand.Execute(first);
        Assert.True(first.IsEditing); Assert.False(second.IsEditing);
        first.Name = "draft name"; first.Description = "draft description"; first.Target = @"D:\edited"; first.OpenWith = @"C:\Windows\notepad.exe";
        vm.EditStagedCommand.Execute(second);
        Assert.False(first.IsEditing); Assert.True(second.IsEditing);
        vm.CloseStagedEditCommand.Execute(second); Assert.All(vm.StagedRows, row => Assert.False(row.IsEditing));
        vm.EditStagedCommand.Execute(first);
        Assert.Equal("draft name", first.Name); Assert.Equal("draft description", first.Description);
        Assert.Equal(@"D:\edited", first.Target); Assert.Equal(@"C:\Windows\notepad.exe", first.OpenWith);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(repo.Load())); Assert.Empty(launcher.Calls);
        vm.CloseStagedEditCommand.Execute(first); vm.CaptureSaveCommand.Execute(null);
        Assert.Empty(vm.StagedRows); Assert.Contains(vm.CaptureWorkspaceItems, row => row.Name == "draft name");
    }
    [Fact] public void Adding_another_row_does_not_open_its_editor_or_change_the_explicit_editor()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm); var first = vm.StagedRows[0]; vm.EditStagedCommand.Execute(first);
        vm.CaptureDropCommand.Execute(vm.CaptureRows.Single(row => row.Name == "Notepad"));
        Assert.True(first.IsEditing); Assert.False(vm.StagedRows.Last().IsEditing);
        var foreign = new MainViewModel.StagedRow(new CapturedItem(ItemType.Link, "other", "https://other.invalid"));
        Assert.False(vm.EditStagedCommand.CanExecute(foreign));
        vm.RemoveStagedCommand.Execute(first); Assert.DoesNotContain(first, vm.StagedRows);
        Assert.All(vm.StagedRows, row => Assert.False(row.IsEditing)); Assert.False(vm.EditStagedCommand.CanExecute(first));
        Assert.Empty(launcher.Calls);
    }

    private sealed class TwoFoldersCapturer : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(new[]
        {
            new CapturedItem(ItemType.Folder, "proj", "D:\\proj"),
            new CapturedItem(ItemType.Folder, "docs", "D:\\docs"),
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/"),
        }, Array.Empty<string>());
    }
    [Fact] public void Staged_reorder_respects_the_four_group_boundaries_like_the_details_screen()
    {
        var repo = new JsonStore(Tmp()); using var _ = repo;
        var vm = new MainViewModel(repo, new RecordingLauncher(), capturer: new TwoFoldersCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.NewName = "A"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
        vm.CaptureExistingCommand.Execute(null); ScanDone(vm);
        vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "A"));
        foreach (var r in vm.CaptureRows) r.IsChecked = true;
        vm.CaptureAddCommand.Execute(null);
        Assert.Equal(new[] { "proj", "docs", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
        var proj = vm.StagedRows[0]; var docs = vm.StagedRows[1]; var link = vm.StagedRows[2];
        // Mirror of the details screen: the entries stay enabled at the group edge and the boundary no-op lives in the move itself.
        Assert.True(vm.StagedMoveUpCommand.CanExecute(proj));                      // group edge above
        Assert.True(vm.StagedMoveUpCommand.CanExecute(link));                      // the only Link: no same-group neighbor
        Assert.True(vm.StagedMoveDownCommand.CanExecute(link));
        vm.StagedMoveDownCommand.Execute(link);                                    // edge no-op, order unchanged
        Assert.Equal(new[] { "proj", "docs", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
        vm.StagedMoveDownCommand.Execute(proj);                                    // swaps within the Folders group
        Assert.Equal(new[] { "docs", "proj", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
        vm.StagedMoveDownCommand.Execute(proj);                                    // group edge below (next is a Link): no-op
        Assert.Equal(new[] { "docs", "proj", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
        vm.StagedMoveUpCommand.Execute(proj);
        Assert.Equal(new[] { "proj", "docs", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
    }

    [Fact] public void Staged_address_edit_uses_the_same_popup_and_stays_staged()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm);
        var staged = vm.StagedRows[0];                                              // proj, a folder
        var original = staged.Target;
        vm.StagedEditAddressCommand.Execute(staged);
        Assert.True(vm.AddressEditOpen);
        Assert.Equal(original, vm.EditAddress);                                     // the popup opens with the full address
        string? copied = null; vm.CopyAddressRequested += a => copied = a;
        vm.CopyFullAddressCommand.Execute(null);
        Assert.Equal(original, copied);                                             // the popup's own copy works as on a saved item
        vm.EditAddress = "D:\\proj-edited";
        Assert.True(vm.AddressEditDirty);                                           // same dirty -> שמור behavior
        vm.SaveAddressCommand.Execute(null);
        Assert.False(vm.AddressEditOpen);
        Assert.Equal("D:\\proj-edited", staged.Target);                           // the staged row took the new address
        Assert.Empty(repo.Load().Workspaces.Single(w => w.Name == "A").Items);      // the store is untouched until שמור
        Assert.Equal(original, vm.CaptureRows.Single(r => r.Name == "proj").Target); // the source capture row is unchanged
    }

    [Fact] public void Copy_address_on_a_staged_row_copies_its_current_address_without_saving()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm);
        var staged = vm.StagedRows[0];
        staged.Target = "D:\\proj-edited";                                          // the inline address edit is what gets copied
        string? copied = null; vm.CopyAddressRequested += a => copied = a;
        vm.StagedCopyAddressCommand.Execute(staged);
        Assert.Equal("D:\\proj-edited", copied);
        Assert.Equal("Address copied", vm.Status);
        Assert.Empty(repo.Load().Workspaces.Single(w => w.Name == "A").Items);      // still staged, nothing saved
    }

    [Fact] public async Task A_staged_open_with_default_stays_staged_until_Save_and_never_touches_the_source()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        StageTwo(vm);
        var link = vm.StagedRows.Single(s => s.Type == ItemType.Link);
        var stagedCount = vm.StagedRows.Count;
        vm.StagedOpenWithCommand.Execute(link);
        Assert.True(vm.OpenWithOpen);
        Assert.Contains("Browser", vm.OpenWithLabel);                               // a staged link asks for a browser, like a saved one
        vm.OpenWithProgram = "C:\\MyBrowser\\b.exe"; vm.OpenWithSetDefault = true;
        vm.OpenWithGoCommand.Execute(null); await vm.OpenTask;
        Assert.False(vm.OpenWithOpen);
        Assert.Equal("C:\\MyBrowser\\b.exe", link.OpenWith);                        // the default landed on the staged row
        Assert.Empty(repo.Load().Workspaces.Single(w => w.Name == "A").Items);      // the store is untouched
        var call = Assert.Single(launcher.Calls);                                   // and it really opened, with the chosen program
        Assert.Equal("https://example.com/", call.Item.Target);
        Assert.Equal("C:\\MyBrowser\\b.exe", call.Launch?.Browser);
        Assert.Equal(stagedCount, vm.StagedRows.Count);                             // the source capture list is unchanged
        vm.CaptureSaveCommand.Execute(null);
        var store = repo.Load(); var m = store.Workspaces.Single(w => w.Name == "A").Items.Single(i => store.Items[i.ItemId].Target == "https://example.com/");
        Assert.Equal("C:\\MyBrowser\\b.exe", m.Launch?.Browser);                    // only שמור persisted the default
    }

    [Fact] public async Task The_saved_item_open_with_popup_is_unchanged()
    {
        var (vm, repo) = Make(out var launcher); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single(w => w.Name == "A"));
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/"; vm.AddItemCommand.Execute(null);
        vm.OpenWithCommand.Execute(vm.Items.Single());
        Assert.True(vm.OpenWithOpen);
        vm.OpenWithProgram = "C:\\MyBrowser\\b.exe"; vm.OpenWithSetDefault = true;
        vm.OpenWithGoCommand.Execute(null); await vm.OpenTask;
        var store = repo.Load();
        var m = store.Workspaces.Single(w => w.Name == "A").Items.Single();
        Assert.Equal("C:\\MyBrowser\\b.exe", m.Launch?.Browser);                    // a saved item's default saves at once, as before
        Assert.Single(launcher.Calls);
    }

    [Fact] public void Delete_from_the_menu_removes_only_that_staged_row()
    {
        var (vm, _) = Make(out _);
        StageTwo(vm);
        var doomed = vm.StagedRows[0];
        vm.RemoveStagedCommand.Execute(doomed);
        Assert.Single(vm.StagedRows); Assert.DoesNotContain(doomed, vm.StagedRows);
    }
}
