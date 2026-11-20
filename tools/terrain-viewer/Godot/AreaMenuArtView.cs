using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-061: the options controls' art: the backgrounds by art number (688, 671), the bar 623, the arrows' frames and the
/// label and value fonts.</summary>
public sealed record AreaMenuControlArt(IReadOnlyDictionary<int, Texture2D> Backgrounds, Texture2D Bar, IReadOnlyList<Texture2D> Less,
    IReadOnlyList<Texture2D> More, AreaArtFontFace LabelFont, AreaArtFontFace ValueFont);

/// <summary>ARC-UI-061: one shown options control: its label, kind, value of <paramref name="Top"/> (a slider's bar) and value text
/// (a list's).</summary>
public readonly record struct AreaMenuControlView(string Label, AreaOptionKind Kind, int Value, int Top, string ValueText);

/// <summary>
/// ARC-UI-058: a menu button window in the original's look (<see cref="AreaMenuArtLayout"/>, ledger MW1–MW8): the window's background
/// over the 800 × 600 screen at the whole factor the bars use, centred in the window, and its labels in the art font with their three
/// colour passes. The pointer over a label lights its face and plays 3026, a press plays 3027, and a release over the pressed label
/// chooses it (as the diagnostic window's buttons do). PROJECT: the shown buttons take the first slots in order (the original's
/// windows have no hidden buttons), and the focused label is lit as the pointer lights it at the window's opening and after a
/// controller's or key's focus move. ARC-UI-059: the options window's page buttons at their own points (MW7). ARC-UI-061: the options
/// page's controls in their own art at their slots (<see cref="AreaOptionsControlLayout"/>, MW8–MW10): an arrow is a push button (the
/// pointer over it shows its third frame, a press its second and sound 3000, the release over it sound 3001 and the change); PROJECT:
/// the focused control's label is lit in the page buttons' hover face. ARC-UI-070: the text in the original's blend (MW31).
/// </summary>
public partial class AreaMenuArtView : Control
{
    private Texture2D? _background;
    private AreaArtFontFace? _font;
    private IReadOnlyList<string> _labels = [];
    private IReadOnlyList<(int X, int Y, int Width, int Height)> _buttons = [];
    private AreaMenuWindow? _window;
    // _hovered is the lit label, _pointer the label under the pointer (its entry plays the sound).
    private int _focus = -1, _hovered = -1, _pointer = -1, _pressed = -1;
    private AreaMenuControlArt? _controlArt;
    private IReadOnlyList<AreaMenuControlView> _controls = [];
    // An arrow is control × 2 + (0 less, 1 more); _arrowHovered is the arrow under the pointer, _arrowHeld the one pressed.
    private IReadOnlyList<(int X, int Y, int Width, int Height)> _arrows = [];
    private int _controlFocus = -1, _arrowHovered = -1, _arrowHeld = -1;

    /// <summary>A label chosen (its index among the shown buttons).</summary>
    public event Action<int>? ButtonPressed;
    /// <summary>A button's interface sound (3026 the pointer entering, 3027 a press; ARC-UI-061: an arrow's 3000 press, 3001 release).</summary>
    public event Action<int>? SoundRequested;
    /// <summary>ARC-UI-061: an options control's arrow released over itself (the control's index among the shown, direction -1 or 1).</summary>
    public event Action<int, int>? ArrowPressed;

    internal int HoveredForSelfTest => _hovered;
    internal AreaArtFontFace? FontForSelfTest => _font;
    internal IReadOnlyList<string> LabelsForSelfTest => Visible ? _labels : [];
    internal IReadOnlyList<(int X, int Y, int Width, int Height)> ButtonsForSelfTest => Visible ? _buttons : [];
    /// <summary>The window position of a label's centre, or null.</summary>
    internal Vector2? ButtonCentreForSelfTest(int index) => Visible && index >= 0 && index < _buttons.Count
        ? GetGlobalTransform() * new Vector2(_buttons[index].X + _buttons[index].Width / 2f, _buttons[index].Y + _buttons[index].Height / 2f) : null;

