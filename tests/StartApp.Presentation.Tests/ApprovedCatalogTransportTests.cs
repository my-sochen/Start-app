using System.Net;
using System.Net.Http.Headers;
using System.Text;
using StartApp.Presentation;

namespace StartApp.Presentation.Tests;

public sealed class ApprovedCatalogTransportTests
{
    // Reserved invalid domain: fake handler only, never a real service/request.
    private static readonly Uri Endpoint = new("https://catalog.invalid/approved.json");
    private static readonly CatalogPolicy Policy = new(65536,128,TimeSpan.FromDays(1),TimeSpan.FromDays(1));
    private static readonly DateTimeOffset Clock = new(2026,11,17,12,0,0,TimeSpan.Zero);
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond = (r,_)=>Task.FromResult(Response(r));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {Calls++; return Respond(request,token);}
    }
    private static string Body => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"update-fixtures","catalog.synthetic.example.json"));
    private static HttpResponseMessage Response(HttpRequestMessage request) => new(HttpStatusCode.OK) {RequestMessage=request,Content=new StringContent(Body,Encoding.UTF8,"application/json")};
    private static ApprovedCatalogTransport Transport(Handler h,CatalogPolicy? p=null,TimeSpan? deadline=null) => new(Endpoint,p??Policy,()=>Clock,deadline??TimeSpan.FromSeconds(10),h);
    [Fact] public async Task Reads_fake_transport_only_and_requests_no_store()
    {
        var h=new Handler {Respond=(r,_)=>{Assert.Equal(Endpoint,r.RequestUri);Assert.True(r.Headers.CacheControl!.NoStore);Assert.True(r.Headers.CacheControl.NoCache);Assert.Null(r.Headers.Authorization);return Task.FromResult(Response(r));}};
        using var t=Transport(h);Assert.Equal(1,(await t.ReadAsync(default)).Revision);Assert.Equal(1,h.Calls);
    }
    [Theory][InlineData(301)][InlineData(302)][InlineData(307)][InlineData(308)]
    public async Task Redirects_never_followed_by_reader(int code)
    {
        var h=new Handler {Respond=(r,_)=>{var s=Response(r);s.StatusCode=(HttpStatusCode)code;s.Headers.Location=new Uri("https://other.invalid/");return Task.FromResult(s);}};
        using var t=Transport(h);await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));Assert.Equal(1,h.Calls);
    }
    [Fact] public async Task Wrong_response_endpoint_rejected()
    {
        var h=new Handler {Respond=(_,_)=>Task.FromResult(Response(new(HttpMethod.Get,"https://other.invalid/")))};
        using var t=Transport(h);await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    [Theory][InlineData("text/html")][InlineData("text/plain")]
    public async Task Wrong_media_type_rejected(string type)
    {
        var h=new Handler {Respond=(r,_)=>{var s=Response(r);s.Content.Headers.ContentType=new MediaTypeHeaderValue(type);return Task.FromResult(s);}};
        using var t=Transport(h);await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Non_UTF8_charset_rejected()
    {
        var h=new Handler {Respond=(r,_)=>{var s=Response(r);s.Content.Headers.ContentType!.CharSet="utf-16";return Task.FromResult(s);}};
        using var t=Transport(h);await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Encoded_body_rejected()
    {
        var h=new Handler {Respond=(r,_)=>{var s=Response(r);s.Content.Headers.ContentEncoding.Add("gzip");return Task.FromResult(s);}};
        using var t=Transport(h);await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Advertised_oversize_rejected()
    {
        using var t=Transport(new Handler(),Policy with {MaximumBytes=10});await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _body=Encoding.UTF8.GetBytes(Body);
        public UnknownLengthContent() {Headers.ContentType=new MediaTypeHeaderValue("application/json");}
        protected override bool TryComputeLength(out long length) {length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(_body).AsTask();
    }
    [Fact] public async Task Body_limit_with_missing_ContentLength()
    {
        var h=new Handler {Respond=(r,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {RequestMessage=r,Content=new UnknownLengthContent()})};
        using var t=Transport(h,Policy with {MaximumBytes=10});await Assert.ThrowsAsync<InvalidDataException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Offline_error_propagates_for_honest_VM_error()
    {
        var h=new Handler {Respond=(_,_)=>throw new HttpRequestException("synthetic offline")};using var t=Transport(h);await Assert.ThrowsAsync<HttpRequestException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Bounded_header_wait_cancels()
    {
        var h=new Handler {Respond=async (_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception();}};
        using var t=Transport(h,deadline:TimeSpan.FromMilliseconds(25));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>t.ReadAsync(default));
    }
    [Fact] public async Task Caller_cancellation_propagates()
    {
        var h=new Handler {Respond=async (_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception();}};
        using var t=Transport(h);using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>t.ReadAsync(cts.Token));
    }
    [Theory][InlineData("http://catalog.invalid/x")][InlineData("https://user:pass@catalog.invalid/x")][InlineData("https://catalog.invalid/x#fragment")][InlineData("https://catalog.invalid:8443/x")]
    public void Unsafe_endpoints_rejected_without_requests(string endpoint)
    {var h=new Handler();Assert.Throws<ArgumentException>(()=>new ApprovedCatalogTransport(new(endpoint),Policy,()=>Clock,TimeSpan.FromSeconds(10),h));Assert.Equal(0,h.Calls);h.Dispose();}
}
