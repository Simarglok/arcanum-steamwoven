using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// Where a companion stands, kept in the session as one map-local entity change of the NPC (property
/// <see cref="Property"/> on the NPC's map, project-defined): the world cell and facing packed into one value, so a
/// placement is one atomic change. Absent means its serialized T0 cell. Values outside the packing are refused.
/// </summary>
public static class AreaCompanionPlacement
{
    public const string Property = "npc.cell";

    // ARC-DEV-094: one packing for a companion's cell and the place of a walking resident (SessionNpcPlacement).
    public static long Encode(CellCoord world, NeighborDirection facing) => SessionNpcPlacement.Encode(world, facing);

    public static (CellCoord World, NeighborDirection Facing)? Decode(long value) => SessionNpcPlacement.Decode(value);

    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);

    /// <summary>
    /// ARC-AI-1622: an NPC the AI host walked before the hero took it into the party stands where the session recorded it (<see cref="GameSessionOwner.QueryNpcPlace"/>, written at each
    /// step the host commits), not on its map row's T0 cell, which is where a group built from the row would start it. Records that place as the companion's cell (the group starts a
    /// companion on it) when <paramref name="map"/> is where the NPC stands, no companion cell is recorded for it yet and a place is. Returns the session's failure, None otherwise.
    /// </summary>
    public static GameSessionFailure SeedFromNpcPlace(GameSessionOwner session, AreaCompanionTemplate template, SessionMapId map)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(template);
        var where = Where(session, template.Home, template.Entity);
        if (where.Error is not null || where.Map != map || where.Placement is not null) return GameSessionFailure.None;
        if (session.QueryNpcPlace(template.Entity) is not { } place || place.Map != map) return GameSessionFailure.None;
        var result = session.ApplyChanges(session.Version, null, Edits(template, map, place.World, place.Facing));
        return result.Disposition == GameSessionDisposition.Rejected ? result.Failure : GameSessionFailure.None;
    }

    /// <summary>ARC-WORLD-017: on the NPC's home map, the runtime map it stands on while it is elsewhere (absent = home).</summary>
    public const string MapProperty = Arcanum.World.EntityHomes.AwayProperty;

    public static MapChangeKey MapKey(string entity) => MapChangeKey.ForEntity(entity, MapProperty);

    /// <summary>
    /// ARC-DEV-079 (MIG4): on the NPC's home map, the world cell its stored object lies on (the T0 cell the home map's data lists it at),
    /// written with every recorded placement (every placement but the stored cell facing the default) and cleared with the one that equals it. The original's object carries
    /// its own record wherever it stands (FM3); here the record is read from the home map's data, and a home that opens only as a window
    /// (the main world map) needs the sector to read, which nothing else keeps once the NPC has left (the T0 cell is not a change).
    /// </summary>
    public const string HomeProperty = "npc.home";

    public static MapChangeKey HomeKey(string entity) => MapChangeKey.ForEntity(entity, HomeProperty);

    /// <summary>The recorded stored cell of <paramref name="entity"/> on its home map, or null (none recorded, or unreadable).</summary>
    public static CellCoord? HomeCell(GameSessionOwner session, SessionMapId home, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.QueryMapChange(home, HomeKey(entity)) is { } value && Decode(value) is { } placed ? placed.World : null;
    }

    /// <summary>The recorded stored cells of the NPCs of <paramref name="home"/>, by entity (an unreadable value is left out).</summary>
    public static IReadOnlyDictionary<string, CellCoord> HomeCells(GameSessionOwner session, SessionMapId home)
    {
        ArgumentNullException.ThrowIfNull(session);
        var cells = new SortedDictionary<string, CellCoord>(StringComparer.Ordinal);
        foreach (var change in session.QueryMapChanges(home))
            if (change.Key.Scope == MapChangeScope.Entity && change.Key.Property == HomeProperty && change.Key.Entity is { } entity && Decode(change.Value) is { } placed)
                cells[entity] = placed.World;
        return cells;
    }

    /// <summary>
    /// Where a companion stands (ARC-WORLD-017, project policy): on its home map unless the home map's
    /// <see cref="MapProperty"/> names another runtime map, and there on that map's <see cref="Property"/> (absent on the
    /// home map = its T0 cell; required elsewhere). An unreadable value is an error, never a guess.
    /// </summary>
    public static AreaCompanionWhere Where(GameSessionOwner session, SessionMapId home, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = home;
        if (session.QueryMapChange(home, MapKey(entity)) is { } away)
        {
            if (away is < 1 or > int.MaxValue || away == home.RuntimeMap) return new(home, null, "map unreadable");
            map = new((int)away);
        }
        if (session.QueryMapChange(map, Key(entity)) is not { } value)
            return map == home ? new(map, null, null) : new(map, null, "placement missing on map " + map.RuntimeMap);
        return Decode(value) is { } placed ? new(map, placed, null) : new(map, null, "placement unreadable");
    }

    /// <summary>The changes that record a companion standing on <paramref name="map"/> at <paramref name="world"/>: the placement
    /// (<see cref="Edit"/>) and the stored cell of its home map (<see cref="HomeProperty"/>), which is written whenever the placement is
    /// written and cleared when it is cleared (ARC-DEV-079).</summary>
    public static IReadOnlyList<MapChangeEdit> Edits(AreaCompanionTemplate template, SessionMapId map, CellCoord world, NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(template);
        var edit = Edit(template, map, world, facing);
        // ARC-MAGIC-050: a created critter has no home map data to point at: the session holds it on the map it stands on.
        if (template.Created) return [edit];
        return [edit, new(template.Home, HomeKey(template.Entity), edit.Value is null ? null : Encode(template.HomeWorld, NeighborDirection.XPlus))];
    }

    /// <summary>The change that records a companion standing on <paramref name="map"/> at <paramref name="world"/>: on its
    /// home map, its T0 cell facing the default clears the key; anywhere else the cell is always written.</summary>
    public static MapChangeEdit Edit(AreaCompanionTemplate template, SessionMapId map, CellCoord world, NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Edit(template.Entity, template.Home, template.HomeWorld, map, world, facing);
    }

    /// <summary>ARC-WORLD-2161: <see cref="Edit(AreaCompanionTemplate, SessionMapId, CellCoord, NeighborDirection)"/> for an NPC known by its entity, its home map and its stored cell there (no body template).</summary>
    public static MapChangeEdit Edit(string entity, SessionMapId home, CellCoord homeWorld, SessionMapId map, CellCoord world, NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return map == home && world == homeWorld && facing == NeighborDirection.XPlus
            ? new(map, Key(entity), null)
            : new(map, Key(entity), Encode(world, facing));
    }

    /// <summary>ARC-WORLD-2161: <see cref="Edits(AreaCompanionTemplate, SessionMapId, CellCoord, NeighborDirection)"/> for an NPC known by its entity, its home map and its stored cell there.</summary>
    public static IReadOnlyList<MapChangeEdit> Edits(string entity, SessionMapId home, CellCoord homeWorld, SessionMapId map, CellCoord world, NeighborDirection facing)
    {
        var edit = Edit(entity, home, homeWorld, map, world, facing);
        return [edit, new(home, HomeKey(entity), edit.Value is null ? null : Encode(homeWorld, NeighborDirection.XPlus))];
    }
}

