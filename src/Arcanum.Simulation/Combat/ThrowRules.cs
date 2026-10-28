namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-COMBAT-017: the thrown range of an item (0x4622A0, ledger RH4): how far a thrower throws it. The shared check reads it as the
/// throwing skill's range (impossible beyond it, SC8) and a thrown item's flight ends beyond it (PF9). The weight is the caller's:
/// the item's effective weight (0x461370, its stored weight plus its magic adjustment), which the session does not keep.
/// </summary>
public static class ThrowRules
{
    /// <summary>The largest item weight the rule takes.</summary>
    public const int MaxWeight = 1_000_000;

    /// <summary>
    /// <c>max(1, (A / max(1, W / 10)) / 5)</c> with <c>A = 50 × strength</c>, raised by <c>A / 2</c> from expert throwing, and
    /// <c>W</c> the weight; signed 32-bit, every division toward zero, as the original computes it.
    /// </summary>
    [OriginalFunction(0x4622A0, OriginalFunctionScope.Partial)]
    public static int Range(int strength, SkillTraining throwing, int weight)
    {
        if (!Enum.IsDefined(throwing)) throw new ArgumentOutOfRangeException(nameof(throwing));
        if (weight is < 0 or > MaxWeight) throw new ArgumentOutOfRangeException(nameof(weight));
        var arm = unchecked(strength * 50);
        if (throwing >= SkillTraining.Expert) arm = unchecked(arm + arm / 2);
        var per = Math.Max(1, weight / 10);
        return Math.Max(1, arm / per / 5);
    }
}
