using System.Runtime.InteropServices;

namespace StartApp.App;

/// <summary>Conservative best-effort capture-only focus return. Any input or unknown foreground permanently revokes it.
/// Never used after item launch. The native APIs can refuse focus changes, so this is not a focus restoration guarantee.</summary>
internal sealed class ScanFocusLease : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private readonly IntPtr _origin = GetForegroundWindow();
    private readonly HashSet<IntPtr> _observed = new();
    private readonly uint? _input;
#if STARTAPP_CAPTURE_TRACE
    private readonly long _traceScan;
#endif
    private readonly bool _originIsUs;
    private bool _revoked;
    private readonly CancellationToken _cancellation;
    public ScanFocusLease(CancellationToken cancellation = default, long traceScan = 0)
    {
        _cancellation = cancellation;
#if STARTAPP_CAPTURE_TRACE
        _traceScan = traceScan;
#endif
        _input = Input(); GetWindowThreadProcessId(_origin, out var pid); _originIsUs = pid == Environment.ProcessId;
        _observed.Add(_origin);
        TraceInput("input-baseline", _input);
    }
    private static uint? Input() { var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref input) ? input.Tick : null; }
    [System.Diagnostics.Conditional("STARTAPP_CAPTURE_TRACE")]
    private void TraceInput(string stage, uint? tick)
    {
#if STARTAPP_CAPTURE_TRACE
        BrowserCaptureTrace.Write(_traceScan, stage, count: unchecked((int)(tick ?? 0)), flag: tick.HasValue);
#endif
    }
    public bool CanContinue
    {
        get
        {
            if (_revoked || _cancellation.IsCancellationRequested) return false;
            var foreground = GetForegroundWindow();
            // Preserve the original short-circuit: no second native read when the baseline failed.
            var currentInput = _input is null ? null : Input();
            if (_input is null || currentInput != _input || foreground == IntPtr.Zero || !_observed.Contains(foreground))
            {
                _revoked = true;
                // Trace the exact read that revoked permission, never a diagnostic re-read.
                TraceInput("input-at-revocation", currentInput);
                TraceRevocation(_input is null ? "focus-input-unavailable" : currentInput is null ? "focus-input-read-failed" : currentInput != _input ? "focus-input-changed" : foreground == IntPtr.Zero ? "focus-zero" : "focus-unobserved");
            }
            return !_revoked;
        }
    }
    [System.Diagnostics.Conditional("STARTAPP_CAPTURE_TRACE")]
    private void TraceRevocation(string stage)
    {
#if STARTAPP_CAPTURE_TRACE
        BrowserCaptureTrace.Write(_traceScan, stage);
#endif
    }
    public void ObserveBrowser(IntPtr hwnd) { if (CanContinue) _observed.Add(hwnd); }
    public void Dispose()
    {
        if (_originIsUs && CanContinue && IsWindow(_origin) && GetForegroundWindow() != _origin)
            SetForegroundWindow(_origin);
    }
}
