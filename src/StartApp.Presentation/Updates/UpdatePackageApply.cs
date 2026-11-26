using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StartApp.Presentation;

/// <summary>Phase 2 (approved 22.11): עדכן עכשיו downloads and installs the approved package
/// inside the app - בלי אתר ובלי התקנה ידנית. The offer still comes only from the approved
/// notice source; the download bytes are verified against the approved sha256 and size before
/// anything is staged, and the swap itself happens only after the app closes.</summary>
public interface IUpdatePackageTransport
{
    Task DownloadAsync(ApprovedUpdate update, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken);
}

/// <summary>A verified update package extracted and ready for the post-close swap.</summary>
public sealed record StagedUpdate(string Version, string StagingDirectory);

/// <summary>Downloaded bytes do not match the approved record. Never retried silently.</summary>
public sealed class UpdatePackageVerificationException(string message) : Exception(message);

public static class UpdatePackageStager
{
    private const int MaxEntries = 4096;
    private const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;

    public static async Task<StagedUpdate> StageAsync(ApprovedUpdate update, IUpdatePackageTransport transport, string workRoot, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workRoot);
        var id = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(workRoot, "package-" + id + ".zip");
        var stagingDir = Path.Combine(workRoot, "staging-" + id);
        try
        {
            await transport.DownloadAsync(update, zipPath, progress, cancellationToken).ConfigureAwait(false);
            var expected = TryExpectedSize(update);
            var length = new FileInfo(zipPath).Length;
            if (expected is long size && length != size)
                throw new UpdatePackageVerificationException("Package size does not match the approved record.");
            string hash;
            await using (var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdatePackageVerificationException("Package hash does not match the approved record.");
            ExtractSafe(zipPath, stagingDir);
            try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new StagedUpdate(update.Version, stagingDir);
        }
        catch
        {
            TryDeleteFile(zipPath); TryDeleteDir(stagingDir);
            throw;
        }
    }

    private static void ExtractSafe(string zipPath, string stagingDir)
    {
        var root = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries) throw new UpdatePackageVerificationException("Package entry count bound.");
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > MaxExtractedBytes) throw new UpdatePackageVerificationException("Package extracted size bound.");
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Length > 1 && name[1] == ':' || name.Split('/').Any(s => s == ".."))
                throw new UpdatePackageVerificationException("Package entry path escapes the staging directory.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(Path.Combine(stagingDir, name)); continue; }
            var dest = Path.GetFullPath(Path.Combine(stagingDir, name));
            if (!dest.StartsWith(root, StringComparison.Ordinal)) throw new UpdatePackageVerificationException("Package entry path escapes the staging directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: false);
        }
    }

    private static long? TryExpectedSize(ApprovedUpdate update)
    {
        var parts = (update.PublicationIdentity ?? "").Split('/');
        return parts.Length == 4 && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0 ? size : null;
    }

    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

/// <summary>The post-close swap, run by the helper copy with --apply-update. The target install
/// directory is moved aside and the staged directory takes its place, with rollback on failure.
/// Data never lives in either directory (store.json is under %LOCALAPPDATA%\StartApp).</summary>
public static class UpdateSwapper
{
    public static void ApplyStagedUpdate(string stagingDir, string targetDir)
    {
        if (!Directory.Exists(stagingDir)) throw new DirectoryNotFoundException(stagingDir);
        if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException(targetDir);
        var backup = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".old-" + Guid.NewGuid().ToString("N")[..8];
        Directory.Move(targetDir, backup);
        try { Directory.Move(stagingDir, targetDir); }
        catch { Directory.Move(backup, targetDir); throw; }
        try { Directory.Delete(backup, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static int RunApplyMode(string stagingDir, string targetDir, int waitPid, string relaunchExe, Func<int, bool>? processAlive = null, Action<string>? relauncher = null, TimeSpan? waitTimeout = null)
    {
        processAlive ??= DefaultAlive;
        relauncher ??= exe => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        var deadline = DateTime.UtcNow.Add(waitTimeout ?? TimeSpan.FromSeconds(120));
        while (processAlive(waitPid))
        {
            if (DateTime.UtcNow > deadline) return 2;
            Thread.Sleep(250);
        }
        ApplyStagedUpdate(stagingDir, targetDir);
        relauncher(relaunchExe);
        return 0;
    }

    private static bool DefaultAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}

/// <summary>Downloads the pinned GitHub release asset. The asset metadata is re-read from the
/// same hardened API the notice source uses (size/state must still match the approved record),
/// then the bytes come from the pinned browser_download_url with one manual https redirect.</summary>
public sealed class GitHubReleasePackageTransport : IUpdatePackageTransport, IDisposable
{
    private const string Root = "https://api.github.com/repos/my-sochen/Start-app";
    private const string DownloadPrefix = "https://github.com/my-sochen/Start-app/releases/download/";
    public const long MaxPackageBytes = 512L * 1024 * 1024;
    private readonly HttpClient _client;

    public GitHubReleasePackageTransport(HttpMessageHandler handler)
    { _client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan }; }

    public static HttpMessageHandler CreateTransport() => MainBetaNoticeSource.CreateTransport();

    public async Task DownloadAsync(ApprovedUpdate update, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var (assetId, expectedSize) = ParseIdentity(update.PublicationIdentity);
        var url = await ReadAssetUrlAsync(assetId, expectedSize, cancellationToken).ConfigureAwait(false);
        using var response = await GetWithOneRedirect(url, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long total = 0; var lastPercent = -1;
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            total += count;
            if (total > expectedSize || total > MaxPackageBytes) throw Bad("Package size bound");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            var percent = (int)(total * 100 / expectedSize);
            if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
        }
        if (total != expectedSize) throw Bad("Incomplete package");
    }

    private async Task<string> ReadAssetUrlAsync(long assetId, long expectedSize, CancellationToken cancellationToken)
    {
        var uri = new Uri(Root + "/releases/assets/" + assetId.ToString(CultureInfo.InvariantCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("StartApp-BetaNotice/1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw Bad("Asset metadata unavailable");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > 1048576) throw Bad("Asset metadata size");
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (root.GetProperty("id").GetInt64() != assetId) throw Bad("Asset identity");
        if (root.GetProperty("size").GetInt64() != expectedSize) throw Bad("Asset size changed");
        if (root.GetProperty("state").GetString() != "uploaded") throw Bad("Asset state");
        var url = root.GetProperty("browser_download_url").GetString() ?? throw Bad("Asset url");
        if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) throw Bad("Asset url identity");
        return url;
    }

    private async Task<HttpResponseMessage> GetWithOneRedirect(string url, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        for (var hop = 0; hop < 2; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (hop == 0) request.Headers.UserAgent.ParseAdd("StartApp-BetaNotice/1");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (hop == 1 || location is null || !location.IsAbsoluteUri || location.Scheme != "https") throw Bad("Package redirect");
                uri = location;
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) { response.Dispose(); throw Bad("Package download status"); }
            return response;
        }
        throw Bad("Package redirect");
    }

    private static (long AssetId, long Size) ParseIdentity(string? identity)
    {
        var parts = (identity ?? "").Split('/');
        if (parts.Length != 4
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var assetId) || assetId <= 0
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0 || size > MaxPackageBytes)
            throw Bad("Publication identity");
        return (assetId, size);
    }

    private static InvalidDataException Bad(string message) => new(message);
    public void Dispose() => _client.Dispose();
}
