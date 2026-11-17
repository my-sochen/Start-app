using System.Diagnostics;
namespace StartApp.App;

// Compiled out of normal builds. Isolated Windows workflow only; no addresses, titles, paths or document text.
internal static class BrowserCaptureTrace
{
#if STARTAPP_CAPTURE_TRACE
    private static readonly object Gate = new();
    private static int _records;
    private static long _scans;
#endif
    [Conditional("STARTAPP_CAPTURE_TRACE")]
    public static void Begin(ref long scan)
    {
#if STARTAPP_CAPTURE_TRACE
        scan = System.Threading.Interlocked.Increment(ref _scans);
        Write(scan, "begin");
#endif
    }
    [Conditional("STARTAPP_CAPTURE_TRACE")]
    public static void Write(long scan, string stage, int index = -1, int count = -1, bool? flag = null)
    {
#if STARTAPP_CAPTURE_TRACE
        // Fixed stage strings and scalar metadata only. Single bounded file, explicit runner opt-in.
        var path = Environment.GetEnvironmentVariable("STARTAPP_CAPTURE_TRACE_PATH");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            lock (Gate)
            {
                if (_records++ >= 512) return;
                System.IO.File.AppendAllText(path, $"{Environment.TickCount64} scan={scan} stage={stage} index={index} count={count} flag={flag}\n");
            }
        }
        catch { /* diagnostics must not fail or change the capture */ }
#endif
    }
}
