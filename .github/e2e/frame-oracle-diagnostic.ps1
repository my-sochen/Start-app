# frame-oracle-diagnostic.ps1 - DIAGNOSTIC ONLY, not part of the validation gate.
# Controlled WPF comparison to establish (or refute) a measurable bottom-edge signature
# that separates BorderThickness 1,8,1,1 (variant A, the product frame) from 1,8,1,0
# (variant B, no bottom frame).
#
# FIDELITY LABELS (honest bounds - this is a SIMPLIFIED harness, not an exact product
# replica; reviewer CR 17:39:43):
# - Same as product preview (live 771a7f62 source): Border subclass exposing a
#   FrameworkElementAutomationPeer (same as PreviewBorder), Height=90, CornerRadius=4,
#   Margin bottom 8, BorderBrush #BFDBFE frozen SolidColorBrush (live converters freeze),
#   ImageBrush Stretch=UniformToFill with a 32x32 red (220,30,30) probe PNG (live probe
#   size) and default BitmapImage cache option (live default), white popup Padding=16
#   CornerRadius=4 MinWidth=440, #66000000 overlay, decorated 900x560 window.
# - DIFFERENT from product: popup holds ONLY the preview (live popup stacks title,
#   buttons, labels and controls), and the popup sits at exact fractional Canvas offsets
#   instead of live centering. Every case runs at FOUR requested Y offsets
#   (0.00/0.25/0.50/0.75 DIP) to sample the anti-aliasing regime; the observed
#   fractional physical origin is recorded per case (observedFracY/observedFracX).
#   Separation is reported ONLY across the observed sampled origins - no product-oracle
#   transfer or phase-independence conclusion follows from four requested offsets.
#   X offset is not swept; five central columns sample X within each requested offset.
# - bluebg/whitebg are BRUSH CONTROLS that RETAIN the A/B border thickness. They are NOT
#   the product no-image scenario (the live FrameThicknessConverter returns Thickness 0
#   when no image file exists). bluebg is expected A~B ambiguous because frame color ==
#   background color; it documents the ambiguous region, it cannot prove anything.
# - Runs on Windows PowerShell 5.1 (.NET Framework WPF). A/B share the same render engine
#   inside one session, which controls engine-level differences; this run is NOT product
#   runtime proof - the final proof remains the gate run against the real app.
#
# Records per case: fractional UIA BoundingRectangle, BOTH integer crop mappings
# (rounded = gate-equivalent PowerShell [int] midpoint-to-even via Convert.ToInt32,
# truncated = raw cast; the capture uses the rounded one), DPI scale, per-column
# (5 central columns) top-12 and bottom-6 pixel rows, solid band counts, and an advisory
# candidate transition signature (second-last row red-dominant R-B>=60 AND last row
# blue-dominant B-R>=20 across all 5 columns). Data collection only; exits nonzero only
# on harness errors. Advisory verdicts must not become a gate from a single A pass.
param([string]$OutDir = 'oracle-out')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

Add-Type -AssemblyName System.Drawing
$probe = Join-Path (Resolve-Path $OutDir).Path 'probe-red.png'
$pb = New-Object System.Drawing.Bitmap 32, 32
$pg = [System.Drawing.Graphics]::FromImage($pb)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 220, 30, 30))
$pg.Dispose()
$pb.Save($probe, [System.Drawing.Imaging.ImageFormat]::Png)
$pb.Dispose()

$cs = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;

public class PeerBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new FrameworkElementAutomationPeer(this);
    }
}

public static class OracleHarness
{
    private static string _outDir;
    private static string _probe;
    private static readonly List<string> _errors = new List<string>();
    private static readonly Dictionary<string, Dictionary<string, string>> _cases =
        new Dictionary<string, Dictionary<string, string>>();

    private static bool IsFrame(SD.Color c)
    {
        return Math.Abs(c.R - 191) <= 12 && Math.Abs(c.G - 219) <= 12 && Math.Abs(c.B - 254) <= 12;
    }
    private static string Rgb(SD.Color c) { return "(" + c.R + "," + c.G + "," + c.B + ")"; }

