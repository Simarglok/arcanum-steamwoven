using Arcanum.Input;
using Arcanum.World;
using Godot;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer;

// ARC-UI-099 (MIG16 tail, SA44; docs/architecture/interface-modes.md): the screen-edge scroll of the original's main loop (0x401F50). The
// mouse on the exact border pixel of the window scrolls the view towards that edge (a corner pixel scrolls diagonally), through the same
// camera path as the arrow keys and under the same mode table (`WorldScrollsGameView`, the reducer's modal rule). The original tests the
// pointer's position on every pass of the main loop, so a pointer parked on the border keeps scrolling; here the last position a mouse
// event reported counts until the pointer leaves the window.
public partial class TerrainViewerApp
{
    /// <summary>The last pointer position the world saw (viewport pixels), or null when no pointer event has come or the pointer left the
    /// window. The viewer gets the position from events only: a window that never saw the pointer has no border pixel.</summary>
    private Vector2? _worldPointer;

    private void NoteWorldPointer(InputEvent input)
    {
        // An automated run (the self-test, a native smoke) sends mouse events at arbitrary points (a default (0, 0) is a corner) or has a
        // stray real pointer; where such a run tests the edge it sets the pointer itself (`WorldPointerForSelfTest`).
        if (AutomatedVisualRun) return;
        if (input is InputEventMouse mouse) _worldPointer = mouse.Position;
    }
    internal void WorldPointerForSelfTest(Vector2? position) => _worldPointer = position;

    /// <summary>The scroll direction (x to the right, y downwards) of the pointer on a border pixel of the window, else (0, 0).</summary>
    internal Vec2 WorldEdgeScrollDirection()
    {
        if (_worldPointer is not { } at || !_worldFocused || !_pointerInWindow) return default;
        var size = WorldViewport;
        var (x, y) = OriginalScroll.EdgeDirection((int)Math.Floor(at.X), (int)Math.Floor(at.Y), (int)Math.Round(size.X), (int)Math.Round(size.Y));
        return new Vec2(x, y);
    }

    /// <summary>ARC-UI-100: the original's scroll clock (0x40E0A0): one step at most per 16 ms, shared by the arrow keys and the screen edge.</summary>
    private readonly OriginalScrollClock _scrollClock = new();

    /// <summary>The keyboard's intent with the original's scroll in place of the lab camera's analog pan. The arrow keys (the reducer's
    /// keyboard camera) and the screen edge (<see cref="WorldEdgeScrollDirection"/>) each give a direction code; the arrows win when both are
    /// held (the original calls the one scroll function from the key records first, and its clock lets a second call in the same pass
    /// through only 16 ms later). A step moves the view by the original's constant pixels (<see cref="OriginalScroll.Step"/>) at most once
    /// per 16 ms; between steps the camera is active but still. The arrows go on where the reducer let them (<c>CameraActive</c>), the edge
    /// only where the mode table lets the view scroll, as before. The right stick keeps its analog pan on the gamepad's own intent.</summary>
    private IntentFrame WithOriginalScroll(IntentFrame intent, RawInputFrame keyboard, InputContext context)
    {
        // A pan the reducer could not compute (an overflowing frame time) stays non-finite so the frame is quarantined as before.
        if (!double.IsFinite(intent.CameraPan.X) || !double.IsFinite(intent.CameraPan.Y)) return intent;
        _scrollClock.Advance(context.Delta);
        var arrows = intent.Accepted && intent.CameraActive;
        var edgeAllowed = intent.Accepted && !(context.Modal && !context.ScrollUnderModal);
        var code = arrows ? OriginalScroll.CodeOf((int)Math.Sign(keyboard.KeyboardCamera.X), (int)Math.Sign(keyboard.KeyboardCamera.Y)) : OriginalScroll.None;
        if (code == OriginalScroll.None && edgeAllowed)
        {
            var edge = WorldEdgeScrollDirection();
            code = OriginalScroll.CodeOf((int)edge.X, (int)edge.Y);
        }
        if (code == OriginalScroll.None) return arrows ? intent with { CameraPan = default } : intent;
        if (!_scrollClock.TryStep()) return intent with { CameraPan = default, CameraActive = true };
        var (x, y) = OriginalScroll.Step(code);
        // The camera's pan is the world image's movement, the opposite of the view's.
        return intent with { CameraPan = new Vec2(-x, -y), CameraActive = true };
    }
}