    /// <summary>ARC-UI-061: the shown controls as "label: value" (a slider's value as value/top).</summary>
    internal IReadOnlyList<string> ControlsForSelfTest => Visible
        ? [.. _controls.Select(control => $"{control.Label}: {control.ValueText}")] : [];
    /// <summary>ARC-UI-061: the points on the 800 × 600 screen where the last draw put the controls' backgrounds.</summary>
    internal IReadOnlyList<(int X, int Y)> ControlPlacesForSelfTest => Visible ? _drawnPlaces : [];
    private readonly List<(int X, int Y)> _drawnPlaces = [];
    /// <summary>ARC-UI-061: an arrow's frame as drawn now (-1 none).</summary>
    internal int ArrowFrameForSelfTest(int control, AreaOptionsArrow arrow) => Visible && _controlArt is { } art && control >= 0 && control < _controls.Count
        ? ArrowFrame(control * 2 + (int)arrow, arrow == AreaOptionsArrow.Less ? art.Less.Count : art.More.Count) : -1;
    /// <summary>ARC-UI-061: the window position of an arrow's centre, or null.</summary>
    internal Vector2? ArrowCentreForSelfTest(int control, AreaOptionsArrow arrow) =>
        Visible && control * 2 + (int)arrow is var index && index >= 0 && index < _arrows.Count
            ? GetGlobalTransform() * new Vector2(_arrows[index].X + _arrows[index].Width / 2f, _arrows[index].Y + _arrows[index].Height / 2f) : null;

