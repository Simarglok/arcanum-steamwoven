using Arcanum.Session;
using Arcanum.Data.Combat;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-052: one item of the looted holder's grid as the loot window draws it: its entity, its inventory location (row × 10 +
/// column of the holder's grid), its size in cells, its name and its kind (for its inventory art).</summary>
public sealed record AreaLootGridItem(string Entity, int Location, int Width, int Height, string Name, SessionItemKind Kind);

/// <summary>ARC-UI-052: a point of the loot window: a cell of the hero's grid (as <see cref="AreaInventoryDrag"/>), a box, a location of the
/// looted holder's grid (scroll included), the Take all button, or none.</summary>
public readonly record struct AreaLootHit(int? Cell, AreaInventoryIcon? Icon, int? Location, bool TakeAll)
{
    public static AreaLootHit None => default;
}

/// <summary>
/// ARC-UI-052: the original look of the loot window (interface mode 2, a dead critter's or a container's inventory), read in the redraw
/// 0x576520, the open 0x5727B0, the hit test 0x575CB0 and the image choice 0x553D10 (ledger IW26–IW32). The frame is the inventory
/// window's 800 × 400 (<see cref="AreaInventoryArtLayout"/>): the background 0x5782D0 is art 234 <c>Loot</c> at (0, 0) and the pack art
/// 221 at (358, 0) as in mode 0; the holder's image at (183, 33); the holder's grid of 10 columns by 8 shown rows of 32 pixels from (8, 136),
/// scrolled by whole rows; the Take all button 808 at (301, 47); the hero's grid, counts and boxes as in mode 0, the hero's doll and weight
/// lines not drawn (the toggle 0x68150C is clear at every open). ARC-UI-065: the steal window (mode 3, a living NPC whose pocket the hero
/// picks) is the same frame without the Take all button (made in mode 2 alone, IW30); a drop across the two grids and a right release are
/// pick-pocket attempts (ledger IW35–IW36). Godot-free.
/// </summary>
public static class AreaLootArtLayout
{
    public const int LootArt = 234, TakeAllArt = 808, DropRefusedArt = 348, UseRefusedArt = 347;
    public const float GridX = 8, GridY = 136;
    public const int Columns = 10, ShownRows = 8, CellPixels = AreaInventoryDrag.CellPixels;
    public static readonly (int X, int Y) Image = (183, 33), TakeAll = (301, 47);
    /// <summary>The scroll bar's rectangle (0x5CAFA0; built by 0x579C40, not drawn by the viewer).</summary>
    public static readonly (int X, int Y, int Width, int Height) ScrollBar = (330, 136, 17, 256);
    /// <summary>The scroll's largest value in rows (0x681510): 4 for a critter, 88 for a container (type 2).</summary>
    public const int CritterScrollMax = 4, ContainerScrollMax = 88;
    /// <summary>The image of an object type 0x553D10 has no rule for (<c>LevelUpIcon</c>, not built: the viewer loots critters and
    /// containers only) and of a container of another description (<c>ContainerIcon</c>).</summary>
    public const int GeneralImage = 432, ContainerImage = 435;
    /// <summary>A critter's image by its race (base stat 27), table 0x5C6FA0 (12 entries).</summary>
    public static IReadOnlyList<int> RaceImages { get; } = [375, 378, 376, 377, 379, 380, 381, 382, 376, 382, 381, 634];
    /// <summary>A container's image by its description (its prototype file less 20, 0x49B290): junk pile, safes, trash, body, altars,
    /// plant (0x553D99, table 0x553F40); every other container <see cref="ContainerImage"/>.</summary>
    public static IReadOnlyDictionary<int, int> ContainerImages { get; } = new Dictionary<int, int>
    {
        [3023] = 832, [3033] = 834, [3034] = 834, [3055] = 835, [3056] = 833, [3057] = 837, [3058] = 838, [3059] = 839, [3060] = 836,
    };

    /// <summary>The arts the loot look needs besides the inventory window's (<see cref="AreaInventoryArtLayout.ArtNumbers"/>).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [LootArt, TakeAllArt, DropRefusedArt, UseRefusedArt];

    /// <summary>ARC-UI-072 (B30, IW29/IW30): the body's doll view: its background <c>Loot_PD</c> (349) at (0, 134), and the two toggle
    /// buttons the open makes when the shown object is not a container, <c>PD_lil_butt</c> (338, the doll) at (23, 108) and
    /// <c>Inven_lil_butt</c> (339, the bag) at (74, 108). The toggle (0x681514) starts clear at every open, so the grid shows first.</summary>
    public const int DollViewArt = 349, DollButtonArt = 338, BagButtonArt = 339;
    public static readonly (int X, int Y) DollView = (0, 134), DollButton = (23, 108), BagButton = (74, 108);

