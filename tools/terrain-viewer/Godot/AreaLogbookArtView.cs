using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-074: the logbook in the original's look (<see cref="AreaLogbookArtLayout"/>, ledger LB14–LB17): the side panel, the book and its
/// page art, the seven tab buttons (the shown tab pressed), the previous and next page buttons, the tab's title and the page numbers in the
/// two page halves and the entries on the spread laid out as the original's layout does. The control is the window's 800 × 400 in art pixels,
/// scaled by the bars' whole factor and docked at the screen's (0, 41). PROJECT: arrow keys and Page Up / Down turn the pages too (the
/// original has the two buttons only). ARC-UI-098 (ledger LB23): the PC lens (art 198 over the game view's round view of the hero, set by the panel) and
/// the click in it that closes the window, and a tab's hover hint (the tab's message of <c>logbk_ui.mes</c>, asked of the message window).
/// </summary>
public partial class AreaLogbookArtView : Control
{
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaTextFonts? _fonts;
    private AreaLogbookView? _view;
    private IReadOnlyList<IReadOnlyList<IReadOnlyList<(int X, AreaArtGlyph Glyph)>>> _wrapped = [];
    // What the spreads lay out: the entries' texts, but a background's text cut into column-sized pieces (LB18); each with its entry's style.
    private IReadOnlyList<AreaLogbookStyle> _styles = [];
    private IReadOnlyList<int> _starts = [0];
    private AreaLogbookSpread _spread = new(0, -1, []);
    private int _spreadIndex;
    // The tab the kept spread belongs to: the tab and the spread are kept across a close and an open (LB3).
    private AreaLogbookTab? _pageTab;
    private int _hoverTab = -1, _hoverPage = -2, _pressedTab = -1, _pressedPage = -2;
    // ARC-UI-098: the lens shows the round view under the window, so the side art leaves its square out (the original's copy replaces it, IW33).
    private bool _lensCut;

    /// <summary>A tab button released over itself.</summary>
    public event Action<AreaLogbookTab>? TabRequested;
    /// <summary>ARC-UI-098: a tab's hover hint (LB23) when the pointer enters its button; null when it leaves (the original's hover-in and hover-out).</summary>
    public event Action<string?>? HintRequested;
    /// <summary>ARC-UI-098: a left release inside the lens (LB23) asks for the window to close.</summary>
    public event Action? CloseRequested;
    /// <summary>The page sound of a turned page (the original plays 0xBC2 when a spread is redrawn by a page button).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _fonts is not null && _art.Count > 0;
    /// <summary>ARC-UI-098: the lens's rectangle on the screen (the window's rectangle scaled and moved), the round view's square.</summary>
    internal (Vector2 Origin, float Side) LensOnScreen
    {
        get
        {
            var (x, y, side) = AreaLogbookArtLayout.Lens;
            return (GlobalPosition + new Vector2(x, y) * Scale.X, side * Scale.X);
        }
    }

    /// <summary>ARC-UI-098: the round view is shown under the window: the side art leaves the lens's square out.</summary>
    internal bool LensCut
    {
        get => _lensCut;
        set { if (_lensCut == value) return; _lensCut = value; QueueRedraw(); }
    }

    public int SpreadIndex => _spreadIndex;
    public AreaLogbookSpread Spread => _spread;
    public bool HasPrevious => _spreadIndex > 0;
    public bool HasNext => _spread.End < _wrapped.Count - 1 && _starts.Count > _spreadIndex + 1;
    internal Vector2 WindowPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _EnterTree() => AreaGameBarPanel.KitBackgroundChanged += QueueRedraw;

    public override void _ExitTree() => AreaGameBarPanel.KitBackgroundChanged -= QueueRedraw;

    private static bool KitBackgroundShown => AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Logbook;

