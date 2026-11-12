using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the skill pane shows: its background art and the four buttons' frames (a null entry: the button's art did not read).</summary>
public sealed record AreaSkillBarLook(Texture2D? Background, IReadOnlyList<Texture2D[]?> Buttons);

/// <summary>
/// ARC-UI-1113: the original's skill pane (bar mode 2) drawn in the bottom strip's message window of <see cref="AreaGameBarPanel"/>, in the
/// strip's frame (ledger SKP1–SKP6): the mode's background art and the skill bar's four buttons in one row. The buttons are the bar's push
/// buttons (GB3, GB4): down while held over them, lit under the pointer. It raises what the pointer does on them; the app decides (the
/// release over a button activates its skill, a right press starts a hotkey drag, the pointer entering writes the button's name).
/// </summary>
public partial class AreaSkillBarView : Control
{
    private TextureRect _background = null!;
    private readonly TextureRect[] _buttons = new TextureRect[SkillPaneRules.Buttons];
    private AreaSkillBarLook? _look;
    // The button held down with the left button and the one under the pointer.
    private int? _held, _hovered;

    /// <summary>A left press on button <c>n</c>, and its release (and whether over the button).</summary>
    public event Action<int>? ButtonPressed;
    public event Action<int, bool>? ButtonReleased;
    /// <summary>A right press on button <c>n</c>: the original starts a drag onto the hotkey bar (0x57E5D0).</summary>
    public event Action<int>? ButtonRightPressed;
    /// <summary>The pointer entered button <c>n</c>.</summary>
    public event Action<int>? Entered;

    internal Vector2? ButtonCentreForSelfTest(int button) => _buttons[button].Visible ? _buttons[button].GetGlobalRect().GetCenter() : null;
    internal Texture2D? ButtonArtForSelfTest(int button) => _buttons[button].Visible ? _buttons[button].Texture : null;
    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal int? HeldForSelfTest => _held;

    public override void _Ready()
    {
        Name = "SkillPane";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        _background = new TextureRect { Name = "Background", Position = Local(SkillPaneRules.BackgroundX, SkillPaneRules.BackgroundY), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_background);
        for (var b = 0; b < SkillPaneRules.Buttons; b++)
        {
            var button = b;
            var rect = new TextureRect { Name = "Skill" + b, Position = Local(SkillPaneRules.ButtonX[b], SkillPaneRules.ButtonY), MouseFilter = MouseFilterEnum.Stop, Visible = false };
            rect.GuiInput += input => ButtonInput(rect, input, button);
            rect.MouseEntered += () => { _hovered = button; Render(); Entered?.Invoke(button); };
            rect.MouseExited += () => { if (_hovered == button) _hovered = null; Render(); };
            AddChild(rect);
            _buttons[b] = rect;
        }
        // Hidden mid-press (the bars went away): no release reaches the view, so the press and the hover are forgotten.
        VisibilityChanged += () => { if (!Visible) { _held = null; _hovered = null; Render(); } };
        Render();
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private void ButtonInput(Control rect, InputEvent input, int button)
    {
        if (input is not InputEventMouseButton click) return;
        if (click.ButtonIndex == MouseButton.Right)
        {
            if (!click.Pressed) return;
            rect.AcceptEvent();
            ButtonRightPressed?.Invoke(button);
            return;
        }
        if (click.ButtonIndex != MouseButton.Left) return;
        rect.AcceptEvent();
        if (click.Pressed)
        {
            _held = button;
            Render();
            ButtonPressed?.Invoke(button);
            return;
        }
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        var over = _held == button && new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position);
        _held = null;
        Render();
        ButtonReleased?.Invoke(button, over);
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaSkillBarLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    private void Render()
    {
        var look = _look;
        _background.Texture = look?.Background;
        _background.Visible = look?.Background is not null;
        for (var b = 0; b < _buttons.Length; b++) DrawButton(_buttons[b], look?.Buttons.ElementAtOrDefault(b), b);
    }

    // A button's frame (GB4): down while held with the pointer over it; lit (two steps) under the pointer; else idle. A button whose art did
    // not read is not shown (no invisible area takes the pointer).
    private void DrawButton(TextureRect rect, Texture2D[]? frames, int button)
    {
        rect.Visible = frames is { Length: > 0 };
        if (frames is not { Length: > 0 }) return;
        var held = _held == button;
        var hovered = _hovered == button;
        var look = held && hovered ? AreaBarButtonLook.Down : hovered && !held ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        rect.Texture = frames[AreaGameBarLayout.Frame(look, frames.Length)];
        rect.Size = frames[0].GetSize();
    }
}
