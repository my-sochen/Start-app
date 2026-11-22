using StartApp.Presentation;

namespace StartApp.Presentation.Tests;

public sealed class UpdateTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private sealed class Source : IApprovedUpdateSource
    {
        public int Calls;
        public Func<CancellationToken, Task<ApprovedUpdate?>> Read = _ => Task.FromResult<ApprovedUpdate?>(null);
        public Task<ApprovedUpdate?> ReadAsync(CancellationToken token) { Calls++; return Read(token); }
    }
    private static Source Candidate(string version) => new() { Read = _ => Task.FromResult<ApprovedUpdate?>(new(version, Commit, Hash)) };
    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("0.2.0-proto.1", "0.1.0-proto.99")]
    [InlineData("1.0.0", "1.0.0-proto.99")]
    [InlineData("1.0.0-proto.10", "1.0.0-proto.9")]
    public void Numeric_version_order_includes_patches_and_version_lines(string newer, string older) =>
        Assert.True(UpdateVersion.Parse(newer)!.CompareTo(UpdateVersion.Parse(older)) > 0);
    [Theory]
    [InlineData("01.0.0")][InlineData("1.0")][InlineData("v1.0.0")][InlineData("1.0.0-beta.01")]
    [InlineData("1.0.0-proto.01")][InlineData("999999999999.0.0")][InlineData("1.0.0\n")]
    public void Invalid_versions_fail_closed(string value) => Assert.Null(UpdateVersion.Parse(value));
    [Fact] public async Task Unstamped_preview_is_disabled_and_never_reads()
    {
        var source = Candidate("99.0.0"); using var vm = new UpdateViewModel(source, null, _ => throw new Exception());
        Assert.False(vm.CheckCommand.CanExecute(null)); await vm.CheckAsync(); Assert.Equal(0, source.Calls);
        Assert.False(vm.UpdateAvailable); Assert.False(vm.DownloadCommand.CanExecute(null));
    }
    [Fact] public async Task Patch_update_only_opens_fixed_official_website_on_explicit_click()
    {
        Uri? opened = null; using var vm = new UpdateViewModel(Candidate("1.0.1"), "1.0.0", uri => opened = uri);
        await vm.CheckAsync(); Assert.True(vm.UpdateAvailable); Assert.Null(opened);
        vm.DownloadCommand.Execute(null); Assert.Equal(UpdateViewModel.OfficialWebsite, opened!.AbsoluteUri);
    }
    [Theory][InlineData("1.0.0")][InlineData("0.9.0")][InlineData("2.0.0-proto.1")]
    public async Task Equal_older_and_experimental_versions_are_not_offered_to_stable(string candidate)
    {
        using var vm = new UpdateViewModel(Candidate(candidate), "1.0.0", _ => throw new Exception());
        await vm.CheckAsync(); Assert.False(vm.UpdateAvailable);
    }
    [Fact] public async Task Stable_successor_is_offered_to_prototype()
    {
        using var vm = new UpdateViewModel(Candidate("1.0.0"), "1.0.0-proto.1", _ => { });
        await vm.CheckAsync(); Assert.True(vm.UpdateAvailable);
    }
    [Fact] public async Task No_candidate_does_not_claim_installed_version_is_latest()
    {
        using var vm = new UpdateViewModel(new Source(), "1.0.0", _ => { });
        await vm.CheckAsync(); Assert.False(vm.UpdateAvailable); Assert.Contains("אין כרגע גרסה מאושרת", vm.Status);
    }
    [Fact] public async Task Missing_provenance_is_rejected()
    {
        var source = new Source { Read = _ => Task.FromResult<ApprovedUpdate?>(new("1.0.1", "short", Hash)) };
        using var vm = new UpdateViewModel(source, "1.0.0", _ => { });
        await vm.CheckAsync(); Assert.False(vm.UpdateAvailable); Assert.Contains("לא ניתן לאמת", vm.Status);
    }
    [Fact] public async Task Duplicate_checks_are_serialized_and_late_result_after_close_is_discarded()
    {
        var gate = new TaskCompletionSource<ApprovedUpdate?>(); var source = new Source { Read = _ => gate.Task };
        using var vm = new UpdateViewModel(source, "1.0.0", _ => throw new Exception());
        var check = vm.CheckAsync(); Assert.True(vm.IsBusy); Assert.False(vm.CheckCommand.CanExecute(null));
        await vm.CheckAsync(); Assert.Equal(1, source.Calls); vm.Dispose();
        await check; gate.SetResult(new("1.0.1", Commit, Hash)); Assert.False(vm.UpdateAvailable);
    }
    [Fact] public async Task Timeout_is_bounded_even_when_source_ignores_cancellation()
    {
        var source = new Source { Read = _ => new TaskCompletionSource<ApprovedUpdate?>().Task };
        using var vm = new UpdateViewModel(source, "1.0.0", _ => { }, TimeSpan.FromMilliseconds(25));
        await vm.CheckAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsBusy); Assert.False(vm.UpdateAvailable); Assert.True(vm.CheckCommand.CanExecute(null));
    }
    [Fact] public async Task Failed_recheck_clears_stale_download_action()
    {
        var source = Candidate("1.0.1"); using var vm = new UpdateViewModel(source, "1.0.0", _ => throw new Exception());
        await vm.CheckAsync(); Assert.True(vm.UpdateAvailable);
        source.Read = _ => throw new HttpRequestException(); await vm.CheckAsync();
        Assert.False(vm.UpdateAvailable); Assert.False(vm.DownloadCommand.CanExecute(null));
    }
    [Fact] public async Task Browser_failure_has_honest_manual_route()
    {
        using var vm = new UpdateViewModel(Candidate("1.0.1"), "1.0.0", _ => throw new InvalidOperationException());
        await vm.CheckAsync(); vm.DownloadCommand.Execute(null); Assert.Contains(UpdateViewModel.OfficialWebsite, vm.Status);
    }
    [Fact] public async Task Adapter_specific_failure_is_honest_and_does_not_fault_command()
    {
        var source = new Source { Read = _ => throw new NotSupportedException("synthetic adapter failure") };
        using var vm = new UpdateViewModel(source, "1.0.0", _ => throw new Exception());
        vm.CheckCommand.Execute(null); await vm.CurrentCheck;
        Assert.False(vm.IsBusy); Assert.False(vm.UpdateAvailable);
        Assert.DoesNotContain("בודק", vm.Status); Assert.Contains("לא ניתן לבדוק", vm.Status);
        Assert.True(vm.CheckCommand.CanExecute(null));
    }

}

public sealed class UpdateSettingsDisplayTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private sealed class Source(Func<CancellationToken, Task<ApprovedUpdate?>> read) : IApprovedUpdateSource
    { public Task<ApprovedUpdate?> ReadAsync(CancellationToken token) => read(token); }
    private static Source Candidate(string version) => new(_ => Task.FromResult<ApprovedUpdate?>(new(version, Commit, Hash)));

    [Fact] public async Task Settings_values_track_completed_checks()
    {
        var now = new DateTime(2026, 11, 22, 9, 12, 0);
        using var vm = new UpdateViewModel(Candidate("1.0.1"), "1.0.0", _ => { }, clock: () => now);
        Assert.Equal("טרם נבדק בהפעלה זו", vm.LastCheckedDisplay);
        Assert.Equal("1.0.0", vm.VersionValueDisplay);
        Assert.Equal("", vm.OfferedVersionDisplay);
        await vm.CheckAsync();
        Assert.True(vm.UpdateAvailable);
        Assert.Equal("נבדק לאחרונה: היום, 09:12", vm.LastCheckedDisplay);
        Assert.Equal("קיימת גרסה חדשה: 1.0.1", vm.OfferedVersionDisplay);
    }

    [Fact] public async Task Beta_values_carry_the_beta_tag_and_failed_checks_keep_the_clock()
    {
        var now = new DateTime(2026, 11, 22, 9, 12, 0);
        using var vm = new UpdateViewModel(Candidate("0.9.1-beta.1"), "0.9.0-beta.2", _ => { }, clock: () => now);
        Assert.Equal("0.9.0-beta.2 (בטא)", vm.VersionValueDisplay);
        await vm.CheckAsync();
        Assert.Equal("קיימת גרסה חדשה: 0.9.1-beta.1 (בטא)", vm.OfferedVersionDisplay);
        using var failing = new UpdateViewModel(new Source(_ => throw new System.Net.Http.HttpRequestException("synthetic")), "1.0.0", _ => { }, clock: () => now);
        await failing.CheckAsync();
        Assert.Equal("טרם נבדק בהפעלה זו", failing.LastCheckedDisplay);
    }
}
