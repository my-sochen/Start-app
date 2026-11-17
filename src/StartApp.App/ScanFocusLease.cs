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
    private readonly bool _originIsUs;
    private bool _revoked;
    private readonly CancellationToken _cancellation;
    public ScanFocusLease(CancellationToken cancellation = default)
    {
        _cancellation = cancellation;
        _input = Input(); GetWindowThreadProcessId(_origin, out var pid); _originIsUs = pid == Environment.ProcessId;
        _observed.Add(_origin);
    }
    private static uint? Input() { var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; return GetLastInputInfo(ref input) ? input.Tick : null; }
    public bool CanContinue
    {
        get
        {
            if (_revoked || _cancellation.IsCancellationRequested) return false;
            var foreground = GetForegroundWindow();
            if (_input is null || Input() != _input || foreground == IntPtr.Zero || !_observed.Contains(foreground))
            {
                _revoked = true;
                // Scalar reason only; do not re-read input or focus for diagnostics.
                BrowserCaptureTrace.Write(0, _input is null ? "focus-input-unavailable" : foreground == IntPtr.Zero ? "focus-zero" : !_observed.Contains(foreground) ? "focus-unobserved" : "focus-input-changed");
            }
            return !_revoked;
        }
    }
    public void ObserveBrowser(IntPtr hwnd) { if (CanContinue) _observed.Add(hwnd); }
    public void Dispose()
    {
        if (_originIsUs && CanContinue && IsWindow(_origin) && GetForegroundWindow() != _origin)
            SetForegroundWindow(_origin);
    }
}
