using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Items;
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
/// ARC-WORLD-1162 (ledger SWB17–SWB21): the near branch's bed lookup and seat, the NPC flag 0x200 latch and a sleeper's re-seat are <see cref="AreaAiHostBeds"/>.
/// ARC-AI-1314 (ledger VA18, VA19, VAG3): the waypoint index (the original's field 0x120) and the standpoint latch (NPC flag 0x200) are the NPC's object state in the session
/// (<see cref="SessionNpcRouteState"/>), written with the step's teleport map in one revision, saved and loaded with the object; a rebuilt host and a loaded game go on from them.
/// Left out (NOT READY, each named in the ledger): the wander of the standpoint step.
/// </summary>
public sealed partial class AreaAiHost
{
    // The route of each NPC row, read once (null: the row's route fields are not readable, so the NPC has no route step).
    private readonly Dictionary<string, AreaNpcRoute?> _routes = new(StringComparer.Ordinal);

    // How many route steps each outcome had, waypoint and standpoint together (the smoke prints them).
    private readonly Dictionary<NpcRouteOutcome, int> _routeOutcomes = [];

    /// <summary>How many fold steps (waypoint and standpoint) ended in each outcome since the host was built.</summary>
    public IReadOnlyDictionary<NpcRouteOutcome, int> RouteOutcomes => _routeOutcomes;

    /// <summary>The waypoint index the NPC's steps work from, or null when the NPC has no route: the one a step stored in the session (the original's field 0x120, saved with the object); else the row's own.</summary>
    public int? RouteIndexOf(string npc) => RouteOf(npc) is { } route ? RouteIndex(npc, route) : null;

    private int RouteIndex(string npc, AreaNpcRoute route) => _session.QueryRouteIndex(npc) ?? route.WaypointIndex;

    private bool RunRouteStep(string npc, Func<NpcRouteFacts, NpcRouteStep> rule, bool waypoints)
    {
        if (RouteFacts(npc) is not { } facts) return false;
        var step = rule(facts);
        _routeOutcomes[step.Outcome] = _routeOutcomes.GetValueOrDefault(step.Outcome) + 1;
        var what = waypoints ? "waypoint" : "standpoint";
        // ARC-AI-1314 (0x4ACBB0, 0x4ACDB0): the step's writes on the NPC are one revision: the waypoint index (field 0x120), the teleport map (0x45F6D0, WS3: the open map while it is 0, also when the step then ends)
        // and, from ARC-WORLD-1162 (SWB21), the far branch of the standpoint step's clear of NPC flag 0x200 before it relocates or walks the NPC.
        var clearsLatch = !waypoints && step.Outcome is NpcRouteOutcome.Relocate or NpcRouteOutcome.RequestTeleport or NpcRouteOutcome.StartWalk && LatchOf(npc);
        var refused = WriteRouteState(npc, step, clearsLatch);
        var written = refused is null ? step.TeleportMapSet?.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        var bed = BedStep(npc, step, route: RouteOf(npc));
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
        if (refused is not null) Trace(npc).Route += $" [route state not written: {refused}]";
        if (bed is not null) Trace(npc).Route += $" [bed: {bed}]";
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

    // The step's writes (a refusal leaves the fields as they were: the next step writes them again). Returns the refusal, or null when they were written or there was nothing to write.
    private GameSessionFailure? WriteRouteState(string npc, NpcRouteStep step, bool clearsLatch)
    {
        if (RouteOf(npc) is not { } route) return null;
        var open = new SessionMapId(step.TeleportMapSet ?? _scene.RuntimeMap);
        var result = _session.WriteRouteStep(_session.Version, npc, open, step.NewWaypointIndex, route.WaypointIndex, clearsLatch ? false : null, LatchData(route), step.TeleportMapSet is not null);
        return result.Failure == GameSessionFailure.None ? null : result.Failure;
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
            // ARC-SCRIPT-1163 (NS3): a script's set standpoint is the session's (the field is saved with the object), over the row's.
            Standpoint(npc, route, day: true), Standpoint(npc, route, day: false));
    }

