using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Measured follow inputs of one area NPC (ARC-RES-026): the NPC is matched by prototype and dialog script, and
/// <see cref="Thresholds"/>, <see cref="Level"/> and <see cref="Alignment"/> are its T0 values.
/// </summary>
public sealed record AreaCompanionFacts(int Prototype, int ScriptId, FollowThresholds Thresholds, int Level, int Alignment);

/// <summary>
/// The NPCs whose follow consent and follow movement run through the session party (ARC-WORLD-013, ARC-WORLD-016).
/// Only measured NPCs are listed; every other speaker keeps the diagnostic follow key of ARC-SCRIPT-005. The PC side
/// is the AREA diagnostic profile (charisma 8, level 1, no Persuasion), not a character sheet.
/// </summary>
public static class AreaCompanionRoster
{
    /// <summary>Virgil at T0 (ARC-RES-026): prototype 17102, script 1324, AI row 5 (−1000, 2001, 2001, 30), level 1, alignment 1.</summary>
    public static AreaCompanionFacts Virgil { get; } = new(17102, 1324, new(-1000, 2001, 2001, 30), 1, 1);
    public const int DiagnosticPcCharisma = 8, DiagnosticPcLevel = 1;

    public static AreaCompanionFacts? For(AreaSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        return speaker.Row.PrototypeNumber == Virgil.Prototype && speaker.ScriptId == Virgil.ScriptId ? Virgil : null;
    }

    /// <summary>The follow-check facts for <paramref name="npc"/> against the diagnostic PC.</summary>
    public static PartyConsentFacts Facts(AreaCompanionFacts npc, int pcAlignment, int reaction) => new(
        MindControlledByPc: false, PcPersuasion: PersuasionTraining.None, PcCharisma: DiagnosticPcCharisma, PcAlignment: pcAlignment,
        PcLevel: DiagnosticPcLevel, OtherLeaderCharisma: null, Reaction: reaction, Thresholds: npc.Thresholds,
        NpcAlignment: npc.Alignment, NpcLevel: npc.Level, MaxFollowers: FollowConsentRules.MaxFollowers(DiagnosticPcCharisma, PersuasionTraining.None));
}

/// <summary>
/// Where a companion stands, kept in the session as one map-local entity change of the NPC (property
/// <see cref="Property"/> on the NPC's map, project-defined): the world cell and facing packed into one value, so a
/// placement is one atomic change. Absent means its serialized T0 cell. Values outside the packing are refused.
/// </summary>
public static class AreaCompanionPlacement
{
    public const string Property = "npc.cell";
    private const long Axis = 1L << 28;

    public static long Encode(CellCoord world, NeighborDirection facing)
    {
        if (world.X < 0 || world.X >= Axis || world.Y < 0 || world.Y >= Axis || !Enum.IsDefined(facing))
            throw new ArgumentOutOfRangeException(nameof(world));
        return ((long)world.X << 32) | ((long)world.Y << 3) | (long)facing;
    }

    public static (CellCoord World, NeighborDirection Facing)? Decode(long value)
    {
        long x = value >> 32, y = (value & 0xFFFFFFFFL) >> 3, facing = value & 7;
        if (value < 0 || x >= Axis || y >= Axis) return null;
        return (new CellCoord((int)x, (int)y), (NeighborDirection)facing);
    }

    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

/// <summary>One companion of the shown map: its own authoritative grid session and follow goal.</summary>
public sealed class AreaCompanion
{
    internal AreaCompanion(AreaSpeaker speaker, string entity, AreaCompanionFacts facts, CellCoord origin, AreaGridSession grid, CompanionFollower follow)
    {
        Speaker = speaker; Entity = entity; Facts = facts; Origin = origin; Grid = grid; Follow = follow;
    }

