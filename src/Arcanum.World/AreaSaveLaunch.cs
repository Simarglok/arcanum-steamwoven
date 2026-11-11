using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>What <see cref="AreaSaveLaunch"/> found: the arguments of the scene a save opens on, or why it cannot be opened.</summary>
public sealed record AreaSaveLaunchResult(TerrainViewerArguments? Arguments, string? Refusal, int RuntimeMap = 0, string? MapName = null);

/// <summary>
/// ARC-DEV-387: the scene a save opens on, found before any scene is loaded. The main menu opens before the start map is loaded
/// (ARC-DEV-381) and the start map is loaded only when a new game is begun; a save chosen from the menu (Last Save, or a row of the
/// load window) is read for the map it was made on (its runtime number, by the map list) and that map loads at once as the scene the
/// menu's load then runs on, instead of the start map loading first and the save's map after it. The scene is made as a transfer
/// to that map is: the saved cell as the arrival tile (a map too large to load whole opens its window around it), no hero (the save
/// brings its own), the default data rules (the save's session brings its own).
/// </summary>
public static class AreaSaveLaunch
{
    /// <summary>Reads the save, then finds the map it was made on in the game's map list (the maps this mode can open, those with sector
    /// files). Refuses with the reason when the save does not read, names no listed map, or the game folder does not open. Safe to call
    /// off the main thread; reads the save file and the shared content index.</summary>
    public static AreaSaveLaunchResult Resolve(TerrainViewerArguments launch, GameSessionSaveSlot slot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(slot);
        var read = slot.Read();
        if (read.Snapshot is not { } snapshot) return new(null, "the save could not be read: " + AreaQuickSave.Describe(read));
        token.ThrowIfCancellationRequested();
        var placement = snapshot.Actor;
        try
        {
            var found = AreaContentRoot.Discover(launch.ModulePath);
            if (found.Status != AreaContentRootStatus.Ready) return new(null, "the module's content did not mount: " + found.Detail);
            var opened = ContentStack.OpenShared(found.Plan!);
            if (opened.Status != ContentStackStatus.Ready) return new(null, "the module's content did not open: " + opened.Status);
            using var stack = opened.Stack!;
            var list = MapList.Read(stack);
            if (list.Status != MapListStatus.Read) return new(null, "the map list did not read: " + list.Status);
            var catalog = WorldCatalog.Open(stack);
            return Build(launch, placement.Map.RuntimeMap, placement.Cell.X, placement.Cell.Y,
                list.ByRuntime(placement.Map.RuntimeMap) is { } entry && catalog.Find(entry.Name) is { SectorFiles.Count: > 0 } folder ? folder.Name : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(null, "the map list did not read: " + error.GetType().Name);
        }
    }

    /// <summary>The scene arguments for a save made on runtime map <paramref name="runtimeMap"/> at the cell
    /// (<paramref name="x"/>, <paramref name="y"/>), whose folder name in the map list is <paramref name="mapName"/> (null: the map list
    /// has no openable map under that number).</summary>
    public static AreaSaveLaunchResult Build(TerrainViewerArguments launch, int runtimeMap, long x, long y, string? mapName)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.AreaMapName is null) return new(null, "the exterior mode has no map list to open the save's map from", runtimeMap);
        if (mapName is null) return new(null, $"map mode cannot open the save's map (runtime map {runtimeMap})", runtimeMap);
        return new(launch with { Hero = null, AreaMapName = mapName, AreaMapArrival = new WorldLocation(x, y), AreaMapSector = null, DataRules = null },
            null, runtimeMap, mapName);
    }
}
