using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What the hero's runs host needs of the viewer's body: the movement gate and the route the viewer's feet walk (all tiles are world cells).</summary>
public interface IAreaHeroRunsViewer
{
    /// <summary>The movement gate 0x4339A0: the hero can act and, in a round, holds the turn. A walk is not started (nor retargeted) otherwise.</summary>
    bool MayStart();
    /// <summary>The hero's route follows the world tile <paramref name="destination"/> now (a far goal the route follower walks, ARC-DEV-149).</summary>
    bool Walking(CellCoord destination);
    /// <summary>ARC-DEV-187: the stun goal's end node: the hero's stunned bit is cleared (idempotent).</summary>
    void HeroStunEnds();
    /// <summary>The walk's end node: stops the route that follows <paramref name="destination"/> or the step the walk gave it (nothing when it follows another goal or none).</summary>
    void StopWalking(CellCoord destination, string why);
    /// <summary>ARC-DEV-149: the world tile the hero stands on or its step under way enters; null when it has none on the map.</summary>
    CellCoord? HeroTile { get; }
    /// <summary>ARC-DEV-149: the hero's encumbrance level.</summary>
    int HeroEncumbrance { get; }
    /// <summary>ARC-DEV-149: the hero is down (falling, unconscious, getting up, dead) and takes no step.</summary>
    bool HeroDown { get; }
    /// <summary>ARC-DEV-149: the hero stands settled on its cell and can act (goal 53's turn plays only then).</summary>
    bool HeroSettled { get; }
    /// <summary>ARC-DEV-149: a turn-based round runs with the hero in combat mode.</summary>
    bool InRound { get; }
    /// <summary>ARC-DEV-149: the planner for the walk to <paramref name="destination"/> (node 0x426040): the walk's budget, the PC's straight-line retry, or a far goal the route follower takes.</summary>
    AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight);
    /// <summary>ARC-DEV-149: whether the step <paramref name="from"/> → <paramref name="next"/> is blocked beyond the grid's own refusal (the Original setting's critter test before a segment's last step).</summary>
    AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment);
    /// <summary>ARC-DEV-149: the walk's step on the hero's body (the route follower issues it at the exact step end): the milliseconds it takes, or 0 when it cannot be taken.</summary>
    int BeginStep(CellCoord from, CellCoord next, bool running);
    /// <summary>ARC-DEV-163: the step <paramref name="from"/> → <paramref name="to"/> the walk gave is queued on the hero's body and has not begun (the route follower issues it at the exact end of the step under way); false once taken, refused or lost.</summary>
    bool StepPending(CellCoord from, CellCoord to);
    /// <summary>ARC-DEV-149: hands the whole walk to the route follower (a far goal beyond the planner's reach); false when it refuses.</summary>
    bool FollowRoute(CellCoord destination);
    /// <summary>The rotation 0..7 the hero's art shows now (a turn in place counts).</summary>
    int HeroFacing { get; }
    /// <summary>ARC-DEV-148: the hero still stands where its turn began (settled on that cell, able to act): goal 53's step goes on; a hero that walked, fell or changed map ends the turn.</summary>
    bool TurnHolds { get; }
    /// <summary>ARC-DEV-148: goal 53's step (0x42C440) sets the art's rotation: the viewer draws it (a settled hero turned in place).</summary>
    void ShowFacing(int rotation);
    /// <summary>The rotate goal's end node (0x42C650): the art's rotation snaps to <paramref name="rotation"/>, the goal's target, when it differs.</summary>
    void SnapFacing(int rotation);

    // ---- ARC-DEV-169 (MIG13 S11a): the hero's talk ----

    /// <summary>The talk target <paramref name="target"/> (a session entity string the viewer gave the goal): its world cell and whether node 1 refuses it; null when it is gone.</summary>
    AiHeroTarget? ObjectTarget(string target) => null;
    /// <summary>The world cell next to <paramref name="target"/> the hero walks to (goal 8's plan); null when there is none.</summary>
    CellCoord? ApproachTile(string target, int range) => null;
    /// <summary>Node 0x42AF00's call to the interface: the dialog with <paramref name="target"/> is asked for (the viewer opens it at the frame's next safe point under its own guards).</summary>
    void OpenDialog(string target) { }
    /// <summary>ARC-DEV-174: the act of the use goals 38 and 81 (node 0x428A10): the use of <paramref name="target"/> is asked for (the viewer applies it at the frame's next safe point under its own guards).</summary>
    void UseObject(string target) { }
    /// <summary>ARC-DEV-175: the act of goal 16 (node 0x42A9B0): the hero takes <paramref name="target"/> into its inventory (the viewer applies it at the frame's next safe point under its own guards).</summary>
    void PickUpObject(string target) { }
    /// <summary>ARC-DEV-176: the door ports the opener goals 11, 12, 49 and 50 of the hero's use of a door run over; null without a door host.</summary>
    IAiDoorWorld? Doors => null;

    // ---- ARC-DEV-181 (MIG13 S12a): the hero's attack (goals 18 and 19) ----

    /// <summary>The attack target <paramref name="target"/> (the string the viewer gave the goal): its world cell and whether it is dead or broken; null when it is gone.</summary>
    AiHeroAttackTarget? AttackTarget(string target) => null;
    /// <summary>The attack is possible (a weapon the hero can attack with, no boomerang away); the viewer says why not on the status line.</summary>
    bool AttackPossible(string target) => false;
    /// <summary>The reach test (0x429960) from the hero's cell.</summary>
    bool InAttackReach(string target) => false;
    /// <summary>The world cell the hero walks to so as to attack; null when there is none.</summary>
    CellCoord? AttackApproachTile(string target) => null;
    /// <summary>The attack's walk may stop here: Steamwoven's ranged-approach stop at the first cell with a clear shot (EX9).</summary>
    bool AttackWalkInReach(string target) => false;
    /// <summary>The attack's start: the turn, the animation, the action points; null when it cannot start.</summary>
    AiAttackSwing? HeroBeginAttack(string target) => null;
    /// <summary>The hit step at the action frame.</summary>
    void HeroAttackHit(string target) { }
    /// <summary>The repeat test after the swing: the hero attacks again.</summary>
    bool HeroAttackRepeats(string target) => false;
    /// <summary>An attack goal ends the run (the viewer clears the attack).</summary>
    void AttackEnds(string target, AiExecutorReason reason) { }
    /// <summary>ARC-DEV-182: the throw's start (the art 14, the action points); null when it cannot start.</summary>
    AiAttackSwing? HeroBeginThrow(string item, CellCoord tile) => null;
    /// <summary>ARC-DEV-182: the throw at the action frame.</summary>
    bool HeroThrowItem(string item, CellCoord tile) => false;
    /// <summary>ARC-DEV-182: the throw run ended (thrown, refused or interrupted): the swing's pose is cleared.</summary>
    void HeroThrowEnds(string item, AiExecutorReason reason) { }
    AiCastGoalStart HeroStartCast(int run) => AiCastGoalStart.Refused;
    bool HeroFaceCast(int run) => true;
    AiCastSwing HeroBeginCast(int run) => new(false);
    bool HeroCastActionFrame(int run) => false;
    bool HeroReleaseCast(int run) => false;
    void HeroCastEnds(int run, HeroCastEnd how) { }
    /// <summary>ARC-DEV-183: a cast run was ended by an interrupt or a kill (not by its own end): the cast host frees the run and drops the queue.</summary>
    void HeroCastInterrupted(int run, string why) { }
    bool HeroFaceSkill(string target) => true;
    void HeroPickPocket(string target, string item, bool plant) { }
    void HeroPickLock(string target, string item) { }
}