    public static string Run(string outDir, string probePath)
    {
        _outDir = outDir;
        _probe = probePath;
        var t = new Thread(RunAll);
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (_errors.Count > 0)
        {
            File.WriteAllText(Path.Combine(_outDir, "errors.txt"), string.Join("\r\n", _errors.ToArray()));
            return "HARNESS-ERRORS: " + _errors.Count;
        }
        return "HARNESS-OK";
    }

    private static void Pump(Window w)
    {
        w.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
    }

    private static void RunAll()
    {
        var sb = new StringBuilder();
        double[] phases = { 0.0, 0.25, 0.5, 0.75 };
        string[] controls = { "redimage", "redsolid", "bluebg", "whitebg" };
        foreach (double phase in phases)
        {
            string ph = phase.ToString("0.00");
            Window w = null;
            try
            {
                w = new Window();
                w.Title = "oracle-phase-" + ph;
                w.Width = 900; w.Height = 560; w.Left = 100; w.Top = 100;
                w.WindowStyle = WindowStyle.SingleBorderWindow; // decorated, like the live app window
                w.ResizeMode = ResizeMode.NoResize;
                w.Topmost = true;
                var overlay = new Grid();
                overlay.Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
                var canvas = new Canvas();
                var popup = new Border();
                popup.Background = Brushes.White;
                popup.Padding = new Thickness(16);
                popup.CornerRadius = new CornerRadius(4);
                popup.MinWidth = 440; // live popup property shape (MinWidth, not fixed Width)
                var stack = new StackPanel();
                var pv = new PeerBorder();
                pv.Height = 90;
                pv.CornerRadius = new CornerRadius(4);
                pv.Margin = new Thickness(0, 0, 0, 8);
                var frameBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254));
                frameBrush.Freeze(); // live ColorBrushConverter freezes its brushes
                pv.BorderBrush = frameBrush;
                AutomationProperties.SetName(pv, "Card design preview");
                stack.Children.Add(pv);
                popup.Child = stack;
                Canvas.SetLeft(popup, 230);
                Canvas.SetTop(popup, 150 + phase); // exact fractional Y origin phase
                canvas.Children.Add(popup);
                overlay.Children.Add(canvas);
                w.Content = overlay;
                w.Show();
                Pump(w);
                Thread.Sleep(400);
                Pump(w);

                var src = PresentationSource.FromVisual(w);
                double sx = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : -1;
                double sy = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M22 : -1;

                AutomationElement pvEl = null;
                var root = AutomationElement.FromHandle(new WindowInteropHelper(w).Handle);
                for (int i = 0; i < 20 && pvEl == null; i++)
                {
                    pvEl = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, "Card design preview"));
                    if (pvEl == null) Thread.Sleep(150);
                }
                if (pvEl == null) throw new Exception("UIA peer for preview not found");

                foreach (string variant in new[] { "A", "B" })
                {
                    pv.BorderThickness = variant == "A" ? new Thickness(1, 8, 1, 1) : new Thickness(1, 8, 1, 0);
                    foreach (string control in controls)
                    {
                        string name = "p" + ph + "-" + variant + "-" + control;
                        try { SetBackground(pv, control); Pump(w); Thread.Sleep(250); Pump(w); Capture(pvEl, sx, sy, phase, variant, control, name, sb); }
                        catch (Exception ex) { _errors.Add(name + ": " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { _errors.Add("phase " + ph + ": " + ex.Message); }
            finally { if (w != null) { try { w.Close(); } catch { } } Thread.Sleep(200); }
        }

        sb.AppendLine("=== advisory A/B separation verdicts (data-derived, NOT a gate) ===");
        sb.AppendLine("(signature: second-last row red-dominant R-B>=60 AND last row blue-dominant B-R>=20, all 5 columns)");
        sb.AppendLine("Separation is reported across the observed sampled origins ONLY; no product-oracle transfer or phase-independence conclusion follows.");
        var origins = new HashSet<string>();
        foreach (var kv in _cases) { Dictionary<string, string> vv = kv.Value; if (vv.ContainsKey("uiaRect")) origins.Add(vv["uiaRect"]); }
        sb.AppendLine("unique observed physical UIA rects: " + origins.Count + " across " + _cases.Count + " cases (quantized duplicates count as repeated observed samples, not as distinct phases)");
        foreach (var c in controls)
        {
            bool aAll = true, bAny = false;
            var perPhase = new StringBuilder();
            foreach (double phase in phases)
            {
                string ph = phase.ToString("0.00");
                Dictionary<string, string> a, b;
                if (!_cases.TryGetValue("p" + ph + "-A-" + c, out a) || !_cases.TryGetValue("p" + ph + "-B-" + c, out b)) { aAll = false; continue; }
                bool aT = a["transAllCols"] == "True";
                bool bT = b["transAllCols"] == "True";
                if (!aT) aAll = false;
                if (bT) bAny = true;
                perPhase.Append(" [" + ph + ": A=" + aT + " B=" + bT + " lastA=" + a["lastRow"] + " lastB=" + b["lastRow"] + " secA=" + a["secondLastRow"] + " secB=" + b["secondLastRow"] + " botSolidA=" + a["bottomSolid"] + " botSolidB=" + b["bottomSolid"] + "]");
            }
            bool boundsMatch = true;
            foreach (double phase in phases)
            {
                Dictionary<string, string> a2, b2;
                if (_cases.TryGetValue("p" + phase.ToString("0.00") + "-A-" + c, out a2) && _cases.TryGetValue("p" + phase.ToString("0.00") + "-B-" + c, out b2))
                { if (a2["uiaRect"] != b2["uiaRect"]) boundsMatch = false; }
            }
            sb.AppendLine(c + ": A-signature-at-all-requested-samples=" + aAll + " B-signature-at-any-requested-sample=" + bAny
                + " SEPARATES-observed-samples-only=" + (aAll && !bAny) + " boundsMatchAB=" + boundsMatch + perPhase);
        }
        File.WriteAllText(Path.Combine(_outDir, "oracle-report.txt"), sb.ToString());
        var js = new StringBuilder();
        js.Append("{");
        bool first = true;
        foreach (var kv in _cases)
        {
            if (!first) js.Append(",");
            first = false;
            js.Append("\"" + kv.Key + "\":{");
            bool f2 = true;
            foreach (var f in kv.Value)
            {
                if (!f2) js.Append(",");
                f2 = false;
                js.Append("\"" + f.Key + "\":\"" + f.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }
            js.Append("}");
        }
        js.Append("}");
        File.WriteAllText(Path.Combine(_outDir, "oracle-report.json"), js.ToString());
    }

    private static void SetBackground(PeerBorder pv, string control)
    {
        if (control == "redimage")
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(_probe); bi.EndInit(); // default CacheOption, like live
            var ib = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
            ib.Freeze();
            pv.Background = ib;
        }
        else if (control == "redsolid") { var b = new SolidColorBrush(Color.FromRgb(220, 30, 30)); b.Freeze(); pv.Background = b; }
        else if (control == "bluebg") { var b = new SolidColorBrush(Color.FromRgb(191, 219, 254)); b.Freeze(); pv.Background = b; }
        else pv.Background = Brushes.White;
    }

    private static void Capture(AutomationElement pvEl, double sx, double sy, double phase, string variant, string control, string name, StringBuilder sb)
    {
        Rect r = pvEl.Current.BoundingRectangle;
        // Gate-equivalent mapping: the gate test (PowerShell) converts with [int]$rect.X, which
        // ROUNDS midpoint-to-even; Convert.ToInt32(double) rounds identically. A raw (int) cast
        // truncates and can pick a different physical row, so BOTH mappings are recorded and the
        // capture uses the rounded (gate-equivalent) one.
        int x = Convert.ToInt32(r.Left); int y = Convert.ToInt32(r.Top);
        int cw = Convert.ToInt32(r.Width); int ch = Convert.ToInt32(r.Height);
        int xt = (int)r.Left; int yt = (int)r.Top; int cwt = (int)r.Width; int cht = (int)r.Height;
        if (cw <= 0 || ch <= 0) throw new Exception("empty crop rect " + r);

        using (var bmp = new SD.Bitmap(cw, ch))
        {
            using (var gg = SD.Graphics.FromImage(bmp))
                gg.CopyFromScreen(x, y, 0, 0, bmp.Size);
            bmp.Save(Path.Combine(_outDir, "crop-" + name + ".png"), SD.Imaging.ImageFormat.Png);

            int[] cols = { 20, cw / 4, cw / 2, (3 * cw) / 4, cw - 21 };
            var m = new Dictionary<string, string>();
            m["variant"] = variant == "A" ? "A (1,8,1,1)" : "B (1,8,1,0)";
            m["control"] = control;
            m["requestedYOffsetDIP"] = phase.ToString("0.00");
            m["observedFracY"] = (r.Top - Math.Floor(r.Top)).ToString("0.###");
            m["observedFracX"] = (r.Left - Math.Floor(r.Left)).ToString("0.###");
            m["uiaRect"] = r.Left.ToString("0.###") + "," + r.Top.ToString("0.###") + " " + r.Width.ToString("0.###") + "x" + r.Height.ToString("0.###");
            m["cropIntRoundedGateEquivalent"] = x + "," + y + " " + cw + "x" + ch;
            m["cropIntTruncated"] = xt + "," + yt + " " + cwt + "x" + cht;
            m["cropMappingDiffers"] = (x != xt || y != yt || cw != cwt || ch != cht).ToString();
            m["dpiScale"] = sx.ToString("0.###") + "," + sy.ToString("0.###");

            int topSolid = 0;
            for (int yy = 0; yy < ch; yy++) { if (IsFrame(bmp.GetPixel(cw / 2, yy))) topSolid++; else break; }
            int bottomSolid = 0;
            for (int yy = ch - 1; yy >= 0; yy--) { if (IsFrame(bmp.GetPixel(cw / 2, yy))) bottomSolid++; else break; }
            m["topSolid"] = topSolid.ToString();
            m["bottomSolid"] = bottomSolid.ToString();

            var lastC = bmp.GetPixel(cw / 2, ch - 1);
            var secC = bmp.GetPixel(cw / 2, ch - 2);
            m["lastRow"] = Rgb(lastC);
            m["secondLastRow"] = Rgb(secC);

            bool transAll = true;
            var rowDump = new StringBuilder();
            foreach (var col in cols)
            {
                var lc = bmp.GetPixel(col, ch - 1);
                var sc = bmp.GetPixel(col, ch - 2);
                bool blueDomLast = (lc.B - lc.R) >= 20;
                bool redDomSecond = (sc.R - sc.B) >= 60;
                if (!(blueDomLast && redDomSecond)) transAll = false;
                rowDump.Append("col" + col + ":");
                for (int yy = 0; yy < Math.Min(12, ch); yy++) rowDump.Append(" t" + yy + Rgb(bmp.GetPixel(col, yy)));
                for (int yy = Math.Max(0, ch - 6); yy < ch; yy++) rowDump.Append(" b" + (ch - 1 - yy) + Rgb(bmp.GetPixel(col, yy)));
                rowDump.Append(";");
            }
            m["transAllCols"] = transAll.ToString();
            m["rows"] = rowDump.ToString();
            _cases[name] = m;

            sb.AppendLine("=== case " + name + " ===");
            foreach (var f in m) sb.AppendLine("  " + f.Key + " = " + f.Value);
        }
    }
}
'@

Add-Type -TypeDefinition $cs -ReferencedAssemblies PresentationFramework, PresentationCore, WindowsBase, UIAutomationClient, UIAutomationTypes, System.Drawing
$result = [OracleHarness]::Run((Resolve-Path $OutDir).Path, $probe)
Get-Content (Join-Path $OutDir 'oracle-report.txt') -ErrorAction SilentlyContinue
$result
if ($result -ne 'HARNESS-OK') { exit 1 }
