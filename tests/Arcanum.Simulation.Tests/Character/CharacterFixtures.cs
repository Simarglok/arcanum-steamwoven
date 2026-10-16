using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>Synthetic fixtures only; innate effect values are invented and deliberately differ from source rules.</summary>
internal static class CharacterFixtures
{
    public static readonly CharacterStat[] Primary =
    [
        CharacterStat.Strength, CharacterStat.Dexterity, CharacterStat.Constitution, CharacterStat.Beauty,
        CharacterStat.Intelligence, CharacterStat.Perception, CharacterStat.Willpower, CharacterStat.Charisma,
    ];

    public static StatEffectDefinition Effect(int id, params StatModifier[] modifiers) => new(id, [.. modifiers]);

    public static StatModifier Add(CharacterStat stat, int value) => new(stat, StatModifierOperator.Add, value);

    public static CharacterStatsDefinition Definition(CharacterRace race = CharacterRace.Human,
        CharacterGender gender = CharacterGender.Male, InnateEffectTable? innate = null) =>
        new(race, gender, innate ?? InnateEffectTable.Empty, CharacterStatLimits.Default);

    public static CharacterStatsOwner Owner(CharacterRace race = CharacterRace.Human,
        CharacterGender gender = CharacterGender.Male, InnateEffectTable? innate = null)
    {
        var result = CharacterStatsOwner.Create(Definition(race, gender, innate));
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
        Assert.IsNotNull(result.Owner);
        return result.Owner;
    }

    public static CharacterStatsSnapshot Snap(CharacterStatsOwner owner, int sectorAptitude = 0)
    {
        var result = owner.Query(new(sectorAptitude));
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
        Assert.IsNotNull(result.Snapshot);
        return result.Snapshot;
    }

    public static void Changed(CharacterStatsMutationResult result)
    {
        Assert.AreEqual(CharacterStatsDisposition.Changed, result.Disposition);
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
    }

    /// <summary>Changed or NoChange: data rows may hit the default value.</summary>
    public static void Accepted(CharacterStatsMutationResult result)
    {
        Assert.AreNotEqual(CharacterStatsDisposition.Rejected, result.Disposition);
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
    }

    public static void Rejected(CharacterStatsMutationResult result, CharacterStatsFailure failure)
    {
        Assert.AreEqual(CharacterStatsDisposition.Rejected, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.AddedEffect);
    }

    public static void NoChange(CharacterStatsMutationResult result)
    {
        Assert.AreEqual(CharacterStatsDisposition.NoChange, result.Disposition);
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
    }

    public static AppliedEffectId Added(CharacterStatsMutationResult result)
    {
        Changed(result);
        Assert.IsNotNull(result.AddedEffect);
        return result.AddedEffect.Value;
    }

    /// <summary>Every base and effective value, for whole-state equality across revisions.</summary>
    public static int[] Values(CharacterStatsSnapshot snapshot) => Enum.GetValues<CharacterStat>()
        .SelectMany(stat => new[] { snapshot.GetBase(stat), snapshot.GetEffective(stat) }).ToArray();

    public static InnateEffectTable SyntheticInnate() => new(
        new Dictionary<CharacterRace, StatEffectDefinition>
        {
            [CharacterRace.Dwarf] = Effect(901, Add(CharacterStat.Constitution, 2), Add(CharacterStat.Charisma, -1)),
            [CharacterRace.Elf] = Effect(902, Add(CharacterStat.Dexterity, 1), Add(CharacterStat.Constitution, -1)),
        },
        new Dictionary<CharacterGender, StatEffectDefinition>
        {
            [CharacterGender.Female] = Effect(903, Add(CharacterStat.Strength, -2), Add(CharacterStat.Constitution, 3)),
        });

    public static int Count(CharacterStatsSnapshot snapshot, EffectCause cause) =>
        snapshot.AppliedEffects.Count(effect => effect.Cause == cause);

    public static ImmutableArray<StatModifier> Modifiers(int count) =>
        Enumerable.Repeat(Add(CharacterStat.Strength, 1), count).ToImmutableArray();
}
