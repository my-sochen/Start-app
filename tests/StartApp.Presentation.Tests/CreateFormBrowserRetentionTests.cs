using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>The captured site's source browser is retained as the item's default browser (spec Nov 11: דפדפן שזוהה
/// בזמן הלכידה יישמר; browser profiles deferred until after the prototype). The same site open in two browsers stays
/// one capture row per browser, named, so the user picks (owner decision Nov 14). Design-at-creation coverage lives in
/// CreateFormDesignTests; this file covers only the retention and two-choice behavior.</summary>
public class CreateFormBrowserRetentionTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private const string Chrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
    private const string Edge = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";

    private sealed class FixedCapturer(params CapturedItem[] items) : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(items, Array.Empty<string>());
    }
    private static void ScanDone(MainViewModel vm)
    {
        Assert.NotNull(vm.CurrentScan);
        Assert.True(vm.CurrentScan!.Wait(TimeSpan.FromSeconds(15)), "scan did not finish");
        vm.CurrentScan!.GetAwaiter().GetResult();
        Assert.False(vm.IsScanning);
    }
    private sealed class FailingRepo(IStoreRepository inner) : IStoreRepository
    {
        public Store Load() => inner.Load();
        public Store Mutate(Action<Catalog> change) => throw new IOException("simulated disk failure");
        public void Dispose() => inner.Dispose();
    }

    [Fact] public void Capture_new_create_retains_the_source_browser_and_applies_the_design()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome),
            new CapturedItem(ItemType.Folder, "proj", "D:\\proj"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "captured"; vm.NewWorkspaceColor = "#FDE68A"; vm.NewWorkspaceImagePath = "C:\\img\\c.png";
        vm.CaptureCreateCommand.Execute(null);
        var store = repo.Load(); var ws = store.Workspaces.Single();
        Assert.Equal("#FDE68A", ws.Color); Assert.Equal("C:\\img\\c.png", ws.ImagePath);
        var link = ws.Items.Single(m => store.Items[m.ItemId].Type == ItemType.Link);
        Assert.Equal(Chrome, link.Launch?.Browser); Assert.Null(link.Launch?.OpenWith);
        var folder = ws.Items.Single(m => store.Items[m.ItemId].Type == ItemType.Folder);
        Assert.Null(folder.Launch); // nothing captured about a folder: Windows default, no launch config invented
        Assert.False(vm.CreateDirty);
    }

    [Fact] public void Capture_new_create_without_a_detected_browser_keeps_the_windows_default()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "no browser";
        vm.CaptureCreateCommand.Execute(null);
        var ws = repo.Load().Workspaces.Single();
        Assert.Null(ws.Items.Single().Launch);
    }

    [Fact] public void Staged_site_prefills_the_captured_browser_and_save_persists_it()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.NewName = "existing"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
        vm.CaptureExistingCommand.Execute(null); ScanDone(vm);
        vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "existing"));
        var row = vm.CaptureRows.Single();
        vm.DropCaptured(row);
        var staged = vm.StagedRows.Single();
        Assert.Equal(Chrome, staged.OpenWith); // prefilled, still editable/clearable before שמור
        vm.CaptureSaveCommand.Execute(null);
        var store = repo.Load(); var ws = store.Workspaces.Single(w => w.Name == "existing");
        Assert.Equal(Chrome, ws.Items.Single().Launch?.Browser);
    }

    [Fact] public void A_staged_browser_the_user_clears_is_saved_as_the_windows_default()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.NewName = "existing"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
        vm.CaptureExistingCommand.Execute(null); ScanDone(vm);
        vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "existing"));
        vm.DropCaptured(vm.CaptureRows.Single());
        vm.StagedRows.Single().OpenWith = ""; // user clears the retained browser
        vm.CaptureSaveCommand.Execute(null);
        var ws = repo.Load().Workspaces.Single(w => w.Name == "existing");
        Assert.Null(ws.Items.Single().Launch);
    }

    [Fact] public void Capture_new_save_and_exit_persists_the_design_and_the_retained_browser()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "guard saved"; vm.NewWorkspaceColor = "#FDE68A"; vm.NewWorkspaceImagePath = "C:\\img\\c.png";
        vm.CaptureCloseCommand.Execute(null); // X with unsaved design -> the guard offers שמור וצא
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForCapture);
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(vm.UnsavedOpen);
        var store = repo.Load(); var ws = store.Workspaces.Single();
        Assert.Equal("#FDE68A", ws.Color); Assert.Equal("C:\\img\\c.png", ws.ImagePath);
        Assert.Equal(Chrome, ws.Items.Single().Launch?.Browser);
        Assert.False(vm.CaptureDirty);
    }

    [Fact] public void Capture_new_with_a_malformed_captured_browser_refuses_and_creates_nothing()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/", "not-a-path"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "bad browser";
        vm.CaptureCreateCommand.Execute(null);
        Assert.StartsWith("Not created (example.com): ", vm.Status); // ValidateLaunch rejects the malformed provenance
        Assert.Empty(repo.Load().Workspaces);
        Assert.True(vm.CaptureDirty); // rows kept for fixing
    }

    [Fact] public void A_staged_malformed_browser_refuses_the_save_and_keeps_the_staged_row()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/", "not-a-path"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.NewName = "existing"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
        vm.CaptureExistingCommand.Execute(null); ScanDone(vm);
        vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "existing"));
        vm.DropCaptured(vm.CaptureRows.Single());
        Assert.Equal("not-a-path", vm.StagedRows.Single().OpenWith); // malformed provenance is shown as-is, never silently substituted
        vm.CaptureSaveCommand.Execute(null);
        Assert.StartsWith("Not saved (example.com): ", vm.Status);
        Assert.Single(vm.StagedRows); // staged row kept for fixing
        Assert.Empty(repo.Load().Workspaces.Single(w => w.Name == "existing").Items);
    }

    [Fact] public void Capture_new_repository_failure_keeps_the_rows_and_creates_nothing()
    {
        using var inner = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome),
            new CapturedItem(ItemType.Folder, "proj", "D:\\proj"));
        var vm = new MainViewModel(new FailingRepo(inner), new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "unsaved capture";
        vm.CaptureCreateCommand.Execute(null);
        Assert.StartsWith("Save failed, nothing was changed: ", vm.Status);
        var store = inner.Load();
        Assert.Empty(store.Workspaces); Assert.Empty(store.Items);
        Assert.Equal("unsaved capture", vm.NewName);
        Assert.Equal(2, vm.CaptureRows.Count(r => r.IsChecked)); // the captured rows stay selected for a retry
        Assert.True(vm.CaptureDirty);
    }

    [Fact] public void The_same_site_from_two_browsers_is_two_named_rows_and_each_creates_with_its_own_browser()
    {
        // The owner decision (Nov 14): same URL open in two browsers -> two choices, browser named, user picks.
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Edge),
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        Assert.Equal(2, vm.CaptureRows.Count);
        Assert.Equal("Select example.com (Edge)", vm.CaptureRows[0].Label);
        Assert.Equal("Select example.com (Chrome)", vm.CaptureRows[1].Label);
        Assert.Equal("  (Edge)", vm.CaptureRows[0].BrowserSuffix);
        Assert.Equal("  (Chrome)", vm.CaptureRows[1].BrowserSuffix);
        vm.CaptureRows[0].IsChecked = false; // the user keeps only the Chrome choice
        vm.NewName = "chosen";
        vm.CaptureCreateCommand.Execute(null);
        var ws = repo.Load().Workspaces.Single();
        Assert.Equal(Chrome, ws.Items.Single().Launch?.Browser);
    }

    [Fact] public void A_site_without_an_identified_browser_has_no_browser_label()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        var row = vm.CaptureRows.Single();
        Assert.Null(row.BrowserName);
        Assert.Equal("", row.BrowserSuffix);
        Assert.Equal("Select example.com", row.Label);
    }

    [Fact] public void Choosing_both_browser_choices_saves_two_instances_each_with_its_own_browser()
    {
        // Per-instance open-with independence (settled Nov 11) applied to the two-choice capture: ticking both rows
        // creates two memberships of one catalog item, Edge on one, Chrome on the other.
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Edge),
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/", Chrome));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "both";
        vm.CaptureCreateCommand.Execute(null);
        var ws = repo.Load().Workspaces.Single();
        Assert.Equal(2, ws.Items.Count);
        Assert.Equal(1, ws.Items.Count(m => m.Launch?.Browser == Edge));
        Assert.Equal(1, ws.Items.Count(m => m.Launch?.Browser == Chrome));
    }
}
