using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.Presentation.Tests;

public class ShowInFolderTests
{
    private sealed class RecordingRevealer : IFileRevealer
    {
        public List<string> Calls = new();
        public RevealStatus Result = RevealStatus.Revealed;
        public Task<RevealResult> RevealAsync(string path) { Calls.Add(path); return Task.FromResult(new RevealResult(Result,"fixture details")); }
    }
    private sealed class FixedCapture : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(new[] {new CapturedItem(ItemType.File,"draft",@"C:\draft.txt"),new CapturedItem(ItemType.Link,"site","https://example.com/")},Array.Empty<string>());
    }
    private static JsonStore Store()
    {
        var repo=new JsonStore(Path.Combine(Path.GetTempPath(),"reveal-"+Guid.NewGuid(),"store.json"));
        repo.Mutate(c=>{
            var w=c.CreateWorkspace("A");
            foreach(var (type,target) in new[]{(ItemType.File,@"C:\a.txt"),(ItemType.Program,@"C:\a.exe"),(ItemType.Folder,@"C:\folder"),(ItemType.Link,"https://example.com/")})
                c.AddToWorkspace(w.Id,c.GetOrAddItem(type,type.ToString(),target).Id);
        });return repo;
    }
    [Fact] public void Saved_details_and_all_items_share_one_nonmutating_action()
    {
        using var repo=Store();var adapter=new RecordingRevealer();var vm=new MainViewModel(repo,new NotImplementedLauncher(),fileRevealer:adapter);
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());var before=System.Text.Json.JsonSerializer.Serialize(repo.Load());
        foreach(var row in vm.Items)
        {
            var applicable=row.Type is "File" or "Program";Assert.Equal(applicable,vm.ShowInFolderCommand.CanExecute(row));
            vm.ShowInFolderCommand.Execute(row);
        }
        Assert.Equal(2,adapter.Calls.Count);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(repo.Load()));
        vm.OpenAllItemsCommand.Execute(null);
        foreach(var row in vm.AllItemRows)
            if(vm.ShowInFolderCommand.CanExecute(row))vm.ShowInFolderCommand.Execute(row);
        Assert.Equal(4,adapter.Calls.Count);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(repo.Load()));
    }
    [Fact] public async Task Staged_action_uses_current_draft_path_and_never_saves()
    {
        using var repo=Store();var adapter=new RecordingRevealer();var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new FixedCapture(),fileRevealer:adapter){ScanMinVisible=TimeSpan.Zero};
        vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single());
        vm.DropCaptured(vm.CaptureRows.Single(x=>x.Type==ItemType.File));var row=vm.StagedRows.Single();row.Target=@"C:\changed.txt";
        var before=System.Text.Json.JsonSerializer.Serialize(repo.Load());Assert.True(vm.ShowInFolderCommand.CanExecute(row));vm.ShowInFolderCommand.Execute(row);
        Assert.Equal(new[]{@"C:\changed.txt"},adapter.Calls);Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(repo.Load()));Assert.True(vm.CaptureDirty);
        vm.RemoveStagedCommand.Execute(row);Assert.False(vm.ShowInFolderCommand.CanExecute(row));vm.ShowInFolderCommand.Execute(row);Assert.Single(adapter.Calls);
    }
    [Theory]
    [InlineData(RevealStatus.Missing,"לא נמצא")][InlineData(RevealStatus.Invalid,"נתיב מלא")][InlineData(RevealStatus.Unavailable,"כרגע")]
    public void Failures_show_Hebrew_feedback(RevealStatus status,string expected)
    {
        using var repo=Store();var adapter=new RecordingRevealer {Result=status};var vm=new MainViewModel(repo,new NotImplementedLauncher(),fileRevealer:adapter);
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());vm.ShowInFolderCommand.Execute(vm.Items.Single(x=>x.Type=="File"));
        Assert.True(vm.ToastVisible);Assert.Contains(expected,vm.ToastMessage);
    }
    [Fact] public void No_adapter_does_not_claim_an_action_is_available()
    {
        using var repo=Store();var vm=new MainViewModel(repo,new NotImplementedLauncher());vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        Assert.False(vm.ShowInFolderCommand.CanExecute(vm.Items.Single(x=>x.Type=="File")));
    }
}
