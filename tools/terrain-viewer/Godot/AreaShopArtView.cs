using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-069: the opening shop (window 15) in the original's look (<see cref="AreaShopArtLayout"/>, ledger MW30): the screen's black, the
/// strips and the two art buttons Accept and Back with their frames, push behaviour and sounds (GB3, GB4, GB7). The window has no background:
/// the barter window the app docks at the screen's (0, 41) covers its middle. PROJECT: the controller's focus row lights its button until the
/// pointer moves; the hints are not shown.
/// </summary>
public partial class AreaShopArtView : Control
{
    private static readonly AreaShopArtButton[] AllButtons = Enum.GetValues<AreaShopArtButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private bool _artSet;
    private int _focus = -1;
    private bool _focusLit;
    private AreaShopArtButton? _hovered, _pressed;

    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaShopArtButton>? ButtonPressed;
    /// <summary>An interface sound (3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _artSet;
    internal int FrameForSelfTest(AreaShopArtButton button) => Frame(button);
    internal AreaShopArtButton? HoveredForSelfTest => _hovered;
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "ShopArt";
        Size = new Vector2(AreaPremadeBrowserArtLayout.ScreenWidth, AreaPremadeBrowserArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        // ARC-UI-144: the shop shares the pre-made art's premultiplied textures (TerrainViewerAreaPremadeBrowserArt), so its canvas blends premultiplied too.
        AreaArtTextDraw.Prepare(this);
    }

    /// <summary>The art's frames by number (the strips and the buttons, <see cref="AreaShopArtLayout.ArtNumbers"/>).</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _artSet = true;
    }

    /// <summary>Shows the window with the controller's <paramref name="focus"/> row.</summary>
    public void Render(int focus)
    {
        if (!_artSet) throw new InvalidOperationException("ARC-UI-069: the shop art is not set");
        // PROJECT: the focus row is lit at the opening and after a focus move, and the pointer darkens it (as the menu art's, ARC-UI-058).
        if (!Visible || focus != _focus) _focusLit = true;
        if (!Visible) (_hovered, _pressed) = (null, null);
        _focus = focus;
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed) = (null, null);
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport (as the other menu windows' art).</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaPremadeBrowserArtLayout.ScreenHeight * factor) / 2));
    }

    private int Frame(AreaShopArtButton button)
    {
        var frames = _art.TryGetValue(AreaShopArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        return AreaCharacterArtDraw.Frame(frames, _pressed is not null, _pressed == button, _hovered == button,
            _focusLit && AreaShopArtLayout.Focused(_focus) == button);
    }

    public override void _Draw()
    {
        if (!_artSet) return;
        // No background of its own (-1, MW1): the screen's black and the strips.
        AreaCharacterArtDraw.Window(this, _art, -1);
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaShopArtLayout.Button(button);
            if (AreaCharacterArtDraw.Art(_art, art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    private AreaShopArtButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaShopArtLayout.Button(button);
            if (AreaCharacterArtDraw.Art(_art, art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y))
                return button;
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
                if (_focusLit) { _focusLit = false; QueueRedraw(); }
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
