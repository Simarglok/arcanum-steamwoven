using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>Saved gameplay randomness (ARC-WORLD-019): the seed and the number of draws made from its sequence.</summary>
public readonly record struct SessionRandomState(ulong Seed, long Draws);

/// <summary>
/// One roll as it was made: the caller's label, the inclusive range, the value and the generator position after it
/// (draws made so far). A roll with equal bounds draws nothing, so its position equals the one before.
/// </summary>
public sealed record SessionRoll(string Label, int Min, int Max, int Value, long Position);

/// <summary>A roll, or the typed reason none was made (<see cref="GameSessionFailure.InvalidRoll"/>,
/// <see cref="GameSessionFailure.RandomExhausted"/>). A refused roll draws nothing.</summary>
public sealed record SessionRollResult(SessionRoll? Roll, GameSessionFailure Failure);

/// <summary>
/// The session generator behind the attack resolver's <see cref="ICombatRollSource"/>: each roll is labelled
/// <c>combat.</c> and the <see cref="CombatRollPurpose"/> name. The source interface has no refusal channel: a range with
/// <c>min &gt; max</c> breaks its precondition and throws <see cref="ArgumentOutOfRangeException"/> (as in
/// <see cref="CombatRollLog"/>), and a generator that ran out of its 2^63 - 1 draws throws
/// <see cref="InvalidOperationException"/>. The label is checked, not recorded; the resolver's log keeps the trace.
/// </summary>
internal sealed class SessionCombatRolls(GameSessionOwner owner) : ICombatRollSource
{
    public int Roll(CombatRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        var result = owner.Roll(Label(purpose), min, max);
        return result.Roll?.Value ?? throw new InvalidOperationException($"Session roll {purpose} refused: {result.Failure}.");
    }

    internal static string Label(CombatRollPurpose purpose) => "combat." + purpose;
}

/// <summary>
/// The session generator behind the magic rules' <see cref="ISpellRollSource"/> (ARC-MAGIC-005): each roll is labelled
/// <c>magic.</c> and the <see cref="SpellRollPurpose"/> name; refusals throw as in <see cref="SessionCombatRolls"/>.
/// </summary>
internal sealed class SessionSpellRolls(GameSessionOwner owner) : ISpellRollSource
{
    public int Roll(SpellRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        var result = owner.Roll(Label(purpose), min, max);
        return result.Roll?.Value ?? throw new InvalidOperationException($"Session roll {purpose} refused: {result.Failure}.");
    }

    internal static string Label(SpellRollPurpose purpose) => "magic." + purpose;
}
