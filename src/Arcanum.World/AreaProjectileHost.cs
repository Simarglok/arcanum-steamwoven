using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// One projectile in flight on the shown map (ARC-COMBAT-016): the created object that owns its run (<see cref="Entity"/>, ARC-DEV-140), the session's shot it carries (the decided outcome), what it was shot at (the struck object of
/// the shot: the goal's target, or the line of fire's blocker), the destination (the goal target's tile at the shot), its flight and carried outcome, and its faced art. The shooter, the carried outcome and the line are the host's: the
/// original keeps the shooter in slot 4 of the run and the outcome in the projectile's fields (GT55).
/// </summary>
public sealed class AreaProjectile
{
    /// <summary>The projectile object's entity (<c>projectile:n</c>); set when the host launches the projectile.</summary>
    public string Entity { get; set; } = "";
    public required SessionShot Shot { get; init; }
    /// <summary>The session that holds the shot: a projectile of another session (a reload, a carried session) is given up.</summary>
    public required GameSessionOwner Owner { get; init; }
    public required int Map { get; init; }
    public required AreaObjectRecord Target { get; init; }
    /// <summary>The viewer's own record of an aimed critter (opaque to the host).</summary>
    public object? Critter { get; init; }
    public AreaObjectAttackTarget? Object { get; init; }
    /// <summary>The goal target's tile; ARC-COMBAT-019: a returning boomerang's is the hero's tile at its turn.</summary>
    public required CellCoord Destination { get; set; }
    public required uint? Art { get; init; }
    public required ProjectileFlight Flight { get; set; }
    public required ProjectileShot State { get; set; }
    /// <summary>ARC-COMBAT-017: the thrown item the projectile carries, which lands where it ends; null for a weapon kept in the hand.</summary>
    public SessionThrownShot? Thrown { get; init; }
    /// <summary>ARC-COMBAT-018: a missed throw's scatter cleared its target: it flies free to its scattered tile and ends leaving it.</summary>
    public bool Targetless { get; init; }
    /// <summary>ARC-COMBAT-019: a boomerang's projectile, which turns back to the hero at its first end (flag 0x1000).</summary>
    public bool Returns { get; init; }
    /// <summary>ARC-UI-063 (PF11): the projectile's parent item is not a weapon (a thrown bottle): it is drawn on a size arc, <see cref="Scale"/>.</summary>
    public bool SizeArc { get; init; }
    /// <summary>ARC-UI-063: the size in percent a <see cref="SizeArc"/> projectile is drawn at, set when it leaves a tile (<see cref="ProjectileRules.ThrownScale"/>); 100 before its first crossing.</summary>
    public int Scale { get; set; } = 100;
    /// <summary>The steps per tick (the weapon's eye-candy column, or the default 4 when it is not read).</summary>
    public int StepsPerTick { get; init; } = ProjectileRules.DefaultStepsPerTick;
    public int Frames { get; set; } = 1;
    /// <summary>The ticks the run took.</summary>
    public long Ticks { get; set; }
}

/// <summary>What the viewer did with the tile a projectile left (<see cref="IAreaProjectileViewer.Crossed"/>): it flies on, it ended (the viewer finished it through <see cref="AreaProjectileHost.Finish"/>), or a boomerang turned back.</summary>
public enum ProjectileCrossResult { Flies, Ended, Turned }

