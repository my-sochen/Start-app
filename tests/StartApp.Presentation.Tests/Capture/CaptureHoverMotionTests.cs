namespace StartApp.Presentation.Tests;
public class CaptureHoverMotionTests
{
 [Fact] public void Static_pose_is_exact_at_zero_envelope(){foreach(var t in new[]{0.0,0.65,1.3,2.59}){Assert.Equal(0,CaptureHoverMotion.Lift(t,0));Assert.Equal(0,CaptureHoverMotion.Tilt(t,0));Assert.Equal(1,CaptureHoverMotion.BeamOpacity(t,0));foreach(var small in new[]{false,true})Assert.Equal((0.0,1.0),CaptureHoverMotion.Cube(t,0,small));}}
 [Fact] public void Reference_extrema(){Assert.Equal(-46,CaptureHoverMotion.Lift(1.3,1),10);Assert.Equal(2.4,CaptureHoverMotion.Tilt(0.65,1),10);Assert.Equal(0.82,CaptureHoverMotion.BeamOpacity(1.3,1),10);Assert.Equal(-34,CaptureHoverMotion.Cube(0,1,false).Offset,10);Assert.Equal(-45.5,CaptureHoverMotion.Cube(0,1,true).Offset,10);}
 [Fact] public void Loop_repeats(){foreach(var t in new[]{0.13,0.65,1.3,2.4}){Assert.Equal(CaptureHoverMotion.Lift(t,1),CaptureHoverMotion.Lift(t+2.6,1),9);Assert.Equal(CaptureHoverMotion.Flow(t),CaptureHoverMotion.Flow(t+2.6),9);foreach(var small in new[]{false,true})Assert.Equal(CaptureHoverMotion.Cube(t,1,small).Offset,CaptureHoverMotion.Cube(t+2.6,1,small).Offset,9);}}
 [Fact] public void Envelope_clamps_smoothly(){Assert.Equal(0,CaptureHoverMotion.Smooth(-1));Assert.Equal(0.5,CaptureHoverMotion.Smooth(0.5));Assert.Equal(1,CaptureHoverMotion.Smooth(2));}
}