/// <summary>What a request for a walk run did.</summary>
public enum HeroWalkRunResult
{
    /// <summary>A new run of goal 3 or 4 was started (the interrupts of the walk starter ended what they ended).</summary>
    Started,
    /// <summary>The running walk now goes to the new destination: the same run, its path flag "replan" set (GS14).</summary>
    Retargeted,
    /// <summary>The running walk already goes to that destination: nothing changed.</summary>
    Unchanged,
    /// <summary>The movement gate or a run's refusal stopped the walk (the hero is not the round's participant, a casting run holds the table), or the table could not take it.</summary>
    Refused,
}

/// <summary>
/// ARC-DEV-146 (MIG13 S10b, docs/architecture/goal-table.md §3.5, GT14): the hero's runs on the shown map's goal table, owner <c>pc</c> (<see cref="AiSchedulerRules.Pc"/>). The tile click is
/// goal 3 (move to tile) or, by the always-run rule, goal 4 (run to tile): <see cref="Walk"/> is the walk starter 0x433640 / the run starter 0x433C80 (the movement gate, then a run that walks already gets its
/// destination overwritten in place and its replan flag set, GS14; otherwise a new run starts after the level-3 interrupt of the hero's runs, GS12). The run is the <b>hero's walk as the table knows it</b>:
/// its lifetime, destination, priority 2 and interrupts are the table's (a right release, a round's start sweep, a map's close end it through the table's rules, and its end node stops the route), while
/// ARC-DEV-149 (S10b-2b): the table's walk nodes plan the path and take its steps one table step at a time over the hero ports (<see cref="IAiHeroWalkWorld"/>); the viewer's route follower is only the feet of a given step (and walks a far goal beyond the planner's reach whole). ARC-DEV-148 (S10b-2a): goal 53 is stepped here (<see cref="StepTurn"/>), the viewer only draws the art's rotation.
/// The run is stepped inside the frame step (<see cref="IGoalStepHost"/>); a far goal's run lives while the route follows its destination.
/// </summary>
public sealed class AreaHeroRunsHost : IGoalStepHost
{
    /// <summary>The hero's owner string on the table.</summary>
    public const string Hero = AiSchedulerRules.Pc;

    /// <summary>The wait of a step that found the route still walking: a frame (the host's steps are one a frame).</summary>
    public const long PollMs = 16;

    private readonly GameSessionOwner _session;
    private readonly IAreaHeroRunsViewer _viewer;
    private readonly StartPorts _ports;
    private readonly WalkPorts _walkPorts;
    private bool _requesting;

