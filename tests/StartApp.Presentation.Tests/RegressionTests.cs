using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class CommandNotificationTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void CommandsRaiseCanExecuteChanged_WhenTheirInputsChange()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        int raised = 0; vm.CreateWorkspaceCommand.CanExecuteChanged += (_, _) => raised++;
        vm.NewName = "x";
        Assert.True(raised > 0); Assert.True(vm.CreateWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void FailedSave_KeepsTypedInput()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "keep"; Directory.CreateDirectory(p + ".tmp");
        vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal("keep", vm.NewName); Assert.StartsWith("Save failed", vm.Status);
    }
    [Fact] public void EmptyWorkspace_DoesNotReportOpenedZeroAsSuccess()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "e"; vm.CreateWorkspaceCommand.Execute(null); vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.EmptyWorkspaceOpen); Assert.Equal("", vm.Status); // dialog state, never an "opened 0" success
        vm.CloseEmptyWorkspaceCommand.Execute(null); Assert.False(vm.EmptyWorkspaceOpen);
    }
}

public class StartupFailureTests
{
    private sealed class ThrowingRepo(Exception e) : IStoreRepository
    {
        public Store Load() => throw e;
        public Store Mutate(Action<Catalog> change) => throw e;
        public void Dispose() { }
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(StoreLockedException))]
    public void ExpectedStartupFailures_GoReadOnlyWithMessage_NotCrash(Type t)
    {
        var vm = new MainViewModel(new ThrowingRepo(t == typeof(StoreLockedException) ? new StoreLockedException() : (Exception)Activator.CreateInstance(t)!), new NotImplementedLauncher());
        Assert.True(vm.ReadOnly); Assert.NotEqual("", vm.Status);
        vm.NewName = "x"; Assert.False(vm.CreateWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void ProgrammingErrors_AreNotSwallowed()
        => Assert.Throws<NullReferenceException>(() => new MainViewModel(new ThrowingRepo(new NullReferenceException()), new NotImplementedLauncher()));

    [Fact] public void AddAndRemoveItem_RaiseCommandNotifications()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "s.json"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        int add = 0, rem = 0; vm.AddItemCommand.CanExecuteChanged += (_, _) => add++; vm.RemoveItemCommand.CanExecuteChanged += (_, _) => rem++;
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt";
        Assert.True(add > 0 && vm.AddItemCommand.CanExecute(null));
        vm.AddItemCommand.Execute(null);
        var row = vm.Items.Single();
        Assert.True(vm.RemoveItemCommand.CanExecute(row));
        vm.RemoveItemCommand.Execute(row);
        Assert.True(rem > 0); Assert.Empty(vm.Items);
    }

    [Fact] public void Card_menu_has_no_workspace_deletion() // the user explicitly rejected it (Nov 11)
    {
        Assert.Null(typeof(MainViewModel).GetProperty("DeleteWorkspaceCommand"));
    }
}

/// <summary>Regression: rebuilding CategoryChoices (after any catalog change such as adding an item) wipes a bound
/// ComboBox selection, and the TwoWay SelectedItem binding pushes null back into the view model. RefreshCategories
/// must restore the create/details selections so the category never blanks and the details form never turns dirty
/// from an unrelated change. The test simulates exactly what WPF does on CollectionChanged(Reset).</summary>
public class CategorySelectionStabilityTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void Adding_an_item_keeps_the_details_category_selection()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.True(vm.InDetails);
        Assert.Equal(Workspace.DefaultCategory, vm.EditCategory);
        Assert.False(vm.DetailsDirty);

        // What WPF does when the ItemsSource is reset: the ComboBox loses its selection and the TwoWay
        // binding writes null (read here as "") back into the view-model property, synchronously.
        vm.CategoryChoices.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) vm.EditCategory = "";
        };

        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/"; vm.NewItemName = "x";
        vm.AddItemCommand.Execute(null);

        Assert.Equal(Workspace.DefaultCategory, vm.EditCategory); // selection restored, not blank
        Assert.False(vm.DetailsDirty); // no spurious unsaved-changes state (Save button / exit guard)
    }
}
