using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What the quantity pane shows besides its state: the background art, each button's frames (a null entry: the button's art did not read) and the count's font (null: no count).</summary>
public sealed record AreaQuantityPaneLook(Texture2D? Background, IReadOnlyList<Texture2D[]?> Buttons, AreaArtFontFace? CountFont);

/// <summary>
/// ARC-UI-1884: the original's quantity pane (bar mode 9, ledger QW3, QW3a) drawn in the bottom strip over the message window: art 298, the five push buttons
/// (All, up, down, Accept, Cancel; the bar's buttons: down while held over them, lit under the pointer), the kind's icon at (269, 534) and the count in six
/// digits of the roller font (art 25) from (404, 545). It raises what the pointer does on the buttons; the app decides (a release over a button acts).
/// </summary>
public partial class AreaQuantityPaneView : Control
{
    private TextureRect _background = null!, _icon = null!;
    private CountLayer _count = null!;
    private readonly TextureRect[] _buttons = new TextureRect[QuantityPaneRules.Buttons.Count];
    private AreaQuantityPaneLook? _look;
    private int _value;
    private int? _held, _hovered;

    /// <summary>A left press on button <c>n</c> (the index of <see cref="QuantityPaneRules.Buttons"/>), and its release (over the button or not).</summary>
    public event Action<QuantityPaneAction>? ButtonPressed;
    public event Action<QuantityPaneAction, bool>? ButtonReleased;

    internal Vector2? ButtonCentreForSelfTest(QuantityPaneAction action) => _buttons[(int)action].Visible ? _buttons[(int)action].GetGlobalRect().GetCenter() : null;
    internal Texture2D? ButtonArtForSelfTest(QuantityPaneAction action) => _buttons[(int)action].Visible ? _buttons[(int)action].Texture : null;
    internal Texture2D? BackgroundForSelfTest => _background.Visible ? _background.Texture : null;
    internal Texture2D? IconForSelfTest => _icon.Visible ? _icon.Texture : null;
    /// <summary>The digits the count draws (the cells' text, no leading zeros), empty without the font.</summary>
    internal string CountTextForSelfTest => _count.Font is null ? string.Empty : new string(QuantityPaneRules.Cells(_value).Where(c => c is not null).Reverse().Select(c => c!.Value).ToArray());

    // ARC-UI-3347: the All button of the kit, through the round-part layer (its rect stays for the pointer).
    private readonly AreaKitRoundPartsLayer _rings = new();

    /// <summary>ARC-UI-3347: the buttons drawn from the kit now, for the self-test.</summary>
    internal IReadOnlyList<int> RingsShownForSelfTest => _rings.ShownForSelfTest;

    public override void _EnterTree()
    {
        InterfaceHd.Changed += KitChanged;
        GetViewport().SizeChanged += KitChanged;
    }

    public override void _ExitTree()
    {
        InterfaceHd.Changed -= KitChanged;
        GetViewport().SizeChanged -= KitChanged;
    }

    private void KitChanged()
    {
        if (IsNodeReady()) Render();
    }

    public override void _Ready()
    {
        Name = "QuantityPane";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(AreaGameBarLayout.ScreenWidth, AreaGameBarLayout.BottomHeight);
        // The background takes the clicks over the pane, so the message window under it does not scroll its entries.
        _background = new TextureRect { Name = "Background", Position = Local(QuantityPaneRules.BackgroundX, QuantityPaneRules.BackgroundY), MouseFilter = MouseFilterEnum.Stop };
        AddChild(_background);
        _icon = new TextureRect { Name = "Icon", Position = Local(QuantityPaneRules.IconX, QuantityPaneRules.IconY), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_icon);
        _count = new CountLayer { Name = "Count", MouseFilter = MouseFilterEnum.Ignore, Size = Size };
        AreaArtTextDraw.Prepare(_count);
        AddChild(_count);
        for (var b = 0; b < _buttons.Length; b++)
        {
            var button = QuantityPaneRules.Buttons[b];
            var rect = new TextureRect { Name = "Quantity" + button.Action, Position = Local(button.X, button.Y), MouseFilter = MouseFilterEnum.Stop, Visible = false };
            var index = b;
            rect.GuiInput += input => ButtonInput(rect, input, index);
            rect.MouseEntered += () => { _hovered = index; Render(); };
            rect.MouseExited += () => { if (_hovered == index) _hovered = null; Render(); };
            AddChild(rect);
            _buttons[b] = rect;
        }
        AddChild(_rings);
        // Hidden mid-press (the bars went away): no release reaches the view, so the press and the hover are forgotten.
        VisibilityChanged += () => { if (!Visible) { _held = null; _hovered = null; Render(); } };
        Render();
    }

