using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class WorldZoomTests
{
    private static void Near(double expected, double actual) => Assert.AreEqual(expected, actual, 1e-12);
    [TestMethod]
    public void LiteralRatePartitionsDeadzoneSignsBoundsAndInvalidInputs()
    {
        Near(0, InputStateReducer.EffectiveTrigger(.1)); Near(.5, InputStateReducer.EffectiveTrigger(.55));
        Near(1, InputStateReducer.EffectiveTrigger(1));
        foreach (var t in new[] { -.1, 1.1, double.NaN, double.PositiveInfinity })
        { Assert.IsFalse(InputStateReducer.ValidTrigger(t)); Near(0, InputStateReducer.EffectiveTrigger(t)); }
        Near(4, WorldZoomPolicy.Advance(2, 1, 1)); Near(1, WorldZoomPolicy.Advance(2, -1, 1));
        Near(Math.Sqrt(8), WorldZoomPolicy.Advance(2, .5, 1));
        foreach (var partitions in new[] { new[] { 1d }, new[] { .04,.06,.17,.23,.5 }, Enumerable.Repeat(.01,100).ToArray() })
        {
            var zoom = 2d; foreach (var dt in partitions) zoom = WorldZoomPolicy.Advance(zoom, 1, dt);
            Near(4, zoom);
        }
        Near(8, WorldZoomPolicy.Advance(2, 1, double.MaxValue)); Near(.125, WorldZoomPolicy.Advance(2, -1, double.MaxValue));
        foreach (var dt in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) Near(2,WorldZoomPolicy.Advance(2,1,dt));
        Near(2,WorldZoomPolicy.Advance(2,0,double.MaxValue));
        foreach (var drive in new[] { -2,2,double.NaN,double.PositiveInfinity }) Near(2,WorldZoomPolicy.Advance(2,drive,1));
        foreach (var z in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity }) Near(2,WorldZoomPolicy.Absolute(2,z));
        Near(.125,WorldZoomPolicy.Absolute(2,-10)); Near(8,WorldZoomPolicy.Absolute(2,100));
    }
    [TestMethod]
    public void WheelCrossingStopsExactlyForOneEventWithoutToleranceStickiness()
    {
        Near(1,WorldZoomPolicy.Step(.9,1.25)); Near(1.25,WorldZoomPolicy.Step(1,1.25));
        Near(1,WorldZoomPolicy.Step(1.1,.8)); Near(.8,WorldZoomPolicy.Step(1,.8));
        Near(.9375,WorldZoomPolicy.Step(.75,1.25)); Near(1.2,WorldZoomPolicy.Step(1.5,.8));
        Near(1,WorldZoomPolicy.Step(1-1e-12,1.25)); Near(1,WorldZoomPolicy.Step(1+1e-12,.8));
        Near(1.000001,WorldZoomPolicy.Absolute(1,1.000001));
    }
    [TestMethod]
    public void StrictSeparatePreferenceCodecUsesLiteralFallbackAndExactValues()
    {
        foreach (var json in new[] { null,"","bad","null","[]","{}","{\"zoom\":3}","{\"version\":2,\"zoom\":3}",
            "{\"version\":\"1\",\"zoom\":3}","{\"version\":1,\"zoom\":\"3\"}","{\"version\":1,\"zoom\":null}",
            "{\"version\":1,\"zoom\":0.124}","{\"version\":1,\"zoom\":8.001}","{\"version\":1,\"zoom\":1e999}" })
            Near(2,WorldZoomCodec.DeserializeOrDefault(json));
        foreach(var value in new[] { .125,1,2,3,8,2.345678901234 }) Near(value,WorldZoomCodec.DeserializeOrDefault(WorldZoomCodec.Serialize(value)));
        Near(3,WorldZoomCodec.DeserializeOrDefault("{\"version\":1,\"zoom\":3.0}"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorldZoomCodec.Serialize(double.NaN));
    }
    [TestMethod]
    public void WorldOptInSelectedAcquisitionIndividualOppositesAndFrameOnlyDrive()
    {
        var context = new InputContext(Delta:.1,AdvanceTime:true) { WorldZoomEnabled = true };
        var pad = new RawInputFrame(3,default) { LeftTrigger = 1, RightTrigger = 1 };
        Assert.IsTrue(InputStateReducer.HasAnyLevel(pad,context)); Assert.IsFalse(InputStateReducer.HasAnyLevel(pad,new()));
        var reducer = new InputStateReducer();
        Assert.IsFalse(reducer.Reduce(pad,context).Accepted); reducer.EndFrame(); Assert.AreEqual(3,reducer.ActiveDevice);
        for(var i=0;i<4;i++) Assert.IsFalse(reducer.Reduce(pad,context).Accepted);
        var neutral = pad with { LeftTrigger = 0,RightTrigger = 0 };
        Assert.IsFalse(reducer.Reduce(neutral,context).Accepted); Assert.IsFalse(reducer.Reduce(neutral,context).Accepted);
        var opposite = reducer.Reduce(pad,context); Near(0,opposite.ZoomDrive);
        Assert.IsFalse(opposite.ActorActive); Assert.IsFalse(opposite.CameraActive);
        Near(1,reducer.Reduce(neutral with {RightTrigger=1},context).ZoomDrive);
        Near(-1,reducer.Reduce(neutral with {LeftTrigger=1},context).ZoomDrive);
        Near(0,reducer.Reduce(neutral with {RightTrigger=1},context with {AdvanceTime=false}).ZoomDrive);
        Near(0,reducer.Reduce(neutral with {RightTrigger=1},context with {Modal=true}).ZoomDrive);
        Near(0,reducer.Reduce(neutral with {DeviceId=9,RightTrigger=1},context).ZoomDrive);
        Assert.AreEqual(InputMethod.Gamepad,reducer.Method);
        reducer.FocusChanged(false); Assert.IsFalse(reducer.Reduce(pad,context).Accepted);
        reducer.Reduce(neutral,context); reducer.Reduce(pad,context); reducer.Reduce(neutral,context);
        Assert.IsFalse(reducer.Reduce(pad,context).Accepted);
    }
}
