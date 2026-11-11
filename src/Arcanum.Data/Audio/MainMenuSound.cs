using Arcanum.Data.Maps;

namespace Arcanum.Data.Audio;

/// <summary>
/// ARC-DEV-332: where the original's main menu gets its music. The game's init module (0x4B9C30, then 0x4B9BE0) opens the map of
/// type 2 (<see cref="MapListType.Shopping"/>, the opening shop's map) or, when the list has none, the map of type 1
/// (<see cref="MapListType.Start"/>) by 0x40FC70 and puts the local PC on it (0x40D8A0); no code of the menu starts a song (every caller of
/// the scheme request, the music streams and the music files is a map, a sector, a script, the world map, a slide or combat). The shop map
/// has no <c>SoundScheme</c> line, but its one sector (0, 0), where the PC stands, has the sector pair 26/29 (<c>M.Main Menu</c>, which loops
/// <c>arcanum.mp3</c> all day, and no ambient): that is the main menu's soundtrack, and the owner's ear agrees. The pair is the sector's
/// (0x4F2330), the map default plays first as at every map open (0x4115D0).
/// </summary>
public static class MainMenuSound
{
    /// <summary>The map the menu stands on and the sector the PC is placed in (null when the row has no start tile).</summary>
    public sealed record Place(MapListEntry Map, WorldSectorCoordinate? Sector);

    /// <summary>The tiles of one sector side (the sector id of a tile is its coordinate divided by this).</summary>
    public const int SectorSide = 64;

    /// <summary>The map and sector of the game init's opening: the shopping map, else the start map; null for a list with neither.</summary>
    public static Place? Choose(MapList maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        if ((maps.OfType(MapListType.Shopping) ?? maps.OfType(MapListType.Start)) is not { } entry) return null;
        return new(entry, entry.Start is { } start && start.X / SectorSide <= WorldSectorCoordinate.MaxCoordinate && start.Y / SectorSide <= WorldSectorCoordinate.MaxCoordinate
            ? new WorldSectorCoordinate((int)(start.X / SectorSide), (int)(start.Y / SectorSide)) : null);
    }
}