    public AreaSpeaker Speaker { get; }
    public string Entity { get; }
    public AreaCompanionFacts Facts { get; }
    public AreaObjectKey Key => Speaker.Row.Key;
    /// <summary>The serialized T0 chart cell of the NPC.</summary>
    public CellCoord Origin { get; }
    public AreaGridSession Grid { get; internal set; }
    public CompanionFollower Follow { get; }
    public CompanionStanding Standing { get; internal set; }
    public CompanionFollowUpdate LastUpdate { get; internal set; }
    public int Jumps { get; internal set; }
}

/// <summary>
/// ARC-WORLD-016: the measured companions of one AREA map as moving actors. Each one walks on its own
/// <see cref="AreaGridSession"/> over the map's T0 step evidence with the live overlay of <see cref="AreaOccupancy"/>:
/// its T0 record no longer blocks, and the PC and the other companions block where they stand. While the session
/// party says it follows the PC, <see cref="CompanionFollower"/> drives it after the PC at each frame boundary; a
/// waiting or free companion stands where it stopped. The PC is admitted against the companions in the same way.
/// Placement lives in the session (<see cref="AreaCompanionPlacement"/>) only when the host records it (quick save,
/// leaving the map), like the PC's. The drawn body is the NPC's T0 frame moved with the actor (no walk cycle or
/// turning). Caller-serialized; no Godot.
/// </summary>
public sealed class AreaCompanionGroup
{
    public const string ActorPrefix = "AREA_COMPANION_";
    public const string Capabilities = "AREA_NPC_T0_BODY_FOLLOWER_V1";
    private readonly AreaStepSnapshot _steps;
    private readonly AreaChart _chart;
    private readonly SessionMapId _map;
    private readonly AreaCompanion[] _companions;

    private AreaCompanionGroup(AreaStepSnapshot steps, AreaChart chart, SessionMapId map, AreaCompanion[] companions)
    {
        _steps = steps; _chart = chart; _map = map; _companions = companions;
    }

    public IReadOnlyList<AreaCompanion> Companions => _companions;
    public SessionMapId Map => _map;
    /// <summary>Speakers listed by the roster that could not stand on this map's chart (cell not admitted, outside the chart, bad placement).</summary>
    public IReadOnlyList<string> Skipped { get; private init; } = [];

    /// <summary>
    /// The roster speakers of <paramref name="speakers"/> on map <paramref name="map"/>, each standing on its session
    /// placement when one is recorded for this map and on its T0 cell otherwise.
    /// </summary>
    public static AreaCompanionGroup Create(IEnumerable<AreaSpeaker> speakers, AreaStepSnapshot steps, AreaChart chart,
        GameSessionOwner session, SessionMapId map, SimulationTime time)
    {
        ArgumentNullException.ThrowIfNull(speakers); ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(chart); ArgumentNullException.ThrowIfNull(session);
        var list = new List<AreaCompanion>(); var skipped = new List<string>();
        AreaCompanionGroup? group = null;
        foreach (var speaker in speakers)
        {
            if (AreaCompanionRoster.For(speaker) is not { } facts) continue;
            var entity = AreaConversationState.EntityId(speaker.Row);
            var origin = Chart(chart, speaker.WorldX, speaker.WorldY);
            var start = origin; var facing = NeighborDirection.XPlus;
            if (session.QueryMapChange(map, AreaCompanionPlacement.Key(entity)) is { } value)
            {
                if (AreaCompanionPlacement.Decode(value) is not { } placed) { skipped.Add(entity + " placement unreadable"); continue; }
                start = Chart(chart, placed.World.X, placed.World.Y); facing = placed.Facing;
            }
            if (!Admitted(steps, chart, start)) { skipped.Add(entity + " cell not admitted"); continue; }
            list.Add(new(speaker, entity, facts, origin, null!, null!));
            var index = list.Count - 1;
            // Every companion and route source reads the group's live overlay at assessment time.
            AreaOccupancy Admission() => group!.OccupancyFor(index, leader: true);
            AreaOccupancy Planning() => group!.OccupancyFor(index, leader: false);
            var grid = new AreaGridSession(steps, time, chart: chart, start: start, facing: facing,
                actorId: ActorPrefix + speaker.Index, capabilities: Capabilities) { Occupancy = Admission };
            list[index] = new(speaker, entity, facts, origin, grid, new CompanionFollower(new AreaRouteSource(steps, chart, Planning)));
        }
        group = new(steps, chart, map, list.ToArray()) { Skipped = skipped.AsReadOnly() };
        group.Refresh(session);
        return group;
    }

