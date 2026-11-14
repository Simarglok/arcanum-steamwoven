using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-UI-064: a point of the barter window: a cell of the hero's grid (as <see cref="AreaInventoryDrag"/>), the drop or use box,
/// the gamble box, a location of the shown object's grid (scroll included), or none. ARC-UI-1918: or a slot of the hero's own doll, with its toggle set
/// (<see cref="AreaHeroDollToggle"/>).</summary>
public readonly record struct AreaBarterHit(int? Cell, AreaInventoryIcon? Icon, int? Location, bool Gamble, Arcanum.Simulation.Equipment.EquipmentSlot? Slot = null)
{
    public static AreaBarterHit None => default;
}

/// <summary>ARC-UI-064: the barter window's parts besides the frame it shares with the inventory window: its background art
/// (<see cref="AreaBarterArtLayout.Background"/>), the merchant's name and the quote line's text.</summary>
public sealed record AreaBarterFrame(int Background, string Name, string Quote);

/// <summary>
/// ARC-UI-064: the original look of the barter window (interface mode 1), read in the open 0x5727B0, the redraw 0x576520, the background
/// 0x5782D0, the hit test 0x575CB0, the move handler 0x573D74 and the hover quote 0x578330 (ledger BW1–BW5). The frame is the inventory
/// window's 800 × 400 (<see cref="AreaInventoryArtLayout"/>): the background is art 235 <c>Barter</c>, or 825 <c>Barter_Follower</c> while
/// the follower's cycle buttons exist (a follower's window when the PC leads another follower too); the pack art 221 at (358, 0); the
/// merchant's image at (183, 33) as the loot window's (<see cref="AreaLootArtLayout.HolderImage"/>), its name centred on x 215 at y 102,
/// the quote line in (89, 128, 253 × 30); the shown object's grid (the merchant's substitute container when it has one) of 10 columns by
/// 7 shown rows of 32 pixels from (8, 168), scrolled by whole rows; the hero's grid, counts, drop and use boxes as in mode 0 and the gamble
/// box 345 at (703, 236). The hero's doll, its weight lines and the merchant's doll view (337 <c>Barter_PD</c> at (0, 157)) are not drawn:
/// the open clears both doll toggles (IW26, IW29). Godot-free.
/// </summary>
public static class AreaBarterArtLayout
{
    public const int BarterArt = 235, FollowerArt = 825, GambleOffArt = 345, GambleOnArt = 344, GambleRefusedArt = 346;
    public const float GridX = 8, GridY = 168;
    public const int Columns = 10, ShownRows = 7, CellPixels = AreaInventoryDrag.CellPixels;
    /// <summary>The gamble box's corner (0x681108, 0x68110C: 703, 277 on the screen, 41 above the frame).</summary>
    public static readonly (int X, int Y) GambleBox = (703, 236);
    /// <summary>The merchant's name: centred on <see cref="NameCentreX"/>, in the rectangle's line (font 0x682418).</summary>
    public const int NameCentreX = 215;
    public static readonly (int X, int Y, int Width, int Height) NameLine = (115, 102, 200, 20);
    /// <summary>The quote line (0x68241C, font 0x682C74; a follower's window prints the follower's weight lines there).</summary>
    public static readonly (int X, int Y, int Width, int Height) QuoteLine = (89, 128, 253, 30);
    /// <summary>The scroll bar's rectangle (0x579C40 with mode 1; not drawn by the viewer).</summary>
    public static readonly (int X, int Y, int Width, int Height) ScrollBar = (330, 168, 17, 224);
    /// <summary>The scroll's largest value in rows (0x681510): 5 for the merchant, 89 for its substitute container (0x573038..0x573071).</summary>
    public const int MerchantScrollMax = 5, ContainerScrollMax = 89;
    /// <summary>The follower's cycle buttons 757 <c>MPCycleLeftButton</c> and 758 <c>MPCycleRightButton</c> at (155, 55) and (253, 55)
    /// (0x573147..0x5731B5). ARC-UI-1120 (BW6): built, drawn with their first frame as the other art buttons are; a release over one
    /// switches the window to the previous (left) or next (right) follower (<see cref="AreaBarterCycle"/>).</summary>
    public static readonly (int X, int Y) CycleLeft = (155, 55), CycleRight = (253, 55);
    public const int CycleLeftArt = 757, CycleRightArt = 758;

    /// <summary>Whether the cycle buttons exist: they are made exactly when the window draws the follower background (BW1).</summary>
    public static bool CycleShown(int background) => background == FollowerArt;

    /// <summary>The cycle button a frame point is on, sized by its art: 0 the left one (the previous follower, 0x574FD0 with 0), 1 the right one
    /// (the next follower), null none.</summary>
    public static int? CycleHit(float x, float y, (int Width, int Height) left, (int Width, int Height) right) =>
        Inside(x, y, CycleLeft, left) ? 0 : Inside(x, y, CycleRight, right) ? 1 : null;

    private static bool Inside(float x, float y, (int X, int Y) corner, (int Width, int Height) art) =>
        art.Width > 0 && art.Height > 0 && x >= corner.X && x < corner.X + art.Width && y >= corner.Y && y < corner.Y + art.Height;

