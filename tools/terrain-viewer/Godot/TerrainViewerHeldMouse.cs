using Arcanum.Simulation.Goals;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-INPUT-1938 (GT105–GT112, GT114a): the retail game polls the mouse through DirectInput (0x4FF5A0; the window-message path 0x4FF410 is dead, bit 0x20 of the init word is never set), and a left button
// held down reposts its press record every time more than 250 ms have passed since the last one (the stamp 0x6046EC). Each repeat is an ordinary press of the game view at the pointer's point of that moment: the
// same gates, the same starters. A tile walk or run press sets the held flag (0x64C6D8) at its tail, whether the starter succeeded or not; while the flag is 1 a press on an object is ignored (0x54E05E) and a tile
// walk marks its run 0x400 (a straight-line plan, GT110), so the first press never marks and every repeat does. A left release clears the flag and does nothing else (GT112): the hero keeps walking to the last target.
// Steamwoven clears the flag on every release, in any interface mode (the original leaves it at 1 after a release in a mode other than 0 or 3, a likely original bug, issue #2572).
// A repeat is posted for a press the world's click handler took; a press a window or a HUD control took has no repeat here (the poll reaches those controls in the original).
public partial class TerrainViewerApp
{
    // The held flag 0x64C6D8: a tile walk press happened and the left button has not been released since.
    private bool _heldWalkFlag;
    // The left button's stamp (0x6046EC): the real clock (ms) of its last press record; null when no press the world took is held.
    private ulong? _leftStampMs;
    private int _heldRepeats;
    // Self-test hooks: the button state, the clock and the pointer (a point and whether a control covers it) of an automated run.
    // A left press that arrived through the input events and has not yet reached the mode-0 handler (the armed cursors and skills take their presses on the press; the repeat is for the ordinary mode-0 press only).
    private bool _pressFromInput;
    private bool? _leftDownForSelfTest;
    private ulong? _clockMsForSelfTest;
    private (Vector2 Point, bool OverGui)? _heldPointerForSelfTest;

    /// <summary>Repeats the polled mouse posted for a held left button (a smoke and the diagnostic line read it).</summary>
    internal int HeldMouseRepeats => _heldRepeats;
    internal bool HeldWalkFlag => _heldWalkFlag;

    internal void HeldMouseForSelfTest(bool? leftDown, ulong? clockMs, (Vector2 Point, bool OverGui)? pointer)
    {
        _leftDownForSelfTest = leftDown; _clockMsForSelfTest = clockMs; _heldPointerForSelfTest = pointer;
    }

    private bool LeftButtonDown => _leftDownForSelfTest ?? Godot.Input.IsMouseButtonPressed(MouseButton.Left);
    private ulong HeldClockMs => _clockMsForSelfTest ?? Time.GetTicksMsec();

    /// <summary>A left press the world's click handler takes stamps the button (the poll's first record) when the button is really down.</summary>
    private void NoteLeftPress()
    {
        _pressFromInput = false;
        // Only a button that is down can be held: a press fed in with no button state behind it (a smoke's, an event with no real button) is released at once and has no repeats.
        _leftStampMs = LeftButtonDown ? HeldClockMs : null;
    }

    /// <summary>The release of the left button: the flag is cleared and the stamp dropped (the walk goes on, GT112).</summary>
    private void NoteLeftRelease() { _leftStampMs = null; _heldWalkFlag = false; }

    /// <summary>
    /// The polled mouse of one frame (0x4FF5A0): a released button ends the hold; a held one whose stamp is more than 250 ms old (<see cref="HeroInputRules.HeldRepeatDue"/>) posts an ordinary press at the pointer's
    /// point now, with the modifier keys now (GT107, GT111), and stamps again.
    /// </summary>
    private void PollHeldMouse()
    {
        if (!LeftButtonDown) { if (_leftStampMs is not null || _heldWalkFlag) NoteLeftRelease(); return; }
        if (_leftStampMs is not { } stamp || !_worldFocused) return;
        var now = HeldClockMs;
        // A clock that went back (a self-test's) posts at once, as the stick's rule does.
        if (now >= stamp && !HeroInputRules.HeldRepeatDue(now - stamp)) return;
        _leftStampMs = now;
        var point = _heldPointerForSelfTest?.Point ?? GetViewport().GetMousePosition();
        var overGui = _heldPointerForSelfTest is { } placed ? placed.OverGui : GetViewport().GuiGetHoveredControl() is not null;
        // A pointer off the game view, or over a control: nothing, and the held flag is kept (GT108).
        if (!_pointerInWindow || overGui || !GetViewport().GetVisibleRect().HasPoint(point)) return;
        _heldRepeats++;
        var ctrl = Godot.Input.IsKeyPressed(Key.Ctrl);
        var alt = Godot.Input.IsKeyPressed(Key.Alt);
        var shift = Godot.Input.IsKeyPressed(Key.Shift);
        WorldObservation(() => WorldRouteClick(point, ctrl, alt, shift, repeat: true));
    }
}
