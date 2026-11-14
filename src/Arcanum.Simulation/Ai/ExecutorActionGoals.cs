using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// The action goals of the NPC executor (slice I8b, ARC-WORLD-089, read in the retail 1.0.7.4 executable through its goal-type table
/// 0x5B03D0): the starters of an item use (0x435450), a skill (0x4350F0, pick locks 0x436220), a throw (0x434F80) and a pickup
/// (0x4356C0), perform's cast (0x4AC250), the item use itself (0x462CC0), and the goals that carry them out: casting (26, 82 and
/// their attempts 27, 83), the item use (39), the skill (33 and its attempts 34, 86, 67), the throw (37) and the pickup (15, 16).
/// Animations, the magic run, the skill invocation, the throw and the pickup are the world's.
/// </summary>
public static partial class AiExecutorRules
{
    /// <summary>The skill numbers the skill goal branches on (0x42A490, 0x42A440).</summary>
    public const int PickpocketSkill = 5, PickLocksSkill = 14;

    /// <summary>The dialog skill service's extra start flag 0x2000 (0x419CB0 passes it to 0x4350F0 when 0x45DDA0 of the NPC is 0, no PC leads it); see <see cref="StartUseSkill"/> for what the goals do with it (nothing).</summary>
    public const int NoPcLeaderRequestFlag = 0x2000;

    // ---- starters ----

