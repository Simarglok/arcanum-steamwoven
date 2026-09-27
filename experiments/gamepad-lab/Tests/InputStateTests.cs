using GamepadLab.Core;
namespace GamepadLab.Tests;

internal static class InputStateTests
{
    public static void Run()
    {
        Assertions.Equal(Vec2.CircularDeadzone(new(.19, 0), .2), default(Vec2), "below deadzone no drift");
        Assertions.Equal(Vec2.CircularDeadzone(new(.2, 0), .2), default(Vec2), "deadzone boundary");
        Assertions.Near(Vec2.CircularDeadzone(new(.6, 0), .2).X, .5, .0001, "deadzone radial rescale");
        Assertions.Near(Vec2.CircularDeadzone(new(2, 0), .2).Length, 1, .0001, "deadzone clamp");
        var diagonal = new Vec2(1, 1).Normalized(); Assertions.Near(diagonal.Length, 1, .0001, "diagonal normalized");

        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { 4, 2 }); Assertions.Equal(reducer.ActiveDevice!.Value, 2, "lowest startup device active");
        var c = new InputContext(Delta: 1d / 60); Assertions.True(!reducer.Reduce(new(4, new(1, 0), Confirm: true), c).Accepted, "nonactive controller rejected");
        var first = reducer.Reduce(new(2, default, Confirm: true), c); Assertions.True(first.Confirm, "confirm edge accepted");
        Assertions.True(!reducer.Reduce(new(2, default, Confirm: true), c).Confirm, "confirm exactly once while held");
        reducer.Reduce(new(2, default), c); Assertions.True(reducer.Reduce(new(2, default, Confirm: true), c).Confirm, "confirm after release");
        foreach (var pulse in new[] { "Cancel", "Inspect", "Previous", "Next", "Context", "Settings" })
        {
            reducer.Reduce(new(2, default), c);
            var raw = pulse switch { "Cancel" => new RawInputFrame(2, default, Cancel: true), "Inspect" => new RawInputFrame(2, default, Inspect: true), "Previous" => new RawInputFrame(2, default, Previous: true), "Next" => new RawInputFrame(2, default, Next: true), "Context" => new RawInputFrame(2, default, Context: true), _ => new RawInputFrame(2, default, Settings: true) };
            var intent = reducer.Reduce(raw, c); Assertions.True(pulse switch { "Cancel" => intent.Cancel, "Inspect" => intent.Inspect, "Previous" => intent.Previous, "Next" => intent.Next, "Context" => intent.Context, _ => intent.Settings }, $"{pulse} pulse once per press");
            Assertions.True(!(pulse switch { "Cancel" => reducer.Reduce(raw, c).Cancel, "Inspect" => reducer.Reduce(raw, c).Inspect, "Previous" => reducer.Reduce(raw, c).Previous, "Next" => reducer.Reduce(raw, c).Next, "Context" => reducer.Reduce(raw, c).Context, _ => reducer.Reduce(raw, c).Settings }), $"{pulse} does not repeat held edge");
        }

        var a = new InputStateReducer(); a.SetStartupDevices(new[] { 1 }); var one = a.Reduce(new(1, new(1, 0)), new InputContext(Delta: 1d / 30));
        var b = new InputStateReducer(); b.SetStartupDevices(new[] { 1 }); var two = b.Reduce(new(1, new(1, 0)), c).Pan + b.Reduce(new(1, new(1, 0)), c).Pan;
        Assertions.Near(one.Pan.X, two.X, .001, "frame-time scaling");
        var zoom = new InputStateReducer(); zoom.SetStartupDevices(new[] { 1 }); Assertions.Near(zoom.Reduce(new(1, new(1, 0)), new InputContext(Zoom: 2)).Pan.X, 520d / 60 / 2, .001, "zoom adjusted pan");
        var keyPan = new InputStateReducer().Reduce(new(-1, default, Keyboard: true, KeyboardPan: new(1, 1)), c); Assertions.Near(keyPan.Pan.Length, 520d / 60, .001, "normalized keyboard pan");

        var method = new InputStateReducer(); method.SetStartupDevices(new[] { 7 }); method.Reduce(new(7, new(.21, 0)), c); Assertions.Equal(method.Method, InputMethod.KeyboardMouse, "analog drift ignored");
        for (var i = 0; i < 7; i++) method.Reduce(new(7, new(.6, 0)), c); Assertions.Equal(method.Method, InputMethod.Gamepad, "meaningful stick threshold after 100ms");
        method.Reduce(new(-1, default, Keyboard: true), c); Assertions.Equal(method.Method, InputMethod.KeyboardMouse, "keyboard switches method");

        reducer.Disconnect(2); Assertions.True(reducer.ActiveDevice is null, "disconnect clears active");
        Assertions.True(!reducer.Reduce(new(8, new(1, 0)), c).Accepted && !reducer.Reduce(new(3, new(1, 0)), c).Accepted, "same-frame activation candidates are consumed");
        reducer.EndFrame(); Assertions.Equal(reducer.ActiveDevice!.Value, 3, "lowest meaningful same-frame device is selected");
        reducer.Reduce(new(3, default), c); reducer.Reduce(new(3, default), c); Assertions.True(reducer.Reduce(new(3, default, Confirm: true), c).Confirm, "neutral quarantine arms after complete frame");
        reducer.FocusChanged(false); Assertions.True(!reducer.Reduce(new(3, default, Confirm: true, Focused: false), c).Accepted, "focus loss suppresses held intent"); reducer.FocusChanged(true); reducer.Reduce(new(3, default), c); reducer.Reduce(new(3, default), c); Assertions.True(reducer.Reduce(new(3, default, Confirm: true), c).Confirm, "focus neutral clearing prevents stale edge");
        reducer.Reset(); reducer.Reduce(new(3, default), c); reducer.Reduce(new(3, default), c); Assertions.True(reducer.Reduce(new(3, default, Settings: true), c).Settings, "reset clears held state");
        var modal = new InputStateReducer(); modal.SetStartupDevices(new[] { 1 }); var modalIntent = modal.Reduce(new(1, new(1, 0), Confirm: true), new InputContext(Modal: true)); Assertions.True(modalIntent.Pan == default && modalIntent.Confirm, "modal suppresses world pan but preserves UI confirmation");

        var repeat = new MenuRepeat(); Assertions.Equal(repeat.Update(1, 0, 0), 1, "digital repeat immediate"); Assertions.Equal(repeat.Update(1, 0, .34), 0, "repeat initial delay"); Assertions.Equal(repeat.Update(1, 0, .01), 1, "repeat fires at .35"); Assertions.Equal(repeat.Update(1, 0, .10), 1, "repeat fires at .10"); Assertions.Equal(repeat.Update(-1, 0, 0), -1, "opposite resets repeat");
        var analog = new MenuRepeat(); Assertions.Equal(analog.Update(0, .64, 0), 0, "analog below engage ignored"); Assertions.Equal(analog.Update(0, .65, 0), 1, "analog engages at threshold"); Assertions.Equal(analog.Update(0, .5, .35), 1, "analog holds until release threshold"); Assertions.Equal(analog.Update(0, .45, 0), 0, "analog releases at threshold");
    }
}