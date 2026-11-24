using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-019: the inventory window's paper doll and the hero's 10 × 12 grid, drawn in the original window's frame
/// (<see cref="AreaInventoryDrag"/>) scaled to fit. A left press puts the item under the pointer on the cursor (it stays in its place, not
/// drawn there); the next left release asks the app to put it where it was released (<see cref="DropRequested"/>) and empties the cursor,
/// unless the app puts a swapped occupant on it (<see cref="Hold"/>). A press while an item is held only waits for its release; a right
/// press does nothing then. ARC-UI-025: a right click with nothing held asks the app to use the item under the pointer
/// (<see cref="UseRequested"/>, IW5; on the right release, ARC-UI-032: the original's mouse subtype 3); the drop and use icons right of the grid take a held item (IW6). Plain diagnostic shapes and text, no
/// original art. ARC-UI-035: once the pointer rests (the engine's still-pointer message, subtype 7) with nothing held, a new item under it
/// (or none) is reported (<see cref="PointerStill"/>, 0x573C74), once until another rests there. ARC-UI-051: with the window's art
/// (<see cref="Look"/>) the frame draws the original look instead (<see cref="AreaInventoryArtLayout"/>, IW20–IW25). ARC-UI-052: in a
/// loot window with the art the frame is the loot window's (<see cref="AreaLootArtLayout"/>, IW26–IW32): the holder's grid on the left,
/// whose items are pressed and released on the hero's grid to take them, and the Take all button. ARC-UI-065: the steal window (mode 3)
/// draws the same frame without Take all; a release across the two grids, or a right release on an item, is a pick-pocket attempt (IW35–IW36).
/// ARC-UI-064: in the barter panel (<see cref="SetBarter"/>) with the art the frame is the barter window's (<see cref="AreaBarterArtLayout"/>,
/// BW1–BW5): the merchant's image, name and quote line, the shown object's grid from y 168, whose items released (or right-released) on the
/// hero's grid are bought and the hero's released on it sold, and the gamble box.
/// </summary>
public partial class AreaInventoryGridArea : Control
{
    private AreaInventoryView? _view;
    private float _scale = 1;
    private Func<float>? _deviceScale;
    private Vector2 _pointer;
    // ARC-UI-1740: an item is held over the bet box now, and what the gate said as it entered (art 344 passes, 346 refuses).
    private bool _overGamble, _gambleGate;
    public event Action<AreaInventoryDrop>? DropRequested;
    /// <summary>ARC-UI-025: a right release with nothing held on an item of the doll or the grid (ARC-UI-032: the release, IW5).</summary>
    public event Action<AreaInventoryHeld>? UseRequested;
    /// <summary>ARC-UI-1740 (BW3): an item held over the barter window's bet box entered it; the host runs the bet gate (0x579840) and answers with <see cref="SetGambleGate"/>.</summary>
    public event Action<AreaInventoryHeld>? GambleEntered;
    /// <summary>ARC-MAGIC-1128: while <see cref="TargetPicking"/>, a left press on an item names it as the target (the item under the pointer, 0x575FA0 in mode 1, ledger IW44); nothing is lifted.</summary>
    public event Action<AreaInventoryHeld>? ItemPicked;
    /// <summary>ARC-MAGIC-1128: a right release while <see cref="TargetPicking"/> cancels the cursor (the spell cursor's own cancel, SU9).</summary>
    public event Action? PickCancelled;
    /// <summary>ARC-MAGIC-1128: a spell's cursor (mode 1) stands over this window: a press picks an item as the spell's target instead of lifting it.</summary>
    public bool TargetPicking { get; set; }
    /// <summary>ARC-UI-035: the pointer rested with nothing held over another item than the last report (its entity) or over none (null).</summary>
    public event Action<string?>? PointerStill;
    /// <summary>ARC-UI-052: the loot window's Take all button was pressed and released.</summary>
    public event Action? TakeAllRequested;
    /// <summary>ARC-UI-054: a left release in the round view of the original look closed the window (IW34).</summary>
    public event Action? HoleReleased;
    /// <summary>ARC-UI-063: a left release in the own window's round view with an item held: the map drop with flag 4 (IW18), which
    /// lifts the item onto the game cursor (the app checks it and closes the window, or puts it back).</summary>
    public event Action<AreaInventoryHeld>? MapDropRequested;
    // ARC-UI-052: the holder's grid's scroll in rows, the holder it belongs to and a press on Take all waiting for its release.
    private int _lootScroll;
    private string? _lootHolder;
    private bool _takeAllDown;
    /// <summary>ARC-DEV-339 (B54): the arrange button (art 340, every mode of the original look) was pressed and released over it.</summary>
    public event Action? ArrangeRequested;
    /// <summary>ARC-DEV-339: the arrange button's press (3000) and release (3001) sounds, the button creator's fields (ledger IW37).</summary>
    public event Action<int>? SoundRequested;
    private bool _arrangeDown;
    /// <summary>ARC-UI-1120 (BW6): a cycle button of the barter window's follower background was pressed and released over it: 0 the left (previous
    /// follower), 1 the right (next).</summary>
    public event Action<int>? CycleRequested;
    private int? _cycleDown;
    /// <summary>ARC-UI-1924 (IW46): a stat hot spot was released over (after its press) or entered by the pointer: the weapon or armour stat lines of the hero or the other party, with the ratings
    /// the window shows for them (null when the reader gave none).</summary>
    public event Action<AreaStatRequest>? StatRequested;
    private AreaStatTarget? _statDown, _statHover;
    // ARC-UI-072 (B30): the other party's doll view is on (the original's toggle 0x681514; clear at every open) and the toggle button a
    // press is waiting on (true the doll button, false the bag button).
    private bool _bodyDoll;
    private bool? _toggleDown;
    // ARC-UI-1918 (IW26, IW30): the hero's own doll view is on (the original's toggle 0x68150C; clear at every open) and the hero's toggle button a press is waiting on.
    private bool _heroDoll;
    private bool? _heroToggleDown;
    private ulong _movedAt;
    private bool _stillReported = true;
    private string? _stillItem;
    /// <summary>How long the pointer must rest for the still-pointer message (SG1: 35 ms).</summary>
    internal const ulong StillMilliseconds = 35;

    /// <summary>ARC-UI-051: the original look, or null for the diagnostic shapes.</summary>
    public AreaInventoryArtLook? Look { get; private set; }

    /// <summary>The item on the cursor, or null.</summary>
    public AreaInventoryHeld? Held { get; private set; }

    /// <summary>ARC-UI-052: the frame is the loot window's original look (the art set and a holder shown, outside the barter panel).</summary>
    public bool LootLook => _barter is null && Look is { LootReady: true } && _view is { Holder: not null };

    /// <summary>ARC-UI-072: the frame shows the other party's doll toggles (the doll arts read, and the shown object is not a container); ARC-UI-1917: the barter window's too
    /// (its doll art is 337, its buttons are the same arts at (8, 123) and (46, 123)).</summary>
    internal bool TogglesShown => (LootLook || BarterLook) && Look!.Art.ContainsKey(DollArt) && Look.Art.ContainsKey(AreaLootArtLayout.DollButtonArt) &&
        Look.Art.ContainsKey(AreaLootArtLayout.BagButtonArt) && (BarterLook ? AreaBarterArtLayout.TogglesShown(_view!) : AreaLootArtLayout.TogglesShown(Mode, _view!));

    /// <summary>The art of the other party's doll view in this window: the barter window's 337, the loot windows' 349.</summary>
    private int DollArt => _barter is not null ? AreaBarterArtLayout.DollViewArt : AreaLootArtLayout.DollViewArt;

    /// <summary>ARC-UI-072: the body's doll view is on (the toggles shown and the doll button chosen).</summary>
    internal bool BodyDoll => _bodyDoll && TogglesShown;

    /// <summary>ARC-UI-1918: the frame shows the hero's own doll toggle (modes 1 to 4 of the original look, with the doll cover and both buttons' arts read).</summary>
    internal bool HeroTogglesShown => (LootLook || BarterLook) && AreaHeroDollToggle.ArtNumbers.All(number => Look!.Art.ContainsKey(number)) && AreaHeroDollToggle.TogglesShown(Mode);

    /// <summary>ARC-UI-1918: the hero's own doll view is on (the toggles shown and the doll button chosen): 341 over the pack, the worn items and covers in the table 0x5CACE8, the
    /// weight lines and ratings, and no grid.</summary>
    internal bool HeroDoll => _heroDoll && HeroTogglesShown;

    /// <summary>ARC-UI-064: the frame is the barter window's original look (the barter panel's, its art set and a shown object).</summary>
    public bool BarterLook => _barter is not null && Look is { BarterReady: true } && _view is { Holder: not null };

    /// <summary>ARC-UI-064: the barter window's own parts (null outside the barter panel).</summary>
    internal AreaBarterFrame? BarterFrame => _barter;
    private AreaBarterFrame? _barter;

    /// <summary>
    /// ARC-UI-1765: the frame is the item identify or repair window's original look (interface modes 5 and 6, ledger IS2): the hero's own window without its boxes and weight lines, with the NPC's image
    /// and quote line (<see cref="AreaServiceArtLayout"/>). The service panel's, with the inventory art set and the hero's own view.
    /// </summary>
    public bool ServiceLook => _service is not null && Look is not null && _view is { Holder: null };

    /// <summary>ARC-UI-1765: the service window's own parts (null outside the service panel).</summary>
    internal AreaServiceFrame? ServiceFrame => _service;
    private AreaServiceFrame? _service;

    /// <summary>ARC-UI-1765: an item of the hero was pressed in the service window's frame (its entity): the app quotes it, and pays on the next press of the same item (IS3).</summary>
    public event Action<string>? ServiceItemPressed;

    /// <summary>ARC-UI-1908: the stored portrait's small image by portrait number (the viewer reads and decodes it on first use; null: it did not read). The windows draw it where
    /// the original's image function 0x553D10 answers a portrait (<see cref="AreaInventoryView.HolderPortrait"/>).</summary>
    public Func<int, Texture2D?>? PortraitTextures { get; set; }

    /// <summary>ARC-UI-1765: the grid draws the item service window's frame with <paramref name="frame"/>'s parts (null: the own window's).</summary>
    public void SetService(AreaServiceFrame? frame)
    {
        if (frame == _service) return;
        _service = frame; QueueRedraw();
    }

