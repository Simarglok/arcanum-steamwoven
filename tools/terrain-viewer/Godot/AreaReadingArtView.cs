using Arcanum.Data.Written;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1877: the reading window in the original's look (<see cref="AreaReadingArtLayout"/>, ledger WR17–WR23): the frame art 494 at (0, 0), the subtype's background art at (132, 0), the lens
/// (art 231 over the round view of the hero, set by the panel) whose rectangle closes the window on a left release, a book's previous and next buttons (arts 495 and 496, shown only when there is
/// such a page) and the text laid out as the original's draw lays it out (<see cref="AreaReadingFlow"/>). The control is the window's 800 × 399 in art pixels, scaled by the bars' whole factor and
/// docked at the screen's (0, 41); what falls outside the window is clipped as the original's blit clips it. A picture's art is asked of <see cref="Picture"/> (it is the written item's own
/// number). PROJECT: Left/Right and Page Up/Down turn a book's pages too (the original has the two buttons only).
/// </summary>
public partial class AreaReadingArtView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private IReadOnlyDictionary<int, AreaArtFontFace> _faces = new Dictionary<int, AreaArtFontFace>();
    private AreaReadingView? _view;
    private IReadOnlyList<AreaReadingPage> _pages = [];
    private IReadOnlyList<AreaReadingLine> _single = [];
    private int _spread;
    private Texture2D? _picture;
    private int _hoverPage = -2, _pressedPage = -2;
    // The round view of the hero shows under the lens, so the frame art leaves its square out (the original's copy replaces it, as in the logbook's side panel).
    private bool _lensCut;

    /// <summary>A left release in the lens (0x56BFC0) asks for the window to close.</summary>
    public event Action? CloseRequested;
    /// <summary>A page button released over itself asks for a turn of one spread (−1 previous, 1 next); the app plays the page sound.</summary>
    public event Action<int>? PageRequested;

    /// <summary>The art of a picture (the written item's own number): its frames, or null when it did not read. Set by the app.</summary>
    public Func<int, Texture2D[]?>? Picture { get; set; }

    public bool ArtSet => _faces.Count > 0 && _art.Count > 0;
    public int SpreadIndex => _spread;
    public int Spreads => _view?.Subtype == WrittenSubtype.Book ? Math.Max(1, (_pages.Count + 1) / 2) : 1;
    public bool HasPrevious => _view?.Subtype == WrittenSubtype.Book && _spread > 0;
    public bool HasNext => _view?.Subtype == WrittenSubtype.Book && 2 * _spread + 2 < _pages.Count;
    internal IReadOnlyList<AreaReadingLine> LinesForSelfTest => Lines();
    internal Vector2 WindowPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    /// <summary>The lens's rectangle on the screen (the window's rectangle scaled and moved): the round view's square.</summary>
    internal (Vector2 Origin, float Side) LensOnScreen
    {
        get
        {
            var (x, y, side) = AreaReadingArtLayout.Lens;
            return (GlobalPosition + new Vector2(x, y) * Scale.X, side * Scale.X);
        }
    }

    /// <summary>The round view is shown under the window: the frame art leaves the lens's square out.</summary>
    internal bool LensCut
    {
        get => _lensCut;
        set { if (_lensCut == value) return; _lensCut = value; QueueRedraw(); }
    }

    public override void _Ready()
    {
        Name = "ReadingArt";
        Size = new Vector2(AreaReadingArtLayout.Window.Width, AreaReadingArtLayout.Window.Height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
        AreaArtTextDraw.Prepare(this);
        Visible = false;
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, IReadOnlyDictionary<int, AreaArtFontFace> faces)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _faces = faces ?? throw new ArgumentNullException(nameof(faces));
        Unwatch();
        // The generated interface paints the window's frames at this window's own device scale (the control's scale, the canvas transform's and the viewport's stretch), and the view redraws
        // when a worker's finished frame replaces the one it drew (it draws the textures in its own _Draw).
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) QueueRedraw(); };
        foreach (var texture in art.Values.SelectMany(frames => frames)) Watch(texture);
    }

    private void Watch(Texture2D? texture)
    {
        if (texture is not HdInterfaceTexture hd || _onTextureChanged is null) return;
        hd.ScaleSource = DeviceScale;
        if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
    }

    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;
    private float _drawnScale;

    /// <summary>Device pixels per Legacy pixel of this window (what the generated frames are painted at).</summary>
    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : Scale.X;

    private void Unwatch()
    {
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    /// <summary>Takes the art back (the module changed to one whose art did not read): the panel then shows its diagnostic layout.</summary>
    public void ClearArt() { Unwatch(); _art = new Dictionary<int, Texture2D[]>(); _faces = new Dictionary<int, AreaArtFontFace>(); CloseView(); }

    /// <summary>Shows <paramref name="view"/>'s document from its first spread (the original's window opens a book at its first spread); false when the view carries no structure to draw.</summary>
    public bool Render(AreaReadingView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!ArtSet || view.Art is not { } document) return false;
        _view = view;
        _spread = 0;
        // A picture's art (the written item's own number) is read when the window shows it, not inside the draw, and watched like the others.
        _picture = document.Subtype == WrittenSubtype.Picture ? Picture?.Invoke(document.BackgroundArt)?.FirstOrDefault() : null;
        Watch(_picture);
        _factor = AreaGameBarPanel.ScreenFactor(GetViewportRect().Size);
        Compose(document);
        Visible = true;
        Place();
        QueueRedraw();
        return true;
    }

    // The screen factor the text was laid out at: a font under 9 px at it is drawn with Garmond9 (ARC-UI-1462), whose glyphs are wider, so the layout depends on it.
    private int _factor = 1;

    private void Compose(AreaReadingArtDocument document)
    {
        AreaArtFont FontOf(int number) => Face(number).Font;
        if (document.Subtype == WrittenSubtype.Book) { _pages = AreaReadingFlow.BookPages(document.Body, FontOf); _single = []; }
        else { _pages = []; _single = AreaReadingFlow.Single(document, FontOf); }
    }

    /// <summary>The font art a line of font <paramref name="number"/> is drawn with (<see cref="AreaReadingText.ShownFont"/> at the current screen factor, when that font read), else the font itself.</summary>
    internal int ShownFontNumber(int number)
    {
        var shown = AreaReadingText.ShownFont(number, _factor);
        return shown != number && _faces.ContainsKey(shown) ? shown : number;
    }

    /// <summary>A font's face; a font art that did not read (a markup font the installed game does not hold) draws in the note's font.</summary>
    private AreaArtFontFace Face(int number) =>
        _faces.TryGetValue(ShownFontNumber(number), out var face) ? face : _faces.TryGetValue(AreaReadingArtLayout.NoteFont, out var fallback) ? fallback : _faces.Values.First();

    public void CloseView()
    {
        Visible = false; _view = null; _lensCut = false; _pages = []; _single = []; _spread = 0; _picture = null;
        (_hoverPage, _pressedPage) = (-2, -2);
    }

    /// <summary>Turns the book by <paramref name="direction"/> spreads (−1 previous, 1 next); false when there is none.</summary>
    public bool Turn(int direction)
    {
        if (_view is null || (direction < 0 ? !HasPrevious : !HasNext)) return false;
        _spread += direction < 0 ? -1 : 1;
        QueueRedraw();
        return true;
    }

    /// <summary>The window at the bars' whole factor, docked at the screen's (0, 41) under the top strip.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        // ARC-UI-1462: a new screen factor changes which fonts are under 9 px, so the text is laid out again (a book keeps its spread where it still exists).
        if (_view?.Art is { } document && factor != _factor)
        {
            _factor = factor;
            Compose(document);
            _spread = Math.Clamp(_spread, 0, Spreads - 1);
            QueueRedraw();
        }
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), AreaReadingArtLayout.Window.Y * factor);
        // A new device scale (the window or the stretch changed) asks the textures for frames at it.
        if (IsInsideTree() && _view is not null && MathF.Abs(DeviceScale() - _drawnScale) > 0.001f) QueueRedraw();
    }

    private Texture2D? Texture(int art, int frame = 0) => AreaCharacterArtDraw.Art(_art, art, frame);

    private IReadOnlyList<AreaReadingLine> Lines() =>
        _view?.Subtype == WrittenSubtype.Book ? _pages.Skip(2 * _spread).Take(2).SelectMany(page => page.Lines).ToArray() : _single;

    // The page buttons' frames: idle, lit under the pointer, down while held over it (GB3/GB4).
    private int PageFrame(int page, int art)
    {
        var count = _art.TryGetValue(art, out var frames) ? frames.Length : 1;
        return AreaCharacterArtDraw.Frame(count, _pressedPage > -2, _pressedPage == page, _hoverPage == page);
    }

    public override void _Draw()
    {
        if (_view?.Art is not { } document) return;
        _drawnScale = DeviceScale();
        if (Texture(AreaReadingArtLayout.FrameArt) is { } frame) Frame(frame);
        var corner = AreaReadingArtLayout.BackgroundCorner;
        var background = document.Subtype == WrittenSubtype.Picture ? _picture : Texture(document.BackgroundArt);
        if (background is not null) DrawTextureRect(background, new Rect2(corner.X, corner.Y, background.GetWidth(), background.GetHeight()), false);
        // The lens's art over the copy of the game view (the view-window mode's copy, then the art, as in the logbook).
        if (Texture(AreaReadingArtLayout.LensArt) is { } lens)
            DrawTextureRect(lens, new Rect2(AreaReadingArtLayout.Lens.X, AreaReadingArtLayout.Lens.Y, lens.GetWidth(), lens.GetHeight()), false);
        if (HasPrevious && Texture(AreaReadingArtLayout.PreviousArt, PageFrame(-1, AreaReadingArtLayout.PreviousArt)) is { } previous)
            DrawTextureRect(previous, new Rect2(AreaReadingArtLayout.PreviousCorner.X, AreaReadingArtLayout.PreviousCorner.Y, previous.GetWidth(), previous.GetHeight()), false);
        if (HasNext && Texture(AreaReadingArtLayout.NextArt, PageFrame(1, AreaReadingArtLayout.NextArt)) is { } next)
            DrawTextureRect(next, new Rect2(AreaReadingArtLayout.NextCorner.X, AreaReadingArtLayout.NextCorner.Y, next.GetWidth(), next.GetHeight()), false);
        var colour = AreaReadingArtLayout.Colour(document.Subtype);
        foreach (var line in Lines()) AreaArtTextDraw.Glyphs(this, Face(line.Font), line.Glyphs, line.X, line.Y, colour);
    }

    // The frame art; with the round view under it the lens's square is left out.
    private void Frame(Texture2D frame)
    {
        float width = frame.GetWidth(), height = frame.GetHeight();
        if (!_lensCut) { DrawTextureRect(frame, new Rect2(0, 0, width, height), false); return; }
        var (x, y, edge) = AreaReadingArtLayout.Lens;
        foreach (var (px, py, pw, ph) in new[] { (0f, 0f, width, (float)y), (0f, y + (float)edge, width, height - y - edge), (0f, (float)y, (float)x, (float)edge),
            (x + (float)edge, (float)y, width - x - edge, (float)edge) })
            if (pw > 0 && ph > 0) DrawTextureRectRegion(frame, new Rect2(px, py, pw, ph), new Rect2(px, py, pw, ph));
    }

    // -1 the previous button, 1 the next, -2 neither; a hidden button cannot be hit.
    private int PageAt(Vector2 at)
    {
        if (HasPrevious && Texture(AreaReadingArtLayout.PreviousArt) is { } previous
            && AreaPremadeBrowserArtLayout.Contains((AreaReadingArtLayout.PreviousCorner.X, AreaReadingArtLayout.PreviousCorner.Y, previous.GetWidth(), previous.GetHeight()), at.X, at.Y)) return -1;
        if (HasNext && Texture(AreaReadingArtLayout.NextArt) is { } next
            && AreaPremadeBrowserArtLayout.Contains((AreaReadingArtLayout.NextCorner.X, AreaReadingArtLayout.NextCorner.Y, next.GetWidth(), next.GetHeight()), at.X, at.Y)) return 1;
        return -2;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hoverPage == -2) return;
        _hoverPage = -2;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = PageAt(motion.Position);
                if (over == _hoverPage) break;
                _hoverPage = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var page = PageAt(button.Position);
                _hoverPage = page;
                if (button.Pressed) _pressedPage = page;
                else
                {
                    // The callback 0x56BFC0 tests the lens's rectangle first on a left release: inside it the window closes (the close frees this view's state, so nothing else runs).
                    if (AreaReadingArtLayout.LensContains(button.Position.X, button.Position.Y))
                    {
                        _pressedPage = -2;
                        AcceptEvent(); CloseRequested?.Invoke();
                        break;
                    }
                    var held = _pressedPage;
                    _pressedPage = -2;
                    if (held > -2 && held == page) PageRequested?.Invoke(held);
                }
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // PROJECT: the wheel turns the pages (as the logbook's does).
                PageRequested?.Invoke(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
                AcceptEvent();
                break;
        }
    }
}
