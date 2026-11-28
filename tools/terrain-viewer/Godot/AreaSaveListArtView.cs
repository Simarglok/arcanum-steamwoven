using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-062: the save and load window in the original's look (<see cref="AreaSaveListArtLayout"/>, ledger MW11–MW15): the strips and
/// the background over the 800 × 600 screen at the bars' whole factor, centred in the window; the list's rows in the art font from the
/// scroll top, the selected row gold; the three art buttons with their idle, down and lit frames (GB4) and push behaviour (GB3:
/// down on the press, acting on a release over it), 3000 on the press and 3001 on the release (GB7). A press on a row selects it.
/// ARC-UI-067 (MW23–MW25): the selected save's picture at its own size from the preview's corner, its info texts and portrait over it
/// while the info display is on, 5005 in red when its info did not read, black with no save (or the save window's new-save row), the
/// cover 748 over the preview, the lens 746, and the toggle 747: a toggle button (GB3) whose press turning it on hides the info and
/// whose next press shows it again (3000 on, 3001 off, as the bar's toggles); the info display's state outlives the window while the
/// button opens up (0x5C4790 is set only by the toggle).
/// PROJECT: the wheel over the list scrolls it (the scroll bar widget's art is not read, F0), and while a question is open the buttons,
/// the toggle and the rows do nothing (the original's question box is modal, not read here).
/// ARC-UI-3038 (census M05, M06): over a complete kit (<see cref="SteamwovenKitScreen.SaveLoad"/>: the top strip, the body background and the bottom strip of the baked set) the screen draws none of the original's arts 336, 335 and 745
/// (<see cref="SteamwovenKitComposer.OwnBackdropArts"/>) and the kit's three parts instead, in the plate's device rectangles; the preview's field and the bottom strip's message box are plain black stand-ins until the kit has them (<see cref="AreaKitStandIns"/>), and
/// the preview, the list, the lens, the cover and the buttons are drawn as before. A part the set lacks, holds at another size or does not load gives the screen's own art whole.
/// </summary>
public partial class AreaSaveListArtView : Control
{
    private static readonly AreaSaveListArtButton[] AllButtons = Enum.GetValues<AreaSaveListArtButton>();
    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    // ARC-UI-2575: the pack's frames are area-resampled at this view's own device scale (the unit scale of the Steamwoven layout).
    private AreaDeviceScale? _claim;
    private AreaDeviceScale Claim => _claim ??= new(this);
    private bool _zoned;
    private AreaArtFont? _font;
    private AreaArtFontFace? _face;
    private IReadOnlyList<string> _rows = [];
    private Texture2D? _preview, _portrait;
    private AreaSaveInfo? _info;
    private bool _unread, _toggleDown, _overToggle, _infoShown = true;
    private AreaSaveInfoMessages _messages = AreaSaveInfoMessages.Project;
    private AreaSaveDateOrder _dateOrder;
    private AreaSaveListMode _mode;
    private int _selected = -1, _top;
    private bool _blocked;
    private AreaSaveListArtButton? _hovered, _pressed;
    // ARC-UI-3038: the kit's three parts of the screen (the shared two-strip layer, ARC-UI-3039).
    private readonly AreaKitTwoStripLayer _kit = new(SteamwovenKitScreen.SaveLoad, "twostrip-saveload");
    private IReadOnlyList<int> _ownBackdropDrawn = [];

    /// <summary>A press on a row (its index in the list).</summary>
    public event Action<int>? RowPressed;
    /// <summary>A button released over itself after its press.</summary>
    public event Action<AreaSaveListArtButton>? ButtonPressed;
    /// <summary>A button's interface sound (3000 the press, 3001 the release).</summary>
    public event Action<int>? SoundRequested;

