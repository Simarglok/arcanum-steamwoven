using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-069: the opening shop (window 15) in the original's look (<see cref="AreaShopArtLayout"/>, ledger MW30): the screen's black, the
/// strips and the two art buttons Accept and Back with their frames, push behaviour and sounds (GB3, GB4, GB7). The window has no background:
/// the barter window the app docks at the screen's (0, 41) covers its middle. PROJECT: the controller's focus row lights its button until the
/// pointer moves; the hints are not shown.
/// ARC-UI-3041 (census M09, M10): the editor shares this view, so it draws the strips for both. Over a complete kit (<see cref="SteamwovenKitScreen.OpeningShop"/> or <see cref="SteamwovenKitScreen.CharacterEditor"/>: the kit's two strips of the creation
/// family and the kit background of the window the screen holds, <see cref="AreaKitTwoStripLayer"/>) the view draws none of the original's arts 336 and 335 and the kit's strips and window background instead; the window frame drawn over it
/// (the barter frame, the character sheet) is told through <see cref="KitBodyChanged"/> to skip its own backdrop and nothing else. A part the set lacks, holds at another size or does not load gives the screen's own art whole.
/// </summary>
public partial class AreaShopArtView : Control
{
    private static readonly AreaShopArtButton[] AllButtons = Enum.GetValues<AreaShopArtButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    // ARC-UI-2575: the generated frames are painted at this view's own device scale (the unit scale of the Steamwoven layout).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);
    private bool _zoned;
    private bool _artSet;
    private int _focus = -1;
    private bool _focusLit;
    private AreaShopArtButton? _hovered, _pressed;
    // ARC-UI-3041: the kit's parts of the opening shop and of the creation editor (one layer each, owner-resident only while its screen shows), which screen the view is now, the barter window's merchant background art (a follower's side has
    // its own kit piece), the original arts the last draw painted as its backdrop and the body window the kit showed at the last draw (what the frame over it was told).
    private readonly AreaKitTwoStripLayer _kitShop = new(SteamwovenKitScreen.OpeningShop, "twostrip-shop"), _kitEditor = new(SteamwovenKitScreen.CharacterEditor, "twostrip-editor");
    private bool _editor;
    private int _barterBackground = AreaBarterArtLayout.BarterArt;
    private IReadOnlyList<int> _ownBackdropDrawn = [];
    private SteamwovenKitWindow? _kitBodyWindow;
    private AreaKitBackdrop? _kitBackdrop;

    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaShopArtButton>? ButtonPressed;
    /// <summary>An interface sound (3000/3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _artSet;

    /// <summary>ARC-UI-3041: the kit background of the window the screen holds now drives the frame drawn over it: the barter window or the character sheet, or null while the screen draws its own art.</summary>
    internal SteamwovenKitWindow? KitBodyWindow => _kitBodyWindow;
    /// <summary>ARC-UI-3041: the overlays of the shop's barter window frame (the kit's doll view and doll cover) while the kit is its body; null otherwise.</summary>
    internal AreaKitBackdrop? KitBackdrop => _kitBackdrop;
    /// <summary>ARC-UI-3041: the kit's body window changed (the kit began or stopped being the screen's backdrop, or the barter window's variant switched): the frame over it sets or lifts its own backdrop skip.</summary>
    internal event Action? KitBodyChanged;
    private AreaKitTwoStripLayer Kit => _editor ? _kitEditor : _kitShop;
    /// <summary>ARC-UI-3041: the kit's draws of the screen as of the last draw (top strip, body piece or pieces, bottom strip), null while the screen draws its own art.</summary>
    internal IReadOnlyList<SteamwovenKitDraw>? KitScreenForSelfTest => Kit.DrawsForSelfTest;
    /// <summary>ARC-UI-3041: the original arts the last draw painted as its backdrop (336 and 335 as far as the pack has them; none over the kit).</summary>
    internal IReadOnlyList<int> OwnBackdropDrawnForSelfTest => _ownBackdropDrawn;
    /// <summary>ARC-UI-3041: true when the kit part drawn for <paramref name="index"/> (0 top, then the body piece or pieces, last the bottom strip) is sampled nearest (its draw is exact).</summary>
    internal bool KitSampledNearestForSelfTest(int index) => Kit.SampledNearestForSelfTest(index);
    /// <summary>ARC-UI-3041: the art frames the view holds, by number.</summary>
    internal IReadOnlyDictionary<int, Texture2D[]> TexturesForSelfTest => _art;
    /// <summary>ARC-UI-3041: the screen the view draws (the creation editor or the opening shop).</summary>
    internal SteamwovenKitScreen KitScreenKindForSelfTest => _editor ? SteamwovenKitScreen.CharacterEditor : SteamwovenKitScreen.OpeningShop;

