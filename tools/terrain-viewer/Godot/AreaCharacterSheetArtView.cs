using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The three fonts of the sheet's original look.</summary>
public sealed record AreaCharacterSheetFonts(AreaArtFontFace Label, AreaArtFontFace Flare, AreaArtFontFace Cloister, AreaArtFontFace? Icons = null,
    AreaArtFontFace? Nick = null, AreaArtFontFace? Garmond = null)
{
    public AreaArtFontFace For(AreaSheetFont font) => font switch
    {
        AreaSheetFont.Flare => Flare, AreaSheetFont.Cloister => Cloister, _ => Label,
    };
}

/// <summary>
/// ARC-UI-075: the character sheet's main page in the original's look (<see cref="AreaCharacterSheetArtLayout"/>, ledger CHW1–CHW9): the
/// background art 22 with the portrait, the numbers in the original's fonts, the alignment gauge and aptitude marker, the ten + and −
/// art buttons and the four tabs, over a window of 800 × 400 placed at the screen's (0, 41) between the interface bars. The pointer over a
/// button or a region of the exe's table asks for its text (the text bar); a release inside the window's close rectangle closes the sheet
/// (0x55AD8B). The right-hand panel (503, 63, 235 × 325) is left to the host. PROJECT: a button takes the pointer on its rectangle, not on its
/// opaque pixels; the controller has no focus row here.
/// </summary>
public partial class AreaCharacterSheetArtView : Control
{
    private enum Kind { Plus, Minus, Tab }
    private readonly record struct Hit(Kind Kind, int Index);

    /// <summary>The close rectangle in the modes 1 and 2 (struct 0x5C8930: art 198 PCWinCvr; a mouse-up inside it closes the sheet).</summary>
    public static (int X, int Y, int Width, int Height) CloseRectangle { get; } = (12, 10, 89, 89);

    private static readonly Hit[] Hits =
    [
        .. Enumerable.Range(0, AreaStatsPage.Rows).Select(row => new Hit(Kind.Plus, row)),
        .. Enumerable.Range(0, AreaStatsPage.Rows).Select(row => new Hit(Kind.Minus, row)),
        .. Enum.GetValues<AreaSheetTab>().Select(tab => new Hit(Kind.Tab, (int)tab)),
    ];

    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaCharacterSheetFonts? _fonts;
    private AreaCharacterSheetTexts _texts = AreaCharacterSheetTexts.Project("not set");
    private (int X, int Y)[] _alignmentHot = [];
    private AreaCharacterSheetView? _view;
    private Texture2D? _portrait;
    private AreaSheetTab _tab = AreaSheetTab.Skills;
    private Hit? _hovered, _pressed;
    private int? _region;

    /// <summary>A + (true) or − (false) of a row (0 hit points, 1 fatigue, 2..9 the stats) released over itself.</summary>
    public event Action<int, bool>? StatPressed;
    public event Action<AreaSheetTab>? TabSelected;
    /// <summary>The pointer entered a button or region (its message id), or left (null).</summary>
    public event Action<int?>? HoverChanged;
    public event Action? CloseRequested;
    /// <summary>An interface sound (3000 / 3001 an art button's press and release).</summary>
    public event Action<int>? SoundRequested;

    /// <summary>ARC-UI-076: the skills and tech pages' control at the panel's place (hidden until the pages' art is set and a page is chosen).</summary>
    public AreaCharacterSheetPageView Pages { get; } = new();

    public bool ArtSet => _fonts is not null;
    public AreaSheetTab Tab => _tab;
    internal AreaCharacterSheetView? ViewForSelfTest => Visible ? _view : null;
    internal int? RegionForSelfTest => _region;
    internal bool PortraitForSelfTest => Visible && _portrait is not null;
    internal IReadOnlyList<AreaSheetText> TextsForSelfTest => _view is { } view && _fonts is { } fonts
        ? AreaCharacterSheetArtLayout.Texts(view, _texts, (font, text) => fonts.For(font).Font.Measure(text).Width) : [];
    /// <summary>The window position of a window-relative point of the sheet.</summary>
    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "CharacterSheetArt";
        Size = new Vector2(AreaCharacterSheetArtLayout.Width, AreaCharacterSheetArtLayout.Height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
        AddChild(Pages);
    }

