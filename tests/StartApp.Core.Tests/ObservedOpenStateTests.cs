using StartApp.Core;
using Xunit;
public class ObservedOpenStateTests
{
    [Theory]
    [InlineData("HTTPS://EXAMPLE.com/a?x=1#p","https://example.COM/a?x=1#p")]
    [InlineData("https://example.com","https://example.com/")]
    [InlineData("https://example.com?x=1","https://example.com/?x=1")]
    [InlineData("https://example.com#p","https://example.com/#p")]
    public void OnlyApprovedEquivalencesMatch(string a,string b)=>Assert.True(ExactObservedUrl.Same(a,b));
    [Theory]
    [InlineData("http://example.com/","https://example.com/")]
    [InlineData("https://example.com/a","https://example.com/A")]
    [InlineData("https://example.com/a","https://example.com/a/")]
    [InlineData("https://example.com?a=1&b=2","https://example.com?b=2&a=1")]
    [InlineData("https://example.com#one","https://example.com#two")]
    [InlineData("https://example.com","https://example.com:443")]
    [InlineData("https://example.com:0443","https://example.com:443")]
    [InlineData("https://example.com/a%2Fb","https://example.com/a%2fb")]
    [InlineData("https://example.com/%61","https://example.com/a")]
    [InlineData("https://example.com","https://www.example.com/")]
    [InlineData("https://example.com/a/../b","https://example.com/b")]
    [InlineData("https://example.com/","https://example.com/?")]
    public void RemainderNeverNormalized(string a,string b)=>Assert.False(ExactObservedUrl.Same(a,b));
    [Theory][InlineData("example.com")][InlineData("https://user@example.com/")][InlineData("https://example.com/has space")][InlineData("https://%65xample.com/")][InlineData("https://example.com/\n")][InlineData("file:///C:/a")][InlineData("https://[::1]/")]
    public void IneligibleInputsNeverConfirm(string target)=>Assert.Null(ExactObservedUrl.Key(target));
    [Fact] public void ObservedUrlIgnoresBrowserAccountChoiceButDoesNotChangeLaunch()
    {
        var observed=new ObservedOpenSnapshot(new[]{new CapturedItem(ItemType.Link,"page","https://example.com/", "C:\\browser-one.exe")});
        var launch=new LaunchConfig(Browser:"C:\\browser-two.exe");
        Assert.Equal(OpenState.Open,observed.Detect(new("i",ItemType.Link,"page","https://EXAMPLE.com"),launch));Assert.Equal("C:\\browser-two.exe",launch.Browser);
        Assert.Equal(OpenState.Unknown,observed.Detect(new("i",ItemType.Link,"page","https://example.com/another"),launch));
    }
    [Fact] public void OnlyExactObservedProgramsFoldersConfirm_NoNotOpenOrGenericDocumentGuess()
    {
        var snapshot=new ObservedOpenSnapshot(new[]{new CapturedItem(ItemType.Program,"app","C:\\app.exe"),new CapturedItem(ItemType.Folder,"f","C:\\folder"),new CapturedItem(ItemType.File,"doc","C:\\doc.txt")});
        Assert.Equal(OpenState.Open,snapshot.Detect(new("i",ItemType.Program,"app","C:\\app.exe"),null));
        Assert.Equal(OpenState.Open,snapshot.Detect(new("i",ItemType.Folder,"f","C:\\folder"),null));
        Assert.Equal(OpenState.Unknown,snapshot.Detect(new("i",ItemType.File,"doc","C:\\doc.txt"),null));
        Assert.Equal(OpenState.Unknown,snapshot.Detect(new("i",ItemType.Program,"app","C:\\APP.exe"),null));
    }
}