/// <summary>A companion standing on a map whose body the map's own scene does not draw: its identity, the map whose stored data holds
/// it (<see cref="HomeMap"/>) and the stored cell recorded for it there (<see cref="HomeCell"/>, null for an NPC that left before it
/// was recorded).</summary>
public readonly record struct AreaVisitor(string Entity, int HomeMap, CellCoord? HomeCell);

/// <summary>
/// ARC-DEV-079 (MIG4, TM3): the companions that stand on <c>map</c> (a recorded cell, <see cref="AreaCompanionPlacement.Property"/>) and
/// have no body among <c>templates</c>, the bodies of the map's own scene (ARC-WORLD-048 named them as not talkable for want of a kept
/// body; since this slice their body is read from their home map by <see cref="AreaHomeDirectory"/>). A cell is recorded on a map other
/// than the home map only while the companion stands there (the transfer clears it on the map it leaves); <c>shown</c> keeps only cells the
/// viewer has loaded (on the main map, a companion recorded in another window is not "here", whatever its home). The home map is the one
/// that carries the NPC's away change (<see cref="Arcanum.World.EntityHomes"/>), else the map itself (a companion of another window of
/// it); the stored cell is the one recorded on that map.
/// </summary>
public static class AreaVisitors
{
    public static IReadOnlyList<AreaVisitor> Here(GameSessionOwner session, SessionMapId map, IEnumerable<AreaCompanionTemplate> templates,
        Func<CellCoord, bool>? shown = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(templates);
        var known = templates.Select(t => t.Entity).ToHashSet(StringComparer.Ordinal);
        return session.QueryMapChanges(map)
            .Where(c => c.Key.Scope == MapChangeScope.Entity && c.Key.Property == AreaCompanionPlacement.Property && c.Key.Entity is { } e && !known.Contains(e)
                && (shown is null || (AreaCompanionPlacement.Decode(c.Value) is { } placed && shown(placed.World))))
            .Select(c => c.Key.Entity!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(entity =>
            {
                var home = Arcanum.World.EntityHomes.HomeOf(session, entity) ?? map.RuntimeMap;
                return new AreaVisitor(entity, home, AreaCompanionPlacement.HomeCell(session, new(home), entity));
            }).ToArray();
    }

    /// <summary>The stored cell to read <paramref name="map"/>'s window around when the map opens only as a window and the viewer holds
    /// none of it: the lowest-entity NPC of that map that stands on another map now (its away change, <c>npc.map</c>: the companions the
    /// resolver is asked about), else the lowest-entity NPC with a recorded stored cell (a follower waiting elsewhere on its own map).
    /// The resolver holds one window per map: NOT READY for companions away from two windows of one map, whose second is kept out of the
    /// rules until MIG9.</summary>
    public static CellCoord? HomeWindow(GameSessionOwner session, int map)
    {
        ArgumentNullException.ThrowIfNull(session);
        var cells = AreaCompanionPlacement.HomeCells(session, new(map));
        foreach (var (entity, cell) in cells)
            if (session.QueryMapChange(new(map), AreaCompanionPlacement.MapKey(entity)) is not null) return cell;
        return cells.Count == 0 ? null : cells.First().Value;
    }
}

/// <summary>A companion's map and recorded placement there (null = its T0 cell on its home map), or why it cannot be read.</summary>
public readonly record struct AreaCompanionWhere(SessionMapId Map, (CellCoord World, NeighborDirection Facing)? Placement, string? Error);

/// <summary>One companion of the shown map: its own authoritative grid session and follow goal.</summary>
public sealed class AreaCompanion
{
    internal AreaCompanion(AreaCompanionTemplate template, AreaGridSession grid, CompanionFollower follow)
    {
        Template = template; Grid = grid; Follow = follow;
    }

