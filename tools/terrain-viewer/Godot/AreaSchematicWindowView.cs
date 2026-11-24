using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The text bar's lines the window shows for a part the view does not carry: the list tabs (<c>schematic_text.mes</c> 4 and 5) and a discipline's
/// name and description (type 10, <c>tech.mes</c>).</summary>
public sealed record AreaSchematicWindowTexts(string LearnedTab, string FoundTab, IReadOnlyList<string> Disciplines)
{
    public string ListTab(bool found) => found ? FoundTab : LearnedTab;
    public string Discipline(int discipline) => discipline >= 0 && discipline < Disciplines.Count ? Disciplines[discipline] : string.Empty;
}

/// <summary>The four fonts of the schematic window's original look (art 371 the record's name, 229 the description and the required number, 370 the
/// discipline glyph, 300 a slot's discipline glyph).</summary>
public sealed record AreaSchematicWindowFonts(AreaArtFontFace Name, AreaArtFontFace Description, AreaArtFontFace Glyph, AreaArtFontFace SlotGlyph);

/// <summary>
/// ARC-UI-142: the schematic window in the original's look (<see cref="AreaSchematicWindowLayout"/>, ledger SW9–SW14): the window art 365 with the page
/// buttons (hidden on the first and the last page), the eight discipline tabs (one radio group, the shown one pressed), the learned and found tabs, the
/// record's art, name, discipline glyph and wrapped description, the two component slots (found or missing overlay, the item's picture fitted and shaded
/// when the expertise lacks, the discipline glyph and the required number), the assemble button with its face art and the round view's lens over a copy of
/// the game view. A control of 800 × 400 placed between the interface bars. Pointer: a press and a release on the same button act (sounds 3000 and
/// 3001), a release in the lens closes the window, and the pointer over a part asks the text bar for its text. PROJECT: a button takes the pointer on
/// its art's rectangle, not its opaque pixels; the controller has no focus row here.
/// </summary>
public partial class AreaSchematicWindowView : Control
{
    private enum Kind { Previous, Next, Assemble, ListTab, Discipline, Slot }
    private readonly record struct Hit(Kind Kind, int A);

    /// <summary>Black: the window's fonts are built with the colour tables' first entry (0x56CD60).</summary>
    public static (byte R, byte G, byte B) Black { get; } = (0, 0, 0);

    private IReadOnlyDictionary<int, Texture2D[]> _art = new Dictionary<int, Texture2D[]>();
    private AreaSchematicWindowFonts? _fonts;
    private AreaSchematicsView? _view;
    private AreaSchematicWindowTexts? _texts;
    private Hit? _hovered, _pressed;
    private bool _lensCut;

    public event Action<AreaSchematicTab>? TabRequested;
    public event Action<int>? DisciplineRequested;
    public event Action<int>? PageRequested;
    public event Action? AssembleRequested;
    public event Action? CloseRequested;
    /// <summary>The pointer is over a part (its text bar entry), or left (null).</summary>
    public event Action<AreaMessageEntry?>? HoverEntryRequested;
    /// <summary>An interface sound of the art look (3000 press, 3001 release).</summary>
    public event Action<int>? SoundRequested;

    /// <summary>The picture of a component slot's item (its inventory art), or null; the app supplies it.</summary>
    public Func<AreaSchematicSlot, Texture2D?>? SlotPicture { get; set; }
    /// <summary>The text bar entry of a slot's item (the pointer over a slot), or null.</summary>
    public Func<AreaSchematicSlot, AreaMessageEntry?>? SlotEntry { get; set; }

    public bool ArtSet => _fonts is not null && _art.Count > 0;
    public bool IsShown => Visible && _view is not null;

    /// <summary>The lens's rectangle on the screen (the window's rectangle scaled and moved): the round view's square.</summary>
    internal (Vector2 Origin, float Side) LensOnScreen
    {
        get
        {
            var (x, y, side, _) = AreaSchematicWindowLayout.RoundView;
            return (GlobalPosition + new Vector2(x, y) * Scale.X, side * Scale.X);
        }
    }