    /// <summary>ARC-UI-060: the window's interface mode (IW26): the own window, or a holder's loot or steal window; ARC-UI-064: the barter
    /// window in the barter panel.</summary>
    internal AreaInventoryMode Mode => _barter is not null ? AreaInventoryMode.Barter
        : _view is { Holder: not null } view ? view.Contents ? AreaInventoryMode.Contents : view.Steal ? AreaInventoryMode.Steal : AreaInventoryMode.Loot : AreaInventoryMode.Own;

    /// <summary>ARC-UI-060: the mode the original look draws: the loot or barter frame's while it is drawn, else the own window's.</summary>
    internal AreaInventoryMode LookMode => LootLook || BarterLook ? Mode : AreaInventoryMode.Own;

    /// <summary>ARC-UI-064: the grid draws the barter window's frame with <paramref name="frame"/>'s parts (null: the inventory window's).</summary>
    public void SetBarter(AreaBarterFrame? frame)
    {
        if (frame == _barter) return;
        _barter = frame; QueueRedraw();
    }

    /// <summary>ARC-UI-3033: the kit's background shows under this frame (loot or barter look): the frame draws none of its own background art (the merchant's or body's side, the hero's inventory), and the kit's doll view and
    /// doll cover replace the window's where the kit has them; null: the frame draws its own art as before.</summary>
    internal AreaKitBackdrop? KitBackdrop => _kit;
    private AreaKitBackdrop? _kit;

    /// <summary>ARC-UI-3033: what the last loot or barter draw did for the self-test: the frame's own background art (and the hero's inventory art) drawn, and the kit's doll view and doll cover drawn in place of the window's.</summary>
    internal (bool Background, bool Pack, bool KitDoll, bool KitCover) KitDrawForSelfTest => _kitDraw;
    private (bool Background, bool Pack, bool KitDoll, bool KitCover) _kitDraw;

    public void SetKitBackdrop(AreaKitBackdrop? kit)
    {
        if (kit == _kit) return;
        _kit = kit; QueueRedraw();
    }

    // The shown object's scroll maximum by the window's mode (IW31, BW4).
    private int ScrollMax(AreaInventoryView view) =>
        _barter is not null ? AreaBarterArtLayout.ScrollMax(view.HolderContainer) : AreaLootArtLayout.ScrollMax(view.HolderContainer);

    /// <summary>ARC-UI-054: screen pixels per frame pixel.</summary>
    internal float FrameScale => _scale;

    /// <summary>ARC-UI-054: the round view is shown under the frame: the background art leaves its square out (the original's copy
    /// replaces the square, IW33; the doll art 223 is opaque there).</summary>
    internal bool RoundViewCut
    {
        get => _roundViewCut;
        set { if (_roundViewCut == value) return; _roundViewCut = value; QueueRedraw(); }
    }
    private bool _roundViewCut;

    /// <summary>ARC-UI-069: the main menu is up (the opening shop's barter window): the open turns on no view window, so its art is not drawn
    /// (IW33), and a release in its square is not tested (IW34): it acts as anywhere else in the frame.</summary>
    internal bool MenuUp
    {
        get => _menuUp;
        set { if (_menuUp == value) return; _menuUp = value; QueueRedraw(); }
    }
    private bool _menuUp;

    /// <summary>
    /// ARC-UI-3033: what the original's window arts paint into their background and the kit has no layer for yet (kit.json <c>grid.inventory_cells</c>, size open): the two item grids' black cells with their grid lines, the five
    /// counter fields and the quote line's field (barter). Drawn under the content while the kit's background shows, so the grids and fields do not vanish with the old art; a stand-in until the kit's grid layer lands.
    /// </summary>
    private void KitFields(bool barter)
    {
        var black = new Color(0f, 0f, 0f);
        var line = new Color(.2f, .24f, .32f);
        void Field(float x, float y, float w, float h) => DrawRect(new Rect2(x * _scale, y * _scale, w * _scale, h * _scale), black);
        void Grid(float x, float y, int columns, int rows)
        {
            Field(x, y, columns * AreaInventoryDrag.CellPixels, rows * AreaInventoryDrag.CellPixels);
            for (var column = 0; column <= columns; column++)
                DrawLine(new Vector2((x + column * AreaInventoryDrag.CellPixels) * _scale, y * _scale), new Vector2((x + column * AreaInventoryDrag.CellPixels) * _scale, (y + rows * AreaInventoryDrag.CellPixels) * _scale), line, Math.Max(1f, _scale * .6f));
            for (var row = 0; row <= rows; row++)
                DrawLine(new Vector2(x * _scale, (y + row * AreaInventoryDrag.CellPixels) * _scale), new Vector2((x + columns * AreaInventoryDrag.CellPixels) * _scale, (y + row * AreaInventoryDrag.CellPixels) * _scale), line, Math.Max(1f, _scale * .6f));
        }
        // The hero's pack grid, and the holder's (the merchant's or the body's) grid below the holder's lines.
        Grid(AreaInventoryDrag.GridX, AreaInventoryDrag.GridY, Arcanum.Session.SessionHeroGrid.Columns, Arcanum.Session.SessionHeroGrid.Rows);
        if (barter) Grid(AreaBarterArtLayout.GridX, AreaBarterArtLayout.GridY, AreaBarterArtLayout.Columns, AreaBarterArtLayout.ShownRows);
        else Grid(AreaLootArtLayout.GridX, AreaLootArtLayout.GridY, AreaLootArtLayout.Columns, AreaLootArtLayout.ShownRows);
        foreach (var (x, y, w, h) in AreaInventoryArtLayout.Counts) Field(x, y, w, h);
        if (barter) { var quote = AreaBarterArtLayout.QuoteLine; Field(quote.X - 7, quote.Y - 3, quote.Width + 14, quote.Height + 6); }
    }

    // ARC-UI-054: a background art at (0, 0), without the round view's square while the round view is shown.
    private void Background(AreaInventoryArtLook look, int number, AreaInventoryMode mode)
    {
        if (look.Art.GetValueOrDefault(number) is not { } texture) return;
        Rect2 Scaled(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
        float width = texture.GetWidth(), height = texture.GetHeight();
        if (!_roundViewCut) { DrawTextureRect(texture, Scaled(0, 0, width, height), false); return; }
        var (x, y) = AreaInventoryHole.Corner(mode);
        const float side = AreaInventoryHole.Side;
        foreach (var (px, py, pw, ph) in new[] { (0f, 0f, width, (float)y), (0f, y + side, width, height - y - side), (0f, (float)y, (float)x, side),
            (x + side, (float)y, width - x - side, side) })
            if (pw > 0 && ph > 0) DrawTextureRectRegion(texture, Scaled(px, py, pw, ph), new Rect2(px, py, pw, ph));
    }

    /// <summary>ARC-UI-052: the holder's grid's scroll in rows.</summary>
    internal int LootScroll => _lootScroll;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        _textLayer = new TextLayer { Name = "Texts" };
        AddChild(_textLayer);
        Fit(AreaInventoryDrag.FrameWidth);
    }

    /// <summary>Scales the frame to the width available.</summary>
    public void Fit(float width)
    {
        var scale = Math.Clamp(width / AreaInventoryDrag.FrameWidth, .5f, 1.25f);
        if (scale == _scale && CustomMinimumSize != Vector2.Zero) return;
        _scale = scale;
        CustomMinimumSize = new(AreaInventoryDrag.FrameWidth * _scale, AreaInventoryDrag.FrameHeight * _scale);
        QueueRedraw();
    }

    /// <summary>ARC-UI-051: draws the frame at <paramref name="scale"/> (the original look fills the window's place, no clamp).</summary>
    public void FitScale(float scale)
    {
        if (!(scale > 0)) throw new ArgumentOutOfRangeException(nameof(scale));
        if (scale == _scale && CustomMinimumSize != Vector2.Zero) return;
        _scale = scale;
        CustomMinimumSize = new(AreaInventoryDrag.FrameWidth * _scale, AreaInventoryDrag.FrameHeight * _scale);
        QueueRedraw();
    }

    /// <summary>
    /// ARC-UI-2575: with the Steamwoven layout on, puts <paramref name="panel"/> (whose <paramref name="area"/> is the 800 × 400 frame) so the frame lies in the window zone at the layout's unit scale, whatever the panel
    /// holds around the frame (the switch, the message and quantity rows); false while that layout is not in use (nothing is changed). The one placement of the inventory, loot and barter windows.
    /// </summary>
    public static bool PlaceInZone(PanelContainer panel, AreaInventoryGridArea area, Vector2 viewSize)
    {
        if (AreaGameBarPanel.ZoneWindow(viewSize, (int)AreaInventoryDrag.FrameWidth, (int)AreaInventoryDrag.FrameHeight) is not { } zone) return false;
        area.FitScale((float)zone.Scale);
        var offset = area.GlobalPosition - panel.GlobalPosition;
        panel.Size = new((float)(AreaInventoryDrag.FrameWidth * zone.Scale) + offset.X * 2, 0);
        panel.Size = new(panel.Size.X, panel.GetCombinedMinimumSize().Y);
        panel.Position = new((float)zone.X - offset.X, (float)zone.Y - offset.Y);
        return true;
    }

    /// <summary>ARC-UI-051: sets the original look (null: the diagnostic shapes).</summary>
    public void SetLook(AreaInventoryArtLook? look)
    {
        if (ReferenceEquals(look, Look)) return;
        Look = look; _takeAllDown = false; _arrangeDown = false; _cycleDown = null; _heroToggleDown = null; QueueRedraw();
        // ARC-UI-1194: the textures of a look this view no longer shows are let go.
        if (_onTextureChanged is { } handler)
            foreach (var texture in _watched.Where(texture => look is null || !look.Art.Values.Contains(texture)).ToArray()) { texture.Changed -= handler; _watched.Remove(texture); }
    }

    // ARC-UI-148: the generated interface paints a window's frames at the window's own device scale: the frame scale times the canvas transform's and the
    // viewport's stretch (the bars' own rule). The look's textures are shared by the inventory and the barter panel, so the area that draws a
    // background claims it at that draw (the last drawer wins, which is the window on screen).
    // ARC-UI-1194: this view draws the textures in its own _Draw, so it does not hear a frame swap by itself: a window nothing else redraws (the barter window at rest)
    // would keep the Legacy pixels until the pointer moved. It redraws when a texture it claimed changes (a worker painted its generated frame, a scale or pack change).
    private readonly HashSet<HdInterfaceTexture> _watched = [];
    private Action? _onTextureChanged;