/// <summary>
/// What the projectile host needs of the viewer (the strike, the item's landing and the map are the viewer's): the cells of the shown map for a projectile, the tile-left handling of 0x4B2870 and the flight goal's own end.
/// </summary>
public interface IAreaProjectileViewer
{
    /// <summary>The cells of the shown map a projectile flew on (a flight off them ends), or null when the shown map or session is another one (the flight is given up).</summary>
    Func<CellCoord, bool>? LineMap(AreaProjectile projectile);
    /// <summary>The tile left (0x4B2870, called before the projectile moves on): what the flight did there. An end finishes the projectile (<see cref="AreaProjectileHost.Finish"/>) before it answers.</summary>
    ProjectileCrossResult Crossed(AreaProjectile projectile, ProjectileCrossing crossing);
    /// <summary>The flight goal's own end (0x42CAC0 → 0x4B2650 → 0x4B2690 without a target or the trigger): <paramref name="land"/> is true when the goal ended by itself (a point off the map, a start on the destination, a refused line:
    /// a thrown item is placed on the projectile's tile and a boomerang gets its second end), false when the flight is given up (the map or session changed, an interrupt, a close). The projectile is finished already.</summary>
    void GoalEnded(AreaProjectile projectile, bool land, string why);
}

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, docs/architecture/goal-table.md §3.5, the S8 read GT53–GT55, GT62, GT63): the host of the shown map's projectiles as owners of goal runs. A projectile is a created object of the session
/// (<see cref="SessionProjectile"/>, <c>projectile:n</c>, unsaved) whose run is goal 36 (<c>ag_projectile</c>), stepped on the animation clock inside the frame step (<see cref="IGoalStepHost"/>): the path nodes build the
/// line once, then a tick of steps every 35 ms leaves each tile (the viewer handles the crossing) until the projectile ends, which pops the run and runs the end node. The run has priority 5 for its whole flight, so no
/// sweep but the map's close ends it (GT62). This host answers the projectile ports (<see cref="IAiProjectileWorld"/>) over the session; the strike, the landing and the map are the viewer's (<see cref="IAreaProjectileViewer"/>).
/// </summary>
public sealed class AreaProjectileHost : IAiProjectileWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly IAreaProjectileViewer _viewer;
    private readonly Dictionary<string, AreaProjectile> _projectiles = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly StartPorts _startPorts;
    private bool _givingUp;

    public AreaProjectileHost(GameSessionOwner session, IAreaProjectileViewer viewer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _startPorts = new(this);
    }

    /// <summary>The session this host's projectiles belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (the viewer draws again when it moved).</summary>
    public int Steps { get; private set; }

    /// <summary>The projectiles in flight, in the order they left.</summary>
    public IReadOnlyList<AreaProjectile> Projectiles => [.. _order.Select(e => _projectiles[e])];

    public int Count => _projectiles.Count;

    /// <summary>Whether <paramref name="projectile"/> is in flight in this host.</summary>
    public bool Owns(AreaProjectile projectile) => _projectiles.TryGetValue(projectile.Entity, out var own) && ReferenceEquals(own, projectile);

    /// <summary>
    /// The launch of the ranged path (0x4B24F0): the projectile object is created at <paramref name="start"/> and its run, goal 36 for <paramref name="destination"/> (<paramref name="struck"/> the struck object's entity, null
    /// for a flight at a tile), starts (the starter 0x435870: level-4 interrupt of the projectile's runs, none yet; no gate; its first step comes 5 animation milliseconds later). False, with the projectile given up
    /// (<see cref="IAreaProjectileViewer.GoalEnded"/> with no landing), when the object or the run could not be made (the original leaves an orphan on a full table, GT54).
    /// </summary>
    public bool Launch(AreaProjectile projectile, CellCoord start, string? struck)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        var entity = _session.CreateProjectile(start);
        if (entity is null) { _viewer.GoalEnded(projectile, false, "the projectile could not be created"); return false; }
        projectile.Entity = entity;
        _projectiles[entity] = projectile;
        _order.Add(entity);
        var started = _session.StartGoal(new GoalStartRequest(entity, GoalStarters.Projectile, AiExecutorRules.ProjectileFrame(struck, projectile.Destination)), _startPorts);
        foreach (var end in started.Ended) EndNodes(end);
        if (!started.Started)
        {
            if (_projectiles.ContainsKey(entity)) { Finish(projectile, killRun: true); _viewer.GoalEnded(projectile, false, "its run could not start"); }
            return false;
        }
        return true;
    }

    /// <summary>The projectile ended: it leaves the host and its object is destroyed. The run, if it still goes, pops on its next tick and its end node finds nothing left (the original's destroy marks the stepped run done, 0x43CCA0).
    /// Idempotent.</summary>
    public void Finish(AreaProjectile projectile, bool killRun = false)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        if (!Owns(projectile)) return;
        _projectiles.Remove(projectile.Entity);
        _order.Remove(projectile.Entity);
        try
        {
            if (killRun) _session.KillGoals(projectile.Entity);
            _session.DestroyProjectile(projectile.Entity);
        }
        catch (ObjectDisposedException)
        {
            // A replaced session: nothing of it is left to clean.
        }
    }

    /// <summary>Every projectile is given up (a map change, a quick load, a replaced session): the shots end, the objects are destroyed and their runs killed (no end node runs twice).</summary>
    public void LoseAll(string why)
    {
        foreach (var entity in _order.ToArray())
            if (_projectiles.TryGetValue(entity, out var projectile)) GiveUp(projectile, why, killRun: true);
    }

    /// <summary>The end nodes of a projectile's run the table ended from outside the stepper (a map's close, a teleport): the flight is given up (the object belongs to the map that closed). A reset runs none, a killed run skips them (GS21)
    /// and a run the stepper finished ran them itself; the projectile that remains in any of those cases is given up all the same, since no one else would end it.</summary>
    public void EndNodes(GoalRunEnd end)
    {
        if (!_projectiles.ContainsKey(end.Run.Owner)) return;
        if (end.Kind is not (GoalEndKind.Reset or GoalEndKind.Finished)) GivingUp(() => AiExecutorRules.EndProjectileRun(end.Run, this));
        if (_projectiles.TryGetValue(end.Run.Owner, out var left)) GiveUp(left, "its run ended");
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        // A projectile of another host (a replaced session, a map left) has nothing here: its run ends.
        if (!_projectiles.ContainsKey(run.Owner)) return SessionGoalStepAnswer.End;
        Steps++;
        var stepped = GoalStepper.StepProjectile(run, this);
        if (stepped.End is GoalStepperEnd.Killed or GoalStepperEnd.NodeLimit or GoalStepperEnd.Overflow) GivingUp(() => AiExecutorRules.EndProjectileRun(run, this));
        if (_projectiles.TryGetValue(run.Owner, out var left) && stepped.Answer.NextDelayMs is null) GiveUp(left, "its run ended");
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    private void GivingUp(Action action)
    {
        var before = _givingUp;
        _givingUp = true;
        try { action(); }
        finally { _givingUp = before; }
    }

    // A flight given up: the viewer ends the shot without a landing (the map or the session is no longer the one it flew on).
    private void GiveUp(AreaProjectile projectile, string why, bool killRun = false)
    {
        if (!Owns(projectile)) return;
        Finish(projectile, killRun);
        _viewer.GoalEnded(projectile, false, why);
    }

    // ---- the projectile ports ----

    private AreaProjectile? Of(string projectile) => _projectiles.GetValueOrDefault(projectile);

    public bool ProjectileAtDestination(string projectile, CellCoord destination) => Of(projectile) is not { } p || p.Flight.Tile == destination;

    public bool ProjectileFace(string projectile) => Of(projectile) is not null;

    public bool ProjectileBuildLine(string projectile, CellCoord destination)
    {
        if (Of(projectile) is not { } p) return false;
        // The line from the tile the projectile is on to the destination (PF3; a boomerang's turn builds it from the tile it turned on, BR4).
        if (ProjectileRules.Launch(p.Flight.Tile, destination) is not { } flight) return false;
        p.Flight = flight;
        return true;
    }

    public bool ProjectileSetup(string projectile) => Of(projectile) is not null;

    public ProjectileTickAnswer ProjectileTick(string projectile)
    {
        if (Of(projectile) is not { } p) return ProjectileTickAnswer.Ended;
        // The object was destroyed under its run (the directory's one destroy path): the flight is over.
        if (_session.QueryProjectile(p.Entity) is null) { GiveUp(p, "its object was destroyed"); return ProjectileTickAnswer.Ended; }
        if (_viewer.LineMap(p) is not { } contains) { GiveUp(p, "the map or session changed"); return ProjectileTickAnswer.Ended; }
        p.Ticks++;
        var turned = false;
        for (var i = 0; i < p.StepsPerTick && Owns(p); i++)
        {
            var (next, crossing) = ProjectileRules.Step(p.Flight, contains);
            // A point off the map fails the goal: the tick answers false and the end node places the thrown item (PF4).
            if (next is null) return ProjectileTickAnswer.Ended;
            // The tile left is handled before the projectile moves into the next one (0x4B2870); a projectile that ended or turned back does not take this step.
            if (crossing is { } c)
            {
                var crossed = _viewer.Crossed(p, c);
                if (crossed == ProjectileCrossResult.Ended || !Owns(p)) return ProjectileTickAnswer.Ended;
                if (crossed == ProjectileCrossResult.Turned) { turned = true; break; }
            }
            p.Flight = next;
        }
        if (Owns(p)) Place(p);
        return turned && Owns(p) ? new(ProjectileTickResult.Turned, p.Destination) : ProjectileTickAnswer.Continue;
    }

    // The projectile object follows the flight: the tile it is on and its pixel offset from that tile's centre.
    private void Place(AreaProjectile p)
    {
        var start = LineOfFireRules.Screen(p.Flight.Start);
        var here = LineOfFireRules.Screen(p.Flight.Tile);
        _session.MoveProjectile(p.Entity, p.Flight.Tile, checked((int)(start.X + p.Flight.X - here.X)), checked((int)(start.Y + p.Flight.Y - here.Y)));
    }

    public void ProjectileEnded(string projectile)
    {
        if (Of(projectile) is not { } p) { _session.DestroyProjectile(projectile); return; }
        var land = !_givingUp;
        Finish(p);
        _viewer.GoalEnded(p, land, land ? "the flight goal ended" : "its run ended");
    }

    // The table's start ports: a projectile's start passes every gate (the starter 0x435870 calls none, GT54); nothing loads; a destroyed projectile is not started for.
    private sealed class StartPorts(AreaProjectileHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => host._session.QueryProjectile(owner) is null;
    }
}