    public AreaCompanionTemplate Template { get; }
    public AreaSpeaker Speaker => Template.Speaker;
    public string Entity => Template.Entity;
    public AreaCompanionFacts Facts => Template.Facts;
    public AreaObjectKey Key => Template.Key;
    /// <summary>The serialized T0 chart cell of the NPC on its home map.</summary>
    public CellCoord Origin => Template.HomeOrigin;
    public AreaGridSession Grid { get; internal set; }
    public CompanionFollower Follow { get; }
    public CompanionStanding Standing { get; internal set; }
    public CompanionFollowUpdate LastUpdate { get; internal set; }
    public int Jumps { get; internal set; }
    /// <summary>ARC-WORLD-045: whether the companion's follow goal lives (the original's goal starts when the leader is beyond the
    /// start distance and is never ended by the goal itself, OL19); it ends when the companion stops following, a conversation
    /// with it starts, or the group is rebuilt (a map change or a load). The loyalty step runs only without it (decision D5).</summary>
    public bool FollowGoal { get; internal set; }
    /// <summary>ARC-WORLD-045: when the companion's next AI tick (and loyalty step) is due, or null before its first.</summary>
    public SimulationTime? NextAiTick { get; internal set; }
    /// <summary>ARC-WORLD-075: whether the companion's walk passes living critters, set from the session at each
    /// <see cref="AreaCompanionGroup.Drive"/>: yes while it is not in combat mode. Its follow and walk-order goals plan with the
    /// request's flag 0x10 (the approach walks' path step asks the flag builder with its second argument set, as the PC's walk does,
    /// ledger LR2 and LR10) and its steps are admitted like the PC's since ARC-WORLD-074; false keeps the ordinary overlay.</summary>
    public bool PassesCritters { get; internal set; }
    /// <summary>ARC-WORLD-111: the gait clock of its drawn walk cycle; a rebuilt group starts a fresh one (its body at T0 until it moves).</summary>
    public AreaBodyCycle Cycle { get; } = new();
    /// <summary>ARC-WORLD-115: whether the AI host's last step of it runs (its gait while the host holds it).</summary>
    public bool AiRunning { get; internal set; }
    /// <summary>ARC-RENDER-2539: whether the step the AI host gave was the stealth walk (a concealed follower below prowling expert, 0x42EDC0). ARC-RENDER-2564: for a companion the host does not hold it is set at each
    /// <see cref="AreaCompanionGroup.Drive"/> from the follow decision's own walk (the group's <see cref="AreaCompanionGroup.ProwlingTraining"/> and the session's flag).</summary>
    public bool AiSneaking { get; internal set; }
    /// <summary>ARC-RENDER-2564 (0x42EDC0): the concealment flag is set and the follow decision's run is not permitted (prowling training below master: it walks), set at each <see cref="AreaCompanionGroup.Drive"/>.</summary>
    public bool RunRefused { get; internal set; }
}

/// <summary>
/// ARC-WORLD-016: the measured companions of one AREA map as moving actors. Each one walks on its own
/// <see cref="AreaGridSession"/> over the map's T0 step evidence with the live overlay of <see cref="AreaOccupancy"/>:
/// its T0 record no longer blocks, and the PC and the other companions block where they stand (ARC-WORLD-075: only while the
/// companion is in combat mode; out of it its plan and steps pass them and T0 critters, <see cref="AreaCompanion.PassesCritters"/>). While the session
/// party says it follows the PC, <see cref="CompanionFollower"/> drives it after the PC at each frame boundary; a
/// waiting or free companion stands where it stopped. The PC is admitted against the companions in the same way.
/// Placement lives in the session (<see cref="AreaCompanionPlacement"/>) only when the host records it (quick save,
/// leaving the map), like the PC's. The drawn body is the NPC's T0 frame moved with the actor; ARC-WORLD-111: the viewer
/// draws its walk or run cycle and its facing on it (<see cref="AreaBodyMoveArt"/>). ARC-WORLD-017: a companion stands on the map the session names (<see cref="AreaCompanionPlacement.Where"/>),
/// so a group holds the companions of every home map standing here (visitors too) and lists the home companions standing
/// elsewhere as <see cref="Absent"/>; <see cref="Carry"/> gives the session changes of a map transfer. Caller-serialized;
/// no Godot.
/// </summary>
public sealed class AreaCompanionGroup
{
    public const string ActorPrefix = "AREA_COMPANION_";
    public const string Capabilities = "AREA_NPC_T0_BODY_FOLLOWER_V1";
    private readonly AreaStepSnapshot _steps;
    private readonly AreaChart _chart;
    private readonly SessionMapId _map;
    private readonly AreaCompanion[] _companions;
    private readonly HashSet<AreaObjectKey> _absent;
    private readonly IReadOnlyList<string> _away;

    private AreaCompanionGroup(AreaStepSnapshot steps, AreaChart chart, SessionMapId map, AreaCompanion[] companions, HashSet<AreaObjectKey> absent,
        IReadOnlyList<string> away)
    {
        _steps = steps; _chart = chart; _map = map; _companions = companions; _absent = absent; _away = away;
    }

    public IReadOnlyList<AreaCompanion> Companions => _companions;
    public SessionMapId Map => _map;
    /// <summary>Companions whose home is this map and who stand on another one, or whose recorded placement here cannot
    /// be read: their T0 record is not in the world here (not drawn, picked, talked to or blocking).</summary>
    public IReadOnlySet<AreaObjectKey> Absent => _absent;
    /// <summary>Companions the session places here that could not stand on this map's chart (cell not admitted, outside the
    /// chart, unreadable or missing placement).</summary>
    public IReadOnlyList<string> Skipped { get; private init; } = [];

