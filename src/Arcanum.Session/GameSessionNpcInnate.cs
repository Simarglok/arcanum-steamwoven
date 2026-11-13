using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1509 (the gender and race cases of the base stat setter 0x4B0980, ledger RM3b): an NPC's innate effects. The setter removes the first entry of the race cause (0, case 3 of the table 0x4B0E74) or
/// the gender cause (9, case 2) from the NPC's list before it stores the new race (stat 27) or gender (stat 26), then adds effect <c>0x40 + race</c> with the race cause, or, for gender 0, effect 0x14A with the
/// gender cause. The rules of those effects are the effect table's, so the host attaches them (like the encumbrance rule, <see cref="GameSessionOwner.AttachEncumbranceEffect"/>); without a rule the swap is not ready.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The effect number of a race's innate effect: <c>0x40 + race</c> (the setter's add after the race store).</summary>
    public const int RaceEffectBase = 0x40;

    /// <summary>The effect a woman's gender store adds (0x14A = 330).</summary>
    public const int FemaleEffectNumber = 0x14A;

    private readonly Dictionary<int, SessionEffectDefinition> _innateEffects = [];

    /// <summary>
    /// ARC-MAGIC-1509: attaches the rules of the innate effects (<see cref="RaceEffectBase"/> + a race 0..10, and <see cref="FemaleEffectNumber"/>) the host reads from <c>rules\effect.mes</c> and the effect tables. Not state:
    /// never saved, attached again after a restore; null (or a call with an empty set) detaches them. A rule of another number or one the effect list does not admit throws <see cref="ArgumentException"/>
    /// and attaches nothing.
    /// </summary>
    public void AttachInnateEffects(IEnumerable<SessionEffectDefinition>? definitions)
    {
        ThrowIfDisposed();
        var accepted = new Dictionary<int, SessionEffectDefinition>();
        foreach (var definition in definitions ?? [])
        {
            if (definition is null || !IsInnateNumber(definition.Number) || !EffectDomain.IsValid(definition)) throw new ArgumentException("Not an admitted rule of an innate effect.", nameof(definitions));
            accepted[definition.Number] = definition;
        }
        _innateEffects.Clear();
        foreach (var pair in accepted) _innateEffects[pair.Key] = pair.Value;
    }

    private static bool IsInnateNumber(int number) => number == FemaleEffectNumber || number is >= RaceEffectBase and < RaceEffectBase + 11;

    /// <summary>The attached rule of innate effect <paramref name="number"/>, or null.</summary>
    internal SessionEffectDefinition? InnateRule(int number) => _innateEffects.GetValueOrDefault(number);

    /// <summary>The removal that opens the race or gender case of the setter (0x4EA2E0): the first entry of the race or gender cause leaves the NPC's list; NoChange when it holds none.</summary>
    internal SessionEffectResult RemoveNpcInnate(SessionLifeTarget target, bool gender) =>
        RemoveNpcEffectsCausedBy(Version, target, gender ? EffectCause.Gender : EffectCause.Race);

    /// <summary>The add that closes it (0x4E9F70): the innate effect <paramref name="rule"/> with the race or gender cause.</summary>
    internal SessionEffectResult AddNpcInnate(SessionLifeTarget target, SessionEffectDefinition rule, bool gender) =>
        AddNpcEffect(Version, target, rule, gender ? EffectCause.Gender : EffectCause.Race);
}
