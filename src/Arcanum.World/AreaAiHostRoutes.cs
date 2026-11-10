using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, RL17, ledger WS1–WS12): the fold's waypoint and standpoint steps of the AI host. The tick rules call them on the wake-up
/// marker of a leaderless NPC outside combat mode (<see cref="AiSchedulerRules.Tick"/>); the rules of the step are
/// <see cref="NpcRouteRules.ForcedWaypoints"/> and <see cref="NpcRouteRules.ForcedStandpoint"/>, over the facts of the NPC's own object row
/// (<see cref="AreaNpcRouteReader"/>), the session clock's hour and the open map. The host applies the outcome: a relocation interrupts the NPC's
/// run and stands it on the target cell as a restored place (the session's <c>npc.pos</c>, so a load or a rebuilt host keeps it).
/// ARC-DEV-116 (MIG11 S5, FM10, GR15, ledger WS3, WS6): the teleport map is the session's (<see cref="GameSessionOwner.QueryTeleportMap"/>, written by
/// every change of place and by the first step of a leaderless NPC); a companion of the shown map is a route NPC too (its row is its home map's), and a
/// former follower whose teleport map is another map than the shown one is sent home: the step's request goes through the session's change of place
/// (<see cref="SendHome"/>, caller <see cref="ChangeOfPlaceCaller.NpcWaypoint"/>, flags 0), which the viewer pings once per frame.
/// ARC-DEV-170 (B06, ledger WS14): the unforced pass is <see cref="RunRoutine"/>, the routine of a leaderless NPC: the same two steps, unforced, start the walk goal 7 to the waypoint or standpoint (<see cref="StartRouteWalk"/>).
/// Left out (NOT READY, each named in the ledger): the bed lookup, the sleep and the wander of the standpoint step, a sleeper's bed re-seat,
/// the original's stored NPC flag 0x200, and the saved waypoint
/// index: the saved schema holds none (S6 may add it), so the index lives here for the host's life and a rebuilt host recovers it from the saved place
/// when the NPC stands exactly on one of its waypoints (<see cref="RouteIndexOf"/>).
/// </summary>
public sealed partial class AreaAiHost
{
    // The route of each NPC row, read once (null: the row's route fields are not readable, so the NPC has no route step).
    private readonly Dictionary<string, AreaNpcRoute?> _routes = new(StringComparer.Ordinal);
    // The waypoint index each NPC's steps stored (field 0x120 of the original); absent: the row's own.
    private readonly Dictionary<string, int> _routeIndex = new(StringComparer.Ordinal);

    // How many route steps each outcome had, waypoint and standpoint together (the smoke prints them).
    private readonly Dictionary<NpcRouteOutcome, int> _routeOutcomes = [];

    /// <summary>How many fold steps (waypoint and standpoint) ended in each outcome since the host was built.</summary>
    public IReadOnlyDictionary<NpcRouteOutcome, int> RouteOutcomes => _routeOutcomes;

    /// <summary>The waypoint index the NPC's steps work from, or null when the NPC has no route: the one a step stored in this host; else, for an NPC the
    /// session left on a cell that is exactly one of its waypoints (a relocation of an earlier host, restored from the saved place), that waypoint's index;
    /// else the row's own. The original keeps the index in field 0x120, saved with the object; here it is recovered from the saved place, so a rebuilt
    /// host goes on from the waypoint the NPC stands on (an NPC that has left its waypoint since falls back to the row's index).</summary>
    public int? RouteIndexOf(string npc) => RouteOf(npc) is { } route ? RouteIndex(npc, route) : null;

    private int RouteIndex(string npc, AreaNpcRoute route)
    {
        if (_routeIndex.TryGetValue(npc, out var held)) return held;
        if (!Movers.IsMoved(npc) || Tile(npc) is not { } cell || cell.X < 0 || cell.Y < 0) return route.WaypointIndex;
        var here = new AreaRouteLocation((uint)cell.X, (uint)cell.Y);
        if (route.WaypointIndex >= 0 && route.WaypointIndex < route.Waypoints.Count && route.Waypoints[route.WaypointIndex] == here) return route.WaypointIndex;
        var found = route.Waypoints.ToList().IndexOf(here);
        return found >= 0 ? found : route.WaypointIndex;
    }

