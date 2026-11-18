using System.Globalization;
using System.Text.RegularExpressions;

namespace StartApp.Presentation;

// Numeric version precedence, then stable > beta > prototype on the same version line.
public sealed record UpdateVersion(int Major, int Minor, int Patch, int? Prototype, int? Beta = null) : IComparable<UpdateVersion>
{
    private static readonly Regex Pattern = new(@"\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-(proto|beta)\.(0|[1-9][0-9]{0,8}))?\z", RegexOptions.CultureInvariant);
    public bool IsStable => Prototype is null && Beta is null;
    public static UpdateVersion? Parse(string? value)
    {
        if (value is null || value.Length > 64) return null;
        var m = Pattern.Match(value); if (!m.Success) return null;
        int Number(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return new(Number(1), Number(2), Number(3), m.Groups[4].Value == "proto" ? Number(5) : null,
            m.Groups[4].Value == "beta" ? Number(5) : null);
    }
    public int CompareTo(UpdateVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;
        int Rank(UpdateVersion v) => v.IsStable ? 2 : v.Beta is not null ? 1 : 0;
        c = Rank(this).CompareTo(Rank(other));
        return c != 0 ? c : (Beta ?? Prototype ?? 0).CompareTo(other.Beta ?? other.Prototype ?? 0);
    }
}

public sealed record ApprovedUpdate(string Version, string Commit, string Sha256, string? PublicationIdentity = null);
public interface IApprovedUpdateSource
{
    Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken);
}

// Deliberately no network route, old pointer fallback or release discovery. Activation
// needs reviewed hosting, per-release owner approval, and build identity stamping.
public sealed class InactiveUpdateSource : IApprovedUpdateSource
{
    public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Update publication is not activated.");
}

