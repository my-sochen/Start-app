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
        Assert.Contains("empty", vm.Status);
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

    [Fact] public void AddAndDelete_RaiseCommandNotifications()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "s.json"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        int add = 0, del = 0; vm.AddItemCommand.CanExecuteChanged += (_, _) => add++; vm.DeleteWorkspaceCommand.CanExecuteChanged += (_, _) => del++;
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt";
        Assert.True(add > 0 && vm.AddItemCommand.CanExecute(null));
        vm.DeleteWorkspaceCommand.Execute(null);
        Assert.True(del > 0); Assert.False(vm.DeleteWorkspaceCommand.CanExecute(null));
    }
}
