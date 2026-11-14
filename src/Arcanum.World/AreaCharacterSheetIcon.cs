using Arcanum.Data.Combat;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-1791: the interface icon the character sheet puts in its portrait slot (141, 21) for an NPC that has no portrait to show, read from the retail 1.0.7.4
/// executable (ledger CHW8 and CHW25). The icon chooser is the one the loot window's holder image
/// uses (0x553D10, <see cref="AreaLootArtLayout.HolderImage"/>). Godot-free.
/// </summary>
public static class AreaCharacterSheetIcon
{
    /// <summary>Every interface art the slot can show (the sheet reads them besides its own arts; a missing one leaves the slot empty).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. AreaLootArtLayout.RaceImages, 383, 384, 385, 434];

    /// <summary>
    /// The art of an NPC's icon when it has no portrait to draw (the critter branch of 0x553D10, after its portrait test): the first of critter flags
    /// 0x4, 0x8000, 0x8, 0x20000000 that is set (arts 384, 383, 385, 434), else the race table by the race stat (art 634 for the stat's 11). Null for
    /// a race stat outside the table.
    /// </summary>
    public static int? NpcArt(uint critterFlags, int race) => AreaLootArtLayout.HolderImage(CombatFieldReader.NpcType, critterFlags, race, 0);
}