    /// <summary>The arts the barter look needs besides the inventory window's (<see cref="AreaInventoryArtLayout.ArtNumbers"/>): the two
    /// backgrounds, the gamble box's three states and the refused box arts it shares with the loot window.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [BarterArt, FollowerArt, GambleOffArt, GambleOnArt, GambleRefusedArt, AreaLootArtLayout.DropRefusedArt, AreaLootArtLayout.UseRefusedArt];

    /// <summary>The background 0x5782D0 draws in mode 1: 825 when the follower's cycle buttons exist (0x681100 set: the partner's leader is
    /// the PC and its leader's next follower, 0x45E590, is another critter), else 235.</summary>
    public static int Background(bool follower, int pcFollowers) => follower && pcFollowers > 1 ? FollowerArt : BarterArt;

    /// <summary>The scroll's largest value: the merchant's or its substitute container's.</summary>
    public static int ScrollMax(bool container) => container ? ContainerScrollMax : MerchantScrollMax;

    /// <summary>The frame rectangle of a shown item at <paramref name="scroll"/> (x = 8 + 32 × column, y = 168 + 32 × (row − scroll)), clipped
    /// to the shown rows (0x5780AA..0x57818E with mode 1's band 168..392); null when no part of it is shown.</summary>
    public static (float X, float Y, float Width, float Height)? ItemRect(AreaLootGridItem item, int scroll)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (x, top, width, height) = FullRect(item, scroll);
        var (first, last) = (Math.Max(top, GridY), Math.Min(top + height, GridY + ShownRows * CellPixels));
        return last <= first ? null : (x, first, width, last - first);
    }

    /// <summary>The unclipped rectangle of the item (its art is centred there before the clip).</summary>
    public static (float X, float Y, float Width, float Height) FullRect(AreaLootGridItem item, int scroll) =>
        (GridX + item.Location % Columns * CellPixels, GridY + (item.Location / Columns - scroll) * CellPixels, item.Width * CellPixels, item.Height * CellPixels);

    /// <summary>The point's place: the shown object's grid (x − 8, y − 168 in 32-pixel cells, 10 × 7, plus the scroll's rows: location =
    /// (row + scroll) × 10 + column, 0x575D56), the gamble box (its art's size, 0x681110, 0x681114), else the hero's grid and boxes as in mode 0
    /// (no doll: no slot).</summary>
    public static AreaBarterHit HitTest(float x, float y, int scroll, (int Width, int Height) gamble, bool heroDoll = false)
    {
        if (x >= GridX && y >= GridY && x < GridX + Columns * CellPixels && y < GridY + ShownRows * CellPixels)
            return new(null, null, ((int)((y - GridY) / CellPixels) + scroll) * Columns + (int)((x - GridX) / CellPixels), false);
        if (x >= GambleBox.X && x < GambleBox.X + gamble.Width && y >= GambleBox.Y && y < GambleBox.Y + gamble.Height) return new(null, null, null, true);
        if (x < AreaInventoryArtLayout.PackX) return AreaBarterHit.None;
        // ARC-UI-1918 (0x575CB0): with the hero's toggle set its doll's slots are hit and no grid cell.
        if (heroDoll)
        {
            var doll = AreaHeroDollToggle.HitTest(x, y);
            return doll.Slot is not null || doll.Icon is not null ? new(null, doll.Icon, null, false, doll.Slot) : AreaBarterHit.None;
        }
        var own = AreaInventoryDrag.HitTest(x, y);
        return own.Slot is not null ? AreaBarterHit.None : new(own.Cell, own.Icon, null, false);
    }

    /// <summary>A left press with nothing held: the shown object's item under the point goes on the cursor (marked as the partner's, with the
    /// grab's offset in its grid), or the hero's grid item; null elsewhere (the gamble box too).</summary>
    public static AreaInventoryHeld? Press(AreaInventoryView view, AreaBarterHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        // ARC-UI-1918: a slot of the hero's own doll (its toggle set) lifts the item worn there.
        if (hit.Slot is { } heroSlot)
            return view.Doll.FirstOrDefault(entry => entry.Slot == heroSlot)?.Entity is { } worn ? new(worn, heroSlot, 0) : null;
        if (hit.Location is { } location)
            return AreaLootArtLayout.ItemAt(view.LootGrid, location) is { } item ? new(item.Entity, null, location - item.Location) { Partner = true } : null;
        return hit.Cell is { } cell && AreaInventoryDrag.ItemAt(view.Grid, cell) is { } own ? new(own.Entity, null, cell - own.Slot) : null;
    }

    /// <summary>
    /// A left release with an item held (BT5): the merchant's item on the hero's grid is a buy (0x575200), the hero's item on the shown grid a
    /// sale (0x575360); ARC-UI-1740: the merchant's item on the bet box is a bet (0x579B60: the original puts the item back first, then runs the
    /// bet; the hero's own item there only goes back, as the gate refuses it silently); anything else puts it back. NOT READY: the hero's grid
    /// moves, drop and use boxes of the barter window (the item goes back; the own window has them).
    /// </summary>
    public static AreaInventoryDrop Release(AreaInventoryHeld held, AreaBarterHit hit)
    {
        ArgumentNullException.ThrowIfNull(held);
        if (hit.Gamble) return held.Partner ? new(AreaInventoryDropKind.Gamble, held) : new(AreaInventoryDropKind.PutBack, held);
        if (held.Partner) return hit.Cell is { } target ? new(AreaInventoryDropKind.Buy, held, GridSlot: target) : new(AreaInventoryDropKind.PutBack, held);
        return hit.Location is { } location ? new(AreaInventoryDropKind.Sell, held, GridSlot: location) : new(AreaInventoryDropKind.PutBack, held);
    }

    /// <summary>A right release with nothing held (0x574262..0x574430, IW36, read by ARC-UI-065): in mode 1 the item under the point moves to
    /// the other side, the merchant's to the hero (a buy) and the hero's grid item to the shown object (a sale), through the same moves as a
    /// drag (0x575200, 0x575360); null on no item. The receiving side's room test (0x466510) is the session's insert check.</summary>
    public static AreaInventoryDrop? RightRelease(AreaInventoryView view, AreaBarterHit hit) => Press(view, hit) is not { } item ? null
        : new(item.Partner ? AreaInventoryDropKind.Buy : AreaInventoryDropKind.Sell, item);

    /// <summary>The art the drop and use boxes show while <paramref name="held"/> is over them: the merchant's item the refused art, the
    /// hero's item the "on" art (IW23, as the loot window's).</summary>
    public static int BoxArt(AreaInventoryIcon icon, AreaInventoryHeld? held, bool over) =>
        AreaLootArtLayout.BoxArt(icon, held, over);

    /// <summary>The gamble box's resting art (0x573E67..0x573F1C): "off" 345 with nothing held over it; the box's size is this art's.</summary>
    public static int GambleArt => GambleOffArt;

    /// <summary>
    /// ARC-UI-1740 (BW3): the box's art now. An item held over it shows 344 when the bet gate 0x579840 passes (<paramref name="gatePasses"/>) and 346
    /// when it does not; the gate runs once as the item enters the box and moving out sets 345 again, as does a release.
    /// </summary>
    public static int GambleBoxArt(bool heldOver, bool gatePasses) => !heldOver ? GambleOffArt : gatePasses ? GambleOnArt : GambleRefusedArt;

    /// <summary>PROJECT: the quote line's text for a shown or hero item (the original's hover quote 0x578330 prints a generated phrase of the
    /// merchant's with the price; the generated lines are not built): the item, its unit price, or why it has none; a follower's window has
    /// no quote (0x578330 returns at once there).</summary>
    public static string Quote(AreaBarterLine line, bool sale, bool follower)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (follower) return string.Empty;
        var verb = sale ? "Sell" : "Buy";
        return line.Quote.Price > 0
            ? $"{verb} {line.Text}: {line.Quote.Price}{(line.Count > 1 ? " each" : string.Empty)}"
            : $"{verb} {line.Text}: no price ({line.Quote.Refusal})";
    }

    /// <summary>The item's quote in the window's view: a hero item's sale, a shown item's buy; empty for an item that is neither.</summary>
    public static string Quote(AreaBarterView view, string? item)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (item is null) return string.Empty;
        if (view.Hero.FirstOrDefault(line => line.Entity == item) is { } sold) return Quote(sold, sale: true, view.Merchant.Follower);
        return view.Wares.FirstOrDefault(line => line.Entity == item) is { } bought ? Quote(bought, sale: false, view.Merchant.Follower) : string.Empty;
    }
}

