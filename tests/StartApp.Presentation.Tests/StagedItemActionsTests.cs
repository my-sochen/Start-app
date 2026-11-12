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

    [Fact] public void Moving_a_staged_item_reorders_only_the_staged_list_and_respects_the_bounds()
    {
        var (vm, _) = Make(out _);
        StageTwo(vm);
        var first = vm.StagedRows[0]; var second = vm.StagedRows[1];
        Assert.False(vm.StagedMoveUpCommand.CanExecute(first));                    // already at the top
        Assert.False(vm.StagedMoveDownCommand.CanExecute(second));                 // already at the bottom
        vm.StagedMoveUpCommand.Execute(second);
        Assert.Same(second, vm.StagedRows[0]); Assert.Same(first, vm.StagedRows[1]);
        vm.StagedMoveDownCommand.Execute(second);
        Assert.Same(first, vm.StagedRows[0]); Assert.Same(second, vm.StagedRows[1]);
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