    /// <summary>ARC-UI-072: the doll view's arts. They are not part of <see cref="ArtNumbers"/>: a window whose doll arts do not read keeps the
    /// grid view and shows no toggles, instead of the diagnostic window.</summary>
    public static IReadOnlyList<int> DollArtNumbers { get; } = [DollViewArt, DollButtonArt, BagButtonArt];

    /// <summary>ARC-UI-072: the nine slot rectangles (x, y, width, height) of the body's doll (table 0x5CAE08, the hero's table 0x5CAC58 at two
    /// thirds of its size, shifted to the loot frame), in slot order; they hit-test and place the worn items and the covers of the empty slots.</summary>
    public static IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, (int X, int Y, int Width, int Height)> BodySlotRects { get; } =
        new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, (int, int, int, int)>
        {
            [Arcanum.Simulation.Equipment.EquipmentSlot.Helmet] = (157, 182, 42, 42), [Arcanum.Simulation.Equipment.EquipmentSlot.Ring1] = (220, 182, 21, 21),
            [Arcanum.Simulation.Equipment.EquipmentSlot.Ring2] = (241, 182, 21, 21), [Arcanum.Simulation.Equipment.EquipmentSlot.Medallion] = (220, 203, 42, 21),
            [Arcanum.Simulation.Equipment.EquipmentSlot.Weapon] = (73, 223, 63, 85), [Arcanum.Simulation.Equipment.EquipmentSlot.Shield] = (220, 224, 63, 85),
            [Arcanum.Simulation.Equipment.EquipmentSlot.Armor] = (136, 224, 84, 106), [Arcanum.Simulation.Equipment.EquipmentSlot.Gauntlets] = (94, 182, 42, 42),
            [Arcanum.Simulation.Equipment.EquipmentSlot.Boots] = (156, 330, 42, 42),
        };

    /// <summary>ARC-UI-072: the window makes the doll and bag buttons in the loot and steal windows when the shown object is not a container
    /// (0x572B03..0x572CC9, IW30); the barter window has its own (8, 123) and (46, 123), not built.</summary>
    public static bool TogglesShown(AreaInventoryMode mode, AreaInventoryView view) =>
        mode is AreaInventoryMode.Loot or AreaInventoryMode.Steal && view is { Holder: not null, HolderContainer: false };

    /// <summary>The toggle button under the point (sized by its art): true the doll button, false the bag button, null none.</summary>
    public static bool? ToggleHit(float x, float y, (int Width, int Height) doll, (int Width, int Height) bag)
    {
        if (x >= DollButton.X && x < DollButton.X + doll.Width && y >= DollButton.Y && y < DollButton.Y + doll.Height) return true;
        if (x >= BagButton.X && x < BagButton.X + bag.Width && y >= BagButton.Y && y < BagButton.Y + bag.Height) return false;
        return null;
    }

    /// <summary>Every image a looted holder can show.</summary>
    public static IReadOnlyList<int> ImageNumbers { get; } =
        RaceImages.Concat(ContainerImages.Values).Concat([383, 384, 385, 434, GeneralImage, ContainerImage]).Distinct().ToArray();

    /// <summary>
    /// The image 0x553D10 draws for a looted holder with the PC as observer: a critter (types 15, 16) by its critter flags (field 0xDA:
    /// 0x4 undead 384, 0x8000 monster 383, 0x8 animal 385, 0x20000000 mechanical 434, in this order), else by its race; a container by its
    /// description. NOT READY: a PC's or a follower's portrait (the observer's own followers show their portrait first), and the other
    /// object types. Null when the facts do not decide it (a race beyond the table, which the original reads past its end).
    /// </summary>
    public static int? HolderImage(int type, uint critterFlags, int race, int description) => type switch
    {
        CombatFieldReader.PcType or CombatFieldReader.NpcType => (critterFlags & 0x4) != 0 ? 384 : (critterFlags & 0x8000) != 0 ? 383
            : (critterFlags & 0x8) != 0 ? 385 : (critterFlags & 0x20000000) != 0 ? 434 : race >= 0 && race < RaceImages.Count ? RaceImages[race] : null,
        CombatFieldReader.ContainerType => ContainerImages.GetValueOrDefault(description, ContainerImage),
        _ => null,
    };

    /// <summary>ARC-UI-065: the Take all button is made, drawn and hit in the loot window (mode 2) alone (0x572815..0x572D85, IW30).</summary>
    public static bool TakeAllShown(AreaInventoryMode mode) => mode == AreaInventoryMode.Loot;

    /// <summary>The scroll's largest value for a holder.</summary>
    public static int ScrollMax(bool container) => container ? ContainerScrollMax : CritterScrollMax;

