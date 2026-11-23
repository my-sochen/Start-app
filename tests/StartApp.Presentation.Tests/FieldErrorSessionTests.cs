using StartApp.Presentation;

public class FieldErrorSessionTests
{
    [Fact] public void New_session_has_no_message_for_any_form()
    {
        var s = new FieldErrorSession();
        foreach (var host in new[] { "add", "create", "details", "item", "address", "design", "category" }) Assert.Equal("", s.ForHost(host));
    }
    [Theory] [InlineData("add")] [InlineData("create")] [InlineData("details")] [InlineData("item")]
    [InlineData("address")] [InlineData("design")] [InlineData("category")]
    public void Error_is_visible_only_to_its_exact_host_without_trimming_or_case_folding(string host)
    {
        var s = new FieldErrorSession { Host = host, Message = " error " };
        Assert.Equal(" error ", s.ForHost(host)); Assert.Equal("", s.ForHost(host.ToUpperInvariant())); Assert.Equal("", s.ForHost(host + " "));
        foreach (var other in new[] { "add", "create", "details", "item", "address", "design", "category" }.Where(x => x != host)) Assert.Equal("", s.ForHost(other));
    }
    [Fact] public void Retarget_and_clear_do_not_retain_previous_form_message()
    {
        var s = new FieldErrorSession { Host = "item", Message = "item error" };
        s.Host = "address"; s.Message = "address error";
        Assert.Equal("", s.ForHost("item")); Assert.Equal("address error", s.ForHost("address"));
        s.Host = ""; s.Message = ""; Assert.Equal("", s.ForHost("address"));
    }
    [Fact] public void Separate_sessions_do_not_share_message_or_host()
    {
        var a = new FieldErrorSession { Host = "item", Message = "error" }; var b = new FieldErrorSession();
        Assert.Equal("", b.ForHost("item")); a.Host = "address"; Assert.Equal("", b.Host); Assert.Equal("", b.Message);
    }
}
