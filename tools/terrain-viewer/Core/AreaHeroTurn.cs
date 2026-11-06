using Arcanum.Simulation.Goals;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-145 (MIG13 S10a, GT102): the hero's rotate goal (53) as the viewer plays it: one 45-degree step the short way every
/// <see cref="HeroInputRules.RotateStepMs"/> game milliseconds (the node 0x42C440), the facing reached after the last step and the run over
/// one tick later; an end (an interrupt, the hero moving) snaps the facing to the target (the end node 0x42C650). The hero is not a run of
/// the goal table yet (S10b), so this is the goal's own timeline over the world clock.
/// </summary>
public sealed class AreaHeroTurn
{
    /// <summary>Milliseconds from the start to the first step: the first frame of a started goal comes at the next pass (5 ms, S4).</summary>
    public const int FirstStepMs = 5;

    private long _due;

    /// <summary>The goal is running.</summary>
    public bool Active { get; private set; }
    /// <summary>The rotation the art shows now (0..7).</summary>
    public int Facing { get; private set; }
    /// <summary>The rotation the goal turns to (0..7).</summary>
    public int Target { get; private set; }

    /// <summary>Starts the goal from <paramref name="facing"/> toward <paramref name="target"/> at <paramref name="nowMs"/>.</summary>
    public void Start(int facing, int target, long nowMs)
    {
        Facing = facing; Target = target; _due = nowMs + FirstStepMs; Active = true;
    }

    /// <summary>
    /// Runs every step due by <paramref name="nowMs"/>; true when the facing changed. The run ends at the step that finds the target
    /// reached (one tick after the step that reached it).
    /// </summary>
    public bool Advance(long nowMs)
    {
        var changed = false;
        while (Active && nowMs >= _due)
        {
            if (Facing == Target) { Active = false; break; }
            Facing = HeroInputRules.RotateStep(Facing, Target);
            changed = true;
            _due += HeroInputRules.RotateStepMs;
        }
        return changed;
    }

    /// <summary>The end node: the facing snaps to the target and the goal stops; true when that changed the facing. Nothing without a run.</summary>
    public bool End()
    {
        if (!Active) return false;
        Active = false;
        var changed = Facing != Target;
        Facing = Target;
        return changed;
    }
}
