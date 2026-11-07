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
    private Vector2 _pointer;
    public event Action<AreaInventoryDrop>? DropRequested;
    /// <summary>ARC-UI-025: a right release with nothing held on an item of the doll or the grid (ARC-UI-032: the release, IW5).</summary>
    public event Action<AreaInventoryHeld>? UseRequested;
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
    // ARC-UI-072 (B30): the other party's doll view is on (the original's toggle 0x681514; clear at every open) and the toggle button a
    // press is waiting on (true the doll button, false the bag button).
    private bool _bodyDoll;
    private bool? _toggleDown;
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

    /// <summary>ARC-UI-072: the frame shows the body's doll toggles (the doll arts read, and the shown object is not a container).</summary>
    internal bool TogglesShown => LootLook && Look!.Art.ContainsKey(AreaLootArtLayout.DollViewArt) && Look.Art.ContainsKey(AreaLootArtLayout.DollButtonArt) &&
        Look.Art.ContainsKey(AreaLootArtLayout.BagButtonArt) && AreaLootArtLayout.TogglesShown(Mode, _view!);

    /// <summary>ARC-UI-072: the body's doll view is on (the toggles shown and the doll button chosen).</summary>
    internal bool BodyDoll => _bodyDoll && TogglesShown;

    /// <summary>ARC-UI-064: the frame is the barter window's original look (the barter panel's, its art set and a shown object).</summary>
    public bool BarterLook => _barter is not null && Look is { BarterReady: true } && _view is { Holder: not null };

    /// <summary>ARC-UI-064: the barter window's own parts (null outside the barter panel).</summary>
    internal AreaBarterFrame? BarterFrame => _barter;
    private AreaBarterFrame? _barter;

    /// <summary>ARC-UI-060: the window's interface mode (IW26): the own window, or a holder's loot or steal window; ARC-UI-064: the barter
    /// window in the barter panel.</summary>
    internal AreaInventoryMode Mode => _barter is not null ? AreaInventoryMode.Barter
        : _view is { Holder: not null } view ? view.Steal ? AreaInventoryMode.Steal : AreaInventoryMode.Loot : AreaInventoryMode.Own;

    /// <summary>ARC-UI-060: the mode the original look draws: the loot or barter frame's while it is drawn, else the own window's.</summary>
    internal AreaInventoryMode LookMode => LootLook || BarterLook ? Mode : AreaInventoryMode.Own;

    /// <summary>ARC-UI-064: the grid draws the barter window's frame with <paramref name="frame"/>'s parts (null: the inventory window's).</summary>
    public void SetBarter(AreaBarterFrame? frame)
    {
        if (frame == _barter) return;
        _barter = frame; QueueRedraw();
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

    /// <summary>ARC-UI-051: sets the original look (null: the diagnostic shapes).</summary>
    public void SetLook(AreaInventoryArtLook? look)
    {
        if (ReferenceEquals(look, Look)) return;
        Look = look; _takeAllDown = false; QueueRedraw();
    }

    public void Render(AreaInventoryView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        // ARC-UI-052: another holder (or none) starts the holder's grid at its top (the open sets the scroll to 0, 0x5731FA).
        if (view.Holder != _lootHolder) { _lootHolder = view.Holder; _lootScroll = 0; _takeAllDown = false; _bodyDoll = false; _toggleDown = null; }
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
    internal void ResetLoot() { _lootHolder = null; _lootScroll = 0; _takeAllDown = false; _bodyDoll = false; _toggleDown = null; }

    /// <summary>ARC-UI-072: the doll or bag button was pressed and released over it (0x573986 sets the toggle, 0x57393B clears it).</summary>
    internal void SetBodyDoll(bool doll) { if (_bodyDoll == doll) return; _bodyDoll = doll; QueueRedraw(); }

    /// <summary>ARC-UI-072: the toggle button under <paramref name="frame"/>, or null (sized by its art).</summary>
    internal bool? ToggleAt(Vector2 frame)
    {
        if (!TogglesShown) return null;
        var (doll, bag) = (Look!.Art[AreaLootArtLayout.DollButtonArt], Look.Art[AreaLootArtLayout.BagButtonArt]);
        return AreaLootArtLayout.ToggleHit(frame.X, frame.Y, (doll.GetWidth(), doll.GetHeight()), (bag.GetWidth(), bag.GetHeight()));
    }

    /// <summary>A left press at a point of the frame (the self-test drives it too).</summary>
    internal void PressAt(Vector2 frame)
    {
        if (Held is not null || _view is null) return;
        if (BarterLook) Held = AreaBarterArtLayout.Press(_view, BarterHit(frame));
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
        return AreaLootArtLayout.HitTest(frame.X, frame.Y, _lootScroll, button is null ? (0, 0) : (button.GetWidth(), button.GetHeight()), BodyDoll);
    }

    /// <summary>ARC-UI-064: the barter window's point under <paramref name="frame"/> with the current scroll.</summary>
    private AreaBarterHit BarterHit(Vector2 frame)
    {
        var box = Look?.Art.GetValueOrDefault(AreaBarterArtLayout.GambleArt);
        return AreaBarterArtLayout.HitTest(frame.X, frame.Y, _lootScroll, box is null ? (0, 0) : (box.GetWidth(), box.GetHeight()));
    }

    /// <summary>ARC-UI-052: scrolls the holder's grid by <paramref name="rows"/> within 0..the holder's maximum.</summary>
    internal void ScrollLoot(int rows)
    {
        if (_view is null || _bodyDoll) return; // the grid is hidden in the doll view: its scroll stays
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

    /// <summary>A left release at a point of the frame: the held item's drop, then the cursor is empty.</summary>
    internal void ReleaseAt(Vector2 frame)
    {
        if (!LootLook) { _toggleDown = null; _takeAllDown = false; }
        if (LootLook && _toggleDown is { } toggle)
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
        var drop = BarterLook ? AreaBarterArtLayout.Release(held, BarterHit(frame)) : LootLook ? AreaLootArtLayout.Release(held, LootHit(frame), Mode)
            : AreaInventoryDrag.Release(held, AreaInventoryDrag.HitTest(frame.X, frame.Y));
        QueueRedraw();
        // The app re-renders the window, which may put an occupant on the cursor: after this input event returns.
        Callable.From(() => DropRequested?.Invoke(drop)).CallDeferred();
    }

    /// <summary>The frame point of a grid cell's centre or of a wear slot's centre (for the self-test).</summary>
    internal static Vector2 CellCentre(int cell) => new(AreaInventoryDrag.GridX + (cell % 10 + .5f) * AreaInventoryDrag.CellPixels,
        AreaInventoryDrag.GridY + (cell / 10 + .5f) * AreaInventoryDrag.CellPixels);

    internal static Vector2 SlotCentre(EquipmentSlot slot)
    {
        var rect = AreaInventoryDrag.SlotRects[slot];
        return new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    public override void _Draw()
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
        Background(look, AreaInventoryArtLayout.DollArt, AreaInventoryMode.Own);
        Art(AreaInventoryArtLayout.PackArt, AreaInventoryArtLayout.PackX, 0);
        // ARC-UI-054: the view window's art over the round view (0x551080 draws it after the copy, IW33).
        var (holeX, holeY) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
        Art(AreaInventoryHole.Art(AreaInventoryMode.Own), holeX, holeY);
        var font = GetThemeDefaultFont();
        var size = Math.Max(6, (int)Math.Round(12 * _scale));
        void Text((int X, int Y, int Width, int Height) rect, string text)
        {
            if (text.Length == 0) return;
            var box = Scaled(rect.X, rect.Y, rect.Width, rect.Height);
            DrawString(font, new(box.Position.X, box.Position.Y + (box.Size.Y + size) / 2 - 2), text, HorizontalAlignment.Left, box.Size.X, size, AreaInventoryArtLook.TextColour);
        }
        if (_view is null) return;
        var lines = look.Texts.Lines(_view.Carry, _view.Speed);
        for (var i = 0; i < lines.Count; i++) Text(AreaInventoryArtLayout.Lines[i], lines[i]);
        var counts = new[] { _view.Gold }.Concat(Enumerable.Range(0, 4).Select(type => type < _view.Ammo.Count ? _view.Ammo[type] : 0)).ToArray();
        for (var i = 0; i < counts.Length; i++) Text(AreaInventoryArtLayout.Counts[i], counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        var over = Held is null ? null : AreaInventoryDrag.HitTest(_pointer.X / _scale, _pointer.Y / _scale).Icon;
        foreach (var icon in new[] { AreaInventoryIcon.Use, AreaInventoryIcon.Drop })
        {
            var (x, y) = AreaInventoryArtLayout.Box(icon);
            Art(AreaInventoryArtLayout.BoxArt(icon, over == icon), x, y);
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
        var view = _view!;
        var barter = BarterLook ? _barter : null;
        Rect2 Scaled(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);
        void Art(int number, float x, float y)
        {
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
        Background(look, barter?.Background ?? AreaLootArtLayout.LootArt, LookMode);
        var (holeX, holeY) = AreaInventoryHole.Corner(LookMode);
        if (!_menuUp) Art(AreaInventoryHole.Art(LookMode), holeX, holeY);
        if (view.HolderImage is { } image) Art(image, AreaLootArtLayout.Image.X, AreaLootArtLayout.Image.Y);
        var font = GetThemeDefaultFont();
        var size = Math.Max(6, (int)Math.Round(12 * _scale));
        if (barter is not null)
        {
            // The merchant's name centred on x 215 (0x5766A4..0x57671A) and the quote line below it (0x57687F); PROJECT: the project's font.
            var (nx, ny, nw, nh) = AreaBarterArtLayout.NameLine;
            // A name wider than its 200-pixel line is cut with an ellipsis, so it stays centred inside the line.
            var name = barter.Name;
            while (name.Length > 1 && font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X > nw * _scale) name = name[..^2] + "…";
            var width = font.GetStringSize(name, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, new(Math.Max(nx * _scale, AreaBarterArtLayout.NameCentreX * _scale - width / 2), ny * _scale + (nh * _scale + size) / 2 - 2), name,
                HorizontalAlignment.Left, nw * _scale, size, AreaInventoryArtLook.TextColour);
            var (qx, qy, qw, qh) = AreaBarterArtLayout.QuoteLine;
            if (barter.Quote.Length > 0)
                DrawMultilineString(font, new(qx * _scale, qy * _scale + size), barter.Quote, HorizontalAlignment.Left, qw * _scale, size, 2,
                    AreaInventoryArtLook.TextColour);
        }
        Art(AreaInventoryArtLayout.PackArt, AreaInventoryArtLayout.PackX, 0);
        var counts = new[] { view.Gold }.Concat(Enumerable.Range(0, 4).Select(type => type < view.Ammo.Count ? view.Ammo[type] : 0)).ToArray();
        for (var i = 0; i < counts.Length; i++)
        {
            var (cx, cy, cw, ch) = AreaInventoryArtLayout.Counts[i];
            var box = Scaled(cx, cy, cw, ch);
            DrawString(font, new(box.Position.X, box.Position.Y + (box.Size.Y + size) / 2 - 2), counts[i].ToString(System.Globalization.CultureInfo.InvariantCulture),
                HorizontalAlignment.Left, box.Size.X, size, AreaInventoryArtLook.TextColour);
        }
        var over = Held is null ? null : barter is not null ? BarterHit(_pointer / _scale).Icon : LootHit(_pointer / _scale).Icon;
        foreach (var icon in new[] { AreaInventoryIcon.Use, AreaInventoryIcon.Drop })
        {
            var (x, y) = AreaInventoryArtLayout.Box(icon);
            Art(barter is not null ? AreaBarterArtLayout.BoxArt(icon, Held, over == icon) : AreaLootArtLayout.BoxArt(icon, Held, over == icon), x, y);
        }
        if (barter is not null) Art(AreaBarterArtLayout.GambleArt, AreaBarterArtLayout.GambleBox.X, AreaBarterArtLayout.GambleBox.Y);
        foreach (var item in view.Grid)
        {
            if (item.Entity == Held?.Entity) continue;
            Item(look.ItemArt(item.Entity), (AreaInventoryDrag.GridX + item.Slot % 10 * AreaInventoryDrag.CellPixels,
                AreaInventoryDrag.GridY + item.Slot / 10 * AreaInventoryDrag.CellPixels, item.Width * AreaInventoryDrag.CellPixels, item.Height * AreaInventoryDrag.CellPixels), item.Name);
        }
        var (shownTop, shownBottom) = barter is not null
            ? (AreaBarterArtLayout.GridY, AreaBarterArtLayout.GridY + AreaBarterArtLayout.ShownRows * AreaBarterArtLayout.CellPixels)
            : (AreaLootArtLayout.GridY, AreaLootArtLayout.GridY + AreaLootArtLayout.ShownRows * AreaLootArtLayout.CellPixels);
        // ARC-UI-072 (B30, IW29): the body's doll view replaces the holder's grid: art 349 at (0, 134), each worn item in its slot rectangle (PROJECT:
        // its inventory art, as the hero's doll draws it; the original draws the paper-doll art at two thirds of its size), and a cover stretched to
        // the rectangle of every empty slot or of the slot whose item is on the cursor. NOT READY: the two numbers (0x464630, 0x464700) and the
        // weapon slot's strength overlay (0x465E90).
        var doll = BodyDoll && barter is null;
        if (doll)
        {
            Art(AreaLootArtLayout.DollViewArt, AreaLootArtLayout.DollView.X, AreaLootArtLayout.DollView.Y);
            foreach (var (slot, rect) in AreaLootArtLayout.BodySlotRects)
            {
                var entry = view.BodyDoll.FirstOrDefault(entry => entry.Slot == slot);
                if (entry?.Entity is { } worn && worn != Held?.Entity) Item(look.ItemArt(worn), rect, entry.Name ?? "?");
                else if (look.Art.GetValueOrDefault(AreaInventoryArtLayout.Covers[slot]) is { } cover) DrawTextureRect(cover, Scaled(rect.X, rect.Y, rect.Width, rect.Height), false);
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
        if (barter is null && AreaLootArtLayout.TakeAllShown(Mode)) Art(AreaLootArtLayout.TakeAllArt, AreaLootArtLayout.TakeAll.X, AreaLootArtLayout.TakeAll.Y);
        // ARC-UI-072: the doll and bag buttons (IW30), drawn with their first frame; the chosen view is the one on show.
        if (barter is null && TogglesShown)
        {
            Art(AreaLootArtLayout.DollButtonArt, AreaLootArtLayout.DollButton.X, AreaLootArtLayout.DollButton.Y);
            Art(AreaLootArtLayout.BagButtonArt, AreaLootArtLayout.BagButton.X, AreaLootArtLayout.BagButton.Y);
        }
        if (Held is { } held)
        {
            var (w, h, name) = held.Partner && view.LootGrid.FirstOrDefault(item => item.Entity == held.Entity) is { } loot ? (loot.Width, loot.Height, loot.Name)
                : view.Grid.FirstOrDefault(item => item.Entity == held.Entity) is { } own ? (own.Width, own.Height, own.Name)
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

    /// <summary>PROJECT: the lines' colour; the original's fonts (0x682C74 for the lines, 0x68345C for the counts) are not read.</summary>
    public static readonly Color TextColour = new(.92f, .86f, .72f);
}
