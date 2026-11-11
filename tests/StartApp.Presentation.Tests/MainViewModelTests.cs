using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class MainViewModelTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void CreateWorkspace_Persists_AndSelects()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "work"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal("work", vm.Selected!.Name);
        Assert.Single(repo.Load().Workspaces);
    }
    [Fact] public void InvalidFile_GoesReadOnly_AndNothingIsWritten()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "{broken");
        using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.True(vm.ReadOnly); Assert.Contains("untouched", vm.Status);
        vm.NewName = "x"; Assert.False(vm.CreateWorkspaceCommand.CanExecute(null));
        Assert.Equal("{broken", File.ReadAllText(p));
    }
    [Fact] public void OpenWorkspace_WithNoAdapter_ReportsPartialNotSuccess()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); var i = c.GetOrAddItem(ItemType.File, "f", "C:\\a.txt"); c.AddToWorkspace(w.Id, i.Id); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.OpenWorkspaceCommand.Execute(null); Assert.True(vm.OpenDecisionPending); vm.ConfirmOpenAnywayCommand.Execute(null);
        Assert.Contains("0 of 1", vm.Status);
    }

    [Fact] public void AddItem_Persists_ReusesSharedItem_AndRejectsInvalidWithoutTrimming()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "a"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.AddItemCommand.Execute(null);
        Assert.Equal("example.com", vm.Items.Single().Name); Assert.Equal("", vm.NewTarget);
        vm.NewName = "b"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.AddItemCommand.Execute(null);
        Assert.Single(repo.Load().Items); Assert.Equal(2, repo.Load().Workspaces.Count);
        vm.NewTarget = " https://example.com/y"; vm.AddItemCommand.Execute(null);
        Assert.StartsWith("Not added", vm.Status); Assert.Single(repo.Load().Items);
    }
}
