using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
public class OpeningNotificationTests
{
    private static JsonStore Repo()=>new(Path.Combine(Path.GetTempPath(),"sa-"+Guid.NewGuid().ToString("N"),"store.json"));
    [Fact] public void RoutineSuccessfulCreateAndCopyAreSilent()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.NewName="w";vm.CreateWorkspaceCommand.Execute(null);Assert.False(vm.ToastVisible);
        vm.NewTarget="C:\\a.txt";vm.AddItemCommand.Execute(null);vm.CopyAddressCommand.Execute(vm.Items.Single());Assert.False(vm.ToastVisible);
    }
    [Fact] public void InvalidFieldStaysInlineNotToast_AndValidSaveClearsIt()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.NewName="w";vm.CreateWorkspaceCommand.Execute(null);
        vm.NewTarget=" bad ";vm.AddItemCommand.Execute(null);Assert.NotEmpty(vm.AddItemError);Assert.False(vm.ToastVisible);Assert.Empty(vm.Items);
        vm.NewTarget="C:\\a.txt";vm.AddItemCommand.Execute(null);Assert.Empty(vm.AddItemError);Assert.Single(vm.Items);Assert.False(vm.ToastVisible);
    }
    [Fact] public void InvalidAddressUsesAddressFieldAndPreservesDraft()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.NewName="w";vm.CreateWorkspaceCommand.Execute(null);vm.NewTarget="C:\\a.txt";vm.AddItemCommand.Execute(null);
        vm.EditAddressCommand.Execute(vm.Items.Single());vm.EditItemTarget=" bad ";vm.SaveItemEditCommand.Execute(null);Assert.NotEmpty(vm.ItemEditError);Assert.Equal(" bad ",vm.EditItemTarget);Assert.True(vm.ItemEditOpen);Assert.False(vm.ToastVisible);
    }
    [Fact] public void InvalidStoreShowsPersistentSaveBlockedState()
    {
        using var repo=Repo();var path=Path.Combine(Path.GetTempPath(),"sa-"+Guid.NewGuid().ToString("N"),"store.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"broken");using var bad=new JsonStore(path);
        var vm=new MainViewModel(bad,new NotImplementedLauncher());Assert.True(vm.SaveBlocked);Assert.NotEmpty(vm.SaveBlockedMessage);Assert.False(vm.CreateWorkspaceCommand.CanExecute(null));Assert.Equal("broken",File.ReadAllText(path));
    }
    [Fact] public async Task SuccessToastAutoHidesButProblemDoesNot()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.NewName="w";vm.CreateWorkspaceCommand.Execute(null);vm.NewTarget="C:\\a.txt";vm.AddItemCommand.Execute(null);
        vm.OpenItemCommand.Execute(vm.Items.Single());await vm.OpenTask;Assert.True(vm.ToastPersistent);await Task.Delay(3100);Assert.True(vm.ToastVisible);Assert.False(vm.LastResultsOpen);vm.DismissToastCommand.Execute(null);Assert.False(vm.ToastVisible);Assert.True(vm.HasLastOpeningResults);
    }
}