    /// <summary>
    /// The companions of <paramref name="templates"/> the session places on map <paramref name="map"/>, each on its
    /// recorded placement there, or on its T0 cell on its home map when none is recorded.
    /// </summary>
    /// <param name="timing">ARC-WORLD-020: per-gait step durations; a companion runs while its follow decision is a run
    /// (research ARC-RES-047 L11). Null keeps the fixed step duration.</param>
    public static AreaCompanionGroup Create(IEnumerable<AreaCompanionTemplate> templates, AreaStepSnapshot steps, AreaChart chart,
        GameSessionOwner session, SessionMapId map, SimulationTime time, GaitTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(templates); ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(chart); ArgumentNullException.ThrowIfNull(session);
        var list = new List<AreaCompanion>(); var skipped = new List<string>(); var absent = new HashSet<AreaObjectKey>(); var away = new List<string>();
        AreaCompanionGroup? group = null;
        foreach (var template in templates)
        {
            var entity = template.Entity;
            var where = AreaCompanionPlacement.Where(session, template.Home, entity);
            if (where.Map != map)
            {
                // A home companion away is not in this map's world.
                if (map == template.Home) { absent.Add(template.Key); away.Add($"script {template.Speaker.ScriptId} on map {where.Map.RuntimeMap}"); }
                continue;
            }
            if (where.Error is { } error)
            {
                // Reported, never guessed: on its home map its T0 body does not stand in for an unreadable placement.
                skipped.Add(entity + " " + error);
                if (map == template.Home) absent.Add(template.Key);
                continue;
            }
            var start = template.HomeOrigin; var facing = NeighborDirection.XPlus;
            if (where.Placement is { } placed) { start = Chart(chart, placed.World.X, placed.World.Y); facing = placed.Facing; }
            if (!Admitted(steps, chart, start)) { skipped.Add(entity + " cell not admitted"); continue; }
            var index = list.Count;
            // Every companion and route source reads the group's live overlay at assessment time.
            AreaOccupancy Admission() => group!.WithExtra(group.OccupancyFor(index, leader: true));
            AreaOccupancy Planning() => group!.WithExtra(group.OccupancyFor(index, leader: false));
            // ARC-WORLD-075: its plan and its steps pass living critters while it is not in combat mode (read at each drive).
            bool Passes() => group!._companions[index].PassesCritters;
            var grid = new AreaGridSession(steps, time, chart: chart, start: start, facing: facing,
                actorId: ActorPrefix + template.Speaker.Index, capabilities: Capabilities) { Occupancy = Admission, CrittersPass = Passes, Timing = timing };
            var source = new AreaRouteSource(steps, chart, Planning, crittersPass: Passes, dead: key => group!.Dead?.Invoke(key) == true);
            var companion = new AreaCompanion(template, grid, new CompanionFollower(source, segmentEndCritterCheck: () => group!._segmentEndCritter));
            // ARC-RENDER-2564 (0x42EDC0): a concealed follower below prowling expert steps the stealth walk (a run wanted ignored), one below master never runs; the flags come from the AI host's step or, for a companion it does not hold, from the group's drive.
            grid.Gait = () => companion.AiSneaking ? MovementGait.Sneak : companion.LastUpdate.Action == FollowAction.Run && !companion.RunRefused ? MovementGait.Run : MovementGait.Walk;
            // ARC-MAGIC-1189 (ledger TT10, SP2): the walk check before each of its steps (0x427110): a trap the companion spots on the next tile refuses the step.
            var entityId = companion.Entity;
            // A companion the AI host holds takes the host's steps, whose own walk check (AreaAiHost.StepBlocked) already rolled for this step.
            grid.StepCharge = (key, _) => group!.HeldByAi?.Invoke(entityId) != true && group.StepTrap?.Invoke(entityId, WorldCell(chart, key.To)) == true ? TrapSpottedReason : null;
            list.Add(companion);
        }
        group = new(steps, chart, map, list.ToArray(), absent, away.AsReadOnly()) { Skipped = skipped.AsReadOnly() };
        group.Refresh(session);
        return group;
    }

    /// <summary>ARC-WORLD-065: the T0 records of the objects the session destroyed or switched off (read at every overlay; the same
    /// set while nothing changed); every overlay moves them. Null is none.</summary>
    public Func<IReadOnlySet<AreaObjectKey>>? Removed { get; set; }

    /// <summary>ARC-MAGIC-1189: the reason a companion's step gives when it spots a trap on its next tile (<see cref="StepTrap"/>).</summary>
    public const string TrapSpottedReason = "TRAP_SPOTTED";

    /// <summary>ARC-MAGIC-1189 (ledger TT10, SP2): the walk check of a companion's step: the companion's session entity and the world cell of its next tile in, whether it spots an unknown
    /// trap there out (the step is then refused; the companion marks it, so the player knows it). Null: no check, as before.</summary>
    public Func<string, CellCoord, bool>? StepTrap { get; set; }

    private static CellCoord WorldCell(AreaChart chart, CellCoord chartCell)
    {
        var cell = chart.ToWorld(chartCell);
        return new(checked((int)cell.X), checked((int)cell.Y));
    }

    /// <summary>ARC-WORLD-075: whether a T0 critter record is dead in the session, for the critter checks of a companion's walk that
    /// passes critters (a living critter on its goal, DC2, or before a segment's last step, LR5). Null is none dead.</summary>
    public Func<AreaObjectKey, bool>? Dead { get; set; }

    /// <summary>ARC-WORLD-115: what the host adds to every companion's overlay (the AI's moved NPCs: their T0 records away, their live
    /// cells held); null adds nothing.</summary>
    public Func<AreaOccupancy, AreaOccupancy>? Extra { get; set; }

    private AreaOccupancy WithExtra(AreaOccupancy occupancy) => Extra?.Invoke(occupancy) ?? occupancy;

    /// <summary>ARC-WORLD-115: whether the AI host holds a companion (by entity) while it fights: its follow decisions are not made, its
    /// walk goes only where the host's steps send it (<see cref="StepTo"/>). Null is none held.</summary>
    public Func<string, bool>? HeldByAi { get; set; }

    /// <summary>ARC-RENDER-2564 (0x42EDC0, 0x4C60C0 with skill 6): the prowling training of a companion's own sheet, read for a concealed companion the AI host does not hold (its follow decision's walk is then the stealth
    /// walk below expert, and never a run below master); no sheet reads as untrained. Null: untrained for every companion.</summary>
    public Func<string, SkillTraining>? ProwlingTraining { get; set; }

    /// <summary>
    /// ARC-WORLD-115: the AI host's step of the companion <paramref name="entity"/> onto the neighbouring chart cell
    /// <paramref name="next"/>: its walk's ordered goal to that cell (<see cref="CompanionFollower.WalkTo"/>, observed at its grid's
    /// time), driven by <see cref="Drive"/>. False for a companion not here or a cell outside the step evidence.
    /// </summary>
    public bool StepTo(string entity, CellCoord next, bool running, bool sneaking = false)
    {
        if (_companions.FirstOrDefault(c => c.Entity == entity) is not { } companion) return false;
        companion.AiRunning = running;
        companion.AiSneaking = sneaking && !running;
        companion.RunRefused = false;
        return companion.Follow.WalkTo(next, companion.Grid.State.Time);
    }

