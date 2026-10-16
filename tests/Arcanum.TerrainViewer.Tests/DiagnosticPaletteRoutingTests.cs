using Arcanum.Input;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-INPUT-001: engine-free chord ownership and WORLD keyboard table,
/// against literal expectations (docs/input/diagnostic-palette.md).</summary>
[TestClass]
public sealed class DiagnosticPaletteRoutingTests
{
    private const WorldKeyModifiers Chord = WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift;

    [TestMethod]
    public void ChordTogglesOncePerPressAndOwnsItsWholePair()
    {
        var router = new DiagnosticPaletteChord();
        var down = router.Route(WorldKey.F9, true, false, Chord, false, true);
        Assert.IsTrue(down.TogglePalette && down.Consumed && router.Owned);
        for (var i = 0; i < 3; i++)
        {
            var echo = router.Route(WorldKey.F9, true, true, Chord, true, true);
            Assert.IsFalse(echo.TogglePalette); Assert.IsTrue(echo.Consumed);
        }
        // Modifier released first: the bare F9 release still belongs to the chord.
        var up = router.Route(WorldKey.F9, false, false, WorldKeyModifiers.None, true, true);
        Assert.IsTrue(up.Consumed && !up.TogglePalette && up.OriginalAction is null && !router.Owned);
        // A later bare F9 release is not owned any more.
        Assert.IsFalse(router.Route(WorldKey.F9, false, false, WorldKeyModifiers.None, true, true).Consumed);
        Assert.AreEqual(1, router.Trace.Count(d => d.TogglePalette));
    }

    [TestMethod]
    [DataRow(WorldKeyModifiers.None)] [DataRow(WorldKeyModifiers.Ctrl)] [DataRow(WorldKeyModifiers.Shift)]
    [DataRow(WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift | WorldKeyModifiers.Alt)]
    [DataRow(WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift | WorldKeyModifiers.Meta)]
    [DataRow(WorldKeyModifiers.Meta | WorldKeyModifiers.Shift)]
    public void OnlyExactCtrlShiftIsTheChord(WorldKeyModifiers modifiers)
    {
        var router = new DiagnosticPaletteChord();
        var decision = router.Route(WorldKey.F9, true, false, modifiers, false, true);
        Assert.IsFalse(decision.TogglePalette || decision.Consumed || router.Owned);
        Assert.AreEqual(WorldKeyRole.Unmapped, decision.Role);
    }

    [TestMethod]
    public void TextEntryOrUnfocusedCannotOpenButOpenPaletteCanClose()
    {
        var router = new DiagnosticPaletteChord();
        Assert.IsFalse(router.Route(WorldKey.F9, true, false, Chord, paletteOpen: false, openAllowed: false).Consumed);
        Assert.IsFalse(router.Route(WorldKey.F9, false, false, Chord, false, false).Consumed);
        var close = router.Route(WorldKey.F9, true, false, Chord, paletteOpen: true, openAllowed: false);
        Assert.IsTrue(close.TogglePalette && close.Consumed);
    }

    [TestMethod]
    public void LostReleaseNeverBlocksTheNextChord()
    {
        var router = new DiagnosticPaletteChord();
        router.Route(WorldKey.F9, true, false, Chord, false, true);
        // Release delivered to another window; a fresh press is a new chord.
        var again = router.Route(WorldKey.F9, true, false, Chord, true, true);
        Assert.IsTrue(again.TogglePalette && again.Consumed);
        router.Reset();
        Assert.IsFalse(router.Owned);
        Assert.IsFalse(router.Route(WorldKey.F9, false, false, Chord, false, true).Consumed);
    }

    [TestMethod]
    public void OtherKeysPassThroughUnconsumedEvenWithChordModifiersHeld()
    {
        var router = new DiagnosticPaletteChord();
        foreach (var key in new[] { WorldKey.F1, WorldKey.F3, WorldKey.R, WorldKey.Digit1, WorldKey.W })
        {
            var decision = router.Route(key, true, false, Chord, false, true);
            Assert.IsFalse(decision.Consumed || decision.TogglePalette, key.ToString());
            Assert.AreEqual(WorldKeyRole.OriginalReserved, decision.Role, key.ToString());
        }
        Assert.IsFalse(router.Owned);
    }