    private bool RunRouteStep(string npc, Func<NpcRouteFacts, NpcRouteStep> rule, bool waypoints)
    {
        if (RouteFacts(npc) is not { } facts) return false;
        var step = rule(facts);
        _routeOutcomes[step.Outcome] = _routeOutcomes.GetValueOrDefault(step.Outcome) + 1;
        if (step.NewWaypointIndex is { } index) _routeIndex[npc] = index;
        // 0x45F6D0 (WS3): the teleport map is written with the open map while it is 0, also when the step then ends.
        var written = step.TeleportMapSet is { } open ? WriteTeleportMap(npc, open) : null;
        var what = waypoints ? "waypoint" : "standpoint";
        switch (step.Outcome)
        {
            case NpcRouteOutcome.Relocate:
                Trace(npc).Route = $"{what} {step.Outcome}: {(_followers.ContainsKey(npc) ? RelocateFollower(npc, step.Target!.Value, facts) : Relocate(npc, step.Target!.Value, facts))}";
                break;
            case NpcRouteOutcome.RequestTeleport:
                // WS6: a forced pass of a leaderless NPC whose teleport map is another map asks for the NPC's change of place to its target there.
                Trace(npc).Route = $"{what} {step.Outcome} to map {step.TeleportMap} ({step.Target!.Value.X},{step.Target.Value.Y}): {SendHome(npc, step.TeleportMap, step.Target.Value)}";
                break;
            case NpcRouteOutcome.StartWalk:
                // ARC-DEV-170 (B06, WS6): an unforced pass on the open map starts the walk goal (0x4AD1B0 → 0x4341C0, goal 7) to the target.
                Trace(npc).Route = $"{what} {step.Outcome} to {step.Target!.Value.X},{step.Target.Value.Y} mode {step.Mode}: {StartRouteWalk(npc, step.Target.Value, step.Mode)}";
                break;
            default:
                Trace(npc).Route = $"{what} {step.Outcome}";
                break;
        }
        if (written is not null) Trace(npc).Route += $" [teleport map {written}]";
        return step.Answer;
    }

    // ARC-DEV-170 (B06, 0x4AD1B0 → 0x4341C0, GS30): the walk starter of the unforced steps. The movement gate first (the walk goal 7 has no other gate); an NPC that already has a walk run
    // (the finder asks for goal 4, whose aliases list the walks 3, 7 and 55) is retargeted in place (the same tile changes nothing, a different one is written over the run's destination
    // and the run plans again); otherwise goal 7 starts through the generic starter 0x44D4E0, which interrupts the NPC's runs at the goal's own priority (2). The mode (stored in the frame's range slot, never read by the goal) is not rewritten
    // by a retarget (the original writes only the destination).
    private string StartRouteWalk(string npc, RouteLocation target, int mode)
    {
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        var request = new GoalStartRequest(npc, GoalStarters.Generic, new GoalFrame(GoalType.MoveNearTile, tile: cell, range: mode));
        if (!StartGatePasses(request)) return "refused by the movement gate";
        if (_session.QueryGoals().RunRunningGoal(npc, GoalType.RunToTile) is { } running)
            return _session.RetargetGoal(running.Handle, cell).Outcome switch
            {
                GoalRetargetOutcome.Retargeted => "retargeted",
                GoalRetargetOutcome.Unchanged => "already walking there",
                var other => "not retargeted: " + other,
            };
        var started = _session.StartGoal(request, _startPorts ??= new StartPorts(this));
        foreach (var end in started.Ended) EndNodes(end);
        return started.Started ? "started" : $"not started: {started.Failure} {started.Outcome}";
    }