    /// <summary>ARC-UI-3041: the merchant background art of the shop's barter window (235, or 825 for a follower): a follower's side has its own kit piece. The draw follows.</summary>
    public void SetBarterBackground(int art)
    {
        if (art == _barterBackground) return;
        _barterBackground = art;
        QueueRedraw();
    }
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
        Claim.Release();
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _artSet = true;
    }

    /// <summary>Shows the window with the controller's <paramref name="focus"/> row.</summary>
    public void Render(int focus, bool editor = false)
    {
        if (!_artSet) throw new InvalidOperationException("ARC-UI-069: the shop art is not set");
        // ARC-UI-3041: the window changes between the shop and the editor; the one left gives its resident body back.
        if (editor != _editor) { (editor ? _kitShop : _kitEditor).Release(); _editor = editor; }
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
        // ARC-UI-3041 (SWI-RUN-5): the body part goes with the screen.
        ReleaseKit();
    }

    private void ReleaseKit()
    {
        _kitShop.Release(); _kitEditor.Release();
        _ownBackdropDrawn = [];
        PublishKitBody(null, null);
    }

    private void PublishKitBody(SteamwovenKitWindow? window, AreaKitBackdrop? backdrop)
    {
        if (_kitBodyWindow == window && _kitBackdrop == backdrop) return;
        (_kitBodyWindow, _kitBackdrop) = (window, backdrop);
        KitBodyChanged?.Invoke();
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport (as the other menu windows' art).</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        // ARC-UI-2575: with the Steamwoven layout the screen's 800 × 400 body lies in the window zone at the layout's unit scale; ARC-UI-2891: with the column option the whole 800 × 600 screen is fitted to the window's height (SteamwovenColumnMenu.Fit).
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

    private int Frame(AreaShopArtButton button)
    {
        var frames = _art.TryGetValue(AreaShopArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        return AreaCharacterArtDraw.Frame(frames, _pressed is not null, _pressed == button, _hovered == button,
            _focusLit && AreaShopArtLayout.Focused(_focus) == button);
    }

    public override void _Draw()
    {
        // ARC-UI-2575: the art is shared among the screens of the pre-made family, so the view on screen claims its frames at every draw (only while the Steamwoven layout places it).
        if (_zoned) Claim.Claim(_art.Values.SelectMany(frames => frames));
        if (!_artSet) return;
        // No background of its own (-1, MW1): the screen's black and the strips. ARC-UI-3041: over a complete kit the strips and the window's body are the kit's.
        var window = _editor ? SteamwovenKitWindow.Character : AreaBarterArtLayout.CycleShown(_barterBackground) ? SteamwovenKitWindow.BarterFollower : SteamwovenKitWindow.Barter;
        var kit = Kit;
        kit.Compose(_zoned, GetViewportRect().Size, window);
        _ownBackdropDrawn = AreaCharacterArtDraw.Window(this, _art, -1, (kit, Position, Scale));
        PublishKitBody(kit.Active ? window : null, kit.Backdrop);
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
        // ARC-UI-3041 (SWI-RUN-5): a view hidden by any route lets the kit's body go.
        if (what == NotificationVisibilityChanged && !IsVisibleInTree()) ReleaseKit();
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
