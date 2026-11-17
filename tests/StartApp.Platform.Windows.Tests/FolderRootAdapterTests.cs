using StartApp.Core;
using StartApp.Platform.Windows;

public class FolderRootAdapterTests
{
    private sealed class Recorder : IProcessStarter
    {
        public List<StartRequest> Requests { get; } = new();
        public void Start(StartRequest request) => Requests.Add(request);
    }
    private sealed class FakeProbe(ProbeResult result) : IFileSystemProbe
    {
        public ProbeResult Probe(string path) => result;
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    public void Root_folder_passes_existing_directory_probe_and_launches_exact_saved_path(string target)
    {
        var item = new Item("i", ItemType.Folder, "root", target);
        Assert.Null(PreflightRules.CheckForm(item, null));
        var check = new FileSystemPreflight(new FakeProbe(new(ProbeKind.Directory))).Check(item, null);
        Assert.Equal(PreflightState.Ready, check.State);
        var recorder = new Recorder();
        Assert.Equal(LaunchStatus.Started, new WindowsLauncher(recorder).Launch(item, null).Status);
        var request = Assert.Single(recorder.Requests);
        Assert.Equal(target, request.FileName);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShell);
    }

    [Theory]
    [InlineData(ProbeKind.File, PreflightState.Unsupported)]
    [InlineData(ProbeKind.NotFound, PreflightState.Missing)]
    [InlineData(ProbeKind.Error, PreflightState.Unavailable)]
    public void Root_syntax_does_not_bypass_environment_checks(ProbeKind kind, PreflightState expected)
    {
        var item = new Item("i", ItemType.Folder, "root", @"C:\");
        Assert.Equal(expected, new FileSystemPreflight(new FakeProbe(new(kind))).Check(item, null).State);
    }

    [Fact]
    public void Root_folder_reparse_guard_stays_in_place()
    {
        var item = new Item("i", ItemType.Folder, "root", @"\\server\share");
        Assert.Equal(PreflightState.NeedsDecision,
            new FileSystemPreflight(new FakeProbe(new(ProbeKind.Directory, Reparse: true))).Check(item, null).State);
    }
}