    /// <summary>A holder's grid location the window shows: below the worn locations (1000..1008); the window skips 1000..1008 in the grid
    /// view and 2000..2009 always (0x577C6C..0x577C9A).</summary>
    public static bool GridLocation(int location) => location is >= 0 and < 1000;

    /// <summary>The frame rectangle of a holder's item at <paramref name="scroll"/> (x = 8 + 32 × column, y = 136 + 32 × (row − scroll)),
    /// clipped to the shown rows (0x5780AA..0x57818E); null when no part of it is shown.</summary>
    public static (float X, float Y, float Width, float Height)? ItemRect(AreaLootGridItem item, int scroll)
    {
        ArgumentNullException.ThrowIfNull(item);
        var top = GridY + (item.Location / Columns - scroll) * CellPixels;
        var bottom = top + item.Height * CellPixels;
        var (first, last) = (Math.Max(top, GridY), Math.Min(bottom, GridY + ShownRows * CellPixels));
        if (last <= first) return null;
        return (GridX + item.Location % Columns * CellPixels, first, item.Width * CellPixels, last - first);
    }

    /// <summary>The unclipped rectangle of the item (its art is centred there before the clip).</summary>
    public static (float X, float Y, float Width, float Height) FullRect(AreaLootGridItem item, int scroll) =>
        (GridX + item.Location % Columns * CellPixels, GridY + (item.Location / Columns - scroll) * CellPixels, item.Width * CellPixels, item.Height * CellPixels);

