using Arcanum.Data.Area;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One scenery's running loop (<see cref="SceneryLoop"/>) with its object and frames (in frame order).</summary>
public sealed class AreaSceneryLoop(AreaObjectRecord row, IReadOnlyList<AreaVisualRecord> frames, SceneryLoop loop)
{
    public AreaObjectRecord Row { get; } = row;
    public AreaObjectKey Key => Row.Key;
    public IReadOnlyList<AreaVisualRecord> Frames { get; } = frames;
    public SceneryLoop Loop { get; } = loop;
    public AreaVisualRecord Visual => Frames[Loop.Frame];
}

/// <summary>
/// ARC-RENDER-035: the animated scenery of the shown map. The start rule (<see cref="SceneryAnimationRules.Starts"/>) runs
/// when an object enters the object list, i.e. at load; the loops then step on the caller's clock (the viewer's world clock,
/// the clock of the goal runs). Frames come from the plan (<see cref="AreaScenePlan.SceneryFrames"/>).
/// </summary>
public sealed class AreaSceneryAnimation
{
    private const int ObjectFlagsSlot = 18, SpellFlagsSlot = 19, SceneryFlagsSlot = 36;
    private readonly List<AreaSceneryLoop> _loops;

    private AreaSceneryAnimation(List<AreaSceneryLoop> loops) => _loops = loops;

    public IReadOnlyList<AreaSceneryLoop> Loops => _loops;

    public static AreaSceneryAnimation Create(AreaScenePlan plan, IEnumerable<AreaObjectRecord> objects, long nowMs, CellCoord pc)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(objects);
        var loops = new List<AreaSceneryLoop>();
        foreach (var row in objects)
        {
            if (!plan.SceneryFrames.TryGetValue(row.Key, out var frames) || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            var body = frames.FirstOrDefault(f => f.Key.Layer == AreaLayerKind.Body);
            // The loop needs every frame of the group, in order (the area owner decodes all of them or none).
            if (body is null || frames.Count != body.FrameCount || frames.Select((f, i) => (int)((f.RawArt >> 14) & 31) == i).Any(ok => !ok)) continue;
            if (!SceneryAnimationRules.Starts(row.Type, Flags(row, ObjectFlagsSlot), Flags(row, SceneryFlagsSlot), Flags(row, SpellFlagsSlot), body.FrameCount)) continue;
            var loop = new SceneryLoop(x, y, frames.Select(f => (f.ArtOffsetX, f.ArtOffsetY)).ToArray(), body.Frame,
                SceneryAnimationRules.PauseMs(body.FramesPerSecond), nowMs, SceneryAnimationRules.Near(x, y, pc));
            loops.Add(new(row, frames, loop));
        }
        return new(loops);
    }

    /// <summary>Steps every loop to <paramref name="nowMs"/>; true when any frame changed.</summary>
    public bool Advance(long nowMs, CellCoord pc)
    {
        var changed = false;
        foreach (var loop in _loops) changed |= loop.Loop.Advance(nowMs, pc);
        return changed;
    }

    private static uint Flags(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : 0;
}
