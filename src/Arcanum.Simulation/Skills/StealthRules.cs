using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>How a concealed critter steps (ARC-SKILLS-008, ledger ST3): the animation and whether it may run.</summary>
public enum StealthWalk
{
    /// <summary>The stealth walk (animation 3): a concealed critter below expert in prowling.</summary>
    StealthWalk,
    /// <summary>The ordinary walk (animation 1): a concealed expert.</summary>
    Walk,
}

/// <summary>A concealed critter's gait: its walk animation and whether it may run (only a master, with fatigue left).</summary>
public readonly record struct StealthGait(StealthWalk Walk, bool CanRun);

/// <summary>
/// Prowling (ARC-SKILLS-008, task S12 of the skills plan), as the retail 1.0.7.4 executable runs it (ledger ST1–ST6 and the
/// awareness rows AW4–AW5): a concealed critter's gait, and the margin by which it lowers an observer's perception in a sight or
/// hearing check. Concealment itself is a pair of critter flags the session keeps; it costs nothing to turn on or off.
/// </summary>
public static class StealthRules
{
    /// <summary>
    /// The gait of a concealed critter by its prowling training (ST3): below expert the stealth walk and never a run; an expert
    /// the ordinary walk and never a run; a master the ordinary walk, and a run while its fatigue is above 0.
    /// </summary>
    public static StealthGait Gait(SkillTraining prowling, bool fatigueLeft) => prowling switch
    {
        SkillTraining.None or SkillTraining.Apprentice => new(StealthWalk.StealthWalk, false),
        SkillTraining.Expert => new(StealthWalk.Walk, false),
        SkillTraining.Master => new(StealthWalk.Walk, fatigueLeft),
        _ => throw new ArgumentOutOfRangeException(nameof(prowling)),
    };

    /// <summary>The prowler's margin over one observer (AW4): <c>clamp(effectiveness − difficulty, 0, 100)</c>, where both are the
    /// shared check's for prowling with the prowler as source and the observer as target (a sight or hearing check).</summary>
    public static int Margin(int effectiveness, int difficulty) => (int)Math.Clamp((long)effectiveness - difficulty, 0, 100);

    /// <summary>The observer's perception after the margin (AW4): <c>perception − perception × margin / 100</c>, truncated toward
    /// zero.</summary>
    public static int PerceptionAfterMargin(int perception, int margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(margin, 100);
        return perception - (int)((long)perception * margin / 100);
    }
}