    [TestMethod]
    public void WorldTableReservesOriginalKeysAndPansWithArrowsOnly()
    {
        var expected = new Dictionary<WorldKey, string>
        {
            [WorldKey.F1] = "Followers: go to point", [WorldKey.F2] = "Followers: attack target", [WorldKey.F3] = "Followers: follow closer",
            [WorldKey.F4] = "Followers: follow farther", [WorldKey.F5] = "Followers: stop attacking", [WorldKey.F7] = "Quick save",
            [WorldKey.F8] = "Quick load", [WorldKey.W] = "World map", [WorldKey.S] = "Sleep/Wait", [WorldKey.A] = "Last active action",
            [WorldKey.C] = "Character sheet", [WorldKey.F] = "Fate points", [WorldKey.I] = "Inventory", [WorldKey.L] = "Logbook",
            [WorldKey.O] = "Options", [WorldKey.R] = "Ready/Unready combat", [WorldKey.T] = "Technical schematics",
            [WorldKey.V] = "Version information", [WorldKey.Space] = "Close interfaces / Turn-Based toggle", [WorldKey.Escape] = "Game menu",
            [WorldKey.Digit1] = "Quick slot 1", [WorldKey.Digit9] = "Quick slot 9", [WorldKey.Digit0] = "Quick slot 10",
        };
        foreach (var (key, action) in expected)
        {
            Assert.AreEqual(WorldKeyRole.OriginalReserved, OriginalWorldKeyboard.Role(key, false), key.ToString());
            Assert.AreEqual(action, OriginalWorldKeyboard.OriginalAction(key), key.ToString());
        }
        foreach (var key in new[] { WorldKey.Up, WorldKey.Down, WorldKey.Left, WorldKey.Right })
            Assert.AreEqual(WorldKeyRole.CameraPan, OriginalWorldKeyboard.Role(key, false));
        foreach (var key in new[] { WorldKey.D, WorldKey.F9, WorldKey.F11, WorldKey.Tab, WorldKey.Other })
            Assert.AreEqual(WorldKeyRole.Unmapped, OriginalWorldKeyboard.Role(key, false), key.ToString());
    }

    [TestMethod]
    public void PaletteOwnsOnlyItsNavigationKeys()
    {
        foreach (var key in new[] { WorldKey.Up, WorldKey.Down, WorldKey.Enter, WorldKey.Space, WorldKey.Escape, WorldKey.Backspace })
            Assert.AreEqual(WorldKeyRole.PaletteControl, OriginalWorldKeyboard.Role(key, true), key.ToString());
        foreach (var key in new[] { WorldKey.W, WorldKey.S, WorldKey.Q, WorldKey.E, WorldKey.F1, WorldKey.Left, WorldKey.Right, WorldKey.Digit1 })
            Assert.AreEqual(WorldKeyRole.Unmapped, OriginalWorldKeyboard.Role(key, true), key.ToString());
    }

    [TestMethod]
    public void TraceIsBounded()
    {
        var router = new DiagnosticPaletteChord { TraceLimit = 4 };
        for (var i = 0; i < 10; i++) router.Route(WorldKey.W, i % 2 == 0, false, WorldKeyModifiers.None, false, true);
        Assert.AreEqual(4, router.Trace.Count);
    }

    [TestMethod]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void FixedSignArrowsIgnoreCameraInversionWhileTheStickFollowsIt(bool invertX, bool invertY)
    {
        static InputStateReducer Armed()
        {
            var reducer = new InputStateReducer(); reducer.Quarantine();
            var neutral = new RawInputFrame(-1, default);
            reducer.Reduce(neutral, new InputContext()); reducer.Reduce(neutral, new InputContext());
            return reducer;
        }
        var context = new InputContext(InvertCameraX: invertX, InvertCameraY: invertY, Delta: 1) { KeyboardCameraFixedSign = true };
        var arrows = Armed().Reduce(new RawInputFrame(-1, default, KeyboardCamera: new Vec2(1, 1)) { KeyboardCameraHeld = true }, context).CameraPan;
        // The false/false sign: right/down arrows move the camera right/down (image left/up).
        Assert.IsTrue(arrows.X < 0 && arrows.Y < 0, $"arrows {arrows.X},{arrows.Y}");
        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { 3 }); reducer.Quarantine();
        var pad = new RawInputFrame(3, default);
        reducer.Reduce(pad, context); reducer.Reduce(pad, context);
        var stick = reducer.Reduce(pad with { RightStick = new Vec2(1, 1) }, context).CameraPan;
        Assert.AreEqual(invertX ? 1 : -1, Math.Sign(stick.X)); Assert.AreEqual(invertY ? 1 : -1, Math.Sign(stick.Y));
        // Without the option the historical lab behaviour (arrows follow inversion) is unchanged.
        var lab = Armed().Reduce(new RawInputFrame(-1, default, KeyboardCamera: new Vec2(1, 1)) { KeyboardCameraHeld = true },
            context with { KeyboardCameraFixedSign = false }).CameraPan;
        Assert.AreEqual(invertX ? 1 : -1, Math.Sign(lab.X)); Assert.AreEqual(invertY ? 1 : -1, Math.Sign(lab.Y));
    }
}
