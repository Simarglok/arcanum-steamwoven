using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// The NPC AI executor (stage A6, research ARC-RES-066; slice I7, ARC-WORLD-086, re-read in the retail 1.0.7.4 executable): perform's
/// dispatch of the pass's choice, the starters with their start gate, and the goal machine that carries an attack, a flight, a
/// back-off run and following out in real time and in a turn-based round, as drivers over an <see cref="IAiPerformWorld"/> and an
/// <see cref="IAiGoalWorld"/>. A run is an immutable <see cref="GoalRun"/> of the session's goal table (ARC-DEV-134, MIG13 S4): a starter proposes one
/// (its handle unallocated, <c>default</c>) and the host starts it in the table; the world runtime's stepper steps it after each <see cref="AiGoalStepResult.DelayMs"/>; the planners, the step walker, the attack animation and resolution, and the action-point
/// owner are the world's. The action goals (casting, item use, skills, throws and pickups) and their starters are slice I8b
/// (ARC-WORLD-089, <c>ExecutorActionGoals.cs</c>).
/// </summary>
public static partial class AiExecutorRules
{
    /// <summary>A run holds at most this many goals; a push beyond ends it.</summary>
    public const int MaxDepth = GoalRun.MaxFrames;

    /// <summary>ARC-DEV-134 (MIG13 S4): the 26 goals whose nodes this executor interprets. The world runtime's stepper hands it a run whose current goal is one
    /// of them and ends any other run as an unknown goal (goal-table.md §3.4); the slices S6–S14 add the nodes of the others.</summary>
    public static ImmutableHashSet<GoalType> InterpretedGoals { get; } =
    [
        GoalType.RunToTile, GoalType.AttemptMove, GoalType.MoveToPause, GoalType.MoveNear, GoalType.PickupItem, GoalType.AttemptPickup, GoalType.Attack,
        GoalType.AttemptAttack, GoalType.Follow, GoalType.Flee, GoalType.CastSpell, GoalType.AttemptSpell, GoalType.UseSkill, GoalType.AttemptSkill,
        GoalType.ThrowItem, GoalType.UseItem, GoalType.MoveAway, GoalType.RunNear, GoalType.MeleeApproach, GoalType.AttemptPickLocks, GoalType.SpreadOut,
        GoalType.CombatApproach, GoalType.RangedApproach, GoalType.CastSpellWithAnimation, GoalType.AttemptSpellWithAnimation, GoalType.AttemptPickpocket,
    ];

    /// <summary>Whether the executor interprets <paramref name="goal"/>'s nodes (<see cref="InterpretedGoals"/>).</summary>
    public static bool Interprets(GoalType goal) => InterpretedGoals.Contains(goal);

    /// <summary>One step of a run runs at most this many nodes; the next ends the owner's subturn and kills the run.</summary>
    public const int MaxNodes = 100;

    /// <summary>A planned segment is walked for at most this many steps before the goal plans again (the path builder's cap).</summary>
    public const int SegmentSteps = OriginalPathRequest.ResultCap;

    /// <summary>The real-time pause before a goal without a path ends its run, in game milliseconds.</summary>
    public const int NoPathPauseMs = 1000;

    /// <summary>A walk goal whose next step is blocked waits 0..this many game milliseconds before it gives up.</summary>
    public const int BlockedStepWaitMs = 300;

    /// <summary>A follower standing within its range re-checks after this many game milliseconds.</summary>
    public const int FollowRecheckMs = 100;

    /// <summary>The cost a cast, a skill and a non-magical item use pay; a blocked step, a flight's end and a follower's in-range
    /// re-check pay <see cref="SmallCharge"/>.</summary>
    public const int SmallCharge = 2;

    /// <summary>A step begins running only below this encumbrance level ("significant"); the flight and the run near walk at it.</summary>
    public const int RunRefusedEncumbrance = 4;

    /// <summary>The item-use, skill and attack starters turn running on for an NPC whose current fatigue is above this.</summary>
    public const int RunFatigue = 8;

    /// <summary>The start gate lets an NPC start an action it cannot pay while its fatigue is above this percent of its maximum.</summary>
    public const int GateFatiguePercent = 35;

    /// <summary>The flight's range: it walks on while its source is within this many tiles.</summary>
    public const int FleeRange = 9;

    /// <summary>A follower stops within this many tiles of its leader (<see cref="SpreadOutFollowRange"/> spread out).</summary>
    public const int FollowRange = 2;

    public const int SpreadOutFollowRange = 5;

    /// <summary>The follower walks while within its range plus this, and runs beyond.</summary>
    public const int FollowWalkMargin = 2;

    /// <summary>The follow starter starts no goal while the leader is within this many tiles (<see cref="SpreadOutStartRange"/>
    /// spread out).</summary>
    public const int FollowStartRange = 4;

    public const int SpreadOutStartRange = 7;

    /// <summary>The spread-out goal moves away from a leader closer than this (<see cref="SpreadOutAwayNoFlag"/> without the NPC flag).</summary>
    public const int SpreadOutAway = 3;

    public const int SpreadOutAwayNoFlag = 1;

    // ---- pure tests ----