    /// <summary>The strip is the bottom one (origin 0, 441): screen coordinates become the strip's own.</summary>
    private static Vector2 Local(int x, int y) => new(x, y - AreaGameBarLayout.BottomTop);

    private void ButtonInput(Control rect, InputEvent input, int button)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
        rect.AcceptEvent();
        var action = QuantityPaneRules.Buttons[button].Action;
        if (click.Pressed)
        {
            _held = button;
            Render();
            ButtonPressed?.Invoke(action);
            return;
        }
        // Godot sends the release to the control that took the press; it is over the button when it lands inside it.
        var over = _held == button && new Rect2(Vector2.Zero, rect.Size).HasPoint(click.Position);
        _held = null;
        Render();
        ButtonReleased?.Invoke(action, over);
    }

    /// <summary>Draws <paramref name="look"/>.</summary>
    public void Bind(AreaQuantityPaneLook look)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        if (IsNodeReady()) Render();
    }

    /// <summary>The count shown and the kind's icon (null: unset, nothing is drawn there).</summary>
    public void SetState(int count, Texture2D? icon)
    {
        _value = count;
        _icon.Texture = icon;
        _icon.Visible = icon is not null;
        if (icon is not null) _icon.Size = icon.GetSize();
        _count.Set(_look?.CountFont, count);
    }

    private void Render()
    {
        var look = _look;
        _background.Texture = look?.Background;
        _background.Visible = look?.Background is not null;
        if (look?.Background is { } background) _background.Size = background.GetSize();
        _count.Set(look?.CountFont, _value);
        var rings = new List<AreaKitRoundPartsLayer.Item>();
        for (var b = 0; b < _buttons.Length; b++)
        {
            var frame = DrawButton(_buttons[b], look?.Buttons.ElementAtOrDefault(b), b);
            AreaKitRingButtonRect.Apply(_buttons[b], QuantityPaneRules.Buttons[b].Art, frame, rings);
        }
        _rings.Show(rings);
    }

    // A button's frame (GB4): down while held with the pointer over it; lit (two steps) under the pointer; else idle. A button whose art did not read is
    // not shown (no invisible area takes the pointer).
    private int DrawButton(TextureRect rect, Texture2D[]? frames, int button)
    {
        rect.Visible = frames is { Length: > 0 };
        if (frames is not { Length: > 0 }) return 0;
        var held = _held == button;
        var hovered = _hovered == button;
        var look = held && hovered ? AreaBarButtonLook.Down : hovered && !held ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        var frame = AreaGameBarLayout.Frame(look, frames.Length);
        rect.Texture = frames[frame];
        rect.Size = frames[0].GetSize();
        return frame;
    }

    /// <summary>The count: its digits right-aligned in six cells of the roller font's "0" width (0x56A300), black.</summary>
    private sealed partial class CountLayer : Control
    {
        private AreaArtFontFace? _face;
        private int _count;

        public AreaArtFontFace? Font => _face;

        public void Set(AreaArtFontFace? face, int count)
        {
            if (ReferenceEquals(face, _face) && count == _count) return;
            _face = face; _count = count;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_face is not { } face) return;
            var cell = face.Font.Layout("0") is { Count: > 0 } zero ? zero[0].Glyph.Advance : 0;
            var cells = QuantityPaneRules.Cells(_count);
            for (var k = 0; k < cells.Count; k++)
                if (cells[k] is { } digit)
                    AreaArtTextDraw.Text(this, face, digit.ToString(), QuantityPaneRules.CountX + (cells.Count - 1 - k) * cell, QuantityPaneRules.CountY - AreaGameBarLayout.BottomTop, (0, 0, 0));
        }
    }
}
