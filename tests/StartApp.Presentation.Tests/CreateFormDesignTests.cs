using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>Optional image/color at workspace creation (spec Nov 12 morning: name required, category defaults to כללי,
/// description/image/color optional - on the ordinary create form and on the capture-new form, which is the same form).</summary>
public class CreateFormDesignTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

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

    [Fact] public void Optional_color_and_image_make_the_create_form_dirty_and_guard_app_close()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.False(vm.CreateDirty);
        vm.NewWorkspaceColor = "#BFDBFE";
        Assert.True(vm.CreateDirty);
        Assert.False(vm.RequestAppClose()); Assert.True(vm.UnsavedForCreate);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.Equal("", vm.NewWorkspaceColor); Assert.False(vm.CreateDirty);

        vm.NewWorkspaceImagePath = "C:\\img\\bg.png";
        Assert.True(vm.CreateDirty);
        Assert.False(vm.RequestAppClose()); Assert.True(vm.UnsavedForCreate);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.Equal("", vm.NewWorkspaceImagePath); Assert.False(vm.CreateDirty);
        Assert.Empty(repo.Load().Workspaces);
    }

    [Fact] public void Create_applies_the_optional_design_and_resets_the_form()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "designed"; vm.NewWorkspaceColor = "#BBF7D0"; vm.NewWorkspaceImagePath = "C:\\img\\bg.png";
        vm.CreateWorkspaceCommand.Execute(null);
        var w = repo.Load().Workspaces.Single();
        Assert.Equal("#BBF7D0", w.Color); Assert.Equal("C:\\img\\bg.png", w.ImagePath);
        Assert.Equal("", vm.NewWorkspaceColor); Assert.Equal("", vm.NewWorkspaceImagePath); Assert.False(vm.CreateDirty);
    }

    [Fact] public void Create_without_design_leaves_color_and_image_empty()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "plain";
        vm.CreateWorkspaceCommand.Execute(null);
        var w = repo.Load().Workspaces.Single();
        Assert.Null(w.Color); Assert.Null(w.ImagePath);
    }

    [Fact] public void Create_with_an_invalid_color_refuses_and_creates_nothing()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "bad color"; vm.NewWorkspaceColor = "blue";
        vm.CreateWorkspaceCommand.Execute(null);
        Assert.StartsWith("Not created: ", vm.Status);
        Assert.Empty(repo.Load().Workspaces);
        Assert.Equal("blue", vm.NewWorkspaceColor); // input kept for fixing
        Assert.True(vm.CreateDirty);
    }

    [Fact] public void Capture_new_create_applies_the_design()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/"),
            new CapturedItem(ItemType.Folder, "proj", "D:\\proj"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "captured"; vm.NewWorkspaceColor = "#FDE68A"; vm.NewWorkspaceImagePath = "C:\\img\\c.png";
        vm.CaptureCreateCommand.Execute(null);
        var store = repo.Load(); var ws = store.Workspaces.Single();
        Assert.Equal("#FDE68A", ws.Color); Assert.Equal("C:\\img\\c.png", ws.ImagePath);
        var link = ws.Items.Single(m => store.Items[m.ItemId].Type == ItemType.Link);
        Assert.Null(link.Launch); // no launch config invented at create
        var folder = ws.Items.Single(m => store.Items[m.ItemId].Type == ItemType.Folder);
        Assert.Null(folder.Launch);
        Assert.False(vm.CreateDirty);
    }

    [Fact] public void Create_with_a_malformed_image_path_refuses_and_creates_nothing()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "bad image"; vm.NewWorkspaceImagePath = "bg.png"; // relative: not an explicit absolute Windows path
        vm.CreateWorkspaceCommand.Execute(null);
        Assert.StartsWith("Not created: ", vm.Status);
        Assert.Empty(repo.Load().Workspaces);
        Assert.Equal("bg.png", vm.NewWorkspaceImagePath); // input kept for fixing
        Assert.True(vm.CreateDirty);
    }

    [Fact] public void Capture_new_with_a_malformed_image_path_refuses_and_creates_nothing()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "bad capture image"; vm.NewWorkspaceImagePath = "C:\\img\\b\tg.png"; // a control character inside the path
        vm.CaptureCreateCommand.Execute(null);
        Assert.StartsWith("Not created: ", vm.Status);
        Assert.Empty(repo.Load().Workspaces);
        Assert.Equal("C:\\img\\b\tg.png", vm.NewWorkspaceImagePath);
        Assert.True(vm.CaptureDirty);
    }

    private sealed class FailingRepo(IStoreRepository inner) : IStoreRepository
    {
        public Store Load() => inner.Load();
        public Store Mutate(Action<Catalog> change) => throw new IOException("simulated disk failure");
        public void Dispose() => inner.Dispose();
    }

    [Fact] public void A_repository_failure_on_create_keeps_every_typed_value_and_creates_nothing()
    {
        using var inner = new JsonStore(Tmp());
        var vm = new MainViewModel(new FailingRepo(inner), new NotImplementedLauncher());
        vm.NewName = "unsaved"; vm.NewWorkspaceDescription = "keep me"; vm.NewWorkspaceColor = "#BFDBFE"; vm.NewWorkspaceImagePath = "C:\\img\\bg.png";
        vm.CreateWorkspaceCommand.Execute(null);
        Assert.StartsWith("Save failed, nothing was changed: ", vm.Status);
        Assert.Empty(inner.Load().Workspaces); // Mutate refused before any change ran (VM-level retention); post-mutation disk atomicity is covered by the existing JsonStore regression fixtures
        Assert.Equal("unsaved", vm.NewName); Assert.Equal("keep me", vm.NewWorkspaceDescription);
        Assert.Equal("#BFDBFE", vm.NewWorkspaceColor); Assert.Equal("C:\\img\\bg.png", vm.NewWorkspaceImagePath);
        Assert.True(vm.CreateDirty);
    }

    [Fact] public void Capture_new_design_only_marks_capture_dirty_and_X_discard_resets_the_form()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewWorkspaceColor = "#BFDBFE"; vm.NewWorkspaceImagePath = "C:\\img\\bg.png";
        Assert.True(vm.CaptureDirty); // capture-new delegates its dirty state to the shared create form
        vm.CaptureCloseCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForCapture);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.Equal("", vm.NewWorkspaceColor); Assert.Equal("", vm.NewWorkspaceImagePath); Assert.False(vm.CaptureDirty);
        Assert.Empty(repo.Load().Workspaces);
    }

    [Fact] public void Capture_new_save_and_exit_persists_the_design()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "guard saved"; vm.NewWorkspaceColor = "#FDE68A"; vm.NewWorkspaceImagePath = "C:\\img\\c.png";
        vm.CaptureCloseCommand.Execute(null); // X with unsaved design -> the guard offers שמור וצא
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForCapture);
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(vm.UnsavedOpen);
        var store = repo.Load(); var ws = store.Workspaces.Single();
        Assert.Equal("#FDE68A", ws.Color); Assert.Equal("C:\\img\\c.png", ws.ImagePath);
        Assert.Null(ws.Items.Single().Launch); // no launch config invented at create
        Assert.False(vm.CaptureDirty);
    }

    [Fact] public void Create_commands_stay_disabled_without_a_name_even_with_design_typed()
    {
        using var repo = new JsonStore(Tmp());
        var cap = new FixedCapturer(new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.NewWorkspaceColor = "#BFDBFE"; vm.NewWorkspaceImagePath = "C:\\img\\bg.png";
        Assert.False(vm.CreateWorkspaceCommand.CanExecute(null));
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        Assert.False(vm.CaptureCreateCommand.CanExecute(null)); // design alone never enables create
    }

    [Fact] public void Successive_creates_do_not_leak_the_previous_design()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "A"; vm.NewWorkspaceColor = "#BFDBFE"; vm.NewWorkspaceImagePath = "C:\\img\\a.png";
        vm.CreateWorkspaceCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.Equal("", vm.NewWorkspaceColor); Assert.Equal("", vm.NewWorkspaceImagePath);
        vm.NewName = "B"; vm.CreateWorkspaceCommand.Execute(null);
        var store = repo.Load();
        Assert.Equal("#BFDBFE", store.Workspaces.Single(w => w.Name == "A").Color);
        Assert.Null(store.Workspaces.Single(w => w.Name == "B").Color);
        Assert.Null(store.Workspaces.Single(w => w.Name == "B").ImagePath);
    }

    [Fact] public void Capture_new_repository_failure_keeps_the_design_the_rows_and_creates_nothing()
    {
        using var inner = new JsonStore(Tmp());
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/"),
            new CapturedItem(ItemType.Folder, "proj", "D:\\proj"));
        var vm = new MainViewModel(new FailingRepo(inner), new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        vm.NewName = "unsaved capture"; vm.NewWorkspaceColor = "#BFDBFE"; vm.NewWorkspaceImagePath = "C:\\img\\c.png";
        vm.CaptureCreateCommand.Execute(null);
        Assert.StartsWith("Save failed, nothing was changed: ", vm.Status);
        var store = inner.Load();
        Assert.Empty(store.Workspaces); Assert.Empty(store.Items);
        Assert.Equal("unsaved capture", vm.NewName); Assert.Equal("#BFDBFE", vm.NewWorkspaceColor);
        Assert.Equal(2, vm.CaptureRows.Count(r => r.IsChecked)); // the captured rows stay selected for a retry
        Assert.True(vm.CaptureDirty);
    }

}