    public override void _Ready()
    {
        Name = "LogbookArt";
        Size = new Vector2(AreaLogbookArtLayout.Window.Width, AreaLogbookArtLayout.Window.Height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        AreaArtTextDraw.Prepare(this);
        Visible = false;
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaTextFonts fonts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        Unwatch();
        // ARC-UI-1196: the pack's frames are area-resampled at this window's own device scale (InterfaceResample) (the control's scale, the canvas transform's and the viewport's
        // stretch: the bars' whole factor times the stretch), and the view redraws when a worker's finished frame replaces the one it drew (it draws the textures in its own _Draw).
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) QueueRedraw(); };
        foreach (var texture in art.Values.SelectMany(frames => frames))
        {
            if (texture is not HdInterfaceTexture hd) continue;
            hd.ScaleSource = DeviceScale;
            if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
        }
    }

    // ARC-UI-1196: the frames this view drew with its own device scale.
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;

    /// <summary>ARC-UI-1196: device pixels per Legacy pixel of this window (what the pack's frames are resampled at).</summary>
    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : Scale.X;

    private void Unwatch()
    {
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    /// <summary>Takes the art back (the module changed to one whose art did not read): the panel then shows its list.</summary>
    public void ClearArt() { Unwatch(); _art = new Dictionary<int, Texture2D[]>(); _fonts = null; ResetPage(); CloseView(); }

    /// <summary>Shows <paramref name="view"/>. A tab other than the one shown starts at the first spread (the tab switch resets the page,
    /// LB3); the same tab rebuilt keeps its spread as far as the list still reaches it.</summary>
    public void Render(AreaLogbookView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-074: the logbook art is not set");
        var sameTab = _pageTab == view.Tab;
        _pageTab = view.Tab;
        _view = view;
        var font = _fonts.Text.Font;
        var width = AreaLogbookArtLayout.Columns[0].Width;
        // A rebuilt book redraws the spread its stored start entry begins (LB15), whatever the new wrapping did to the numbering.
        var keptStart = sameTab && _starts.Count > _spreadIndex ? _starts[_spreadIndex] : 0;
        var lineHeight = _fonts.TextLineHeight;
        var texts = new List<string>();
        var styles = new List<AreaLogbookStyle>();
        foreach (var entry in view.Entries)
        {
            var line = AreaLogbook.Line(entry);
            var pieces = view.Tab == AreaLogbookTab.Background
                ? AreaLogbookArtLayout.SplitPieces(line, piece => AreaPremadeBrowserArtLayout.Wrap(font, piece, width).Count * lineHeight, AreaLogbookArtLayout.Columns[0].Height)
                : [line];
            foreach (var piece in pieces) { texts.Add(piece); styles.Add(entry.Style); }
        }
        _styles = styles;
        _wrapped = texts.Select(text => AreaPremadeBrowserArtLayout.Wrap(font, text, width)).ToArray();
        _starts = AreaLogbookArtLayout.Starts(_wrapped.Select(lines => lines.Count).ToArray(), _fonts.TextLineHeight);
        _spreadIndex = AreaLogbookArtLayout.SpreadOf(_starts, keptStart);
        Layout();
        Visible = true;
        Place();
        QueueRedraw();
    }

    /// <summary>A game reset (a new session): the first spread again (the original sets the rumors tab and the first page, LB3).</summary>
    public void ResetPage() { _pageTab = null; _spreadIndex = 0; _starts = [0]; }

    public void CloseView()
    {
        // PROJECT: the window going away takes a tab's hint with it (the original's destroy path is not read for a leave).
        if (_hoverTab >= 0) HintRequested?.Invoke(null);
        Visible = false; _view = null; _lensCut = false;
        (_hoverTab, _hoverPage, _pressedTab, _pressedPage) = (-1, -2, -1, -2);
    }

    private void Layout() => _spread = AreaLogbookArtLayout.Spread(_wrapped.Select(lines => lines.Count).ToArray(), _fonts!.TextLineHeight, _starts[_spreadIndex]);

    /// <summary>Turns the page by <paramref name="direction"/> spreads (−1 previous, 1 next); false when there is none (0x53F640, 0x53F6A0).</summary>
    public bool Turn(int direction)
    {
        if (_view is null || (direction < 0 ? !HasPrevious : !HasNext)) return false;
        _spreadIndex += direction < 0 ? -1 : 1;
        Layout();
        SoundRequested?.Invoke(TerrainViewerApp.BookPageSound);
        QueueRedraw();
        return true;
    }

    /// <summary>The window at the bars' whole factor, docked at the screen's (0, 41) under the top strip.</summary>
    public void Place()
    {
        var size = GetViewportRect().Size;
        // ARC-UI-2575: with the Steamwoven layout the window lies in the window zone at the layout's unit scale.
        if (AreaGameBarPanel.ZoneWindow(size, AreaLogbookArtLayout.Window.Width, AreaLogbookArtLayout.Window.Height) is { } zone)
        {
            Scale = new Vector2((float)zone.Scale, (float)zone.Scale);
            Position = new Vector2((float)zone.X, (float)zone.Y);
            RedrawOnScaleChange();
            return;
        }
        var factor = AreaGameBarPanel.ScreenFactor(size);
        Scale = new Vector2(factor, factor);
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), AreaLogbookArtLayout.Window.Y * factor);
        RedrawOnScaleChange();
    }

    // The pack's frames are area-resampled at the window's device scale: a new scale draws them again.
    private float _drawnScale;

    private void RedrawOnScaleChange()
    {
        var device = DeviceScale();
        if (Math.Abs(device - _drawnScale) < 0.0005f) return;
        _drawnScale = device;
        QueueRedraw();
    }

    private Texture2D? Texture(int art, int frame = 0) => AreaCharacterArtDraw.Art(_art, art, frame);

    private int TabFrame(int index)
    {
        var count = _art.TryGetValue(AreaLogbookArtLayout.Tabs[index].Art, out var frames) ? frames.Length : 1;
        // The radio group presses the shown tab's button and keeps it down (0x538670).
        if (_view is not null && (int)_view.Tab == index) return AreaGameBarLayout.Frame(AreaBarButtonLook.Down, Math.Max(1, count));
        return AreaCharacterArtDraw.Frame(count, _pressedTab >= 0, _pressedTab == index, _hoverTab == index);
    }

    private int PageFrame(int page, int art)
    {
        var count = _art.TryGetValue(art, out var frames) ? frames.Length : 1;
        return AreaCharacterArtDraw.Frame(count, _pressedPage > -2, _pressedPage == page, _hoverPage == page);
    }

    public override void _Draw()
    {
        if (_fonts is null || _view is null) return;
        // ARC-UI-3037: over the kit's logbook background (594 + 2286 master px) the side panel 264 and the book 260 are not drawn; the page art 261 (the cloth frame round the text), the tabs, the lens art and the text stay.
        var kit = KitBackgroundShown;
        if (kit) AreaKitStandIns.Logbook();
        if (!kit && Texture(AreaLogbookArtLayout.SideArt) is { } side) Side(side);
        if (!kit && Texture(AreaLogbookArtLayout.BookArt) is { } book) DrawTextureRect(book, new Rect2(AreaLogbookArtLayout.BookCorner.X, AreaLogbookArtLayout.BookCorner.Y, book.GetWidth(), book.GetHeight()), false);
        if (Texture(AreaLogbookArtLayout.PageArt) is { } page) DrawTextureRect(page, new Rect2(AreaLogbookArtLayout.PageCorner.X, AreaLogbookArtLayout.PageCorner.Y, page.GetWidth(), page.GetHeight()), false);
        for (var i = 0; i < AreaLogbookArtLayout.Tabs.Count; i++)
        {
            var (art, x, y) = AreaLogbookArtLayout.Tabs[i];
            if (Texture(art, TabFrame(i)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
        // ARC-UI-098: the lens's art over the copy of the game view (0x51DA80 after the mode's copy, LB23), drawn when it reads.
        if (Texture(AreaLogbookArtLayout.LensArt) is { } lens)
            DrawTextureRect(lens, new Rect2(AreaLogbookArtLayout.Lens.X, AreaLogbookArtLayout.Lens.Y, lens.GetWidth(), lens.GetHeight()), false);
        if (HasPrevious && Texture(AreaLogbookArtLayout.PreviousArt, PageFrame(-1, AreaLogbookArtLayout.PreviousArt)) is { } previous)
            DrawTextureRect(previous, new Rect2(AreaLogbookArtLayout.PreviousCorner.X, AreaLogbookArtLayout.PreviousCorner.Y, previous.GetWidth(), previous.GetHeight()), false);
        if (HasNext && Texture(AreaLogbookArtLayout.NextArt, PageFrame(1, AreaLogbookArtLayout.NextArt)) is { } next)
            DrawTextureRect(next, new Rect2(AreaLogbookArtLayout.NextCorner.X, AreaLogbookArtLayout.NextCorner.Y, next.GetWidth(), next.GetHeight()), false);

        // The title in both halves and the two page numbers (0x53F6E0): centred in their rectangles.
        var title = AreaLogbookArtLayout.TitleText(_view.Title ?? AreaLogbook.TabName(_view.Tab));
        foreach (var rect in AreaLogbookArtLayout.Titles) Centred(_fonts.Title, rect, title);
        for (var half = 0; half < 2; half++)
            Centred(_fonts.Text, AreaLogbookArtLayout.PageNumbers[half], AreaLogbookArtLayout.PageNumberText(AreaLogbookArtLayout.LeftPage(_spreadIndex) + half));

        var lineHeight = _fonts.TextLineHeight;
        foreach (var placed in _spread.Placed)
        {
            var column = AreaLogbookArtLayout.Columns[placed.Column];
            var (colour, struck) = AreaLogbookArtLayout.Look(_styles[placed.Entry]);
            for (var line = 0; line < placed.Lines; line++)
            {
                var glyphs = _wrapped[placed.Entry][line];
                var y = column.Y + placed.Y + line * lineHeight;
                AreaArtTextDraw.Glyphs(this, _fonts.Text, glyphs, column.X, y, colour);
                if (struck && glyphs.Count > 0)
                    DrawRect(new Rect2(column.X, y + lineHeight / 2, glyphs[^1].X + glyphs[^1].Glyph.Width, 1), new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f));
            }
        }
    }

    // The side panel; with the round view under it the lens's square is left out.
    private void Side(Texture2D side)
    {
        float width = side.GetWidth(), height = side.GetHeight();
        if (!_lensCut) { DrawTextureRect(side, new Rect2(0, 0, width, height), false); return; }
        var (x, y, edge) = AreaLogbookArtLayout.Lens;
        foreach (var (px, py, pw, ph) in new[] { (0f, 0f, width, (float)y), (0f, y + (float)edge, width, height - y - edge), (0f, (float)y, (float)x, (float)edge),
            (x + (float)edge, (float)y, width - x - edge, (float)edge) })
            if (pw > 0 && ph > 0) DrawTextureRectRegion(side, new Rect2(px, py, pw, ph), new Rect2(px, py, pw, ph));
    }

    private void Centred(AreaArtFontFace face, (int X, int Y, int Width, int Height) rect, string text) =>
        AreaArtTextDraw.Text(this, face, text, AreaPremadeBrowserArtLayout.Centred(rect, face.Font.Measure(text).Width), rect.Y, AreaLogbookArtLayout.Black);

    private int TabAt(Vector2 at)
    {
        for (var i = 0; i < AreaLogbookArtLayout.Tabs.Count; i++)
        {
            var (art, x, y) = AreaLogbookArtLayout.Tabs[i];
            if (Texture(art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return i;
        }
        return -1;
    }

    // -1 the previous button, 1 the next, -2 neither; a hidden button cannot be hit.
    private int PageAt(Vector2 at)
    {
        if (HasPrevious && Texture(AreaLogbookArtLayout.PreviousArt) is { } previous
            && AreaPremadeBrowserArtLayout.Contains((AreaLogbookArtLayout.PreviousCorner.X, AreaLogbookArtLayout.PreviousCorner.Y, previous.GetWidth(), previous.GetHeight()), at.X, at.Y)) return -1;
        if (HasNext && Texture(AreaLogbookArtLayout.NextArt) is { } next
            && AreaPremadeBrowserArtLayout.Contains((AreaLogbookArtLayout.NextCorner.X, AreaLogbookArtLayout.NextCorner.Y, next.GetWidth(), next.GetHeight()), at.X, at.Y)) return 1;
        return -2;
    }

    // A change of the hovered tab is the original's hover-out of the old button and hover-in of the new one (subtypes 3 and 2): the hint of the tab
    // entered is asked for, and none when the pointer is over no tab.
    private void SetHover(int tab, int page)
    {
        var changed = tab != _hoverTab;
        (_hoverTab, _hoverPage) = (tab, page);
        if (changed) HintRequested?.Invoke(tab >= 0 && _view is not null ? _view.HintFor((AreaLogbookTab)tab) : null);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || (_hoverTab < 0 && _hoverPage == -2)) return;
        SetHover(-1, -2);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var (tab, pageButton) = (TabAt(motion.Position), PageAt(motion.Position));
                if (tab == _hoverTab && pageButton == _hoverPage) break;
                SetHover(tab, pageButton);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var (overTab, overPage) = (TabAt(button.Position), PageAt(button.Position));
                SetHover(overTab, overPage);
                if (button.Pressed) (_pressedTab, _pressedPage) = (overTab, overPage);
                else
                {
                    // ARC-UI-098 (LB23, callback 0x53F350): the first test of a left release is the lens's rectangle; inside it the window closes.
                    // The close frees this view's state with the window, so nothing else runs.
                    if (AreaLogbookArtLayout.LensContains(button.Position.X, button.Position.Y))
                    {
                        (_pressedTab, _pressedPage) = (-1, -2);
                        AcceptEvent(); CloseRequested?.Invoke();
                        break;
                    }
                    var (heldTab, heldPage) = (_pressedTab, _pressedPage);
                    (_pressedTab, _pressedPage) = (-1, -2);
                    if (heldTab >= 0 && heldTab == overTab) TabRequested?.Invoke((AreaLogbookTab)heldTab);
                    else if (heldPage > -2 && heldPage == overPage) Turn(heldPage);
                }
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                // PROJECT: the wheel turns the pages.
                Turn(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
                AcceptEvent();
                break;
        }
    }
}