    /// <summary>The round view is shown under the window: the window art leaves the lens's square out.</summary>
    internal bool LensCut
    {
        get => _lensCut;
        set { if (_lensCut == value) return; _lensCut = value; QueueRedraw(); }
    }

    internal Vector2 PointForSelfTest(double x, double y) => GetGlobalTransform() * new Vector2((float)x, (float)y);
    internal int? HoveredForSelfTest => _hovered is { } hit ? (int)hit.Kind * 100 + hit.A : null;

    public override void _EnterTree() => AreaGameBarPanel.KitBackgroundChanged += QueueRedraw;

    public override void _ExitTree() => AreaGameBarPanel.KitBackgroundChanged -= QueueRedraw;

    private static bool KitBackgroundShown => AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Schematic;

    public override void _Ready()
    {
        Name = "SchematicWindowArt";
        Size = new Vector2(AreaSchematicWindowLayout.Width, AreaSchematicWindowLayout.Height);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        AreaArtTextDraw.Prepare(this);
    }

    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaSchematicWindowFonts fonts)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        Unwatch();
        // ARC-UI-1199: the generated interface paints the window's frames at this window's own device scale (the control's scale, the canvas transform's and the viewport's stretch),
        // and the view redraws when a worker's finished frame replaces the one it drew (it draws the textures in its own _Draw).
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) QueueRedraw(); };
        foreach (var texture in art.Values.SelectMany(frames => frames))
        {
            if (texture is not HdInterfaceTexture hd) continue;
            hd.ScaleSource = DeviceScale;
            if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
        }
    }

    // ARC-UI-1199: the frames this view drew with its own device scale, and the scale the last draw asked for.
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;
    private float _drawnScale;

    /// <summary>ARC-UI-1199: device pixels per Legacy pixel of this window (what the generated frames are painted at).</summary>
    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : Scale.X;

    private void Unwatch()
    {
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    /// <summary>Texts the window needs that the view does not carry: the hover texts of the list tabs and the assemble button (schematic_text.mes 3..5)
    /// and of a discipline tab (tech.mes name and description).</summary>
    public void SetTexts(AreaSchematicWindowTexts texts) => _texts = texts;

    public void Render(AreaSchematicsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_fonts is null) throw new InvalidOperationException("ARC-UI-142: the schematic window art is not set");
        if (!Visible) (_hovered, _pressed) = (null, null);
        _view = view;
        Visible = true;
        Place();
        QueueRedraw();
        if (_hovered is null) return;
        // A press can hide a page button or change a slot: the part under the pointer is found again.
        _hovered = HitAt(GetLocalMousePosition());
        HoverEntryRequested?.Invoke(_hovered is { } hit ? EntryOf(hit) : null);
    }

    public void CloseView()
    {
        if (_hovered is not null) HoverEntryRequested?.Invoke(null);
        (_hovered, _pressed, _view) = (null, null, null);
        Visible = false;
    }

    /// <summary>The window between the strips: the whole free height at the HD art's exact scale, centred; the original's bitmaps at the largest whole
    /// scale standing on the bottom strip (ARC-UI-127, ARC-UI-132).</summary>
    public void Place()
    {
        var (scale, position) = AreaGameBarPanel.FittedWindow(GetViewportRect().Size, AreaSchematicWindowLayout.Width, AreaSchematicWindowLayout.Height,
            InterfaceHd.Enabled && InterfaceHd.Pack is not null);
        Scale = new Vector2(scale, scale);
        Position = position;
        // ARC-UI-1199: a new device scale (the window or the stretch changed) asks the textures for frames at it.
        if (IsInsideTree() && _view is not null && MathF.Abs(DeviceScale() - _drawnScale) > 0.001f) QueueRedraw();
    }

    private Texture2D? Art(int number, int frame = 0) => AreaCharacterArtDraw.Art(_art, number, frame);

    private int Frames(int art) => _art.TryGetValue(art, out var frames) ? frames.Length : 1;

    private int Frame(int art, Hit hit, bool selected = false) => selected ? AreaGameBarLayout.Frame(AreaBarButtonLook.Down, Math.Max(1, Frames(art)))
        : AreaCharacterArtDraw.Frame(Frames(art), _pressed is not null, _pressed == hit, _hovered == hit);

    private bool PreviousShown => _view is { Page: > 0 };
    private bool NextShown => _view is { } view && view.Page < view.Pages - 1;

    private (int Art, (int X, int Y) At) Place(Hit hit) => hit.Kind switch
    {
        Kind.Previous => (AreaSchematicWindowLayout.PreviousArt, AreaSchematicWindowLayout.Previous),
        Kind.Next => (AreaSchematicWindowLayout.NextArt, AreaSchematicWindowLayout.Next),
        Kind.Assemble => (AreaSchematicWindowLayout.AssembleArt, AreaSchematicWindowLayout.Assemble),
        Kind.ListTab => (AreaSchematicWindowLayout.ListTabArt, AreaSchematicWindowLayout.ListTab(hit.A == 1)),
        _ => (AreaSchematicWindowLayout.DisciplineArt(hit.A), AreaSchematicWindowLayout.DisciplineTab(hit.A)),
    };

    /// <summary>The buttons in the order the build makes them (previous, next, assemble, the list tabs, the eight discipline tabs), then the two slot
    /// regions; a hidden page button takes no pointer.</summary>
    private Hit? HitAt(Vector2 at)
    {
        bool In(Hit hit)
        {
            var (art, place) = Place(hit);
            return Art(art) is { } texture && AreaSchematicWindowLayout.Inside((place.X, place.Y, texture.GetWidth(), texture.GetHeight()), at.X, at.Y);
        }
        if (_view is null) return null;
        var candidates = new List<Hit>();
        if (PreviousShown) candidates.Add(new(Kind.Previous, 0));
        if (NextShown) candidates.Add(new(Kind.Next, 0));
        candidates.Add(new(Kind.Assemble, 0));
        candidates.Add(new(Kind.ListTab, 0));
        candidates.Add(new(Kind.ListTab, 1));
        for (var discipline = 0; discipline < AreaSchematicWindowLayout.Disciplines; discipline++) candidates.Add(new(Kind.Discipline, discipline));
        foreach (var hit in candidates) if (In(hit)) return hit;
        for (var slot = 0; slot < 2; slot++)
            if (SlotOf(slot) is not null && AreaSchematicWindowLayout.Inside(AreaSchematicWindowLayout.Slot(slot), at.X, at.Y)) return new(Kind.Slot, slot);
        return null;
    }

    private AreaSchematicSlot? SlotOf(int slot) => slot == 0 ? _view?.Component1 : _view?.Component2;

    private AreaMessageEntry? EntryOf(Hit hit)
    {
        static AreaMessageEntry? Text(string? text) => string.IsNullOrEmpty(text) ? null : AreaMessageEntry.Text(AreaMessageKind.Hover, text);
        switch (hit.Kind)
        {
            case Kind.Assemble: return Text(_view?.HoverText);
            case Kind.ListTab: return Text(_texts?.ListTab(found: hit.A == 1));
            case Kind.Discipline: return Text(_texts?.Discipline(hit.A));
            case Kind.Slot: return SlotOf(hit.A) is { } slot ? SlotEntry?.Invoke(slot) : null;
            default: return null;
        }
    }

    private void DrawArt(int art, (int X, int Y) place, int frame = 0)
    {
        if (Art(art, frame) is { } texture) DrawTextureRect(texture, new Rect2(place.X, place.Y, texture.GetWidth(), texture.GetHeight()), false);
    }

    private void DrawText(AreaArtFontFace face, string text, int x, int y)
    {
        if (text.Length > 0) AreaArtTextDraw.Text(this, face, text, x, y, Black);
    }

    public override void _Draw()
    {
        if (_fonts is not { } fonts || _view is not { } view) return;
        _drawnScale = DeviceScale();
        DrawWindow();
        for (var discipline = 0; discipline < AreaSchematicWindowLayout.Disciplines; discipline++)
            DrawArt(AreaSchematicWindowLayout.DisciplineArt(discipline), AreaSchematicWindowLayout.DisciplineTab(discipline),
                Frame(AreaSchematicWindowLayout.DisciplineArt(discipline), new(Kind.Discipline, discipline), selected: discipline == view.Discipline));
        foreach (var found in new[] { false, true })
        {
            // The list tabs are plain buttons: the exe presses no list tab, only the discipline group (0x56D530).
            DrawArt(AreaSchematicWindowLayout.ListTabArt, AreaSchematicWindowLayout.ListTab(found), Frame(AreaSchematicWindowLayout.ListTabArt, new(Kind.ListTab, found ? 1 : 0)));
        }
        if (PreviousShown) DrawArt(AreaSchematicWindowLayout.PreviousArt, AreaSchematicWindowLayout.Previous, Frame(AreaSchematicWindowLayout.PreviousArt, new(Kind.Previous, 0)));
        if (NextShown) DrawArt(AreaSchematicWindowLayout.NextArt, AreaSchematicWindowLayout.Next, Frame(AreaSchematicWindowLayout.NextArt, new(Kind.Next, 0)));

        // The record: its art at its natural size, the discipline glyph of its product, the name and the wrapped description.
        if (view.RecordArt > 0) DrawArt(view.RecordArt, AreaSchematicWindowLayout.RecordArtAt);
        if (view.Product is { Discipline: >= 0 } product)
            DrawText(fonts.Glyph, ((char)('0' + product.Discipline)).ToString(), AreaSchematicWindowLayout.GlyphAt.X, AreaSchematicWindowLayout.GlyphAt.Y);
        DrawText(fonts.Name, view.Name, AreaSchematicWindowLayout.NameAt.X, AreaSchematicWindowLayout.NameAt.Y);
        AreaCharacterArtDraw.Lines(this, AreaSchematicWindowLayout.Description, view.Description, fonts.Description, Black);

        for (var slot = 0; slot < 2; slot++) if (SlotOf(slot) is { } component) DrawSlot(fonts, slot, component);

        // The assemble button's art and its face by state (0x56DDC0: the face is the page draw's, the button the build's).
        DrawArt(AreaSchematicWindowLayout.AssembleArt, AreaSchematicWindowLayout.Assemble, Frame(AreaSchematicWindowLayout.AssembleArt, new(Kind.Assemble, 0)));
        DrawArt(AreaSchematicWindowLayout.FaceArt(view.Button), AreaSchematicWindowLayout.AssembleFaceAt);
        if (Art(AreaSchematicWindowLayout.RoundViewArt) is { } lens)
        {
            var (x, y, _, _) = AreaSchematicWindowLayout.RoundView;
            DrawTextureRect(lens, new Rect2(x, y, lens.GetWidth(), lens.GetHeight()), false);
        }
    }

    // The window art; with the round view under it the lens's square is left out.
    private void DrawWindow()
    {
        // ARC-UI-3037: over the kit's schematic background the base art 365 is not drawn (tabs, slots, arrows, lens art and text stay).
        if (KitBackgroundShown) { AreaKitStandIns.Schematic(this); return; }
        if (Art(AreaSchematicWindowLayout.BackgroundArt) is not { } window) return;
        float width = window.GetWidth(), height = window.GetHeight();
        if (!_lensCut) { DrawTextureRect(window, new Rect2(0, 0, width, height), false); return; }
        var (x, y, edge, _) = AreaSchematicWindowLayout.RoundView;
        foreach (var (px, py, pw, ph) in new[] { (0f, 0f, width, (float)y), (0f, y + (float)edge, width, height - y - edge), (0f, (float)y, (float)x, (float)edge),
            (x + (float)edge, (float)y, width - x - edge, (float)edge) })
            if (pw > 0 && ph > 0) DrawTextureRectRegion(window, new Rect2(px, py, pw, ph), new Rect2(px, py, pw, ph));
    }

    private void DrawSlot(AreaSchematicWindowFonts fonts, int slot, AreaSchematicSlot component)
    {
        var rect = AreaSchematicWindowLayout.Slot(slot);
        // The found or missing overlay at the slot's top-left, then the item's picture fitted into the slot, shaded when the expertise lacks.
        // (0x56E190: the overlay is inside the expertise test; a component the expertise does not reach shows the shaded picture only.)
        if (component.ExpertiseOk) DrawArt(component.Found ? AreaSchematicWindowLayout.FoundOverlayArt : AreaSchematicWindowLayout.MissingOverlayArt, (rect.X, rect.Y));
        if (SlotPicture?.Invoke(component) is { } picture)
        {
            var (x, y, width, height) = AreaSchematicWindowLayout.FitPicture(rect, picture.GetWidth(), picture.GetHeight());
            DrawTextureRect(picture, new Rect2(x, y, width, height), false, component.ExpertiseOk ? Colors.White : ShadeColour);
        }
        var needed = -component.Complexity;
        if (needed <= 0) return;
        var (gx, gy) = AreaSchematicWindowLayout.SlotGlyphAt(slot);
        DrawText(fonts.SlotGlyph, ((char)('1' + component.Discipline)).ToString(), gx, gy);
        var (nx, ny) = AreaSchematicWindowLayout.SlotNumberAt(slot);
        DrawText(fonts.Description, needed.ToString(System.Globalization.CultureInfo.InvariantCulture), nx, ny);
    }

    /// <summary>The shade of an item whose expertise lacks: the draw 0x56E190 blends the picture with the colour (255, 204, 102) (PROJECT: the blend's
    /// strength is the project's, a multiply).</summary>
    private static readonly Color ShadeColour = new(1f, 204 / 255f, 102 / 255f);

    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit || _hovered is null) return;
        _hovered = null;
        HoverEntryRequested?.Invoke(null);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                var over = HitAt(motion.Position);
                if (over is { } hit && EntryOf(hit) is { } entry) HoverEntryRequested?.Invoke(entry);
                else if (_hovered is not null) HoverEntryRequested?.Invoke(null);
                if (over == _hovered) break;
                _hovered = over;
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                var at = HitAt(button.Position);
                _hovered = at;
                if (button.Pressed)
                {
                    _pressed = at is { Kind: not Kind.Slot } ? at : null;
                    if (_pressed is not null) SoundRequested?.Invoke(AreaGameBarLayout.PressSound);
                }
                else
                {
                    var held = _pressed;
                    _pressed = null;
                    if (held is { } pressed && at == pressed)
                    {
                        SoundRequested?.Invoke(AreaGameBarLayout.ReleaseSound);
                        Act(pressed);
                    }
                    else if (held is null && AreaSchematicWindowLayout.Inside(AreaSchematicWindowLayout.RoundView, button.Position.X, button.Position.Y))
                        CloseRequested?.Invoke();
                }
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    /// <summary>ARC-UI-2071 (ledger DH10, SW12): the lone left release after the reply that opened the window (screen point). The window's mouse branch (0x56D530, type 0,
    /// subtype 1) reads no press state: a release inside the lens's square closes it; no button acts on a release it did not see pressed.</summary>
    internal void LoneRelease(Vector2 screen) => _GuiInput(new InputEventMouseButton
    {
        ButtonIndex = MouseButton.Left, Pressed = false, Position = GetGlobalTransformWithCanvas().AffineInverse() * screen, GlobalPosition = screen,
    });

    private void Act(Hit hit)
    {
        switch (hit.Kind)
        {
            case Kind.Previous: PageRequested?.Invoke(-1); break;
            case Kind.Next: PageRequested?.Invoke(1); break;
            case Kind.Assemble: AssembleRequested?.Invoke(); break;
            case Kind.ListTab: TabRequested?.Invoke(hit.A == 1 ? AreaSchematicTab.Found : AreaSchematicTab.Learned); break;
            case Kind.Discipline: DisciplineRequested?.Invoke(hit.A); break;
        }
    }
}
