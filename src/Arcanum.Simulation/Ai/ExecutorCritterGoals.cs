using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-137 (MIG13 S6, docs/architecture/goal-table.md §3.5, the S6 read GT26–GT37): the nodes of the critter-state goals, the stun 57, the knock-down 62, the get-up 63 and
/// dying 31, as the goal stepper interprets them. The animation work is the port's (<see cref="IAiGoalWorld.Clip"/>, <see cref="IAiGoalWorld.ShowArt"/>): a goal sets the art to its
/// animation at frame 0, stores the frame delay (1000 / the ART's rate, 100 ms when the header does not read) in <see cref="GoalFrame.RestMs"/> and the playing flag, and each step shows
/// one frame more, the last staying. The starters are <see cref="CritterStateRules"/>' (session side).
/// </summary>
public static partial class AiExecutorRules
{
    /// <summary>The stun animation the stun goal plays (animation 23).</summary>
    public const int StunAnimation = 23;
    /// <summary>Animation 11, which goal 0 also plays under a paralysis (GT170; its meaning was not read).</summary>
    public const int ElevenAnimation = 11;
    /// <summary>The get-up animation (animation 8).</summary>
    public const int GetUpAnimation = 8;
    /// <summary>A concealed NPC is shown in animation 5 where a standing one shows 0 (the end nodes reset the art to it).</summary>
    public const int ConcealedAnimation = 5;
    /// <summary>The knock-down's wait after its last frame before the get-up replaces it (a conscious critter), in animation milliseconds.</summary>
    public const int KnockDownGetUpWaitMs = 200;
    /// <summary>The stun's countdown loses this many per skipped turn of a round (0x427000).</summary>
    public const int StunTurnCost = 3;
    /// <summary>The get-up's cost in action points in a round.</summary>
    public const int GetUpCost = 5;

    /// <summary>Whether <paramref name="goal"/> is one of the four critter-state goals.</summary>
    public static bool IsCritterState(GoalType goal) => goal is GoalType.AnimateStunned or GoalType.KnockDown or GoalType.GetUp or GoalType.Dying;

    /// <summary>The stun goal's frame (the starter 0x4357B0): the countdown (20 − constitution) / 2, truncated toward zero; the animation plays at least once.</summary>
    public static GoalFrame StunFrame(int constitution) => new(GoalType.AnimateStunned) { Animation = StunAnimation, Count = (20 - constitution) / 2 };

    /// <summary>The knock-down goal's frame (the starter 0x435A90).</summary>
    public static GoalFrame KnockDownFrame() => new(GoalType.KnockDown) { Animation = DyingAnimationRules.FallDown };

    /// <summary>The get-up goal's frame (the starter 0x434DE0).</summary>
    public static GoalFrame GetUpFrame() => new(GoalType.GetUp) { Animation = GetUpAnimation };

    /// <summary>The dying goal's frame (the starter 0x435080): <paramref name="animation"/> is the kill's choice (7, 17, 18 or 19); <paramref name="lying"/>: the critter lay in its fall (was unconscious) when killed, which the art may no longer say (a rebuilt host), so node 3 keeps the fall.</summary>
    public static GoalFrame DyingFrame(int animation, bool lying = false) => new(GoalType.Dying) { Animation = animation, Count = lying ? 1 : 0 };

    /// <summary>The dying animation the original keeps for a death: the fall for a death of at most 20 damage or a kill, 17 for a head hit, 19 for a leg hit, 18 otherwise (ledger FA10).</summary>
    public static int DyingAnimationOf(DeathAnimation death) => death switch
    {
        DeathAnimation.Head => DyingAnimationRules.Decapitation,
        DeathAnimation.Leg => DyingAnimationRules.SeveredLeg,
        DeathAnimation.Body => DyingAnimationRules.BlownOutChunk,
        _ => DyingAnimationRules.FallDown,
    };

    private static bool PlayingNow(GoalFrame goal) => (goal.Flags & GoalFrameFlags.Playing) != 0;

    private static GoalFrame WithPlaying(GoalFrame goal, bool playing) =>
        goal with { Flags = playing ? goal.Flags | GoalFrameFlags.Playing : goal.Flags & ~GoalFrameFlags.Playing };

    private static int StandingAnimation(IAiGoalWorld world, string owner) => world.Mover(owner) is { Prowling: true, Pc: false } ? ConcealedAnimation : 0;

