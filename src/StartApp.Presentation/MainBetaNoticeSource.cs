using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StartApp.Presentation;

// Private implementation. No production composition/descriptor enrollment is enabled.
// An informational notice only: public metadata is NOT downloaded ZIP verification.
public sealed class NoticeCheckDeferredException(DateTimeOffset retryAt, TimeSpan retryAfter) : IOException("Notice check cooldown")
{
    public DateTimeOffset RetryAt { get; }=retryAt;
    public TimeSpan RetryAfter { get; }=retryAfter;
}
public interface IClickRevalidatingUpdateSource : IApprovedUpdateSource
{
    Task<ApprovedUpdate?> ReadForClickAsync(CancellationToken cancellationToken);
}
public sealed class MainBetaNoticeSource : IClickRevalidatingUpdateSource, IDisposable
{
    public const string DescriptorPath = ".github/release/approved-beta-notice.json";
    private const string Root = "https://api.github.com/repos/my-sochen/Start-app";
    private readonly HttpClient _client;
    private readonly string _installed;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _retryAfter, _checkAfter, _clickAfter;
    private int _active;
    public MainBetaNoticeSource(string installed, HttpMessageHandler handler, Func<DateTimeOffset>? clock = null)
    {
        _installed = UpdateVersion.Parse(installed) is not null ? installed : throw new ArgumentException("Installed identity required");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _client = new(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public static HttpMessageHandler CreateTransport() => new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false, Credentials = null, AutomaticDecompression = DecompressionMethods.None };
    public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) => ReadCore(false,cancellationToken);
    public Task<ApprovedUpdate?> ReadForClickAsync(CancellationToken cancellationToken) => ReadCore(true,cancellationToken);
    private async Task<ApprovedUpdate?> ReadCore(bool click,CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) throw new InvalidOperationException("Check already active");
        try
        {
            if (_clock() < _retryAfter) throw Bad("Notice API temporarily unavailable; retry later");
            var next=click?_clickAfter:_checkAfter;
            if(_clock()<next)throw new NoticeCheckDeferredException(next,next-_clock());
            if(click)_clickAfter=_clock().AddSeconds(30);else _checkAfter=_clock().AddSeconds(30);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var session = new Session(this, deadline.Token);
            var main = await session.Main();
            var commit = await session.Get("/git/commits/" + main);
            if (Hash(commit, "sha") != main) throw Bad("Commit identity");
            var tree = Hash(commit.GetProperty("tree"), "sha");
            var parts = DescriptorPath.Split('/');
            for (var index = 0; index < parts.Length; index++)
            {
                var listing = await session.Get("/git/trees/" + tree);
                if (Hash(listing,"sha") != tree || Bool(listing,"truncated")) throw Bad("Incomplete tree");
                var entries = listing.GetProperty("tree");
                if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength()>4096) throw Bad("Tree count");
                var found = entries.EnumerateArray().Where(e => Text(e,"path") == parts[index]).ToArray();
                if (found.Length != 1) throw Bad("Descriptor path unavailable");
                var entry=found[0]; var leaf=index==parts.Length-1;
                if (Text(entry,"type") != (leaf?"blob":"tree") || (leaf ? Text(entry,"mode") is not ("100644" or "100755") : Text(entry,"mode")!="040000")) throw Bad("Descriptor tree mode");
                tree=Hash(entry,"sha");
            }
            var blob=await session.Get("/git/blobs/"+tree);
            if(Hash(blob,"sha")!=tree || Text(blob,"encoding")!="base64")throw Bad("Descriptor blob");
            var size=Positive(blob,"size");if(size>65536)throw Bad("Descriptor bytes");
            var bytes=Convert.FromBase64String(Text(blob,"content"));
            if(bytes.Length!=size || Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes("blob "+bytes.Length.ToString(CultureInfo.InvariantCulture)+"\0").Concat(bytes).ToArray()))!=tree)throw Bad("Descriptor blob digest");
            var candidate=ParseDescriptor(bytes,_clock());
            ApprovedUpdate? result=null;
            if(candidate is not null && !UpdateVersion.Parse(_installed)!.IsStable && UpdateVersion.Parse(candidate.Version)!.CompareTo(UpdateVersion.Parse(_installed))>0)
            {
                await session.Published(candidate);
                result=new(candidate.Version,candidate.Commit,candidate.Sha256,$"{candidate.ReleaseId}/{candidate.AssetId}/{candidate.Size}/{candidate.ApprovalRecordId}");
            }
            if(await session.Main()!=main)throw Bad("Notice source changed during check");
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally { Volatile.Write(ref _active,0); }
    }
    public static CatalogRelease? ParseDescriptor(ReadOnlyMemory<byte> bytes,DateTimeOffset now)
    {
        if(bytes.Length>65536)throw Bad("Descriptor size");
        using var doc=Strict(bytes);var root=doc.RootElement;
        Fields(root,"schema","channel","candidate");
        if(Positive(root,"schema")!=1 || Text(root,"channel")!="beta-notice")throw Bad("Descriptor channel");
        var record=root.GetProperty("candidate");if(record.ValueKind==JsonValueKind.Null)return null;
        // Reuse reviewed exact record/version/int64 semantics without changing the
        // rolling catalog contract. Synthetic envelope exists only in memory.
        now=now.ToUniversalTime();
        var generated=new DateTimeOffset(now.Year,now.Month,now.Day,now.Hour,now.Minute,now.Second,TimeSpan.Zero);
        var envelope=Encoding.UTF8.GetBytes("{\"schema\":1,\"revision\":1,\"channel\":\"beta-notice\",\"generated_utc\":\""+generated.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture)+"\",\"expires_utc\":\""+generated.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture)+"\",\"releases\":["+record.GetRawText()+"]}");
        return ApprovedCatalogContract.Parse(envelope,now,new(66000,1,TimeSpan.FromHours(1),TimeSpan.FromHours(1)),channel:"beta-notice").Releases.Single();
    }
    private sealed class Session(MainBetaNoticeSource owner,CancellationToken token)
    {
        private int _calls;private int _bytes;
        public async Task<JsonElement> Get(string path)
        {
            if(++_calls>24)throw Bad("Notice request budget");
            var uri=new Uri(Root+path);
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("StartApp-BetaNotice/1");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            request.Headers.CacheControl=new(){NoCache=true,NoStore=true};
            using var response=await owner._client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry=owner._clock().AddMinutes(1);
                if(response.Headers.RetryAfter?.Delta is TimeSpan delta && delta>TimeSpan.Zero)retry=Max(retry,owner._clock().Add(delta));
                if(response.Headers.RetryAfter?.Date is DateTimeOffset date)retry=Max(retry,date);
                if(response.Headers.TryGetValues("X-RateLimit-Reset",out var resets) && long.TryParse(resets.SingleOrDefault(),out var seconds))
                    try { retry=Max(retry,DateTimeOffset.FromUnixTimeSeconds(seconds)); }catch(ArgumentOutOfRangeException){}
                owner._retryAfter=Max(owner._retryAfter,retry);throw Bad("Notice API unavailable; retry later");
            }
            if(response.StatusCode!=HttpStatusCode.OK || response.RequestMessage?.RequestUri!=uri)throw Bad("Notice response identity/status");
            var content=response.Content;var type=content.Headers.ContentType;
            if(type?.MediaType is not ("application/json" or "application/vnd.github+json") || type.CharSet?.Trim('"') is string charset && !charset.Equals("utf-8",StringComparison.OrdinalIgnoreCase) || content.Headers.ContentEncoding.Count!=0)throw Bad("Notice response media");
            if(content.Headers.ContentLength>1048576)throw Bad("Notice response size");
            await using var stream=await content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];
            while(true){var count=await stream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,1048577-(int)output.Length)),token);if(count==0)break;_bytes+=count;if(output.Length+count>1048576 || _bytes>4194304)throw Bad("Notice allocation budget");output.Write(buffer,0,count);}
            if(content.Headers.ContentLength is long length && length!=output.Length)throw Bad("Incomplete notice response");
            token.ThrowIfCancellationRequested();using var doc=Strict(output.ToArray());return doc.RootElement.Clone();
        }
        public async Task<string> Main(){var d=await Get("/git/ref/heads/main");if(Text(d,"ref")!="refs/heads/main" || Text(d.GetProperty("object"),"type")!="commit")throw Bad("MAIN reference");return Hash(d.GetProperty("object"),"sha");}
        public async Task Published(CatalogRelease r)
        {
            var release=await Get("/releases/"+r.ReleaseId);
            if(Positive(release,"id")!=r.ReleaseId || Bool(release,"draft") || !Bool(release,"prerelease") || Text(release,"tag_name")!=r.Tag)throw Bad("Release unavailable");
            var published=Text(release,"published_at");
            if(!DateTimeOffset.TryParseExact(published,"yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date) || date>owner._clock())throw Bad("Release publication time");
            var asset=await Get("/releases/assets/"+r.AssetId);Asset(asset,r);
            var member=false;
            for(var page=1;page<=8;page++)
            {
                var list=await Get("/releases/"+r.ReleaseId+"/assets?per_page=100&page="+page);
                if(list.ValueKind!=JsonValueKind.Array || list.GetArrayLength()>100)throw Bad("Asset membership");
                foreach(var entry in list.EnumerateArray())if(Positive(entry,"id")==r.AssetId){if(member)throw Bad("Duplicate asset");Asset(entry,r);member=true;}
                if(list.GetArrayLength()<100){if(!member)throw Bad("Asset absent");break;}
                if(page==8)throw Bad("Asset pagination bound");
            }
            var tag=await Get("/git/ref/tags/"+r.Tag);
            if(Text(tag,"ref")!="refs/tags/"+r.Tag)throw Bad("Tag reference");
            var obj=tag.GetProperty("object");var seen=new HashSet<string>();
            for(var depth=0;depth<5;depth++)
            {
                var sha=Hash(obj,"sha");var kind=Text(obj,"type");
                if(kind=="commit"){if(sha!=r.Commit)throw Bad("Moved tag");return;}
                if(kind!="tag" || !seen.Add(sha))throw Bad("Tag chain");
                var annotated=await Get("/git/tags/"+sha);if(Hash(annotated,"sha")!=sha)throw Bad("Tag object identity");obj=annotated.GetProperty("object");
            }
            throw Bad("Tag depth bound");
        }
        private static void Asset(JsonElement a,CatalogRelease r)
        {
            if(Positive(a,"id")!=r.AssetId || Text(a,"name")!=r.Asset || Positive(a,"size")!=r.Size || Text(a,"state")!="uploaded" || Text(a,"browser_download_url")!="https://github.com/my-sochen/Start-app/releases/download/"+r.Tag+"/"+r.Asset)throw Bad("Asset identity/state");
        }
    }
    private static DateTimeOffset Max(DateTimeOffset a,DateTimeOffset b)=>a>b?a:b;
    private static InvalidDataException Bad(string message)=>new(message);
    private static string Text(JsonElement e,string name)=>e.GetProperty(name).ValueKind==JsonValueKind.String?e.GetProperty(name).GetString()!:throw Bad("Expected string");
    private static bool Bool(JsonElement e,string name)=>e.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False?e.GetProperty(name).GetBoolean():throw Bad("Expected bool");
    private static long Positive(JsonElement e,string name){var v=e.GetProperty(name);if(v.ValueKind!=JsonValueKind.Number || !Regex.IsMatch(v.GetRawText(),"\\A[1-9][0-9]*\\z") || !v.TryGetInt64(out var n)||n<=0)throw Bad("Positive int64");return n;}
    private static string Hash(JsonElement e,string name){var s=Text(e,name);return Regex.IsMatch(s,"\\A[0-9a-f]{40}\\z")?s:throw Bad("Commit/blob hash");}
    private static void Fields(JsonElement e,params string[] names){if(e.ValueKind!=JsonValueKind.Object || !e.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names))throw Bad("Descriptor fields");}
    private static JsonDocument Strict(ReadOnlyMemory<byte> bytes)
    {
        var d=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
        try{void Check(JsonElement e){if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject()){if(!names.Add(p.Name))throw Bad("Duplicate JSON field");Check(p.Value);}}else if(e.ValueKind==JsonValueKind.Array)foreach(var a in e.EnumerateArray())Check(a);}Check(d.RootElement);return d;}catch{d.Dispose();throw;}
    }
    public void Dispose()=>_client.Dispose();
}