    private void ClaimScale(AreaInventoryArtLook look)
    {
        _deviceScale ??= DeviceScale;
        _onTextureChanged ??= () => { if (GodotObject.IsInstanceValid(this)) QueueRedraw(); };
        foreach (var texture in look.Art.Values)
        {
            if (texture is not HdInterfaceTexture hd) continue;
            hd.ScaleSource = _deviceScale;
            if (_watched.Add(hd)) hd.Changed += _onTextureChanged;
        }
    }

    public override void _EnterTree() => AreaGameBarPanel.KitBackgroundChanged += RedrawForKit;

    private void RedrawForKit() => QueueRedraw();

    /// <summary>ARC-UI-3032: the kit's background of the loot, steal and See Contents windows shows under this window (the bar panel drew it), so the window draws no background art of its own, and takes the kit's doll covers where the set has them.
    /// Never in the barter window (its own backdrop family has no kit window yet) nor the own window.</summary>
    internal bool KitBackgroundShown => _barter is null && LootLook && AreaGameBarPanel.KitBackgroundWindow == SteamwovenKitWindow.Loot;

    /// <summary>ARC-UI-3032: for the self-test: the background arts this window drew at its last loot-mode redraw (the loot art 234 and pack art 221, or none), and the arts it drew from a kit cover instead.</summary>
    internal (IReadOnlyList<int> Backgrounds, IReadOnlyList<int> KitCovers) LootDrawForSelfTest { get; private set; } = ([], []);

    public override void _ExitTree()
    {
        AreaGameBarPanel.KitBackgroundChanged -= RedrawForKit;
        if (_onTextureChanged is { } handler) foreach (var texture in _watched) texture.Changed -= handler;
        _watched.Clear();
    }

    internal float DeviceScale() =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() ? _scale * GetGlobalTransformWithCanvas().Scale.X * GetViewport().GetStretchTransform().Scale.X : _scale;

    public void Render(AreaInventoryView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        // ARC-UI-052: another holder (or none) starts the holder's grid at its top (the open sets the scroll to 0, 0x5731FA).
        if (view.Holder != _lootHolder) { _lootHolder = view.Holder; _lootScroll = 0; _takeAllDown = false; _bodyDoll = false; _toggleDown = null; _heroDoll = false; _heroToggleDown = null; _arrangeDown = false; _cycleDown = null; _statDown = null; _statHover = null; }
        _lootScroll = Math.Clamp(_lootScroll, 0, ScrollMax(view));
        // A held item that is no longer where it was taken from (another move changed its place) cannot be dropped any more.
        if (Held is { } held && (held.Partner ? !view.LootGrid.Any(item => item.Entity == held.Entity) && view.BodyDoll.All(slot => slot.Entity != held.Entity)
                : held.Worn is { } worn ? view.Doll.FirstOrDefault(slot => slot.Slot == worn)?.Entity != held.Entity
                : !view.Grid.Any(item => item.Entity == held.Entity))) Held = null;
        QueueRedraw();
    }

    /// <summary>Puts an item on the cursor: the occupant a wear move took out of its slot (offset 0, ledger IW3).</summary>
    public void Hold(AreaInventoryHeld? held) { Held = held; QueueRedraw(); }