    // The animation step of the three animated goals (0x42E070 for the stun, 0x42CC80, 0x42D6F0 for dying): one frame more each call, the delay stored by the start between them; at the last
    // frame the playing flag is cleared and the step answers "done". A stoned object refuses, and so does a critter with flag 0x40 outside the lying animations (17..19), with the playing flag
    // still set (the stun's frame step; the knock-down's allows animation 7 and 11 too).
    private static Transition FrameStep(string owner, GoalFrame goal, IAiGoalWorld world, int doneNode, bool allowFall, bool allowEleven = false)
    {
        if (world.Stoned(owner) || world.Paralysed(owner) && !(goal.Animation is >= DyingAnimationRules.Decapitation and <= DyingAnimationRules.SeveredLeg
            || allowFall && goal.Animation == DyingAnimationRules.FallDown || allowEleven && goal.Animation == ElevenAnimation)) return Next(doneNode);
        var last = Math.Max((world.Clip(owner, goal.Animation)?.Frames ?? 1) - 1, 0);
        if (goal.FrameIndex >= last) return new(Move.Next, doneNode, Top: WithPlaying(goal, false));
        var next = goal.FrameIndex + 1;
        world.ShowArt(owner, goal.Animation, next);
        return new(Move.Restart, Delay: Math.Max(goal.RestMs, 0), Top: goal with { FrameIndex = next });
    }

    // The common start of an animation (the stun, fall, get-up and dying starts): the art is set to the animation at frame 0, its delay is stored (100 ms when the header does not read) and
    // the playing flag set. False when the header does not read and the goal is the stun (GT26).
    private static GoalFrame StartAnimation(string owner, GoalFrame goal, IAiGoalWorld world, bool setArt = true)
    {
        if (setArt) world.ShowArt(owner, goal.Animation, 0);
        var clip = world.Clip(owner, goal.Animation);
        return WithPlaying(goal with { FrameIndex = 0, RestMs = clip is { } read ? DyingAnimationRules.Delay(read.FramesPerSecond) : DyingAnimationRules.UnreadArtDelayMs }, true);
    }

    // ---- the stun (goal 57) ----