    /// <summary>
    /// ARC-DEV-206 (the smoke's order): the routine's own walk starter (<see cref="StartRouteWalk"/>, goal 7 through the generic starter, an existing walk retargeted) from where the NPC stands to
    /// <paramref name="cell"/>; the text says what happened ("started", "retargeted", or why not). A follower is the party's to order and is refused.
    /// </summary>
    public string OrderWalk(string npc, CellCoord cell)
    {
        if (!_npcs.ContainsKey(npc)) return "not an NPC of this host";
        if (_followers.ContainsKey(npc)) return "a follower walks by the party's drive";
        if (cell.X < 0 || cell.Y < 0) return "outside the cell range";
        return StartRouteWalk(npc, new RouteLocation((uint)cell.X, (uint)cell.Y), 0);
    }

    /// <summary>
    /// ARC-DEV-231 (MIG13 S14a, GT158–GT161): a script's walk to (69) or run to (74) of <paramref name="npc"/> to <paramref name="cell"/>, from where the NPC stands: the route walk's starter (movement gate, an existing walk
    /// retargeted in place, else goal 7, or goal 55 for the run, through the generic starter at its own level 2, the 3 in the range slot the nodes never read). The run to turns the run flag on (0x40) on the run it found or
    /// started when the NPC's encumbrance is below 4, even for the same tile (GT161). The answer of the starter is never seen by the script; the text says what happened. A follower is the party's to order and is refused.
    /// </summary>
    public string ScriptWalk(string npc, CellCoord cell, bool run)
    {
        if (!_npcs.ContainsKey(npc)) return "not an NPC of this host";
        if (_followers.ContainsKey(npc)) return "a follower walks by the party's drive";
        if (cell.X < 0 || cell.Y < 0) return "outside the cell range";
        var request = new GoalStartRequest(npc, GoalStarters.Generic, new GoalFrame(run ? GoalType.RunNearTile : GoalType.MoveNearTile, tile: cell, range: ScriptWalkRange));
        if (!StartGatePasses(request)) return "refused by the movement gate";
        var turnsRun = run && Mover(npc).Encumbrance < AiExecutorRules.RunRefusedEncumbrance;
        if (_session.QueryGoals().RunRunningGoal(npc, GoalType.RunToTile) is { } running)
        {
            if (turnsRun && (running.Flags & GoalRunFlags.Running) == 0) _session.TurnOnRunFlags(running.Handle, GoalRunFlags.Running);
            return _session.RetargetGoal(running.Handle, cell).Outcome switch
            {
                GoalRetargetOutcome.Retargeted => "retargeted",
                GoalRetargetOutcome.Unchanged => "already walking there",
                var other => "not retargeted: " + other,
            };
        }
        var started = _session.StartGoal(request, _startPorts ??= new StartPorts(this));
        foreach (var end in started.Ended) EndNodes(end);
        if (!started.Started) return $"not started: {started.Failure} {started.Outcome}";
        if (turnsRun && _session.QueryGoals().RunRunningGoal(npc, GoalType.RunToTile) is { } now) _session.TurnOnRunFlags(now.Handle, GoalRunFlags.Running);
        return "started";
    }

    /// <summary>
    /// ARC-DEV-233 (MIG13 S14b, GT155–GT157): a script's perform animation (62) of <paramref name="npc"/>: goal 60 (kneel, magic hands, kneel back) for animation 10, else goal 0 playing the animation once from frame
    /// 0, through the generic starter at the goals' priority 3 (the NPC's runs of priority 1–3 end; a priority 4/5 run refuses). No gate: any NPC may animate, in a round too. An animation outside 0..25 starts
    /// nothing. The text says what happened.
    /// </summary>
    public string ScriptAnimate(string npc, int animation)
    {
        if (!_npcs.ContainsKey(npc)) return "not an NPC of this host";
        if (AiExecutorRules.PerformAnimationFrame(animation, critter: true) is not { } frame) return $"animation {animation} outside 0..{AiExecutorRules.AnimationCount - 1}";
        var started = _session.StartGoal(new GoalStartRequest(npc, GoalStarters.Generic, frame), _startPorts ??= new StartPorts(this));
        foreach (var end in started.Ended) EndNodes(end);
        return started.Started ? "started" : $"not started: {started.Failure} {started.Outcome}";
    }

