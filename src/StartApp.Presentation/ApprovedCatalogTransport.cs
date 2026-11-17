using System.Net;
using System.Net.Http.Headers;

namespace StartApp.Presentation;

// Reusable bounded transport PREPARATION. No endpoint is selected here and no
// instance is wired into the application. Parsing does not authenticate approval:
// the reviewed deployment/record authority must be established before activation.
public sealed class ApprovedCatalogTransport : IDisposable
{
    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly CatalogPolicy _policy;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _deadline;
    public ApprovedCatalogTransport(Uri endpoint, CatalogPolicy policy, Func<DateTimeOffset> clock, TimeSpan deadline)
        : this(endpoint, policy, clock, deadline, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.None
        }) { }

    internal ApprovedCatalogTransport(Uri endpoint, CatalogPolicy policy, Func<DateTimeOffset> clock,
        TimeSpan deadline, HttpMessageHandler handler)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 ||
            endpoint.Fragment.Length != 0 || !endpoint.IsDefaultPort) throw new ArgumentException("An exact HTTPS endpoint without credentials/fragment/nonstandard port is required.", nameof(endpoint));
        if (policy.MaximumBytes is <= 0 or > 1048576 || policy.MaximumRecords is <= 0 or > 1024 ||
            policy.MaximumAge <= TimeSpan.Zero || policy.MaximumLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(policy));
        if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(deadline));
        _endpoint = endpoint; _policy = policy; _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _deadline = deadline;
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ValidatedCatalog> ReadAsync(CancellationToken cancellationToken, long? minimumRevision = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_deadline); var token = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("Catalog redirect is not allowed.");
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri != _endpoint) throw new InvalidDataException("Catalog response identity changed.");
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog content type is not JSON.");
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset) && !string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Catalog encoding is not UTF-8.");
        if (response.Content.Headers.ContentEncoding.Count != 0) throw new InvalidDataException("Encoded catalog is not supported.");
        if (response.Content.Headers.ContentLength is long length && length > _policy.MaximumBytes) throw new InvalidDataException("Catalog Content-Length exceeds limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[Math.Min(8192, _policy.MaximumBytes + 1)];
        while (true)
        {
            // Always read at most remaining+one so absent/misleading length cannot
            // grow memory beyond a fixed byte budget before rejection.
            var remaining = _policy.MaximumBytes - (int)output.Length;
            var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), token).ConfigureAwait(false);
            if (count == 0) break;
            if (count > remaining) throw new InvalidDataException("Catalog body exceeds byte limit.");
            output.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        var catalog = ApprovedCatalogContract.Parse(output.ToArray(), _clock(), _policy, minimumRevision);
        token.ThrowIfCancellationRequested();
        return catalog;
    }
    public void Dispose() => _client.Dispose();
}
