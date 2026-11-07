using System.Collections.Immutable;
using System.Text.Json;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-DEV-208 (schema 72): the goal runs of the active map.
    private static readonly string[] GoalsFields = ["nextRunId", "runs"];
    private static readonly string[] GoalRunFields = ["slot", "id", "owner", "flags", "pathFlags", "dueMs", "paceMs", "frames"];
    private static readonly string[] GoalFrameFields =
    [
        "goal", "node", "target", "item", "tile", "range", "sound", "skill", "spellRun", "path", "pathIndex", "plannedTarget", "restMs", "flags", "animation",
        "frameIndex", "count", "pathPending", "near", "eyeCandy", "fireRound", "rotation",
    ];
    // The flag bits a saved run or frame may carry: the defined ones, less the run's Killed (a killed run is never saved) and the hero walk's RouteFollow.
    private const int RunFlagMask = (int)(GoalRunFlags.ActionFrameReached | GoalRunFlags.ActionFrameHandled | GoalRunFlags.Running | GoalRunFlags.NoWalk | GoalRunFlags.Flight | GoalRunFlags.NearOccupied);
    private const int PathFlagMask = (int)GoalPathFlags.Replan;
    private const int FrameFlagMask = (int)(GoalFrameFlags.NotCounted | GoalFrameFlags.Follow | GoalFrameFlags.Playing | GoalFrameFlags.ActionReached | GoalFrameFlags.ActionHandled | GoalFrameFlags.Raised
        | GoalFrameFlags.Begun | GoalFrameFlags.RandomStart | GoalFrameFlags.OverlaysFound | GoalFrameFlags.NormalDamage);
    // The cell range of a saved cell: a map is 2000 x 2000 sectors of 64 cells.
    private const int MaxCell = 2000 * 64;

    private static readonly string[] GoalEyeCandyFields = ["art", "entry", "tag", "source", "frame", "backgroundFrame"];

    private static void WriteGoals(Utf8JsonWriter w, SessionGoalState goals)
    {
        w.WriteStartObject("goals");
        w.WriteNumber("nextRunId", goals.NextRunId);
        w.WriteStartArray("runs");
        foreach (var run in goals.Runs.OrderBy(r => r.Handle.Slot))
        {
            w.WriteStartObject();
            w.WriteNumber("slot", run.Handle.Slot);
            w.WriteNumber("id", run.Handle.RunId);
            w.WriteString("owner", run.Owner);
            w.WriteNumber("flags", (int)run.Flags);
            w.WriteNumber("pathFlags", (int)run.PathFlags);
            w.WriteNumber("dueMs", run.DueMs);
            w.WriteNumber("paceMs", run.PaceMs);
            w.WriteStartArray("frames");
            foreach (var frame in run.Frames) WriteGoalFrame(w, frame);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteGoalFrame(Utf8JsonWriter w, GoalFrame frame)
    {
        w.WriteStartObject();
        w.WriteNumber("goal", (int)frame.Goal);
        w.WriteNumber("node", frame.Node);
        WriteNullableString(w, "target", frame.Target);
        WriteNullableString(w, "item", frame.Item);
        WriteGoalCell(w, "tile", frame.Tile);
        w.WriteNumber("range", frame.Range);
        WriteNullableInt(w, "sound", frame.Sound);
        WriteNullableInt(w, "skill", frame.Skill);
        WriteNullableInt(w, "spellRun", frame.SpellRun);
        w.WriteStartArray("path");
        foreach (var cell in frame.Path)
        {
            w.WriteStartArray();
            w.WriteNumberValue(cell.X);
            w.WriteNumberValue(cell.Y);
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteNumber("pathIndex", frame.PathIndex);
        WriteGoalCell(w, "plannedTarget", frame.PlannedTargetTile);
        w.WriteNumber("restMs", frame.RestMs);
        w.WriteNumber("flags", (int)frame.Flags);
        w.WriteNumber("animation", frame.Animation);
        w.WriteNumber("frameIndex", frame.FrameIndex);
        w.WriteNumber("count", frame.Count);
        w.WriteBoolean("pathPending", frame.PathPending);
        w.WriteBoolean("near", frame.Near);
        if (frame.EyeCandy is { } eye)
        {
            w.WriteStartObject("eyeCandy");
            w.WriteNumber("art", eye.Art);
            w.WriteNumber("entry", eye.Entry);
            w.WriteNumber("tag", eye.Tag);
            WriteNullableString(w, "source", eye.Source);
            w.WriteNumber("frame", eye.Frame);
            w.WriteNumber("backgroundFrame", eye.BackgroundFrame);
            w.WriteEndObject();
        }
        else w.WriteNull("eyeCandy");
        w.WriteNumber("fireRound", frame.FireRound);
        w.WriteNumber("rotation", frame.Rotation);
        w.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name); else w.WriteString(name, value);
    }

    private static void WriteNullableInt(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v) w.WriteNumber(name, v); else w.WriteNull(name);
    }

    private static void WriteGoalCell(Utf8JsonWriter w, string name, CellCoord? cell)
    {
        if (cell is not { } c) { w.WriteNull(name); return; }
        w.WriteStartArray(name);
        w.WriteNumberValue(c.X);
        w.WriteNumberValue(c.Y);
        w.WriteEndArray();
    }

    // Shapes and ranges: the runs ascending by slot (the canonical order), 1..8 frames of defined goals, the entity strings valid. Which kinds the build saves, the
    // slot bounds against the table and the path index are Restore's.
    private static SessionGoalState Goals(JsonElement element)
    {
        const string p = "$.goals";
        var f = Fields(element, p, GoalsFields);
        var next = Int(f["nextRunId"], p + ".nextRunId", 1, int.MaxValue);
        var rows = Array(f["runs"], p + ".runs", GoalRunTable.SlotCount);
        var runs = new GoalRun[rows.Length];
        var previousSlot = -1;
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{p}.runs[{i}]";
            var r = Fields(rows[i], at, GoalRunFields);
            var slot = Int(r["slot"], at + ".slot", 0, GoalRunTable.SlotCount - 1);
            if (slot <= previousSlot) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".slot");
            previousSlot = slot;
            var frameRows = Array(r["frames"], at + ".frames", GoalRun.MaxFrames);
            if (frameRows.Length == 0) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at + ".frames");
            var frames = ImmutableArray.CreateBuilder<GoalFrame>(frameRows.Length);
            for (var j = 0; j < frameRows.Length; j++) frames.Add(GoalFrameOf(frameRows[j], $"{at}.frames[{j}]"));
            var run = new GoalRun(Entity(r["owner"], at + ".owner"), new(slot, Int(r["id"], at + ".id", 1, int.MaxValue)), frames[0],
                (GoalRunFlags)FlagBits(r["flags"], at + ".flags", RunFlagMask), Long(r["dueMs"], at + ".dueMs", 0, long.MaxValue), Int(r["paceMs"], at + ".paceMs", 0, 60_000))
            {
                PathFlags = (GoalPathFlags)FlagBits(r["pathFlags"], at + ".pathFlags", PathFlagMask),
            }.WithFrames(frames.ToImmutable());
            runs[i] = run;
        }
        return new(next, runs);
    }

    private static GoalFrame GoalFrameOf(JsonElement element, string p)
    {
        var f = Fields(element, p, GoalFrameFields);
        var goal = Int(f["goal"], p + ".goal", 0, int.MaxValue);
        if (!GoalTypes.IsDefined(goal) || !GoalDescriptors.Has((GoalType)goal)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".goal");
        var pathRows = Array(f["path"], p + ".path", SessionGoalLimits.MaxPathCells);
        var path = ImmutableArray.CreateBuilder<CellCoord>(pathRows.Length);
        for (var i = 0; i < pathRows.Length; i++) path.Add(GoalCell(pathRows[i], $"{p}.path[{i}]"));
        return new GoalFrame((GoalType)goal, NullableEntity(f["target"], p + ".target"), NullableGoalCell(f["tile"], p + ".tile"),
            Int(f["range"], p + ".range", 0, 1_000_000), NullableGoalInt(f["sound"], p + ".sound"))
        {
            Node = Int(f["node"], p + ".node", 0, 1_000),
            Item = NullableEntity(f["item"], p + ".item"),
            Skill = NullableGoalInt(f["skill"], p + ".skill"),
            SpellRun = NullableGoalInt(f["spellRun"], p + ".spellRun"),
            Path = path.ToImmutable(),
            PathIndex = Int(f["pathIndex"], p + ".pathIndex", 0, SessionGoalLimits.MaxPathCells),
            PlannedTargetTile = NullableGoalCell(f["plannedTarget"], p + ".plannedTarget"),
            RestMs = Int(f["restMs"], p + ".restMs", 0, int.MaxValue),
            Flags = (GoalFrameFlags)FlagBits(f["flags"], p + ".flags", FrameFlagMask),
            Animation = Int(f["animation"], p + ".animation", 0, int.MaxValue),
            FrameIndex = Int(f["frameIndex"], p + ".frameIndex", 0, int.MaxValue),
            Count = Int(f["count"], p + ".count", int.MinValue, int.MaxValue),
            PathPending = Bool(f["pathPending"], p + ".pathPending"),
            Near = Bool(f["near"], p + ".near"),
            EyeCandy = f["eyeCandy"].ValueKind == JsonValueKind.Null ? null : GoalEyeCandyOf(f["eyeCandy"], p + ".eyeCandy"),
            FireRound = Int(f["fireRound"], p + ".fireRound", int.MinValue, int.MaxValue),
            Rotation = Int(f["rotation"], p + ".rotation", 0, 7),
        };
    }

    private static GoalEyeCandy GoalEyeCandyOf(JsonElement element, string p)
    {
        var f = Fields(element, p, GoalEyeCandyFields);
        return new(UInt(f["art"], p + ".art"), Int(f["entry"], p + ".entry", int.MinValue, int.MaxValue), Int(f["tag"], p + ".tag", int.MinValue, int.MaxValue),
            NullableEntity(f["source"], p + ".source"), Int(f["frame"], p + ".frame", 0, int.MaxValue), Int(f["backgroundFrame"], p + ".backgroundFrame", 0, int.MaxValue));
    }

    private static int FlagBits(JsonElement element, string path, int mask)
    {
        var value = Int(element, path, 0, int.MaxValue);
        if ((value & ~mask) != 0) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        return value;
    }

    private static string? NullableEntity(JsonElement element, string path) => element.ValueKind == JsonValueKind.Null ? null : Entity(element, path);

    private static int? NullableGoalInt(JsonElement element, string path) => element.ValueKind == JsonValueKind.Null ? null : Int(element, path, int.MinValue, int.MaxValue);

    private static CellCoord GoalCell(JsonElement element, string path)
    {
        var pair = Array(element, path, 2);
        if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        return new(Int(pair[0], path + "[0]", 0, MaxCell), Int(pair[1], path + "[1]", 0, MaxCell));
    }

    private static CellCoord? NullableGoalCell(JsonElement element, string path) => element.ValueKind == JsonValueKind.Null ? null : GoalCell(element, path);
}