    /// <summary>
    /// ARC-DEV-236 (MIG13 S14e, GT187): a script's become prone (93) of <paramref name="npc"/>: the interrupt 0x424070 at level 4 (a refusal, a priority-5 run that will not yield, does nothing), then the art becomes the
    /// fall (animation 7) at its last frame. No goal starts, and no flag, combat mode, focus or follower changes; the object lies with no run until something starts the get-up.
    /// </summary>
    public string ScriptProne(string npc)
    {
        if (!Walks(npc)) return "not an NPC of this host";
        var (_, succeeded) = InterruptOwner(npc, ScriptProneLevel);
        if (!succeeded) return "refused by a run of the NPC";
        var last = Math.Max((Clip(npc, DyingAnimationRules.FallDown)?.Frames ?? 1) - 1, 0);
        ShowArt(npc, DyingAnimationRules.FallDown, last);
        return "lying";
    }

    private const int ScriptProneLevel = 4;

    /// <summary>
    /// ARC-DEV-241 (MIG13 S14f, GT183/GT184): a script's cast of <paramref name="spell"/> at <paramref name="target"/> (an NPC of this host or the PC's scheduler id) by <paramref name="caster"/>, an NPC of this host, or by
    /// none (<c>null</c>: 61, 136 or an absent caster handle). The cast host's script entry point (<see cref="SessionSpellCaster.CastByScript"/>: no cost, no line of fire, free and unresistable flags) starts the run; with
    /// a caster its casting goal, 26 or 82 by the record's casting animation, starts as perform's cast does (queued under a running cast, refused at depth 4 or in a round, replacing the other runs by the generic
    /// starter's interrupt); without one no goal starts, the run begins by its event. A cast by the PC is not this host's (ARC-DEV-244: the viewer plays the hero's casting goal); the answer of the starter is never seen by the script.
    /// The text says what happened.
    /// </summary>
    public string ScriptCast(string? caster, string target, int spell, bool free, bool unresistable)
    {
        if (_scene.Spells?.Invoke() is not { } spells) return "no cast host";
        if (caster == Pc) return "the PC's cast is the hero's run host's";
        if (caster is null)
        {
            var bare = spells.CastByScript(null, spell, target, free, unresistable);
            return bare.Outcome == SpellCastAttemptOutcome.Started ? $"cast without a caster, run {bare.RunId}" : $"not cast: {bare.Outcome}";
        }
        if (!_npcs.ContainsKey(caster)) return "the caster is not an NPC of this host";
        var current = RunOf(caster);
        _scriptCast = (free, unresistable);
        AiCastStart cast;
        try { cast = Cast(caster, new AiPassChoice(AiPassAction.Cast, spell, 0, null, target)); }
        finally { _scriptCast = null; }
        if (cast.Run is not { } run) return cast.Queued ? "queued under a running cast" : "not cast: the check refused it or the run was not started";
        var goal = cast.CastAnimation ? GoalType.CastSpellWithAnimation : GoalType.CastSpell;
        Started(caster, current, new AiPerformResult(AiPerformKind.Cast, new GoalRun(caster, default, new GoalFrame(goal, target) { SpellRun = run })) { Started = true });
        return $"cast, run {run}, goal {(int)goal}";
    }

    /// <summary>The coins a stack must hold for the script's steal to start (GT164).</summary>
    public const int ScriptStealMinimumCoins = 100;

    /// <summary>
    /// ARC-DEV-234 (MIG13 S14c, GT164): a script's try to steal 100 coins of <paramref name="thief"/> from <paramref name="victim"/> (an NPC or the PC's scheduler id): nothing happens unless the victim holds at least
    /// 100 coins; then the skill starter 0x4350F0 (start gate with the skill cost, goal 33 with skill 5, run flag 0x4000) starts the thief's pickpocket run. How much is taken is decided by goals 33 → 86, whose NPC attempt is
    /// not ready in the AI host (a Skill notice, no transfer). The text says what happened.
    /// </summary>
    public string ScriptSteal(string thief, string victim)
    {
        if (!_npcs.ContainsKey(thief)) return "not an NPC of this host";
        if (victim != Pc && !_npcs.ContainsKey(victim)) return "the victim is not an NPC of this host";
        var coins = victim == Pc ? _session.QueryGold() : _session.QueryNpcGold(victim) ?? 0;
        if (coins < ScriptStealMinimumCoins) return $"the victim holds {coins} coins, fewer than {ScriptStealMinimumCoins}";
        var current = RunOf(thief);
        // A heartbeat repeats the action every beat and the attempt transfers nothing yet (so the coins never fall below 100): a thief already on this victim keeps its run instead of restarting it.
        if (current is { Bottom: { Goal: GoalType.UseSkill, Skill: AiExecutorRules.PickpocketSkill } bottom } && bottom.Target == victim) return "already stealing from the victim";
        var result = AiExecutorRules.StartUseSkill(this, thief, victim, AiExecutorRules.PickpocketSkill, null, current);
        Started(thief, current, result);
        return result.Started ? "started" : $"not started: {result.Refusal}";
    }

