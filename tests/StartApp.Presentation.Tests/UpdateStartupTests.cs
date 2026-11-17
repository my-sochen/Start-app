using StartApp.Presentation;

namespace StartApp.Presentation.Tests;
public sealed class UpdateStartupTests
{
    private sealed class Source : IApprovedUpdateSource
    {
        public int Calls;
        public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) {Calls++;return Task.FromResult<ApprovedUpdate?>(null);}
    }
    [Fact] public async Task Startup_checks_once_but_manual_retry_still_available()
    {
        var s=new Source();using var vm=new UpdateViewModel(s,"0.1.0-proto.1",_=>{});
        await vm.CheckOnStartupOnceAsync();await vm.CheckOnStartupOnceAsync();Assert.Equal(1,s.Calls);
        await vm.CheckAsync();Assert.Equal(2,s.Calls);
    }
    [Fact] public async Task Inactive_startup_never_reads()
    {
        var s=new Source();using var vm=new UpdateViewModel(s,null,_=>{});
        await vm.CheckOnStartupOnceAsync();await vm.CheckOnStartupOnceAsync();Assert.Equal(0,s.Calls);
    }
    [Fact] public async Task Disposed_startup_never_reads()
    {
        var s=new Source();var vm=new UpdateViewModel(s,"0.1.0-proto.1",_=>{});vm.Dispose();
        await vm.CheckOnStartupOnceAsync();Assert.Equal(0,s.Calls);
    }
}