    /// <summary>
    /// Perform's cast (0x4AC250) of <paramref name="npc"/> with <paramref name="choice"/> at the pass <paramref name="target"/>: the
    /// magic domain builds and checks the invocation and runs it (<see cref="IAiPerformWorld.Cast"/>, which also spends an item
    /// source's food or scroll); a run it started gets its casting goal, 26 or 82 per the record's casting animation, without a
    /// start gate (0x455C30 calls none) and replacing the object's run. No run (a refused check, or a cast the magic domain queued
    /// under a running cast, SU13) keeps the current run.
    /// </summary>
    [OriginalFunction(0x4AC250, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartCast(IAiPerformWorld world, string npc, AiPassChoice choice, string? target, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(choice);
        var cast = world.Cast(npc, choice);
        if (cast.Run is not { } run) return new(AiPerformKind.Cast, current, AiStartRefusal.NoRun);
        var goal = cast.CastAnimation ? GoalType.CastSpellWithAnimation : GoalType.CastSpell;
        return new(AiPerformKind.Cast, new GoalRun(npc, default, new GoalFrame(goal, target) { SpellRun = run })) { Started = true };
    }

    /// <summary>
    /// The item-use starter (0x435450) of <paramref name="npc"/> using <paramref name="item"/> on <paramref name="target"/>: both are
    /// needed; the start gate with <see cref="TurnBasedRules.UseItemCost"/>; goal 39 with the target and the item, run flag 0x4000
    /// and running for an NPC with current fatigue above 8.
    /// </summary>
    [OriginalFunction(0x435450, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartUseItem(IAiPerformWorld world, string npc, string? target, string? item, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (target is null || item is null) return new(AiPerformKind.UseItem, current, AiStartRefusal.NoObject);
        if (!StartGate(world, npc, TurnBasedRules.UseItemCost)) return new(AiPerformKind.UseItem, current, AiStartRefusal.Gate);
        var frame = new GoalFrame(GoalType.UseItem, target) { Item = item };
        return new(AiPerformKind.UseItem, new GoalRun(npc, default, frame, ActionFlags(world, npc, running: true))) { Started = true };
    }

    /// <summary>
    /// The skill starter (0x4350F0) of <paramref name="npc"/> using <paramref name="skill"/> on <paramref name="target"/> with the
    /// tool <paramref name="item"/> (null: none): pick locks goes to its own starter (<see cref="StartPickLocks"/>); otherwise the start
    /// gate with <see cref="TurnBasedRules.UseSkillCost"/>; goal 33 with the target, the item and the skill, run flag 0x4000 and running
    /// for an NPC with current fatigue above 8. A missing target refuses (inference: the original asserts on it).
    /// <paramref name="requestFlags"/> is the starter's last argument (0x4350F0 ORs it into the goal record's word +0x70; <see cref="NoPcLeaderRequestFlag"/> is the dialog service's 0x2000,
    /// ARC-SCRIPT-1789, ledger HLR10/SK8). Of the goal's nodes only the plant bit 0x1 of the pick-pocket goals is read (0x429160, 0x42A2A0, 0x42A4E0 map it to the attempt's 0x2); the node that maps
    /// 0x2000 to a forced success (0x428E10) belongs to the use-item-with-skill goal 40, so the flag changes no run built here: it is accepted, never stored and never read, as in the original.
    /// </summary>
    [OriginalFunction(0x4350F0, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartUseSkill(IAiPerformWorld world, string npc, string? target, int skill, string? item, GoalRun? current, int requestFlags = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (skill == PickLocksSkill) return StartPickLocks(world, npc, target, item, current);
        if (target is null) return new(AiPerformKind.UseSkill, current, AiStartRefusal.NoObject);
        if (!StartGate(world, npc, TurnBasedRules.UseSkillCost)) return new(AiPerformKind.UseSkill, current, AiStartRefusal.Gate);
        var frame = new GoalFrame(GoalType.UseSkill, target) { Item = item, Skill = skill };
        return new(AiPerformKind.UseSkill, new GoalRun(npc, default, frame, ActionFlags(world, npc, running: true))) { Started = true };
    }

    /// <summary>
    /// The pick-locks starter (0x436220): the start gate with <see cref="TurnBasedRules.UseSkillCost"/>; goal 33 with skill 14; no art for
    /// the skill animation (12) refuses; the animation's loop count is the world's (<see cref="AiActionAnimation.PickLocks"/>); run flag
    /// 0x4000 and no running.
    /// </summary>
    [OriginalFunction(0x436220, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartPickLocks(IAiPerformWorld world, string npc, string? target, string? item, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (target is null) return new(AiPerformKind.UseSkill, current, AiStartRefusal.NoObject);
        if (!StartGate(world, npc, TurnBasedRules.UseSkillCost)) return new(AiPerformKind.UseSkill, current, AiStartRefusal.Gate);
        if (!world.HasSkillArt(npc)) return new(AiPerformKind.UseSkill, current, AiStartRefusal.NoArt);
        var frame = new GoalFrame(GoalType.UseSkill, target) { Item = item, Skill = PickLocksSkill };
        return new(AiPerformKind.UseSkill, new GoalRun(npc, default, frame, GoalRunFlags.NearOccupied)) { Started = true };
    }

    /// <summary>
    /// The throw starter (0x434F80) of <paramref name="npc"/> throwing <paramref name="item"/> at <paramref name="tile"/>: the start
    /// gate with cost 0; then an item and a tile are needed; an equal throw already in the run (same item and tile) refuses, so does a
    /// failed interrupt of the lower-priority animations (priority 3); goal 37 without running.
    /// </summary>
    [OriginalFunction(0x434F80, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartThrow(IAiPerformWorld world, string npc, string? item, CellCoord? tile, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (!StartGate(world, npc, 0)) return new(AiPerformKind.Throw, current, AiStartRefusal.Gate);
        if (item is null || tile is not { } at) return new(AiPerformKind.Throw, current, AiStartRefusal.NoObject);
        if (QueuedRuns(world, npc, current).Any(r => r.Frames.Any(f => f.Goal == GoalType.ThrowItem && f.Item == item && f.Tile == at)))
            return new(AiPerformKind.Throw, current, AiStartRefusal.AlreadyQueued);
        if (!world.InterruptLowerAnimations(npc)) return new(AiPerformKind.Throw, current, AiStartRefusal.Interrupt);
        return new(AiPerformKind.Throw, new GoalRun(npc, default, new GoalFrame(GoalType.ThrowItem, tile: at) { Item = item })) { Started = true };
    }

    /// <summary>
    /// The pickup starter (0x4356C0) of <paramref name="npc"/> picking up <paramref name="item"/> (the item search's pickup,
    /// <see cref="IAiItemSearchWorld.StartPickup"/>): the start gate with <see cref="TurnBasedRules.UseItemCost"/>; an NPC is refused
    /// while the item system's pickup switch is off (0x4681A0); goal 15 with the item, run flag 0x4000, no running.
    /// </summary>
    [OriginalFunction(0x4356C0, OriginalFunctionScope.Partial)]
    public static AiPerformResult StartPickup(IAiPerformWorld world, string npc, string? item, GoalRun? current)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        if (item is null) return new(AiPerformKind.Pickup, current, AiStartRefusal.NoObject);
        if (!StartGate(world, npc, TurnBasedRules.UseItemCost)) return new(AiPerformKind.Pickup, current, AiStartRefusal.Gate);
        if (!world.Mover(npc).Pc && !world.NpcPickupsOn) return new(AiPerformKind.Pickup, current, AiStartRefusal.PickupsOff);
        return new(AiPerformKind.Pickup, new GoalRun(npc, default, new GoalFrame(GoalType.PickupItem, item) { Item = item }, GoalRunFlags.NearOccupied))
            { Started = true };
    }

    // The item-use and skill starters' run flags: 0x4000 and, for an NPC with current fatigue above 8, running (0x436AB0).
    private static GoalRunFlags ActionFlags(IAiGoalWorld world, string npc, bool running)
    {
        var mover = world.Mover(npc);
        return GoalRunFlags.NearOccupied | (running && !mover.Pc && mover.Fatigue > RunFatigue ? GoalRunFlags.Running : GoalRunFlags.None);
    }

    /// <summary>
    /// The end nodes of every goal of <paramref name="run"/>, top first (as the end step runs them), for a run the host drops without
    /// stepping it to its end: a run perform or a starter replaces (<see cref="AiPerformResult.Started"/> with another run), a run
    /// killed, stopped by the node limit or by an overflow, or one the host forgets. The cast's attempt goals end their magic run
    /// (0x429CD0), so its slot does not leak. In the original an object may hold several runs at once and a new run does not end the
    /// old one; the executor's one run per object needs this call (inference).
    /// </summary>
    public static void EndRun(GoalRun run, IAiGoalWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        for (var i = run.Frames.Length - 1; i >= 0; i--) EndNode(run.Owner, run.Frames[i], world);
    }

    // ---- the item use (0x462CC0) ----

    /// <summary>
    /// The item use (0x462CC0) of <paramref name="item"/> by <paramref name="user"/> on <paramref name="target"/> (null: none), as the
    /// item-use goal's last node and the chooser's immediate use of an AI-action item (<see cref="AiChooserOutcome.ItemUsedAtOnce"/>)
    /// run it, single player (ledger TP1, re-read here): a scroll whose spell needs more intelligence than the user has → nothing; no
    /// target and OIF_NEEDS_TARGET → the targeting cursor; a trap device → its use; the use script, which may stop it → 4 action
    /// points; then an item without spell charges and without the magical flag pays 4 action points and is read (written), used as a
    /// lockpick or healing kit with its skill (generic) or spent (food, scroll); an item with either casts its slot-0 spell without a
    /// charge and a food or scroll is spent. The action points are the consume's (0x4B7CD0): only in a round, on the user's subturn.
    /// ARC-UI-025: the player's inventory window runs the same use through its own <see cref="IItemUseWorld"/>.
    /// </summary>
    [OriginalFunction(0x462CC0, OriginalFunctionScope.Partial)]
    public static AiItemUseResult UseItemNow(IItemUseWorld world, string user, string item, string? target)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(item);
        var facts = world.UsedItem(user, item);
        if (facts.Kind == AiItemKind.Scroll && facts.ScrollTooHard) return new(AiItemUseStep.ScrollTooHard, false, false);
        if (target is null && facts.NeedsTarget) { world.OpenTargeting(user, item); return new(AiItemUseStep.Targeting, false, false); }
        if (facts.TrapDevice) { world.UseTrapDevice(user, item, target); return new(AiItemUseStep.TrapDevice, false, false); }
        if (!world.RunUseScript(user, item, target)) return new(AiItemUseStep.ScriptStopped, Charge(world, user), false);
        if (facts.Charges != 0 || facts.Magical)
        {
            world.UseItemMagic(user, item, target);
            return new(AiItemUseStep.Magic, false, Spend(world, facts.Kind, item));
        }
        var charged = Charge(world, user);
        switch (facts.Kind)
        {
            case AiItemKind.Written:
                world.ReadWritten(user, item);
                return new(AiItemUseStep.Read, charged, false);
            case AiItemKind.Generic when facts.Lockpick:
                if (target is null || !world.Lockable(target)) return new(AiItemUseStep.NotLockable, charged, false);
                world.StartSkillWithItem(user, item, target, PickLocksSkill);
                return new(AiItemUseStep.Lockpick, charged, false);
            case AiItemKind.Generic when facts.HealingKit && target is not null:
                world.StartSkillWithItem(user, item, target, AiPassChoice.HealSkill);
                return new(AiItemUseStep.HealingKit, charged, false);
            case AiItemKind.Food or AiItemKind.Scroll:
                return new(AiItemUseStep.Spent, charged, Spend(world, facts.Kind, item));
            default:
                return new(AiItemUseStep.Nothing, charged, false);
        }
    }

    private static bool Spend(IItemUseWorld world, AiItemKind kind, string item)
    {
        if (kind is not (AiItemKind.Food or AiItemKind.Scroll)) return false;
        world.SpendItem(item);
        return true;
    }

    // The item use's 4 action points (0x4B7CD0): true when they were charged on the user's subturn.
    private static bool Charge(IItemUseWorld world, string user)
    {
        if (!world.InRound || world.RoundActor != user) return false;
        world.Consume(user, TurnBasedRules.UseItemCost);
        return true;
    }

    // ---- the casting goals (26, 82, 27, 83) ----

    // Goal 26/82: get up, unconceal; the start (0x429B50 with 0x429BC0 behind the always-true 0x429BB0); the turn (0x42C850); then the
    // attempt goal replaces it. Every refusal ends the magic run's goal (0x429CD0) and the run (node 10). The approach node (range 8,
    // move-near) is not reachable (EX13).
    private static Transition CastNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        if (goal.SpellRun is not { } run) return End(AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
            case 2:
                return Readiness(owner, goal with { Node = goal.Node + 2 }, world) is { } ready ? ready : Next(goal.Node + 1);
            default:
                if (world.StartCast(owner, run) != AiCastGoalStart.Started || !world.FaceCast(owner, run))
                {
                    world.EndCast(owner, run, refused: true);
                    return End(AiExecutorReason.CastRefused);
                }
                var attempt = goal.Goal == GoalType.CastSpell ? GoalType.AttemptSpell : GoalType.AttemptSpellWithAnimation;
                return new(Move.Replace, Frame: Child(attempt, goal), Reason: AiExecutorReason.CastPlays);
        }
    }

    // Goal 27/83: the play's start (0x429F00, 0x42A010); without a play the release at once and the end step (node 12/13); else the
    // action frame (0x42CAA0 → 0x429C40: the missile or the release 0x429C80), and the pop when the play ends (0x431A40). The goal's end
    // node is the magic run's end (0x429CD0), run by the stepper on the pop and the end step.
    private static Transition AttemptSpellNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        if (goal.SpellRun is not { } run) return End(AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
                var swing = world.BeginCast(owner, run, goal.Goal == GoalType.AttemptSpellWithAnimation);
                if (!swing.Plays)
                    return End(world.ReleaseCast(owner, run) ? AiExecutorReason.CastReleased : AiExecutorReason.CastNotReleased);
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                if (!world.CastActionFrame(owner, run)) return new(Move.Pop, Reason: AiExecutorReason.CastNotReleased);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.CastReleased);
            default:
                return new(Move.Pop);
        }
    }

    // The end node of a goal (the stepper's 0x423D10 and 0x4239F9): the cast's attempt goals end their magic run (0x429CD0, table slot
    // 0x1E0); every other end node is the animation's clean-up, the host's.
    private static void EndNode(string owner, GoalFrame frame, IAiGoalWorld world)
    {
        if (frame.Goal is GoalType.AttemptSpell or GoalType.AttemptSpellWithAnimation && frame.SpellRun is { } run)
            world.EndCast(owner, run, refused: false);
        else CritterEndNode(owner, frame, world);
    }

    // ---- the skill goals (33, 34, 86, 67) ----

    // Goal 33: range 1; within it with a clear first step → the attempt goal for the skill (67 pick locks, 86 pickpocket, 34 others)
    // replaces it; otherwise the follower's path (0x427990) and the plain walk (goal 5), so a blocked step ends the run (EX20), or no
    // path. The "path still valid" node 0x425740 is not modelled: every return plans again (inference, as goal 8).
    private static Transition UseSkillNode(string owner, GoalFrame goal, GoalRunFlags flags, IAiGoalWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                return new(Move.Next, 2, Top: goal with { Range = 1 });
            case 2:
                if (target is null || world.Tile(target) is null) return End(AiExecutorReason.ObjectGone);
                return Within(world, owner, target, goal.Range) && world.FirstStepClear(owner, target, false) ? Next(5) : Next(4);
            case 4:
                var path = Plan(world, owner, new(AiPathKind.Near, target, null, goal.Range, false, Running(world, owner, flags))
                    { NearOccupied = (flags & GoalRunFlags.NearOccupied) != 0 });
                if (path is null) return NoPath(owner, world, AiExecutorReason.NoPath);
                return new(Move.Push, Frame: Child(GoalType.AttemptMove, goal) with { Path = path.Steps }, Reason: AiExecutorReason.ActionApproach);
            default:
                var attempt = goal.Skill switch
                {
                    PickLocksSkill => GoalType.AttemptPickLocks,
                    PickpocketSkill => GoalType.AttemptPickpocket,
                    _ => GoalType.AttemptSkill,
                };
                return new(Move.Replace, Frame: Child(attempt, goal));
        }
    }

    // Goal 34: the turn (0x42C0F0) and the animation's art (0x42BD40) or the run ends; an animation that cannot start (0x42CB10) charges
    // 2 and ends it; at the action frame the attempt (0x42A2A0: a target destroyed or off, or 4 action points the consume refuses, end
    // the run; else the skill invocation); the goal pops when the animation ends.
    private static Transition AttemptSkillNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target!;
        switch (goal.Node)
        {
            case 1:
                if (!world.FaceForAction(owner, target, null)) return End(AiExecutorReason.ActionRefused);
                return BeginAction(owner, goal, AiActionAnimation.Skill, world);
            case 2:
                if (!SkillAttempt(owner, goal, world, out _)) return End(AiExecutorReason.ActionRefused);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.SkillAttempted);
            default:
                return new(Move.Pop);
        }
    }

    // Goal 86: the turn, then the attempt before any animation; a result with the run's flag 0x40000 pops at once (0x42A430), any other
    // plays the skill animation (no art: the end step; cannot start: 2 and the end step) and pops when it ends.
    private static Transition AttemptPickpocketNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target!;
        if (goal.Node != 1) return new(Move.Pop);
        if (!world.FaceForAction(owner, target, null)) return End(AiExecutorReason.ActionRefused);
        if (!SkillAttempt(owner, goal, world, out var skipsAnimation)) return End(AiExecutorReason.ActionRefused);
        if (skipsAnimation) return new(Move.Pop, Reason: AiExecutorReason.SkillAttempted);
        var swing = world.BeginAction(owner, AiActionAnimation.Skill);
        return swing.Start switch
        {
            AiActionStart.Started => new(Move.Next, 2, Delay: Math.Max(swing.ActionFrameMs + swing.RestMs, 1), Reason: AiExecutorReason.SkillAttempted),
            AiActionStart.NoArt => End(AiExecutorReason.ActionRefused),
            _ => ChargedEnd(owner, world, AiExecutorReason.ActionRefused),
        };
    }

    // Goal 67: the turn and the art, else the end step; the pick-locks loops (0x42D300: none left, or not a critter → the goal pops);
    // after them the attempt (0x42A4E0, no action points), then the goal pops (inference: the loops' end leads to one attempt).
    private static Transition AttemptPickLocksNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target!;
        switch (goal.Node)
        {
            case 1:
                if (!world.FaceForAction(owner, target, null)) return End(AiExecutorReason.ActionRefused);
                var swing = world.BeginAction(owner, AiActionAnimation.PickLocks);
                return swing.Start switch
                {
                    AiActionStart.Started => Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } },
                    AiActionStart.NoArt => End(AiExecutorReason.ActionRefused),
                    _ => new(Move.Pop, Reason: AiExecutorReason.ActionRefused),
                };
            case 2:
                world.AttemptPickLocks(owner, target, goal.Item);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.SkillAttempted);
            default:
                return new(Move.Pop);
        }
    }

    // 0x42A2A0: a target destroyed or off refuses; the 4 action points through the consume, whose refusal refuses; then the invocation.
    private static bool SkillAttempt(string owner, GoalFrame goal, IAiGoalWorld world, out bool skipsAnimation)
    {
        skipsAnimation = false;
        var target = goal.Target!;
        if (world.Gone(target) || !Pay(world, owner, TurnBasedRules.UseSkillCost)) return false;
        skipsAnimation = world.AttemptSkill(owner, target, goal.Skill ?? -1, goal.Item);
        return true;
    }

    // The animation's art and start (0x42BD40, 0x42CB10): no art → the end step; cannot start → 2 action points and the end step
    // (node 9, 0x433270); started → its action frame.
    private static Transition BeginAction(string owner, GoalFrame goal, AiActionAnimation animation, IAiGoalWorld world)
    {
        var swing = world.BeginAction(owner, animation);
        return swing.Start switch
        {
            AiActionStart.Started => Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } },
            AiActionStart.NoArt => End(AiExecutorReason.ActionRefused),
            _ => ChargedEnd(owner, world, AiExecutorReason.ActionRefused),
        };
    }

    private static Transition ChargedEnd(string owner, IAiGoalWorld world, AiExecutorReason reason)
    {
        ChargeSmall(world, owner);
        return End(reason);
    }

    // The consume (0x4B7CD0) with its answer: outside a round or another critter's subturn it passes; for the actor it answers whether
    // the action points cover the cost (the local PC also with points left and fatigue above 1, TB9) and takes them.
    private static bool Pay(IAiGoalWorld world, string owner, int cost)
    {
        if (!world.InRound || world.RoundActor != owner) return true;
        var mover = world.Mover(owner);
        var paid = TurnBasedRules.CanAfford(world.ActionPoints(owner), cost, mover.Pc, mover.Fatigue);
        world.Consume(owner, cost);
        return paid;
    }

    // ---- the throw (37) ----

    // Goal 37: the art (0x42BD40, animation 14), the turn to the tile (0x42C390) and the start (0x42CB10); at the action frame the throw
    // (0x42AB90); every path ends with 2 action points (0x433270) and the end step, so a round's pending cost is charged again (EX6).
    // The world's BeginAction tests the art with the start, so the turn comes first: without the throw art the thrower still turns,
    // where the original does not (the charges and the end are the same).
    private static Transition ThrowItemNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        if (goal.Item is not { } item || goal.Tile is not { } tile) return ChargedEnd(owner, world, AiExecutorReason.ObjectGone);
        switch (goal.Node)
        {
            case 1:
                if (!world.FaceForAction(owner, null, tile)) return ChargedEnd(owner, world, AiExecutorReason.ActionRefused);
                var swing = world.BeginAction(owner, AiActionAnimation.Throw);
                if (swing.Start != AiActionStart.Started) return ChargedEnd(owner, world, AiExecutorReason.ActionRefused);
                return Next(2, Math.Max(swing.ActionFrameMs, 1)) with { Top = goal with { RestMs = Math.Max(swing.RestMs, 0) } };
            case 2:
                if (!world.ThrowItem(owner, item, tile)) return ChargedEnd(owner, world, AiExecutorReason.ActionRefused);
                return new(Move.Next, 3, Delay: Math.Max(goal.RestMs, 1), Reason: AiExecutorReason.Thrown);
            default:
                return ChargedEnd(owner, world, AiExecutorReason.Thrown);
        }
    }

    // ---- the item use (39) ----

    // Goal 39: range 1; within it with a clear first step → the use (0x428CD0: the item still in the user's inventory, then 0x462CC0),
    // else the walk near (goal 8). Both ends of the use are the end step.
    private static Transition UseItemNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        var target = goal.Target;
        switch (goal.Node)
        {
            case 1:
                return new(Move.Next, 2, Top: goal with { Range = 1 });
            case 2:
                if (target is null || world.Tile(target) is null) return End(AiExecutorReason.ObjectGone);
                return Within(world, owner, target, goal.Range) && world.FirstStepClear(owner, target, false)
                    ? Next(3)
                    : new(Move.Push, Frame: Child(GoalType.MoveNear, goal), Reason: AiExecutorReason.ActionApproach);
            default:
                if (goal.Item is not { } item || world.ItemHolder(item) != owner) return End(AiExecutorReason.ItemNotHeld);
                UseItemNow(world, owner, item, target);
                return End(AiExecutorReason.ItemUsed);
        }
    }

    // ---- the pickup (15, 16) ----

    // Goal 15 (its constant nodes 0x429370 … 0x429430 folded): on the item's tile → the attempt (16); otherwise range 0 and the walk near
    // (goal 8) onto it.
    private static Transition PickupNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        if (goal.Target is not { } item || world.Tile(item) is not { } there) return End(AiExecutorReason.ObjectGone);
        if (world.Tile(owner) == there) return new(Move.Push, Frame: Child(GoalType.AttemptPickup, goal));
        return new(Move.Push, Frame: Child(GoalType.MoveNear, goal) with { Range = 0 }, Top: goal with { Range = 0 },
            Reason: AiExecutorReason.ActionApproach);
    }

    // Goal 16: the pickup (0x42A9B0: an item destroyed or off is not taken), then the stand animation and the end step either way.
    private static Transition AttemptPickupNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        if (goal.Target is { } item && !world.Gone(item)) world.PickUp(owner, item);
        return End(AiExecutorReason.PickedUp);
    }
}
