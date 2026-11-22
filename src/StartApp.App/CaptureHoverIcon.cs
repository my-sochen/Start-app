using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>
/// The capture button's hover animation (beta, owner-approved Nov 22 00:14).
/// Renders the approved UFO motion model (<see cref="CaptureHoverMotion"/>, a
/// direct port of the designer's make_anim.py generator) as real WPF vectors:
/// the logo ship bobs and tilts, its hull dots pulse, two solid wavy beams pour
/// into the hatch slot and boxes descend into it, in a seamless 1.8s loop.
/// Rest state (t = 0) shows the static ship only. Animation runs only while the
/// ancestor button is hovered; all motion stays inside the 64x64 icon bounds.
/// </summary>
public sealed class CaptureHoverIcon : Canvas
{
    private static readonly Brush NavyBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(CaptureHoverMotion.Navy)));
    private static readonly Brush WhiteBrush = Freeze(Brushes.White);

    private readonly Canvas _animatedRoot = new();
    private readonly Canvas _ship = new();
    private readonly Path _beamLeftGlow = BeamPath(CaptureHoverMotion.BeamGlowWidth);
    private readonly Path _beamRightGlow = BeamPath(CaptureHoverMotion.BeamGlowWidth);
    private readonly Path _beamLeft = BeamPath(CaptureHoverMotion.BeamWidth);
    private readonly Path _beamRight = BeamPath(CaptureHoverMotion.BeamWidth);
    private readonly Canvas _beams = new();
    private Path[] _dots = [];
    private readonly Path[] _boxes = { BoxPath(), BoxPath() };
    private readonly TranslateTransform _bob = new();
    private readonly RotateTransform _tilt = new();
    private readonly TranslateTransform[] _boxOffsets = { new(), new() };
    private readonly ScaleTransform[] _boxScales = { new(), new() };

    private readonly StreamGeometryCache _streamLeft = new();
    private readonly StreamGeometryCache _streamRight = new();

    private Button? _hoverSource;
    private bool _ticking;
    private Stopwatch? _clock;
    private double _phase;
    private long _lastTickMs;
    private bool _easingOut;
    private double _easeOutRate = 1.0;
    private double _easeOutTarget;

    public CaptureHoverIcon()
    {
        Width = 64;
        Height = 64;
        ClipToBounds = true;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { StopTicking(); _phase = 0; RenderFrameAt(0); }
            else if (_hoverSource?.IsMouseOver == true) StartTicking();
        };

        _beams.Children.Add(_beamLeftGlow);
        _beams.Children.Add(_beamRightGlow);
        _beams.Children.Add(_beamLeft);
        _beams.Children.Add(_beamRight);
        _beams.Opacity = 0;
        _beamLeftGlow.Opacity = CaptureHoverMotion.BeamGlowOpacity;
        _beamRightGlow.Opacity = CaptureHoverMotion.BeamGlowOpacity;
        _beamLeftGlow.Data = _streamLeft.Geometry;
        _beamLeft.Data = _streamLeft.Geometry;
        _beamRightGlow.Data = _streamRight.Geometry;
        _beamRight.Data = _streamRight.Geometry;

        for (var i = 0; i < _boxes.Length; i++)
        {
            // translate(x y) scale(s): scale around the box origin first, then move it.
            var transforms = new TransformGroup();
            transforms.Children.Add(_boxScales[i]);
            transforms.Children.Add(_boxOffsets[i]);
            _boxes[i].RenderTransform = transforms;
            _boxes[i].Opacity = 0;
        }

        BuildShip();

        _animatedRoot.RenderTransform = new TransformGroup { Children = { _tilt, _bob } };
        _animatedRoot.Children.Add(_beams);
        foreach (var box in _boxes) _animatedRoot.Children.Add(box);
        _animatedRoot.Children.Add(_ship);
        Children.Add(_animatedRoot);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RenderFrameAt(0);
    }

    private void BuildShip()
    {
        // Static ship line-art in ship space, placed like the logo: bottom of the icon.
        var body = new GeometryGroup();
        body.Children.Add(new EllipseGeometry(new Point(119, 34), 106, 31));
        body.Children.Add(Geometry.Parse("M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56"));
        _ship.Children.Add(new Path
        {
            Data = body,
            Stroke = NavyBrush,
            StrokeThickness = 15,
            Opacity = 0.14,
        });
        _ship.Children.Add(new Path
        {
            Data = new EllipseGeometry(new Point(119, 34), 106, 31),
            Stroke = NavyBrush,
            StrokeThickness = 9.5,
        });
        _ship.Children.Add(new Path
        {
            Data = Geometry.Parse("M48 56 C73 68 88 97 119 97 C150 97 165 68 190 56"),
            Stroke = NavyBrush,
            StrokeThickness = 9,
        });
        _ship.Children.Add(new Path
        {
            Data = new EllipseGeometry(new Point(118, 25), 25, 8),
            Fill = WhiteBrush,
            Stroke = NavyBrush,
            StrokeThickness = 7,
        });
        _dots = CaptureHoverMotion.Dots
            .Select(d => new Path
            {
                Data = new EllipseGeometry(new Point(d.Cx, d.Cy), d.R, d.R),
                Fill = NavyBrush,
            })
            .ToArray();
        foreach (var dot in _dots) _ship.Children.Add(dot);

        var place = new TransformGroup();
        place.Children.Add(new ScaleTransform(CaptureHoverMotion.ShipScale, CaptureHoverMotion.ShipScale));
        place.Children.Add(new TranslateTransform(CaptureHoverMotion.ShipLeftX, CaptureHoverMotion.ShipTopY));
        _ship.RenderTransform = place;
    }

    /// <summary>Renders one exact frame. Used by the hover ticker and by the synthetic UI capture harness.</summary>
    public void RenderFrameAt(double t)
    {
        _bob.Y = CaptureHoverMotion.BobDy(t);
        _tilt.Angle = CaptureHoverMotion.TiltDeg(t);
        _tilt.CenterX = CaptureHoverMotion.TiltCenter.X;
        _tilt.CenterY = CaptureHoverMotion.TiltCenter.Y;

        _beams.Opacity = CaptureHoverMotion.BeamOpacity(t);
        if (_beams.Opacity > 0)
        {
            _streamLeft.Update(CaptureHoverMotion.StreamLeftStart, CaptureHoverMotion.StreamLeftEnd, CaptureHoverMotion.StreamLeftAmp, t);
            _streamRight.Update(CaptureHoverMotion.StreamRightStart, CaptureHoverMotion.StreamRightEnd, CaptureHoverMotion.StreamRightAmp, t);
        }

        for (var i = 0; i < _boxes.Length; i++)
        {
            var (opacity, x, y, scale) = i == 0
                ? CaptureHoverMotion.BoxState(t, CaptureHoverMotion.Box1Top, CaptureHoverMotion.Box1End, CaptureHoverMotion.Box1T0)
                : CaptureHoverMotion.BoxState(t, CaptureHoverMotion.Box2Top, CaptureHoverMotion.Box2End, CaptureHoverMotion.Box2T0);
            _boxes[i].Opacity = opacity;
            _boxOffsets[i].X = x;
            _boxOffsets[i].Y = y;
            _boxScales[i].ScaleX = scale;
            _boxScales[i].ScaleY = scale;
        }

        for (var i = 0; i < _dots.Length; i++)
            _dots[i].Opacity = CaptureHoverMotion.DotOpacity(t, CaptureHoverMotion.DotPhases[i]);
    }

    // Reuses one PathGeometry per beam and rewrites the bezier points in place each
    // frame; the segment count is fixed, so the rendered geometries keep a stable identity.
    private sealed class StreamGeometryCache
    {
        public readonly PathGeometry Geometry = new();
        private PolyBezierSegment? _segment;

        public void Update((double X, double Y) start, (double X, double Y) end, double amp, double t)
        {
            var segments = CaptureHoverMotion.StreamSegments(start, end, amp, t);
            if (_segment is null || _segment.Points.Count != segments.Count * 3)
            {
                var figure = new PathFigure { StartPoint = new Point(start.X, start.Y) };
                _segment = new PolyBezierSegment();
                figure.Segments.Add(_segment);
                Geometry.Figures.Clear();
                Geometry.Figures.Add(figure);
            }
            else
            {
                Geometry.Figures[0].StartPoint = new Point(start.X, start.Y);
            }
            var points = _segment.Points;
            points.Clear();
            foreach (var (c1, c2, segEnd) in segments)
            {
                points.Add(new Point(c1.X, c1.Y));
                points.Add(new Point(c2.X, c2.Y));
                points.Add(new Point(segEnd.X, segEnd.Y));
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hoverSource = FindAncestorButton();
        if (_hoverSource is null) return;
        DependencyPropertyDescriptor.FromProperty(IsMouseOverProperty, typeof(Button))
            .AddValueChanged(_hoverSource, OnHoverChanged);
        if (_hoverSource.IsMouseOver) StartTicking();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopTicking();
        if (_hoverSource is not null)
        {
            DependencyPropertyDescriptor.FromProperty(IsMouseOverProperty, typeof(Button))
                .RemoveValueChanged(_hoverSource, OnHoverChanged);
            _hoverSource = null;
        }
    }

    private void OnHoverChanged(object? sender, EventArgs e)
    {
        if (_hoverSource?.IsMouseOver == true)
        {
            _easingOut = false;
            StartTicking();
        }
        else if (_ticking)
        {
            // Ease out to the next rest pose (t wraps to 0) instead of snapping back.
            var loop = CaptureHoverMotion.LoopSeconds;
            var remaining = loop - (_phase % loop);
            if (remaining >= loop - 0.001 || remaining <= 0.001)
            {
                _easingOut = false;
                StopTicking();
                RenderFrameAt(0);
                return;
            }
            _easeOutTarget = _phase + remaining;
            _easeOutRate = remaining / 0.45; // constant ~450 ms settle, slight speed change only
            _easingOut = true;
        }
    }

    private void StartTicking()
    {
        if (_ticking || !IsVisible) return;
        _ticking = true;
        _clock = Stopwatch.StartNew();
        _lastTickMs = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
        _clock = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_clock is null) return;
        var nowMs = _clock.ElapsedMilliseconds;
        var dt = _lastTickMs == 0 ? 0 : (nowMs - _lastTickMs) / 1000.0;
        _lastTickMs = nowMs;
        _phase += dt * (_easingOut ? _easeOutRate : 1.0);
        if (_easingOut && _phase >= _easeOutTarget)
        {
            _easingOut = false;
            _phase = 0;
            StopTicking();
            RenderFrameAt(0);
            return;
        }
        RenderFrameAt(_phase % CaptureHoverMotion.LoopSeconds);
    }

    private Button? FindAncestorButton()
    {
        DependencyObject current = this;
        while (VisualTreeHelper.GetParent(current) is { } parent)
        {
            if (parent is Button button) return button;
            current = parent;
        }
        return null;
    }

    private static Path BeamPath(double thickness) => new()
    {
        Stroke = NavyBrush,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static Path BoxPath() => new()
    {
        Data = new RectangleGeometry(new Rect(-5, -5, 10, 10), 1.8, 1.8),
        Fill = WhiteBrush,
        Stroke = NavyBrush,
        StrokeThickness = 1.7,
    };

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