    /// <summary>The PC's overlay: every companion's T0 record is moved, and the companions hold their live cells.</summary>
    public AreaOccupancy OccupancyForPc() => _companions.Length == 0 ? AreaOccupancy.None : Cached(-1, true);

    /// <summary>The PC's live cells, set by the host after each PC advance (committed cell and a started step's cell).</summary>
    public IReadOnlyList<CellCoord> Pc { get; private set; } = [];

    /// <summary>Companion <paramref name="index"/>'s overlay: the PC (unless <paramref name="leader"/> is false, for
    /// planning toward it) and the other companions block; every companion's T0 record is moved.</summary>
    private AreaOccupancy OccupancyFor(int index, bool leader) => Cached(index, leader);

    // A planner asks for the overlay at every step it assesses: one value per (mover, PC cells, companion states) is
    // reused while none of them changed (states are immutable and replaced by each advance).
    private readonly Dictionary<(int Index, bool Leader), (object[] Inputs, AreaOccupancy Value)> _cache = [];

    private AreaOccupancy Cached(int index, bool leader)
    {
        var inputs = new object[_companions.Length + 1];
        inputs[0] = Pc;
        for (var i = 0; i < _companions.Length; i++) inputs[i + 1] = (object?)_companions[i].Grid?.State ?? this;
        if (_cache.TryGetValue((index, leader), out var known) && known.Inputs.Length == inputs.Length
            && known.Inputs.Zip(inputs).All(pair => ReferenceEquals(pair.First, pair.Second))) return known.Value;
        var held = new List<CellCoord>();
        if (leader && index >= 0) held.AddRange(Pc);
        for (var i = 0; i < _companions.Length; i++)
            if (i != index && _companions[i].Grid is { } other) held.AddRange(Held(other.State));
        var value = new AreaOccupancy(_companions.Select(c => c.Key), held);
        _cache[(index, leader)] = (inputs, value);
        return value;
    }

    private static IEnumerable<CellCoord> Held(MovementState state)
    {
        yield return state.CommittedCell;
        if (state.Segment is { } segment && segment.To != state.CommittedCell) yield return segment.To;
        if (state.Segment is { } other && other.From != state.CommittedCell) yield return other.From;
    }

    /// <summary>Reads each companion's standing from the session (call after a dialog or a load).</summary>
    public void Refresh(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var companion in _companions) companion.Standing = session.QueryCompanion(companion.Entity).Standing;
    }

