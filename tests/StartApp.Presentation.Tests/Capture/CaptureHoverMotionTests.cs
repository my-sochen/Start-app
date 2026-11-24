namespace StartApp.Presentation.Tests;

/// <summary>
/// Pins the capture-button hover motion model to the designer's approved generator
/// (make_anim.py, frozen with the owner-approved capture-hover.svg, Nov 22 00:14).
/// Reference values below were computed from that generator itself.
/// </summary>
public class CaptureHoverMotionTests
{
    private const double T = CaptureHoverMotion.LoopSeconds;

    [Fact]
    public void Loop_is_seamless_at_every_sampled_time()
    {
        foreach (var t in new[] { 0.0, 0.13, 0.45, 0.9, 1.35, 1.79 })
        {
            Assert.Equal(CaptureHoverMotion.BobDy(t), CaptureHoverMotion.BobDy(t + T), 10);
            Assert.Equal(CaptureHoverMotion.TiltDeg(t), CaptureHoverMotion.TiltDeg(t + T), 10);
            Assert.Equal(CaptureHoverMotion.BeamOpacity(t), CaptureHoverMotion.BeamOpacity(t + T), 10);
            foreach (var phase in CaptureHoverMotion.DotPhases)
                Assert.Equal(CaptureHoverMotion.DotOpacity(t, phase), CaptureHoverMotion.DotOpacity(t + T, phase), 10);
            var a = CaptureHoverMotion.StreamSegments(CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp, t);
            var b = CaptureHoverMotion.StreamSegments(CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp, t + T);
            Assert.Equal(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].C1.X, b[i].C1.X, 10);
                Assert.Equal(a[i].C2.Y, b[i].C2.Y, 10);
                Assert.Equal(a[i].End.X, b[i].End.X, 10);
                Assert.Equal(a[i].End.Y, b[i].End.Y, 10);
            }
        }
    }

    [Fact]
    public void Rest_frame_hides_beams_and_boxes_and_shows_static_ship()
    {
        Assert.Equal(0.0, CaptureHoverMotion.BeamOpacity(0), 10);
        Assert.Equal(0.0, CaptureHoverMotion.BoxState(0, CaptureHoverMotion.Box1Top, CaptureHoverMotion.Box1End, CaptureHoverMotion.Box1T0).Opacity, 10);
        Assert.Equal(0.0, CaptureHoverMotion.BoxState(0, CaptureHoverMotion.Box2Top, CaptureHoverMotion.Box2End, CaptureHoverMotion.Box2T0).Opacity, 10);
        Assert.Equal(0.0, CaptureHoverMotion.BobDy(0), 6);
    }

    // Exact reference frames from make_anim.py (the approved generator).
    [Theory]
    [InlineData(0.45, -1.121355, 0.362358, 0.683198)]
    [InlineData(0.9, -2.0, -0.932039, 0.5525)]
    [InlineData(1.35, -1.0, -0.362358, 0.70125)]
    public void Frame_values_match_generator(double t, double bob, double tilt, double beamOpacity)
    {
        Assert.True(Math.Abs(bob - CaptureHoverMotion.BobDy(t)) < 1e-6, $"bob at {t}");
        Assert.True(Math.Abs(tilt - CaptureHoverMotion.TiltDeg(t)) < 1e-6, $"tilt at {t}");
        Assert.True(Math.Abs(beamOpacity - CaptureHoverMotion.BeamOpacity(t)) < 1e-6, $"beam opacity at {t}");
    }

    [Theory]
    [InlineData(0.0, 0.86, 0.980849, 0.737979)]
    [InlineData(0.45, 0.86, 0.739151, 0.982021)]
    public void Dot_pulses_match_generator(double t, double d0, double d1, double d2)
    {
        Assert.Equal(d0, CaptureHoverMotion.DotOpacity(t, 0.0), 4);
        Assert.Equal(d1, CaptureHoverMotion.DotOpacity(t, 2.1), 4);
        Assert.Equal(d2, CaptureHoverMotion.DotOpacity(t, 4.2), 4);
    }

    [Theory]
    [InlineData(0.45, 1.0, 25.494118, 22.588235, 0.67)]
    [InlineData(0.9, 0.274321, 30.258824, 41.117647, 0.22)]
    public void Box_descent_matches_generator(double t, double opacity, double x, double y, double scale)
    {
        var box = CaptureHoverMotion.BoxState(t, CaptureHoverMotion.Box1Top, CaptureHoverMotion.Box1End, CaptureHoverMotion.Box1T0);
        Assert.Equal(opacity, box.Opacity, 4);
        Assert.Equal(x, box.X, 4);
        Assert.Equal(y, box.Y, 4);
        Assert.Equal(scale, box.Scale, 4);
    }

    [Fact]
    public void Boxes_descend_on_a_straight_uniform_line()
    {
        foreach (var t in new[] { 0.3, 0.5, 0.7 })
        {
            var p = (t - CaptureHoverMotion.Box1T0) / CaptureHoverMotion.BoxDuration;
            var box = CaptureHoverMotion.BoxState(t, CaptureHoverMotion.Box1Top, CaptureHoverMotion.Box1End, CaptureHoverMotion.Box1T0);
            Assert.Equal(CaptureHoverMotion.Box1Top.X + (CaptureHoverMotion.Box1End.X - CaptureHoverMotion.Box1Top.X) * p, box.X, 10);
            Assert.Equal(CaptureHoverMotion.Box1Top.Y + (CaptureHoverMotion.Box1End.Y - CaptureHoverMotion.Box1Top.Y) * p, box.Y, 10);
        }
    }

    [Fact]
    public void Beam_endpoints_stay_pinned_and_separated()
    {
        foreach (var t in new[] { 0.0, 0.45, 0.9, 1.35 })
        {
            foreach (var (start, end, amp) in new[]
            {
                (CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp),
                (CaptureHoverMotion.StreamRightStart, CaptureHoverMotion.StreamRightEnd, CaptureHoverMotion.StreamRightAmp),
            })
            {
                var atStart = CaptureHoverMotion.StreamPoint(start, end, amp, 0, t);
                var atEnd = CaptureHoverMotion.StreamPoint(start, end, amp, 1, t);
                Assert.True(Math.Abs(atStart.X - start.X) < 1e-9 && Math.Abs(atStart.Y - start.Y) < 1e-9, "stream start pinned");
                Assert.True(Math.Abs(atEnd.X - end.X) < 1e-9 && Math.Abs(atEnd.Y - end.Y) < 1e-9, "stream end pinned");
            }
        }
        // The two streams never meet: each ends over its own half of the hatch slot.
        Assert.True(CaptureHoverMotion.StreamRightEnd.X - CaptureHoverMotion.StreamLeftEnd.X > 5.9, "stream endpoints stay 6 icon px apart");
    }

    [Fact]
    public void Beam_midpoints_match_generator()
    {
        var left = CaptureHoverMotion.StreamPoint(CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp, 0.5, 0.9);
        Assert.Equal(21.07647157865082, left.X, 8);
        Assert.Equal(26.498269523331455, left.Y, 8);
        var right = CaptureHoverMotion.StreamPoint(CaptureHoverMotion.StreamRightStart, CaptureHoverMotion.StreamRightEnd, CaptureHoverMotion.StreamRightAmp, 0.5, 0.9);
        Assert.Equal(42.7183445053493, right.X, 8);
        Assert.Equal(26.51182786678267, right.Y, 8);
    }

    [Fact]
    public void Moving_parts_stay_inside_button_bounds()
    {
        // Spec: all motion inside the button bounds, min margin 1.5 icon px.
        // Covers the ported moving parts (beams incl. glow stroke, boxes incl. stroke);
        // the static ship itself is the approved logo placement, verified by the designer.
        for (var t = 0.0; t < T; t += 0.02)
        {
            for (var s = 0.0; s <= 1.0; s += 0.02)
            {
                foreach (var (start, end, amp) in new[]
                {
                    (CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp),
                    (CaptureHoverMotion.StreamRightStart, CaptureHoverMotion.StreamRightEnd, CaptureHoverMotion.StreamRightAmp),
                })
                {
                    var p = CaptureHoverMotion.StreamPoint(start, end, amp, s, t);
                    var half = CaptureHoverMotion.BeamGlowWidth / 2;
                    Assert.InRange(p.X, 1.5 + half, 62.5 - half);
                    Assert.InRange(p.Y, 1.5 + half, 62.5 - half);
                }
            }
            foreach (var (top, end, t0) in new[]
            {
                (CaptureHoverMotion.Box1Top, CaptureHoverMotion.Box1End, CaptureHoverMotion.Box1T0),
                (CaptureHoverMotion.Box2Top, CaptureHoverMotion.Box2End, CaptureHoverMotion.Box2T0),
            })
            {
                var box = CaptureHoverMotion.BoxState(t, top, end, t0);
                if (box.Opacity <= 0) continue;
                var halfExtent = 5 * box.Scale + 0.85; // half rect plus stroke
                Assert.InRange(box.X, 1.5 + halfExtent, 62.5 - halfExtent);
                Assert.InRange(box.Y, 1.5 + halfExtent, 62.5 - halfExtent);
            }
        }
    }

    [Fact]
    public void Ship_placement_and_dots_match_approved_spec()
    {
        Assert.Equal(0.216, CaptureHoverMotion.ShipScale, 10);
        Assert.Equal(40.0, CaptureHoverMotion.ShipTopY, 10);
        Assert.Equal(32 - 119 * 0.216, CaptureHoverMotion.ShipLeftX, 10);
        Assert.Equal(new[] { (72.5, 41.8, 7.0), (119.1, 47.6, 7.0), (166.5, 41.0, 7.0) }, CaptureHoverMotion.Dots);
    }
}
