namespace Arcanum.Data.Area;

/// <summary>
/// ARC-SCRIPT-1735 (E5-a tail, ledger NS5, NS9): what the script action change art number (59) does to an object's art id (object field 1), as the
/// original's art id helpers do it (0x502700 the class, 0x502780 the number setter, 0x502C40 the frame setter), on the art id alone: the class is the
/// top four bits; the classes 0, 1, 2 and 12 (tile, wall, critter, monster) and the items' own range are not changed here (see <see cref="WithNumber"/>).
/// Whether the new id names an existing ART file is the caller's test (0x502220).
/// </summary>
public static class ObjectArtNumber
{
    /// <summary>The art class of an id: the top four bits (0x502700, read inline by the project's code).</summary>
    public static int Class(uint aid) => (int)(aid >> 28);

    /// <summary>The classes whose number setter answers 0 (tile 0, wall 1, critter 2 and monster 12): the original builds the id 0 for them, which names no ART of this project's reading.</summary>
    public static bool IsCritterClass(uint aid) => Class(aid) is 0 or 1 or 2 or 12;

    /// <summary>
    /// 0x502780: the id with its number bits replaced. A unique NPC (class 13) takes 8 bits at bit 20 (range 256); an interface art (class 5) 12 bits at bit 16 (range 4,096); an item (class 6) 11 bits at
    /// bit 17 with a range that depends on its subtype (the project does not model it: <see cref="ItemRangeKnown"/> is false and an item id is returned as it is); every other class (portal, scenery, container, misc,
    /// light, roof, facade, eye candy) takes 9 bits at bit 19 (range 512). The classes 0, 1, 2 and 12 answer 0. A number outside the range logs an error in the original and keeps the id (<paramref name="outOfRange"/>).
    /// </summary>
    [OriginalFunction(0x502780, OriginalFunctionScope.Partial)]
    public static uint WithNumber(uint aid, uint number, out bool outOfRange)
    {
        outOfRange = false;
        var cls = Class(aid);
        if (cls is 0 or 1 or 2 or 12) return 0;
        uint range, mask; int shift;
        if (cls == 13) { range = 0x100; mask = 0x0FF00000; shift = 0x14; }
        else if (cls == 6) { return aid; }
        else if (cls == 5) { range = 0x1000; mask = 0x0FFF0000; shift = 0x10; }
        else { range = 0x200; mask = 0x0FF80000; shift = 0x13; }
        if (number >= range) { outOfRange = true; return aid; }
        return (~mask & aid) | (number << shift);
    }

    /// <summary>Whether <see cref="WithNumber"/> models the class's number range (an item's range is not modelled).</summary>
    public static bool ItemRangeKnown(uint aid) => Class(aid) != 6;

    /// <summary>
    /// 0x502C40 with frame 0: the id with its frame bits cleared. The classes 0, 1 and 6 keep the id; an interface or misc art (5, 8) holds the frame in bits 8..15; a facade (11) in bits 1..10; a light and an eye
    /// candy (9, 14) in bits 12..18; every other class in bits 14..18.
    /// </summary>
    [OriginalFunction(0x502C40, OriginalFunctionScope.Partial)]
    public static uint WithFrameZero(uint aid) => Class(aid) switch
    {
        0 or 1 or 6 => aid,
        5 or 8 => aid & 0xFFFF00FF,
        11 => aid & 0xFFFFF801,
        9 or 14 => aid & 0xFFF80FFF,
        _ => aid & 0xFFF83FFF,
    };

    /// <summary>The id change art number writes for an object whose art id is <paramref name="aid"/> and the number <paramref name="number"/> (the number setter, then frame 0).</summary>
    public static uint Changed(uint aid, int number, out bool outOfRange) => WithFrameZero(WithNumber(aid, unchecked((uint)number), out outOfRange));
}
