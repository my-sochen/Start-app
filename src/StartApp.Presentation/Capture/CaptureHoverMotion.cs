namespace StartApp.Presentation;

/// <summary>Motion values for the blue traced UFO hover reference.</summary>
public static class CaptureHoverMotion
{
    public const double LoopSeconds = 2.6;
    public static double Smooth(double u) { u = Math.Clamp(u, 0, 1); return u*u*(3-2*u); }
    public static double Lift(double t, double e) => -46*(0.5-0.5*Math.Cos(2*Math.PI*t/LoopSeconds))*e;
    public static double Tilt(double t, double e) => 2.4*Math.Sin(2*Math.PI*t/LoopSeconds)*e;
    public static double BeamOpacity(double t, double e) => 1-0.18*(0.5-0.5*Math.Cos(2*Math.PI*t/LoopSeconds))*e;
    public static (double Offset, double Opacity) Cube(double t, double e, bool small)
    {
        var u = ((t/LoopSeconds + (small ? 0.5 : 0))%1+1)%1;
        return (e*((small ? -271 : -34)+451*u), 1-e*(1-Smooth(u/0.12)));
    }
    public static double Flow(double t) => ((t/1.3)%1+1)%1*440;
}
