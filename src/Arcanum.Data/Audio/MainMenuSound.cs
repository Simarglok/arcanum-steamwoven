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

    /// <summary>ARC-UI-139: the two scheme slots the menu's pair fills (0 = empty), without a game session.</summary>
    public readonly record struct Slots(int Slot0, int Slot1);

    /// <summary>
    /// ARC-UI-139: the schemes in the menu's two slots, as the original's scheme request (0x41BD50, <c>SoundDomain.Play</c>) leaves them for the
    /// opening's map: the map default plays first as at every map open (0x4115D0); the start sector then asks for its pair unless both ids are 0
    /// (0x4F2330), which replaces the default (the request leaves the same two ids as a set; their slot order may differ from the original's when the opening map has a default pair, which the shop map has not, and the scheme player treats both slots alike). An ambient id
    /// equal to the music id becomes 0, and an id whose scheme has no list base fills no slot; a loaded id takes the first free slot (the
    /// installed pair 26/29: scheme 26 loops <c>arcanum.mp3</c> in slot 0, 29 loads nothing).
    /// </summary>
    /// <param name="mapDefault">The map info's pair, or null without one.</param>
    /// <param name="sector">The start sector's pair, or null without a sound record.</param>
    /// <param name="loads">Whether a scheme id fills a slot (the catalogue found its list).</param>
    public static Slots Choose(SoundPair? mapDefault, SoundPair? sector, Func<int, bool> loads)
    {
        ArgumentNullException.ThrowIfNull(loads);
        var pair = sector is { } sectorPair && (sectorPair.Music != 0 || sectorPair.Ambient != 0) ? sectorPair : mapDefault ?? default;
        var (music, ambient) = pair.Music == pair.Ambient ? (pair.Music, 0) : (pair.Music, pair.Ambient);
        var slots = new int[2];
        var next = 0;
        foreach (var id in new[] { music, ambient })
            if (id != 0 && loads(id)) slots[next++] = id;
        return new(slots[0], slots[1]);
    }

    /// <summary>A music and ambient scheme id pair (a map info's <c>SoundScheme</c> line, or a sector's sound record).</summary>
    public readonly record struct SoundPair(int Music, int Ambient);
}