    /// <summary>
    /// The start gate (0x4348E0) with the action's <paramref name="cost"/>: the object must be active; outside a round it passes;
    /// in a round only the critter whose turn it is passes, a PC (or any non-NPC) always, an NPC when the cost is at most its action
    /// points left or its current fatigue is above 35 % of maximum (<see cref="TurnBasedRules.NpcMayStartAction"/>).
    /// </summary>
    [OriginalFunction(0x4348E0, OriginalFunctionScope.Full)]
    public static bool StartGate(IAiPerformWorld world, string id, int cost)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(id);
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        if (!world.Active(id)) return false;
        if (!world.InRound) return true;
        if (world.RoundActor != id) return false;
        var mover = world.Mover(id);
        return mover.Pc || TurnBasedRules.NpcMayStartAction(world.ActionPoints(id), cost, mover.Fatigue, mover.MaxFatigue);
    }

    /// <summary>
    /// ARC-WORLD-099: the goal stepper's last call for the owner of a run it freed as done (0x423B95–0x423BA0: outside a round, or in
    /// one while the current participant has action points left) and 0x4B4320 with the tests of the fidget's start (0x435BD0). An NPC
    /// (not a PC) that is in combat mode outside a round (<paramref name="combatMode"/>) or auto-animates (critter flags 2, 0x2:
    /// <paramref name="autoAnimates"/>) starts its fidget animation (goal 2, priority 1) when the start gate with cost 0 lets it, it runs
    /// no animation (<paramref name="animating"/>: no run, no fidget) and its current art allows one (<paramref name="artAllows"/>, the
    /// world's art test 0x503E20). True: the fidget starts. In a round it does not in practice: the owner whose subturn it is either
    /// started a run in its subturn hook or ended its subturn there, and the gate refuses every other critter.
    /// </summary>
    [OriginalFunction(0x4B4320, OriginalFunctionScope.Partial)]
    public static bool FidgetAfterRun(IAiPerformWorld world, string id, bool combatMode, bool autoAnimates, bool animating, bool artAllows)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(id);
        if (world.InRound && (world.RoundActor is not { } actor || world.ActionPoints(actor) <= 0)) return false;
        if (!(!world.InRound && combatMode) && !autoAnimates) return false;
        return !world.Mover(id).Pc && StartGate(world, id, 0) && !animating && artAllows;
    }

    /// <summary>
    /// The attack-possible test (0x4294F0): the attacker active and not in Body of Air (unless elemental), the target neither
    /// destroyed, off nor dead, and when the line of fire reports a blocker other than the target, a path to the target exists.
    /// The line's cost is ignored here.
    /// </summary>
    [OriginalFunction(0x4294F0, OriginalFunctionScope.Partial)]
    public static bool AttackPossible(AiAttackFacts facts) =>
        !facts.AttackerInactive && !(facts.BodyOfAir && !facts.Elemental) && !facts.TargetGone && !facts.TargetDead
        && !(facts.ForeignBlocker && facts.NoPathToTarget);

    /// <summary>The reach test (0x429960, <see cref="LineOfFireRules.InReach{TObject}"/>): the distance at most the weapon's range (at
    /// least 1) and a clear line.</summary>
    public static bool InReach(AiReachFacts reach) => reach.Distance >= 0 && reach.Distance <= Math.Max(reach.WeaponRange, 1) && reach.ClearLine;

    /// <summary>
    /// Whether the ranged approach stops at the current tile (its callback 0x425590, after the target-moved test). Original (EX9,
    /// <see cref="CombatAnomalyProfile.RangedApproachSkipsClearShot"/>): only when the attack has become impossible while the line
    /// is clear and a weapon is wielded, so it walks on to the tile next to a target that stands still. Steamwoven: at the first
    /// tile from which <see cref="InReach"/> passes.
    /// </summary>
    [OriginalFunction(0x425590, OriginalFunctionScope.Partial)]
    public static bool RangedApproachStops(AiAttackFacts facts, AiReachFacts reach, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.RangedApproachSkipsClearShot ? !AttackPossible(facts) && reach.ClearLine && reach.Weapon : InReach(reach);
    }

    /// <summary>
    /// The escape line's destination (0x426A80, its first part): the direction from the source's tile to the NPC's
    /// (<see cref="LineOfFireRules.Rotation"/>, one of eight; 5 on the same tile) and the tile <c>range + 1</c> steps from the
    /// source in that direction, on the line through the NPC beyond it. The map's bounds are the planner's.
    /// </summary>
    public static CellCoord EscapeDestination(CellCoord source, CellCoord npc, int range)
    {
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        var rotation = LineOfFireRules.Rotation(source, npc);
        var tile = source;
        for (var i = 0; i <= range; i++) tile = OriginalPathRequest.Neighbor(tile, rotation);
        return tile;
    }

    /// <summary>The Chebyshev distance between two tiles.</summary>
    public static int Distance(CellCoord a, CellCoord b) => checked((int)Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y)));

    // ---- perform and the starters ----

    /// <summary>
    /// Perform (0x4AC180), the pass's last step, for <paramref name="npc"/> with the pass record's <paramref name="target"/>,
    /// <paramref name="choice"/> (the action, spell, skill and item the support rules wrote; null: action 0) and alerted
    /// <paramref name="sound"/>, the object's current run <paramref name="current"/> (null: none). In a round nothing unless it is
    /// the NPC's turn; BACKING_OFF → the back-off run; a cast → the magic domain's cast and its casting goal (<see cref="StartCast"/>),
    /// an item → the item-use starter (<see cref="StartUseItem"/>), a skill → the skill starter (<see cref="StartUseSkill"/>), each with
    /// the choice's target (the pass record's, which the chooser wrote; <paramref name="target"/> when the choice has none); otherwise by the danger state: NONE → non-combat perform (0x4AC380), COMBAT_FOCUS → combat perform (0x4AC6E0:
    /// back-off start, else the grenade step and its throw, else the attack starter), FLEE → perform fleeing and the flee goal,
    /// SURRENDER → perform surrender.
    /// </summary>
    [OriginalFunction(0x4AC180, OriginalFunctionScope.Partial)]
    public static AiPerformResult Perform(IAiPerformWorld world, string npc, string? target, AiPassChoice? choice, int? sound, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (world.InRound && world.RoundActor != npc) return new(AiPerformKind.NotActor, current);
        if ((world.DangerBits(npc) & DangerFlags.BackingOff) != 0) return BackOff(world, npc, target, current);
        // The chooser writes the pass record's target with its choice (SP6): the action's target is the choice's.
        var actionTarget = choice?.Target ?? target;
        switch (choice?.Action ?? AiPassAction.None)
        {
            case AiPassAction.Cast: return StartCast(world, npc, choice!, actionTarget, current);
            case AiPassAction.UseItem: return StartUseItem(world, npc, actionTarget, choice!.Item, current);
            case AiPassAction.UseSkill: return StartUseSkill(world, npc, actionTarget, choice!.Skill, choice.Item, current);
        }
        switch (world.ReadDanger(npc))
        {
            case DangerState.None:
                world.LeaveCombatMode(npc);
                if (world.Leader(npc) is not { } leader) { world.RunRoutine(npc); return new(AiPerformKind.Routine, current); }
                if (world.Waiting(npc)) return new(AiPerformKind.Waiting, current);
                var follow = StartFollow(world, npc, leader, current);
                world.LoyaltyCheck(npc);
                return follow with { Kind = AiPerformKind.Follow };
            case DangerState.CombatFocus:
                if (world.StartBackOff(npc, target)) return new(AiPerformKind.BackOffStart, current);
                // The grenade step (0x4AC910) answers "thrown" once it called the throw starter, whatever the starter did.
                if (target is not null && world.Grenade(npc, target) is { Throws: true, Grenade: { } grenade })
                    return StartThrow(world, npc, grenade, world.Tile(target), current) with { Kind = AiPerformKind.Grenade };
                return StartAttack(world, npc, target, sound, current);
            case DangerState.Flee:
                var fleeing = world.PerformFleeing(npc);
                if (!fleeing.StartsFleeGoal) return new(AiPerformKind.Flee, current);
                return StartFlee(world, npc, fleeing.Source, current) with { Kind = AiPerformKind.Flee };
            default:
                world.PerformSurrender(npc);
                return new(AiPerformKind.Surrender, current);
        }
    }

    // The back-off (0x4AC7B0): reached or no "away" tile clears the bit (MR15); otherwise the run to the tile (0x433C80, which asks
    // no start gate), running unless encumbered.
    private static AiPerformResult BackOff(IAiPerformWorld world, string npc, string? target, GoalRun? current)
    {
        var step = world.BackOff(npc, target);
        if (step.Step == BackOffRunStep.Runs && world.AwayTile(npc, target, step.Range) is { } tile)
        {
            var run = new GoalRun(npc, default, new GoalFrame(GoalType.RunToTile, tile: tile), Gait(world, npc, GoalRunFlags.Running));
            return new(AiPerformKind.BackOffRun, run) { Started = true };
        }
        if (step.Step != BackOffRunStep.NotBackingOff) world.EndBackOff(npc);
        return new(AiPerformKind.BackOffEnded, current);
    }

    /// <summary>
    /// The attack starter (0x434B00) of <paramref name="attacker"/> on <paramref name="target"/>: an attack on oneself is refused; a
    /// "default throws" weapon that can be unwielded is taken off (0x466E00) and the throw starter runs with it at the target's tile
    /// (<see cref="StartThrow"/>, whose answer is the starter's) instead; then the start gate with the weapon's
    /// attack cost, Body of Air without the elemental flag, an attack on the same target already queued, an interrupt of the
    /// lower-priority animations that fails and a boomerang in flight refuse (asking the critters sharing the tile to step apart);
    /// a run whose attack is already swinging counts as queued too. An attack run approaching another target restarts as an attack
    /// on the new target (inference: the original updates the running goal). The goal stores the
    /// target and the alerted sound; running is on for an NPC with current fatigue above 8.
    /// </summary>
    [OriginalFunction(0x434B00, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartAttack(IAiPerformWorld world, string attacker, string? target, int? sound, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(attacker);
        if (target is null) return new(AiPerformKind.Attack, current, AiStartRefusal.NoObject);
        if (target == attacker) return new(AiPerformKind.Attack, current, AiStartRefusal.Self);
        if (world.ThrowableWeapon(attacker) is { } weapon)
        {
            world.Unwield(attacker, weapon);
            return StartThrow(world, attacker, weapon, world.Tile(target), current) with { Kind = AiPerformKind.ThrowWeapon };
        }
        if (!StartGate(world, attacker, world.AttackCost(attacker))) return new(AiPerformKind.Attack, current, AiStartRefusal.Gate);
        var facts = world.AttackFacts(attacker, target);
        var refusal = facts.BodyOfAir && !facts.Elemental ? AiStartRefusal.BodyOfAir : AiStartRefusal.None;
        if (refusal == AiStartRefusal.None && current?.Bottom.Goal is GoalType.Attack or GoalType.AttemptAttack)
        {
            // An attack on the same target, or a swing already going, is kept; an approach toward another target restarts as an
            // attack on the new one (inference: the original updates the running attack goal).
            if (current.Bottom.Target == target || current.Bottom.Goal == GoalType.AttemptAttack) refusal = AiStartRefusal.AlreadyQueued;
            else return new(AiPerformKind.Attack, current.WithFrames([new GoalFrame(GoalType.Attack, target, sound: sound)])) { Started = true };
        }
        if (refusal == AiStartRefusal.None && !world.InterruptLowerAnimations(attacker)) refusal = AiStartRefusal.Interrupt;
        if (refusal == AiStartRefusal.None && world.BoomerangInFlight(attacker)) refusal = AiStartRefusal.BoomerangInFlight;
        if (refusal != AiStartRefusal.None)
        {
            world.StepApart(attacker);
            return new(AiPerformKind.Attack, current, refusal);
        }
        var mover = world.Mover(attacker);
        var flags = !mover.Pc && mover.Fatigue > RunFatigue ? GoalRunFlags.Running : GoalRunFlags.None;
        return new(AiPerformKind.Attack, new GoalRun(attacker, default, new GoalFrame(GoalType.Attack, target, sound: sound), flags)) { Started = true };
    }

    /// <summary>The flee starter (0x434980) of <paramref name="npc"/> away from <paramref name="source"/>: the start gate with cost 0,
    /// the source stored, then run flag 0x400 (0x436CB0), which every later path request of the flight carries.</summary>
    [OriginalFunction(0x434980, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartFlee(IAiPerformWorld world, string npc, string? source, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (source is null) return new(AiPerformKind.Flee, current, AiStartRefusal.NoObject);
        if (!StartGate(world, npc, 0)) return new(AiPerformKind.Flee, current, AiStartRefusal.Gate);
        return new(AiPerformKind.Flee, new GoalRun(npc, default, new GoalFrame(GoalType.Flee, source), GoalRunFlags.Flight)) { Started = true };
    }

    /// <summary>
    /// The follow starter (0x4346A0) of <paramref name="npc"/> toward <paramref name="leader"/>: only for an NPC; no new goal while
    /// the leader is within 4 tiles (7 with "spread out"); an existing follow, walk-near or run-near run is kept; otherwise the
    /// start gate with cost 0 and the lower-priority animations' interrupt, then goal 24 without running.
    /// </summary>
    [OriginalFunction(0x4346A0, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartFollow(IAiPerformWorld world, string npc, string leader, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(leader);
        var mover = world.Mover(npc);
        if (mover.Pc) return new(AiPerformKind.Follow, current, AiStartRefusal.NotNpc);
        if (world.Tile(npc) is not { } at || world.Tile(leader) is not { } there) return new(AiPerformKind.Follow, current, AiStartRefusal.NoObject);
        if (Distance(at, there) <= (mover.SpreadOut ? SpreadOutStartRange : FollowStartRange)) return new(AiPerformKind.Follow, current, AiStartRefusal.LeaderNear);
        if (current?.Bottom.Goal is GoalType.Follow or GoalType.MoveNear or GoalType.RunNear) return new(AiPerformKind.Follow, current, AiStartRefusal.AlreadyQueued);
        if (!StartGate(world, npc, 0)) return new(AiPerformKind.Follow, current, AiStartRefusal.Gate);
        if (!world.InterruptLowerAnimations(npc)) return new(AiPerformKind.Follow, current, AiStartRefusal.Interrupt);
        return new(AiPerformKind.Follow, new GoalRun(npc, default, new GoalFrame(GoalType.Follow, leader))) { Started = true };
    }

    /// <summary>The spread-out goal's starter (0x436960) of <paramref name="npc"/> toward <paramref name="leader"/>: the start gate with
    /// cost 0 and goal 69, without the flight's flag. The order that sets the NPC flag "spread out" and calls it is slice I9's.</summary>
    [OriginalFunction(0x436960, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartSpreadOut(IAiPerformWorld world, string npc, string leader)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(leader);
        if (!StartGate(world, npc, 0)) return new(AiPerformKind.Follow, null, AiStartRefusal.Gate);
        return new(AiPerformKind.Follow, new GoalRun(npc, default, new GoalFrame(GoalType.SpreadOut, leader))) { Started = true };
    }

    private static GoalRunFlags Gait(IAiGoalWorld world, string id, GoalRunFlags flags) =>
        world.Mover(id).Encumbrance >= RunRefusedEncumbrance ? flags & ~GoalRunFlags.Running : flags;

    // ---- the goal machine ----

    private enum Move { Next, Push, Pop, PopTwo, Replace, Restart, Wait, End, Kill }

    // The walk goals' blocked-step exit (the original's node 10).
    private const int BlockedNode = 10;

    private readonly record struct Transition(Move Move, int Node = 0, GoalFrame? Frame = null, int Delay = 0,
        AiExecutorReason Reason = AiExecutorReason.None, GoalFrame? Top = null, GoalRunFlags? Flags = null);

    private static Transition Next(int node, int delay = 0) => new(Move.Next, node, Delay: delay);
    private static Transition End(AiExecutorReason reason) => new(Move.End, Reason: reason);

    /// <summary>
    /// One step of a run (the stepper 0x423560): nodes of the top goal run until one waits (a positive delay, a restart or the
    /// blocked step's random wait, <see cref="AiRollPurpose.BlockedStepWait"/>) or the run ends. A node names the next node, pushes a goal (with a copy of the parent's data; a push beyond 8 goals ends the run),
    /// pops back (the parent restarts at its first node in the same step; popping the bottom goal finishes the run), replaces
    /// itself, restarts its goal (the step ends), chooses the end step or kills the run. At the end step, in a round, an owner
    /// other than the local PC is charged the round's pending cost in the Original setting (EX6,
    /// <see cref="CombatAnomalyProfile.EndStepRechargesPendingCost"/>); Steamwoven charges nothing there. The 101st node of a step
    /// ends the owner's subturn and kills the run. <paramref name="carried"/> is the reason the run's previous step reported (a reason holds until a node reports
    /// another; ARC-DEV-134: the table's run keeps no overlay reason, so the caller carries it).
    /// </summary>
    [OriginalFunction(0x423560, OriginalFunctionScope.Partial)]
    public static AiGoalStepResult Step(GoalRun run, IAiGoalWorld world, IAiRollSource rolls, CombatAnomalyProfile profile, AiExecutorReason carried = AiExecutorReason.None)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        var frames = run.Frames.ToBuilder();
        var flags = run.Flags;
        var reason = carried;
        for (var nodes = 1; ; nodes++)
        {
            if (nodes > MaxNodes)
            {
                if (world.InRound && world.RoundActor == run.Owner) world.EndSubturn(run.Owner);
                return new(null, 0, AiRunEnd.NodeLimit, nodes - 1, reason);
            }
            var top = frames[^1];
            var t = Node(run.Owner, top, frames, flags, world, rolls, profile);
            if (t.Reason != AiExecutorReason.None) reason = t.Reason;
            if (t.Flags is { } f) flags = f;
            if (t.Top is { } changed) frames[^1] = top = changed;
            switch (t.Move)
            {
                case Move.Next:
                    frames[^1] = top with { Node = t.Node };
                    break;
                case Move.Push:
                    if (frames.Count >= MaxDepth) return new(null, 0, AiRunEnd.Overflow, nodes, reason);
                    frames[^1] = top with { Node = 1 };
                    frames.Add(t.Frame!);
                    break;
                case Move.Pop:
                case Move.PopTwo:
                    var pops = t.Move == Move.Pop ? 1 : 2;
                    for (var i = 0; i < pops && frames.Count > 0; i++)
                    {
                        // The pop runs the popped goal's end node (0x423D10): the cast's attempt goals end their magic run (0x429CD0).
                        EndNode(run.Owner, frames[^1], world);
                        frames.RemoveAt(frames.Count - 1);
                    }
                    if (frames.Count == 0) return new(null, 0, AiRunEnd.Finished, nodes, reason);
                    frames[^1] = frames[^1] with { Node = 1 };
                    break;
                case Move.Replace:
                    frames[^1] = t.Frame!;
                    break;
                case Move.Restart:
                case Move.Wait:
                    frames[^1] = top with { Node = t.Move == Move.Restart ? 1 : t.Node };
                    return new(run.WithFrames(frames.ToImmutable()) with { Flags = flags }, Math.Max(t.Delay, 0), AiRunEnd.Running, nodes, reason);
                case Move.End:
                    if (profile.EndStepRechargesPendingCost && world.PendingCost > 0 && !world.Mover(run.Owner).Pc) Consume(world, run.Owner, world.PendingCost);
                    // Then the end node of every goal of the run (0x4239F9), the top first.
                    for (var i = frames.Count - 1; i >= 0; i--) EndNode(run.Owner, frames[i], world);
                    return new(null, 0, AiRunEnd.EndStep, nodes, reason);
                default:
                    return new(null, 0, AiRunEnd.Killed, nodes, reason);
            }
            if (t.Delay > 0) return new(run.WithFrames(frames.ToImmutable()) with { Flags = flags }, t.Delay, AiRunEnd.Running, nodes, reason);
        }
    }

    private static Transition Node(string owner, GoalFrame top, ImmutableArray<GoalFrame>.Builder frames, GoalRunFlags flags,
        IAiGoalWorld world, IAiRollSource rolls, CombatAnomalyProfile profile) => top.Goal switch
    {
        GoalType.Attack => AttackNode(owner, top, world),
        GoalType.AttemptAttack => AttemptAttackNode(owner, top, world),
        GoalType.CombatApproach => CombatApproachNode(owner, top, flags, world),
        GoalType.MeleeApproach or GoalType.RangedApproach or GoalType.AttemptMove => WalkNode(owner, top, frames, flags, world, rolls, profile),
        GoalType.Flee => FleeNode(owner, top, flags, world),
        GoalType.MoveAway => MoveAwayNode(owner, top, frames, flags, world, profile),
        GoalType.RunToTile => RunToTileNode(owner, top, flags, world),
        GoalType.Follow or GoalType.SpreadOut => FollowNode(owner, top, world),
        GoalType.MoveNear or GoalType.RunNear => MoveNearNode(owner, top, flags, world),
        GoalType.CastSpell or GoalType.CastSpellWithAnimation => CastNode(owner, top, world),
        GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation => AttemptSpellNode(owner, top, world),
        GoalType.UseSkill => UseSkillNode(owner, top, flags, world),
        GoalType.AttemptSkill => AttemptSkillNode(owner, top, world),
        GoalType.AttemptPickpocket => AttemptPickpocketNode(owner, top, world),
        GoalType.AttemptPickLocks => AttemptPickLocksNode(owner, top, world),
        GoalType.ThrowItem => ThrowItemNode(owner, top, world),
        GoalType.UseItem => UseItemNode(owner, top, world),
        GoalType.PickupItem => PickupNode(owner, top, world),
        GoalType.AttemptPickup => AttemptPickupNode(owner, top, world),
        _ => PauseNode(top),
    };

    // ---- the attack (goals 18, 19, 79) ----

    private static Transition AttackNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                // 0x429450 fails → pop (AG2: no end step, so no EX6 charge; popping the bottom goal finishes the run).
                return target is not null && world.Tile(target) is not null && !world.DeadCritter(target) ? Next(2) : new(Move.Pop, Reason: AiExecutorReason.AttackTargetGone);
            case 2:
                if (AttackPossible(world.AttackFacts(owner, target!))) return Next(3);
                if (world.InRound && world.RoundActor == owner) world.EndSubturn(owner);
                return End(AiExecutorReason.AttackImpossible);
            case 3:
            case 4:
                return Readiness(owner, goal, world) is { } ready ? ready : Next(goal.Node + 1);
            case 5:
                return InReach(world.Reach(owner, target!))
                    ? new(Move.Replace, Frame: new GoalFrame(GoalType.AttemptAttack, target, range: goal.Range, sound: goal.Sound), Reason: AiExecutorReason.AttackInReach)
                    : Next(6);
            case 6:
                return new(Move.Next, 7, Top: goal with { Range = 1 });
            case 7:
                return Within(world, owner, target, goal.Range) && world.FirstStepClear(owner, target!, false)
                    ? Next(8)
                    : new(Move.Push, Frame: Child(GoalType.CombatApproach, goal), Reason: AiExecutorReason.AttackApproach);
            default:
                return NoWay(owner, world, AiExecutorReason.AttackBlocked, endsSubturn: false);
        }
    }

    // Knocked down → get up (goal 63); concealed (not a PC) → unconceal (goal 54). Each is a subgoal after which the goal restarts.
    private static Transition? Readiness(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var mover = world.Mover(owner);
        if (goal.Node == 3 && mover.KnockedDown) return new(Move.Restart, Delay: world.GetUp(owner));
        if (goal.Node == 4 && mover.Concealed && !mover.Pc) return new(Move.Restart, Delay: world.Unconceal(owner));
        return null;
    }

    private static Transition AttemptAttackNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target!;
        switch (goal.Node)
        {
            case 1:
                if (world.Tile(target) is null || !InReach(world.Reach(owner, target))) return End(AiExecutorReason.AttackBlocked);
                var cost = world.AttackCost(owner);
                if (!Check(world, owner, cost)) return new(Move.Kill, Reason: AiExecutorReason.Unpaid);
                Consume(world, owner, cost);
                var swing = world.BeginAttack(owner, target);
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                // The action frame: in reach → the hit step; out of reach → no hit, the action points are spent.
                var hit = world.Tile(target) is not null && InReach(world.Reach(owner, target));
                if (hit) world.Hit(owner, target);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: hit ? AiExecutorReason.AttackHit : AiExecutorReason.AttackOutOfReach);
            default:
                // The repeat test (0x429760) lets only a PC in combat mode with auto-attack go on; an NPC's goal ends after one attack.
                return new(Move.Pop);
        }
    }

    private static Transition CombatApproachNode(string owner, GoalFrame goal, GoalRunFlags flags, IAiGoalWorld world)
    {
        var target = goal.Target!;
        switch (goal.Node)
        {
            case 1:
                return Within(world, owner, target, goal.Range) && world.FirstStepClear(owner, target, false) ? new(Move.Pop) : Next(3);
            case 3:
            case 4:
                return Readiness(owner, goal, world) is { } ready ? ready : Next(goal.Node + 1);
            default:
                var path = Plan(world, owner, new(AiPathKind.Attack, target, null, goal.Range, false, Running(world, owner, flags)));
                if (path is null) return NoPath(owner, world, AiExecutorReason.AttackNoPath);
                if (goal.Sound is { } sound) world.PlaySound(owner, sound);
                var walk = GoalType.MeleeApproach;
                if (world.Reach(owner, target).WeaponRange > 1) walk = GoalType.RangedApproach;
                return new(Move.Push, Frame: Child(walk, goal) with { Sound = null, Path = path.Steps, PlannedTargetTile = world.Tile(target) },
                    Top: goal with { Sound = null });
        }
    }

    // ---- the walk goals (5, 61, 80) ----

    private static Transition WalkNode(string owner, GoalFrame goal, ImmutableArray<GoalFrame>.Builder frames, GoalRunFlags flags,
        IAiGoalWorld world, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        var target = goal.Target;
        if (goal.Node == BlockedNode)
        {
            // Node 10: 2 action points; the plain walk and the ranged approach end the run, the melee approach re-plans (EX20).
            ChargeSmall(world, owner);
            return goal.Goal == GoalType.MeleeApproach ? new(Move.Pop, Reason: AiExecutorReason.StepBlocked) : End(AiExecutorReason.StepBlocked);
        }
        if (goal.Goal == GoalType.MeleeApproach && target is not null && world.Tile(target) != goal.PlannedTargetTile) return new(Move.Pop);
        if (goal.Goal == GoalType.RangedApproach && target is not null)
        {
            if (world.Tile(target) != goal.PlannedTargetTile) return new(Move.PopTwo);
            var reach = world.Reach(owner, target);
            if (RangedApproachStops(world.AttackFacts(owner, target), reach, profile))
            {
                // The callback writes the weapon's range into the goals' range and stops the approach.
                for (var i = 0; i < frames.Count; i++) frames[i] = frames[i] with { Range = Math.Max(reach.WeaponRange, 1) };
                return new(Move.PopTwo, Top: frames[^1]);
            }
        }
        if (goal.PathIndex >= goal.Path.Length) return new(Move.Pop);
        var next = goal.Path[goal.PathIndex];
        var flight = (flags & GoalRunFlags.Flight) != 0;
        switch (world.StepBlocked(owner, next, flight))
        {
            case AiStepBlock.Occupied:
                // The step's wait, which ends the step even when it draws 0 (the run goes on in the next frame).
                var wait = rolls.Roll(AiRollPurpose.BlockedStepWait, 0, BlockedStepWaitMs);
                return new(Move.Wait, BlockedNode, Delay: wait, Reason: AiExecutorReason.StepBlocked);
            case AiStepBlock.CannotStart:
                // No wait drawn; the step still ends here (inference: the next frame goes on at node 10), so a melee walk that keeps
                // re-planning onto a step that cannot start does not spin within one step.
                return new(Move.Wait, BlockedNode, Reason: AiExecutorReason.StepBlocked);
            case AiStepBlock.Clear:
                break;
            default:
                throw new InvalidOperationException("The world answered an undefined step block.");
        }
        var mover = world.Mover(owner);
        var running = Running(world, owner, flags);
        var cost = TurnBasedRules.StepCost(running, mover.Fatigue);
        if (!Check(world, owner, cost)) return new(Move.Kill, Reason: AiExecutorReason.Unpaid);
        Consume(world, owner, cost);
        var ms = world.BeginStep(owner, next, running);
        return new(Move.Next, 1, Delay: Math.Max(ms, 1), Top: goal with { PathIndex = goal.PathIndex + 1 });
    }

    // ---- the flight (goals 25, 52) ----

    private static Transition FleeNode(string owner, GoalFrame goal, GoalRunFlags flags, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                var running = world.Mover(owner).Encumbrance < RunRefusedEncumbrance;
                return new(Move.Next, 2, Flags: running ? flags | GoalRunFlags.Running : flags & ~GoalRunFlags.Running);
            case 2:
                return new(Move.Next, 3, Top: goal with { Range = FleeRange });
            default:
                if (Within(world, owner, goal.Target, goal.Range) && world.FirstStepClear(owner, goal.Target!, true))
                    return new(Move.Push, Frame: Child(GoalType.MoveAway, goal), Reason: AiExecutorReason.FleeEscapeLine);
                ChargeSmall(world, owner);
                return End(AiExecutorReason.FleeSafe);
        }
    }

    private static Transition MoveAwayNode(string owner, GoalFrame goal, ImmutableArray<GoalFrame>.Builder frames, GoalRunFlags flags,
        IAiGoalWorld world, CombatAnomalyProfile profile)
    {
        var source = goal.Target;
        // The spread-out order's move-away has no flight flag; Steamwoven sets no NO_FLEE from it (EX12).
        var spreadOut = frames.Count > 1 && frames[^2].Goal == GoalType.SpreadOut;
        var marks = !spreadOut || profile.SpreadOutSetsNoFlee;
        switch (goal.Node)
        {
            case 1:
            case 2:
                return Readiness(owner, goal with { Node = goal.Node + 2 }, world) is { } ready ? ready : Next(goal.Node + 1);
            case 3:
                return Within(world, owner, source, goal.Range) && world.FirstStepClear(owner, source!, true) ? Next(5) : new(Move.Pop);
            case 5:
                var flight = (flags & GoalRunFlags.Flight) != 0;
                if (source is not null && !world.Gone(source) && !world.Mover(owner).Entangled
                    && world.Tile(source) is { } from && world.Tile(owner) is { } at)
                {
                    var path = Plan(world, owner, new(AiPathKind.Escape, source, EscapeDestination(from, at, goal.Range), goal.Range, flight, Running(world, owner, flags)));
                    if (path is not null)
                    {
                        if (path.StraightStoppedShort && marks) world.SetNoFlee(owner);
                        return new(Move.Push, Frame: Child(GoalType.AttemptMove, goal) with { Path = path.Steps },
                            Reason: spreadOut ? AiExecutorReason.SpreadOutAway : AiExecutorReason.FleeEscapeLine);
                    }
                }
                if (world.InRound && world.RoundActor == owner && !world.Mover(owner).Pc) world.EndSubturn(owner);
                if (world.Mover(owner).CannotTurn) return End(AiExecutorReason.FleeCornered);
                if (source is not null) world.Face(owner, source);
                if (marks) world.SetNoFlee(owner);
                return End(AiExecutorReason.FleeCornered);
            default:
                return Next(5);
        }
    }

    // ---- the run to a tile (goal 4) ----

    private static Transition RunToTileNode(string owner, GoalFrame goal, GoalRunFlags flags, IAiGoalWorld world)
    {
        if (goal.Tile is not { } tile || world.Tile(owner) is not { } at) return End(AiExecutorReason.ObjectGone);
        if (at == tile) return new(Move.Pop, Reason: AiExecutorReason.RunToTile);
        var path = Plan(world, owner, new(AiPathKind.ToTile, null, tile, 0, false, Running(world, owner, flags)));
        if (path is null) return NoPath(owner, world, AiExecutorReason.NoPath);
        return new(Move.Push, Frame: Child(GoalType.AttemptMove, goal) with { Path = path.Steps }, Reason: AiExecutorReason.RunToTile);
    }

    // ---- following (goals 24, 69, 8, 56) ----

    private static Transition FollowNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var leader = goal.Target;
        if (leader is null || world.Tile(leader) is not { } there || world.Tile(owner) is not { } at || world.Gone(leader))
            return End(AiExecutorReason.ObjectGone);
        var spread = world.Mover(owner).SpreadOut;
        var range = spread ? SpreadOutFollowRange : FollowRange;
        var distance = Distance(at, there);
        if (goal.Goal == GoalType.SpreadOut && distance < (spread ? SpreadOutAway : SpreadOutAwayNoFlag))
            return new(Move.Push, Frame: Child(GoalType.MoveAway, goal) with { Range = range }, Top: goal with { Range = range },
                Reason: AiExecutorReason.SpreadOutAway);
        if (distance <= range && world.FirstStepClear(owner, leader, false))
        {
            ChargeSmall(world, owner);
            return new(Move.Restart, Delay: FollowRecheckMs, Top: goal with { Range = range }, Reason: AiExecutorReason.FollowInRange);
        }
        var walk = distance <= range + FollowWalkMargin;
        var near = new GoalFrame(walk ? GoalType.MoveNear : GoalType.RunNear, leader, range: range);
        var reason = walk ? AiExecutorReason.FollowWalk : AiExecutorReason.FollowRun;
        // The follow goal becomes the walk or run near; the spread-out goal keeps itself under it (inference).
        return goal.Goal == GoalType.Follow ? new(Move.Replace, Frame: near, Reason: reason) : new(Move.Push, Frame: near, Reason: reason);
    }

    private static Transition MoveNearNode(string owner, GoalFrame goal, GoalRunFlags flags, IAiGoalWorld world)
    {
        var leader = goal.Target;
        if (leader is null || world.Tile(leader) is not { } there || world.Tile(owner) is not { } at) return End(AiExecutorReason.ObjectGone);
        if (Distance(at, there) <= goal.Range && world.FirstStepClear(owner, leader, false)) return new(Move.Pop, Reason: AiExecutorReason.FollowInRange);
        // The run near runs unless encumbered; the walk near keeps the run's gait (an item-use run an NPC above 8 fatigue started
        // runs, AG16; a follower's walks; a spread-out run walks near running after a run near).
        var runNear = goal.Goal == GoalType.RunNear;
        var running = runNear ? world.Mover(owner).Encumbrance < RunRefusedEncumbrance : Running(world, owner, flags);
        var gait = runNear ? running ? flags | GoalRunFlags.Running : flags & ~GoalRunFlags.Running : flags;
        var path = Plan(world, owner, new(AiPathKind.Near, leader, null, goal.Range, false, running) { NearOccupied = (flags & GoalRunFlags.NearOccupied) != 0 });
        if (path is null) return NoPath(owner, world, AiExecutorReason.NoPath);
        return new(Move.Push, Frame: Child(GoalType.MeleeApproach, goal) with { Path = path.Steps, PlannedTargetTile = there }, Flags: gait,
            Reason: running ? AiExecutorReason.FollowRun : AiExecutorReason.FollowWalk);
    }

    // ---- the pause (goal 6) and the common ends ----

    private static Transition PauseNode(GoalFrame goal) => goal.Node == 1 ? Next(2, NoPathPauseMs) : new(Move.End);

    // A planner found no path (the common node 0x4246D0): in real time the pause goal, whose only node ends the run after a
    // second; in a round the run ends at once and the planner's failure ends a non-PC's subturn (0x4B7C90).
    private static Transition NoPath(string owner, IAiGoalWorld world, AiExecutorReason reason) => NoWay(owner, world, reason, endsSubturn: true);

    private static Transition NoWay(string owner, IAiGoalWorld world, AiExecutorReason reason, bool endsSubturn)
    {
        if (!world.InRound) return new(Move.Push, Frame: new GoalFrame(GoalType.MoveToPause), Reason: reason);
        if (endsSubturn && world.RoundActor == owner && !world.Mover(owner).Pc) world.EndSubturn(owner);
        return End(reason);
    }

    private static AiPath? Plan(IAiGoalWorld world, string owner, AiPathRequest request)
    {
        var path = world.PlanPath(owner, request);
        if (path is null || path.Steps.IsDefaultOrEmpty) return null;
        return path.Steps.Length > SegmentSteps ? path with { Steps = path.Steps[..SegmentSteps] } : path;
    }

    private static GoalFrame Child(GoalType goal, GoalFrame parent) =>
        new(goal, parent.Target, parent.Tile, parent.Range, parent.Sound) { Item = parent.Item, Skill = parent.Skill, SpellRun = parent.SpellRun };

    private static bool Within(IAiGoalWorld world, string owner, string? target, int range) =>
        target is not null && world.Tile(target) is { } there && world.Tile(owner) is { } at && Distance(at, there) <= range;

    private static bool Running(IAiGoalWorld world, string owner, GoalRunFlags flags) =>
        (flags & GoalRunFlags.Running) != 0 && world.Mover(owner).Encumbrance < RunRefusedEncumbrance;

    // ---- action points ----

    // The action-point check (0x4B7790): outside a round and for another critter it passes; for the actor it writes the pending
    // cost and passes when the action points cover it, or for the local PC with action points left and fatigue above 1.
    private static bool Check(IAiGoalWorld world, string owner, int cost)
    {
        if (!world.InRound || world.RoundActor != owner) return true;
        world.PendingCost = cost;
        var mover = world.Mover(owner);
        return TurnBasedRules.CanAfford(world.ActionPoints(owner), cost, mover.Pc, mover.Fatigue);
    }

    // The consume (0x4B7CD0) acts only for the critter whose turn it is in a round.
    private static void Consume(IAiGoalWorld world, string owner, int cost)
    {
        if (world.InRound && world.RoundActor == owner) world.Consume(owner, cost);
    }

    // 0x433270: 2 action points for any object but the local PC.
    private static void ChargeSmall(IAiGoalWorld world, string owner)
    {
        if (!world.Mover(owner).Pc) Consume(world, owner, SmallCharge);
    }
}
