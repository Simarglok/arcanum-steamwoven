using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
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

    /// <summary>ARC-WORLD-017: on the NPC's home map, the runtime map it stands on while it is elsewhere (absent = home).</summary>
    public const string MapProperty = "npc.map";

    public static MapChangeKey MapKey(string entity) => MapChangeKey.ForEntity(entity, MapProperty);

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

    /// <summary>The change that records a companion standing on <paramref name="map"/> at <paramref name="world"/>: on its
    /// home map, its T0 cell facing the default clears the key; anywhere else the cell is always written.</summary>
    public static MapChangeEdit Edit(AreaCompanionTemplate template, SessionMapId map, CellCoord world, NeighborDirection facing)
    {
        ArgumentNullException.ThrowIfNull(template);
        return map == template.Home && world == template.HomeWorld && facing == NeighborDirection.XPlus
            ? new(map, Key(template.Entity), null)
            : new(map, Key(template.Entity), Encode(world, facing));
    }
}

/// <summary>A companion's map and recorded placement there (null = its T0 cell on its home map), or why it cannot be read.</summary>
public readonly record struct AreaCompanionWhere(SessionMapId Map, (CellCoord World, NeighborDirection Facing)? Placement, string? Error);

/// <summary>
/// ARC-WORLD-017: the home-map T0 data of one roster companion, enough to stand, draw, light and pick it on any loaded
/// map: its speaker row, its T0 body commands with their visuals, and its T0 chart cell on the home map. The body keeps
/// the T0 frame, flip and alpha wherever it stands.
/// </summary>
public sealed record AreaCompanionTemplate(AreaSpeaker Speaker, AreaCompanionFacts Facts, SessionMapId Home, CellCoord HomeOrigin,
    CellCoord HomeWorld, IReadOnlyList<AreaDrawCommand> Commands, IReadOnlyDictionary<AreaVisualKey, AreaVisualRecord> Visuals)
{
    public string Entity => AreaConversationState.EntityId(Speaker.Row);
    public AreaObjectKey Key => Speaker.Row.Key;

    /// <summary>The roster speakers of <paramref name="speakers"/> on home map <paramref name="home"/>, with their body
    /// commands from <paramref name="plan"/> and the visuals those commands draw.</summary>
    public static IReadOnlyList<AreaCompanionTemplate> From(IEnumerable<AreaSpeaker> speakers, AreaScenePlan plan,
        IEnumerable<AreaVisualRecord> visuals, AreaChart chart, SessionMapId home)
    {
        ArgumentNullException.ThrowIfNull(speakers); ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(visuals); ArgumentNullException.ThrowIfNull(chart);
        var catalog = new Dictionary<AreaVisualKey, AreaVisualRecord>();
        foreach (var visual in visuals) catalog.TryAdd(visual.Key, visual);
        var templates = new List<AreaCompanionTemplate>();
        foreach (var speaker in speakers)
        {
            if (AreaCompanionRoster.For(speaker) is not { } facts) continue;
            var commands = plan.Commands.Where(command => command.Identity == speaker.Row.Key).ToArray();
            var drawn = new Dictionary<AreaVisualKey, AreaVisualRecord>();
            foreach (var command in commands)
                drawn[command.Visual] = catalog.TryGetValue(command.Visual, out var visual) ? visual : throw new InvalidOperationException("AREA_COMPANION_VISUAL_REQUIRED");
            templates.Add(new(speaker, facts, home, chart.ToChart(new AreaStepCell(speaker.WorldX, speaker.WorldY)),
                new(checked((int)speaker.WorldX), checked((int)speaker.WorldY)), Array.AsReadOnly(commands), new ReadOnlyDictionary<AreaVisualKey, AreaVisualRecord>(drawn)));
        }
        return templates.AsReadOnly();
    }

    /// <summary>The frame repository of another map with the templates' T0 body frames added from the home map's
    /// (a key already present must be the same decoded frame).</summary>
    public static IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames(IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> map,
        IEnumerable<AreaCompanionTemplate> templates, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> home)
    {
        ArgumentNullException.ThrowIfNull(map); ArgumentNullException.ThrowIfNull(templates); ArgumentNullException.ThrowIfNull(home);
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>(map);
        foreach (var command in templates.SelectMany(template => template.Commands))
        {
            var frame = home.TryGetValue(command.Frame, out var found) ? found : throw new InvalidOperationException("AREA_COMPANION_FRAME_REQUIRED");
            if (frames.TryGetValue(command.Frame, out var known))
            {
                if (!ReferenceEquals(known, frame) && (known.Width != frame.Width || known.Height != frame.Height || !known.Rgba.AsSpan().SequenceEqual(frame.Rgba)))
                    throw new InvalidOperationException("AREA_COMPANION_FRAME_CONFLICT");
            }
            else frames.Add(command.Frame, frame);
        }
        return new ReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>(frames);
    }
}

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
}

