using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using StartApp.Presentation;
namespace StartApp.Presentation.Tests;

public sealed class BetaNoticeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private const string Version = "0.2.0-beta.1";
    private static readonly Uri Endpoint = new("https://my-sochen.github.io/Start-app/synthetic-beta-notice.json");
    private static JsonObject Doc(long revision = 1) => new()
    {
        ["schema"] = 1, ["revision"] = revision, ["channel"] = "beta-notice", ["generated_utc"] = "2026-11-17T23:00:00Z", ["expires_utc"] = "2026-11-18T01:00:00Z",
        ["releases"] = new JsonArray(new JsonObject {
            ["version"] = Version, ["tag"] = "v" + Version, ["commit"] = new string('a',40), ["sha256"] = new string('b',64),
            ["release_id"] = 1, ["asset_id"] = 2, ["size"] = 10, ["asset"] = "StartApp-win-x64.zip", ["layout"] = "portable-v1", ["approval_record_id"] = "synthetic-not-approval"
        })
    };
    private static byte[] Bytes(JsonObject doc) => Encoding.UTF8.GetBytes(doc.ToJsonString());
    private static ValidatedCatalog Parse(JsonObject doc) => ApprovedCatalogContract.Parse(Bytes(doc), Now, new(65536,128,TimeSpan.FromDays(1),TimeSpan.FromDays(1)), channel:"beta-notice");
    [Theory]
    [InlineData("1.0.0", "1.0.0-beta.99")][InlineData("1.0.0-beta.1","1.0.0-proto.99")]
    [InlineData("1.0.0-beta.10","1.0.0-beta.9")][InlineData("1.0.1-proto.1","1.0.0-beta.99")]
    public void Numeric_and_channel_precedence(string newer,string older) => Assert.True(UpdateVersion.Parse(newer)!.CompareTo(UpdateVersion.Parse(older)) > 0);
    [Fact] public void Beta_catalog_is_distinct_and_does_not_change_prototype_selection()
    {
        Assert.Equal(Version, Parse(Doc()).SelectNewer("0.1.0-proto.1")?.Version);
        Assert.Null(Parse(Doc()).SelectNewer("0.1.0")); Assert.Null(Parse(Doc()).SelectNewer("0.2.0-beta.1"));
        Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Doc()),Now,new(65536,128,TimeSpan.FromDays(1),TimeSpan.FromDays(1))));
    }
    [Theory][InlineData("schema","2")][InlineData("revision","0")][InlineData("channel","\"prototype\"")]
    [InlineData("expires_utc","\"2026-11-18T00:00:00Z\"")][InlineData("generated_utc","\"2026-11-18T00:01:00Z\"")]
    [InlineData("expires_utc","\"2026-11-20T00:00:00Z\"")]
    public void Envelope_rejected(string key,string raw) { var d=Doc();d[key]=JsonNode.Parse(raw); Assert.Throws<InvalidDataException>(()=>Parse(d)); }
    [Theory][InlineData("version","\"0.2.0-proto.1\"")][InlineData("tag","\"v0.2.0-beta.2\"")]
    [InlineData("commit","\"short\"")][InlineData("asset_id","0")][InlineData("size","1.0")]
    [InlineData("layout","\"other\"")][InlineData("approved","true")]
    public void Invalid_or_self_asserted_record_rejected(string key,string raw) {var d=Doc();d["releases"]![0]![key]=JsonNode.Parse(raw);Assert.Throws<InvalidDataException>(()=>Parse(d));}
    [Fact] public void Duplicates_and_rollback_rejected()
    {
        var d=Doc();d["releases"]!.AsArray().Add(d["releases"]![0]!.DeepClone());Assert.Throws<InvalidDataException>(()=>Parse(d));
        Assert.Throws<InvalidDataException>(()=>ApprovedCatalogContract.Parse(Bytes(Doc()),Now,new(65536,128,TimeSpan.FromDays(1),TimeSpan.FromDays(1)),2,"beta-notice"));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;var response=reply(request);response.RequestMessage ??= request;return Task.FromResult(response);}
    }
    private static HttpResponseMessage Response(byte[] bytes,string type="application/json")
    {var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};r.Content.Headers.ContentType=new(type);return r;}
    [Fact] public async Task Bounded_source_and_real_viewmodel_startup_notify_beta_then_revalidate_click()
    {
        var handler=new Handler(r=>{Assert.True(r.Headers.CacheControl!.NoStore);return Response(Bytes(Doc()));});
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",handler,()=>Now);
        var opened=new List<Uri>(); using var vm=new UpdateViewModel(source,"0.1.0-proto.1",opened.Add,revalidateBeforeDownload:true);
        int notices=0;vm.StartupUpdateAvailable+=()=>notices++;
        await vm.CheckOnStartupOnceAsync();await vm.CheckOnStartupOnceAsync();Assert.Equal(1,notices);Assert.Equal(1,handler.Calls);
        Assert.True(vm.UpdateAvailable);Assert.Contains("בטא",vm.Status);Assert.Contains("אב טיפוס",vm.DistributionDisplay);Assert.Empty(opened);
        vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.Equal(2,handler.Calls);Assert.Single(opened);Assert.Equal(UpdateViewModel.OfficialWebsite,opened[0].AbsoluteUri);
    }
    [Fact] public async Task Withdrawn_or_changed_offer_never_opens_stale_notice()
    {
        var doc=Doc();var handler=new Handler(_=>Response(Bytes(doc)));using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",handler,()=>Now);
        var opened=0;using var vm=new UpdateViewModel(source,"0.1.0-proto.1",_=>opened++,revalidateBeforeDownload:true);
        await vm.CheckAsync();doc["revision"]=2;doc["releases"]![0]!["asset_id"]=3;
        vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.Equal(0,opened);Assert.Contains("השתנו",vm.Status);
        doc["revision"]=3;doc["releases"]=new JsonArray();vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.Equal(0,opened);Assert.False(vm.UpdateAvailable);
    }
    [Theory][InlineData(201,"application/json",100)][InlineData(302,"application/json",100)][InlineData(200,"text/html",100)][InlineData(200,"application/json",65537)]
    public async Task Redirect_contenttype_oversize_fail_closed(int status,string type,int size)
    {
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",new Handler(_=>{var r=Response(new byte[size],type);r.StatusCode=(HttpStatusCode)status;return r;}),()=>Now);
        using var vm=new UpdateViewModel(source,"0.1.0-proto.1",_=>throw new Exception());await vm.CheckAsync();Assert.False(vm.UpdateAvailable);Assert.False(vm.IsBusy);
    }
    [Theory][InlineData("gzip",null)][InlineData(null,"utf-16")]
    public async Task Encoded_or_non_utf8_response_rejected(string? encoding,string? charset)
    {
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",new Handler(_=>{var r=Response(Bytes(Doc())); if(encoding is not null)r.Content.Headers.ContentEncoding.Add(encoding);r.Content.Headers.ContentType!.CharSet=charset;return r;}),()=>Now);
        await Assert.ThrowsAsync<InvalidDataException>(()=>source.ReadAsync(CancellationToken.None));
    }
    [Theory][InlineData("http://my-sochen.github.io/Start-app/notice.json")][InlineData("https://other.invalid/Start-app/notice.json")]
    [InlineData("https://my-sochen.github.io/other/notice.json")][InlineData("https://my-sochen.github.io/Start-app/notice.json?x=1")]
    public void Wrong_endpoint_rejected(string url) => Assert.Throws<ArgumentException>(()=>new BetaNoticeSource(new(url),"0.1.0-proto.1",new Handler(_=>throw new Exception())));
    [Fact] public async Task Inactive_stamped_build_keeps_label_but_never_reads_or_checks()
    {
        using var vm=new UpdateViewModel(new InactiveUpdateSource(),"0.1.0-proto.1",_=>throw new Exception());
        await vm.CheckOnStartupOnceAsync();Assert.False(vm.CheckCommand.CanExecute(null));Assert.False(vm.UpdateAvailable);Assert.Contains("אב טיפוס",vm.DistributionDisplay);
    }
    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) {length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,System.Net.TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
    [Fact] public async Task Chunked_body_cannot_bypass_byte_cap()
    {
        var handler=new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamingContent(new byte[65537])};response.Content.Headers.ContentType=new("application/json");return response;});
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",handler,()=>Now);
        await Assert.ThrowsAsync<InvalidDataException>(()=>source.ReadAsync(CancellationToken.None));
    }
    [Fact] public async Task Expired_or_rollback_catalog_clears_previous_offer()
    {
        var now=Now;var doc=Doc(2);var handler=new Handler(_=>Response(Bytes(doc)));
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",handler,()=>now);
        using var vm=new UpdateViewModel(source,"0.1.0-proto.1",_=>throw new Exception());
        await vm.CheckAsync();Assert.True(vm.UpdateAvailable);doc["revision"]=1;
        await vm.CheckAsync();Assert.False(vm.UpdateAvailable);doc["revision"]=3;now=Now.AddDays(1);
        await vm.CheckAsync();Assert.False(vm.UpdateAvailable);
    }
    [Fact] public async Task Cancellation_is_not_reported_as_a_current_version()
    {
        using var source=new BetaNoticeSource(Endpoint,"0.1.0-proto.1",new Handler(_=>throw new OperationCanceledException()),()=>Now);
        using var vm=new UpdateViewModel(source,"0.1.0-proto.1",_=>throw new Exception());await vm.CheckAsync();
        Assert.False(vm.UpdateAvailable);Assert.Contains("לא הסתיימה",vm.Status);Assert.False(vm.IsBusy);
    }

}
