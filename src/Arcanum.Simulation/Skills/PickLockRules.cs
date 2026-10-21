namespace Arcanum.Simulation.Skills;

/// <summary>What one pick-locks attempt does to its portal or container (ARC-SKILLS-006, ledger PL1).</summary>
public enum PickLockResult
{
    /// <summary>A locked target was unlocked (a success, critical or not).</summary>
    Unlocked,
    /// <summary>An unlocked target was locked (a success).</summary>
    Locked,
    /// <summary>A locked target was jammed (a critical failure).</summary>
    Jammed,
    /// <summary>Nothing changed (a failure, or a critical failure on an unlocked target).</summary>
    Failed,
}

/// <summary>
/// One attempt's outcome: the result and whether the original calls its witnesses (the guards and owners nearby who may see or
/// hear the picker, 0x4AEE50), with <see cref="WitnessFailure"/> set when it calls them for a failed attempt.
/// </summary>
public readonly record struct PickLockOutcome(PickLockResult Result, bool WitnessesCalled, bool WitnessFailure);

/// <summary>
/// The pick-locks case of the skill handler (ARC-SKILLS-006), as the retail 1.0.7.4 executable runs it after the shared check
/// (ledger PL1–PL6): it reads only whether the target is locked now (the lock query, LK2), never its jam, magical hold or
/// always-locked bits, and works the same for a portal and a container. On a locked target a success unlocks (the setter
/// schedules the relock an hour later, LK3), a critical failure jams (relock a day later, LK4), and the witnesses are called
/// either way; on an unlocked target a success locks it and nothing else happens.
/// </summary>
public static class PickLockRules
{
    public static PickLockOutcome Resolve(bool locked, bool success, bool critical)
    {
        if (!locked) return new(success ? PickLockResult.Locked : PickLockResult.Failed, false, false);
        if (success) return new(PickLockResult.Unlocked, true, false);
        return new(critical ? PickLockResult.Jammed : PickLockResult.Failed, true, true);
    }
}
