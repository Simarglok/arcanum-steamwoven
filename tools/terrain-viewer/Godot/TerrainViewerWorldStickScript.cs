using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const int SimulatedPadId = 3;
    // ARC-INPUT-001: the WORLD keyboard never moves the actor. Synthetic movement
    // scripts that used to hold W/A/S/D now drive the same screen sectors through
    // the selected controller's left stick: W/S set LeftY -1/+1 and A/D set LeftX
    // -1/+1, one production joypad callback per key edge, so callback order and
    // same-tick sequence numbers stay those of the former keyboard scripts.
    private readonly HashSet<Key> _simulatedSectorKeys = new();

    internal static Vec2 SectorVector(IEnumerable<Key> keys)
    {
        var held = keys.ToHashSet();
        return new((held.Contains(Key.D) ? 1 : 0) - (held.Contains(Key.A) ? 1 : 0), (held.Contains(Key.S) ? 1 : 0) - (held.Contains(Key.W) ? 1 : 0));
    }
    internal void SimulatedSectorKey(Key key, bool pressed, int device = SimulatedPadId)
    {
        if (key is not (Key.W or Key.A or Key.S or Key.D)) throw new ArgumentOutOfRangeException(nameof(key));
        if (pressed) _simulatedSectorKeys.Add(key); else _simulatedSectorKeys.Remove(key);
        var vector = SectorVector(_simulatedSectorKeys);
        var axis = key is Key.A or Key.D ? JoyAxis.LeftX : JoyAxis.LeftY;
        _Input(new InputEventJoypadMotion { Device = device, Axis = axis, AxisValue = (float)(axis == JoyAxis.LeftX ? vector.X : vector.Y) });
    }
    internal void ClearSimulatedSectorKeys() => _simulatedSectorKeys.Clear();
    /// <summary>The selected simulated controller's current level, as the adapter holds it.</summary>
    internal RawInputFrame SimulatedPadLevel(int device = SimulatedPadId)
        => _worldAdapter.SampleController(device, true) ?? new RawInputFrame(device, default);
    internal static RawInputFrame StickLevel(Vec2 leftStick, int device = SimulatedPadId) => new(device, leftStick);
    internal void ReleaseSimulatedPadForSelfTest() { _worldAdapter.Clear(); ClearSimulatedSectorKeys(); _worldReducer.SetStartupDevices(Array.Empty<int>()); QuarantineWorld(); }
    /// <summary>The palette's Fit / Reset scene handlers (what its buttons call).</summary>
    internal void WorldPaletteFitForSelfTest() { RequireExplicitTest(); FitWorldView(); }
    internal void WorldPaletteResetSceneForSelfTest() { RequireExplicitTest(); PaletteResetScene(); }
    internal double WorldCameraZoomForSelfTest => _camera!.State.Zoom;
    private void RequireExplicitTest() { if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required."); }
    /// <summary>Selects the simulated controller and quarantines, as a fresh acquisition does.</summary>
    internal void SelectSimulatedPadForSelfTest() { _worldAdapter.Clear(); ClearSimulatedSectorKeys(); _worldReducer.SetStartupDevices(new[] { SimulatedPadId }); QuarantineWorld(); }
    /// <summary>A former keyboard script frame, split for the WORLD route: arrows stay on
    /// the keyboard frame and the old actor vector becomes the selected pad's left stick.</summary>
    internal static (RawInputFrame Keyboard, RawInputFrame Pad) SplitActorFrame(RawInputFrame raw)
        => (raw with { KeyboardActor = default, KeyboardActorHeld = false }, StickLevel(raw.KeyboardActor, SimulatedPadId) with { Focused = raw.Focused });

    /// <summary>One full Ctrl+Shift+F9 press/release through the production callback.</summary>
    internal void SendPaletteChordForSelfTest(KeyLocation ctrl = KeyLocation.Left, KeyLocation shift = KeyLocation.Left)
    {
        _Input(new InputEventKey { Keycode = Key.Ctrl, Location = ctrl, Pressed = true, CtrlPressed = true });
        _Input(new InputEventKey { Keycode = Key.Shift, Location = shift, Pressed = true, CtrlPressed = true, ShiftPressed = true });
        _Input(new InputEventKey { Keycode = Key.F9, Pressed = true, CtrlPressed = true, ShiftPressed = true });
        _Input(new InputEventKey { Keycode = Key.F9, Pressed = false, CtrlPressed = true, ShiftPressed = true });
        _Input(new InputEventKey { Keycode = Key.Shift, Location = shift, Pressed = false, CtrlPressed = true });
        _Input(new InputEventKey { Keycode = Key.Ctrl, Location = ctrl, Pressed = false });
    }
}
