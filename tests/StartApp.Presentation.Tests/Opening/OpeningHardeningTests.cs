using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
public class OpeningHardeningTests
{
    private sealed class Launcher:IItemLauncher { public int Count;public LaunchResult Launch(Item item,LaunchConfig? launch){Count++;return new(LaunchStatus.Started);} }
    private sealed class Ready:IItemPreflight {public PreflightFinding Check(Item item,LaunchConfig? launch)=>new(PreflightState.Ready);}
    private static JsonStore Repo(int count=1)
    {
        var r=new JsonStore(Path.Combine(Path.GetTempPath(),"sa-"+Guid.NewGuid().ToString("N"),"store.json"));
        r.Mutate(c=>{var w=c.CreateWorkspace("w");for(var i=0;i<count;i++)c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.File,"f"+i,$"C:\\doc{i}.txt").Id);});return r;
    }
    private sealed class FailingRepo(JsonStore original, bool corrupt):IStoreRepository
    {
        public Store Load()=>original.Load();
        public Store Mutate(Action<Catalog> mutation)=>throw (corrupt?new StoreCorruptException("synthetic corruption",null):new IOException("synthetic write denied"));
        public void Dispose(){}
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task FailedTimestampWriteCannotBeHiddenBySuccessfulLaunchToast(bool corrupt)
    {
        using var original=Repo();using var repo=new FailingRepo(original,corrupt);var vm=new MainViewModel(repo,new Launcher(),new Ready());
        vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);await vm.OpenTask;
        Assert.Equal(OpeningOutcome.RequestSent,Assert.Single(vm.LastOpeningResults).Outcome);Assert.True(vm.ToastPersistent);Assert.True(vm.SaveBlocked);Assert.NotEmpty(vm.ToastTechnicalDetails);Assert.Contains("לא נשמר",vm.ToastMessage);Assert.Null(original.Load().Workspaces.Single().LastOpenedAt);
        await Task.Delay(3100);Assert.True(vm.ToastVisible);Assert.True(vm.SaveBlocked);Assert.Equal(corrupt,vm.ReadOnly);
    }
    private sealed class Blocked:IItemPreflight
    {
        public int Entered;public readonly ManualResetEventSlim Gate=new();
        public PreflightFinding Check(Item item,LaunchConfig? launch){Interlocked.Increment(ref Entered);Gate.Wait(TimeSpan.FromSeconds(10));return new(PreflightState.Ready);}
    }
    [Fact] public async Task CancelledGenerationsNeverAccumulateMoreThanFourWorkers()
    {
        using var repo=Repo(4);var blocked=new Blocked();var vm=new MainViewModel(repo,new Launcher(),blocked){OpenCheckTimeout=TimeSpan.FromMilliseconds(100)};
        var tasks=new List<Task>();
        try
        {
            for(var i=0;i<7;i++)
            {
                vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);tasks.Add(vm.OpenTask);
                if(i==0){var until=DateTime.UtcNow.AddSeconds(3);while(blocked.Entered<4&&DateTime.UtcNow<until)await Task.Delay(10);}
                vm.CancelOpenCommand.Execute(null);await tasks[^1];
            }
            Assert.InRange(blocked.Entered,1,4);
        }
        finally{blocked.Gate.Set();await Task.WhenAll(tasks);}
    }
    private sealed class SnapshotSource:IOpenSnapshotSource
    {
        public int Calls;public string[] Selected=Array.Empty<string>();
        public OpenState Detect(Item item,LaunchConfig? launch)=>throw new Exception("must use immutable snapshot");
        public Task<IOpenStateDetector> ObserveAsync(IReadOnlyCollection<Item> selected,CancellationToken token)
        {Calls++;Selected=selected.Select(x=>x.Target).ToArray();return Task.FromResult<IOpenStateDetector>(new NoOpenStateDetection());}
    }
    [Fact] public async Task OneImmutableSnapshotForSelectedItemsOnly()
    {
        using var repo=Repo(3);var source=new SnapshotSource();var l=new Launcher();var vm=new MainViewModel(repo,l,new Ready(),source);
        vm.OpenWorkspaceCommand.Execute(null);vm.OpeningChoices[1].IsChecked=false;vm.ConfirmSelectionCommand.Execute(null);await vm.OpenTask;
        Assert.Equal(1,source.Calls);Assert.Equal(2,source.Selected.Length);Assert.DoesNotContain("C:\\doc1.txt",source.Selected);Assert.Equal(2,l.Count);
    }
    private sealed class Capturer:IOpenItemCapturer {public CaptureScan Scan()=>new(Array.Empty<CapturedItem>(),Array.Empty<string>());}
    [Fact] public async Task CaptureCreateInvalidNameAndDesignHaveVisibleFieldStateNotToast()
    {
        using var repo=Repo();var vm=new MainViewModel(repo,new Launcher(),capturer:new Capturer()){ScanMinVisible=TimeSpan.Zero};
        vm.CaptureNewCommand.Execute(null);await vm.CurrentScan!;vm.NewName=" ";vm.CaptureCreateCommand.Execute(null);Assert.NotEmpty(vm.CreateError);Assert.False(vm.ToastVisible);
        vm.NewName="valid";vm.NewWorkspaceColor="bad";vm.CaptureCreateCommand.Execute(null);Assert.NotEmpty(vm.CreateError);Assert.False(vm.ToastVisible);Assert.True(vm.IsCaptureNew);
    }
    private sealed class NeverSnapshot:IOpenSnapshotSource
    {
        public bool Cancelled;
        public OpenState Detect(Item item,LaunchConfig? launch)=>OpenState.Unknown;
        public async Task<IOpenStateDetector> ObserveAsync(IReadOnlyCollection<Item> selected,CancellationToken token)
        {
            try { await Task.Delay(10000,token); }
            catch(OperationCanceledException){Cancelled=true;}
            return new NoOpenStateDetection();
        }
    }
    [Fact] public async Task SnapshotTimeoutOnlyMeansUnknown_ReadyItemsStillLaunch()
    {
        using var repo=Repo(2);var snapshot=new NeverSnapshot();var l=new Launcher();
        var vm=new MainViewModel(repo,l,new Ready(),snapshot){OpenCheckTimeout=TimeSpan.FromMilliseconds(100)};
        vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);await vm.OpenTask;
        Assert.Equal(2,l.Count);Assert.All(vm.LastOpeningResults,r=>Assert.Equal(OpeningOutcome.RequestSent,r.Outcome));Assert.False(vm.OpenDecisionPending);
    }
}
