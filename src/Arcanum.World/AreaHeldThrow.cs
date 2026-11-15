using Arcanum.Session;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-063: the item the inventory lifts onto the game cursor and its throw from the game view (ledger IW18, TT1, TT2, TT6, TT7):
/// the lift's refusals, what a press in the game view does with the held item, and the messages. Godot-free.
/// </summary>
public static class AreaHeldThrow
{
    /// <summary>
    /// The refusal of the lift onto the game cursor, in the order of <see cref="GameSessionOwner.ThrowHeroItem"/>'s own checks, or
    /// <see cref="GameSessionFailure.None"/>: an item whose throw script (point 6) comes with an insert-item script (the give-back of a refusing throw would run it, NOT READY, <see cref="GameSessionFailure.ItemScriptUnsupported"/>), and the no-drop flag
    /// (the original's remove check 0x466DA0 refuses the lift and the item stays, <see cref="GameSessionFailure.ItemNotDroppable"/>). ARC-COMBAT-1955: the removal's wield-off and remove-item scripts and the item's throw script run at the throw
    /// (<see cref="SessionHeroThrow.Scripts"/>), so the lift no longer refuses them. ARC-UI-086: an ammunition stack takes the
    /// original's stack path instead (QW1, QW5: the units are split off after a quantity question; the stack path runs no remove check and
    /// no remove-item event), so only its throw script refuses it (NOT READY); ARC-UI-094: so does the hero's gold pile (its coins are split off the same way, HG1). The session checks again at the throw.
    /// </summary>
    public static GameSessionFailure Lift(SessionGridMoveItem facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var stack = facts.Kind.Type is SessionItemType.Ammo or SessionItemType.Gold;
        var throws = facts.ScriptPoints.Contains(SessionScriptPoint.Throw);
        if (throws && (stack || facts.ScriptPoints.Contains(SessionScriptPoint.InsertItem))) return GameSessionFailure.ItemScriptUnsupported;
        return !stack && facts.NoDrop ? GameSessionFailure.ItemNotDroppable : GameSessionFailure.None;
    }

    /// <summary>The status line of a refused lift or throw of <paramref name="name"/>; the item stays where it was.</summary>
    public static string Refusal(GameSessionFailure failure, string name) => failure switch
    {
        GameSessionFailure.AttackUnsupported => $"not thrown: {name} needs a count to throw (the quantity question gave none)",
        GameSessionFailure.GoldShort => $"not thrown: the hero holds fewer coins than asked",
        GameSessionFailure.ItemScriptUnsupported => $"not thrown: {name}'s scripts do not run here (a stack's throw script, or a throw script with an insert-item script, is NOT READY)",
        GameSessionFailure.AmmoShort => $"not thrown: {name} has fewer units than asked",
        GameSessionFailure.InvalidAmmo or GameSessionFailure.BudgetExceeded => $"not thrown: the stack of {name} cannot be split ({failure})",
        GameSessionFailure.ItemNotDroppable => $"not thrown: {name} cannot be dropped",
        GameSessionFailure.ItemEffectUnsupported => $"not thrown: {name}'s item spells need the shown map's spell data",
        GameSessionFailure.WeaponInFlight => $"not thrown: {name} is still in flight",
        _ => $"not thrown: {name} ({failure})",
    };
}

/// <summary>
/// ARC-UI-063: the cursor while an item is held (ledger CU7): the lift and the refresh (0x573740, 0x5736E0) set the cursor art to the
/// held item's inventory art (field 0x5D), lay the cursor art shown before it over that art (0x500160 with (2, 2)) and put the hot point
/// at (2, 2) of the item art (0x5000F0); the cursor refresh 0x553990 leaves it while the item is held (CU1). The viewer draws the item art
/// with its top-left corner <see cref="Offset"/> pixels up and left of the pointer and the cursor art over it with its own hot point on
/// the pointer (PROJECT: the overlay's exact placement in 0x500160 was not read). Godot-free.
/// </summary>
public static class AreaHeldCursor
{
    /// <summary>The hot point in the item art and the overlay's place (0x500160 and 0x5000F0 with 2, 2).</summary>
    public const int Offset = 2;
    /// <summary>Godot's limit for a custom cursor image's side.</summary>
    public const int MaxSide = 256;

    /// <summary>
    /// The held cursor's image: the item art (<paramref name="itemRgba"/>, <paramref name="itemWidth"/> × <paramref name="itemHeight"/>,
    /// RGBA8) at (−<see cref="Offset"/>, −<see cref="Offset"/>) from the pointer and <paramref name="cursor"/> over it with its hot point on
    /// the pointer, in one image grown to hold both; its hot point is the pointer's place in it. Null when either is empty or the image
    /// would exceed <see cref="MaxSide"/>.
    /// </summary>
    [OriginalFunction(0x5736E0, OriginalFunctionScope.Partial)]
    public static AreaInterfaceFrame? Compose(int itemWidth, int itemHeight, byte[] itemRgba, AreaInterfaceFrame cursor)
    {
        ArgumentNullException.ThrowIfNull(itemRgba);
        ArgumentNullException.ThrowIfNull(cursor);
        if (itemWidth <= 0 || itemHeight <= 0 || itemRgba.Length != itemWidth * itemHeight * 4) return null;
        if (cursor.Width <= 0 || cursor.Height <= 0 || cursor.Rgba.Length != cursor.Width * cursor.Height * 4) return null;
        // Both corners relative to the pointer.
        var (itemX, itemY) = (-Offset, -Offset);
        var (cursorX, cursorY) = (-cursor.HotX, -cursor.HotY);
        var left = Math.Min(itemX, cursorX);
        var top = Math.Min(itemY, cursorY);
        var right = Math.Max(itemX + itemWidth, cursorX + cursor.Width);
        var bottom = Math.Max(itemY + itemHeight, cursorY + cursor.Height);
        var (width, height) = (right - left, bottom - top);
        if (width > MaxSide || height > MaxSide) return null;
        var rgba = new byte[width * height * 4];
        Over(rgba, width, itemRgba, itemWidth, itemHeight, itemX - left, itemY - top);
        Over(rgba, width, cursor.Rgba, cursor.Width, cursor.Height, cursorX - left, cursorY - top);
        return new(width, height, rgba, -left, -top);
    }

    // Source-over blend of a straight-alpha RGBA8 image into the destination at (x, y).
    private static void Over(byte[] destination, int destinationWidth, byte[] source, int width, int height, int x, int y)
    {
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var s = (row * width + column) * 4;
            var alpha = source[s + 3];
            if (alpha == 0) continue;
            var d = ((y + row) * destinationWidth + x + column) * 4;
            if (alpha == 255) { Array.Copy(source, s, destination, d, 4); continue; }
            var below = destination[d + 3] * (255 - alpha) / 255;
            var outAlpha = alpha + below;
            for (var c = 0; c < 3; c++) destination[d + c] = (byte)((source[s + c] * alpha + destination[d + c] * below) / outAlpha);
            destination[d + 3] = (byte)outAlpha;
        }
    }
}