    /// <summary>ARC-WORLD-1582 (ledger TT17): the companion's own walk ends on the tile of a trap object it just stepped onto: its goal stops (a started step still ends at its cell centre)
    /// and the follow goal plans again from there. False for a companion not here or one with no goal running.</summary>
    public bool EndWalkOnTrap(string entity) =>
        _companions.FirstOrDefault(c => c.Entity == entity) is { } companion && companion.Follow.EndWalkOnTrap(companion.Grid.State.Time);

    /// <summary>ARC-WORLD-115: the AI host's turn of a settled companion toward the chart cell <paramref name="toward"/> (its grid stands
    /// again on its cell with the new facing; a companion mid-step keeps its facing).</summary>
    public void FaceTo(string entity, CellCoord toward)
    {
        if (_companions.FirstOrDefault(c => c.Entity == entity) is not { } companion) return;
        var grid = companion.Grid;
        var state = grid.State;
        if (state.Phase != MovementPhase.Settled || state.CommittedCell == toward) return;
        var facing = RoutePlanner.Toward(state.CommittedCell, toward);
        if (facing == state.Facing) return;
        companion.Grid = new AreaGridSession(_steps, state.Time, chart: _chart, start: state.CommittedCell, facing: facing,
            actorId: state.Binding.ActorId, capabilities: Capabilities)
            { Occupancy = grid.Occupancy, CrittersPass = grid.CrittersPass, Timing = grid.Timing, Gait = grid.Gait, StepCharge = grid.StepCharge };
    }

    /// <summary>ARC-DEV-105 (MIG9 S5, the follower catch-up 0x4AD4D0, which ends in the move 0x43E770): the AI host's move of the companion
    /// <paramref name="entity"/> onto the chart cell <paramref name="cell"/> (the PC's cell): its grid stands settled there at its own time with
    /// its facing, as a follow jump does. False for a companion not here or a cell outside the step evidence.</summary>
    public bool JumpTo(string entity, CellCoord cell)
    {
        if (_companions.FirstOrDefault(c => c.Entity == entity) is not { } companion || !Admitted(_steps, _chart, cell)) return false;
        var grid = companion.Grid;
        companion.Follow.Interrupt(grid.State.Time);
        companion.Grid = new AreaGridSession(_steps, grid.State.Time, chart: _chart, start: cell, facing: grid.State.Facing,
            actorId: grid.State.Binding.ActorId, capabilities: Capabilities)
            { Occupancy = grid.Occupancy, CrittersPass = grid.CrittersPass, Timing = grid.Timing, Gait = grid.Gait, StepCharge = grid.StepCharge };
        companion.Jumps++;
        return true;
    }

    // ARC-WORLD-075: the session's rule switch SegmentEndCritterEndsWalk, read at each drive.
    private bool _segmentEndCritter;

    /// <summary>The PC's overlay: every companion's T0 record is moved (here or away), and the companions hold their live cells;
    /// removed objects are moved too.</summary>
    public AreaOccupancy OccupancyForPc() =>
        _companions.Length == 0 && _absent.Count == 0 && Removed?.Invoke() is not { Count: > 0 } ? AreaOccupancy.None : Cached(-1, true);

    /// <summary>The PC's live cells, set by the host after each PC advance (committed cell and a started step's cell).</summary>
    public IReadOnlyList<CellCoord> Pc { get; private set; } = [];

    /// <summary>Companion <paramref name="index"/>'s overlay: the PC (unless <paramref name="leader"/> is false, for
    /// planning toward it) and the other companions block; every companion's T0 record is moved.</summary>
    private AreaOccupancy OccupancyFor(int index, bool leader) => Cached(index, leader);

    // A planner asks for the overlay at every step it assesses: one value per (mover, PC cells, companion states) is
    // reused while none of them changed (states are immutable and replaced by each advance).
    private readonly Dictionary<(int Index, bool Leader), (object[] Inputs, AreaOccupancy Value)> _cache = [];
    private static readonly IReadOnlySet<AreaObjectKey> NoneRemoved = new HashSet<AreaObjectKey>();

    private AreaOccupancy Cached(int index, bool leader)
    {
        var removed = Removed?.Invoke() ?? NoneRemoved;
        var inputs = new object[_companions.Length + 2];
        inputs[0] = Pc;
        inputs[1] = removed;
        for (var i = 0; i < _companions.Length; i++) inputs[i + 2] = (object?)_companions[i].Grid?.State ?? this;
        if (_cache.TryGetValue((index, leader), out var known) && known.Inputs.Length == inputs.Length
            && known.Inputs.Zip(inputs).All(pair => ReferenceEquals(pair.First, pair.Second))) return known.Value;
        var held = new List<CellCoord>();
        if (leader && index >= 0) held.AddRange(Pc);
        for (var i = 0; i < _companions.Length; i++)
            if (i != index && _companions[i].Grid is { } other) held.AddRange(Held(other.State));
        var value = new AreaOccupancy(_companions.Select(c => c.Key).Concat(_absent).Concat(removed), held);
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

    /// <summary>ARC-WORLD-045: a conversation with the companion interrupts its goals, the follow goal too (OL19).</summary>
    public void InterruptGoals(AreaObjectKey key)
    {
        foreach (var companion in _companions.Where(c => c.Key == key)) companion.FollowGoal = false;
    }

    /// <summary>
    /// ARC-WORLD-045: the AI ticks due at <paramref name="now"/> (the PC stands as <paramref name="pc"/>). A following companion's
    /// tick runs the session's loyalty step (<see cref="GameSessionOwner.RunLoyaltyStep"/>) with the facts the host gives (none:
    /// no step) and whether its follow goal lives, then schedules its next tick by its distance to the PC
    /// (<see cref="AreaCompanionLoyalty.AiTickDelayMs"/>); a waiting or free companion has no tick. Returns each step's result.
    /// </summary>
    public IReadOnlyList<(AreaCompanion Companion, SessionLoyaltyResult Result)> Loyalty(GameSessionOwner session, MovementState pc, SimulationTime now,
        Func<AreaCompanion, PartyConsentFacts?> facts)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(pc); ArgumentNullException.ThrowIfNull(facts);
        var results = new List<(AreaCompanion, SessionLoyaltyResult)>();
        foreach (var companion in _companions)
        {
            companion.Standing = session.QueryCompanion(companion.Entity).Standing;
            if (companion.Standing != CompanionStanding.Following) { companion.FollowGoal = false; companion.NextAiTick = null; continue; }
            // A host clock that went back (a fresh actor session) leaves a tick further away than any delay: it is due now.
            if (companion.NextAiTick is { } due && now.Value < due.Value &&
                due.Value <= now.Value + ExactScalar.Create(AreaCompanionLoyalty.MaxAiTickDelayMs, 1000)) continue;
            var delay = AreaCompanionLoyalty.AiTickDelayMs(FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc.CommittedCell));
            companion.NextAiTick = new SimulationTime(now.Value + ExactScalar.Create(delay, 1000));
            if (facts(companion) is not { } stepFacts) continue;
            var result = session.RunLoyaltyStep(session.Version, companion.Entity, stepFacts, companion.FollowGoal);
            if (result.Step is { Kind: LoyaltyStepKind.Left }) { companion.Standing = CompanionStanding.Free; companion.FollowGoal = false; }
            results.Add((companion, result));
        }
        return results;
    }

