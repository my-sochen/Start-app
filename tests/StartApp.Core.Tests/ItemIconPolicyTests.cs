using StartApp.Core;
using Xunit;
public class ItemIconPolicyTests
{
    [Theory]
    [InlineData("https://example.com/private/sensitive?token=123#secret", "https://example.com/favicon.ico")]
    [InlineData("http://example.com:8080/a", "http://example.com:8080/favicon.ico")]
    public void IconRequestDropsFullPageAndAllSensitiveRemainder(string input,string expected) => Assert.Equal(expected,ItemIconPolicy.WebIconAddress(input));
    [Theory]
    [InlineData("file:///C:/secret")][InlineData("javascript:foo")][InlineData("example.com")][InlineData("https://user:pass@example.com/x")][InlineData("https://example.com/has space")][InlineData("https://%65xample.com/")]
    public void AmbiguousOrUnsupportedNeverProducesRequest(string target)=>Assert.Null(ItemIconPolicy.WebIconAddress(target));
}
