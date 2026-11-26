using StartApp.Core;
using StartApp.Platform.Windows;
using Xunit;

public class BrowserWindowLaunchTests
{
    private sealed class Starter : IProcessStarter { public List<StartRequest> Requests=new();public void Start(StartRequest request)=>Requests.Add(request); }
    [Fact] public void DedicatedBatchUsesOneSafeArgumentListAndNeverClaimsOwnership()
    {
        var starter=new Starter();var launcher=new WindowsLauncher(starter);
        var sites=new[]{new Item("c",ItemType.Link,"C","https://example.org/c?a=1&b=2"),new Item("a",ItemType.Link,"A","https://example.org/a#fragment")};
        var result=launcher.LaunchWindow(@"C:\Programs\chrome.exe",sites);
        var request=Assert.Single(starter.Requests);Assert.False(request.UseShell);Assert.Equal(new[]{"--new-window",sites[0].Target,sites[1].Target},request.Arguments);
        Assert.Equal(LaunchStatus.Started,result.Status);Assert.Equal(Ownership.Unknown,result.Ownership);Assert.Null(result.OpaqueHandle);
    }
    [Theory][InlineData("chrome.exe")][InlineData(@"C:\firefox.exe")][InlineData(@"C:\cmd.exe")]
    public void UnsupportedBrowserIsNeverStarted(string browser)
    {
        var starter=new Starter();var result=new WindowsLauncher(starter).LaunchWindow(browser,new[]{new Item("a",ItemType.Link,"A","https://example.org/")});
        Assert.Equal(LaunchStatus.Unavailable,result.Status);Assert.Empty(starter.Requests);
    }
    [Theory][InlineData("--user-data-dir=bad")][InlineData("file:///C:/a")][InlineData("https://example.org/ bad")]
    public void InvalidTargetsAreNotBrowserFlags(string target)
    {
        var starter=new Starter();var result=new WindowsLauncher(starter).LaunchWindow(@"C:\msedge.exe",new[]{new Item("a",ItemType.Link,"A",target)});
        Assert.Equal(LaunchStatus.Unavailable,result.Status);Assert.Empty(starter.Requests);
    }
    [Fact]public void OversizedBatchIsNotSplitIntoExtraWindows()
    {
        var starter=new Starter();var result=new WindowsLauncher(starter).LaunchWindow(@"C:\chrome.exe",new[]{new Item("a",ItemType.Link,"A","https://example.org/"+new string('a',30000))});
        Assert.Equal(LaunchStatus.Unavailable,result.Status);Assert.Empty(starter.Requests);
    }
}
