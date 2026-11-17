using System.Globalization;
using System.Text.RegularExpressions;

namespace StartApp.Presentation;

// Numeric SemVer precedence, including patch releases. Build metadata is identity,
// not precedence. Only the reviewed prototype channel and stable versions are supported.
public sealed record UpdateVersion(int Major, int Minor, int Patch, int? Prototype) : IComparable<UpdateVersion>
{
    private static readonly Regex Pattern = new(@"\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-proto\.(0|[1-9][0-9]{0,8}))?\z", RegexOptions.CultureInvariant);
    public static UpdateVersion? Parse(string? value)
    {
        if (value is null || value.Length > 64) return null;
        var m = Pattern.Match(value);
        if (!m.Success) return null;
        int Number(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return new(Number(1), Number(2), Number(3), m.Groups[4].Success ? Number(4) : null);
    }
    public int CompareTo(UpdateVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        c = Patch.CompareTo(other.Patch); if (c != 0) return c;
        if (Prototype is null) return other.Prototype is null ? 0 : 1;
        return other.Prototype is null ? -1 : Prototype.Value.CompareTo(other.Prototype.Value);
    }
}

public sealed record ApprovedUpdate(string Version, string Commit, string Sha256);
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
    private bool _disposed;
    private bool _busy;
    private bool _startupCheckStarted;
    private bool _available;
    private string _status;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); CheckCommand.Raise(); } }
    public bool UpdateAvailable { get => _available; private set { Set(ref _available, value); DownloadCommand.Raise(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public RelayCommand CheckCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public Task CurrentCheck { get; private set; } = Task.CompletedTask;

    public UpdateViewModel(IApprovedUpdateSource source, string? installedVersion, Action<Uri> openBrowser, TimeSpan? timeout = null)
    {
        _source = source; _current = UpdateVersion.Parse(installedVersion); _openBrowser = openBrowser;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(timeout));
        _status = _current is null ? "בדיקת עדכונים תופעל לאחר אישור מקור הגרסאות וזהות הבנייה." : "בדיקת עדכונים זמינה לפי בקשה.";
        CheckCommand = new(_ => CurrentCheck = CheckAsync(), _ => !_disposed && !IsBusy && _current is not null);
        DownloadCommand = new(_ => OpenDownload(), _ => !_disposed && UpdateAvailable);
    }

    // One discovery attempt per application updater instance, not per Settings
    // opening or repeated Window.Loaded event. Inactive identity still reads zero.
    public Task CheckOnStartupOnceAsync()
    {
        if (_disposed || _startupCheckStarted) return Task.CompletedTask;
        _startupCheckStarted = true;
        return CurrentCheck = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (_disposed || IsBusy || _current is null) return;
        IsBusy = true; UpdateAvailable = false; Status = "בודק עדכונים...";
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
            if (_current.Prototype is null && version.Prototype is not null)
            { Status = "אין עדכון מתאים לערוץ הגרסה המותקנת."; return; }
            UpdateAvailable = version.CompareTo(_current) > 0;
            Status = UpdateAvailable ? $"זמין עדכון {candidate.Version}. ההורדה תיפתח באתר הרשמי; ההתקנה ידנית." : "לא נמצאה גרסה מאושרת חדשה יותר.";
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
        if (_disposed || !UpdateAvailable) return;
        try { _openBrowser(new Uri(OfficialWebsite)); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Status = "לא ניתן לפתוח דפדפן. כתובת האתר הרשמי: " + OfficialWebsite; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        CheckCommand.Raise(); DownloadCommand.Raise();
    }
}