    private static Transition StunNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
                return FrameStep(owner, goal, world, 7, allowFall: false);
            case 3:
                // A stunned critter that lies in its fall first gets up (0x426F10, the get-up goal pushed).
                return world.ShownAnimation(owner) == DyingAnimationRules.FallDown ? new(Move.Push, Frame: GetUpFrame()) : Next(4);
            case 4:
            {
                var mover = world.Mover(owner);
                return mover.Concealed && !mover.Pc ? new(Move.Restart, Delay: world.Unconceal(owner)) : Next(5);
            }
            case 5:
            {
                // The start sets the art before its guards (GT37 f), then refuses a stoned object and a critter with flag 0x40; an unreadable header refuses too (the stun ends at once).
                world.ShowArt(owner, StunAnimation, 0);
                if (world.Stoned(owner) || world.Paralysed(owner) || world.Clip(owner, StunAnimation) is null) return Next(6);
                return new(Move.Restart, Top: StartAnimation(owner, goal, world, setArt: false));
            }
            case 6:
                ChargeSmall(world, owner);
                return new(Move.End);
            default:
            {
                // The countdown (0x426F60): out of a round one play less each time the animation reaches its last frame; in a round the animation loops and the round's skipped turns count
                // (GT27, session side). At 0 or below the goal ends (its end node clears the stunned bit).
                if (world.InRound) return Next(5);
                var count = goal.Count - 1;
                if (count > 0) return new(Move.Next, 5, Top: goal with { Count = count });
                // The original's countdown clears the bit itself and the end node clears it again; the end node, which every end runs, does it here.
                return new(Move.End, Top: goal with { Count = count });
            }
        }
    }

    // ---- the knock-down (goal 62) ----

    private static Transition KnockDownNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
                return FrameStep(owner, goal, world, 4, allowFall: true);
            case 3:
                // The fall start (0x42E4B0): the art becomes the fall-down animation at frame 0; no sound; the delay is stored (100 ms with an unreadable header, and the goal goes on).
                return new(Move.Restart, Top: StartAnimation(owner, goal, world));
            default:
                // After the last frame (0x42E460): an unconscious critter keeps its fall and the goal pops; a conscious one gets up after 200 ms (the goal replaced by the get-up goal).
                return world.Unconscious(owner) ? new(Move.Pop) : new(Move.Replace, Frame: GetUpFrame(), Delay: KnockDownGetUpWaitMs);
        }
    }

    // ---- the get-up (goal 63) ----

    private static Transition GetUpNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
            {
                var step = FrameStep(owner, goal, world, 0, allowFall: false);
                // The animation ends in a pop (the goal's end node then stands the critter up); a refusal of the step does the same.
                return step.Move == Move.Next ? new(Move.Pop) : step;
            }
            case 3:
            {
                // The start (0x42E590) refuses a critter that cannot act and, in a round, any critter that is not the current participant; the current one pays 5 action points (a failed
                // payment ends its turn and refuses). A refusal goes to the last node and the end step, whose end node stands the critter up at once (GT37 a).
                if (!world.CanAct(owner)) return Next(4);
                if (world.InRound)
                {
                    if (world.RoundActor != owner) return Next(4);
                    if (!Check(world, owner, GetUpCost))
                    {
                        world.EndSubturn(owner);
                        return Next(4);
                    }
                    Consume(world, owner, GetUpCost);
                }
                return new(Move.Restart, Top: StartAnimation(owner, goal, world));
            }
            default:
                ChargeSmall(world, owner);
                return new(Move.End);
        }
    }

    // ---- dying (goal 31) ----

    private static Transition DyingNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
                return FrameStep(owner, goal, world, 7, allowFall: false);
            case 3:
                // A critter that lies in its fall keeps it and goes straight to the pool (0x426F10 → 0x45D730).
                return DyingAnimationRules.LiesFallen(world.ShownAnimation(owner)) || goal.Count != 0 ? Next(7) : Next(4);
            case 4:
                return Next(5);
            case 5:
                // 0x42BC10: the art becomes the kill's animation at frame 0 (the third argument of the starter, slot 14). The critter leaves combat mode (the session's kill did).
                world.ShowArt(owner, goal.Animation, 0);
                return Next(6);
            case 6:
            {
                // The start (0x42D570): a critter whose art already shows the fall or a bloody death past frame 0 keeps it and goes on to the pool; otherwise the delay is stored.
                var clip = world.Clip(owner, goal.Animation);
                if (DyingAnimationRules.Start(world.ShownAnimation(owner), world.ShownFrame(owner), clip?.FramesPerSecond ?? 0) is null) return Next(7);
                return new(Move.Restart, Top: StartAnimation(owner, goal, world, setArt: false));
            }
            case 7:
                // The pool node (0x42FED0): the pool is the viewer's, made when the run is gone (BloodPoolRules.Leaves); it always answers true.
                return Next(9);
            case 8:
                ChargeSmall(world, owner);
                return new(Move.End);
            default:
                // Node 9 (0x42FF40): the dead flags and the notification; then the run ends.
                world.DyingFinished(owner);
                return new(Move.End);
        }
    }

    // The end nodes of the critter-state goals (the stun 0x42B250, the get-up 0x42B090): the art goes back to standing (5 when concealed) and the stun's bit is cleared. The knock-down and dying have none.
    // A killed run (GS21) skips the original's stun end node and is left to the AI tick's cleanup of an orphaned bit (GT35); Steamwoven has no such tick, so the bit is cleared by every end.
    private static void CritterEndNode(string owner, GoalFrame frame, IAiGoalWorld world)
    {
        switch (frame.Goal)
        {
            case GoalType.AnimateStunned:
                // The art goes back only while it is still the stun's (a knock-down or a death that interrupted the stun has set its own already).
                if (world.ShownAnimation(owner) == StunAnimation) world.ShowArt(owner, StandingAnimation(world, owner), 0);
                world.ClearStunned(owner);
                break;
            case GoalType.GetUp:
                world.ShowArt(owner, StandingAnimation(world, owner), 0);
                break;
            // ARC-DEV-233 (GT172, GT178): the end node of goals 0 and 51 stands a critter (the other objects keep their last frame); goal 60 has none. The owners of the host's runs are critters.
            case GoalType.Animate:
            case GoalType.AnimateReverse:
                world.ShowArt(owner, StandingAnimation(world, owner), 0);
                break;
        }
    }
}
