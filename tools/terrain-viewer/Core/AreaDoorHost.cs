using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Locks;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-138 (MIG13 S7, docs/architecture/goal-table.md §3.5, the S7 read GT38–GT51): the host of the shown map's doors and windows as owners of goal runs. A door's change is its own
/// run on the session's table: <see cref="Open"/> starts goal 70 (the opening: the first open step at once, one more per ART frame delay, the open sound, then goal 72 after 1.5 s unless the portal
/// keeps itself open), 72 tests the doorway every 1.5 s (no close in a round, none with a living critter on the door's tile) and starts 71, the closing, whose last step closes the door;
/// <see cref="Close"/> starts 71 at once. The run is the start of the original's goals 12 and 50 (their node 8 and node 4): the opener's own goals 11, 12, 49 and 50 are nodes of the
/// executor (<see cref="AiExecutorRules.StepDoor"/>) that the hero's click and the walkers' door step will push in the slices S10 and S11; until then the viewer decides the open attempt itself
/// (<see cref="AreaPortals.Decide"/>) and charges the action points. The runs step on the animation clock inside the frame step (<see cref="IGoalStepHost"/>), so they stop with the pause and take
/// a round's time jump once; each step writes the session's frame (the passage rule reads it), so a door is passable from its first open step and blocks again at its last close step.
/// This host answers the door ports from the session and the portals; a critter's tiles and the sounds are the viewer's.
/// </summary>
public sealed class AreaDoorHost : IAiDoorWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly AreaPortals _portals;
    private readonly Dictionary<string, AreaPortal> _byEntity;
    private readonly Func<IEnumerable<AreaStepCell>> _critters;
    private readonly Action<AreaPortal, DoorSound> _sound;
    private readonly StartPorts _startPorts;

    /// <param name="critters">The tiles of the living player characters and NPCs of the map (world cells), read when a pending close tests the doorway.</param>
    /// <param name="sound">Plays a door's sound at its place.</param>
    public AreaDoorHost(GameSessionOwner session, AreaPortals portals, Func<IEnumerable<AreaStepCell>> critters, Action<AreaPortal, DoorSound> sound)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _portals = portals ?? throw new ArgumentNullException(nameof(portals));
        _critters = critters ?? throw new ArgumentNullException(nameof(critters));
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        _byEntity = portals.All.ToDictionary(p => p.Entity, StringComparer.Ordinal);
        _startPorts = new(this);
    }

    /// <summary>
    /// ARC-DEV-176 (MIG13 S11d): the viewer's side of the hero as opener. <see cref="HeroUse"/> is the use node of goal 12 for the hero (0x428A10: the action points, the door's use script): false ends the run.
    /// <see cref="HeroNotice"/> reports what a door goal of the hero did (a refusal of the open attempt: locked, jammed, held, script; the opening or closing started) for the status line and the locked sound.
    /// </summary>
    public Func<AreaPortal, bool>? HeroUse { get; set; }
    public Action<AreaPortal, AreaPortalUse>? HeroNotice { get; set; }

    /// <summary>The session this host's doors belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>Whether <paramref name="entity"/> is a door or window of the shown map.</summary>
    public bool Owns(string entity) => _byEntity.ContainsKey(entity);

    /// <summary>Starts the door's opening as its own run (goal 12's node 8, starter 0x4246E0): the runs of the door end first (level 3), the first step comes 5 animation milliseconds later. False
    /// when the table refused the start. <paramref name="opener"/> is the critter that opened it.</summary>
    public bool Open(AreaPortal portal, string opener) => Start(portal, GoalType.AnimateDoorOpen, opener);

    /// <summary>Starts the door's closing (goal 50's node 4): a magically held door refuses (the caller decided; the run's own begin does not test it).</summary>
    public bool Close(AreaPortal portal, string opener) => Start(portal, GoalType.AnimateDoorClosed, opener);

    /// <summary>The fallback of a start the table refused (spell runs hold it, the queue is full): the final open frame is written at once, as before the door had a run, so a step the actor already
    /// took through the door does not cross a portal that stays shut. False when the portal cannot open or the session refused.</summary>
    public bool OpenNow(AreaPortal portal) => portal.OpenFrame != 0
        && _session.SetOpenableFrame(_session.Version, _portals.Openable(portal), portal.Baseline, portal.OpenFrame).Disposition != GameSessionDisposition.Rejected;

    /// <summary>The door's runs in the table (an opening, a pending close or a closing), in slot order.</summary>
    public IReadOnlyList<GoalRun> RunsOf(AreaPortal portal) => _session.QueryGoals().RunsOf(portal.Entity);

    /// <summary>The goal a door is playing now (70, 71 or 72), or null when it has no run.</summary>
    public GoalType? GoalOf(AreaPortal portal) => RunsOf(portal).FirstOrDefault()?.Top.Goal;

    private bool Start(AreaPortal portal, GoalType goal, string opener)
    {
        var started = _session.StartGoal(new GoalStartRequest(portal.Entity, GoalStarters.Generic, AiExecutorRules.DoorRunFrame(goal, opener)), _startPorts);
        foreach (var end in started.Ended) EndNodes(end);
        return started.Started;
    }

    /// <summary>The end nodes of a door run the table ended from outside the stepper (a start's interrupts, a map's close): a closing that was cut short closes the door. A reset runs none
    /// and a killed run skips them (GS21). The AI host hands the ends it drains here.</summary>
    public void EndNodes(GoalRunEnd end)
    {
        if (end.Kind is GoalEndKind.Reset or GoalEndKind.Killed || !_byEntity.ContainsKey(end.Run.Owner)) return;
        AiExecutorRules.EndDoorRun(end.Run, this);
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        // A door of another window of the map (the view moved on) has no portal here: its run ends and the door stays where it stands.
        if (!_byEntity.ContainsKey(run.Owner)) return SessionGoalStepAnswer.End;
        Steps++;
        var stepped = GoalStepper.StepDoor(run, this);
        if (stepped.End is GoalStepperEnd.Killed or GoalStepperEnd.NodeLimit or GoalStepperEnd.Overflow) AiExecutorRules.EndDoorRun(run, this);
        // A node that ended the run from outside (its own start's interrupt) ran the end nodes already; the session's close ends the marked run.
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    // ---- the door ports ----

    private AreaPortal? Portal(string door) => _byEntity.GetValueOrDefault(door);

    public bool DoorValid(string door) => Portal(door) is { } portal
        && _session.QueryObjectCondition(_portals.Map, portal.Entity, DamageableObjectKind.Portal) != SessionObjectCondition.Destroyed;

    public bool DoorOpen(string door) => Portal(door) is { } portal && _portals.Frame(_session, portal) != 0;

    public bool DoorBusy(string door) => _session.QueryGoals().MainRunOf(door) is not null;

    public bool DoorRefusesOpen(string opener, string door)
    {
        if (Portal(door) is not { } portal) return true;
        var refusal = _portals.OpenRefusal(_session, portal);
        if (refusal is { } why && opener == AreaHeroRunsHost.Hero) HeroNotice?.Invoke(portal, why);
        return refusal is not null;
    }

    public bool UseDoor(string opener, string door) => opener != AreaHeroRunsHost.Hero || Portal(door) is not { } portal || HeroUse?.Invoke(portal) != false;

    public bool DoorHeld(string door) => Portal(door) is { } portal && (State(portal).Flags & LockRules.MagicallyHeldBit) != 0;

    public bool DoorStaysOpen(string door) => Portal(door) is { } portal && (State(portal).Flags & LockRules.StaysOpenBit) != 0;

    public bool DoorRound => _session.QueryCombatRound() is not null;

    public bool DoorwayOccupied(string door) => Portal(door) is { } portal && _critters().Any(cell => cell == portal.Cell);

    public bool StartDoorRun(string door, GoalType goal, string opener)
    {
        if (Portal(door) is not { } portal || !Start(portal, goal, opener)) return false;
        if (opener == AreaHeroRunsHost.Hero) HeroNotice?.Invoke(portal, goal == GoalType.AnimateDoorOpen ? AreaPortalUse.Opened : AreaPortalUse.Closed);
        return true;
    }

    /// <summary>The portal of a door's session entity, or null when it is not a portal of the shown map.</summary>
    public AreaPortal? PortalOf(string entity) => Portal(entity);

    public bool DoorStep(string door, bool opening)
    {
        if (Portal(door) is not { } portal) return false;
        var frame = _portals.Frame(_session, portal);
        if ((opening ? PortalFrameRules.OpenStep(portal.Art, frame) : PortalFrameRules.CloseStep(portal.Art, frame)) is not { } step) return false;
        return _session.SetOpenableFrame(_session.Version, _portals.Openable(portal), portal.Baseline, step.Frame).Disposition != GameSessionDisposition.Rejected;
    }

    public int DoorStepMs(string door) => Portal(door) is { } portal ? (int)Math.Min(portal.StepMs, int.MaxValue) : 100;

    public void PlayDoorSound(string door, DoorSound sound)
    {
        if (Portal(door) is { } portal) _sound(portal, sound);
    }

    public void SnapDoorClosed(string door)
    {
        if (Portal(door) is { } portal)
            _session.SetOpenableFrame(_session.Version, _portals.Openable(portal), portal.Baseline, PortalFrameRules.Closed);
    }

    private SessionOpenableState State(AreaPortal portal) => _session.QueryOpenable(_portals.Openable(portal), portal.Baseline);

    // The table's start ports: a door's start passes every gate (the starter 0x4246E0 calls none, GT42); nothing loads; a destroyed door is not started for.
    private sealed class StartPorts(AreaDoorHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => host.Portal(owner) is { } portal
            && host._session.QueryObjectCondition(host._portals.Map, portal.Entity, DamageableObjectKind.Portal) == SessionObjectCondition.Destroyed;
    }
}
