namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-407: which of the viewer's two save folders a run uses. Map mode (a run started on a named map) keeps its saves in
/// <c>saves/map</c>, the Crash Site mode in <c>saves</c>. The choice follows the run's launch map, not whether the map is already
/// loaded: in the lazy start (ARC-DEV-387) the menu's Load Game and Last Save open before any scene stands, and a choice by the loaded
/// map made them read the Crash Site folder, so a player with saves saw an empty list until the start map had loaded.
/// </summary>
public static class AreaSaveFolder
{
    /// <param name="readyMapName">The map of the loaded scene, <c>null</c> while none stands (a lazy start, a map load).</param>
    /// <param name="launchMapName">The map the run was started on (<c>--area-map</c>), <c>null</c> in the Crash Site mode.</param>
    public static bool UsesMapFolder(string? readyMapName, string? launchMapName) => readyMapName is not null || launchMapName is not null;
}