    private RouteLocation Standpoint(string npc, AreaNpcRoute route, bool day)
    {
        var stand = AreaUseScriptHost.Standpoint(_session, _scene.RuntimeMap, npc, day) ?? (day ? route.StandpointDay : route.StandpointNight);
        return new(stand.X, stand.Y);
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

    /// <summary>ARC-MAGIC-038 (ledger TP5): the viewer's runner of the tile scripts of the tiles an immediate path relocation (<see cref="RelocateAlongPath"/>) walks. The original runs them (0x4C0980) before it
    /// interrupts and places the object (0x455350); the hook gets the NPC and the tiles in walk order and runs them with the NPC still on its old tile. Without it the host queues <see cref="AreaAiNoticeKind.Entered"/> notices
    /// for them after the placement.</summary>
    public Action<string, IReadOnlyList<CellCoord>>? BeforeRelocationScripts { get; set; }

    /// <summary>
    /// ARC-MAGIC-036 (MA-5, MV1–MV3): the spell Movement component's immediate path relocation (0x455350) of an NPC or companion this host runs: the path request from its tile to <paramref name="destination"/>
    /// (<see cref="ImmediatePathWalk"/>, its owner kind by its leader: a PC's side with a PC leader, else an NPC's), the NPC's walk runs interrupted (the original's level-5 interrupt 0x424070; here
    /// the walk runs, as for a hold: the spell's own run is not the host's to end), the NPC placed on the last tile of the walk with its facing kept (a companion by its walk's jump), and the tile
    /// script run for every tile walked and once more for the last one, as the relocation's notification does (TG2, TG9). Answers what happened, for the log: nothing moves without a route or when the shown map
    /// does not hold the last tile.
    /// </summary>
    public (int Tiles, string Result) RelocateAlongPath(string npc, CellCoord destination, IDropMap map)
    {
        ArgumentException.ThrowIfNullOrEmpty(npc);
        ArgumentNullException.ThrowIfNull(map);
        var follower = _followers.ContainsKey(npc);
        if (!follower && !_npcs.ContainsKey(npc)) return (0, "not run by the host");
        if (Tile(npc) is not { } from) return (0, "no tile");
        var tiles = ImmediatePathWalk.Tiles(map, from, destination, Leader(npc) is null ? DropOwner.Npc : DropOwner.PcSide);
        if (tiles.Count == 0) return (0, "no route");
        var last = tiles[^1];
        if (!PlaceHeld(last)) return (tiles.Count, $"arrival {last.X},{last.Y} not held by the shown map");
        // 0x455350 runs the tile script of every tile it steps onto before it interrupts and places the object: the viewer's hook runs them now, with the NPC on its old tile. Without the hook the
        // notices below carry them after the placement (the project's earlier order).
        var scripted = BeforeRelocationScripts is not null;
        if (BeforeRelocationScripts is { } run)
        {
            run(npc, tiles);
            if (Gone(npc) || DeadCritter(npc)) return (tiles.Count, "gone during the walk's tile scripts");
            // A tile script may have made the NPC join or leave the group, or taken the destination tile.
            follower = _followers.ContainsKey(npc);
            if (!follower && !_npcs.ContainsKey(npc)) return (tiles.Count, "no longer run by the host after the walk's tile scripts");
            if (!PlaceHeld(last)) return (tiles.Count, $"arrival {last.X},{last.Y} not held by the shown map after the walk's tile scripts");
        }
        InterruptForHold(npc);
        if (follower)
        {
            if (_scene.Followers is not { } followers || !followers.Jump(npc, last)) return (tiles.Count, "the companion's walk refused the jump");
        }
        else
        {
            var row = _npcs[npc];
            Movers.Restore(npc, row.Row.Key, row.Origin, last, Movers.FacingOf(npc, NeighborDirection.XPlus));
            RecordPlace(npc);
        }
        if (!scripted) foreach (var tile in tiles) _notices.Add(new(AreaAiNoticeKind.Entered, npc, null) { Cell = tile });
        _notices.Add(new(AreaAiNoticeKind.Entered, npc, null) { Cell = last });
        return (tiles.Count, $"{from.X},{from.Y} -> {last.X},{last.Y}");
    }

    /// <summary>
    /// ARC-MAGIC-038 (ledger TP3, 0x4524C0 case 1): the spell Movement component's Random_Tile_In_Radius for an NPC or companion this host runs: the placement 0x4F4E40 once (<see cref="DropPlacementRules.PlaceOnce"/>) from its
    /// tile with the direction the cast host drew, its owner kind by its leader, then the same immediate path relocation to the tile it found (<see cref="RelocateAlongPath"/>). Nothing moves when a step of the radius walk leaves
    /// the map (the original's output stays zero) or when the placement keeps the NPC's own tile.
    /// </summary>
    public (int Tiles, string Result) RelocateToRandomTile(string npc, int radius, int direction, IDropMap map)
    {
        ArgumentException.ThrowIfNullOrEmpty(npc);
        ArgumentNullException.ThrowIfNull(map);
        if (!_followers.ContainsKey(npc) && !_npcs.ContainsKey(npc)) return (0, "not run by the host");
        if (Tile(npc) is not { } from) return (0, "no tile");
        if (radius < 0 || direction is < 0 or > DropPlacementRules.MaxDirection) return (0, $"radius {radius} direction {direction} out of range");
        var chosen = DropPlacementRules.PlaceOnce(from, radius, Leader(npc) is null ? DropOwner.Npc : DropOwner.PcSide, map, direction);
        if (chosen is not { } to) return (0, "a step of the radius walk leaves the map");
        return to == from ? (0, "the placement kept the tile") : RelocateAlongPath(npc, to, map);
    }

    // Whether the shown map holds a resident on this world cell: it lies on the chart (a map with a loaded-sector chart, the main map's windows: anywhere on the map, ARC-DEV-119),
    // its tile is admitted and the hero is not on it.
    private bool PlaceHeld(CellCoord world) =>
        (_scene.LoadedSteps is not null || _scene.Chart.Contains(ToChart(world))) && Admitted(new AreaStepCell(world.X, world.Y)) && _scene.HeroCell() != world;
}