    /// <summary>The point's place in the loot window: the hero's grid, the boxes (as mode 0), the holder's grid (x − 8, y − 136 in 32-pixel
    /// cells, 10 × 8, plus the scroll's rows: location = (row + scroll) × 10 + column, 0x575E35), or the Take all button.</summary>
    public static AreaLootHit HitTest(float x, float y, int scroll, (int Width, int Height) takeAll, bool doll = false)
    {
        if (x >= TakeAll.X && x < TakeAll.X + takeAll.Width && y >= TakeAll.Y && y < TakeAll.Y + takeAll.Height) return new(null, null, null, true);
        // ARC-UI-072 (IW29, 0x575DC3..0x575E27): in the doll view the holder's side hits only its nine slot rectangles, as the location
        // 1000 + slot (the hit test falls through to no item elsewhere); the grid is not there.
        if (doll && x < AreaInventoryArtLayout.PackX)
        {
            foreach (var (slot, rect) in BodySlotRects)
                if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height) return new(null, null, (int)slot, false);
            return AreaLootHit.None;
        }
        if (!doll && x >= GridX && y >= GridY && x < GridX + Columns * CellPixels && y < GridY + ShownRows * CellPixels)
            return new(null, null, ((int)((y - GridY) / CellPixels) + scroll) * Columns + (int)((x - GridX) / CellPixels), false);
        if (x < AreaInventoryArtLayout.PackX) return AreaLootHit.None;
        // The hero's own side: its grid and boxes; its doll is not shown in this mode, so no slot is hit.
        var own = AreaInventoryDrag.HitTest(x, y);
        return own.Slot is not null ? AreaLootHit.None : new(own.Cell, own.Icon, null, false);
    }

    /// <summary>
    /// The holder's grid as the window shows it: items at a grid location keep it; PROJECT: an item the session holds at no location (-1:
    /// a made item added to an NPC, an item an NPC took off) goes to the first free place of its size, row by row, as an insert finds one
    /// (0x466390 fit test over 10 columns; the original stores a location for every item it inserts); worn items (1000..1008) and anything
    /// else outside the grid are left out (IW28). Order is kept.
    /// </summary>
    public static IReadOnlyList<AreaLootGridItem> Arrange(IReadOnlyList<AreaLootGridItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var placed = items.Where(item => GridLocation(item.Location)).ToList();
        bool Fits(int location, int width, int height) => location % Columns + width <= Columns && placed.All(other =>
            location % Columns + width <= other.Location % Columns || other.Location % Columns + other.Width <= location % Columns ||
            location / Columns + height <= other.Location / Columns || other.Location / Columns + other.Height <= location / Columns);
        var result = new List<AreaLootGridItem>(items.Count);
        foreach (var item in items)
        {
            if (GridLocation(item.Location)) { result.Add(item); continue; }
            if (item.Location != -1) continue;
            var (width, height) = (Math.Clamp(item.Width, 1, Columns), Math.Max(1, item.Height));
            var free = Enumerable.Range(0, 1000 - (height - 1) * Columns).Cast<int?>().FirstOrDefault(location => Fits(location!.Value, width, height));
            if (free is not { } location) continue;
            var put = item with { Location = location, Width = width, Height = height };
            placed.Add(put); result.Add(put);
        }
        return result;
    }

    /// <summary>The holder's item that covers a location, or null.</summary>
    public static AreaLootGridItem? ItemAt(IReadOnlyList<AreaLootGridItem> grid, int location)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (row, column) = (location / Columns, location % Columns);
        return grid.FirstOrDefault(item => row >= item.Location / Columns && row < item.Location / Columns + item.Height &&
            column >= item.Location % Columns && column < item.Location % Columns + item.Width);
    }

    /// <summary>A left press with nothing held: the holder's item under the point goes on the cursor (marked as the holder's, with the grab's
    /// offset in its grid), or the hero's grid item as in mode 0; null elsewhere.</summary>
    public static AreaInventoryHeld? Press(AreaInventoryView view, AreaLootHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        // ARC-UI-072: a location of the worn slots (1000..1008) is the body's doll slot; the grid has none there.
        if (hit.Location is >= 1000 and <= 1008 and var slot)
            return view.BodyDoll.FirstOrDefault(entry => (int)entry.Slot == slot)?.Entity is { } worn ? new(worn, null, 0) { Partner = true } : null;
        if (hit.Location is { } location)
            return ItemAt(view.LootGrid, location) is { } item ? new(item.Entity, null, location - item.Location) { Partner = true } : null;
        return hit.Cell is { } cell && AreaInventoryDrag.ItemAt(view.Grid, cell) is { } own ? new(own.Entity, null, cell - own.Slot) : null;
    }

    /// <summary>A left release with an item held: the holder's item on the hero's grid is taken (<see cref="AreaInventoryDropKind.Take"/>;
    /// in the steal window an attempt to steal it, 0x574EB8 → 0x575180, IW35); on a box the refused art shows (IW23) and it goes back; the
    /// hero's item on its grid or a box as in mode 0. ARC-UI-065: in the steal window the hero's item released on the holder's grid is an
    /// attempt to plant it (<see cref="AreaInventoryDropKind.Plant"/>, 0x575100). NOT READY: in the loot window that release (the move into
    /// the holder, 0x575360) and either window's move of a holder's item within its own grid go back.</summary>
    public static AreaInventoryDrop Release(AreaInventoryHeld held, AreaLootHit hit, AreaInventoryMode mode)
    {
        ArgumentNullException.ThrowIfNull(held);
        if (held.Partner) return hit.Cell is { } target ? new(AreaInventoryDropKind.Take, held, GridSlot: target) : new(AreaInventoryDropKind.PutBack, held);
        // A doll slot (a location of 1000 and up, ARC-UI-072) is not a grid place of the victim: a hero's item released on it goes back.
        if (hit.Location is { } location and < 1000 && mode == AreaInventoryMode.Steal) return new(AreaInventoryDropKind.Plant, held, GridSlot: location);
        if (hit.Location is not null || hit.TakeAll) return new(AreaInventoryDropKind.PutBack, held);
        return AreaInventoryDrag.Release(held, new AreaInventoryHit(hit.Cell, null, hit.Icon));
    }

    /// <summary>
    /// ARC-UI-065: a right release with nothing held (0x574262..0x574430, IW36): in modes 1–3 the item under the point moves to the other
    /// side, the holder's to the hero (<see cref="AreaInventoryDropKind.Take"/>; in the steal window an attempt to steal it) and the hero's
    /// grid item to the holder (in the steal window an attempt to plant it, <see cref="AreaInventoryDropKind.Plant"/>). Null on no item and
    /// for the hero's item in the loot window (NOT READY: the move into the holder, 0x575360). The original first asks the receiving side
    /// for room (0x466510; a refusal prints its reason, 0x4673F0, and nothing else happens); PROJECT: the session's insert check of the move
    /// decides instead.
    /// </summary>
    public static AreaInventoryDrop? RightRelease(AreaInventoryView view, AreaLootHit hit, AreaInventoryMode mode)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (mode is not (AreaInventoryMode.Loot or AreaInventoryMode.Steal) || Press(view, hit) is not { } item) return null;
        if (item.Partner) return new(AreaInventoryDropKind.Take, item);
        return mode == AreaInventoryMode.Steal ? new(AreaInventoryDropKind.Plant, item) : null;
    }

    /// <summary>The art a box shows while <paramref name="held"/> is over it: the holder's own item the refused art (348, 347), the hero's
    /// item the "on" art (IW23); the "off" art with nothing held or the pointer elsewhere.</summary>
    public static int BoxArt(AreaInventoryIcon icon, AreaInventoryHeld? held, bool over) => held is { Partner: true } && over
        ? icon == AreaInventoryIcon.Drop ? DropRefusedArt : UseRefusedArt
        : AreaInventoryArtLayout.BoxArt(icon, held is not null && over);
}