/// <summary>
/// ARC-WORLD-016: the measured companions of one AREA map as moving actors. Each one walks on its own
/// <see cref="AreaGridSession"/> over the map's T0 step evidence with the live overlay of <see cref="AreaOccupancy"/>:
/// its T0 record no longer blocks, and the PC and the other companions block where they stand. While the session
/// party says it follows the PC, <see cref="CompanionFollower"/> drives it after the PC at each frame boundary; a
/// waiting or free companion stands where it stopped. The PC is admitted against the companions in the same way.
/// Placement lives in the session (<see cref="AreaCompanionPlacement"/>) only when the host records it (quick save,
/// leaving the map), like the PC's. The drawn body is the NPC's T0 frame moved with the actor (no walk cycle or
/// turning). ARC-WORLD-017: a companion stands on the map the session names (<see cref="AreaCompanionPlacement.Where"/>),
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
    public static AreaCompanionGroup Create(IEnumerable<AreaCompanionTemplate> templates, AreaStepSnapshot steps, AreaChart chart,
        GameSessionOwner session, SessionMapId map, SimulationTime time)
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
            AreaOccupancy Admission() => group!.OccupancyFor(index, leader: true);
            AreaOccupancy Planning() => group!.OccupancyFor(index, leader: false);
            var grid = new AreaGridSession(steps, time, chart: chart, start: start, facing: facing,
                actorId: ActorPrefix + template.Speaker.Index, capabilities: Capabilities) { Occupancy = Admission };
            list.Add(new(template, grid, new CompanionFollower(new AreaRouteSource(steps, chart, Planning))));
        }
        group = new(steps, chart, map, list.ToArray(), absent, away.AsReadOnly()) { Skipped = skipped.AsReadOnly() };
        group.Refresh(session);
        return group;
    }

    /// <summary>The PC's overlay: every companion's T0 record is moved (here or away), and the companions hold their live cells.</summary>
    public AreaOccupancy OccupancyForPc() => _companions.Length == 0 && _absent.Count == 0 ? AreaOccupancy.None : Cached(-1, true);

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
        var value = new AreaOccupancy(_companions.Select(c => c.Key).Concat(_absent), held);
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
    /// Records every companion's committed cell and facing on this map in the session, in one atomic step: a companion
    /// back on its home T0 cell facing the default clears the change. Returns the refusal (None when applied).
    /// </summary>
    public GameSessionFailure Record(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = session.ApplyChanges(session.Version, null, _companions.Select(Stay).ToArray());
        return result.Disposition == GameSessionDisposition.Rejected ? result.Failure : GameSessionFailure.None;
    }

    private MapChangeEdit Stay(AreaCompanion companion)
    {
        var state = companion.Grid.State; var world = _chart.ToWorld(state.CommittedCell);
        return AreaCompanionPlacement.Edit(companion.Template, _map, new(checked((int)world.X), checked((int)world.Y)), state.Facing);
    }

    /// <summary>
    /// ARC-WORLD-017 (research C15, C16, V4): the session changes of the PC leaving this map for
    /// <paramref name="destination"/> at the world cell <paramref name="arrival"/>. Every companion the session party says
    /// follows the PC goes with it, at any distance, onto the same arrival cell with its own facing (the next follow
    /// decision on the destination steps it off the shared cell); its home map names the destination, or forgets the away
    /// map when the destination is home. Every other companion (waiting, free) stays where it stands on this map. The host
    /// applies the changes together with the PC's placement in one <see cref="GameSessionOwner.ApplyChanges"/>.
    /// </summary>
    public (IReadOnlyList<MapChangeEdit> Changes, IReadOnlyList<AreaCompanion> Carried) Carry(GameSessionOwner session, SessionMapId destination, CellCoord arrival)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (destination == _map) throw new ArgumentException("AREA_COMPANION_CARRY_SAME_MAP", nameof(destination));
        var changes = new List<MapChangeEdit>(); var carried = new List<AreaCompanion>();
        foreach (var companion in _companions)
        {
            if (session.QueryCompanion(companion.Entity).Standing != CompanionStanding.Following) { changes.Add(Stay(companion)); continue; }
            var template = companion.Template;
            changes.Add(new(_map, AreaCompanionPlacement.Key(companion.Entity), null));
            changes.Add(AreaCompanionPlacement.Edit(template, destination, arrival, companion.Grid.State.Facing));
            changes.Add(new(template.Home, AreaCompanionPlacement.MapKey(companion.Entity), destination == template.Home ? null : destination.RuntimeMap));
            carried.Add(companion);
        }
        return (changes.AsReadOnly(), carried.AsReadOnly());
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
            var state = companion.Grid.State;
            double du = DiagnosticGridActor.Seconds(state.Anchor.U) - companion.Origin.X, dv = DiagnosticGridActor.Seconds(state.Anchor.V) - companion.Origin.Y;
            int px = (int)Math.Round(40 * (dv - du), MidpointRounding.AwayFromZero), py = (int)Math.Round(20 * (du + dv), MidpointRounding.AwayFromZero);
            var world = _chart.ToWorld(state.CommittedCell);
            foreach (var command in companion.Template.Commands)
                moved.Add(command with
                {
                    Destination = new TerrainRect(checked(command.Destination.X + px), checked(command.Destination.Y + py), command.Destination.Width, command.Destination.Height),
                    MapX = checked((int)world.X), MapY = checked((int)world.Y),
                });
        }
        return moved.AsReadOnly();
    }

    /// <summary>
    /// ARC-WORLD-017: the light of a moved body command (from <see cref="Commands"/>) where it stands now: its ART
    /// channel, and with <paramref name="local"/> the CE object colour at its current cell (tile class plus stationary
    /// lights, as for a T0 body). Null for a command that is not a companion body of this group.
    /// </summary>
    public AreaLitColors? Light(AreaDrawCommand command, AmbientPair pair, AreaLocalLighting? local)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_companions.FirstOrDefault(c => c.Key == command.Identity)?.Template is not { } template
            || !template.Visuals.TryGetValue(command.Visual, out var visual)) return null;
        var channel = AreaAmbientPolicy.Channel(visual.RawArt);
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
        cell.X is >= AreaGridSession.MinimumChartCell and <= AreaGridSession.MaximumChartCell &&
        cell.Y is >= AreaGridSession.MinimumChartCell and <= AreaGridSession.MaximumChartCell && steps.IsAdmitted(chart.ToWorld(cell));
}
