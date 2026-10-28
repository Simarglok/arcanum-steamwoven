using Arcanum.Simulation.Sound;

namespace Arcanum.Simulation.Equipment;

/// <summary>
/// ARC-WORLD-112: the art a critter shows for its body armour, as the retail 1.0.7.4 executable sets it (ledger BA1–BA6). Wearing an
/// item in the body-armour slot (1006) sets the critter's current art to <see cref="WornArt"/> of the item (the wield hook 0x4677B0);
/// taking it off sets <see cref="WornArt"/> without an item (0x467CB0). Only the body-armour slot changes the body: helmet, gauntlets,
/// boots, rings and medallion are not drawn on it, and the weapon and shield slots take the weapon art (0x465020, not here). The art
/// id's armour part is the item's use-art subtype, its palette the use art's palette; bare, the part is 0 (underwear) and the palette
/// the critter's base art's. The file the id names (the race and gender body, plate's sexless art) is the art owner's
/// (<c>AreaOriginalArtNames</c>, N1).
/// </summary>
public static class BodyArmorArtRules
{
    /// <summary>Art type of a critter (2), an item (6), a monster (12) and a unique NPC (13), bits 28..31 of an art id.</summary>
    public const uint CritterArt = 2, ItemArt = 6, MonsterArt = 12, UniqueNpcArt = 13;
    /// <summary>The armour parts a critter art id holds (0..8: underwear, villager, leather, chain, plate, robe, plate 2, barbarian,
    /// clothes) and a monster art id (0..7).</summary>
    public const int CritterArmorParts = 9, MonsterArmorParts = 8;
    /// <summary>The palettes an art id holds (bits 4..5).</summary>
    public const int Palettes = 4;

    /// <summary>The critter's art with its armour part set (0x504060): bits 20..23 of a critter art id, bits 20..22 of a monster's;
    /// a part past the type's range (9 or more, 8 or more) sets 0 (the original logs it); another art type is returned unchanged.</summary>
    [OriginalFunction(0x504060, OriginalFunctionScope.Full)]
    public static uint WithArmor(uint art, int armor) => (art >> 28) switch
    {
        CritterArt => (art & 0xFF0FFFFFu) | ((uint)(armor is >= 0 and < CritterArmorParts ? armor : 0) << 20),
        MonsterArt => (art & 0xFF8FFFFFu) | ((uint)(armor is >= 0 and < MonsterArmorParts ? armor : 0) << 20),
        _ => art,
    };

    /// <summary>The armour part an item's use art gives its wearer (0x5044F0): bits 6..9 of an item art id, 0 for another type (the bits
    /// <c>Arcanum.Data.Combat.CombatItemArt.Subtype</c> reads too; Simulation does not reference Data).</summary>
    [OriginalFunction(0x5044F0, OriginalFunctionScope.Full)]
    public static int ItemArmor(uint useArt) => useArt >> 28 == ItemArt ? (int)((useArt >> 6) & 15) : 0;

    /// <summary>An art id's palette (0x502D80): bits 4..5, 0 for art types 9 and 11.</summary>
    [OriginalFunction(0x502D80, OriginalFunctionScope.Full)]
    public static int Palette(uint art) => (art >> 28) is 9 or 11 ? 0 : (int)((art >> 4) & 3);

    /// <summary>The art with its palette set (0x502DB0): bits 4..5; a palette past 3 sets 0 (the original logs it); art types 9 and 11
    /// are returned unchanged.</summary>
    [OriginalFunction(0x502DB0, OriginalFunctionScope.Full)]
    public static uint WithPalette(uint art, int palette) =>
        (art >> 28) is 9 or 11 ? art : (art & ~0x30u) | ((uint)(palette is >= 0 and < Palettes ? palette : 0) << 4);

    /// <summary>
    /// The critter's art for its body armour (0x465AE0). Without an item: the current art with armour part 0 and the base art's palette.
    /// With an item: the current art with the item's armour part (<see cref="ItemArmor"/> of its use art) and the use art's palette, or
    /// null when the part does not stand in the art id (a part the critter's type cannot hold, or a type without one: the setter leaves
    /// a different part, so the original fails and the wear check refuses the item as not wieldable).
    /// </summary>
    /// <param name="currentArt">The critter's current art (object field 1).</param>
    /// <param name="baseArt">The critter's base art (object field 0x18), whose palette a bare body takes.</param>
    /// <param name="useArt">The worn item's use art (item field 0x5F), or null for no item.</param>
    [OriginalFunction(0x465AE0, OriginalFunctionScope.Full)]
    public static uint? WornArt(uint currentArt, uint baseArt, uint? useArt)
    {
        if (useArt is not { } use) return WithPalette(WithArmor(currentArt, 0), Palette(baseArt));
        var armor = ItemArmor(use);
        var worn = WithArmor(currentArt, armor);
        return SoundCueRules.CritterArtArmor(worn) == armor ? WithPalette(worn, Palette(use)) : null;
    }
}
