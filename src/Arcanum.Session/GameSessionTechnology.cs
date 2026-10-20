using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// One discipline of a critter now (ARC-TECHNOLOGY-002): the stored degree, the degree its effective intelligence lets count,
/// and the effective expertise in percent (unclamped, as the original).
/// </summary>
public readonly record struct SessionTechDiscipline(int Discipline, int Degree, int CountingDegree, int Expertise);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero's eight disciplines now (ARC-TECHNOLOGY-002), or null without a hero: the stored degrees of its advancement fields,
    /// each lowered while its effective intelligence (effects and poison included) is below the degree's minimum, and the
    /// expertise <see cref="TechDegreeRules.EffectiveExpertise"/> gives with the "expertise in discipline" changes of its durable
    /// effects (every cause). The schematic gate and the character sheet read this.
    /// </summary>
    public ImmutableArray<SessionTechDiscipline>? QueryHeroTechnology()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var degrees = _hero.State.Advancement.TechDegrees;
        var intelligence = _hero.Stats.Query(new(0)).Snapshot!.GetEffective(CharacterStat.Intelligence);
        var changes = _effects.Entries.SelectMany(entry => _effects.Definitions[entry.Number].Changes)
            .Where(change => change.Kind == EffectAttributeKind.TechDiscipline).ToArray();
        var result = ImmutableArray.CreateBuilder<SessionTechDiscipline>(TechDegreeRules.Disciplines);
        for (var discipline = 0; discipline < TechDegreeRules.Disciplines; discipline++)
        {
            var effects = changes.Where(change => change.Attribute == discipline)
                .Select(change => new EffectModifier(EffectDomain.Operator(change.Operator), change.Value)).ToArray();
            result.Add(new(discipline, degrees[discipline], TechDegreeRules.CountingDegree(degrees[discipline], intelligence),
                TechDegreeRules.EffectiveExpertise(degrees[discipline], intelligence, effects)));
        }
        return result.MoveToImmutable();
    }
}
