using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Which art font a browser text is drawn with (MW14, MW20).</summary>
public sealed record AreaPremadeBrowserFonts(AreaArtFontFace Text, AreaArtFontFace Name);

/// <summary>
/// ARC-UI-066: the pre-made character browser (window 13) in the original's look (<see cref="AreaPremadeBrowserArtLayout"/>, ledger
/// MW17–MW21, MW26): the strips and the background over the 800 × 600 screen at the bars' whole factor, centred in the window; the browsed
/// hero's large portrait, its name, gender, race and background name centred in their fields, its story (or its race's text) and
/// its fifteen stats, each field filled black first; the four art buttons with their idle, down and lit frames (GB4) and push
/// behaviour (GB3: down on the press, acting on a release over it), 3000 on the press and 3001 on the release (GB7). ARC-UI-070: the
/// text in the original's blend (MW31). A story shows the lines its field holds, as the original's text draw (MW26).
/// </summary>
public partial class AreaPremadeBrowserArtView : Control
{
    private static readonly AreaPremadeBrowserButton[] AllButtons = Enum.GetValues<AreaPremadeBrowserButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    // ARC-UI-2575: the generated frames are painted at this view's own device scale (the unit scale of the Steamwoven layout).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);
    private bool _zoned;
    private AreaPremadeBrowserFonts? _fonts;
    private AreaPremadeBrowserView? _view;
    private Texture2D? _portrait;
    private AreaPremadeBrowserButton? _hovered, _pressed;

    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaPremadeBrowserButton>? ButtonPressed;
    /// <summary>A button's interface sound (3000 the press, 3001 the release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _fonts is not null;
    internal AreaPremadeBrowserView? ViewForSelfTest => Visible ? _view : null;
    internal bool PortraitForSelfTest => Visible && _portrait is not null;
    internal int FrameForSelfTest(AreaPremadeBrowserButton button) => Frame(button);
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "PremadeBrowserArt";
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

    /// <summary>Shows <paramref name="view"/> with its portrait (null when it did not read: the field stays black).</summary>
    public void Render(AreaPremadeBrowserView view, Texture2D? portrait)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-066: the browser art is not set");
        if (!Visible) (_hovered, _pressed) = (null, null);
        (_view, _portrait) = (view, portrait);
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _view, _portrait) = (null, null, null, null);
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

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private int Frame(AreaPremadeBrowserButton button)
    {
        var frames = _art.TryGetValue(AreaPremadeBrowserArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        return AreaCharacterArtDraw.Frame(frames, _pressed is not null, _pressed == button, _hovered == button);
    }

    public override void _Draw()
    {
        // ARC-UI-2575: the art is shared among the screens of the pre-made family, so the view on screen claims its frames at every draw (only while the Steamwoven layout places it).
        if (_zoned) Claim.Claim(_art.Values.SelectMany(frames => frames));
        if (_fonts is not { } fonts || _view is not { } view) return;
        AreaCharacterArtDraw.Window(this, _art, AreaPremadeBrowserArtLayout.BackgroundArt);
        AreaCharacterArtDraw.Character(this, fonts, view, _portrait, AreaCharacterDescription.Of(view));
        // The redraw 0x5458D0: the player name over the character draw.
        AreaCharacterArtDraw.Centred(this, AreaPremadeBrowserArtLayout.Name, view.Name, fonts.Name, AreaPremadeBrowserArtLayout.Red);
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaPremadeBrowserArtLayout.Button(button);
            if (Art(art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    private AreaPremadeBrowserButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaPremadeBrowserArtLayout.Button(button);
            if (Art(art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return button;
        }
        return null;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = ButtonAt(motion.Position);
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = ButtonAt(button.Position);
                // The press lights what is under it even when the pointer did not move since the opening (GB3: down over it).
                _hovered = at;
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                }
                else if (_pressed is { } held)
                {
                    _pressed = null;
                    // GB3: a release elsewhere only lets the button go, silently.
                    if (at == held) { SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound); ButtonPressed?.Invoke(held); }
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}