    /// <summary>
    /// One frame: the PC stands as <paramref name="pc"/> after its advance to <paramref name="frontier"/>; each
    /// following companion decides and drives to the same boundary. A jump stands it settled on the PC's cell (the
    /// next decision steps it off). Returns every companion advance's results in order (for logging).
    /// </summary>
    /// <param name="heldOnly">ARC-WORLD-115: drive only the companions the AI host holds (a turn-based round: a companion moves only on
    /// its own subturn, by its AI).</param>
    public IReadOnlyList<(AreaCompanion Companion, GridAdvance Advance)> Drive(GameSessionOwner session, MovementState pc, SimulationTime frontier,
        bool heldOnly = false)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(pc);
        Pc = Held(pc).ToArray();
        _segmentEndCritter = session.Rules.Navigation.SegmentEndCritterEndsWalk;
        var advances = new List<(AreaCompanion, GridAdvance)>();
        foreach (var companion in _companions)
        {
            var record = session.QueryCompanion(companion.Entity);
            companion.Standing = record.Standing;
            companion.PassesCritters = !session.QueryCombatMode(SessionCombatant.Critter(companion.Entity));
            var grid = companion.Grid;
            if (frontier.Value <= grid.State.Time.Value) continue;
            // ARC-WORLD-115: a companion the AI host holds makes no follow decision; its walk drives only the host's steps.
            var held = HeldByAi?.Invoke(companion.Entity) == true;
            if (heldOnly && !held) continue;
            var update = held ? new CompanionFollowUpdate(companion.AiRunning ? FollowAction.Run : FollowAction.Walk, null)
                : FollowDecision(session, companion, record, pc.CommittedCell, frontier);
            companion.LastUpdate = update;
            if (!held)
            {
                // ARC-RENDER-2564 (0x42EDC0): the gait function reads the concealment flag and the prowling training for every critter, the AI host's or not.
                var prowling = session.IsConcealed(SessionCombatant.Critter(companion.Entity));
                var facts = new AiMoverFacts(Prowling: prowling, ProwlingTraining: prowling ? ProwlingTraining?.Invoke(companion.Entity) ?? SkillTraining.None : SkillTraining.None);
                companion.AiSneaking = AiExecutorRules.Sneaks(facts);
                companion.RunRefused = !AiExecutorRules.RunPermitted(facts);
            }
            // ARC-WORLD-045: an approach starts the follow goal, which then lives until something interrupts it.
            // ARC-SCRIPT-1136 (CF9): a follower that carries the wait bit is waiting for the AI (the non-combat perform returns "waiting"), so it follows nobody.
            if (record.Standing != CompanionStanding.Following || record.WaitFlag || held) companion.FollowGoal = false;
            else if (update.Action is FollowAction.Walk or FollowAction.Run or FollowAction.Jump) companion.FollowGoal = true;
            if (update.JumpTo is { } jump)
            {
                if (!Admitted(_steps, _chart, jump)) continue;
                companion.Grid = new AreaGridSession(_steps, frontier, chart: _chart, start: jump, facing: grid.State.Facing,
                    actorId: grid.State.Binding.ActorId, capabilities: Capabilities)
                    { Occupancy = grid.Occupancy, CrittersPass = grid.CrittersPass, Timing = grid.Timing, Gait = grid.Gait, StepCharge = grid.StepCharge };
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
    /// The follow decision of one companion the host does not hold. The hero's follower follows the PC; ARC-WORLD-2132: a map NPC that another critter of the group leads (the session's NPC-led link,
    /// its leader a companion of this group) follows that leader's committed cell, as the original's non-combat perform makes no leader-type test (docs/world/party-runtime.md); one whose leader this group does not hold,
    /// or that waits, holds.
    /// </summary>
    private CompanionFollowUpdate FollowDecision(GameSessionOwner session, AreaCompanion companion, SessionCompanion record, CellCoord pc, SimulationTime frontier)
    {
        if (session.QueryNpcLink(companion.Entity) is { } link)
        {
            var leader = _companions.FirstOrDefault(c => c.Entity == link.Leader);
            return leader is null || record.WaitFlag
                ? companion.Follow.Update(companion.Grid.State, pc, record.SpreadOut, false, frontier)
                : companion.Follow.Update(companion.Grid.State, leader.Grid.State.CommittedCell, record.SpreadOut, true, frontier);
        }
        return companion.Follow.Update(companion.Grid.State, pc, record.SpreadOut, record.Standing == CompanionStanding.Following && !record.WaitFlag, frontier);
    }

    /// <summary>
    /// Records every companion's committed cell and facing on this map in the session, in one atomic step: a companion
    /// back on its home T0 cell facing the default clears the change. Returns the refusal (None when applied).
    /// </summary>
    public GameSessionFailure Record(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-MAGIC-050: a created critter the session destroyed since the group was built has no placement to record.
        var result = session.ApplyChanges(session.Version, null, _companions.Where(c => !c.Template.Created || session.QueryCreatedObject(c.Entity) is not null).SelectMany(Stay).ToArray());
        return result.Disposition == GameSessionDisposition.Rejected ? result.Failure : GameSessionFailure.None;
    }

    private IReadOnlyList<MapChangeEdit> Stay(AreaCompanion companion)
    {
        var state = companion.Grid.State; var world = _chart.ToWorld(state.CommittedCell);
        return AreaCompanionPlacement.Edits(companion.Template, _map, new(checked((int)world.X), checked((int)world.Y)), state.Facing);
    }

    /// <summary>
    /// ARC-WORLD-017 (research C15, C16, V4): the session changes of the PC leaving this map for
    /// <paramref name="destination"/> at the world cell <paramref name="arrival"/>. Every companion the session party says
    /// follows the PC goes with it, at any distance, onto the same arrival cell with its own facing (the next follow
    /// decision on the destination steps it off the shared cell); its home map names the destination, or forgets the away
    /// map when the destination is home. Every other companion (waiting, free, or ordered to wait by the change of place: ARC-DEV-073, the
    /// unconscious ones in <paramref name="facts"/>) stays where it stands on this map. The host applies the changes together with the
    /// PC's placement and the returned move list in one <see cref="GameSessionOwner.ApplyChanges"/>.
    /// </summary>
    public (IReadOnlyList<MapChangeEdit> Changes, IReadOnlyList<AreaCompanion> Carried, SessionMoveList MoveList) Carry(GameSessionOwner session,
        SessionMapId destination, CellCoord arrival, SessionTransferFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (destination == _map) throw new ArgumentException("AREA_COMPANION_CARRY_SAME_MAP", nameof(destination));
        // ARC-DEV-073 (MIG10): the session's move list says who travels and who is ordered to wait (an unconscious follower stays).
        var moveList = session.QueryTransferMoveList(facts ?? new SessionTransferFacts());
        var (changes, carried) = Carry(destination, arrival, moveList);
        return (changes, carried, moveList);
    }

    /// <summary>ARC-DEV-113: <see cref="Carry(GameSessionOwner, SessionMapId, CellCoord, SessionTransferFacts?)"/> for the move list the
    /// session's change of place built: the edits of the companions that travel onto the arrival and of those that stay.</summary>
    public (IReadOnlyList<MapChangeEdit> Changes, IReadOnlyList<AreaCompanion> Carried) Carry(SessionMapId destination, CellCoord arrival,
        SessionMoveList moveList)
    {
        ArgumentNullException.ThrowIfNull(moveList);
        if (destination == _map) throw new ArgumentException("AREA_COMPANION_CARRY_SAME_MAP", nameof(destination));
        var travelling = moveList.Travelling.ToHashSet(StringComparer.Ordinal);
        var changes = new List<MapChangeEdit>(); var carried = new List<AreaCompanion>();
        foreach (var companion in _companions)
        {
            if (!travelling.Contains(companion.Entity)) { changes.AddRange(Stay(companion)); continue; }
            changes.AddRange(Arrive(companion.Template, _map, destination, arrival, companion.Grid.State.Facing));
            carried.Add(companion);
        }
        return (changes.AsReadOnly(), carried.AsReadOnly());
    }

    /// <summary>
    /// ARC-WORLD-037: the session changes that stand a companion that follows the PC from map <paramref name="from"/> on
    /// <paramref name="destination"/> at the world cell <paramref name="arrival"/> (as <see cref="Carry"/> does for the
    /// group): its placement on the map it leaves is cleared, its cell on the destination written, and its home map names
    /// the destination (or forgets the away map when the destination is home). On the same map only the cell is written.
    /// ARC-DEV-351 (RL22, TG9): on another map the object also gets the original's OF_TELEPORTED mark (<see cref="SessionTeleportedMark"/>) on the cell it is
    /// written on, which the open of that map takes off to notify it (a follower that travels with the PC: at once; a former follower sent home alone: when its
    /// home map opens).
    /// </summary>
    public static IReadOnlyList<MapChangeEdit> Arrive(AreaCompanionTemplate template, SessionMapId from, SessionMapId destination, CellCoord arrival,
        NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (from == destination) return AreaCompanionPlacement.Edits(template, destination, arrival, facing);
        // ARC-MAGIC-050: a created critter moves whole with the session's change of place (the session records it on the destination); no away change names its home.
        if (template.Created)
            return
            [
                new(from, AreaCompanionPlacement.Key(template.Entity), null),
                new(destination, AreaCompanionPlacement.Key(template.Entity), AreaCompanionPlacement.Encode(arrival, facing)),
                new(destination, SessionTeleportedMark.Key(template.Entity), SessionNpcPlacement.Fits(arrival, facing) ? SessionNpcPlacement.Encode(arrival, facing) : null),
            ];
        return Arrive(template.Entity, template.Home, template.HomeWorld, from, destination, arrival, facing);
    }

    /// <summary>
    /// ARC-WORLD-2161: <see cref="Arrive(AreaCompanionTemplate, SessionMapId, SessionMapId, CellCoord, NeighborDirection)"/> for a follower with no body here (one whose follow facts do not read, or whose home map could not give a body): the original's
    /// move list carries the whole object, so the session's placement edits stand it on the destination all the same; the body is read from its home map when that map shows it (a visitor).
    /// </summary>
    public static IReadOnlyList<MapChangeEdit> Arrive(AreaBodylessFollower follower, SessionMapId from, SessionMapId destination, CellCoord arrival, NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(follower);
        return from == destination
            ? AreaCompanionPlacement.Edits(follower.Entity, follower.Home, follower.HomeWorld, destination, arrival, facing)
            : Arrive(follower.Entity, follower.Home, follower.HomeWorld, from, destination, arrival, facing);
    }

    /// <summary>ARC-WORLD-2039: the edits of <see cref="Arrive(AreaCompanionTemplate, SessionMapId, SessionMapId, CellCoord, NeighborDirection)"/> for an NPC known by its entity, its home map and its stored cell there (a free NPC a script's teleport sends to another map).</summary>
    public static IReadOnlyList<MapChangeEdit> Arrive(string entity, SessionMapId home, CellCoord homeWorld, SessionMapId from, SessionMapId destination, CellCoord arrival, NeighborDirection facing) =>
    [
        new(from, AreaCompanionPlacement.Key(entity), null),
        .. AreaCompanionPlacement.Edits(entity, home, homeWorld, destination, arrival, facing),
        new(home, AreaCompanionPlacement.MapKey(entity), destination == home ? null : destination.RuntimeMap),
        new(destination, SessionTeleportedMark.Key(entity), SessionNpcPlacement.Fits(arrival, facing) ? SessionNpcPlacement.Encode(arrival, facing) : null),
    ];

    /// <summary>The companions the session party says follow the PC, with the facing each stands with now.</summary>
    public IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)> Following(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _companions.Where(c => session.QueryCompanion(c.Entity).Standing == CompanionStanding.Following)
            .Select(c => (c.Template, c.Grid.State.Facing)).ToArray();
    }

    /// <summary>
    /// ARC-DEV-073 (MIG10): the companions that leave this map with the PC, with the facing each stands with now, and the session's move
    /// list for the change of place (the unconscious followers are ordered to wait and stay; <see cref="Following"/> without the wait
    /// orders).
    /// </summary>
    public (IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)> Companions, SessionMoveList MoveList) Leaving(
        GameSessionOwner session, SessionTransferFacts facts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(facts);
        var moveList = session.QueryTransferMoveList(facts);
        return (Leaving(moveList), moveList);
    }