    // The 3 the script walks put in the frame's range slot (GT158, GT161): no node reads it.
    private const int ScriptWalkRange = 3;

    // The getter's write (a refusal leaves the field 0: the next step writes it again).
    private string WriteTeleportMap(string npc, int open)
    {
        var result = _session.EnsureTeleportMap(_session.Version, npc, new SessionMapId(open));
        return result.Failure == GameSessionFailure.None ? open.ToString(System.Globalization.CultureInfo.InvariantCulture) : "not written: " + result.Failure;
    }

    // ---- ARC-DEV-116 (MIG11 S5): a former follower goes home ----

    // The requests of NPCs the session's one slot could not take yet (Steamwoven); the Original setting makes each at once and the slot keeps the last.
    private List<(string Npc, int Map, CellCoord Cell)> Homecomings => _scene.Homecomings ?? _ownHomecomings;
    private readonly List<(string Npc, int Map, CellCoord Cell)> _ownHomecomings = [];

    /// <summary>The NPCs waiting to ask for their change of place (Steamwoven: one request is made per frame, <see cref="FlushHomecomings"/>).</summary>
    public IReadOnlyList<string> PendingHomecomings => [.. Homecomings.Select(h => h.Npc)];

    private string SendHome(string npc, int map, RouteLocation target)
    {
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (_session.Rules.Lifecycle.NonPlayerRequestsOverwriteEachOther) return Submit(npc, map, cell).Text;
        // Steamwoven: a request waits for the slot; the NPC's own earlier one is replaced (one request per NPC).
        Homecomings.RemoveAll(h => h.Npc == npc);
        Homecomings.Add((npc, map, cell));
        return "queued";
    }

    private (string Text, bool Accepted) Submit(string npc, int map, CellCoord cell)
    {
        try
        {
            var submitted = _session.RequestChangeOfPlace(npc, ChangeOfPlaceCaller.NpcWaypoint, map, cell, ChangeOfPlaceFlags.None, 0, _scene.MapListed ?? (m => m == _scene.RuntimeMap));
            return (submitted.Status + (submitted.Replaced is { } lost ? $" (lost {lost.Entity})" : ""), submitted.Accepted);
        }
        catch (ArgumentException)
        {
            return ("not a map NPC", false);
        }
    }

    /// <summary>
    /// ARC-DEV-116: makes the next waiting request when the slot is free (nothing pending or in progress); the viewer calls it once per frame before it pings
    /// (<see cref="GameSessionOwner.ProcessChangeOfPlace"/>). A request the project refuses (a map the viewer does not list, a pending hero request) is
    /// dropped with its reason in the NPC's trace. True when a request was made.
    /// </summary>
    public bool FlushHomecomings()
    {
        if (Homecomings.Count == 0 || _session.QueryChangeOfPlace() is not null || _session.ChangeOfPlaceInProgress) return false;
        var (npc, map, cell) = Homecomings[0];
        Homecomings.RemoveAt(0);
        var (text, accepted) = Submit(npc, map, cell);
        Trace(npc).Route += $" -> {text}";
        return accepted;
    }

