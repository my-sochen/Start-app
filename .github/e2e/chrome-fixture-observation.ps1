# Diagnostic only: no capture/launch/selection decisions may rely on this census.
# Native enumeration stores metadata ONLY after exact fixture-PID filtering.
if (-not ('CaptureFixtureNative' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class CaptureFixtureNative {
  public sealed class Window {
    public long Hwnd { get; set; }
    public int Pid { get; set; }
    public bool Visible { get; set; }
    public bool Minimized { get; set; }
    public bool PositiveArea { get; set; }
  }
  public sealed class Snapshot {
    public bool EnumerationSucceeded { get; set; }
    public bool CapReached { get; set; }
    public Window[] Windows { get; set; } = Array.Empty<Window>();
  }
  private delegate bool EnumCallback(IntPtr h, IntPtr state);
  [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback cb,IntPtr state);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h,out Rect rect);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
  public static Snapshot Observe(int[] pids) {
    var owned = new HashSet<int>(pids ?? Array.Empty<int>());
    var rows = new List<Window>(); bool capped = false;
    bool enumerated = EnumWindows((h,_)=> {
      GetWindowThreadProcessId(h,out var pid);
      if (!owned.Contains((int)pid)) return true;
      if(rows.Count >= 20) { capped = true; return false; }
      bool rectOk = GetWindowRect(h,out var rect);
      rows.Add(new Window { Hwnd=h.ToInt64(), Pid=(int)pid, Visible=IsWindowVisible(h), Minimized=IsIconic(h),
        PositiveArea=rectOk && rect.Right>rect.Left && rect.Bottom>rect.Top });
      return true;
    },IntPtr.Zero);
    return new Snapshot { EnumerationSucceeded=enumerated, CapReached=capped, Windows=rows.ToArray() };
  }
}
'@
}
function Get-CaptureFixtureNativeSnapshot {
  param([int[]]$OwnedPids)
  # Never replace an empty profile-bound PID set with any Chrome process.
  if (@($OwnedPids).Count -eq 0) { return [pscustomobject]@{ EnumerationSucceeded=$true; CapReached=$false; Windows=@() } }
  return [CaptureFixtureNative]::Observe($OwnedPids)
}
function Test-CaptureFixtureWindowIdentity {
  param([long]$ExpectedHwnd,[int]$ExpectedPid,[int[]]$CurrentOwnedPids,[long]$ObservedHwnd,[int]$ObservedPid)
  return ($ExpectedHwnd -ne 0 -and $ExpectedPid -gt 0 -and $CurrentOwnedPids -contains $ExpectedPid -and
    $ObservedHwnd -eq $ExpectedHwnd -and $ObservedPid -eq $ExpectedPid)
}
