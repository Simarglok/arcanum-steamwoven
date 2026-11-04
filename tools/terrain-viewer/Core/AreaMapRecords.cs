using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-072 (MIG1, TM3): the record source of the viewer: the object rows of any whole map of the game folder's module content
/// stack, by runtime map number, read on demand for <see cref="RecordResolver"/>. It reads rows only: the strict AREA publication of
/// the map and its whole-map step evidence (the life evidence of its NPCs), no scene plan, no art, no lighting. A map that opens only
/// as a window (the main world map: more sector files than a whole-map plan admits) is refused with a reason, since its stored
/// objects outside the window cannot be listed this way (NOT READY: an NPC whose home is such a map resolves only while the viewer
/// holds that map's window; the viewer's launch maps are given to the resolver, so the Crash Site's own NPCs are held).
/// Unlike a map load it does not compare the viewer's archive arguments with the stack's sources (`RequireSame`) or the expected
/// digests: the stack comes from the same game folder as the viewer's, and the rows are derived data.
/// A map the list does not name, one with no sector files, or one that does not read is a typed failure, never an exception.
/// </summary>
public sealed class AreaMapRecords(TerrainViewerArguments launch) : IMapRecordSource
{
    public MapRecordsLoad Load(int runtimeMap, CancellationToken token)
    {
        try { return LoadChecked(runtimeMap, token); }
        catch (Exception error) when (error is InvalidOperationException or AreaException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(null, $"map {runtimeMap} did not read: {error.GetType().Name} {error.Message}");
        }
    }

    private MapRecordsLoad LoadChecked(int runtimeMap, CancellationToken token)
    {
        var discovery = ContentMountPlan.Discover(launch.GameRoot ?? throw new InvalidOperationException("AREA_RECORDS_GAME_ROOT_REQUIRED"));
        if (discovery.Status != ContentMountStatus.Ready) return new(null, "mount " + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(null, "stack " + opened.Status);
        using var stack = opened.Stack!;
        var list = MapList.Read(stack);
        if (list.Status != MapListStatus.Read) return new(null, "the map list did not read: " + list.Status);
        if (list.ByRuntime(runtimeMap) is not { } entry) return new(null, $"the map list has no runtime map {runtimeMap}");
        var created = AreaMapPlans.Create(stack, WorldCatalog.Open(stack), entry.Name, launch.DataRules ?? Arcanum.Data.Fixes.DataFixProfile.Steamwoven);
        if (created.Status != AreaMapPlanStatus.Ready) return new(null, $"map {entry.Name} has no whole-map plan: {created.Status}");
        token.ThrowIfCancellationRequested();
        var plan = created.Plan!;
        using var owner = plan.Open();
        var snapshot = owner.LoadStrict(token);
        var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, plan.Map.AggregatePath, 1);
        var build = AreaInventoryMap.Build(snapshot, steps, token);
        return build.Map is null ? new(null, $"map {entry.Name} has no records: {build.Status} {build.Detail}") : new(build.Map, build.Detail);
    }
}
