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
        // The synthetic self-test sends mouse events at arbitrary points (its default (0, 0) is a corner); it sets the pointer itself
        // where it tests the edge. The native smokes drive real events and keep the rule.
        if (SelfTestBootstrap && !_nativeSmoke) return;
        if (input is InputEventMouse mouse) _worldPointer = mouse.Position;
    }
    private void WorldPointerLeft() => _worldPointer = null;
    internal void WorldPointerForSelfTest(Vector2? position) => _worldPointer = position;

    /// <summary>The scroll direction (x to the right, y downwards) of the pointer on a border pixel of the window, else (0, 0).</summary>
    internal Vec2 WorldEdgeScrollDirection()
    {
        if (_worldPointer is not { } at || !_worldFocused) return default;
        var size = WorldViewport;
        var (x, y) = OriginalScroll.EdgeDirection((int)Math.Floor(at.X), (int)Math.Floor(at.Y), (int)Math.Round(size.X), (int)Math.Round(size.Y));
        return new Vec2(x, y);
    }

    /// <summary>The keyboard's intent with the edge scroll added: the camera pan of the border direction and an active camera. Only an
    /// accepted frame scrolls, and only where the mode table lets the view scroll: an open window with a row of 0, the main menu and every
    /// panel outside the stack keep the camera still, as they do for the arrow keys.</summary>
    private IntentFrame WithEdgeScroll(IntentFrame intent, InputContext context)
    {
        if (!intent.Accepted || (context.Modal && !context.ScrollUnderModal)) return intent;
        var direction = WorldEdgeScrollDirection();
        if (direction == default) return intent;
        var pan = InputStateReducer.KeyboardCameraPan(direction, context);
        return intent with { CameraPan = intent.CameraPan + pan, CameraActive = true };
    }
}