    public AreaHeroRunsHost(GameSessionOwner session, IAreaHeroRunsViewer viewer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _ports = new(this);
        _walkPorts = new(this);
    }

    /// <summary>The session this host's runs belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>The runs that ended because the route no longer followed their destination, or the turn no longer played.</summary>
    public int RouteEnds { get; private set; }

    /// <summary>Whether <paramref name="run"/> is a walk of the hero this host steps: the bottom goal 3 or 4 of the owner <c>pc</c>.</summary>
    public static bool IsHeroWalk(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.MoveToTile or GoalType.RunToTile;

    /// <summary>Whether <paramref name="run"/> is a turn of the hero this host steps: the bottom goal 53 (rotate) of the owner <c>pc</c>.</summary>
    public static bool IsHeroTurn(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.Rotate;

    /// <summary>Whether <paramref name="run"/> is a run of the hero this host steps: its walk (3, 4) or its turn (53).</summary>
    public static bool IsHeroRun(GoalRun run) => IsHeroWalk(run) || IsHeroTurn(run) || IsHeroTalk(run) || IsHeroUse(run) || IsHeroPickup(run) || IsHeroAttack(run) || IsHeroThrow(run) || IsHeroCast(run) || IsHeroSkill(run) || IsHeroStun(run);

    /// <summary>ARC-DEV-187 (MIG13 S13a): the hero's stun run (goal 57): the session starts it from a life step that stunned the hero, which interrupts the hero's other runs (levels 4 then 1).</summary>
    public static bool IsHeroStun(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.AnimateStunned;

    /// <summary>ARC-DEV-187: the hero's stun run on the table, or null.</summary>
    public GoalRun? StunRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroStun);

    /// <summary>ARC-DEV-169: whether <paramref name="run"/> is a talk of the hero this host steps: the bottom goal 21 (talk) of the owner <c>pc</c>.</summary>
    public static bool IsHeroTalk(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.Talk;

    /// <summary>ARC-DEV-174: whether <paramref name="run"/> is a use of the hero this host steps: the bottom goal 38 (use object) or 81 (use container) of the owner <c>pc</c>, or (ARC-DEV-176) the door goal 38 turned into at its node 6 (11, 12, 49, 50).</summary>
    public static bool IsHeroUse(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.UseObject or GoalType.UseContainer or GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor;

    /// <summary>ARC-DEV-175: whether <paramref name="run"/> is a pickup of the hero this host steps: the bottom goal 15 (pick up an item) of the owner <c>pc</c>.</summary>
    public static bool IsHeroPickup(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.PickupItem;

    /// <summary>ARC-DEV-182: the hero's throw run (goal 37).</summary>
    public static bool IsHeroThrow(GoalRun run) => run.Owner == Hero && run.Bottom.Goal == GoalType.ThrowItem;

    /// <summary>ARC-DEV-184: the hero's skill run (goal 33, or goal 86 once it replaced it, and what they pushed: the approach 8, the walk 3).</summary>
    public static bool IsHeroSkill(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.UseSkill or GoalType.AttemptPickpocket or GoalType.AttemptPickLocks;

    /// <summary>ARC-DEV-184: the hero's skill run on the table, or null.</summary>
    public GoalRun? SkillRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroSkill);

    /// <summary>ARC-DEV-183: the hero's casting run (goal 26, or goal 27 once it replaced it).</summary>
    public static bool IsHeroCast(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.CastSpell or GoalType.AttemptSpell;

    /// <summary>ARC-DEV-183: the hero's casting run on the table, or null.</summary>
    public GoalRun? CastRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroCast);

    /// <summary>ARC-DEV-182: the hero's throw run on the table, or null.</summary>
    public GoalRun? ThrowRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroThrow);

    /// <summary>ARC-DEV-181: the hero's attack run (goal 18, or goal 19 once the target is in reach).</summary>
    public static bool IsHeroAttack(GoalRun run) => run.Owner == Hero && run.Bottom.Goal is GoalType.Attack or GoalType.AttemptAttack;

    /// <summary>ARC-DEV-181: how the last attack run the table stepped ended (the stepper's end and the node's reason), for the status line and the log.</summary>
    public string? LastAttackEnd { get; private set; }

    /// <summary>ARC-DEV-181: the hero's attack run on the table, or null.</summary>
    public GoalRun? AttackRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroAttack);

    /// <summary>ARC-DEV-181: the target string of the hero's attack run, or null.</summary>
    public string? AttackTarget => AttackRun?.Bottom.Target;

    /// <summary>ARC-DEV-181: the hero's walk to its attack target is on (the attack run holds a walk goal above it).</summary>
    public bool AttackWalking => AttackRun is { } run && run.Frames.Any(f => f.Goal is GoalType.MoveToTile or GoalType.RunToTile);

    /// <summary>ARC-DEV-175: the hero's pickup run on the table now (goal 15 and what it pushed: the approach 8, the walk 3, the attempt 16), or null.</summary>
    public GoalRun? PickupRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroPickup);

    /// <summary>ARC-DEV-174: the hero's use run on the table now (goal 38 or 81 and what it pushed: the approach 8, the walk 3), or null.</summary>
    public GoalRun? UseRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroUse);

    /// <summary>ARC-DEV-174: the target of the hero's use run, or null without one.</summary>
    public string? UseTarget => UseRun?.Bottom.Target;

    /// <summary>ARC-DEV-169: the hero's talk run on the table now (goal 21 and what it pushed: the approach 8, the walk 3), or null.</summary>
    public GoalRun? TalkRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroTalk);