    /// <summary>ARC-DEV-113: the companions that leave this map in <paramref name="moveList"/> (the session's change of place built it),
    /// with the facing each stands with now.</summary>
    public IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)> Leaving(SessionMoveList moveList)
    {
        ArgumentNullException.ThrowIfNull(moveList);
        var travelling = moveList.Travelling.ToHashSet(StringComparer.Ordinal);
        return _companions.Where(c => travelling.Contains(c.Entity)).Select(c => (c.Template, c.Grid.State.Facing)).ToArray();
    }

    /// <summary>The companion of a speaker, if it stands on this map.</summary>
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
    /// anchor from the home T0 chart cell, ordered at the committed cell. Charts share one projection, so the same shift
    /// stands a visitor on another map's chart. Frame, flip, alpha and visual key stay the T0 body's.
    /// </summary>
    public IReadOnlyList<AreaDrawCommand> Commands()
    {
        var moved = new List<AreaDrawCommand>();
        foreach (var companion in _companions)
        {
            var pose = PoseOf(companion);
            foreach (var command in companion.Template.Commands)
                moved.Add(command with
                {
                    Destination = new TerrainRect(checked(command.Destination.X + pose.ShiftX), checked(command.Destination.Y + pose.ShiftY), command.Destination.Width, command.Destination.Height),
                    MapX = checked((int)pose.CellX), MapY = checked((int)pose.CellY),
                });
        }
        return moved.AsReadOnly();
    }

    /// <summary>ARC-DEV-353: where each companion of this group stands (its committed world cell and the projected shift of its continuous
    /// anchor from the home T0 chart cell), the pose its body command carries and its carried light is stood by.</summary>
    public IReadOnlyList<AreaCritterPose> Poses() => _companions.Select(PoseOf).ToArray();

    private AreaCritterPose PoseOf(AreaCompanion companion)
    {
        var state = companion.Grid.State;
        double du = GridActorMath.Seconds(state.Anchor.U) - companion.Origin.X, dv = GridActorMath.Seconds(state.Anchor.V) - companion.Origin.Y;
        int px = (int)Math.Round(40 * (dv - du), MidpointRounding.AwayFromZero), py = (int)Math.Round(20 * (du + dv), MidpointRounding.AwayFromZero);
        var world = _chart.ToWorld(state.CommittedCell);
        return new AreaCritterPose(companion.Key, world.X, world.Y, px, py);
    }

    /// <summary>
    /// ARC-WORLD-017: the light of a moved body command (from <see cref="Commands"/>) where it stands now: its ART
    /// channel, and with <paramref name="local"/> the source's object colour at its current cell (tile class plus stationary
    /// lights, as for a T0 body). Null for a command that is not a companion body of this group.
    /// </summary>
    public AreaLitColors? Light(AreaDrawCommand command, AmbientPair pair, AreaLocalLighting? local)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_companions.FirstOrDefault(c => c.Key == command.Identity)?.Template is not { } template
            || !template.Visuals.TryGetValue(command.Visual, out var visual)) return null;
        // ARC-RENDER-018, ARC-RENDER-026: an additive, multiply or constant-alpha body stays unlit where it stands.
        var channel = command.Unlit ? AreaAmbientChannel.Unlit : AreaAmbientPolicy.Channel(visual.RawArt);
        if (local is not null) return local.Body(command, visual, channel, pair);
        var color = AreaLocalLighting.Ambient(channel, pair);
        return new AreaLitColors(color, color);
    }

    public string Status()
    {
        var away = (_away.Count == 0 ? "" : "; away: " + string.Join(", ", _away)) + (Skipped.Count == 0 ? "" : "; skipped: " + string.Join(", ", Skipped));
        if (_companions.Length == 0) return "companions: none on this map" + away;
        return string.Join("; ", _companions.Select(c =>
        {
            var state = c.Grid.State; var world = _chart.ToWorld(state.CommittedCell);
            var route = c.Follow.Route;
            return $"companion script {c.Speaker.ScriptId} {c.Standing} at ({world.X},{world.Y}){(c.Template.Home != _map ? " visiting" : "")} {state.Phase} follow={c.Follow.LastAction}"
                + $" route={route.Status}{(route.StopReason is { } reason ? " " + reason : "")} jumps={c.Jumps}";
        })) + away;
    }

    private static CellCoord Chart(AreaChart chart, long worldX, long worldY) => chart.ToChart(new AreaStepCell(worldX, worldY));

    private static bool Admitted(AreaStepSnapshot steps, AreaChart chart, CellCoord cell) =>
        chart.Contains(cell) && steps.IsAdmitted(chart.ToWorld(cell));
}
