using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The three fonts of the sheet's original look.</summary>
public sealed record AreaCharacterSheetFonts(AreaArtFontFace Label, AreaArtFontFace Flare, AreaArtFontFace Cloister, AreaArtFontFace? Icons = null,
    AreaArtFontFace? Nick = null, AreaArtFontFace? Garmond = null, AreaArtFontFace? Pork = null)
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
    public static (int X, int Y, int Width, int Height) CloseRectangle => (AreaLensPlace.Corner((12, 10)).X, AreaLensPlace.Corner((12, 10)).Y, AreaLensPlace.Side, AreaLensPlace.Side); // ARC-UI-2714: the common place of the kit background

    private static readonly Hit[] Hits =
    [
        .. Enumerable.Range(0, AreaStatsPage.Rows).Select(row => new Hit(Kind.Plus, row)),
        .. Enumerable.Range(0, AreaStatsPage.Rows).Select(row => new Hit(Kind.Minus, row)),
        .. Enum.GetValues<AreaSheetTab>().Select(tab => new Hit(Kind.Tab, (int)tab)),
    ];

    /// <summary>ARC-UI-088: the NPC sheet of a dialog (the original's mode 2) creates no + or − button.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>ARC-UI-147: the character editor of the new-character flow (window 14, creation modes 0 and 3): the window stands at the menu screen's
    /// (0, 41) at the whole factor, as the opening shop's barter window does (ARC-UI-069), and the portrait's rectangle does not close it (the
    /// close rectangle is the interface modes 1 and 2's, struct 0x5C8930).</summary>
    public bool MenuScreen { get; set; }

    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private readonly AreaKitFaceLayer _kitFaces = new();

    /// <summary>ARC-UI-3341: the kit faces the sheet's own buttons show now (the native smoke).</summary>
    internal IReadOnlyList<AreaKitFaceLayer.Item> KitFacesForSelfTest => _kitFaces.ShownForSelfTest;
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
    /// <summary>ARC-UI-077: the spells page in the original's look (the right-hand panel, a sibling of <see cref="Pages"/>).</summary>
    public AreaCharacterSheetSpellsView Spells { get; } = new();
    /// <summary>ARC-UI-1742: the auto-level scheme page in the original's look (a sibling of <see cref="Pages"/>).</summary>
    public AreaCharacterSheetSchemesView Schemes { get; } = new();

    public bool ArtSet => _fonts is not null;
    public AreaSheetTab Tab => _tab;
    internal AreaCharacterSheetView? ViewForSelfTest => Visible ? _view : null;
    internal int? RegionForSelfTest => _region;
    internal bool PortraitForSelfTest => Visible && _portrait is not null;
    /// <summary>ARC-UI-2865: the portrait texture the sheet draws (the HD smoke reads whether it is the pack's redraw).</summary>
    internal Texture2D? PortraitTextureForSelfTest => _portrait;
    /// <summary>ARC-UI-1791: whether the icon art of the view is held (the slot then shows it).</summary>
    internal bool IconForSelfTest => Visible && _portrait is null && _view?.IconArt is { } icon && Art(icon) is not null;
    internal IReadOnlyList<AreaSheetText> TextsForSelfTest => _view is { } view && _fonts is { } fonts
        ? AreaCharacterSheetArtLayout.Texts(view, _texts, (font, text) => fonts.For(font).Font.Measure(text).Width) : [];
    /// <summary>The window position of a window-relative point of the sheet.</summary>
    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);
    /// <summary>ARC-UI-2924: the button the pointer lights now (its kind and index), or null.</summary>
    internal (string Kind, int Index)? HoveredForSelfTest => _hovered is { } hit ? (hit.Kind.ToString(), hit.Index) : null;

    public override void _Ready()
    {
        Name = "CharacterSheetArt";
        Size = new Vector2(AreaCharacterSheetArtLayout.Width, AreaCharacterSheetArtLayout.Height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
        AddChild(_kitFaces); // under the pages, as the tabs drawn by this view are
        AddChild(Pages);
        AddChild(Spells);
        AddChild(Schemes);
    }

    /// <param name="alignmentHot">The hot point of each frame of art 254.</param>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaCharacterSheetFonts fonts, AreaCharacterSheetTexts texts, (int X, int Y)[] alignmentHot)
    {
        // ARC-UI-1195: the textures of a look this view no longer shows are let go.
        if (_onTextureChanged is { } handler) { foreach (var texture in _watched) texture.Changed -= handler; _watched.Clear(); }
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

    // ARC-UI-2694: the lens shows the round view of the game under the window, so the window art leaves its square out (the original's copy replaces it, 0x550DA0 mode 1).
    private bool _lensCut;

    /// <summary>The lens's rectangle on the screen (the window's rectangle scaled and moved): the round view's square.</summary>
    internal (Vector2 Origin, float Side) LensOnScreen
    {
        get
        {
            var (x, y, side) = AreaCharacterSheetArtLayout.Lens;
            return (GetGlobalTransform() * new Vector2(x, y), side * GetGlobalTransform().Scale.X);
        }
    }

    /// <summary>The round view is shown under the window: the window art leaves the lens's square out.</summary>
    internal bool LensCut
    {
        get => _lensCut;
        set { if (_lensCut == value) return; _lensCut = value; QueueRedraw(); }
    }

    public void CloseView()
    {
        Visible = false; _lensCut = false;
        (_hovered, _pressed, _region, _view, _portrait) = (null, null, null, null, null);
    }

    /// <summary>ARC-UI-2071 (ledger DH10, 0x55A5C0): the lone left release after the reply that opened the sheet (screen point): a release inside the close rectangle closes it
    /// (0x55AD8B), the buttons act only on a release they saw pressed.</summary>
    internal void LoneRelease(Vector2 screen) => _GuiInput(new InputEventMouseButton
    {
        ButtonIndex = MouseButton.Left, Pressed = false, Position = GetGlobalTransformWithCanvas().AffineInverse() * screen, GlobalPosition = screen,
    });

    /// <summary>The window between the strips (ARC-UI-132): the whole free height at the HD art's exact scale, centred; the original's bitmaps
    /// at the largest whole scale standing on the bottom strip (ARC-UI-127; the original's 800 × 600 gives 1 and the sheet under the top strip).</summary>
    public void Place()
    {
        if (MenuScreen)
        {
            // ARC-UI-147: the menu's 800 × 600 screen at the bars' whole factor, centred; the window under the top strip (as AreaShopArtLayout.Window).
            var screen = GetViewportRect().Size;
            // ARC-UI-2575: with the Steamwoven layout the screen's body lies in the window zone at the layout's unit scale; ARC-UI-2924: the editor is a two-strip screen like the shop it shares its strips with, so
            // the whole 800 × 600 screen is the fitted plate (SteamwovenColumnMenu.Fit), black beside it.
            if (AreaGameBarPanel.TwoStripScreen(screen, AreaShopArtLayout.Window.Y) is { } zone)
            {
                Scale = new Vector2((float)zone.Scale, (float)zone.Scale);
                Position = new Vector2((float)zone.X, (float)Math.Round(zone.Y + AreaShopArtLayout.Window.Y * zone.Scale));
                RedrawOnScaleChange();
                return;
            }
            var factor = AreaGameBarPanel.ScreenFactor(screen);
            Scale = new Vector2(factor, factor);
            Position = new Vector2(AreaGameBarPanel.ScreenLeft(screen, factor),
                MathF.Round((screen.Y - AreaPremadeBrowserArtLayout.ScreenHeight * factor) / 2) + AreaShopArtLayout.Window.Y * factor);
            RedrawOnScaleChange();
            return;
        }
        var (scale, position) = AreaGameBarPanel.FittedWindow(GetViewportRect().Size, AreaCharacterSheetArtLayout.Width, AreaCharacterSheetArtLayout.Height,
            InterfaceHd.Enabled && InterfaceHd.Pack is not null);
        Scale = new Vector2(scale, scale);
        Position = position;
        RedrawOnScaleChange();
    }

    // ARC-UI-1195: a change of the window's scale moves no pixel by itself (the canvas item is only stretched), so the frames are drawn again at the new device scale.
    private float _drawnScale;

    private void RedrawOnScaleChange()
    {
        var device = DeviceScale();
        if (Math.Abs(device - _drawnScale) < 0.0005f) return;
        _drawnScale = device;
        QueueRedraw(); Pages.QueueRedraw(); Spells.QueueRedraw(); Schemes.QueueRedraw();
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
            if (ReadOnly && hit.Kind != Kind.Tab) continue;
            var (art, x, y) = Place(hit);
            if (Art(art) is { } texture && AreaPremadeBrowserArtLayout.Contains((x, y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y)) return hit;
        }
        return null;
    }

    // ARC-UI-1195: the pack's frames of the sheet are area-resampled at the window's own device scale (InterfaceResample) (the frame scale times the canvas transform's and the viewport's
    // stretch, the bars' own rule). The sheet's textures are shared (the in-game sheet, the NPC sheet, the creation editor), so the view on screen claims them at its draw.
    // The pages (the children, ARC-UI-1784: the schemes page too) draw the same textures at the same scale; a texture swapped in by a worker redraws all of them, since each draws in its own _Draw.
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;
    private Func<float>? _deviceScale;

    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : 1f;

    private void ClaimScale()
    {
        _deviceScale ??= DeviceScale;
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) { QueueRedraw(); Pages.QueueRedraw(); Spells.QueueRedraw(); Schemes.QueueRedraw(); } };
        foreach (var frames in _art.Values)
            foreach (var texture in frames)
            {
                if (texture is not HdInterfaceTexture hd) continue;
                hd.ScaleSource = _deviceScale;
                if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
            }
    }

    public override void _EnterTree()
    {
        AreaGameBarPanel.KitBackgroundChanged += QueueRedraw;
        AreaKitSheets.Changed += QueueRedraw;
    }

    public override void _ExitTree()
    {
        AreaGameBarPanel.KitBackgroundChanged -= QueueRedraw;
        AreaKitSheets.Changed -= QueueRedraw;
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    /// <summary>ARC-UI-1195: the textures of the given art numbers that the sheet holds, every frame, with the art number of each (the native smoke).</summary>
    internal IReadOnlyList<(int Art, HdInterfaceTexture Texture)> TexturesForSelfTest(IEnumerable<int> arts) =>
        [.. arts.Where(_art.ContainsKey).SelectMany(number => _art[number].OfType<HdInterfaceTexture>().Select(texture => (number, texture)))];

    /// <summary>ARC-UI-3037: the kit's character background shows under this window. ARC-UI-3041: the creation editor's sheet (<see cref="MenuScreen"/>) follows the editor screen's own kit body (<see cref="KitBody"/>), not the bars' window (the menu has none).</summary>
    private bool KitBackgroundShown => MenuScreen ? _kitBody : AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Character;

    /// <summary>ARC-UI-3041 (census M09): the creation editor's kit body (the character window's kit background, drawn by the screen's strips view under this window) shows now: the sheet draws no art 22 of its own and its stand-ins and stat names instead.
    /// Meaningful only on the editor's sheet.</summary>
    internal bool KitBackgroundShownForSelfTest => KitBackgroundShown;
    /// <summary>ARC-UI-3041: the last draw painted the original's art 22 (false over the kit's background).</summary>
    internal bool OwnBackgroundDrawnForSelfTest { get; private set; }
    /// <summary>ARC-UI-3165: the words and numbers the last draw wrote itself over the kit's background in place of the ones art 22 had baked in (<see cref="AreaKitStandIns.SheetLabels"/>: the eight stat names, the dial's E and G, the ruler's seven divisions unless the kit's ruler part carries them, ARC-UI-3321); empty when the art's own background was drawn.</summary>
    internal IReadOnlyList<string> KitLabelsDrawnForSelfTest { get; private set; } = [];
    /// <summary>ARC-UI-3299: the label parts of the kit (<see cref="AreaKitStandIns.SheetLabelParts"/>) the last draw laid over the kit's background; their text is not written.</summary>
    internal IReadOnlyList<string> KitLabelPartsDrawnForSelfTest { get; private set; } = [];

    public bool KitBody
    {
        get => _kitBody;
        set { if (_kitBody == value) return; _kitBody = value; QueueRedraw(); }
    }
    private bool _kitBody;

    public override void _Draw()
    {
        if (_fonts is not { } fonts || _view is not { } view) return;
        ClaimScale();
        // ARC-UI-3037: over the kit's character background the main page draws no art 22 of its own (its lens art, portrait, markers, tabs, buttons and pages stay).
        OwnBackgroundDrawnForSelfTest = false;
        var kitLabels = new List<string>();
        KitLabelPartsDrawnForSelfTest = [];
        if (!KitBackgroundShown && Art(AreaCharacterSheetArtLayout.BackgroundArt) is { } window)
        {
            OwnBackgroundDrawnForSelfTest = true;
            float width = window.GetWidth(), height = window.GetHeight();
            if (!_lensCut) DrawTextureRect(window, new Rect2(0, 0, width, height), false);
            else
            {
                // The lens's square is left out, so the round view under the window shows through (the four pieces around it).
                var (lx, ly, edge) = AreaCharacterSheetArtLayout.Lens;
                foreach (var (px, py, pw, ph) in new[] { (0f, 0f, width, (float)ly), (0f, ly + (float)edge, width, height - ly - edge), (0f, (float)ly, (float)lx, (float)edge),
                    (lx + (float)edge, (float)ly, width - lx - edge, (float)edge) })
                    if (pw > 0 && ph > 0) DrawTextureRectRegion(window, new Rect2(px, py, pw, ph), new Rect2(px, py, pw, ph));
            }
        }
        if (KitBackgroundShown)
        {
            var fixturesDrawn = AreaKitStandIns.CharacterSheet(this);
            // The stat names and the dial's E&G are baked into art 22 (the exe draws only the numbers): with the art skipped the sheet draws the kit's label parts (the old HD version's lettering, ARC-UI-3299) and, only where the kit has none, writes the text itself in its own font.
            var labelFace = fonts.For(AreaSheetFont.Cloister);
            var partsDrawn = AreaKitStandIns.SheetLabelParts(this);
            KitLabelPartsDrawnForSelfTest = [.. partsDrawn.Order()];
            // ARC-UI-3321: the ruler part carries its divisions (cut from the old HD frame), so their text is written only where the part is missing.
            var carried = new HashSet<string>(partsDrawn);
            if (fixturesDrawn.Contains(AreaKitStandIns.RulerPart)) carried.Add(AreaKitStandIns.RulerPart);
            foreach (var (label, x, y, rgb) in AreaKitStandIns.SheetLabels(carried)) { AreaArtTextDraw.Text(this, labelFace, label, x, y, rgb); kitLabels.Add(label); }
        }
        KitLabelsDrawnForSelfTest = kitLabels;
        // ARC-UI-2694: the lens's cover art over the round view (0x51DA80 after the mode's copy, as in the logbook), when it reads.
        if (!MenuScreen && !AreaLensPlace.SurfaceRing && Art(AreaCharacterSheetArtLayout.LensArt) is { } lens) // ARC-UI-3101: the kit surface carries the ring
            DrawTextureRect(lens, new Rect2(AreaCharacterSheetArtLayout.Lens.X, AreaCharacterSheetArtLayout.Lens.Y, lens.GetWidth(), lens.GetHeight()), false);
        // The portrait in its slot (141, 21), at its own size (0x4CE470).
        if (_portrait is not null) DrawTextureRect(_portrait, new Rect2(141, 21, _portrait.GetWidth(), _portrait.GetHeight()), false);
        // ARC-UI-1791 (ledger CHW25): an NPC without a portrait to show has its icon blitted at the same place at its own size (0x559ABC); the hero never has one.
        else if (view.IconArt is { } icon && Art(icon) is { } image) DrawTextureRect(image, new Rect2(141, 21, image.GetWidth(), image.GetHeight()), false);
        // 0x55EC90: the alignment gauge by its hot point, the aptitude marker by its top-left.
        var frames = _art.TryGetValue(AreaCharacterSheetArtLayout.AlignmentArt, out var gauge) ? gauge.Length : 0;
        var frame = AreaCharacterSheetArtLayout.AlignmentFrame(view.Alignment, frames);
        if (Art(AreaCharacterSheetArtLayout.AlignmentArt, frame) is { } needle)
        {
            var (hotX, hotY) = frame < _alignmentHot.Length ? _alignmentHot[frame] : (0, 0);
            var (x, y) = AreaCharacterSheetArtLayout.AlignmentTopLeft(hotX, hotY);
            // ARC-UI-3321: over the kit the needle is the kit's clean HD frame (the art's own frame carries its shadow as grey pixels painted for the old dial, #3312); its hot point is the original's, and its soft shadow reaches into the cell beyond the frame.
            var (cellX, cellY) = AreaCharacterSheetArtLayout.NeedleCell(frame);
            const int cell = AreaCharacterSheetArtLayout.NeedleCellUnits;
            if (!KitBackgroundShown || !AreaKitFixtures.DrawRegion(this, 1, AreaKitStandIns.NeedlePart, x, y, cellX, cellY, cell, cell))
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
        var faces = new List<AreaKitFaceLayer.Item>();
        foreach (var hit in Hits)
        {
            if (ReadOnly && hit.Kind != Kind.Tab) continue;
            var (number, x, y) = Place(hit);
            if (hit.Kind == Kind.Tab && Art(number) is null) continue;
            var shown = hit.Kind == Kind.Tab && hit.Index == (int)_tab ? AreaBarButtonFrameDown(number) : Frame(hit);
            if (hit.Kind != Kind.Tab && KitBackgroundShown && KitButton(hit, x, y, shown)) continue;
            if (Art(number, shown) is not { } texture) continue;
            var place = new Rect2(x, y, texture.GetWidth(), texture.GetHeight());
            // ARC-UI-3341: over the kit's background a tab the kit holds a face for (the schematics tab) draws that face in the art's place.
            if (KitBackgroundShown && AreaKitSheets.Face(number, shown) is { } face) faces.Add(new(number, face, place));
            else DrawTextureRect(texture, place, false);
        }
        _kitFaces.Show(faces);
    }

    /// <summary>
    /// ARC-UI-3321: a + or − button from the kit's layers over its socket (face, symbol and the overlay of the look in the art's frame order: 1 pressed, 2 hover); false when the kit has none (the original art is drawn).
    /// </summary>
    private bool KitButton(Hit hit, int x, int y, int frame)
    {
        var layers = SteamwovenKitTable.SheetButtonLayers(SteamwovenKitTable.SheetButtonName(hit.Index, hit.Kind == Kind.Plus));
        if (!AreaKitFixtures.Has(layers.Plate) || !AreaKitFixtures.Has(layers.Glyph)) return false;
        AreaKitFixtures.Draw(this, 1, layers.Plate, x, y);
        AreaKitFixtures.Draw(this, 1, layers.Glyph, x, y);
        if (SteamwovenKitTable.SheetButtonOverlay(SteamwovenKitTable.SheetButtonName(hit.Index, hit.Kind == Kind.Plus), frame) is { } over) AreaKitFixtures.Draw(this, 1, over, x, y);
        return true;
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
                    else if (!MenuScreen && held is null && AreaPremadeBrowserArtLayout.Contains(CloseRectangle, button.Position.X, button.Position.Y)) CloseRequested?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }
}
