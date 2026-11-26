using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;

public class WindowSessionSubscriptionsTests
{
    private sealed class MemoryStore : IStoreRepository
    {
        private readonly Store _store=new();
        public Store Load()=>_store;
        public Store Mutate(Action<Catalog> change){change(new Catalog(_store));return _store;}
        public void Dispose(){}
    }
    private static MainViewModel Vm()=>new(new MemoryStore(),new NotImplementedLauncher());
    [Fact] public void Replacing_same_session_does_not_duplicate_handlers()
    {
        var calls=0;var changes=0;var vm=Vm();
        using var owner=new WindowSessionSubscriptions(()=>{},_=>{},()=>{},()=>calls++,()=>{},(_,_)=>changes++);
        owner.Replace(vm);owner.Replace(vm);vm.PickImageCommand.Execute(null);vm.NewName="one";
        Assert.Equal(1,calls);Assert.True(changes>0);Assert.Same(vm,owner.Current);
    }
    [Fact] public void Replacement_detaches_old_session_before_new_one()
    {
        var calls=0;var changes=0;var a=Vm();var b=Vm();
        using var owner=new WindowSessionSubscriptions(()=>{},_=>{},()=>{},()=>calls++,()=>{},(_,_)=>changes++);
        owner.Replace(a);owner.Replace(b);a.PickImageCommand.Execute(null);a.NewName="old";
        Assert.Equal(0,calls);Assert.Equal(0,changes);
        b.PickImageCommand.Execute(null);b.NewName="new";Assert.Equal(1,calls);Assert.True(changes>0);
    }
    [Fact] public void Null_and_repeated_dispose_detach_every_lifetime()
    {
        var calls=0;var vm=Vm();
        var owner=new WindowSessionSubscriptions(()=>{},_=>{},()=>{},()=>calls++,()=>calls++,(_,_)=>calls++);
        owner.Replace(vm);owner.Replace(null);owner.Dispose();owner.Dispose();
        vm.PickImageCommand.Execute(null);vm.PickCreateImageCommand.Execute(null);vm.NewName="detached";
        Assert.Equal(0,calls);Assert.Null(owner.Current);
        owner.Replace(vm);vm.PickImageCommand.Execute(null);Assert.Equal(1,calls);owner.Dispose();
    }
}
