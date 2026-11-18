using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StartApp.Presentation;
namespace StartApp.Presentation.Tests;
public sealed class MainBetaNoticeTests
{
    private static readonly DateTimeOffset Now=new(2026,11,18,9,0,0,TimeSpan.Zero);
    private const string Commit="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Version="0.2.0-beta.1";
    private static object Candidate()=>new {version=Version,tag="v"+Version,commit=Commit,release_id=1,asset_id=2,asset="StartApp-win-x64.zip",size=10,sha256=new string('b',64),layout="portable-v1",approval_record_id="fixture-only"};
    private static string Raw(object o)=>JsonSerializer.Serialize(o);
    private static byte[] Descriptor(object? candidate)=>Encoding.UTF8.GetBytes(Raw(new{schema=1,channel="beta-notice",candidate}));
    private sealed class Fixture : HttpMessageHandler
    {
        public DateTimeOffset Clock=Now;
        public Dictionary<string,string> Routes=[];
        public List<string> Requests=[];
        public Func<string,HttpResponseMessage?>? Override;
        public Fixture(object? candidate=null,bool empty=false)
        {
            var bytes=Descriptor(empty?null:candidate??Candidate());
            var blob=Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0").Concat(bytes).ToArray()));
            Routes["/git/ref/heads/main"]=Raw(new{@ref="refs/heads/main",@object=new{type="commit",sha=Commit}});
            Routes["/git/commits/"+Commit]=Raw(new{sha=Commit,tree=new{sha=new string('c',40)}});
            Tree('c',".github","040000","tree",new string('d',40));Tree('d',"release","040000","tree",new string('e',40));Tree('e',"approved-beta-notice.json","100644","blob",blob);
            Routes["/git/blobs/"+blob]=Raw(new{sha=blob,encoding="base64",size=bytes.Length,content=Convert.ToBase64String(bytes)});
            Routes["/releases/1"]=Raw(new{id=1,draft=false,prerelease=true,tag_name="v"+Version,published_at="2026-11-18T08:00:00Z",assets=Array.Empty<object>()});
            var asset=new{id=2,name="StartApp-win-x64.zip",size=10,state="uploaded",browser_download_url="https://github.com/my-sochen/Start-app/releases/download/v"+Version+"/StartApp-win-x64.zip"};
            Routes["/releases/assets/2"]=Raw(asset);Routes["/releases/1/assets?per_page=100&page=1"]=Raw(new[]{asset});
            Routes["/git/ref/tags/v"+Version]=Raw(new{@ref="refs/tags/v"+Version,@object=new{type="commit",sha=Commit}});
        }
        public void Tree(char sha,string path,string mode,string type,string target)=>Routes["/git/trees/"+new string(sha,40)]=Raw(new{sha=new string(sha,40),truncated=false,tree=new[]{new{path,mode,type,sha=target}}});
        public void Change(string route,string field,JsonNode? value){var o=JsonNode.Parse(Routes[route])!;o[field]=value;Routes[route]=o.ToJsonString();}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Assert.Equal("api.github.com",request.RequestUri!.Host);Assert.Null(request.Headers.Authorization);Assert.True(request.Headers.CacheControl!.NoStore);Assert.False(request.Headers.Contains("Cookie"));
            var path=request.RequestUri.PathAndQuery.Replace("/repos/my-sochen/Start-app","");Requests.Add(path);
            var response=Override?.Invoke(path)??new(HttpStatusCode.OK){Content=new StringContent(Routes[path],Encoding.UTF8,"application/json")};response.RequestMessage=request;return Task.FromResult(response);
        }
        public MainBetaNoticeSource Source(string installed="0.1.0-proto.1")=>new(installed,this,()=>Clock);
    }
    [Fact]public async Task Exact_tracked_source_published_asset_tag_and_final_main_qualified()
    {var f=new Fixture();using var s=f.Source();var r=await s.ReadAsync(default);Assert.Equal(Version,r!.Version);Assert.Equal("1/2/10/fixture-only",r.PublicationIdentity);Assert.Equal(11,f.Requests.Count);Assert.Equal(f.Requests[0],f.Requests[^1]);Assert.DoesNotContain(f.Requests,p=>p.Contains("download"));}
    [Fact]public async Task Startup_once_then_immediate_explicit_click_rechecks_and_only_opens_site()
    {var f=new Fixture();using var vm=new UpdateViewModel(f.Source(),"0.1.0-proto.1",u=>Assert.Equal(UpdateViewModel.OfficialWebsite,u.AbsoluteUri),revalidateBeforeDownload:true);var notices=0;vm.StartupUpdateAvailable+=()=>notices++;await vm.CheckOnStartupOnceAsync();await vm.CheckOnStartupOnceAsync();Assert.Equal(1,notices);vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.True(vm.UpdateAvailable);Assert.Equal(22,f.Requests.Count);}
    [Theory][InlineData("0.2.0-beta.1")][InlineData("0.2.0")][InlineData("1.0.0-proto.1")]
    public async Task Equal_stable_or_newer_installed_not_offered(string installed){var f=new Fixture();using var s=f.Source(installed);Assert.Null(await s.ReadAsync(default));Assert.Equal(7,f.Requests.Count);}
    [Fact]public async Task Explicit_null_is_known_no_candidate(){var f=new Fixture(empty:true);using var s=f.Source();Assert.Null(await s.ReadAsync(default));Assert.Equal(7,f.Requests.Count);}
    [Theory][InlineData("draft","true")][InlineData("prerelease","false")][InlineData("published_at","null")][InlineData("published_at","\"2026-11-18T10:00:00Z\"")][InlineData("published_at","\"bad\"")][InlineData("tag_name","\"other\"")]
    public async Task Enrolled_but_not_currently_published_beta_is_unknown(string field,string value){var f=new Fixture();f.Change("/releases/1",field,JsonNode.Parse(value));using var s=f.Source();await Assert.ThrowsAnyAsync<Exception>(()=>s.ReadAsync(default));}
    [Theory][InlineData("state","\"new\"")][InlineData("size","11")][InlineData("id","3")][InlineData("browser_download_url","\"https://other.invalid/x\"")]
    public async Task Asset_exact_identity_uploaded_and_fixed_repo_required(string field,string value){var f=new Fixture();f.Change("/releases/assets/2",field,JsonNode.Parse(value));using var s=f.Source();await Assert.ThrowsAnyAsync<Exception>(()=>s.ReadAsync(default));}
    [Theory][InlineData("120000","blob")][InlineData("160000","commit")][InlineData("040000","tree")]
    public async Task Descriptor_symlink_submodule_or_directory_refused(string mode,string type){var f=new Fixture();f.Tree('e',"approved-beta-notice.json",mode,type,new string('f',40));using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));Assert.DoesNotContain(f.Requests,p=>p.StartsWith("/git/blobs"));}
    [Fact]public async Task Partial_embedded_assets_does_not_replace_membership(){var f=new Fixture();f.Routes["/releases/1/assets?per_page=100&page=1"]="[]";using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));}
    [Fact]public async Task Moved_tag_refused(){var f=new Fixture();f.Routes["/git/ref/tags/v"+Version]=Raw(new{@ref="refs/tags/v"+Version,@object=new{type="commit",sha=new string('f',40)}});using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));}
    [Fact]public async Task Main_drift_after_reads_refused(){var f=new Fixture();f.Override=path=>path=="/git/ref/heads/main"&&f.Requests.Count>1?new(HttpStatusCode.OK){Content=new StringContent(Raw(new{@ref="refs/heads/main",@object=new{type="commit",sha=new string('f',40)}}),Encoding.UTF8,"application/json")}:null;using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));}
    [Fact]public async Task Source_and_click_cooldown_no_background_retry(){var f=new Fixture();using var s=f.Source();await s.ReadAsync(default);var count=f.Requests.Count;await Assert.ThrowsAsync<NoticeCheckDeferredException>(()=>s.ReadAsync(default));Assert.Equal(count,f.Requests.Count);await s.ReadForClickAsync(default);count=f.Requests.Count;await Assert.ThrowsAsync<NoticeCheckDeferredException>(()=>s.ReadForClickAsync(default));Assert.Equal(count,f.Requests.Count);}
    [Fact]public async Task Shared_ip_rate_limit_blocks_click_too(){var f=new Fixture();f.Override=_=>{var r=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("{}")};r.Headers.RetryAfter=new(TimeSpan.FromMinutes(5));return r;};using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadForClickAsync(default));Assert.Single(f.Requests);}
    [Theory][InlineData("{\"schema\":1,\"schema\":1,\"channel\":\"beta-notice\",\"candidate\":null}")][InlineData("{\"schema\":1,\"channel\":\"beta-notice\",\"candidate\":null,\"other\":true}")]
    public void Strict_descriptor_duplicate_or_extra_rejected(string raw)=>Assert.Throws<InvalidDataException>(()=>MainBetaNoticeSource.ParseDescriptor(Encoding.UTF8.GetBytes(raw),Now));
    [Fact]public async Task Annotated_tag_is_peeled_to_exact_commit()
    {var f=new Fixture();var tag=new string('f',40);f.Routes["/git/ref/tags/v"+Version]=Raw(new{@ref="refs/tags/v"+Version,@object=new{type="tag",sha=tag}});f.Routes["/git/tags/"+tag]=Raw(new{sha=tag,@object=new{type="commit",sha=Commit}});using var s=f.Source();Assert.NotNull(await s.ReadAsync(default));Assert.Equal(12,f.Requests.Count);}
    [Fact]public async Task Truncated_tree_and_blob_digest_tamper_fail_closed()
    {foreach(var mutate in new[]{"tree","blob"}){var f=new Fixture();if(mutate=="tree")f.Change("/git/trees/"+new string('c',40),"truncated",JsonValue.Create(true));else{var key=f.Routes.Keys.Single(k=>k.StartsWith("/git/blobs/"));f.Change(key,"content",JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))));}using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));}}
    [Fact]public async Task Paged_asset_membership_requires_complete_terminal_page()
    {var f=new Fixture();var unrelated=Enumerable.Range(100,100).Select(i=>new{id=i,name="other",size=1,state="uploaded",browser_download_url="https://example.invalid/"}).ToArray();var target=f.Routes["/releases/1/assets?per_page=100&page=1"];f.Routes["/releases/1/assets?per_page=100&page=1"]=Raw(unrelated);f.Routes["/releases/1/assets?per_page=100&page=2"]=target;using var s=f.Source();Assert.NotNull(await s.ReadAsync(default));Assert.Equal(12,f.Requests.Count);}
    [Fact]public async Task Valid_changed_approved_identity_survives_early_second_click_then_rechecks_after_wait()
    {
        var f=new Fixture();var opened=0;using var vm=new UpdateViewModel(f.Source(),"0.1.0-proto.1",_=>opened++,revalidateBeforeDownload:true);await vm.CheckAsync();
        var changed=JsonSerializer.SerializeToNode(Candidate())!;changed["approval_record_id"]="fixture-reviewed-second";
        f.Routes=new Fixture(changed).Routes;
        vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.True(vm.UpdateAvailable);Assert.Contains("השתנו",vm.Status);Assert.Equal(0,opened);
        var calls=f.Requests.Count;vm.DownloadCommand.Execute(null);await vm.CurrentCheck;
        Assert.True(vm.UpdateAvailable);Assert.True(vm.DownloadCommand.CanExecute(null));Assert.Contains("30",vm.Status);Assert.Contains("בדיקה נוספת",vm.Status);Assert.Equal(calls,f.Requests.Count);Assert.Equal(0,opened);
        f.Clock=f.Clock.AddSeconds(31);vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.Equal(calls+11,f.Requests.Count);Assert.Equal(1,opened);
    }
    [Fact]public async Task Changed_click_identity_requires_another_explicit_click()
    {var f=new Fixture();var opened=0;using var vm=new UpdateViewModel(f.Source(),"0.1.0-proto.1",_=>opened++,revalidateBeforeDownload:true);await vm.CheckAsync();f.Routes["/releases/assets/2"]="{}";vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Assert.False(vm.UpdateAvailable);Assert.Equal(0,opened);}
    [Theory][InlineData(201)][InlineData(302)][InlineData(404)]
    public async Task Non200_or_redirect_never_becomes_known_no_candidate(int status){var f=new Fixture();f.Override=_=>new((HttpStatusCode)status){Content=new StringContent("{}")};using var s=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>s.ReadAsync(default));}
    [Fact]public async Task Precanceled_request_performs_no_reads(){var f=new Fixture();using var s=f.Source();using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.ReadAsync(cancel.Token));Assert.Empty(f.Requests);}
    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Started.TrySetResult();await Task.Delay(Timeout.Infinite,token);throw new Exception("unreachable");}
    }
    [Fact]public async Task One_inflight_and_cancellation_release_source_without_retry()
    {var h=new WaitingHandler();using var source=new MainBetaNoticeSource("0.1.0-proto.1",h,()=>Now);using var cancel=new CancellationTokenSource();var pending=source.ReadAsync(cancel.Token);await h.Started.Task;await Assert.ThrowsAsync<InvalidOperationException>(()=>source.ReadForClickAsync(default));cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);await Assert.ThrowsAsync<NoticeCheckDeferredException>(()=>source.ReadAsync(default));}
    [Fact]public async Task Oversized_response_and_nonjson_or_encoded_media_fail_closed()
    {foreach(var mode in new[]{"bytes","media","encoding"}){var f=new Fixture();f.Override=_=>{var content=new ByteArrayContent(mode=="bytes"?new byte[1048577]:Encoding.UTF8.GetBytes("{}"));content.Headers.ContentType=new(mode=="media"?"text/html":"application/json");if(mode=="encoding")content.Headers.ContentEncoding.Add("gzip");return new(HttpStatusCode.OK){Content=content};};using var source=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>source.ReadAsync(default));}}
    [Fact]public async Task Annotated_cycle_and_asset_page_bound_stop()
    {foreach(var mode in new[]{"tag","pages"}){var f=new Fixture();if(mode=="tag"){var tag=new string('f',40);f.Routes["/git/ref/tags/v"+Version]=Raw(new{@ref="refs/tags/v"+Version,@object=new{type="tag",sha=tag}});f.Routes["/git/tags/"+tag]=Raw(new{sha=tag,@object=new{type="tag",sha=tag}});}else{var page=Raw(Enumerable.Range(10,100).Select(i=>new{id=i}));for(var i=1;i<=8;i++)f.Routes["/releases/1/assets?per_page=100&page="+i]=page;}using var source=f.Source();await Assert.ThrowsAsync<InvalidDataException>(()=>source.ReadAsync(default));Assert.True(f.Requests.Count<=24);}}
    [Fact]public async Task Unknown_keeps_app_working_without_false_up_to_date(){var f=new Fixture();f.Override=_=>new(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{}")};using var vm=new UpdateViewModel(f.Source(),"0.1.0-proto.1",_=>throw new Exception());await vm.CheckOnStartupOnceAsync();Assert.False(vm.UpdateAvailable);Assert.False(vm.IsBusy);Assert.Contains("לא ניתן",vm.Status);}
}