    /// <param name="alignmentHot">The hot point of each frame of art 254.</param>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts, AreaCharacterSheetTexts texts, (int X, int Y)[] alignmentHot)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _alignmentHot = alignmentHot ?? [];
    }

    public AreaCharacterSheetTexts Texts => _texts;

    public void Render(AreaCharacterSheetView view, Texture2D? portrait)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-075: the sheet art is not set");
        if (!Visible) (_hovered, _pressed, _region, _tab) = (null, null, null, AreaSheetTab.Skills);
        (_view, _portrait) = (view, portrait);
        Visible = true;
        Place();
        QueueRedraw();
    }

    public void SelectTab(AreaSheetTab tab)
    {
        if (_tab == tab) return;
        _tab = tab; QueueRedraw();
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _region, _view, _portrait) = (null, null, null, null, null);
    }

    /// <summary>The window at the bars' whole factor, under the top strip and centred.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), AreaGameBarPanel.TopInset);
    }

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private (int Art, int X, int Y) Place(Hit hit) => hit.Kind switch
    {
        Kind.Plus => (AreaCharacterSheetArtLayout.PlusArt, AreaCharacterSheetArtLayout.Plus(hit.Index).X, AreaCharacterSheetArtLayout.Plus(hit.Index).Y),
        Kind.Minus => (AreaCharacterSheetArtLayout.MinusArt, AreaCharacterSheetArtLayout.Minus(hit.Index).X, AreaCharacterSheetArtLayout.Minus(hit.Index).Y),
        _ => (AreaCharacterSheetArtLayout.Tab((AreaSheetTab)hit.Index).Art, AreaCharacterSheetArtLayout.Tab((AreaSheetTab)hit.Index).X,
            AreaCharacterSheetArtLayout.Tab((AreaSheetTab)hit.Index).Y),
    };

    private int Frame(Hit hit)
    {
        var frames = _art.TryGetValue(Place(hit).Art, out var art) ? art.Length : 1;
        return AreaCharacterArtDraw.Frame(frames, _pressed is not null, _pressed == hit, _hovered == hit);
    }

    private int MessageOf(Hit hit) => hit.Kind == Kind.Tab ? AreaCharacterSheetArtLayout.Tab((AreaSheetTab)hit.Index).Hint : AreaCharacterSheetArtLayout.RowHint(hit.Index);

    /// <summary>The buttons in the exe's creation order (plus, minus, tabs): the first one under the pointer wins (0x5380F0).</summary>
    private Hit? HitAt(Vector2 at)
    {
        foreach (var hit in Hits)
        {
            var (art, x, y) = Place(hit);
            if (Art(art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return hit;
        }
        return null;
    }

    public override void _Draw()
    {
        if (_fonts is not { } fonts || _view is not { } view) return;
        if (Art(AreaCharacterSheetArtLayout.BackgroundArt) is { } window) DrawTextureRect(window, new Rect2(0, 0, window.GetWidth(), window.GetHeight()), false);
        // The portrait in its slot (141, 21), at its own size (0x4CE470).
        if (_portrait is not null) DrawTextureRect(_portrait, new Rect2(141, 21, _portrait.GetWidth(), _portrait.GetHeight()), false);
        // 0x55EC90: the alignment gauge by its hot point, the aptitude marker by its top-left.
        var frames = _art.TryGetValue(AreaCharacterSheetArtLayout.AlignmentArt, out var gauge) ? gauge.Length : 0;
        var frame = AreaCharacterSheetArtLayout.AlignmentFrame(view.Alignment, frames);
        if (Art(AreaCharacterSheetArtLayout.AlignmentArt, frame) is { } needle)
        {
            var (hotX, hotY) = frame < _alignmentHot.Length ? _alignmentHot[frame] : (0, 0);
            var (x, y) = AreaCharacterSheetArtLayout.AlignmentTopLeft(hotX, hotY);
            DrawTextureRect(needle, new Rect2(x, y, needle.GetWidth(), needle.GetHeight()), false);
        }
        if (Art(AreaCharacterSheetArtLayout.AptitudeArt) is { } marker)
        {
            var (x, y) = AreaCharacterSheetArtLayout.AptitudeMarker(view.MagickAptitude - view.TechAptitude);
            DrawTextureRect(marker, new Rect2(x, y, marker.GetWidth(), marker.GetHeight()), false);
        }
        foreach (var text in AreaCharacterSheetArtLayout.Texts(view, _texts, (font, value) => fonts.For(font).Font.Measure(value).Width))
        {
            var face = fonts.For(text.Font);
            AreaArtTextDraw.Text(this, face, text.Text, AreaCharacterSheetArtLayout.Left(text, face.Font.Measure(text.Text).Width), text.Y,
                AreaCharacterSheetArtLayout.Rgb(text.Colour));
        }
        foreach (var hit in Hits)
        {
            var (number, x, y) = Place(hit);
            if (hit.Kind == Kind.Tab && Art(number) is null) continue;
            var shown = hit.Kind == Kind.Tab && hit.Index == (int)_tab ? AreaBarButtonFrameDown(number) : Frame(hit);
            if (Art(number, shown) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
    }

    /// <summary>The shown tab keeps its pressed frame (the page is on).</summary>
    private int AreaBarButtonFrameDown(int number) => AreaGameBarLayout.Frame(AreaBarButtonLook.Down, Math.Max(1, _art.TryGetValue(number, out var art) ? art.Length : 1));

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || (_hovered is null && _region is null)) return;
        (_hovered, _region) = (null, null);
        HoverChanged?.Invoke(null);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = HitAt(motion.Position);
                int? region = over is { } hit ? MessageOf(hit) : AreaCharacterSheetArtLayout.RegionAt(motion.Position.X, motion.Position.Y);
                // The message window shows a hover entry only when nothing was stored in the last 3 s (ML2), so the text is written again on each
                // move over a region, as the bars' hover is (GameBarHover); leaving it takes the text away.
                if (region is not null || region != _region) HoverChanged?.Invoke(region);
                if (over == _hovered && region == _region) break;
                (_hovered, _region) = (over, region);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = HitAt(button.Position);
                _hovered = at;
                if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                }
                else
                {
                    var held = _pressed;
                    _pressed = null;
                    if (held is { } pressed && at == pressed)
                    {
                        SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                        if (pressed.Kind == Kind.Tab) { var tab = (AreaSheetTab)pressed.Index; SelectTab(tab); TabSelected?.Invoke(tab); }
                        else StatPressed?.Invoke(pressed.Index, pressed.Kind == Kind.Plus);
                    }
                    // 0x55AD8B: a mouse-up inside the close rectangle closes the sheet, whatever lies there.
                    else if (held is null && AreaPremadeBrowserArtLayout.Contains(CloseRectangle, button.Position.X, button.Position.Y)) CloseRequested?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}