    /// <summary>
    /// One frame: the PC stands as <paramref name="pc"/> after its advance to <paramref name="frontier"/>; each
    /// following companion decides and drives to the same boundary. A jump stands it settled on the PC's cell (the
    /// next decision steps it off). Returns every companion advance's results in order (for logging).
    /// </summary>
    public IReadOnlyList<(AreaCompanion Companion, GridAdvance Advance)> Drive(GameSessionOwner session, MovementState pc, SimulationTime frontier)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(pc);
        Pc = Held(pc).ToArray();
        var advances = new List<(AreaCompanion, GridAdvance)>();
        foreach (var companion in _companions)
        {
            var record = session.QueryCompanion(companion.Entity);
            companion.Standing = record.Standing;
            var grid = companion.Grid;
            if (frontier.Value <= grid.State.Time.Value) continue;
            var update = companion.Follow.Update(grid.State, pc.CommittedCell, record.SpreadOut, record.Standing == CompanionStanding.Following, frontier);
            companion.LastUpdate = update;
            if (update.JumpTo is { } jump)
            {
                if (!Admitted(_steps, _chart, jump)) continue;
                companion.Grid = new AreaGridSession(_steps, frontier, chart: _chart, start: jump, facing: grid.State.Facing,
                    actorId: grid.State.Binding.ActorId, capabilities: Capabilities) { Occupancy = grid.Occupancy };
                companion.Jumps++;
                continue;
            }
            var drive = companion.Follow.Drive(grid.State, frontier, (until, batches) =>
            {
                var advance = grid.Advance(until, batches.ToArray());
                if (advance.Movement.Status != MovementStatus.Completed)
                    throw new InvalidOperationException($"GRID_COMPANION_{advance.Movement.Status}_{advance.Movement.Failure?.Kind}");
                advances.Add((companion, advance));
                return advance.Movement;
            });
            if (!drive.Completed) throw new InvalidOperationException("GRID_COMPANION_" + drive.FailureCode);
        }
        return advances;
    }

    /// <summary>
    /// Records every companion's committed cell and facing on this map in the session: a companion back on its T0
    /// cell facing the default clears the change. Stops at the first refusal and returns it (None when all applied).
    /// </summary>
    public GameSessionFailure Record(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var companion in _companions)
        {
            var state = companion.Grid.State;
            var key = AreaCompanionPlacement.Key(companion.Entity);
            var world = _chart.ToWorld(state.CommittedCell);
            var result = state.CommittedCell == companion.Origin && state.Facing == NeighborDirection.XPlus
                ? session.ClearMapChange(session.Version, _map, key)
                : session.SetMapChange(session.Version, _map, key,
                    AreaCompanionPlacement.Encode(new(checked((int)world.X), checked((int)world.Y)), state.Facing));
            if (result.Disposition == GameSessionDisposition.Rejected) return result.Failure;
        }
        return GameSessionFailure.None;
    }

    /// <summary>The companion of a speaker, if it is one.</summary>
    public AreaCompanion? Of(AreaSpeaker speaker) => _companions.FirstOrDefault(c => c.Speaker.Row.Key == speaker.Row.Key);

    /// <summary>The world cell a speaker stands on now: a companion's committed cell, else its T0 cell.</summary>
    public (long X, long Y) WorldCell(AreaSpeaker speaker)
    {
        if (Of(speaker) is not { } companion) return (speaker.WorldX, speaker.WorldY);
        var world = _chart.ToWorld(companion.Grid.State.CommittedCell);
        return (world.X, world.Y);
    }

    /// <summary>
    /// The companions' T0 body commands moved with their live actors: shifted by the projected offset of the continuous
    /// anchor from the T0 cell, ordered at the committed cell. Frame, flip, alpha and lighting key stay the T0 body's.
    /// </summary>
    public IReadOnlyList<AreaDrawCommand> Commands(AreaScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var moved = new List<AreaDrawCommand>();
        foreach (var companion in _companions)
        {
            var state = companion.Grid.State;
            double du = DiagnosticGridActor.Seconds(state.Anchor.U) - companion.Origin.X, dv = DiagnosticGridActor.Seconds(state.Anchor.V) - companion.Origin.Y;
            int px = (int)Math.Round(40 * (dv - du), MidpointRounding.AwayFromZero), py = (int)Math.Round(20 * (du + dv), MidpointRounding.AwayFromZero);
            var world = _chart.ToWorld(state.CommittedCell);
            foreach (var command in plan.Commands.Where(c => c.Identity == companion.Key))
                moved.Add(command with
                {
                    Destination = new TerrainRect(checked(command.Destination.X + px), checked(command.Destination.Y + py), command.Destination.Width, command.Destination.Height),
                    MapX = checked((int)world.X), MapY = checked((int)world.Y),
                });
        }
        return moved.AsReadOnly();
    }

    public string Status()
    {
        if (_companions.Length == 0) return "companions: none on this map";
        return string.Join("; ", _companions.Select(c =>
        {
            var state = c.Grid.State; var world = _chart.ToWorld(state.CommittedCell);
            var route = c.Follow.Route;
            return $"companion script {c.Speaker.ScriptId} {c.Standing} at ({world.X},{world.Y}) {state.Phase} follow={c.Follow.LastAction}"
                + $" route={route.Status}{(route.StopReason is { } reason ? " " + reason : "")} jumps={c.Jumps}";
        }));
    }

    private static CellCoord Chart(AreaChart chart, long worldX, long worldY) => chart.ToChart(new AreaStepCell(worldX, worldY));

    private static bool Admitted(AreaStepSnapshot steps, AreaChart chart, CellCoord cell) =>
        cell.X is >= AreaGridSession.MinimumChartCell and <= AreaGridSession.MaximumChartCell &&
        cell.Y is >= AreaGridSession.MinimumChartCell and <= AreaGridSession.MaximumChartCell && steps.IsAdmitted(chart.ToWorld(cell));
}
