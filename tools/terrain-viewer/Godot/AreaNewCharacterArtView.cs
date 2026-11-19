using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-068: the new-character window (window 12) in the original's look (<see cref="AreaNewCharacterArtLayout"/>, ledger MW27–MW29):
/// the strips and the background 765, the character draw it shares with the pre-made browser (<see cref="AreaCharacterArtDraw"/>), the
/// name field (with a closing "|" while typed) and the ten art buttons with their frames, push behaviour and sounds (GB3, GB4, GB7). The
/// pointer over the gender's or the race's band shows that text in the description field (0x5456B0); the band is forgotten when the
/// window opens (0x5446F0 sets the flag to 0). The name field is an art-less button: 3026 when the pointer enters it, 3027 on its
/// press, acting on a release over it, as the menu's text buttons (MW27). A press elsewhere than the field ends the name's typing
/// first (NW8). PROJECT: the controller's focus row lights its buttons until the pointer moves.
/// </summary>
public partial class AreaNewCharacterArtView : Control
{
    private static readonly AreaNewCharacterArtButton[] AllButtons = Enum.GetValues<AreaNewCharacterArtButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    // ARC-UI-2575: the generated frames are painted at this view's own device scale (the unit scale of the Steamwoven layout).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);
    private bool _zoned;
    private AreaPremadeBrowserFonts? _fonts;
    private AreaNewCharacterView? _view;
    private Texture2D? _portrait;
    private int _focus = -1;
    private bool _focusLit;
    private AreaNewCharacterArtButton? _hovered, _pressed;
    private bool _overName, _pressedName;
    private AreaNewCharacterHover _hover;

    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaNewCharacterArtButton>? ButtonPressed;
    /// <summary>The name field released over itself after its press.</summary>
    public event Action? NameClicked;
    /// <summary>A press not on the name field (it ends the name's typing before any button acts, NW8).</summary>
    public event Action? PressedElsewhere;
    /// <summary>An interface sound (3000/3001 an art button's press and release, 3026/3027 the name field's).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _fonts is not null;
    internal AreaNewCharacterView? ViewForSelfTest => Visible ? _view : null;
    internal AreaNewCharacterHover HoverForSelfTest => _hover;
    internal bool PortraitForSelfTest => Visible && _portrait is not null;
    /// <summary>ARC-UI-2865: the portrait texture the window draws (the HD smoke reads whether it is the pack's redraw).</summary>
    internal Texture2D? PortraitTextureForSelfTest => _portrait;
    internal AreaCharacterDescription? DescriptionForSelfTest => Visible && _view is { } view ? AreaNewCharacterArtLayout.Description(view, _hover) : null;
    internal int FrameForSelfTest(AreaNewCharacterArtButton button) => Frame(button);
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "NewCharacterArt";
        Size = new Vector2(AreaPremadeBrowserArtLayout.ScreenWidth, AreaPremadeBrowserArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    /// <summary>The art's frames by number and the two fonts.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaPremadeBrowserFonts fonts)
    {
        Claim.Release();
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    }

    /// <summary>Shows <paramref name="view"/> with its portrait (null when it did not read: the field stays black) and the controller's
    /// <paramref name="focus"/> row.</summary>
    public void Render(AreaNewCharacterView view, Texture2D? portrait, int focus)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-068: the new-character art is not set");
        // PROJECT: the focus row is lit at the opening and after a focus move, and the pointer darkens it (as the menu art's, ARC-UI-058).
        if (!Visible || focus != _focus) _focusLit = true;
        if (!Visible) (_hovered, _pressed, _overName, _pressedName, _hover) = (null, null, false, false, AreaNewCharacterHover.None);
        (_view, _portrait, _focus) = (view, portrait, focus);
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _overName, _pressedName, _hover, _view, _portrait) = (null, null, false, false, AreaNewCharacterHover.None, null, null);
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        // ARC-UI-2575: with the Steamwoven layout the screen's 800 × 400 body lies in the window zone at the layout's unit scale; ARC-UI-2891: with the column option the whole 800 × 600 screen stands in the column's full-screen zone.
        if (AreaGameBarPanel.TwoStripScreen(size, AreaShopArtLayout.Window.Y) is { } zone)
        {
            Scale = new Vector2((float)zone.Scale, (float)zone.Scale);
            Position = new Vector2((float)zone.X, (float)zone.Y);
            _zoned = true;
            Claim.RedrawIfScaleChanged();
            return;
        }
        if (_zoned) { _zoned = false; Claim.Release(); QueueRedraw(); }
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaPremadeBrowserArtLayout.ScreenHeight * factor) / 2));
    }

    private int Frame(AreaNewCharacterArtButton button)
    {
        var frames = _art.TryGetValue(AreaNewCharacterArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        return AreaCharacterArtDraw.Frame(frames, _pressed is not null || _pressedName, _pressed == button, _hovered == button,
            _focusLit && AreaNewCharacterArtLayout.Focused(_focus).Contains(button));
    }

    public override void _Draw()
    {
        // ARC-UI-2575: the art is shared among the screens of the pre-made family, so the view on screen claims its frames at every draw (only while the Steamwoven layout places it).
        if (_zoned) Claim.Claim(_art.Values.SelectMany(frames => frames));
        if (_fonts is not { } fonts || _view is not { } view) return;
        AreaCharacterArtDraw.Window(this, _art, AreaNewCharacterArtLayout.BackgroundArt);
        AreaCharacterArtDraw.Character(this, fonts, view.Character, _portrait, AreaNewCharacterArtLayout.Description(view, _hover));
        // The redraw 0x5447B0: the name field over the character draw.
        var field = AreaPremadeBrowserArtLayout.Name;
        AreaCharacterArtDraw.Fill(this, field);
        var (x, text) = AreaNewCharacterArtLayout.NameLine(fonts.Name.Font, view.Character.Name, view.Typing);
        AreaArtTextDraw.Text(this, fonts.Name, text, x, field.Y, AreaPremadeBrowserArtLayout.Red);
        foreach (var button in AllButtons)
        {
            var (art, bx, by) = AreaNewCharacterArtLayout.Button(button);
            if (AreaCharacterArtDraw.Art(_art, art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(bx, by, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    private AreaNewCharacterArtButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaNewCharacterArtLayout.Button(button);
            if (AreaCharacterArtDraw.Art(_art, art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y))
                return button;
        }
        return null;
    }

    private bool NameAt(Vector2 at) => ButtonAt(at) is null && AreaPremadeBrowserArtLayout.Contains(AreaNewCharacterArtLayout.NameButton, at.X, at.Y);

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || (_hovered is null && !_overName && _hover == AreaNewCharacterHover.None)) return;
        (_hovered, _overName, _hover) = (null, false, AreaNewCharacterHover.None);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = ButtonAt(motion.Position);
                var name = NameAt(motion.Position);
                var hover = AreaNewCharacterArtLayout.Hover(motion.Position.X, motion.Position.Y);
                if (_focusLit) { _focusLit = false; QueueRedraw(); }
                if (name && !_overName) SoundRequested?.Invoke(AreaGameBarLayout.MenuHoverSound);
                if (over == _hovered && name == _overName && hover == _hover) break;
                (_hovered, _overName, _hover) = (over, name, hover);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = ButtonAt(button.Position);
                var onName = NameAt(button.Position);
                // The press lights what is under it even when the pointer did not move since the opening (GB3: down over it).
                (_hovered, _overName) = (at, onName);
                if (button.Pressed)
                {
                    if (!onName) PressedElsewhere?.Invoke();
                    (_pressed, _pressedName) = (at, onName);
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                    else if (onName) SoundRequested?.Invoke(AreaGameBarLayout.MenuPressSound);
                }
                else if (_pressed is { } held)
                {
                    _pressed = null;
                    // GB3: a release elsewhere only lets the button go, silently.
                    if (at == held) { SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound); ButtonPressed?.Invoke(held); }
                }
                else if (_pressedName)
                {
                    _pressedName = false;
                    if (onName) NameClicked?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}