    public override void _Ready()
    {
        Name = "MainMenuArt";
        Size = new Vector2(AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    /// <summary>The background art and the labels' font.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D> backgrounds, AreaArtFontFace font)
    {
        _backgrounds = backgrounds ?? throw new ArgumentNullException(nameof(backgrounds));
        _font = font ?? throw new ArgumentNullException(nameof(font));
    }

    private IReadOnlyDictionary<int, Texture2D> _backgrounds = new Dictionary<int, Texture2D>();

    /// <summary>ARC-UI-061: the options controls' art; without it the options window's controls are not drawn here.</summary>
    public void SetControlArt(AreaMenuControlArt art) => _controlArt = art ?? throw new ArgumentNullException(nameof(art));

    /// <summary>ARC-UI-061: true when the options controls are drawn in their art.</summary>
    public bool DrawsControls => _controlArt is not null;

    /// <summary>True when the art is set and <paramref name="window"/> is one this look draws.</summary>
    public bool Draws(AreaMenuWindow window) => _font is not null && AreaMenuArtLayout.Background(window) is { } art && _backgrounds.ContainsKey(art);

    /// <summary>Shows <paramref name="window"/> with its shown labels; <paramref name="focus"/> is the menu's focused button (-1 or
    /// below for none, as the options window's focus on a control). ARC-UI-061: <paramref name="controls"/> are the options page's shown
    /// controls (drawn with <see cref="SetControlArt"/>'s art), <paramref name="controlFocus"/> the focused one (-1 none).</summary>
    public void Render(AreaMenuWindow window, IReadOnlyList<string> labels, int focus, IReadOnlyList<AreaMenuControlView>? controls = null, int controlFocus = -1)
    {
        ArgumentNullException.ThrowIfNull(labels);
        controls ??= [];
        if (controls.Count > 0 && _controlArt is null) throw new InvalidOperationException("ARC-UI-061: the options controls' art is not set");
        if (_font is null || AreaMenuArtLayout.Background(window) is not { } art || !_backgrounds.TryGetValue(art, out var background))
            throw new InvalidOperationException("ARC-UI-058: the menu art does not draw " + window);
        focus = Math.Max(focus, -1);
        var opened = _window != window || !Visible;
        // The focused label is lit when a window opens and after a focus move of the controller or a key (PROJECT); the pointer's next
        // move takes it over again.
        if (opened) { _hovered = focus; _pointer = -1; _pressed = -1; }
        else if (focus != _focus) _hovered = focus;
        (_window, _focus, _background, _labels) = (window, focus, background, labels);
        _buttons = [.. labels.Select((label, slot) => AreaMenuArtLayout.Button(window, slot, _font.Font.Measure(label)))];
        if (_hovered >= _buttons.Count) _hovered = -1;
        // ARC-UI-061: a page change or a new window lets a held arrow go (no release reaches it).
        if (opened || controls.Count != _controls.Count) { _arrowHovered = -1; _arrowHeld = -1; }
        (_controls, _controlFocus) = (controls, controlFocus);
        _arrows = _controlArt is { } controlArt ? [.. controls.SelectMany((_, index) => new[] { AreaOptionsArrow.Less, AreaOptionsArrow.More }.Select(arrow =>
        {
            var (x, y) = AreaOptionsControlLayout.Arrow(AreaOptionsControlLayout.Place(index), arrow);
            var frame = (arrow == AreaOptionsArrow.Less ? controlArt.Less : controlArt.More)[0];
            return (x, y, frame.GetWidth(), frame.GetHeight());
        }))] : [];
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_window, _hovered, _pointer, _pressed) = (null, -1, -1, -1);
        (_controls, _arrows, _controlFocus, _arrowHovered, _arrowHeld) = ([], [], -1, -1, -1);
    }

    /// <summary>ARC-UI-2577, ARC-UI-2891: the full-screen layout of the Steamwoven interface for the window now (the plate fitted to the window's height, black beside it), or null while the layout is off (the HD option or the generated interface off, the older smokes' suspension, or a window below f = 0.25).</summary>
    internal SteamwovenFullScreenPlan? FullScreenPlan
    {
        get
        {
            if (!AreaGameBarPanel.SteamwovenOn) return null;
            var size = GetViewportRect().Size;
            // ARC-UI-2891: the plate is fitted to the window's height (SteamwovenColumnMenu.Fit).
            var options = SteamwovenHud.Options;
            // The window's size and the layout options decide the plan: it is looked up once per size (the panel asks several times a frame).
            if (_planSize != size || !Equals(_planOptions, options)) (_plan, _planSize, _planOptions) = (SteamwovenFullScreen.Plan((int)size.X, (int)size.Y, options), size, options);
            return _plan;
        }
    }

    private SteamwovenFullScreenPlan? _plan;
    private Vector2 _planSize;
    private SteamwovenLayoutOptions? _planOptions;
    private bool _hadPlan;

    /// <summary>ARC-DEV-334: the 800 × 600 plate's scale and top left in the window (the whole factor of the bars, ARC-UI-3003: the pack's 16:9 plate is gone), also for the
    /// original's dialog box the in-play menu's quit question puts on the same plate.</summary>
    public (float Factor, Vector2 Position) PlateFit()
    {
        // ARC-UI-2577: the Steamwoven layout puts the plate in the window at the fitted scale.
        if (FullScreenPlan is { } layout) return ((float)layout.Factor, new Vector2(layout.Zone.X, layout.Zone.Y));
        var size = GetViewportRect().Size;
        var plate = new Vector2(AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight);
        var factor = AreaGameBarPanel.ScreenFactor(size);
        var position = (size - plate * factor) / 2;
        return (factor, new Vector2(AreaGameBarPanel.ScreenLeft(size, (int)factor), MathF.Round(position.Y)));
    }

    /// <summary>Centres the interactive 800 × 600 plate.</summary>
    public void Place()
    {
        var (factor, position) = PlateFit();
        Scale = new Vector2(factor, factor);
        Position = position;
        // A fractional layout scale is sampled linear (as the strips of the prototype are); the original's whole factors stay Nearest.
        var layoutOn = FullScreenPlan is not null;
        TextureFilter = layoutOn ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest;
        // The plate follows the layout: a switch of it with no new Render still redraws.
        if (layoutOn != _hadPlan) { _hadPlan = layoutOn; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (_background is null || _font is null) return;
        var width = (float)AreaMenuArtLayout.ScreenWidth;
        var height = (float)AreaMenuArtLayout.ScreenHeight;
        // ARC-UI-2461: the kit's plate for this menu's background, when the set has it at the composition's size; the plate is the 800 × 600 composition itself.
        // SWI-LAY-5: the plate is sampled nearest where its baked pixels are the device pixels (a canvas texture of its own, so the labels and controls keep the view's linear sampling), else linear.
        if (KitPlatePart() is { } kit && SteamwovenKitStore.Texture(kit.Set, kit.Part.Id) is { } kitPlate)
        {
            SteamwovenKitStore.Retain("plate", kit.Set, [kit.Part.Id]);
            DrawTextureRect(KitPlateTexture(kitPlate, kit.Part), new Rect2(0, 0, width, height), false);
        }
        else
        {
            if (FullScreenPlan is { } plan && SteamwovenKitStore.SetFor(plan.Layout.Step) is { } held) SteamwovenKitStore.Retain("plate", held, []);
            DrawTextureRect(_background, new Rect2(0, 0, width, height), false);
        }
        DrawControls();
        for (var i = 0; i < _labels.Count; i++)
        {
            var (x, y, _, _) = _buttons[i];
            var glyphs = _font.Font.Layout(_labels[i]);
            var passes = AreaMenuArtLayout.Passes;
            for (var pass = 0; pass < passes.Count; pass++)
            {
                var (dx, dy, colour, blend) = passes[pass];
                if (pass == passes.Count - 1 && i == _hovered) colour = AreaMenuArtLayout.HoverFace;
                AreaArtTextDraw.Glyphs(this, _font, glyphs, x + dx, y + dy, colour, blend);
            }
        }
    }

    private CanvasTexture? _plateTexture;

    /// <summary>The texture the plate is drawn with: the kit's own when it is drawn scaled, a nearest-sampled canvas texture of it when its pixels are the device pixels.</summary>
    private Texture2D KitPlateTexture(Texture2D texture, SteamwovenKitPart part)
    {
        var exact = FullScreenPlan is { } plan && part.Width == plan.Zone.Width && part.Height == plan.Zone.Height;
        if (!exact) return texture;
        if (_plateTexture is null || _plateTexture.DiffuseTexture != texture) _plateTexture = new CanvasTexture { DiffuseTexture = texture, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        return _plateTexture;
    }

    /// <summary>ARC-UI-2461: true when the kit plate drawn now is sampled nearest (its baked pixels are the device pixels), for the self-test.</summary>
    internal bool KitPlateExactForSelfTest => KitPlatePart() is { } kit && FullScreenPlan is { } plan && kit.Part.Width == plan.Zone.Width && kit.Part.Height == plan.Zone.Height;

    /// <summary>ARC-UI-2461: the id of the kit plate this menu draws now, for the self-test (null: the pack's or the original's plate).</summary>
    internal string? KitPlateForSelfTest => KitPlatePart()?.Part.Id;

    /// <summary>ARC-UI-2461: the kit set and the plate part this menu draws instead of its pack or original background (the Steamwoven layout on, the set holding that menu's plate at the composition's size); null for the pack's or the original's.</summary>
    private (SteamwovenKitSet Set, SteamwovenKitPart Part)? KitPlatePart()
    {
        if (FullScreenPlan is not { } plan || _window is not { } window || AreaMenuArtLayout.Background(window) is not { } art) return null;
        if (SteamwovenKitStore.SetFor(plan.Layout.Step) is not { } set || SteamwovenKitComposer.Plate(set, art) is not { } part) return null;
        return (set, part);
    }

    /// <summary>ARC-UI-061: the frame an arrow shows (GB4): down while held with the pointer over it, lit under the pointer when none is
    /// held, idle otherwise.</summary>
    private int ArrowFrame(int arrow, int frames) => AreaGameBarLayout.Frame(
        _arrowHeld == arrow && _arrowHovered == arrow ? AreaBarButtonLook.Down
        : _arrowHeld < 0 && _arrowHovered == arrow ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle, frames);

    private void DrawControls()
    {
        _drawnPlaces.Clear();
        if (_controlArt is not { } art) return;
        for (var i = 0; i < _controls.Count; i++)
        {
            var control = _controls[i];
            var point = AreaOptionsControlLayout.Place(i);
            var background = art.Backgrounds[AreaOptionsControlLayout.Background(control.Kind)];
            DrawTextureRect(background, new Rect2(point.X, point.Y, background.GetWidth(), background.GetHeight()), false);
            _drawnPlaces.Add(point);
            var labelWidth = art.LabelFont.Font.Measure(control.Label).Width;
            DrawText(art.LabelFont, control.Label, AreaOptionsControlLayout.Label(point, background.GetWidth(), labelWidth),
                i == _controlFocus ? AreaMenuArtLayout.HoverFace : AreaOptionsControlLayout.LabelFace);
            var bar = art.Bar;
            if (control.Kind == AreaOptionKind.Slider)
            {
                var filled = AreaOptionsControlLayout.Filled(Math.Clamp(control.Value, 0, control.Top), control.Top, bar.GetWidth());
                var (x, y) = AreaOptionsControlLayout.Field(point);
                if (filled > 0) DrawTextureRectRegion(bar, new Rect2(x, y, filled, bar.GetHeight()), new Rect2(0, 0, filled, bar.GetHeight()));
            }
            else
            {
                var width = art.ValueFont.Font.Measure(control.ValueText).Width;
                DrawText(art.ValueFont, control.ValueText, AreaOptionsControlLayout.Value(point, bar.GetWidth(), width), AreaOptionsControlLayout.ValueFace);
            }
            foreach (var arrow in new[] { AreaOptionsArrow.Less, AreaOptionsArrow.More })
            {
                var frames = arrow == AreaOptionsArrow.Less ? art.Less : art.More;
                var texture = frames[ArrowFrame(i * 2 + (int)arrow, frames.Count)];
                var (x, y) = AreaOptionsControlLayout.Arrow(point, arrow);
                DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
            }
        }
    }

    /// <summary>A one-pass text (the controls' fonts have flag 0x10 only: no shadow, 0x8, and the cover blend, MW31).</summary>
    private void DrawText(AreaArtFontFace font, string text, (int X, int Y) at, (byte R, byte G, byte B) face) =>
        AreaArtTextDraw.Text(this, font, text, at.X, at.Y, face);

    // The pointer leaving the screen's control (into the margin or out of the window) is the original's event 3: the label goes dark.
    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;
        _pointer = -1;
        if (_hovered < 0 && _arrowHovered < 0) return;
        _hovered = -1; _arrowHovered = -1;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                // ARC-UI-061: while an arrow is held no other arrow takes the pointer (GB3); an arrow has no sound for the pointer.
                var arrowOver = AreaMenuArtLayout.HitTest(_arrows, motion.Position.X, motion.Position.Y);
                if (_arrowHeld >= 0 && arrowOver != _arrowHeld) arrowOver = -1;
                if (arrowOver != _arrowHovered) { _arrowHovered = arrowOver; QueueRedraw(); }
                var over = AreaMenuArtLayout.HitTest(_buttons, motion.Position.X, motion.Position.Y);
                if (over != _pointer)
                {
                    _pointer = over;
                    if (over >= 0) SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
                }
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = AreaMenuArtLayout.HitTest(_buttons, button.Position.X, button.Position.Y);
                var arrowAt = AreaMenuArtLayout.HitTest(_arrows, button.Position.X, button.Position.Y);
                if (button.Pressed && arrowAt >= 0)
                {
                    // ARC-UI-061: an arrow goes down on the press (3000) and acts on the release over it (3001, GB3/GB6).
                    (_arrowHeld, _arrowHovered) = (arrowAt, arrowAt);
                    SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                    QueueRedraw();
                }
                else if (!button.Pressed && _arrowHeld >= 0)
                {
                    var held = _arrowHeld;
                    _arrowHeld = -1;
                    _arrowHovered = arrowAt;
                    QueueRedraw();
                    if (arrowAt == held)
                    {
                        SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                        ArrowPressed?.Invoke(held / 2, held % 2 == 0 ? -1 : 1);
                    }
                }
                else if (button.Pressed)
                {
                    _pressed = at;
                    if (at >= 0) SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
                }
                else
                {
                    var chosen = _pressed >= 0 && at == _pressed ? at : -1;
                    _pressed = -1;
                    if (chosen >= 0) ButtonPressed?.Invoke(chosen);
                }
                AcceptEvent();
                break;
        }
    }
}
