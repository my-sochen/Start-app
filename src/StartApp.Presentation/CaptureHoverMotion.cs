namespace StartApp.Presentation;

/// <summary>
/// Pure motion model for the capture-button hover animation (beta, owner-approved
/// Nov 22 00:14 "מעולה זה טוב"). Direct port of the designer's generator
/// (make_anim.py, frozen with the approved capture-hover.svg handoff): same
/// constants, same math, same 1.8s seamless loop in a 64x64 icon space.
/// No WPF dependency so the model is unit-testable on any platform; the WPF
/// control (StartApp.App CaptureHoverIcon) only renders what this model returns.
/// </summary>
public static class CaptureHoverMotion
{
    public const double LoopSeconds = 1.8;
    public const string Navy = "#142743";

    // Ship placement inside the 64x64 icon space (logo layout: ship at the bottom).
    public const double ShipScale = 0.216;
    public const double ShipTopY = 40.0;
    public static readonly double ShipLeftX = 32 - 119 * ShipScale;

    // Beam streams: splayed centerline plus a perpendicular sine wiggle that
    // travels down into the hatch; the sin(pi*s) envelope pins both ends exactly.
    public static readonly (double X, double Y) StreamLeftStart = (16, 9);
    public static readonly (double X, double Y) StreamLeftEnd = (28.8, 43.0);
    public const double StreamLeftAmp = 2.0;
    public static readonly (double X, double Y) StreamRightStart = (48, 9);
    public static readonly (double X, double Y) StreamRightEnd = (34.8, 43.0);
    public const double StreamRightAmp = -2.0;
    public const double WiggleCycles = 1.25;

    // Boxes descend on straight uniform paths into the hatch.
    public static readonly (double X, double Y) Box1Top = (22, 9);
    public static readonly (double X, double Y) Box1End = (31, 44);
    public const double Box1T0 = 0.12;
    public static readonly (double X, double Y) Box2Top = (42, 9);
    public static readonly (double X, double Y) Box2End = (33, 44);
    public const double Box2T0 = 0.72;
    public const double BoxDuration = 0.85;

    public const double BobAmplitude = 2.0;
    public const double TiltAmplitudeDeg = 1.0;
    public const double TiltPhase = 1.2;
    public static readonly (double X, double Y) TiltCenter = (32, 40);

    // Hull dots (ship space), pixel-measured from the approved logo.
    public static readonly (double Cx, double Cy, double R)[] Dots =
        { (72.5, 41.8, 7), (119.1, 47.6, 7), (166.5, 41.0, 7) };
    public static readonly double[] DotPhases = { 0.0, 2.1, 4.2 };

    public const double BeamGlowWidth = 4.6;
    public const double BeamGlowOpacity = 0.18;
    public const double BeamWidth = 2.6;

    private static double Wrap(double t) => ((t % LoopSeconds) + LoopSeconds) % LoopSeconds;

    private static double Smooth(double a, double b, double x)
    {
        if (x <= a) return 0.0;
        if (x >= b) return 1.0;
        var f = (x - a) / (b - a);
        return f * f * (3 - 2 * f);
    }

    /// <summary>Reference float profile: rest, ease up, hold, long easy descent. 0..1..0 over the loop.</summary>
    public static double FloatProfile(double t)
    {
        var u = Wrap(t) / LoopSeconds;
        return Smooth(0.05, 0.42, u) - Smooth(0.55, 0.95, u);
    }

    /// <summary>Vertical bob of the whole icon (negative y: the ship floats up).</summary>
    public static double BobDy(double t) => -BobAmplitude * FloatProfile(t);

    public static double TiltDeg(double t) => TiltAmplitudeDeg * Math.Sin(2 * Math.PI * t / LoopSeconds + TiltPhase);

    public static double DotOpacity(double t, double phase) =>
        0.72 + 0.28 * (0.5 + 0.5 * Math.Sin(4 * Math.PI * t / LoopSeconds + phase));

    /// <summary>Beam visibility: strongest at rest, dims/retracts while the ship floats.</summary>
    public static double BeamOpacity(double t)
    {
        t = Wrap(t);
        var baseOp = 0.85 * Smooth(0.03, 0.25, t) * (1 - Smooth(1.58, 1.78, t));
        return baseOp * (1 - 0.35 * FloatProfile(t));
    }

    /// <summary>One point on a beam stream: s in [0,1] along the centerline, wiggle travels down with t.</summary>
    public static (double X, double Y) StreamPoint(
        (double X, double Y) start, (double X, double Y) end, double amp, double s, double t)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var nx = -dy / length;
        var ny = dx / length;
        var w = amp * Math.Sin(2 * Math.PI * WiggleCycles * s - 2 * Math.PI * t / LoopSeconds) * Math.Sin(Math.PI * s);
        return (start.X + dx * s + nx * w, start.Y + dy * s + ny * w);
    }

    /// <summary>
    /// Beam centerline as Catmull-Rom-interpolated cubic segments (same 9-sample
    /// construction as the generator). Returns (control1, control2, end) triples;
    /// the first segment starts at StreamPoint(s=0).
    /// </summary>
    public static IReadOnlyList<((double X, double Y) C1, (double X, double Y) C2, (double X, double Y) End)> StreamSegments(
        (double X, double Y) start, (double X, double Y) end, double amp, double t, int samples = 9)
    {
        var pts = new (double X, double Y)[samples];
        for (var i = 0; i < samples; i++)
            pts[i] = StreamPoint(start, end, amp, i / (double)(samples - 1), t);
        var segments = new List<((double, double), (double, double), (double, double))>(samples - 1);
        for (var i = 0; i < samples - 1; i++)
        {
            var prev = pts[Math.Max(i - 1, 0)];
            var p0 = pts[i];
            var p1 = pts[i + 1];
            var next = pts[Math.Min(i + 2, samples - 1)];
            var c1 = (p0.X + (p1.X - prev.X) / 6.0, p0.Y + (p1.Y - prev.Y) / 6.0);
            var c2 = (p1.X - (next.X - p0.X) / 6.0, p1.Y - (next.Y - p0.Y) / 6.0);
            segments.Add((c1, c2, p1));
        }
        return segments;
    }

    /// <summary>Box state: opacity, center position and uniform scale at time t. Invisible before t0 and after landing.</summary>
    public static (double Opacity, double X, double Y, double Scale) BoxState(
        double t, (double X, double Y) top, (double X, double Y) end, double t0)
    {
        var p = (Wrap(t) - t0) / BoxDuration;
        if (p <= 0) return (0.0, top.X, top.Y, 1.0);
        if (p >= 1) return (0.0, end.X, end.Y, 0.15);
        var x = top.X + (end.X - top.X) * p;
        var y = top.Y + (end.Y - top.Y) * p;
        var scale = 1 - 0.85 * p;
        var opacity = Smooth(0.0, 0.10, p) * (1 - Smooth(0.78, 0.99, p));
        return (opacity, x, y, scale);
    }
}
