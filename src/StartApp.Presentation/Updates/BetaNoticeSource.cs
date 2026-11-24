namespace StartApp.Presentation;

// Policy/clock/transport are explicit. No real endpoint or activation is selected here.
// Production factory below remains inactive until the reviewed activation step.
public sealed class BetaNoticeSource : IApprovedUpdateSource, IDisposable
{
    public const int MaximumBytes = 65536;
    private readonly ApprovedCatalogTransport _transport;
    private readonly string _installedVersion;
    
    private long _revision;
    private int _active;
    public BetaNoticeSource(Uri endpoint, string installedVersion, HttpMessageHandler handler, Func<DateTimeOffset>? clock = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Host != "my-sochen.github.io" || endpoint.Port != 443 ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.AbsolutePath.StartsWith("/Start-app/", StringComparison.Ordinal)) throw new ArgumentException("Official HTTPS endpoint required");
        if (UpdateVersion.Parse(installedVersion) is null) throw new ArgumentException("Installed identity required");
        _installedVersion = installedVersion;
        _transport = new ApprovedCatalogTransport(endpoint, new(MaximumBytes,128,TimeSpan.FromHours(24),TimeSpan.FromHours(24)), clock ?? (() => DateTimeOffset.UtcNow), TimeSpan.FromSeconds(10), handler);
    }
    public static HttpMessageHandler CreateTransport() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false };
    public async Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) throw new InvalidOperationException("Check already active");
        try
        {
            var catalog = await _transport.ReadAsync(cancellationToken, _revision == 0 ? null : _revision, "beta-notice").ConfigureAwait(false);
            _revision = catalog.Revision; // In-session only, not cross-restart rollback protection.
            var selected = catalog.SelectNewer(_installedVersion);
            return selected is null ? null : new(selected.Version, selected.Commit, selected.Sha256, $"{selected.ReleaseId}/{selected.AssetId}/{selected.Size}/{selected.ApprovalRecordId}");
        }
        finally { Volatile.Write(ref _active, 0); }
    }
    public void Dispose() => _transport.Dispose();
}
