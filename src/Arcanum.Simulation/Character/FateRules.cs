using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Character;

/// <summary>
/// The PC's fate kinds (ARC-WORLD-054), numbered as the source numbers them (its mask table at 0x5BC48C): kind 0 is the full
/// heal, used at once; each other kind has one armed bit (1 &lt;&lt; (kind − 1)) of the PC's fate flags until its event uses it.
/// </summary>
public enum FateKind
{
    FullHeal = 0,
    ForceGoodReaction = 1,
    CriticalHit = 2,
    CriticalMiss = 3,
    SaveAgainstMagick = 4,
    SpellAtMaximum = 5,
    Gambling = 6,
    Heal = 7,
    PickPocket = 8,
    Repair = 9,
    PickLocks = 10,
    DisarmTraps = 11,
}

/// <summary>The PC's fate points (the source's stat 20, 0..100) and its armed fate flags (field 0xFE, bits 0..10).</summary>
public readonly record struct FateState(int Points, int Armed)
{
    public static FateState None => default;

    public bool IsValid => Points is >= 0 and <= FateRules.MaxPoints && (Armed & ~FateRules.AllArmed) == 0;
}

/// <summary>What an arm, undo, use or grant did.</summary>
public enum FateStatus
{
    /// <summary>A point was spent and the kind's bit set.</summary>
    Armed,
    /// <summary>A point was spent on the full heal, which the caller applies now.</summary>
    UsedAtOnce,
    /// <summary>Arm: the kind is already armed (nothing spent).</summary>
    AlreadyArmed,
    /// <summary>Arm: no point left (nothing changed).</summary>
    NoPoints,
    /// <summary>Undo: the bit was cleared and the point refunded.</summary>
    Undone,
    /// <summary>Undo or use: the kind is not armed (nothing changed).</summary>
    NotArmed,
    /// <summary>Use: the bit was cleared; the event takes its fated outcome.</summary>
    Used,
    /// <summary>Grant: one point more (none at the maximum).</summary>
    Granted,
}

public readonly record struct FateChange(FateStatus Status, FateState Before, FateState After)
{
    public bool Changed => Before != After;
}

/// <summary>
/// The fate rules of the retail 1.0.7.4 executable (ARC-WORLD-054, ledger FT1–FT8), read by two readers: arm (0x4F50C0) spends
/// a point and sets the kind's bit, or for the full heal applies it at once (0x4F52D0); undo (0x4F51B0) clears the bit and
/// refunds the point; use (0x4F5270) clears an armed bit for its event; the dialog action <c>fp</c> and the script command
/// "grant one fate point" add a point. Points pass the stat setter's clamp 0..100, so a refund or grant at 100 is lost.
/// Pure functions over explicit values; invalid input throws <see cref="ArgumentException"/>.
/// </summary>
public static class FateRules
{
    public const int MaxPoints = 100;
    public const int KindCount = 12;
    /// <summary>The bits of kinds 1..11.</summary>
    public const int AllArmed = (1 << (KindCount - 1)) - 1;

    /// <summary>The kind's armed bit, 0 for the full heal (the source's mask table).</summary>
    public static int Mask(FateKind kind) => Defined(kind) == FateKind.FullHeal ? 0 : 1 << ((int)kind - 1);

    /// <summary>Whether the kind is armed (0x4F5090); the full heal never is.</summary>
    [OriginalFunction(0x4F5090, OriginalFunctionScope.Full)]
    public static bool IsArmed(FateState state, FateKind kind) => (Valid(state).Armed & Mask(kind)) != 0;

    /// <summary>
    /// Arms a kind (0x4F50C0, single player): refused when armed, then when no point is left; otherwise one point is spent and the
    /// bit set, or for the full heal nothing is armed and the caller applies the heal (<see cref="FateStatus.UsedAtOnce"/>).
    /// </summary>
    [OriginalFunction(0x4F50C0, OriginalFunctionScope.Partial)]
    public static FateChange Arm(FateState state, FateKind kind)
    {
        var mask = Mask(kind);
        Valid(state);
        if ((state.Armed & mask) != 0) return new(FateStatus.AlreadyArmed, state, state);
        if (state.Points <= 0) return new(FateStatus.NoPoints, state, state);
        var after = new FateState(state.Points - 1, state.Armed | mask);
        return new(mask == 0 ? FateStatus.UsedAtOnce : FateStatus.Armed, state, after);
    }

    /// <summary>Undoes an armed kind (0x4F51B0): the bit is cleared and the point refunded, up to 100.</summary>
    [OriginalFunction(0x4F51B0, OriginalFunctionScope.Partial)]
    public static FateChange Undo(FateState state, FateKind kind)
    {
        var mask = Mask(kind);
        Valid(state);
        if ((state.Armed & mask) == 0) return new(FateStatus.NotArmed, state, state);
        return new(FateStatus.Undone, state, new(Math.Min(MaxPoints, state.Points + 1), state.Armed & ~mask));
    }

    /// <summary>The event of an armed kind uses it (0x4F5270): the bit is cleared, no point is refunded.</summary>
    [OriginalFunction(0x4F5270, OriginalFunctionScope.Partial)]
    public static FateChange Use(FateState state, FateKind kind)
    {
        var mask = Mask(kind);
        Valid(state);
        if ((state.Armed & mask) == 0) return new(FateStatus.NotArmed, state, state);
        return new(FateStatus.Used, state, state with { Armed = state.Armed & ~mask });
    }

    /// <summary>One more point (the dialog action <c>fp</c>, the script command "grant one fate point"), up to 100.</summary>
    public static FateChange Grant(FateState state) =>
        new(FateStatus.Granted, Valid(state), state with { Points = Math.Min(MaxPoints, state.Points + 1) });

    /// <summary>The fate kind a skill check can use (the run's mapping, ledger SC14); null for a skill without one.</summary>
    public static FateKind? Of(SkillFate fate) => fate switch
    {
        SkillFate.None => null,
        SkillFate.Gambling => FateKind.Gambling,
        SkillFate.Heal => FateKind.Heal,
        SkillFate.PickPocket => FateKind.PickPocket,
        SkillFate.Repair => FateKind.Repair,
        SkillFate.PickLocks => FateKind.PickLocks,
        SkillFate.DisarmTraps => FateKind.DisarmTraps,
        _ => throw new ArgumentOutOfRangeException(nameof(fate)),
    };

    private static FateKind Defined(FateKind kind) => Enum.IsDefined(kind) ? kind : throw new ArgumentOutOfRangeException(nameof(kind));

    private static FateState Valid(FateState state) => state.IsValid ? state : throw new ArgumentOutOfRangeException(nameof(state));
}