/// <summary>
/// ARC-UI-1120 (BW6): which follower the barter window switches to when a cycle button is released, read in the executable's handler 0x574FD0 (the
/// buttons 0x681100 and 0x681398 call it with 0 and 1) and the follower steppers 0x45E4F0 and 0x45E590. Godot-free.
/// </summary>
public static class AreaBarterCycle
{
    /// <summary>The follower the window shows next from <paramref name="current"/>, or null when it stays. The leader's follower list is stepped
    /// backwards (<paramref name="next"/> false) or forwards, wrapping at either end, until a follower <paramref name="acceptable"/> is met
    /// (0x575080); the walk ends at the current follower, which the original always accepts (its window is open), so a full turn with no other follower
    /// acceptable changes nothing, as here. A current follower not in the list does not move the window.</summary>
    public static string? Step(IReadOnlyList<string> followers, string current, bool next, Func<string, bool> acceptable)
    {
        ArgumentNullException.ThrowIfNull(followers);
        ArgumentNullException.ThrowIfNull(acceptable);
        var at = -1;
        for (var i = 0; i < followers.Count; i++) if (followers[i] == current) { at = i; break; }
        if (at < 0) return null;
        for (var step = 1; step < followers.Count; step++)
        {
            var candidate = followers[((at + (next ? step : -step)) % followers.Count + followers.Count) % followers.Count];
            if (acceptable(candidate)) return candidate;
        }
        return null;
    }
}