public sealed class UpdateViewModel : ObservableBase, IDisposable
{
    public const string OfficialWebsite = "https://my-sochen.github.io/Start-app/";
    private readonly IApprovedUpdateSource _source;
    private readonly UpdateVersion? _current;
    private readonly Action<Uri> _openBrowser;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _timeout;
    private readonly bool _revalidateBeforeDownload;
    private readonly bool _discoveryEnabled;
    private ApprovedUpdate? _offered;
    private bool _disposed;
    private bool _busy;
    private bool _startupCheckStarted;
    private bool _available;
    private string _status;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); CheckCommand.Raise(); DownloadCommand.Raise(); } }
    public bool UpdateAvailable { get => _available; private set { Set(ref _available, value); DownloadCommand.Raise(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string InstalledVersionDisplay { get; }
    public string DistributionDisplay => _current?.Prototype is not null ? "אב טיפוס - גרסת ניסוי, לא גרסת בטא" : _current?.Beta is not null ? "גרסת בטא" : _current is null ? "בנייה לא מזוהה - אינה גרסת הפצה מאומתת" : "גרסה יציבה";
    public RelayCommand CheckCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public Task CurrentCheck { get; private set; } = Task.CompletedTask;
    public event Action? StartupUpdateAvailable;

    public UpdateViewModel(IApprovedUpdateSource source, string? installedVersion, Action<Uri> openBrowser, TimeSpan? timeout = null, bool revalidateBeforeDownload = false)
    {
        _revalidateBeforeDownload = revalidateBeforeDownload; _discoveryEnabled = source is not InactiveUpdateSource;
        _source = source; _current = UpdateVersion.Parse(installedVersion); _openBrowser = openBrowser;
        InstalledVersionDisplay = _current is null ? "גרסה מותקנת: זהות הבנייה טרם אומתה" : $"גרסה מותקנת: {VersionForRtl(installedVersion!)}";
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _status = _current is null || !_discoveryEnabled ? "בדיקת עדכונים תופעל לאחר אישור מקור הגרסאות וזהות הבנייה." : "בדיקת עדכונים זמינה לפי בקשה.";
        CheckCommand = new(_ => CurrentCheck = CheckAsync(), _ => !_disposed && !IsBusy && _current is not null && _discoveryEnabled);
        DownloadCommand = new(_ => OpenDownload(), _ => !_disposed && UpdateAvailable && !IsBusy);
    }

    // Version tokens are validated ASCII identities. Explicit LTR embedding keeps their
    // hyphen/dot groups in logical order inside Hebrew RTL sentences; surrounding text stays RTL.
    private static string VersionForRtl(string version) => "\u202A" + version + "\u202C";

    // One discovery attempt per application updater instance, not per Settings
    // opening or repeated Window.Loaded event. Inactive identity still reads zero.
    public Task CheckOnStartupOnceAsync()
    {
        if (_disposed || _startupCheckStarted) return Task.CompletedTask;
        // An in-flight manual check must not consume the startup-only attempt.
        if (IsBusy) return CurrentCheck;
        _startupCheckStarted = true;
        return CurrentCheck = CheckStartupAsync();
    }

    private async Task CheckStartupAsync()
    {
        await CheckAsync();
        if (!_disposed && UpdateAvailable) StartupUpdateAvailable?.Invoke();
    }

    public async Task CheckAsync()
    {
        if (_disposed || IsBusy || _current is null || !_discoveryEnabled) return;
        IsBusy = true; UpdateAvailable = false; _offered = null; Status = "בודק עדכונים...";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(_timeout);
        try
        {
            var candidate = await _source.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
            if (_disposed) return;
            if (candidate is null) { Status = "אין כרגע גרסה מאושרת במקור העדכונים."; return; }
            var version = UpdateVersion.Parse(candidate.Version);
            if (version is null || !Regex.IsMatch(candidate.Commit ?? "", "\\A[0-9a-f]{40}\\z") ||
                !Regex.IsMatch(candidate.Sha256 ?? "", "\\A[0-9a-f]{64}\\z"))
            { Status = "לא ניתן לאמת את פרטי הגרסה. לא תוצע הורדה."; return; }
            // A stable installation must not receive an experimental prototype.
            if (_current.IsStable && !version.IsStable)
            { Status = "אין עדכון מתאים לערוץ הגרסה המותקנת."; return; }
            UpdateAvailable = version.CompareTo(_current) > 0;
            if (UpdateAvailable) _offered = candidate;
            Status = UpdateAvailable ? $"{(version.Beta is not null ? "גרסת בטא זמינה" : "זמין עדכון")} {VersionForRtl(candidate.Version)}. ההורדה תיפתח באתר הרשמי; ההתקנה ידנית." : "לא נמצאה גרסה מאושרת חדשה יותר.";
        }
        catch (OperationCanceledException) { if (!_disposed) Status = "בדיקת העדכונים לא הסתיימה בזמן. אפשר לנסות שוב ידנית."; }
        // A future source may fail with an adapter-specific exception. The optional
        // check must fail closed without a faulted command or a stuck checking label.
        // Do not turn runtime-fatal failures into a normal network error.
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { if (!_disposed) Status = "לא ניתן לבדוק עדכונים כרגע. הגרסה הנוכחית ממשיכה לעבוד."; }
        finally { if (!_disposed) IsBusy = false; }
    }
    private void OpenDownload()
    {
        if (_disposed || !UpdateAvailable || IsBusy) return;
        if (_revalidateBeforeDownload) { CurrentCheck = RevalidateAndOpenAsync(); return; }
        OpenDownloadCore();
    }
    private async Task RevalidateAndOpenAsync()
    {
        var previous = _offered;
        await CheckAsync();
        if (_disposed || !UpdateAvailable) return;
        if (previous != _offered) { Status = "פרטי הגרסה השתנו. בדקו את הגרסה החדשה ולחצו שוב לפתיחת האתר."; return; }
        OpenDownloadCore();
    }
    private void OpenDownloadCore()
    {
        if (_disposed || !UpdateAvailable) return;
        try { _openBrowser(new Uri(OfficialWebsite)); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Status = "לא ניתן לפתוח דפדפן. כתובת האתר הרשמי: " + OfficialWebsite; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        if (_source is IDisposable owned) owned.Dispose();
        CheckCommand.Raise(); DownloadCommand.Raise();
    }
}