    /// <summary>ARC-UI-029: while the quantity window asks, a press on the doll or grid does nothing (the original's mode 13, QW6).</summary>
    internal bool Blocked { get; set; }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _pointer = motion.Position; _movedAt = Time.GetTicksMsec(); _stillReported = false;
            StatHoverAt(motion.Position / _scale);
            TrackGamble();
            if (Held is not null) QueueRedraw();
            return;
        }
        if (Blocked && @event is InputEventMouseButton) { AcceptEvent(); return; }
        // ARC-UI-052: PROJECT: the wheel scrolls the holder's grid by a row (the original's scroll bar at 330, 136 is not drawn; ARC-UI-064:
        // the barter window's at 330, 168 neither).
        if ((LootLook || BarterLook) && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            ScrollLoot(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
        {
            // The original uses the item on the right release (mouse subtype 3, IW5); the press does nothing in the window.
            _pointer = right.Position;
            if (!right.Pressed) RightReleaseAt(right.Position / _scale);
            AcceptEvent();
            return;
        }
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return;
        _pointer = button.Position;
        if (button.Pressed) PressAt(button.Position / _scale);
        else ReleaseAt(button.Position / _scale);
        AcceptEvent();
    }

    public override void _Process(double delta)
    {
        if (_stillReported || Time.GetTicksMsec() - _movedAt < StillMilliseconds) return;
        _stillReported = true;
        StillAt(_pointer / _scale);
    }

    /// <summary>ARC-UI-035: a still pointer at a point of the frame (0x573C74): with nothing held, the item under it is reported when it is
    /// not the last one reported (the self-test drives it too).</summary>
    internal void StillAt(Vector2 frame)
    {
        if (Held is not null || Blocked || _view is null) return;
        var item = (BarterLook ? AreaBarterArtLayout.Press(_view, BarterHit(frame)) : LootLook ? AreaLootArtLayout.Press(_view, LootHit(frame))
            : AreaInventoryDrag.Press(_view, AreaInventoryDrag.HitTest(frame.X, frame.Y)))?.Entity;
        if (item == _stillItem) return;
        _stillItem = item;
        PointerStill?.Invoke(item);
    }

    /// <summary>The window opened or closed: the still pointer forgets the last item it reported and waits for a move.</summary>
    internal void ResetStill() { _stillItem = null; _stillReported = true; }

    /// <summary>ARC-UI-052: the window opened: the holder's grid starts at its top (0x5731FA sets the scroll to 0 at every open).</summary>
    internal void ResetLoot() { _lootHolder = null; _lootScroll = 0; _takeAllDown = false; _bodyDoll = false; _toggleDown = null; _heroDoll = false; _heroToggleDown = null; _arrangeDown = false; _cycleDown = null; _statDown = null; _statHover = null; }

    /// <summary>ARC-UI-072: the doll or bag button was pressed and released over it (0x573986 sets the toggle, 0x57393B clears it).</summary>
    internal void SetBodyDoll(bool doll) { if (_bodyDoll == doll) return; _bodyDoll = doll; QueueRedraw(); }

    /// <summary>ARC-UI-072: the toggle button under <paramref name="frame"/>, or null (sized by its art).</summary>
    internal bool? ToggleAt(Vector2 frame)
    {
        if (!TogglesShown) return null;
        var (doll, bag) = (Look!.Art[AreaLootArtLayout.DollButtonArt], Look.Art[AreaLootArtLayout.BagButtonArt]);
        return BarterLook ? AreaBarterArtLayout.ToggleHit(frame.X, frame.Y, (doll.GetWidth(), doll.GetHeight()), (bag.GetWidth(), bag.GetHeight()))
            : AreaLootArtLayout.ToggleHit(frame.X, frame.Y, (doll.GetWidth(), doll.GetHeight()), (bag.GetWidth(), bag.GetHeight()));
    }

    /// <summary>ARC-UI-1918: the hero's doll or bag button was pressed and released over it (the button handler sets the toggle on the doll button and clears it on the bag button, ledger IW45).</summary>
    internal void SetHeroDoll(bool doll) { if (_heroDoll == doll) return; _heroDoll = doll; QueueRedraw(); }

    /// <summary>ARC-UI-1918: the hero's toggle button under <paramref name="frame"/> (true the doll button, false the bag button), or null (sized by its art).</summary>
    internal bool? HeroToggleAt(Vector2 frame)
    {
        if (!HeroTogglesShown) return null;
        var (doll, bag) = (Look!.Art[AreaLootArtLayout.DollButtonArt], Look.Art[AreaLootArtLayout.BagButtonArt]);
        return AreaHeroDollToggle.ToggleHit(frame.X, frame.Y, (doll.GetWidth(), doll.GetHeight()), (bag.GetWidth(), bag.GetHeight()));
    }

    /// <summary>ARC-DEV-339: whether the arrange button is drawn: the original look with its art read (the diagnostic look has none).</summary>
    internal bool ArrangeShown => Look is not null && Look.Art.ContainsKey(AreaInventoryArtLayout.ArrangeArt);

    /// <summary>ARC-DEV-339: whether a frame point is on the arrange button (sized by its art).</summary>
    internal bool ArrangeAt(Vector2 frame) => ArrangeShown && Look!.Art[AreaInventoryArtLayout.ArrangeArt] is { } art &&
        AreaInventoryArtLayout.ArrangeHit(frame.X, frame.Y, (art.GetWidth(), art.GetHeight()));

    /// <summary>
    /// ARC-UI-1924 (IW46): the stat hot spot under <paramref name="frame"/>: the hero's art buttons and its rectangles over the damage and armour numbers (all the time in the windows of modes 0, 5 and 6, with the
    /// hero's doll toggle in those of modes 1 to 4) and the other party's two rectangles over its doll's numbers (its doll view on); the arts size their buttons, the diagnostic look has none.
    /// </summary>
    internal AreaStatTarget? StatAt(Vector2 frame)
    {
        if (Look is null) return null;
        (int, int) Size(int art) => Look.Art.TryGetValue(art, out var texture) ? (texture.GetWidth(), texture.GetHeight()) : (0, 0);
        return AreaStatHotSpots.Hit(LookMode, HeroDoll, BodyDoll, frame.X, frame.Y, Size(AreaStatHotSpots.WeaponArt), Size(AreaStatHotSpots.ArmourArt));
    }

    private AreaStatRequest StatRequest(AreaStatTarget target, bool release) =>
        new(target, target is AreaStatTarget.HeroWeapon or AreaStatTarget.HeroArmour ? _view?.HeroRatings : _view?.BodyRatings, release);

    /// <summary>ARC-UI-1924: the pointer at a point of the frame: entering a stat hot spot is the button's hover-in (message subtype 2), which shows its stat lines without changing the bar's mode.</summary>
    internal void StatHoverAt(Vector2 frame)
    {
        var target = Held is null && !Blocked && _view is not null ? StatAt(frame) : null;
        if (target == _statHover) return;
        _statHover = target;
        if (target is { } entered) StatRequested?.Invoke(StatRequest(entered, release: false));
    }

    /// <summary>ARC-UI-1120 (BW6): whether the barter window's cycle buttons are drawn: the original barter look on the follower background with both
    /// arts read.</summary>
    internal bool CycleShown => BarterLook && _barter is { } frame && AreaBarterArtLayout.CycleShown(frame.Background)
        && Look!.Art.ContainsKey(AreaBarterArtLayout.CycleLeftArt) && Look.Art.ContainsKey(AreaBarterArtLayout.CycleRightArt);

    /// <summary>ARC-UI-1120: the cycle button a frame point is on (0 left, 1 right), sized by its art; null none or not shown.</summary>
    internal int? CycleAt(Vector2 frame) => CycleShown && Look!.Art[AreaBarterArtLayout.CycleLeftArt] is { } left && Look.Art[AreaBarterArtLayout.CycleRightArt] is { } right
        ? AreaBarterArtLayout.CycleHit(frame.X, frame.Y, (left.GetWidth(), left.GetHeight()), (right.GetWidth(), right.GetHeight())) : null;

    /// <summary>A left press at a point of the frame (the self-test drives it too).</summary>
    internal void PressAt(Vector2 frame)
    {
        _arrangeDown = false; _cycleDown = null; _heroToggleDown = null; _statDown = null;
        if (Held is not null || _view is null) return;
        // ARC-UI-1918 (0x573850): the hero's toggle buttons are the button system's (the button handler), served in every mode that makes them (1 to 4, the See Contents window too); the press takes the press sound.
        if (HeroToggleAt(frame) is { } heroToggle) { _heroToggleDown = heroToggle; SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.PressSound); QueueRedraw(); return; }
        // ARC-UI-1924 (IW46): the stat hot spots are buttons of the button system too (no sound), served in every mode that makes them; the stat lines come on their release.
        if (StatAt(frame) is { } stat) { _statDown = stat; return; }
        // ARC-RES-1327 (ledger XR16): the See Contents window (mode 4) is not served by the left press of modes 0 to 3 (0x573850 skips it for modes 4, 5 and 6): nothing is picked, not even by a
        // spell or an item cursor, and no toggle or Take all is pressed. The arrange button is the button system's (a press on it is served above all).
        if (Mode == AreaInventoryMode.Contents && !ArrangeAt(frame)) { QueueRedraw(); return; }
        if (TargetPicking)
        {
            // ARC-MAGIC-1336 (0x573850, IW44): the loot window (and the steal window, which the activation closes first) names the item under the pointer by its own layout, the holder's grid and doll
            // and the hero's; its doll and bag buttons are the button system's and take the press first. The barter window is closed by the activation, so it has no target.
            if (LootLook)
            {
                _toggleDown = ToggleAt(frame);
                if (_toggleDown is not null) { QueueRedraw(); return; }
                if (AreaLootArtLayout.Press(_view, LootHit(frame)) is { } lootPicked) Callable.From(() => ItemPicked?.Invoke(lootPicked)).CallDeferred();
                return;
            }
            if (!BarterLook && AreaInventoryDrag.Press(_view, AreaInventoryDrag.HitTest(frame.X, frame.Y)) is { } picked)
                Callable.From(() => ItemPicked?.Invoke(picked)).CallDeferred();
            return;
        }
        // ARC-UI-1120: a cycle button takes the press as the arrange button does (art buttons of the bars: press sound 3000).
        if (CycleAt(frame) is { } cycle) { _cycleDown = cycle; SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.PressSound); QueueRedraw(); return; }
        // ARC-DEV-339: the arrange button takes the press first (the button system, not the window's handler, serves it), with its press sound.
        if (ArrangeAt(frame)) { _arrangeDown = true; SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.PressSound); QueueRedraw(); return; }
        // ARC-UI-1765: in the item identify and repair windows nothing is picked up (the left press of 0x573850 serves the inventory modes 8, 10, 11 and 4, IW2): a press on an item is its quote and
        // payment event, which the app runs through the model (IS3: the original's event is the still pointer or the click, ledger IS3 says which was not settled).
        if (ServiceLook)
        {
            if (AreaServiceArtLayout.ItemAt(_view, frame.X, frame.Y) is { } entity) Callable.From(() => ServiceItemPressed?.Invoke(entity)).CallDeferred();
            return;
        }
        if (BarterLook)
        {
            // ARC-UI-1917: the doll and bag buttons take the press before the window's items, as the loot window's do.
            _toggleDown = ToggleAt(frame);
            Held = _toggleDown is null ? AreaBarterArtLayout.Press(_view, BarterHit(frame)) : null;
        }
        else if (LootLook)
        {
            var hit = LootHit(frame);
            _takeAllDown = hit.TakeAll;
            _toggleDown = ToggleAt(frame);
            Held = _toggleDown is null ? AreaLootArtLayout.Press(_view, hit) : null;
        }
        else Held = AreaInventoryDrag.Press(_view, AreaInventoryDrag.HitTest(frame.X, frame.Y));
        QueueRedraw();
    }

    /// <summary>ARC-UI-052: the loot window's point under <paramref name="frame"/> with the current scroll.</summary>
    private AreaLootHit LootHit(Vector2 frame)
    {
        // ARC-UI-065: the steal window has no Take all button (IW30), so nothing hits it.
        var button = AreaLootArtLayout.TakeAllShown(Mode) ? Look?.Art.GetValueOrDefault(AreaLootArtLayout.TakeAllArt) : null;
        return AreaLootArtLayout.HitTest(frame.X, frame.Y, _lootScroll, button is null ? (0, 0) : (button.GetWidth(), button.GetHeight()), BodyDoll, HeroDoll);
    }

    /// <summary>ARC-UI-064: the barter window's point under <paramref name="frame"/> with the current scroll.</summary>
    internal AreaBarterHit BarterHit(Vector2 frame)
    {
        var box = Look?.Art.GetValueOrDefault(AreaBarterArtLayout.GambleArt);
        return AreaBarterArtLayout.HitTest(frame.X, frame.Y, _lootScroll, box is null ? (0, 0) : (box.GetWidth(), box.GetHeight()), BodyDoll, HeroDoll);
    }

    /// <summary>ARC-UI-052: scrolls the holder's grid by <paramref name="rows"/> within 0..the holder's maximum.</summary>
    internal void ScrollLoot(int rows)
    {
        if (_view is null || BodyDoll) return; // the grid is hidden in the doll view: its scroll stays
        var scroll = Math.Clamp(_lootScroll + rows, 0, ScrollMax(_view));
        if (scroll == _lootScroll) return;
        _lootScroll = scroll; QueueRedraw();
    }

    /// <summary>ARC-UI-025: a right release at a point of the frame: with nothing held, the item under it is offered for use (IW5).</summary>
    internal void RightReleaseAt(Vector2 frame)
    {
        if (Held is not null || _view is null) return;
        // ARC-UI-065: in the loot and steal windows the right release moves the item under it to the other side (IW36): a take, or in the
        // steal window a theft or a plant attempt; ARC-UI-064: in the barter window a buy or a sale. ARC-UI-052: it uses an item only in the
        // player's own window (mode 0, IW5).
        if (TargetPicking) { Callable.From(() => PickCancelled?.Invoke()).CallDeferred(); return; }
        // ARC-UI-1765: the right release is the inventory modes' (IW36, modes 0 to 3); the service windows (5 and 6) ignore it.
        if (ServiceLook) return;
        if (BarterLook)
        {
            if (AreaBarterArtLayout.RightRelease(_view, BarterHit(frame)) is { } trade) Callable.From(() => DropRequested?.Invoke(trade)).CallDeferred();
            return;
        }
        if (LootLook)
        {
            if (AreaLootArtLayout.RightRelease(_view, LootHit(frame), Mode) is { } drop) Callable.From(() => DropRequested?.Invoke(drop)).CallDeferred();
            return;
        }
        if (AreaInventoryDrag.Press(_view, AreaInventoryDrag.HitTest(frame.X, frame.Y)) is not { } item) return;
        Callable.From(() => UseRequested?.Invoke(item)).CallDeferred();
    }

    /// <summary>The frame point of an icon's centre (for the self-test).</summary>
    internal static Vector2 IconCentre(AreaInventoryIcon icon) => new(AreaInventoryDrag.IconX + AreaInventoryDrag.IconWidth / 2,
        (icon == AreaInventoryIcon.Drop ? AreaInventoryDrag.DropIconY : AreaInventoryDrag.UseIconY) + AreaInventoryDrag.IconHeight / 2);

    /// <summary>A frame point on the screen (for the self-test).</summary>
    internal Vector2 FrameToScreenForSelfTest(Vector2 frame) => GetGlobalTransformWithCanvas() * (frame * _scale);

    /// <summary>
    /// ARC-UI-2071 (ledger DH10, IW34, IS3): the left release of the dialog reply press that opened this window, a release no press of this window began. The original's window
    /// handler takes it as any release: the round view's square first (the window closes), else an item of the identify or repair window is quoted or paid for as a click on it.
    /// A Godot control gets a release only from its own press, so the app hands this one in (screen point); the diagnostic list has neither.
    /// </summary>
    internal void LoneRelease(Vector2 screen)
    {
        var frame = GetGlobalTransformWithCanvas().AffineInverse() * screen / _scale;
        if (Look is not null && !_menuUp && AreaInventoryHole.Contains(LookMode, frame.X, frame.Y)) { ReleaseAt(frame); return; }
        if (ServiceLook && _view is { } view && AreaServiceArtLayout.ItemAt(view, frame.X, frame.Y) is { } entity) Callable.From(() => ServiceItemPressed?.Invoke(entity)).CallDeferred();
    }

    /// <summary>A left release at a point of the frame: the held item's drop, then the cursor is empty.</summary>
    internal void ReleaseAt(Vector2 frame)
    {
        if (_cycleDown is { } pressed)
        {
            // ARC-UI-1120: the cycle button acts on its release over it, with its release sound; the window changes follower on the next frame.
            _cycleDown = null;
            if (CycleAt(frame) == pressed)
            {
                SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.ReleaseSound);
                Callable.From(() => CycleRequested?.Invoke(pressed)).CallDeferred();
            }
            return;
        }
        if (_statDown is { } statPressed)
        {
            // ARC-UI-1924 (IW46): a hot spot acts on its release over it (message subtype 0): the bar goes to the message mode and the stat lines are shown.
            _statDown = null;
            if (StatAt(frame) == statPressed) Callable.From(() => StatRequested?.Invoke(StatRequest(statPressed, release: true))).CallDeferred();
            return;
        }
        if (_heroToggleDown is { } heroPressed)
        {
            // ARC-UI-1918: a toggle button acts on its release over it, with its release sound (3001).
            _heroToggleDown = null;
            if (HeroToggleAt(frame) == heroPressed)
            {
                SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.ReleaseSound);
                SetHeroDoll(heroPressed);
            }
            return;
        }
        if (_arrangeDown)
        {
            // ARC-DEV-339: a button acts on its release over it, with its release sound (3001).
            _arrangeDown = false;
            if (ArrangeAt(frame))
            {
                SoundRequested?.Invoke(Arcanum.TerrainViewer.Core.AreaGameBarLayout.ReleaseSound);
                Callable.From(() => ArrangeRequested?.Invoke()).CallDeferred();
            }
            return;
        }
        if (!LootLook && !BarterLook) _toggleDown = null;
        if (!LootLook) _takeAllDown = false;
        if ((LootLook || BarterLook) && _toggleDown is { } toggle)
        {
            // ARC-UI-072: a toggle button acts on its release over it.
            _toggleDown = null;
            if (ToggleAt(frame) == toggle) SetBodyDoll(toggle);
            return;
        }
        if (LootLook && _takeAllDown)
        {
            // ARC-UI-052: a button acts on its release over it.
            _takeAllDown = false;
            if (LootHit(frame).TakeAll) Callable.From(() => TakeAllRequested?.Invoke()).CallDeferred();
            return;
        }
        // ARC-UI-054: a release in the round view closes the window (0x574559, IW34); ARC-UI-060: by the window's mode, a held item goes
        // back first outside the own window. ARC-UI-063: in the own window a held item released there is the map drop of IW18: the app
        // lifts it onto the game cursor and closes the window, or puts it back.
        if (Look is not null && !_menuUp && AreaInventoryHole.Contains(LookMode, frame.X, frame.Y))
        {
            var release = AreaInventoryHole.Release(LookMode, Held is not null);
            var lifted = Held;
            if (Held is not null) { Held = null; QueueRedraw(); }
            if (release == AreaRoundViewRelease.MapDrop) Callable.From(() => MapDropRequested?.Invoke(lifted!)).CallDeferred();
            else Callable.From(() => HoleReleased?.Invoke()).CallDeferred();
            return;
        }
        if (Held is not { } held) return;
        Held = null;
        _overGamble = false;
        var drop = BarterLook ? AreaBarterArtLayout.Release(held, BarterHit(frame)) : LootLook ? AreaLootArtLayout.Release(held, LootHit(frame), Mode)
            : AreaInventoryDrag.Release(held, AreaInventoryDrag.HitTest(frame.X, frame.Y));
        QueueRedraw();
        // The app re-renders the window, which may put an occupant on the cursor: after this input event returns.
        Callable.From(() => DropRequested?.Invoke(drop)).CallDeferred();
    }

    /// <summary>
    /// ARC-UI-1740 (BW3, 0x573E67..0x573F1C): the pointer moved with an item held: entering the bet box asks the host's gate once (<see cref="GambleEntered"/>), which
    /// sets the art 344 or 346 through <see cref="SetGambleGate"/>; moving out sets the resting art 345 again.
    /// </summary>
    private void TrackGamble() => TrackGambleAt(_pointer / _scale);

    internal void TrackGambleAt(Vector2 frame)
    {
        var over = BarterLook && Held is not null && BarterHit(frame).Gamble;
        if (over == _overGamble) return;
        _overGamble = over;
        if (over) { _gambleGate = false; GambleEntered?.Invoke(Held!); }
        QueueRedraw();
    }

    /// <summary>ARC-UI-1740: the host's answer to <see cref="GambleEntered"/>: the gate passed (art 344) or refused the item (art 346).</summary>
    internal void SetGambleGate(bool passes) { _gambleGate = passes; QueueRedraw(); }

    /// <summary>ARC-UI-1740: the bet box's art number now (for the self-test): 345 at rest, 344 or 346 with an item held over it.</summary>
    internal int GambleArtNow => AreaBarterArtLayout.GambleBoxArt(_overGamble && Held is not null, _gambleGate);

    /// <summary>ARC-DEV-306: the screen point of a frame point (for the native smoke, which pushes real pointer events).</summary>
    internal Vector2 ScreenPointForSelfTest(Vector2 frame) => GetGlobalTransformWithCanvas() * (frame * _scale);

    /// <summary>The frame point of a grid cell's centre or of a wear slot's centre (for the self-test).</summary>
    internal static Vector2 CellCentre(int cell) => new(AreaInventoryDrag.GridX + (cell % 10 + .5f) * AreaInventoryDrag.CellPixels,
        AreaInventoryDrag.GridY + (cell / 10 + .5f) * AreaInventoryDrag.CellPixels);

    internal static Vector2 SlotCentre(EquipmentSlot slot)
    {
        var rect = AreaInventoryDrag.SlotRects[slot];
        return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    // ARC-UI-2795: the windows' texts are the original's art fonts. The frame's draw collects them as runs (placed in the frame's scaled pixels) and a child layer, which
    // has the art text draw's premultiplied material (the frame's own draws are not all opaque), draws them over the frame in the same redraw.
    private readonly List<TextRun> _runs = [];
    private TextLayer? _textLayer;
    private readonly record struct TextRun(AreaArtFontFace Face, IReadOnlyList<(int X, AreaArtGlyph Glyph)> Glyphs, int X, int Y, AreaArtTextBlend Blend);

    private sealed partial class TextLayer : Control
    {
        internal IReadOnlyList<TextRun> Runs { get; set; } = [];
        internal float TextScale { get; set; } = 1;

        public TextLayer()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
            AreaArtTextDraw.Prepare(this);
        }

        public override void _Draw()
        {
            foreach (var run in Runs) AreaArtTextDraw.Glyphs(this, run.Face, run.Glyphs, run.X, run.Y, (255, 255, 255), run.Blend, scale: TextScale);
        }
    }

    /// <summary>One line of <paramref name="face"/> text in <paramref name="rect"/> (the original's pixels) at the frame's scale: left, centred or right in its width and
    /// centred in its height unless <paramref name="middle"/> is false (PROJECT: the exe's anchors are not read). White, as the original's fonts of these windows.</summary>
    private void Line(AreaArtFontFace? face, string text, (int X, int Y, int Width, int Height) rect, AreaArtLabel.Alignment align = AreaArtLabel.Alignment.Left,
        bool middle = true, AreaArtTextBlend blend = AreaArtTextBlend.Coverage)
    {
        if (face is null || text.Length == 0) return;
        var width = face.Font.Measure(text).Width;
        var x = align switch { AreaArtLabel.Alignment.Right => rect.Width - width, AreaArtLabel.Alignment.Centre => (rect.Width - width) / 2, _ => 0 };
        var y = middle ? (rect.Height - AreaTextFontLayout.LineHeight(face.Frames)) / 2 : 0;
        _runs.Add(new(face, face.Font.Layout(text), (int)MathF.Round((rect.X + Math.Max(0, x)) * _scale), (int)MathF.Round((rect.Y + y) * _scale), blend));
    }

    /// <summary>A text broken at <paramref name="rect"/>'s width and drawn from its top left, as many lines as its height takes (the message window's text draw, MW26).</summary>
    private void Block(AreaArtFontFace? face, string text, (int X, int Y, int Width, int Height) rect)
    {
        if (face is null || text.Length == 0) return;
        foreach (var line in AreaTextFontLayout.Place(face.Font, AreaTextFontLayout.LineHeight(face.Frames), rect, text).Lines)
            _runs.Add(new(face, line.Glyphs, (int)MathF.Round((rect.X + line.X) * _scale), (int)MathF.Round((rect.Y + line.Y) * _scale), AreaArtTextBlend.Coverage));
    }

    public override void _Draw()
    {
        _runs.Clear();
        DrawFrame();
        if (_textLayer is null) return;
        _textLayer.Runs = [.. _runs];
        _textLayer.TextScale = _scale;
        _textLayer.QueueRedraw();
    }

    private void DrawFrame()
    {
        if (LootLook || BarterLook) { DrawLoot(Look!); return; }
        if (Look is { } look) { DrawOriginal(look); return; }
        var font = GetThemeDefaultFont();
        var size = Math.Max(8, (int)(11 * _scale));
        var line = new Color(.35f, .4f, .45f);
        var text = new Color(.85f, .88f, .9f);
        Rect2 Scaled(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
        void Label(Rect2 rect, string label, Color color) =>
            DrawString(font, rect.Position + new Vector2(3, size + 2), label, HorizontalAlignment.Left, Math.Max(1, rect.Size.X - 6), size, color);
        foreach (var (slot, r) in AreaInventoryDrag.SlotRects)
        {
            var rect = Scaled(r.X, r.Y, r.Width, r.Height);
            var worn = _view?.Doll.FirstOrDefault(entry => entry.Slot == slot);
            var shown = worn?.Entity is { } entity && entity != Held?.Entity;
            DrawRect(rect, shown ? new Color(.25f, .22f, .14f) : new Color(.1f, .11f, .14f));
            DrawRect(rect, line, false);
            Label(rect, shown ? worn!.Name ?? "?" : AreaInventoryHost.SlotName(slot), shown ? text : new Color(.5f, .55f, .6f));
        }
        foreach (var (icon, y) in new[] { (AreaInventoryIcon.Drop, AreaInventoryDrag.DropIconY), (AreaInventoryIcon.Use, AreaInventoryDrag.UseIconY) })
        {
            var rect = Scaled(AreaInventoryDrag.IconX, y, AreaInventoryDrag.IconWidth, AreaInventoryDrag.IconHeight);
            DrawRect(rect, new Color(.16f, .14f, .1f));
            DrawRect(rect, line, false);
            Label(rect, icon.ToString(), new Color(1f, .85f, .55f));
        }
        var grid = Scaled(AreaInventoryDrag.GridX, AreaInventoryDrag.GridY, AreaInventoryDrag.GridRight - AreaInventoryDrag.GridX,
            AreaInventoryDrag.GridBottom - AreaInventoryDrag.GridY);
        DrawRect(grid, new Color(.08f, .09f, .12f));
        for (var column = 0; column <= 10; column++)
            DrawLine(new(grid.Position.X + column * AreaInventoryDrag.CellPixels * _scale, grid.Position.Y), new(grid.Position.X + column * AreaInventoryDrag.CellPixels * _scale, grid.End.Y), line);
        for (var row = 0; row <= 12; row++)
            DrawLine(new(grid.Position.X, grid.Position.Y + row * AreaInventoryDrag.CellPixels * _scale), new(grid.End.X, grid.Position.Y + row * AreaInventoryDrag.CellPixels * _scale), line);
        if (_view is null) return;
        foreach (var item in _view.Grid)
        {
            // The held item stays in its place but is not drawn there (the original's item flag 0x10, IW2).
            if (item.Entity == Held?.Entity) continue;
            var rect = Item(item).Grow(-2 * _scale);
            DrawRect(rect, new Color(.2f, .3f, .38f));
            DrawRect(rect, new Color(.5f, .65f, .75f), false);
            Label(rect, item.Name, text);
        }
        if (Held is { } held)
        {
            var name = _view.Grid.FirstOrDefault(item => item.Entity == held.Entity)?.Name ?? _view.Doll.FirstOrDefault(slot => slot.Entity == held.Entity)?.Name ?? "?";
            var placed = _view.Grid.FirstOrDefault(item => item.Entity == held.Entity);
            var (w, h) = placed is null ? (1, 1) : (placed.Width, placed.Height);
            var cell = AreaInventoryDrag.CellPixels * _scale;
            var origin = _pointer - new Vector2(held.Offset % 10 + .5f, held.Offset / 10 + .5f) * cell;
            var rect = new Rect2(origin, new Vector2(w, h) * cell);
            DrawRect(rect, new Color(.45f, .38f, .18f, .85f));
            DrawRect(rect, new Color(1f, .85f, .55f), false);
            Label(rect, name, text);
        }

        Rect2 Item(AreaGridItem item) => Scaled(AreaInventoryDrag.GridX + item.Slot % 10 * AreaInventoryDrag.CellPixels,
            AreaInventoryDrag.GridY + item.Slot / 10 * AreaInventoryDrag.CellPixels, item.Width * AreaInventoryDrag.CellPixels, item.Height * AreaInventoryDrag.CellPixels);
    }

    // ARC-UI-051: the redraw's mode-0 order (0x576520): the doll and pack arts, the three lines, the counts, the boxes, the grid items, the
    // worn items and the empty slots' covers; then the held item on the cursor.
    private void DrawOriginal(AreaInventoryArtLook look)
    {
        ClaimScale(look);
        Rect2 Scaled(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
        void Art(int number, float x, float y)
        {
            if (look.Art.GetValueOrDefault(number) is { } texture) DrawTextureRect(texture, Scaled(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
        void Item(Texture2D? texture, (float X, float Y, float Width, float Height) rect, Vector2? origin = null, string? name = null)
        {
            if (texture is null)
            {
                // An item whose art does not read keeps the diagnostic look: a framed box with its name.
                var box = (origin is { } start ? new Rect2(start, new Vector2(rect.Width, rect.Height) * _scale) : Scaled(rect.X, rect.Y, rect.Width, rect.Height)).Grow(-2 * _scale);
                DrawRect(box, new Color(.2f, .3f, .38f, .85f));
                DrawRect(box, new Color(.5f, .65f, .75f), false);
                if (name is not null) DrawString(GetThemeDefaultFont(), box.Position + new Vector2(3, 11 * _scale + 2), name, HorizontalAlignment.Left, Math.Max(1, box.Size.X - 6), Math.Max(8, (int)(11 * _scale)), AreaInventoryArtLook.TextColour);
                return;
            }
            var (x, y, w, h) = AreaInventoryArtLayout.Place(rect, texture.GetWidth(), texture.GetHeight());
            var placed = origin is { } at ? new Rect2(at + new Vector2(x - rect.X, y - rect.Y) * _scale, new Vector2(w, h) * _scale) : Scaled(x, y, w, h);
            DrawTextureRect(texture, placed, false);
        }
        // ARC-UI-3037: over the kit's background (the paper doll and the pack, also of the identify and repair windows) the window draws neither of its own background arts; the view window's art, items and text stay.
        if (AreaGameBarPanel.KitBackgroundWindow != SteamwovenKitWindow.Inventory)
        {
            Background(look, AreaInventoryArtLayout.DollArt, AreaInventoryMode.Own);
            Art(AreaInventoryArtLayout.PackArt, AreaInventoryArtLayout.PackX, 0);
        }
        else AreaKitStandIns.Inventory(this, _scale);
        // ARC-UI-054: the view window's art over the round view (0x551080 draws it after the copy, IW33).
        var (holeX, holeY) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
        Art(AreaInventoryHole.Art(AreaInventoryMode.Own), holeX, holeY);
        var fonts = look.Fonts;
        if (_view is null) return;
        // ARC-UI-1765 (0x576520 modes 5 and 6): the NPC's image at half its size, then its quote line in its rectangle; the weight lines are mode 0's alone (IW24) and the boxes are not made (IW19).
        var service = ServiceLook ? _service : null;
        if (service is not null)
        {
            // ARC-UI-1908 (0x576520 modes 5 and 6): a portrait is drawn by 0x4CE680 forced to 32 by 32 at the corner (the art is halved); PROJECT: a portrait file that did not read leaves the halved art.
            if (service.Portrait is { } portraitId && PortraitTextures?.Invoke(portraitId) is { } portrait)
            {
                var (ix, iy, iw, ih) = AreaServiceArtLayout.PortraitRect;
                DrawTextureRect(portrait, Scaled(ix, iy, iw, ih), false);
            }
            else if (service.Image is { } image && look.Art.GetValueOrDefault(image) is { } picture)
            {
                var (ix, iy, iw, ih) = AreaServiceArtLayout.ImageRect(picture.GetWidth(), picture.GetHeight());
                DrawTextureRect(picture, Scaled(ix, iy, iw, ih), false);
            }
            // ARC-UI-2795 (exe 0x576520): the quote line is font 0x682C74 (art 229, white).
            if (service.Quote.Length > 0) Block(fonts?.Text, service.Quote, AreaServiceArtLayout.QuoteLine);
        }
        else
        {
            var lines = look.Texts.Lines(_view.Carry, _view.Speed);
            for (var i = 0; i < lines.Count; i++) Line(fonts?.Text, lines[i], AreaInventoryArtLayout.Lines[i]);
        }
        var counts = new[] { _view.Gold }.Concat(Enumerable.Range(0, 4).Select(type => type < _view.Ammo.Count ? _view.Ammo[type] : 0)).ToArray();
        for (var i = 0; i < counts.Length; i++)
            Line(fonts?.Bar, counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture), AreaInventoryArtLayout.Counts[i], blend: AreaArtTextBlend.Additive);
        var over = Held is null ? null : AreaInventoryDrag.HitTest(_pointer.X / _scale, _pointer.Y / _scale).Icon;
        foreach (var icon in service is not null ? Array.Empty<AreaInventoryIcon>() : new[] { AreaInventoryIcon.Use, AreaInventoryIcon.Drop })
        {
            var (x, y) = AreaInventoryArtLayout.Box(icon);
            Art(AreaInventoryArtLayout.BoxArt(icon, over == icon), x, y);
        }
        // ARC-UI-1267 (IW25, IW41): the hero's damage and armour ratings (0x464630, 0x464700) under the doll, in the rectangles of 0x5CAEF8 and 0x5CAF08: the first ends at the
        // right edge of its rectangle, the second starts at the left edge of its (PROJECT: the text's top sits on the rectangle's top, F0 for the original's vertical anchor).
        if (_view.HeroRatings is { } ratings)
        {
            // ARC-UI-2795: the PC's two ratings are font 0x681390 (art 486, white).
            Line(fonts?.Rating, ratings.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaInventoryArtLayout.DamageRatingBox, AreaArtLabel.Alignment.Right, middle: false);
            Line(fonts?.Rating, ratings.Armour.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaInventoryArtLayout.ArmourRatingBox, AreaArtLabel.Alignment.Left, middle: false);
        }
        // ARC-DEV-339: the arrange button, drawn with its first frame as the other buttons are.
        Art(AreaInventoryArtLayout.ArrangeArt, AreaInventoryArtLayout.ArrangeButton.X, AreaInventoryArtLayout.ArrangeButton.Y);
        // ARC-UI-1924 (IW46): the hot spots' art buttons 676 and 677 beside the damage and armour numbers (visible in the windows of modes 0, 5 and 6).
        foreach (var weapon in new[] { true, false })
        {
            var (bx, by) = AreaStatHotSpots.ArtButton(AreaInventoryMode.Own, weapon);
            Art(weapon ? AreaStatHotSpots.WeaponArt : AreaStatHotSpots.ArmourArt, bx, by);
        }
        // ARC-UI-1765: the remembered item's cells are filled before its art is drawn (the redraw 0x576520 tests the remembered object 0x681458 in its grid pass).
        if (service is not null && AreaServiceArtLayout.SelectedRect(_view, service.Selected) is { } selected)
        {
            var (red, green, blue) = AreaServiceArtLayout.SelectedFill;
            DrawRect(Scaled(selected.X, selected.Y, selected.Width, selected.Height), new Color(red / 255f, green / 255f, blue / 255f));
        }
        foreach (var item in _view.Grid)
        {
            // The held item stays in its place but is not drawn there (the original's item flag 0x10, IW2).
            if (item.Entity == Held?.Entity) continue;
            Item(look.ItemArt(item.Entity), (AreaInventoryDrag.GridX + item.Slot % 10 * AreaInventoryDrag.CellPixels,
                AreaInventoryDrag.GridY + item.Slot / 10 * AreaInventoryDrag.CellPixels, item.Width * AreaInventoryDrag.CellPixels, item.Height * AreaInventoryDrag.CellPixels),
                name: item.Name);
        }
        foreach (var (slot, rect) in AreaInventoryDrag.SlotRects)
        {
            // A held worn item leaves its slot to the cover, as an empty one (0x5772B4 skips it, 0x577851 covers every unmarked slot).
            var entry = _view.Doll.FirstOrDefault(entry => entry.Slot == slot);
            // ARC-UI-1267 (IW43): the weapon's slot is filled dark red first when the weapon needs more strength than the hero has (0x465E90); the item is drawn on the fill.
            if (slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && entry?.Entity is { } wielded && wielded != Held?.Entity && _view.HeroRatings is { WeaponTooHeavy: true })
            {
                var (red, green, blue) = AreaLootArtLayout.TooHeavyFill;
                DrawRect(Scaled(rect.X, rect.Y, rect.Width, rect.Height), new Color(red / 255f, green / 255f, blue / 255f));
            }
            if (entry?.Entity is { } worn && worn != Held?.Entity) Item(look.ItemArt(worn), rect, name: entry.Name ?? "?");
            else Art(AreaInventoryArtLayout.Covers[slot], rect.X, rect.Y);
        }
        if (Held is { } held)
        {
            var placed = _view.Grid.FirstOrDefault(item => item.Entity == held.Entity);
            var texture = look.ItemArt(held.Entity);
            // ARC-UI-055: a worn item on the cursor takes its art's cells (0x461780), as it will in the grid.
            var (w, h) = placed is not null ? (placed.Width, placed.Height)
                : texture is not null ? Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)texture.GetWidth(), (uint)texture.GetHeight())) : (1, 1);
            var cell = AreaInventoryDrag.CellPixels * _scale;
            var origin = _pointer - new Vector2(held.Offset % 10 + .5f, held.Offset / 10 + .5f) * cell;
            var name = placed?.Name ?? _view.Doll.FirstOrDefault(slot => slot.Entity == held.Entity)?.Name ?? "?";
            Item(texture, (0, 0, w * AreaInventoryDrag.CellPixels, h * AreaInventoryDrag.CellPixels), origin, name);
        }
    }

    // ARC-UI-052: the redraw's order in the loot mode (0x576520): the loot and pack arts, the holder's image, the counts, the boxes, the hero's
    // grid items (no doll, no weight lines), the holder's grid items clipped to its shown rows; then the Take all button and the held item.
    // ARC-UI-064: the barter mode the same way with its background, the merchant's name and quote line after its image, the gamble box after
    // the drop and use boxes, the shown grid from y 168 (7 rows) and no Take all button.
    private void DrawLoot(AreaInventoryArtLook look)
    {
        ClaimScale(look);
        var view = _view!;
        var barter = BarterLook ? _barter : null;
        Rect2 Scaled(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
        // ARC-UI-3032: over the kit's background the loot window draws no background art of its own and the doll covers come from the kit when the set has them (the original art otherwise).
        var kit = KitBackgroundShown;
        var coversFromKit = new List<int>();
        void Art(int number, float x, float y)
        {
            if (kit && AreaGameBarPanel.KitCover(number) is { } cover)
            {
                DrawTextureRect(cover.Texture, Scaled(x, y, (float)cover.Width, (float)cover.Height), false);
                coversFromKit.Add(number);
                return;
            }
            if (look.Art.GetValueOrDefault(number) is { } texture) DrawTextureRect(texture, Scaled(x, y, texture.GetWidth(), texture.GetHeight()), false);
        }
        // An item's art centred in its rectangle (scaled down when larger) and cut to the band from clipTop to clipBottom of the frame.
        void Item(Texture2D? texture, (float X, float Y, float Width, float Height) rect, string name, float clipTop = float.MinValue, float clipBottom = float.MaxValue)
        {
            var (top, bottom) = (Math.Max(rect.Y, clipTop), Math.Min(rect.Y + rect.Height, clipBottom));
            if (bottom <= top) return;
            if (texture is null)
            {
                // An item whose art does not read keeps the diagnostic look: a framed box with its name.
                var box = Scaled(rect.X, top, rect.Width, bottom - top).Grow(-2 * _scale);
                DrawRect(box, new Color(.2f, .3f, .38f, .85f));
                DrawRect(box, new Color(.5f, .65f, .75f), false);
                DrawString(GetThemeDefaultFont(), box.Position + new Vector2(3, 11 * _scale + 2), name, HorizontalAlignment.Left, Math.Max(1, box.Size.X - 6), Math.Max(8, (int)(11 * _scale)), AreaInventoryArtLook.TextColour);
                return;
            }
            var (x, y, w, h) = AreaInventoryArtLayout.Place(rect, texture.GetWidth(), texture.GetHeight());
            var (from, to) = (Math.Max(y, clipTop), Math.Min(y + h, clipBottom));
            if (to <= from) return;
            var ratio = texture.GetHeight() / h;
            DrawTextureRectRegion(texture, Scaled(x, from, w, to - from), new Rect2(0, (from - y) * ratio, texture.GetWidth(), (to - from) * ratio));
        }
        // ARC-UI-3033: over a complete kit background of the barter window the frame's own background art (the merchant's side, and the hero's inventory below) is not drawn; the loot window's kit (ARC-UI-3032) is the bool above.
        var barterKit = _kit;
        void Overlay(AreaKitOverlay overlay, float x, float y) => DrawTextureRect(overlay.Texture, Scaled(x, y, (float)overlay.UnitWidth, (float)overlay.UnitHeight), false);
        _kitDraw = (barterKit is null, barterKit is null, false, false);
        IReadOnlyList<int>? backgrounds = barterKit is not null ? [] : barter is not null ? null : SteamwovenKitComposer.LootBackgroundArts(kit);
        LootDrawForSelfTest = (backgrounds ?? [barter!.Background, AreaInventoryArtLayout.PackArt], coversFromKit);
        if (barterKit is not null) KitFields(barter is not null);
        else if (backgrounds is null || backgrounds.Contains(AreaLootArtLayout.LootArt)) Background(look, barter?.Background ?? AreaLootArtLayout.LootArt, LookMode);
        var (holeX, holeY) = AreaInventoryHole.Corner(LookMode);
        if (!_menuUp) Art(AreaInventoryHole.Art(LookMode), holeX, holeY);
        // ARC-UI-1908 (0x576520 modes 1-4, IW27): a portrait the image function answers is drawn at its own size at the image's corner by 0x4CE470; PROJECT: a portrait file that
        // did not read leaves the flag-and-race art in its place, so the window is not empty.
        if (view.HolderPortrait is { } portraitId && PortraitTextures?.Invoke(portraitId) is { } portrait)
        {
            var (px, py) = AreaLootArtLayout.Image;
            DrawTextureRect(portrait, Scaled(px, py, portrait.GetWidth(), portrait.GetHeight()), false);
        }
        else if (view.HolderImage is { } image) Art(image, AreaLootArtLayout.Image.X, AreaLootArtLayout.Image.Y);
        var fonts = look.Fonts;
        if (barter is not null)
        {
            // The merchant's name centred on x 215 (0x5766A4..0x57671A) in font 0x682418 (art 27, white) and the quote line below it (0x57687F) in font 0x682C74 (art 229, white; ARC-UI-2795).
            // A name wider than its 200-pixel line is cut with an ellipsis (PROJECT), so it stays centred inside the line.
            if (fonts?.Title is { } titleFace)
                Line(titleFace, AreaSaveListArtLayout.Fit(titleFace.Font, barter.Name, AreaBarterArtLayout.NameLine.Width), AreaBarterArtLayout.NameLine, AreaArtLabel.Alignment.Centre);
            if (barter.Quote.Length > 0) Block(fonts?.Text, barter.Quote, AreaBarterArtLayout.QuoteLine);
        }
        if (backgrounds is null || backgrounds.Contains(AreaInventoryArtLayout.PackArt)) Art(AreaInventoryArtLayout.PackArt, AreaInventoryArtLayout.PackX, 0);
        // ARC-UI-1918 (IW26, 0x576520): with the hero's toggle set the doll cover 341 is drawn over the pack at (361, 0), then the three weight lines in the rectangles of 0x5CAEC8
        // and the two ratings in those of 0x5CAF18 (the own window's lines and ratings, moved).
        var heroDoll = HeroDoll;
        if (heroDoll)
        {
            if (barterKit?.HeroDollCover is { } kitCover) { Overlay(kitCover, AreaHeroDollToggle.Cover.X, AreaHeroDollToggle.Cover.Y); _kitDraw.KitCover = true; }
            else Art(AreaHeroDollToggle.CoverArt, AreaHeroDollToggle.Cover.X, AreaHeroDollToggle.Cover.Y);
            var lines = look.Texts.Lines(view.Carry, view.Speed);
            for (var i = 0; i < lines.Count && lines[i].Length > 0; i++)
            {
                var (lx, ly, lw, lh) = AreaHeroDollToggle.Lines[i];
                Line(fonts?.Text, lines[i], (lx, ly, lw, lh));
            }
            if (view.HeroRatings is { } heroRatings)
            {
                // The damage rating ends at the right edge of its rectangle, the armour rating starts at the left edge of its (as the own window's; PROJECT: the text's top sits on the rectangle's top).
                // ARC-UI-2795: the PC's ratings are font 0x681390 (art 486, white).
                Line(fonts?.Rating, heroRatings.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaHeroDollToggle.DamageRatingBox, AreaArtLabel.Alignment.Right, middle: false);
                Line(fonts?.Rating, heroRatings.Armour.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaHeroDollToggle.ArmourRatingBox, AreaArtLabel.Alignment.Left, middle: false);
            }
        }
        var counts = new[] { view.Gold }.Concat(Enumerable.Range(0, 4).Select(type => type < view.Ammo.Count ? view.Ammo[type] : 0)).ToArray();
        for (var i = 0; i < counts.Length; i++)
        {
            Line(fonts?.Bar, counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture), AreaInventoryArtLayout.Counts[i], blend: AreaArtTextBlend.Additive);
        }
        var over = Held is null ? null : barter is not null ? BarterHit(_pointer / _scale).Icon : LootHit(_pointer / _scale).Icon;
        // ARC-RES-1327 (XR16): the open turns the drop and use boxes off in modes 4, 5 and 6 (0x5727B0), so the See Contents window draws neither.
        foreach (var icon in Mode == AreaInventoryMode.Contents ? Array.Empty<AreaInventoryIcon>() : new[] { AreaInventoryIcon.Use, AreaInventoryIcon.Drop })
        {
            var (x, y) = AreaInventoryArtLayout.Box(icon);
            Art(barter is not null ? AreaBarterArtLayout.BoxArt(icon, Held, over == icon) : AreaLootArtLayout.BoxArt(icon, Held, over == icon), x, y);
        }
        if (barter is not null) Art(GambleArtNow, AreaBarterArtLayout.GambleBox.X, AreaBarterArtLayout.GambleBox.Y);
        // ARC-UI-1918 (0x576520): the hero's grid items are drawn only while its toggle is clear; with it set the worn items fill the slot rectangles of 0x5CACE8 at full size (PROJECT: scaled
        // down to fit), the weapon's slot dark red first when its weapon is too heavy (IW43), and a cover, at its own size, on the top-left of every slot with no item or whose item is on the cursor.
        foreach (var item in heroDoll ? [] : view.Grid)
        {
            if (item.Entity == Held?.Entity) continue;
            Item(look.ItemArt(item.Entity), (AreaInventoryDrag.GridX + item.Slot % 10 * AreaInventoryDrag.CellPixels,
                AreaInventoryDrag.GridY + item.Slot / 10 * AreaInventoryDrag.CellPixels, item.Width * AreaInventoryDrag.CellPixels, item.Height * AreaInventoryDrag.CellPixels), item.Name);
        }
        if (heroDoll)
        {
            foreach (var (slot, rect) in AreaHeroDollToggle.SlotRects)
            {
                var entry = view.Doll.FirstOrDefault(entry => entry.Slot == slot);
                if (slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && entry?.Entity is { } wielded && wielded != Held?.Entity && view.HeroRatings is { WeaponTooHeavy: true })
                {
                    var (red, green, blue) = AreaLootArtLayout.TooHeavyFill;
                    DrawRect(Scaled(rect.X, rect.Y, rect.Width, rect.Height), new Color(red / 255f, green / 255f, blue / 255f));
                }
                if (entry?.Entity is { } worn && worn != Held?.Entity) Item(look.ItemArt(worn), rect, entry.Name ?? "?");
                else Art(AreaInventoryArtLayout.Covers[slot], rect.X, rect.Y);
            }
        }
        var (shownTop, shownBottom) = barter is not null
            ? (AreaBarterArtLayout.GridY, AreaBarterArtLayout.GridY + AreaBarterArtLayout.ShownRows * AreaBarterArtLayout.CellPixels)
            : (AreaLootArtLayout.GridY, AreaLootArtLayout.GridY + AreaLootArtLayout.ShownRows * AreaLootArtLayout.CellPixels);
        // ARC-UI-072 (B30, IW29): the body's doll view replaces the holder's grid: art 349 at (0, 134), each worn item in its slot rectangle (PROJECT:
        // its inventory art, as the hero's doll draws it; the original draws the paper-doll art at two thirds of its size), and a cover stretched to
        // the rectangle of every empty slot or of the slot whose item is on the cursor. ARC-UI-1121 (IW29, IW41, IW43): the weapon's slot is filled
        // dark red first when the weapon is too heavy for the body (0x465E90), and the body's two ratings (0x464630, 0x464700) are printed under the doll.
        // ARC-UI-1917 (IW29): the barter window's doll view is the same with art 337 at (0, 157), the same nine rectangles (0x5CAD78) and covers.
        var doll = BodyDoll;
        if (doll)
        {
            var (dollX, dollY) = barter is not null ? AreaBarterArtLayout.DollView : AreaLootArtLayout.DollView;
            if (barterKit?.Doll is { } kitDoll) { Overlay(kitDoll, dollX, dollY); _kitDraw.KitDoll = true; }
            else Art(DollArt, dollX, dollY);
            foreach (var (slot, rect) in AreaLootArtLayout.BodySlotRects)
            {
                var entry = view.BodyDoll.FirstOrDefault(entry => entry.Slot == slot);
                if (slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && entry?.Entity is not null && view.BodyRatings is { WeaponTooHeavy: true })
                {
                    var (red, green, blue) = AreaLootArtLayout.TooHeavyFill;
                    DrawRect(Scaled(rect.X, rect.Y, rect.Width, rect.Height), new Color(red / 255f, green / 255f, blue / 255f));
                }
                if (entry?.Entity is { } worn && worn != Held?.Entity) Item(look.ItemArt(worn), rect, entry.Name ?? "?");
                else if (look.Art.GetValueOrDefault(AreaInventoryArtLayout.Covers[slot]) is { } cover) DrawTextureRect(cover, Scaled(rect.X, rect.Y, rect.Width, rect.Height), false);
            }
            if (view.BodyRatings is { } ratings)
            {
                // The damage rating ends at the right edge of its box, the armour rating starts at the left edge of its box (0x576AF7..0x576C38); ARC-UI-2795: the other party's rating is font 0x682C74 (art 229, white).
                Line(fonts?.Text, ratings.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaLootArtLayout.DamageRatingBox, AreaArtLabel.Alignment.Right);
                Line(fonts?.Text, ratings.Armour.ToString(System.Globalization.CultureInfo.InvariantCulture), AreaLootArtLayout.ArmourRatingBox, AreaArtLabel.Alignment.Left);
            }
        }
        foreach (var item in doll ? [] : view.LootGrid)
        {
            // The held item stays in its place but is not drawn there (item flag 0x10, as in the hero's grid).
            if (item.Entity == Held?.Entity) continue;
            if ((barter is not null ? AreaBarterArtLayout.ItemRect(item, _lootScroll) : AreaLootArtLayout.ItemRect(item, _lootScroll)) is null) continue;
            Item(look.ItemArt(item.Entity), barter is not null ? AreaBarterArtLayout.FullRect(item, _lootScroll) : AreaLootArtLayout.FullRect(item, _lootScroll),
                item.Name, shownTop, shownBottom);
        }
        // ARC-DEV-339: the arrange button every mode makes (IW30), the barter window's too.
        Art(AreaInventoryArtLayout.ArrangeArt, AreaInventoryArtLayout.ArrangeButton.X, AreaInventoryArtLayout.ArrangeButton.Y);
        // ARC-UI-1120 (BW6): the follower's cycle buttons, drawn with their first frame as the other art buttons are.
        if (barter is not null && CycleShown)
        {
            Art(AreaBarterArtLayout.CycleLeftArt, AreaBarterArtLayout.CycleLeft.X, AreaBarterArtLayout.CycleLeft.Y);
            Art(AreaBarterArtLayout.CycleRightArt, AreaBarterArtLayout.CycleRight.X, AreaBarterArtLayout.CycleRight.Y);
        }
        if (barter is null && AreaLootArtLayout.TakeAllShown(Mode)) Art(AreaLootArtLayout.TakeAllArt, AreaLootArtLayout.TakeAll.X, AreaLootArtLayout.TakeAll.Y);
        // ARC-UI-072: the doll and bag buttons (IW30), drawn with their first frame; the chosen view is the one on show.
        if (TogglesShown)
        {
            var (dollButton, bagButton) = barter is not null ? (AreaBarterArtLayout.DollButton, AreaBarterArtLayout.BagButton) : (AreaLootArtLayout.DollButton, AreaLootArtLayout.BagButton);
            Art(AreaLootArtLayout.DollButtonArt, dollButton.X, dollButton.Y);
            Art(AreaLootArtLayout.BagButtonArt, bagButton.X, bagButton.Y);
        }
        // ARC-UI-1918 (IW30): the hero's own doll and bag buttons at (713, 37) and (751, 37), the same arts, in the windows of modes 1 to 4.
        if (HeroTogglesShown)
        {
            Art(AreaLootArtLayout.DollButtonArt, AreaHeroDollToggle.DollButton.X, AreaHeroDollToggle.DollButton.Y);
            Art(AreaLootArtLayout.BagButtonArt, AreaHeroDollToggle.BagButton.X, AreaHeroDollToggle.BagButton.Y);
        }
        // ARC-UI-1924 (IW46): the hero's hot spots' art buttons at (374, 318) and (599, 319): made hidden by the open, shown with the hero's doll toggle.
        if (HeroDoll)
            foreach (var weapon in new[] { true, false })
            {
                var (bx, by) = AreaStatHotSpots.ArtButton(Mode, weapon);
                Art(weapon ? AreaStatHotSpots.WeaponArt : AreaStatHotSpots.ArmourArt, bx, by);
            }
        if (Held is { } held)
        {
            var (w, h, name) = held.Partner && view.LootGrid.FirstOrDefault(item => item.Entity == held.Entity) is { } loot ? (loot.Width, loot.Height, loot.Name)
                : view.Grid.FirstOrDefault(item => item.Entity == held.Entity) is { } own ? (own.Width, own.Height, own.Name)
                // ARC-UI-1918: the hero's worn item lifted from its doll takes its art's cells, as in the own window.
                : held.Worn is not null && view.Doll.FirstOrDefault(slot => slot.Entity == held.Entity) is { } heroWorn && look.ItemArt(held.Entity) is { } heroArt
                    ? (Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)heroArt.GetWidth(), (uint)heroArt.GetHeight())).Item1,
                       Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)heroArt.GetWidth(), (uint)heroArt.GetHeight())).Item2, heroWorn.Name ?? "?")
                : held.Partner && view.BodyDoll.FirstOrDefault(slot => slot.Entity == held.Entity) is { } wornSlot && look.ItemArt(held.Entity) is { } art
                    ? (Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)art.GetWidth(), (uint)art.GetHeight())).Item1,
                       Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)art.GetWidth(), (uint)art.GetHeight())).Item2, wornSlot.Name ?? "?")
                    : (1, 1, "?");
            var cell = AreaInventoryDrag.CellPixels;
            var origin = _pointer / _scale - new Vector2(held.Offset % 10 + .5f, held.Offset / 10 + .5f) * cell;
            Item(look.ItemArt(held.Entity), (origin.X, origin.Y, w * cell, h * cell), name);
        }
    }
}

/// <summary>ARC-UI-051: the inventory window's original look for the frame: the interface arts by number, an item's inventory art by
/// entity (null when it does not read) and the line texts.</summary>
public sealed record AreaInventoryArtLook(IReadOnlyDictionary<int, Texture2D> Art, Func<string, Texture2D?> ItemArt, AreaInventoryTexts Texts)
{
    /// <summary>ARC-UI-052: the loot window's own arts read (<see cref="AreaInventoryArtContent.LootArtReady"/>).</summary>
    public bool LootReady { get; init; }

    /// <summary>ARC-UI-064: the barter window's own arts read (<see cref="AreaInventoryArtContent.BarterArtReady"/>).</summary>
    public bool BarterReady { get; init; }

    /// <summary>ARC-UI-2795: the original's text fonts (art 27 for the merchant's name, 229 for the lines and quotes, 171 for the counts, 486 for the PC's ratings); null draws no window text.</summary>
    public AreaTextFonts? Fonts { get; init; }

    /// <summary>PROJECT: the colour of the diagnostic look's texts and of an item's name when its art did not read (the windows' own texts are white, in the original's fonts).</summary>
    public static readonly Color TextColour = new(.92f, .86f, .72f);
}
