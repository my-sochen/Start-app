using StartApp.Core;
using StartApp.Platform.Windows;

public class WindowsFileRevealerTests
{
    private sealed class StubProbe(ProbeResult result) : IFileSystemProbe
    {
        public int Calls;
        public ProbeResult Probe(string path) { Calls++; return result; }
    }
    [Theory]
    [InlineData(@"C:\Users\Example User\מסמך.txt")]
    [InlineData(@"C:\Tools\editor.exe")]
    [InlineData(@"\\server\share\folder\report.txt")]
    [InlineData(@"C:\a,b & c\report.lnk")]
    public void Selects_exact_saved_entry_without_launching_it(string path)
    {
        var probe=new StubProbe(new(ProbeKind.File));var selected=new List<string>();
        var result=new WindowsFileRevealer(probe,selected.Add).Reveal(path);
        Assert.Equal(RevealStatus.Revealed,result.Status);Assert.Equal(new[]{path},selected);Assert.Equal(1,probe.Calls);
    }
    [Theory]
    [InlineData("")][InlineData("editor.exe")][InlineData("https://example.com/")]
    [InlineData(@"shell:AppsFolder")][InlineData(@"C:\a.txt:stream")]
    [InlineData(@"\\?\C:\a.txt")][InlineData(@"\\.\C:\a.txt")]
    [InlineData(@"C:\folder\..\a.txt")][InlineData(@"C:\folder\a.txt ")]
    [InlineData(@"C:\folder\a*.txt")][InlineData("C:\\folder\\a\n.txt")]
    public void Invalid_or_ambiguous_paths_never_reach_probe_or_shell(string path)
    {
        var probe=new StubProbe(new(ProbeKind.File));var selected=new List<string>();
        Assert.Equal(RevealStatus.Invalid,new WindowsFileRevealer(probe,selected.Add).Reveal(path).Status);
        Assert.Equal(0,probe.Calls);Assert.Empty(selected);
    }
    [Theory]
    [InlineData(ProbeKind.NotFound,RevealStatus.Missing)]
    [InlineData(ProbeKind.Error,RevealStatus.Unavailable)]
    [InlineData(ProbeKind.Directory,RevealStatus.Invalid)]
    public void Failed_probe_is_honest_and_never_opens_another_location(ProbeKind kind,RevealStatus expected)
    {
        var selected=new List<string>();var r=new WindowsFileRevealer(new StubProbe(new(kind,"reason")),selected.Add).Reveal(@"C:\a.txt");
        Assert.Equal(expected,r.Status);Assert.Empty(selected);
    }
    [Fact] public void Select_failure_returns_unavailable_without_retry()
    {
        var count=0;var r=new WindowsFileRevealer(new StubProbe(new(ProbeKind.File)),_=>{count++;throw new System.Runtime.InteropServices.COMException("fixture");}).Reveal(@"C:\a.txt");
        Assert.Equal(RevealStatus.Unavailable,r.Status);Assert.Equal(1,count);Assert.Contains("COMException",r.Detail);
    }
    [Fact] public void Reparse_entry_is_selected_as_an_entry_not_followed_or_run()
    {
        string? selected=null;var r=new WindowsFileRevealer(new StubProbe(new(ProbeKind.File,Reparse:true)),p=>selected=p).Reveal(@"C:\link.lnk");
        Assert.Equal(RevealStatus.Revealed,r.Status);Assert.Equal(@"C:\link.lnk",selected);
    }
}
