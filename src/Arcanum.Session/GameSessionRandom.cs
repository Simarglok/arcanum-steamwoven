using Arcanum.Simulation.Combat;

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
/// <c>combat.</c> and the <see cref="CombatRollPurpose"/> name. The source interface has no refusal channel, so a refused
/// roll throws <see cref="InvalidOperationException"/>; <see cref="CombatRollLog"/> already refuses bad ranges first, so
/// only a generator that ran out of its 2^63 - 1 draws can reach it.
/// </summary>
internal sealed class SessionCombatRolls(GameSessionOwner owner) : ICombatRollSource
{
    public int Roll(CombatRollPurpose purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        var result = owner.Roll(Label(purpose), min, max);
        return result.Roll?.Value ?? throw new InvalidOperationException($"Session roll {purpose} refused: {result.Failure}.");
    }

    internal static string Label(CombatRollPurpose purpose) => "combat." + purpose;
}