    public bool ArtSet => _font is not null;
    internal IReadOnlyDictionary<int, Texture2D[]> TexturesForSelfTest => _art;
    internal int TopForSelfTest => _top;
    /// <summary>The shown rows' texts as drawn (fitted), from the scroll top.</summary>
    internal IReadOnlyList<string> ShownRowsForSelfTest => Visible && _font is { } font
        ? [.. Enumerable.Range(_top, Math.Clamp(_rows.Count - _top, 0, AreaSaveListArtLayout.ShownRows))
            .Select(row => AreaSaveListArtLayout.Fit(font, _rows[row], AreaSaveListArtLayout.Row(0).Width))] : [];
    /// <summary>ARC-UI-3038: the kit's three draws of the screen as of the last draw (top strip, body, bottom strip), null while the screen draws its own art.</summary>
    internal IReadOnlyList<SteamwovenKitDraw>? KitScreenForSelfTest => _kit.DrawsForSelfTest;
    /// <summary>ARC-UI-3038: the original arts drawn as the backdrop in the last draw (336, 335, 745 as far as the pack has them; none over the kit).</summary>
    internal IReadOnlyList<int> OwnBackdropDrawnForSelfTest => _ownBackdropDrawn;
    /// <summary>ARC-UI-3038: true when the kit part drawn for <paramref name="index"/> (0 top, 1 body, 2 bottom) is sampled nearest (its draw is exact).</summary>
    internal bool KitSampledNearestForSelfTest(int index) => _kit.SampledNearestForSelfTest(index);
    internal int FrameForSelfTest(AreaSaveListArtButton button) => Frame(button);
    internal int ToggleFrameForSelfTest => ToggleFrame();
    internal bool InfoShownForSelfTest => _infoShown;
    /// <summary>The info display's texts as drawn now (none while it is off or there is no info).</summary>
    internal IReadOnlyList<AreaSaveInfoText> InfoTextsForSelfTest => Visible ? InfoTexts() : [];
    internal bool PortraitDrawnForSelfTest => Visible && _infoShown && _info is { Portrait: > 0 } && !_unread && _portrait is not null;
    /// <summary>The window position of a screen point of the 800 × 600 screen.</summary>
    internal Vector2 ScreenPointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);

    public override void _Ready()
    {
        Name = "SaveListArt";
        Size = new Vector2(AreaSaveListArtLayout.ScreenWidth, AreaSaveListArtLayout.ScreenHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    /// <summary>The art's frames by number and the font; ARC-UI-067: the info display's texts and the date's order.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFontFace font, AreaSaveInfoMessages messages, AreaSaveDateOrder dateOrder)
    {
        Claim.Release();
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _face = font ?? throw new ArgumentNullException(nameof(font));
        _font = font.Font;
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _dateOrder = dateOrder;
    }

    /// <summary>Shows the rows (the save window's new-save row first) with <paramref name="selected"/> lit; a new opening starts at the
    /// scroll top 0 (0x543380), a changed selection is scrolled into view (MW15) and other renders keep the top (the wheel's too).
    /// <paramref name="blocked"/>: a question is open. ARC-UI-067: the selected save's <paramref name="info"/> and portrait, or
    /// <paramref name="unread"/> when it has a save whose info did not read (neither for none or the new-save row).</summary>
    public void Render(AreaSaveListMode mode, IReadOnlyList<string> rows, int selected, Texture2D? preview, bool blocked, AreaSaveInfo? info = null,
        Texture2D? portrait = null, bool unread = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (_font is null) throw new InvalidOperationException("ARC-UI-062: the save window art is not set");
        var opened = !Visible;
        // MW25: the toggle is made anew (up) with the window; the info display keeps its state.
        if (opened) { _top = 0; _hovered = null; _pressed = null; _toggleDown = false; }
        var follow = opened || selected != _selected;
        (_mode, _rows, _selected, _preview, _blocked, _info, _portrait, _unread) = (mode, rows, selected, preview, blocked, info, portrait, unread);
        if (blocked) _pressed = null;
        _top = follow ? AreaSaveListArtLayout.ScrollTop(_top, selected, rows.Count) : Math.Clamp(_top, 0, AreaSaveListArtLayout.MaxScrollTop(rows.Count));
        Visible = true;
        Place();
        QueueRedraw();
    }

    /// <summary>The art is dropped (a module whose art did not read): the panel's diagnostic window shows again.</summary>
    public void ClearArt()
    {
        CloseView();
        (_art, _font, _face) = (new Dictionary<int, Texture2D[]>(), null, null);
    }

    public void CloseView()
    {
        Visible = false;
        (_hovered, _pressed, _preview, _portrait, _info) = (null, null, null, null, null);
        _kit.Release();
        _ownBackdropDrawn = [];
    }

    /// <summary>The screen at the bars' whole factor, centred in the viewport.</summary>
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
        Position = new Vector2(AreaGameBarPanel.ScreenLeft(size, factor), MathF.Round((size.Y - AreaSaveListArtLayout.ScreenHeight * factor) / 2));
    }

    private Texture2D? Art(int number, int frame = 0) => _art.TryGetValue(number, out var frames) && frame < frames.Length ? frames[frame] : null;

    private int Frame(AreaSaveListArtButton button)
    {
        var frames = _art.TryGetValue(AreaSaveListArtLayout.Button(button).Art, out var art) ? art.Length : 1;
        // GB3/GB4: a held button shows down over it and idle away from it; another button is not lit while one is held.
        var look = _pressed is { } held ? held == button && _hovered == button ? AreaBarButtonLook.Down : AreaBarButtonLook.Idle
            : _hovered == button ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;
        return AreaGameBarLayout.Frame(look, Math.Max(1, frames));
    }

    /// <summary>The toggle's frame (GB4): down while on (it stays down under the pointer); its two frames show frame 0 when lit.</summary>
    private int ToggleFrame()
    {
        var frames = _art.TryGetValue(AreaSaveListArtLayout.ToggleArt, out var art) ? art.Length : 1;
        // GB3: nothing is lit while a button is held.
        return AreaGameBarLayout.Frame(_toggleDown ? AreaBarButtonLook.Down : _overToggle && _pressed is null ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle,
            Math.Max(1, frames));
    }

    private bool ToggleAt(Vector2 at) => Art(AreaSaveListArtLayout.ToggleArt) is { } texture
        && AreaSaveListArtLayout.Contains((AreaSaveListArtLayout.Toggle.X, AreaSaveListArtLayout.Toggle.Y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y);

    /// <summary>MW23/MW24: the info texts while the display is on and the save's info read; 5005 (red) whatever the display when it did
    /// not.</summary>
    private IReadOnlyList<AreaSaveInfoText> InfoTexts() => _font is not { } font ? []
        : _unread ? [AreaSaveListArtLayout.Unread(font, _messages)]
        : _info is { } info && _infoShown ? AreaSaveListArtLayout.InfoTexts(font, info, _messages, _dateOrder) : [];

    private void DrawText(AreaSaveInfoText text)
    {
        AreaArtTextDraw.Text(this, _face!, text.Text, text.X, text.Y, text.Warning ? AreaSaveListArtLayout.WarningColour : AreaSaveListArtLayout.RowColour);
    }

    public override void _Draw()
    {
        // ARC-UI-2575: the art is shared among the screens of the pre-made family, so the view on screen claims its frames at every draw (only while the Steamwoven layout places it).
        if (_zoned) Claim.Claim(_art.Values.SelectMany(frames => frames));
        if (_font is null || _face is null) return;
        DrawRect(new Rect2(0, 0, AreaSaveListArtLayout.ScreenWidth, AreaSaveListArtLayout.ScreenHeight), Colors.Black);
        // ARC-UI-3038: over a complete kit the strips and the body are the kit's and the screen's own arts 336, 335 and 745 are not drawn.
        var kit = _kit.Compose(_zoned, GetViewportRect().Size);
        var own = SteamwovenKitComposer.OwnBackdropArts(SteamwovenKitScreen.SaveLoad, kit);
        var ownDrawn = new List<int>(3);
        _kit.Draw(this, Position, Scale);
        if (own.Contains(AreaSaveListArtLayout.TopStripArt) && Art(AreaSaveListArtLayout.TopStripArt) is { } top) { DrawTextureRect(top, new Rect2(0, 0, top.GetWidth(), top.GetHeight()), false); ownDrawn.Add(AreaSaveListArtLayout.TopStripArt); }
        if (own.Contains(AreaSaveListArtLayout.BottomStripArt) && Art(AreaSaveListArtLayout.BottomStripArt) is { } bottom)
        {
            foreach (var (x, y, width, height) in AreaSaveListArtLayout.BottomStripParts)
                DrawTextureRectRegion(bottom, new Rect2(x, y, width, height), new Rect2(x, 0, width, height));
            ownDrawn.Add(AreaSaveListArtLayout.BottomStripArt);
        }
        var (px, py, pw, ph) = AreaSaveListArtLayout.Preview;
        // ARC-UI-3038: the preview's field, which the original's background leaves as a hole over its black, and the bottom strip's message box stand as plain black fields over the kit's background.
        if (kit) AreaKitStandIns.SaveLoad(this);
        // MW23: the picture at its own size (the original's fills the 468 × 300 rectangle from its corner); none, the new-save row or
        // an unread info leave the preview black. PROJECT: the stored picture is the screen fitted into 468 × 300 keeping its shape, so
        // a narrower or lower one is centred; kept within the preview.
        if (_info is not null && !_unread && _preview is not null && _preview.GetWidth() > 0 && _preview.GetHeight() > 0)
        {
            var (w, h) = (Math.Min(pw, _preview.GetWidth()), Math.Min(ph, _preview.GetHeight()));
            DrawTextureRectRegion(_preview, new Rect2(px + (pw - w) / 2, py + (ph - h) / 2, w, h), new Rect2(0, 0, w, h));
        }
        // The background's preview hole is transparent: the preview shows through it.
        if (own.Contains(AreaSaveListArtLayout.BackgroundArt) && Art(AreaSaveListArtLayout.BackgroundArt) is { } background)
        {
            DrawTextureRect(background, new Rect2(0, AreaSaveListArtLayout.WindowTop, background.GetWidth(), background.GetHeight()), false);
            ownDrawn.Add(AreaSaveListArtLayout.BackgroundArt);
        }
        _ownBackdropDrawn = ownDrawn;
        // MW13: the list's rectangle, five rows taller, is filled black before the rows.
        var (lx, ly, lw, lh) = AreaSaveListArtLayout.List;
        DrawRect(new Rect2(lx, ly, lw, lh + AreaSaveListArtLayout.ListFillExtra), Colors.Black);
        for (var slot = 0; slot < AreaSaveListArtLayout.ShownRows && _top + slot < _rows.Count; slot++)
        {
            var row = _top + slot;
            var (x, y, width, _) = AreaSaveListArtLayout.Row(slot);
            // MW14 fonts carry no flags: the cover blend (MW31).
            AreaArtTextDraw.Text(this, _face, AreaSaveListArtLayout.Fit(_font, _rows[row], width), x, y,
                row == _selected ? AreaSaveListArtLayout.SelectedColour : AreaSaveListArtLayout.RowColour);
        }
        // MW24: the hero's name, then the portrait at its own size (PO3, size 0), then the other texts.
        var texts = InfoTexts();
        for (var i = 0; i < texts.Count; i++)
        {
            DrawText(texts[i]);
            if (i == 0 && !_unread && _portrait is not null && _info is { Portrait: > 0 })
                DrawTextureRect(_portrait, new Rect2(AreaSaveListArtLayout.Portrait.X, AreaSaveListArtLayout.Portrait.Y, _portrait.GetWidth(), _portrait.GetHeight()), false);
        }
        // MW23: the cover over the preview, whatever it shows.
        if (Art(AreaSaveListArtLayout.CoverArt) is { } cover) DrawTextureRect(cover, new Rect2(px, py, cover.GetWidth(), cover.GetHeight()), false);
        // MW25, MW34: with no game behind the window (the main menu) the lens shows black; from play the original copies the view around the PC into it
        // (not drawn here yet: the background shows).
        if (LensBlack)
        {
            var (lensX, lensY, lensWidth, lensHeight) = AreaSaveListArtLayout.Lens;
            DrawCircle(new Vector2(lensX + lensWidth / 2f, lensY + lensHeight / 2f), lensWidth / 2f - 1, Colors.Black);
        }
        if (Art(AreaSaveListArtLayout.LensArt) is { } lens)
            DrawTextureRect(lens, new Rect2(AreaSaveListArtLayout.Lens.X, AreaSaveListArtLayout.Lens.Y, lens.GetWidth(), lens.GetHeight()), false);
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaSaveListArtLayout.Button(button);
            if (Art(art, Frame(button)) is { } texture) DrawTextureRect(texture, new Rect2(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
        if (Art(AreaSaveListArtLayout.ToggleArt, ToggleFrame()) is { } toggle)
            DrawTextureRect(toggle, new Rect2(AreaSaveListArtLayout.Toggle.X, AreaSaveListArtLayout.Toggle.Y, toggle.GetWidth(), toggle.GetHeight()), false);
    }

    private AreaSaveListArtButton? ButtonAt(Vector2 at)
    {
        foreach (var button in AllButtons)
        {
            var (art, x, y) = AreaSaveListArtLayout.Button(button);
            if (Art(art) is { } texture && AreaSaveListArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return button;
        }
        return null;
    }

    /// <summary>ARC-UI-3503: the window was opened with no game behind it: the lens is black.</summary>
    public bool LensBlack
    {
        get => _lensBlack;
        set { if (_lensBlack == value) return; _lensBlack = value; QueueRedraw(); }
    }

    private bool _lensBlack;

    /// <summary>ARC-UI-3503 (MW33): the pointer came over a button or the info toggle (its hint's <c>MainMenu.mes</c> number) or left it (null).</summary>
    public event Action<int?>? HintChanged;

    private int? HintAt(AreaSaveListArtButton? button, bool toggle) => button is { } over ? AreaSaveListArtLayout.Hint(over, _mode) : toggle ? AreaSaveListArtLayout.ToggleHint : null;

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || (_hovered is null && !_overToggle)) return;
        (_hovered, _overToggle) = (null, false);
        QueueRedraw();
        HintChanged?.Invoke(null);
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = ButtonAt(motion.Position);
                var overToggle = ToggleAt(motion.Position);
                if (over == _hovered && overToggle == _overToggle) break;
                (_hovered, _overToggle) = (over, overToggle);
                QueueRedraw();
                HintChanged?.Invoke(HintAt(over, overToggle));
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel
                when AreaSaveListArtLayout.Contains(AreaSaveListArtLayout.WheelArea, wheel.Position.X, wheel.Position.Y):
                var top = Math.Clamp(_top + (wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 0, AreaSaveListArtLayout.MaxScrollTop(_rows.Count));
                if (top != _top) { _top = top; QueueRedraw(); }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = ButtonAt(button.Position);
                if (_blocked) { AcceptEvent(); break; }
                // The press lights what is under it even when the pointer did not move since the opening (GB3: down over it).
                _hovered = at;
                // MW25: a press on the toggle turns it on (the info hidden, 3000) or off (shown again, 3001); its release does nothing.
                if (button.Pressed && at is null && _pressed is null && ToggleAt(button.Position))
                {
                    _toggleDown = !_toggleDown;
                    _infoShown = !_toggleDown;
                    SoundRequested?.Invoke(_toggleDown ? AreaGameBarLayout.PressSound : AreaGameBarLayout.ReleaseSound);
                }
                else if (button.Pressed)
                {
                    _pressed = at;
                    if (at is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                    else if (AreaSaveListArtLayout.RowAt(button.Position.X, button.Position.Y, _top, _rows.Count, _mode) is { } row && row >= 0) RowPressed?.Invoke(row);
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