    // A companion of the shown map is a route NPC: its row is its home map's, its teleport map the session's. Its relocation is its walk's jump.
    private string RelocateFollower(string npc, RouteLocation target, NpcRouteFacts facts)
    {
        InterruptRuns(npc, FarPathLevel);
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (Tile(npc) == cell) return $"already on {cell.X},{cell.Y}";
        if (!PlaceHeld(cell)) return $"target {cell.X},{cell.Y} not held by the shown map";
        if (_scene.Followers is not { } followers || !followers.Jump(npc, cell)) return "the companion's walk refused the jump";
        // ARC-DEV-328 (B25, GR12): the waypoint's relocation (0x4ACBB0, 0x43E770) notifies like any move, so the cell's tile script runs for the companion (ledger TG2, TG9).
        _notices.Add(new(AreaAiNoticeKind.Entered, npc, null) { Cell = cell });
        return $"{facts.Location.X},{facts.Location.Y} -> {cell.X},{cell.Y}";
    }

    private AreaNpcRoute? RouteOf(string npc)
    {
        if (_routes.TryGetValue(npc, out var held)) return held;
        // An NPC no window has held yet has no row to read (ARC-DEV-119: a later window gives it), so that is no answer to keep.
        if (!TryNpc(npc, out var row)) return null;
        var route = AreaNpcRouteReader.Read(row.Row) is { Status: AreaNpcRouteStatus.Ready, Route: { } read } ? read : null;
        _routes[npc] = route;
        return route;
    }

    // The facts of one step; null when the host holds no NPC row of this id (a companion of another map's host, an unreadable route).
    private NpcRouteFacts? RouteFacts(string npc)
    {
        if (RouteOf(npc) is not { } route || Tile(npc) is not { } cell || cell.X < 0 || cell.Y < 0) return null;
        // ARC-DEV-116 (FM10): the teleport map is the session's; the row's own (field 243 of the stored object, 0 in every row of the installed maps) is the baseline.
        return new(Sleeping(npc), Hidden(npc), Leader(npc) is not null, _session.QueryClockSnapshot().Hour, route.NpcFlags, _session.QueryTeleportMap(npc) ?? route.TeleportMap, _scene.RuntimeMap,
            new((uint)cell.X, (uint)cell.Y), [.. route.Waypoints.Select(w => new RouteLocation(w.X, w.Y))], RouteIndex(npc, route),
            new(route.StandpointDay.X, route.StandpointDay.Y), new(route.StandpointNight.X, route.StandpointNight.Y));
    }

    // The original's relocation (0x424070 at priority 4, then 0x43E770 with no offset): the run ends and the NPC stands on the target. A mover that
    // exists keeps its facing; an NPC that never moved is given the mover's default (+x), as after any first step or turn of the host (the original
    // keeps the object's own rotation: NOT READY, the rows' rotation is not read here). A target the shown map cannot hold (outside the chart, not admitted, the hero's own cell) leaves the NPC where it stands.
    private string Relocate(string npc, RouteLocation target, NpcRouteFacts facts)
    {
        InterruptRuns(npc, FarPathLevel);
        if (target.X > int.MaxValue || target.Y > int.MaxValue) return $"target {target.X},{target.Y} outside the cell range";
        var cell = new CellCoord((int)target.X, (int)target.Y);
        if (Tile(npc) == cell) return $"already on {cell.X},{cell.Y}";
        if (!PlaceHeld(cell)) return $"target {cell.X},{cell.Y} not held by the shown map";
        var row = _npcs[npc];
        Movers.Restore(npc, row.Row.Key, row.Origin, cell, Movers.FacingOf(npc, NeighborDirection.XPlus));
        RecordPlace(npc);
        NoteEntered(npc, cell);
        return $"{facts.Location.X},{facts.Location.Y} -> {cell.X},{cell.Y}";
    }

    // Whether the shown map holds a resident on this world cell: it lies on the chart (a map with a loaded-sector chart, the main map's windows: anywhere on the map, ARC-DEV-119),
    // its tile is admitted and the hero is not on it.
    private bool PlaceHeld(CellCoord world) =>
        (_scene.LoadedSteps is not null || _scene.Chart.Contains(ToChart(world))) && Admitted(new AreaStepCell(world.X, world.Y)) && _scene.HeroCell() != world;
}
