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
    private sealed class GatedSource : IApprovedUpdateSource
    {
        public int Calls;
        public readonly TaskCompletionSource<ApprovedUpdate?> Gate = new();
        public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) { Calls++; return Gate.Task; }
    }
    [Fact] public async Task Manual_in_flight_does_not_consume_startup_attempt()
    {
        var s = new GatedSource(); using var vm = new UpdateViewModel(s, "1.0.0", _ => {});
        var manual = vm.CheckAsync(); await vm.CheckOnStartupOnceAsync(); Assert.Equal(1, s.Calls);
        s.Gate.SetResult(null); await manual;
        await vm.CheckOnStartupOnceAsync(); await vm.CheckOnStartupOnceAsync(); Assert.Equal(2, s.Calls);
    }
    [Theory][InlineData(null, "גרסה מותקנת: זהות הבנייה טרם אומתה")][InlineData("1.0.0", "גרסה מותקנת: \u202A1.0.0\u202C")]
    public void Installed_version_display_is_truthful(string? version, string expected)
    {
        using var vm = new UpdateViewModel(new Source(), version, _ => {});
        Assert.Equal(expected, vm.InstalledVersionDisplay);
    }
    private sealed class FixedSource(ApprovedUpdate? value) : IApprovedUpdateSource
    {
        public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }
    [Fact] public async Task Startup_newer_notifies_once_manual_checks_do_not_notify()
    {
        using var vm = new UpdateViewModel(new FixedSource(new("1.0.1", new string('a',40), new string('b',64))), "1.0.0", _ => {});
        var notices = 0; vm.StartupUpdateAvailable += () => notices++;
        await vm.CheckAsync(); Assert.Equal(0, notices);
        await vm.CheckOnStartupOnceAsync(); await vm.CheckOnStartupOnceAsync(); Assert.Equal(1, notices);
        await vm.CheckAsync(); Assert.Equal(1, notices);
    }
    [Theory][InlineData(null)][InlineData("1.0.0")][InlineData("0.9.0")][InlineData("invalid")]
    public async Task Startup_without_valid_newer_update_does_not_notify(string? version)
    {
        using var vm = new UpdateViewModel(new FixedSource(version is null ? null : new(version, new string('a',40), new string('b',64))), "1.0.0", _ => {});
        var notices = 0; vm.StartupUpdateAvailable += () => notices++;
        await vm.CheckOnStartupOnceAsync(); Assert.Equal(0, notices);
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
