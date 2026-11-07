using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

public static partial class AiExecutorRules
{
    /// <summary>The magic-hands animation goal 60 plays after the kneel (animation 10).</summary>
    public const int MagicHandsAnimation = 10;
    /// <summary>The kneel animation goal 60 starts with (animation 9).</summary>
    public const int KneelAnimation = 9;
    /// <summary>The highest animation number an art can carry (26 and above log "range exceeded" and read as 0, GT173).</summary>
    public const int AnimationCount = 26;

    /// <summary>
    /// The frame of the script action 62 (ARC-DEV-233, S14b, GT155–GT156): goal 60 (kneel, magic hands, kneel back) for a PC or NPC and animation 10, else goal 0, the animation played once from frame 0 to the last.
    /// Null for an animation outside 0..25 (the original logs an assertion and starts nothing).
    /// </summary>
    public static GoalFrame? PerformAnimationFrame(int animation, bool critter) =>
        animation is < 0 or >= AnimationCount ? null
        : critter && animation == MagicHandsAnimation ? new GoalFrame(GoalType.AnimateKneelMagicHands) { Animation = KneelAnimation }
        : new GoalFrame(GoalType.Animate) { Animation = animation };

    // ---- the animation goal 0 (GT168–GT172) ----
    // Nodes 1 (playing: 2, else 3), 2 the animation step (done: pop), 3 a critter lying in its fall gets up first (63 pushed), 4 a concealed NPC unconceals, 5 the start (the art becomes the animation
    // at frame 0; a stoned object, or a critter with flag 0x60 unless the animation is 17..19, refuses), 6 the refused end (2 action points of the current participant). The end node stands a critter
    // (animation 0, 5 when concealed); any other object keeps its last frame.
    private static Transition AnimateNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
            {
                var step = FrameStep(owner, goal, world, 0, allowFall: true, allowEleven: true);
                return step.Move == Move.Next ? new(Move.Pop) : step;
            }
            case 3:
                return world.ShownAnimation(owner) == DyingAnimationRules.FallDown ? new(Move.Push, Frame: GetUpFrame()) : Next(4);
            case 4:
            {
                var mover = world.Mover(owner);
                return mover.Concealed && !mover.Pc ? new(Move.Restart, Delay: world.Unconceal(owner)) : Next(5);
            }
            case 5:
            {
                var lyingAnimation = goal.Animation is >= DyingAnimationRules.Decapitation and <= DyingAnimationRules.SeveredLeg;
                if (world.Stoned(owner) || world.Paralysed(owner) && !lyingAnimation) return Next(6);
                return new(Move.Restart, Top: StartAnimation(owner, goal, world));
            }
            default:
                ChargeSmall(world, owner);
                return new(Move.End);
        }
    }

    // ---- the kneel goal 60 (GT175–GT177) and its tail, the reverse goal 51 (GT178) ----
    // Goal 60 plays the kneel (9) forward, then the hands (10) once at the kneel's rate, then is replaced by goal 51, which plays the kneel backward to frame 0 and stands the critter.
    private static Transition KneelNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
            {
                // The step (0x42E2D0): the start's guards (a stoned object, flag 0x40) send the goal to its tail; at the last frame of the kneel the art becomes the hands at frame 0 and the rate is kept.
                // At the last frame of the hands the art is the kneel at its last frame and the goal is replaced by the reverse goal.
                if (world.Stoned(owner) || world.Paralysed(owner)) return Reverse(goal, world, owner, false);
                var last = Math.Max((world.Clip(owner, goal.Animation)?.Frames ?? 1) - 1, 0);
                if (goal.FrameIndex < last)
                {
                    var next = goal.FrameIndex + 1;
                    world.ShowArt(owner, goal.Animation, next);
                    return new(Move.Restart, Delay: Math.Max(goal.RestMs, 0), Top: goal with { FrameIndex = next });
                }
                if (goal.Animation == KneelAnimation)
                {
                    world.ShowArt(owner, MagicHandsAnimation, 0);
                    return new(Move.Restart, Delay: Math.Max(goal.RestMs, 0), Top: goal with { Animation = MagicHandsAnimation, FrameIndex = 0 });
                }
                return Reverse(goal, world, owner, true);
            }
            case 3:
                return world.ShownAnimation(owner) == DyingAnimationRules.FallDown ? new(Move.Push, Frame: GetUpFrame()) : Next(4);
            case 4:
            {
                var mover = world.Mover(owner);
                return mover.Concealed && !mover.Pc ? new(Move.Restart, Delay: world.Unconceal(owner)) : Next(5);
            }
            case 5:
                // The start (0x42E1B0) sets the art to the kneel at frame 0 before its guards, then refuses a stoned object and a critter with flag 0x40; a refused start has no end node (GT176).
                world.ShowArt(owner, KneelAnimation, 0);
                if (world.Stoned(owner) || world.Paralysed(owner)) return Next(6);
                return new(Move.Restart, Top: StartAnimation(owner, goal with { Animation = KneelAnimation }, world, setArt: false));
            default:
                ChargeSmall(world, owner);
                return new(Move.End);
        }
    }

    // The replace of goal 60 by goal 51 (delay 0): the reverse goal starts from the kneel's last frame (an animation of the hands that ended, or a guard that refused, leaves the art as it is).
    private static Transition Reverse(GoalFrame goal, IAiGoalWorld world, string owner, bool showKneelEnd)
    {
        var last = Math.Max((world.Clip(owner, KneelAnimation)?.Frames ?? 1) - 1, 0);
        if (showKneelEnd) world.ShowArt(owner, KneelAnimation, last);
        return new(Move.Replace, Frame: new GoalFrame(GoalType.AnimateReverse) { Animation = KneelAnimation, FrameIndex = last }, Delay: 0);
    }

    private static Transition ReverseNode(string owner, GoalFrame goal, IAiGoalWorld world)
    {
        switch (goal.Node)
        {
            case 1:
                return PlayingNow(goal) ? Next(2) : Next(3);
            case 2:
            {
                // The reverse step (0x42E8B0): one frame back each delay; at frame 0 the playing flag is cleared and the goal pops (its end node stands the critter).
                if (world.Stoned(owner) || world.Paralysed(owner) || goal.FrameIndex <= 0) return new(Move.Pop);
                var previous = goal.FrameIndex - 1;
                world.ShowArt(owner, goal.Animation, previous);
                return new(Move.Restart, Delay: Math.Max(goal.RestMs, 0), Top: goal with { FrameIndex = previous });
            }
            case 3:
            {
                // The start (0x42E720): refusals as in goal 0; the art stays at the frame it holds and the rate is recomputed.
                if (world.Stoned(owner) || world.Paralysed(owner)) return Next(4);
                var clip = world.Clip(owner, goal.Animation);
                var started = goal with
                {
                    RestMs = clip is { } read ? DyingAnimationRules.Delay(read.FramesPerSecond) : DyingAnimationRules.UnreadArtDelayMs,
                    Flags = goal.Flags | GoalFrameFlags.Playing,
                };
                return new(Move.Restart, Top: started);
            }
            default:
                ChargeSmall(world, owner);
                return new(Move.End);
        }
    }
}
