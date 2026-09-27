using GamepadLab.Core;
namespace GamepadLab.Tests;

internal static class InputStateTests
{
    public static void Run()
    {
        Assertions.Equal(Vec2.CircularDeadzone(new(.2, 0), .2), default(Vec2), "deadzone boundary");
        Assertions.Near(Vec2.CircularDeadzone(new(.6, 0), .2).X, .5, .0001, "deadzone rescale");
        Assertions.Near(Vec2.CircularDeadzone(new(2, 0), .2).Length, 1, .0001, "deadzone clamp");
        var diagonal = new Vec2(1, 1).Normalized(); Assertions.Near(diagonal.Length, 1, .0001, "diagonal normalized");
        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { 4, 2 }); Assertions.Equal(reducer.ActiveDevice!.Value, 2, "lowest device active");
        var c = new InputContext(Delta: 1d / 60); Assertions.True(!reducer.Reduce(new(4, new(1, 0), Confirm: true), c).Accepted, "nonactive rejected");
        var first = reducer.Reduce(new(2, default, Confirm: true), c); Assertions.True(first.Confirm, "edge accepted");
        Assertions.True(!reducer.Reduce(new(2, default, Confirm: true), c).Confirm, "pulse once per press");
        reducer.Reduce(new(2, default), c); Assertions.True(reducer.Reduce(new(2, default, Confirm: true), c).Confirm, "second edge");
        var a = new InputStateReducer(); a.SetStartupDevices(new[] { 1 }); var one = a.Reduce(new(1, new(1, 0)), new InputContext(Delta: 1d / 30));
        var b = new InputStateReducer(); b.SetStartupDevices(new[] { 1 }); var two = b.Reduce(new(1, new(1, 0)), c).Pan + b.Reduce(new(1, new(1, 0)), c).Pan;
        Assertions.Near(one.Pan.X, two.X, .001, "frame-rate displacement");
        reducer.Disconnect(2); Assertions.True(reducer.ActiveDevice is null, "disconnect clear");
        Assertions.True(!reducer.Reduce(new(3, new(1, 0)), c).Accepted, "reconnect activation quarantined");
        reducer.Reduce(new(3, default), c); reducer.Reduce(new(3, default), c); Assertions.True(reducer.Reduce(new(3, default, Confirm: true), c).Confirm, "neutral quarantine arms");
        var repeat = new MenuRepeat(); Assertions.Equal(repeat.Update(1, 0, 0), 1, "repeat immediate"); Assertions.Equal(repeat.Update(1, 0, .34), 0, "repeat delay"); Assertions.Equal(repeat.Update(1, 0, .01), 1, "repeat fires"); Assertions.Equal(repeat.Update(-1, 0, 0), -1, "opposite resets");
    }
}