    /// <summary>ARC-DEV-169: the target of the hero's talk run, or null without one.</summary>
    public string? TalkTarget => TalkRun?.Bottom.Target;

    /// <summary>The hero's turn run on the table now, or null.</summary>
    public GoalRun? TurnRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroTurn);

    /// <summary>The hero's walk run on the table now, or null.</summary>
    public GoalRun? WalkRun => _session.QueryGoals().RunsOf(Hero).FirstOrDefault(IsHeroWalk);

    /// <summary>The destination the hero's walk run goes to, or null without one.</summary>
    public CellCoord? WalkDestination => WalkRun?.Bottom.Tile;

    /// <summary>
    /// The highest priority of what the hero's runs do now (0 without one): what a right release's interrupt asks of them. A walk run whose route no longer follows its destination is ending (its step ends it
    /// within a frame) and counts for nothing (a turn run counts while it is on the table), so a request in that frame does not meet a run that is already gone in effect.
    /// </summary>
    public int HighestPriority => _session.QueryGoals().RunsOf(Hero).Where(Doing).Select(r => GoalDescriptors.Of(r.Top.Goal)?.Priority ?? 0).DefaultIfEmpty(0).Max();

    // A walk run the table steps counts while it is on the table; one that hands its goal to the route follower (a far goal) counts while the route follows it (ARC-DEV-149).
    private bool Doing(GoalRun run) => !IsHeroWalk(run) || (run.Bottom.Flags & GoalFrameFlags.RouteFollow) == 0 || run.Bottom.Tile is { } destination && _viewer.Walking(destination);

    /// <summary>
    /// The walk starter for a tile click (0x433640, or the run starter 0x433C80 when the always-run rule makes the hero a runner), GT14: the movement gate first; then a run of the hero that walks
    /// (goals 3, 4 and their aliases, <see cref="GoalRunTable.RunRunningGoal"/>) is retargeted in place; otherwise a new run starts after the level-3 interrupt of every run of the hero (a refusal of a
    /// priority-4 or -5 run stops the start). The ends of the runs the start interrupted need no route stop: the caller sets the route's goal itself.
    /// <paramref name="held"/> (ARC-DEV-147, S10c, GT109/GT111): the walk is a repeat of a held input. The run it found or started is marked as a flight (0x400, the original's mark of its held-button repeats), whether it was
    /// retargeted, kept (a repeat on the run's own tile re-marks it) or newly started, and the planner then plans every segment of the run as a straight line. The original's first press of a held button marks nothing;
    /// the stick has no first press that picks a tile, so its first request is already marked (Steamwoven's decision). The mark is the hero's walk run's only: the original's stale-handle slip (a refused starter
    /// marks the last run any start or find touched, GT109) is not modelled.
    /// </summary>
    public HeroWalkRunResult Walk(CellCoord destination, bool run, bool held = false)
    {
        if (!_viewer.MayStart()) return HeroWalkRunResult.Refused;
        var goal = run ? GoalType.RunToTile : GoalType.MoveToTile;
        if (_session.QueryGoals().RunRunningGoal(Hero, goal) is { } running)
        {
            var retarget = _session.RetargetGoal(running.Handle, destination);
            var retargeted = retarget.Outcome switch
            {
                GoalRetargetOutcome.Retargeted => HeroWalkRunResult.Retargeted,
                GoalRetargetOutcome.Unchanged => HeroWalkRunResult.Unchanged,
                _ => HeroWalkRunResult.Refused,
            };
            // (The mark is refused only inside a table pass or a goal step, where this starter is never called from; the next repeat marks it again.)
            if (held && retargeted != HeroWalkRunResult.Refused) _session.TurnOnRunFlags(running.Handle, GoalRunFlags.Flight);
            return retargeted;
        }
        var request = new GoalStartRequest(Hero, run ? GoalStarters.Run : GoalStarters.Walk, new GoalFrame(goal, tile: destination),
            (run ? GoalRunFlags.Running : GoalRunFlags.None) | (held ? GoalRunFlags.Flight : GoalRunFlags.None));
        SessionGoalStartResult started;
        _requesting = true;
        try
        {
            started = _session.StartGoal(request, _ports);
            foreach (var end in started.Ended) EndNodes(end);
        }
        finally { _requesting = false; }
        return started.Started ? HeroWalkRunResult.Started : HeroWalkRunResult.Refused;
    }

    /// <summary>
    /// ARC-DEV-169 (MIG13 S11a, GS26 and GS27): the object click's talk goal: the hero's click builds goal 21 itself, calling no starter and passing no start gate, but the viewer's movement gate (the hero can act, and in a round holds the
    /// turn) stands before it. The goal starts through the generic starter 0x44D4E0 (a level-3 interrupt of every run of the hero, its own priority 3; a talk run that is on already ends and the new one starts, GS27's
    /// "a repeated order restarts it"). <paramref name="run"/> sets the run flag (running is turned on by the always-run rule, GS27). False when the gate or a run's refusal (a spell attempt) stopped it.
    /// </summary>
    public bool MayTalk => _viewer.MayStart();

    public bool Talk(string target, bool run)
    {
        if (!MayTalk) return false;
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.Talk, target), run ? GoalRunFlags.Running : GoalRunFlags.None);
        var started = _session.StartGoal(request, _ports);
        // A walk, an approach or a turn this start interrupted is over: its end node stops the route (the talk's own approach sets its own).
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_TALK");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-174 (MIG13 S11b, GS26 and GS27): the object click's use goal 38 (<paramref name="container"/>: 81), started as the talk is (<see cref="Talk"/>): the movement gate, the generic starter 0x44D4E0 (a level-3 interrupt of the
    /// hero's runs, so a repeated click restarts the run), the run flag on. The goal's frame carries the target; node 1 writes the range. False when the gate or a run's refusal stopped it.
    /// </summary>
    public bool Use(string target, bool container, bool run)
    {
        if (!MayTalk) return false;
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(container ? GoalType.UseContainer : GoalType.UseObject, target), run ? GoalRunFlags.Running : GoalRunFlags.None);
        var started = _session.StartGoal(request, _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_USE");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-175 (MIG13 S11c, GS26 and GS27): the object click's pickup goal 15, started as the talk and the use are (<see cref="Talk"/>): the movement gate, the generic starter 0x44D4E0 (a level-3 interrupt of the hero's
    /// runs, so a repeated click restarts the run), the run flag on. False when the gate or a run's refusal stopped it.
    /// </summary>
    public bool Pickup(string target, bool run)
    {
        if (!MayTalk) return false;
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.PickupItem, target), run ? GoalRunFlags.Running : GoalRunFlags.None);
        var started = _session.StartGoal(request, _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_PICKUP");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-181 (MIG13 S12a, GS26 and GS27): the object click's attack goal 18, started as the talk, the use and the pickup are (<see cref="Talk"/>): the movement gate, the generic starter 0x44D4E0 (a level-3 interrupt of the
    /// hero's runs), the run flag on. The goal's frame carries the target; the hit aim is the viewer's. False when the gate or a run's refusal stopped it.
    /// </summary>
    public bool Attack(string target, bool run)
    {
        if (!MayTalk) return false;
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.Attack, target), run ? GoalRunFlags.Running : GoalRunFlags.None);
        var started = _session.StartGoal(request, _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_ATTACK");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-182 (MIG13 S12b, TT2): the throw goal 37 of a held item, started by the game-view press as the clicks' goals are (<see cref="Talk"/>): the movement gate, the generic starter (a level-3 interrupt of the hero's runs), no
    /// run flag. The frame carries the item and the pressed tile. False when the gate or a run's refusal stopped it.
    /// </summary>
    public bool Throw(string item, CellCoord tile)
    {
        if (!MayTalk) return false;
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.ThrowItem, tile: tile) { Item = item });
        var started = _session.StartGoal(request, _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_THROW");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-183 (MIG13 S12c): the casting goal 26 for the magic run the cast host built. The starter 0x455C30 calls no gate; the generic starter interrupts at the goal's own priority (3), so a cast replaces the walk. False when a run's refusal stopped it.
    /// </summary>
    public bool Cast(int run, string? target)
    {
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.CastSpell, target) { SpellRun = run });
        var started = _session.StartGoal(request, _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_CAST");
        return started.Started;
    }

    /// <summary>
    /// ARC-DEV-184 (MIG13 S12d, GT141): the skill goal 33 for a pick pocket, as the starter 0x4350F0 builds it (the victim, the item, skill 5, the plant flag): the movement gate, the generic starter (a level-3 interrupt of the
    /// hero's runs). False when the gate or a run's refusal stopped it.
    /// </summary>
    public bool PickPocket(string victim, string item, bool plant)
    {
        if (!MayTalk) return false;
        var frame = new GoalFrame(GoalType.UseSkill, victim) { Item = item, Skill = AiExecutorRules.PickpocketSkill, Flags = plant ? GoalFrameFlags.Plant : GoalFrameFlags.None };
        var started = _session.StartGoal(new GoalStartRequest(Hero, GoalStarters.Generic, frame), _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_SKILL");
        return started.Started;
    }

    /// <summary>ARC-DEV-185 (MIG13 S12e): the skill goal 33 for a lock pick, as the starter 0x4350F0 builds it for skill 14 (the portal, the lockpicks, the skill): the movement gate, the generic starter. False when the gate or a run's refusal stopped it.</summary>
    public bool PickLock(string portal, string item)
    {
        if (!MayTalk) return false;
        var frame = new GoalFrame(GoalType.UseSkill, portal) { Item = item, Skill = AiExecutorRules.PickLocksSkill };
        var started = _session.StartGoal(new GoalStartRequest(Hero, GoalStarters.Generic, frame), _ports);
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_REPLACED_BY_SKILL");
        return started.Started;
    }

    /// <summary>ARC-DEV-184: the hero's skill run ends (a cancel): the run is killed and the walk it pushed stops. False when there was none.</summary>
    public bool EndSkill(string why)
    {
        GoalRun? found;
        try { found = SkillRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-183: the hero's casting run ends (a cancel, a map change): the run is killed and its end node runs. False when there was none.</summary>
    public bool EndCast(string why)
    {
        GoalRun? found;
        try { found = CastRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-182: the hero's throw run ends (a cancel before the throw landed): the run is killed. False when there was none.</summary>
    public bool EndThrow(string why)
    {
        GoalRun? found;
        try { found = ThrowRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-181: the hero's attack run ends (a cancel: a new click, the stick, a window, the hero down): the run is killed and its walk's route stops. False when there was none.</summary>
    public bool EndAttack(string why)
    {
        GoalRun? found;
        try { found = AttackRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session: nothing of it is left to end
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-175: the hero's pickup run ends (a cancel of the old walk-to-pick-up intent): the run is killed and its walk's route stops. False when there was none.</summary>
    public bool EndPickup(string why)
    {
        GoalRun? found;
        try { found = PickupRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session: nothing of it is left to end
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-174: the hero's use run ends (a cancel of the old walk-to-use intent): the run is killed and its walk's route stops. False when there was none.</summary>
    public bool EndUse(string why)
    {
        GoalRun? found;
        try { found = UseRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session: nothing of it is left to end
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>ARC-DEV-169: the hero's talk run ends (a cancel of the old approach intent: a quick load, the hero down, a stick, a reset): the run is killed and its walk's route stops. False when there was none.</summary>
    public bool EndTalk(string why)
    {
        GoalRun? found;
        try { found = TalkRun; }
        catch (ObjectDisposedException) { return false; } // a replaced session: nothing of it is left to end
        if (found is not { } run) return false;
        var change = _session.KillGoal(run.Handle);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return true;
    }

    /// <summary>
    /// ARC-DEV-147: the stick is released and the held walk ends where the hero is (Steamwoven's decision: the original's button release changes nothing and the hero walks on to the last tile, GT112, which a stick cannot ask).
    /// The hero's runs are interrupted at level 3 as a new ordinary action would (<see cref="Interrupt"/>): the walk run's end node stops the route, whose step under way still finishes at its cell centre. False when
    /// the hero has no walk run, or when a run of priority 4 or 5 (a spell attempt) refused the interrupt.
    /// </summary>
    public bool EndHeldWalk(string why) => WalkRun is not null && Interrupt(GoalInterruptRules.OrdinaryActions, why);

    /// <summary>
    /// The rotate starter (0x433440, GT100) once its gates passed (the viewer's request host decides them): goal 53 starts through 0x44D4E0, a level-2 interrupt of every run of the hero (its own priority,
    /// GS12), its target rotation in the frame; false when a run refuses it (a spell attempt) or the table cannot take it. The goal's first step comes 5 ms later (GS7).
    /// </summary>
    public bool Turn(int rotation)
    {
        var request = new GoalStartRequest(Hero, GoalStarters.Generic, new GoalFrame(GoalType.Rotate) { Rotation = rotation & 7 });
        var started = _session.StartGoal(request, _ports);
        // A walk this start interrupted is over (the turn asks nothing of the route's goal): its end node stops the route.
        foreach (var end in started.Ended) EndNodes(end, "ROUTE_STOPPED_BY_TURN");
        return started.Started;
    }

    /// <summary>
    /// The level-3 interrupt of every run of the hero (0x424070, GS17): the right button's release, a new ordinary action. The first refusal (a spell attempt of priority 4 or 5) stops it and
    /// answers false; an ended walk's end node stops the route. True when nothing refused.
    /// </summary>
    public bool Interrupt(int level, string why)
    {
        var change = _session.InterruptGoals(Hero, level);
        foreach (var step in change.Steps)
            if (step.Ended is { } end) EndNodes(end, why);
        return change.Failure == SessionGoalFailure.None && change.Succeeded;
    }

    /// <summary>
    /// The end nodes of an ended run of the hero (a start's interrupt, a right release, a round's sweep, a map's close, the session's own end): a walk run's end node stops the route that still follows its destination
    /// (the route is the walk's feet, so the hero stops at the cell it is walking to). A walk this host's own start ended asks nothing of the route (the caller sets the next goal).
    /// </summary>
    public void EndNodes(GoalRunEnd end, string? why = null)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (end.Kind == GoalEndKind.Finished) return;
        // ARC-DEV-187: a stun cut short (a knock-down, a death, a reset) still clears the stunned bit (the stun goal's end node).
        if (IsHeroStun(end.Run)) { _viewer.HeroStunEnds(); return; }
        // The rotate goal's end node snaps the facing to the target, even when the run was interrupted (GT102): the walk that interrupted it draws its own facing after.
        if (IsHeroTurn(end.Run)) { _viewer.SnapFacing(end.Run.Bottom.Rotation); return; }
        // ARC-DEV-182: a throw interrupted before it landed leaves the item in the pack (the pose is cleared); one that ended on its own already said so.
        if (IsHeroThrow(end.Run)) { if (end.Run.Bottom.Item is { } thrown) _viewer.HeroThrowEnds(thrown, AiExecutorReason.ActionRefused); return; }
        // ARC-DEV-183: a cast cut before it ended frees its magic run through the end node (the viewer ignores a run whose goal already ended).
        if (IsHeroCast(end.Run)) { if (end.Run.Bottom.SpellRun is { } cut) _viewer.HeroCastInterrupted(cut, why ?? "interrupted"); return; }
        if (!_requesting && IsHeroWalk(end.Run) && end.Run.Bottom.Tile is { } destination) _viewer.StopWalking(destination, why ?? "ROUTE_STOPPED_BY_GOAL_END");
        // A talk run that walked (goal 8 pushed the walk 3 to a cell next to the target): its walk's route stops with it.
        else if (!_requesting && (IsHeroTalk(end.Run) || IsHeroUse(end.Run) || IsHeroPickup(end.Run) || IsHeroAttack(end.Run) || IsHeroSkill(end.Run)) && end.Run.Frames.FirstOrDefault(f => f.Goal is GoalType.MoveToTile or GoalType.RunToTile)?.Tile is { } walked)
            _viewer.StopWalking(walked, why ?? "ROUTE_STOPPED_BY_GOAL_END");
    }

    /// <summary>The runs the host drops with the view (a quick load, a replaced session, the map shown elsewhere): the hero's runs end at level 6 and the route stops with them.</summary>
    public void LoseAll(string why)
    {
        try
        {
            var change = _session.InterruptGoals(Hero, GoalInterruptRules.MaxLevel);
            foreach (var step in change.Steps)
                if (step.Ended is { } end) EndNodes(end, why);
        }
        catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to end */ }
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (IsHeroTurn(run))
        {
            Steps++;
            return StepTurn(run);
        }
        // ARC-DEV-187: the viewer reads no stun animation (23), so the stun's animation cannot start and the goal ends at its first step, as an NPC's does (goal-table.md §4): its end clears the stunned bit.
        if (IsHeroStun(run)) { Steps++; _viewer.HeroStunEnds(); return SessionGoalStepAnswer.End; }
        if (!IsHeroWalk(run) && !IsHeroTalk(run) && !IsHeroUse(run) && !IsHeroPickup(run) && !IsHeroAttack(run) && !IsHeroThrow(run) && !IsHeroCast(run) && !IsHeroSkill(run)) return SessionGoalStepAnswer.End;
        Steps++;
        // ARC-DEV-149 (MIG13 S10b-2b): the walk is stepped by the table's nodes over the hero ports (goals 3 and 4 plan, goal 5 takes the steps on the hero's body); a far goal is handed to the route follower, and the run
        // lives while the route follows it.
        var stepped = GoalStepper.StepHeroWalk(run, _walkPorts);
        if (stepped.Answer.NextDelayMs is null && stepped.End is not GoalStepperEnd.Finished) RouteEnds++;
        if (IsHeroAttack(run) && stepped.Answer.NextDelayMs is null) LastAttackEnd = $"{stepped.End} {stepped.Step?.Reason}";
        return stepped.Answer;
    }

    /// <summary>
    /// ARC-DEV-148 (MIG13 S10b-2, GT102): one step of goal 53, node 0x42C440. A hero that no longer stands where the turn began (it walked, fell, or the map changed) ends the run, as does an art already facing the target (the step
    /// answers false: the run ends one tick after the step that reached it); in a round the hero is not the current participant for, nothing changes and the step comes again in 30 ms; otherwise the art turns one
    /// 45-degree step the short way (<see cref="HeroInputRules.RotateStep"/>) and the step comes again in <see cref="HeroInputRules.RotateStepMs"/>. The fast turn (0x4B8040, GT102) is not modelled. An end by interrupt runs the end node
    /// (<see cref="EndNodes"/>), which snaps the facing to the target.
    /// </summary>
    private SessionGoalStepAnswer StepTurn(GoalRun run)
    {
        var target = run.Bottom.Rotation;
        if (!_viewer.TurnHolds) { RouteEnds++; return SessionGoalStepAnswer.End; }
        var facing = _viewer.HeroFacing;
        if (facing == target) return SessionGoalStepAnswer.End;
        if (_session.QueryCombatRound() is { } round && !round.Actor.IsHero) return SessionGoalStepAnswer.After(HeroInputRules.RotateStepMs);
        _viewer.ShowFacing(HeroInputRules.RotateStep(facing, target));
        TurnSteps++;
        return SessionGoalStepAnswer.After(HeroInputRules.RotateStepMs);
    }

    /// <summary>The 45-degree steps goal 53's runs took (a test and the diagnostic line read it).</summary>
    public int TurnSteps { get; private set; }

    /// <summary>The steps the walks gave the hero's body (a test and the diagnostic line read it).</summary>
    public int WalkSteps { get; private set; }

    // ARC-DEV-149: the hero's walk ports (IAiHeroWalkWorld) over the viewer's body, planner and route follower.
    private sealed class WalkPorts(AreaHeroRunsHost host) : IAiHeroWalkWorld
    {
        public CellCoord? Tile() => host._viewer.HeroTile;
        public int Encumbrance => host._viewer.HeroEncumbrance;
        public bool Down => host._viewer.HeroDown;
        public bool InRound => host._viewer.InRound;
        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight) => host._viewer.PlanWalk(destination, running, straight);
        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => host._viewer.StepBlocked(from, next, lastOfSegment);

        public int BeginStep(CellCoord from, CellCoord next, bool running)
        {
            var ms = host._viewer.BeginStep(from, next, running);
            if (ms > 0) host.WalkSteps++;
            return ms;
        }

        public bool StepPending(CellCoord from, CellCoord to) => host._viewer.StepPending(from, to);
        public bool FollowRoute(CellCoord destination) => host._viewer.FollowRoute(destination);
        public bool RouteFollowing(CellCoord destination) => host._viewer.Walking(destination);
        public AiHeroTarget? ObjectTarget(string target) => host._viewer.ObjectTarget(target);
        public CellCoord? ApproachTile(string target, int range) => host._viewer.ApproachTile(target, range);
        public void OpenDialog(string target) => host._viewer.OpenDialog(target);
        public void UseObject(string target) => host._viewer.UseObject(target);
        public void PickUpObject(string target) => host._viewer.PickUpObject(target);
        public IAiDoorWorld? Doors => host._viewer.Doors;
        public AiHeroAttackTarget? AttackTarget(string target) => host._viewer.AttackTarget(target);
        public bool AttackPossible(string target) => host._viewer.AttackPossible(target);
        public bool InAttackReach(string target) => host._viewer.InAttackReach(target);
        public CellCoord? AttackApproachTile(string target) => host._viewer.AttackApproachTile(target);
        public bool AttackWalkInReach(string target) => host._viewer.AttackWalkInReach(target);
        public AiAttackSwing? HeroBeginAttack(string target) => host._viewer.HeroBeginAttack(target);
        public void HeroAttackHit(string target) => host._viewer.HeroAttackHit(target);
        public bool HeroAttackRepeats(string target) => host._viewer.HeroAttackRepeats(target);
        public void AttackEnds(string target, AiExecutorReason reason) => host._viewer.AttackEnds(target, reason);
        public AiAttackSwing? HeroBeginThrow(string item, CellCoord tile) => host._viewer.HeroBeginThrow(item, tile);
        public bool HeroThrowItem(string item, CellCoord tile) => host._viewer.HeroThrowItem(item, tile);
        public void HeroThrowEnds(string item, AiExecutorReason reason) => host._viewer.HeroThrowEnds(item, reason);
        public AiCastGoalStart HeroStartCast(int run) => host._viewer.HeroStartCast(run);
        public bool HeroFaceCast(int run) => host._viewer.HeroFaceCast(run);
        public AiCastSwing HeroBeginCast(int run) => host._viewer.HeroBeginCast(run);
        public bool HeroCastActionFrame(int run) => host._viewer.HeroCastActionFrame(run);
        public bool HeroReleaseCast(int run) => host._viewer.HeroReleaseCast(run);
        public void HeroCastEnds(int run, HeroCastEnd how) => host._viewer.HeroCastEnds(run, how);
        public bool HeroFaceSkill(string target) => host._viewer.HeroFaceSkill(target);
        public void HeroPickPocket(string target, string item, bool plant) => host._viewer.HeroPickPocket(target, item, plant);
        public void HeroPickLock(string target, string item) => host._viewer.HeroPickLock(target, item);
        bool IAiHeroWalkWorld.HeroSettled => host._viewer.HeroSettled;
        public bool NeedsTurn(int rotation) => host._viewer.HeroSettled && host._viewer.HeroFacing != (rotation & 7);

        // Goal 53 pushed by a walk (node 0x42C440): the standing hero's art turns one 45-degree step the short way; a hero that moved on needs no turn (its step shows its facing).
        public int TurnStep(int rotation)
        {
            if (!host._viewer.HeroSettled) return 0;
            var facing = host._viewer.HeroFacing;
            if (facing == (rotation & 7)) return 0;
            if (host._session.QueryCombatRound() is { } round && !round.Actor.IsHero) return HeroInputRules.RotateStepMs;
            host._viewer.ShowFacing(HeroInputRules.RotateStep(facing, rotation & 7));
            host.TurnSteps++;
            return HeroInputRules.RotateStepMs;
        }
    }

    // The walk starters' gate (0x4339A0) is the viewer's: asked before the start. The table's own gate has nothing more to ask of the hero.
    private sealed class StartPorts(AreaHeroRunsHost host) : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => host._viewer.MayStart();
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
