using Arcanum.Data.Area;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>One scenery's loop state (<see cref="SceneryLoop"/>) with its object and frames (in frame order). <see cref="Burning"/> is scenery flag 0x10: the loop is goal 77, not goal 1 (GT77).</summary>
public sealed class AreaSceneryLoop(AreaObjectRecord row, IReadOnlyList<AreaVisualRecord> frames, SceneryLoop loop, bool burning)
{
    public AreaObjectRecord Row { get; } = row;
    public bool Burning { get; } = burning;
    /// <summary>The session entity that owns the loop's run.</summary>
    public string Entity { get; } = AreaUseScriptHost.EntityId(row);
    public AreaObjectKey Key => Row.Key;
    public IReadOnlyList<AreaVisualRecord> Frames { get; } = frames;
    public SceneryLoop Loop { get; } = loop;
    public AreaVisualRecord Visual => Frames[Loop.Frame];
}

/// <summary>
/// ARC-RENDER-035: the animated scenery of the shown map. The start rule (<see cref="SceneryAnimationRules.Starts"/>) runs
/// when an object enters the object list, i.e. at load. ARC-DEV-142 (MIG13 S9a): the loops' timing is the scenery's goal run (<see cref="AreaSceneryHost"/>), which steps a loop's frame on the animation clock.
/// Frames come from the plan (<see cref="AreaScenePlan.SceneryFrames"/>).
/// </summary>
public sealed class AreaSceneryAnimation
{
    private const int ObjectFlagsSlot = 18, SpellFlagsSlot = 19, SceneryFlagsSlot = 36;
    /// <summary>Scenery flag 0x10: the scenery burns (the starter 0x433580 picks goal 77 for it, GT77).</summary>
    public const uint BurningFlag = 0x10;
    private readonly List<AreaSceneryLoop> _loops;

    private AreaSceneryAnimation(List<AreaSceneryLoop> loops) => _loops = loops;

    public IReadOnlyList<AreaSceneryLoop> Loops => _loops;

    /// <summary>
    /// ARC-RENDER-1781 (AN6, AS5): a loop whose frames changed (the object's art id was changed, <see cref="AreaSceneryHost.Reframe"/>) takes the place of the object's loop (in its list position), or joins the list when the object had none.
    /// </summary>
    internal void Replace(AreaSceneryLoop loop)
    {
        var at = _loops.FindIndex(l => l.Entity == loop.Entity);
        if (at >= 0) _loops[at] = loop; else _loops.Add(loop);
    }

    /// <summary>ARC-RENDER-1781: the object's loop is gone (its run was killed, the art has one frame); false when it had none.</summary>
    internal bool Remove(string entity) => _loops.RemoveAll(l => l.Entity == entity) > 0;

    public static AreaSceneryAnimation Create(AreaScenePlan plan, IEnumerable<AreaObjectRecord> objects)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(objects);
        var loops = new List<AreaSceneryLoop>();
        foreach (var row in objects)
        {
            if (BaseFrames(plan, row) is not { } own || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            var (frames, body) = own;
            if (!SceneryAnimationRules.Starts(row.Type, Flags(row, ObjectFlagsSlot), Flags(row, SceneryFlagsSlot), Flags(row, SpellFlagsSlot), body.FrameCount)) continue;
            var loop = new SceneryLoop(x, y, frames.Select(f => (f.ArtOffsetX, f.ArtOffsetY)).ToArray(), body.Frame,
                SceneryAnimationRules.PauseMs(body.FramesPerSecond));
            loops.Add(new(row, frames, loop, (Flags(row, SceneryFlagsSlot) & BurningFlag) != 0));
        }
        return new(loops);
    }

    /// <summary>
    /// The frames the plan holds of the scenery's own art (the map data's art id), in frame order, with the T0 body's visual (the frame count and rate are its), or null when the plan holds none of it: the loop needs every frame of the group, in order
    /// (the area owner decodes all of them or none).
    /// </summary>
    public static (IReadOnlyList<AreaVisualRecord> Frames, AreaVisualRecord Body)? BaseFrames(AreaScenePlan plan, AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(row);
        if (!plan.SceneryFrames.TryGetValue(row.Key, out var frames)) return null;
        var body = frames.FirstOrDefault(f => f.Key.Layer == AreaLayerKind.Body);
        if (body is null || frames.Count != body.FrameCount || frames.Select((f, i) => (int)((f.RawArt >> 14) & 31) == i).Any(ok => !ok)) return null;
        return (frames, body);
    }

    /// <summary>The object flags (field 18), the scenery flags (36) and the spell flags (19) of the row, as the start rule reads them (0 when absent).</summary>
    public static uint ObjectFlags(AreaObjectRecord row) => Flags(row, ObjectFlagsSlot);
    public static uint SceneryFlags(AreaObjectRecord row) => Flags(row, SceneryFlagsSlot);
    public static uint SpellFlags(AreaObjectRecord row) => Flags(row, SpellFlagsSlot);

    private static uint Flags(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : 0;
}
