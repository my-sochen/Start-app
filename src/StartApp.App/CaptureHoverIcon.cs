using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Renders the traced icon paths without substituting geometric approximations.</summary>
public sealed class CaptureHoverIcon : Canvas
{
    private readonly Canvas _all = Layer();
    private readonly Canvas _beam = Layer();
    private readonly Canvas _body = Layer();
    private readonly Canvas _cubes = Layer();
    private readonly TranslateTransform _lift = new();
    private readonly RotateTransform _tilt = new(0,627,760);
    private readonly TranslateTransform[] _cubeOffset = {new(),new()};
    private readonly Path[] _cube = new Path[2];
    private readonly LinearGradientBrush _flow = new()
    {
        MappingMode = BrushMappingMode.Absolute, StartPoint = new(0,250), EndPoint = new(0,690),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops = new() { new(Color.FromArgb(0,186,230,253),0),new(Color.FromArgb(191,186,230,253),0.5),new(Color.FromArgb(0,186,230,253),1) }
    };
    private readonly TranslateTransform _flowOffset = new();
    private readonly Path _flowPath;
    private readonly DropShadowEffect _beamGlow = new() {Color=Color.FromRgb(56,130,246),ShadowDepth=0};
    private readonly DropShadowEffect _bodyGlow = new() {Color=Color.FromRgb(56,130,246),ShadowDepth=0};
    private Button? _button;
    private bool _ticking, _hovered;
    private readonly Stopwatch _clock = new();
    private double _hoverStart, _transitionStart, _fromEnvelope, _envelope;
    public bool IsAnimationRunning => _ticking;
    public double HoverEnvelope => _envelope;
    public bool ReducedMotion => !SystemParameters.ClientAreaAnimation || ForceReducedMotion;
    public bool ForceReducedMotion { get; set; }

    public CaptureHoverIcon()
    {
        Width=Height=874; IsHitTestVisible=false; FlowDirection=FlowDirection.LeftToRight;
        var viewport=Layer(); viewport.RenderTransform=new TranslateTransform(-190,-190);
        var transform=new TransformGroup(); transform.Children.Add(_tilt);transform.Children.Add(_lift);_all.RenderTransform=transform;
        _beam.Children.Add(Shape(CaptureHoverGeometry.Body,Blue()));
        _flow.Transform=_flowOffset;
        _flowPath=Shape(CaptureHoverGeometry.Body,_flow);_beam.Children.Add(_flowPath);
        _beam.OpacityMask=Mask(false);_beam.Effect=_beamGlow;
        _body.Children.Add(Shape(CaptureHoverGeometry.Body,Blue()));_body.OpacityMask=Mask(true);_body.Effect=_bodyGlow;
        _cubes.Clip=new RectangleGeometry(new Rect(0,0,1300,662));
        for(int i=0;i<2;i++) {_cube[i]=Shape(i==0?CaptureHoverGeometry.LargeCube:CaptureHoverGeometry.SmallCube,Blue());_cube[i].RenderTransform=_cubeOffset[i];_cubes.Children.Add(_cube[i]);}
        _all.Children.Add(_beam);_all.Children.Add(_cubes);_all.Children.Add(_body);viewport.Children.Add(_all);Children.Add(viewport);
        Loaded+=OnLoaded;Unloaded+=OnUnloaded;
        IsVisibleChanged+=(_,_)=>{if(!IsVisible) Reset();else if(_button?.IsMouseOver==true) ChangeHover(true);};
        RenderFrameAt(0,0);
    }
    private static Canvas Layer()=>new(){Width=1300,Height=1300};
    private static LinearGradientBrush Blue()=>new(Color.FromRgb(29,78,216),Color.FromRgb(125,211,252),new Point(520,992),new Point(733,264)){MappingMode=BrushMappingMode.Absolute};
    private static Brush Mask(bool inverse)=>new LinearGradientBrush(inverse?Colors.Transparent:Colors.White,inverse?Colors.White:Colors.Transparent,new Point(0,600),new Point(0,690)){MappingMode=BrushMappingMode.Absolute};
    private static Path Shape(string data,Brush fill)=>new(){Data=Geometry.Parse("F0 "+data),Fill=fill};
    public void RenderFrameAt(double t,double envelope=1)
    {
        var e=Math.Clamp(envelope,0,1);_envelope=e;
        _lift.Y=CaptureHoverMotion.Lift(t,e);_tilt.Angle=CaptureHoverMotion.Tilt(t,e);
        _beam.Opacity=CaptureHoverMotion.BeamOpacity(t,e);_flowPath.Opacity=0.6*e;_flowOffset.Y=CaptureHoverMotion.Flow(t);
        for(int i=0;i<2;i++){var s=CaptureHoverMotion.Cube(t,e,i==1);_cubeOffset[i].Y=s.Offset;_cube[i].Opacity=s.Opacity;}
        _beamGlow.BlurRadius=20*e;_beamGlow.Opacity=0.6*e;
        _bodyGlow.BlurRadius=22*e;_bodyGlow.Opacity=0.4*e*(0.8+0.2*Math.Sin(2*Math.PI*t/2.6*2));
    }
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        DependencyObject current=this;
        while(VisualTreeHelper.GetParent(current) is {} parent){if(parent is Button b){_button=b;break;}current=parent;}
        if(_button==null)return;
        _button.MouseEnter+=Enter;_button.MouseLeave+=Leave;
        SystemParameters.StaticPropertyChanged+=SystemChanged;
        if(_button.IsMouseOver)ChangeHover(true);
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)
    {
        Reset();SystemParameters.StaticPropertyChanged-=SystemChanged;
        if(_button!=null){_button.MouseEnter-=Enter;_button.MouseLeave-=Leave;_button=null;}
    }
    private void Enter(object sender,System.Windows.Input.MouseEventArgs e)=>ChangeHover(true);
    private void Leave(object sender,System.Windows.Input.MouseEventArgs e)=>ChangeHover(false);
    private void SystemChanged(object? sender,PropertyChangedEventArgs e){if(ReducedMotion)Reset();else if(_button?.IsMouseOver==true)ChangeHover(true);}
    private void ChangeHover(bool hovered)
    {
        if(ReducedMotion){Reset();return;}
        if(!_ticking){if(!hovered)return;_clock.Restart();_hoverStart=0;_ticking=true;CompositionTarget.Rendering+=Tick;}
        _fromEnvelope=_envelope;_transitionStart=_clock.Elapsed.TotalSeconds;
        if(hovered&&!_hovered)_hoverStart=_clock.Elapsed.TotalSeconds;
        _hovered=hovered;
    }
    private void Tick(object? sender,EventArgs e)
    {
        if(ReducedMotion||!IsVisible){Reset();return;}
        var now=_clock.Elapsed.TotalSeconds;var u=(now-_transitionStart)/(_hovered?0.35:0.5);
        var envelope=_fromEnvelope+((_hovered?1:0)-_fromEnvelope)*CaptureHoverMotion.Smooth(u);
        RenderFrameAt(now-_hoverStart,envelope);
        if(!_hovered&&u>=1)Reset();
    }
    private void Reset(){if(_ticking)CompositionTarget.Rendering-=Tick;_clock.Stop();_ticking=false;_hovered=false;RenderFrameAt(0,0);}
}
