using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>FR-CHAR-008: stored base is separate from effective value; effects aggregate then clamp.</summary>
[TestClass]
public sealed class BaseEffectiveTests
{
    [TestMethod]
    public void DefaultsAreEightForPrimaryAndZeroForPoints()
    {
        var snapshot = Snap(Owner());
        Assert.AreEqual(0L, snapshot.Revision);
        Assert.AreEqual("steamwoven-character-stats-v1", CharacterStatsSnapshot.RulesId);
        foreach (var stat in Primary)
        {
            Assert.AreEqual(8, snapshot.GetBase(stat));
            Assert.AreEqual(8, snapshot.GetEffective(stat));
            Assert.IsFalse(snapshot.IsExtraordinary(stat));
        }
        Assert.AreEqual(0, snapshot.GetEffective(CharacterStat.MagickPoints));
        Assert.AreEqual(0, snapshot.GetEffective(CharacterStat.TechPoints));
        Assert.AreEqual(0, snapshot.AppliedEffects.Length);
    }

    [TestMethod]
    public void AddingAndRemovingSyntheticEffectRestoresEffectiveWithoutTouchingBase()
    {
        var owner = Owner();
        var before = Snap(owner);
        var id = Added(owner.AddEffect(Effect(7, Add(CharacterStat.Strength, 3)), EffectCause.Spell));
        var during = Snap(owner);
        Assert.AreEqual(8, during.GetBase(CharacterStat.Strength));
        Assert.AreEqual(11, during.GetEffective(CharacterStat.Strength));
        Assert.AreEqual(5500, during.GetEffective(CharacterStat.CarryWeight), "dependent derived value follows");
        Assert.AreEqual(EffectCause.Spell, during.AppliedEffects.Single().Cause);

        Changed(owner.RemoveEffect(id));
        var after = Snap(owner);
        CollectionAssert.AreEqual(Values(before), Values(after));
        Assert.AreEqual(2L, after.Revision);
        Assert.AreEqual(11, during.GetEffective(CharacterStat.Strength), "older snapshot stays detached");
    }

    [TestMethod]
    public void AggregationOrderIsPercentMultiplyDivideAddMinimumMaximum()
    {
        var owner = Owner(CharacterRace.Ogre);
        Changed(owner.SetBase(CharacterStat.Strength, 10));
        // 10 * 150% = 15; *2 = 30; /3 = 10; +5 = 15; minimum max(12, 17) -> 17; maximum min(19, 16) -> 16.
        Added(owner.AddEffect(Effect(1,
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, 5),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Maximum, 19),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Divide, 3),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Minimum, 12)), EffectCause.Item));
        Added(owner.AddEffect(Effect(2,
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Minimum, 17),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Multiply, 2),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Percent, 50),
            new StatModifier(CharacterStat.Strength, StatModifierOperator.Maximum, 16)), EffectCause.Bless));
        Assert.AreEqual(16, Snap(owner).GetEffective(CharacterStat.Strength));
        Assert.AreEqual(10, Snap(owner).GetBase(CharacterStat.Strength));
    }

    [TestMethod]
    [DataRow(-7, 1, DisplayName = "negative add is clamped to minimum 1")]
    [DataRow(30, 20, DisplayName = "positive add is clamped to human maximum 20")]
    public void EffectiveValueIsClampedAfterModifiers(int delta, int expected)
    {
        var owner = Owner();
        Added(owner.AddEffect(Effect(1, Add(CharacterStat.Perception, delta)), EffectCause.Curse));
        Assert.AreEqual(expected, Snap(owner).GetEffective(CharacterStat.Perception));
    }

    [TestMethod]
    public void DivisionTruncatesTowardZeroAndSaturatesExtremeProducts()
    {
        var owner = Owner();
        Changed(owner.SetBase(CharacterStat.Willpower, 7));
        Added(owner.AddEffect(Effect(1, new StatModifier(CharacterStat.Willpower, StatModifierOperator.Divide, -2)), EffectCause.Spell));
        Assert.AreEqual(1, Snap(owner).GetEffective(CharacterStat.Willpower), "7 / -2 = -3, clamped to 1");

        var huge = Owner();
        for (var i = 0; i < 4; i++)
            Added(huge.AddEffect(Effect(i, new StatModifier(CharacterStat.Intelligence, StatModifierOperator.Multiply, 1000),
                new(CharacterStat.Intelligence, StatModifierOperator.Percent, 1000)), EffectCause.Tech));
        Assert.AreEqual(20, Snap(huge).GetEffective(CharacterStat.Intelligence));
    }

    [TestMethod]
    public void IndependentCausesRemainWhenOneIsRemoved()
    {
        var owner = Owner();
        var first = Added(owner.AddEffect(Effect(1, Add(CharacterStat.Charisma, 2)), EffectCause.Item));
        var second = Added(owner.AddEffect(Effect(1, Add(CharacterStat.Charisma, 2)), EffectCause.Item));
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(12, Snap(owner).GetEffective(CharacterStat.Charisma));
        Changed(owner.RemoveEffect(first));
        Assert.AreEqual(10, Snap(owner).GetEffective(CharacterStat.Charisma));
        Rejected(owner.RemoveEffect(first), CharacterStatsFailure.UnknownEffect);
        Assert.AreEqual(second, Snap(owner).AppliedEffects.Single().Id);
    }

    [TestMethod]
    public void SetBaseChangesOnlyStoredValueAndCountsRevisions()
    {
        var owner = Owner();
        Changed(owner.SetBase(CharacterStat.Intelligence, 12));
        NoChange(owner.SetBase(CharacterStat.Intelligence, 12));
        Changed(owner.SetBase(CharacterStat.MagickPoints, 210));
        var snapshot = Snap(owner);
        Assert.AreEqual(2L, snapshot.Revision);
        Assert.AreEqual(2L, owner.Revision);
        Assert.AreEqual(12, snapshot.GetBase(CharacterStat.Intelligence));
        Assert.AreEqual(210, snapshot.GetEffective(CharacterStat.MagickPoints));
        Rejected(owner.SetBase(CharacterStat.TechPoints, 211), CharacterStatsFailure.OutOfRange);
        Rejected(owner.SetBase(CharacterStat.TechPoints, -1), CharacterStatsFailure.OutOfRange);
        Rejected(owner.SetBase((CharacterStat)99, 1), CharacterStatsFailure.InvalidStat);
        Rejected(owner.SetBase((CharacterStat)(-1), 1), CharacterStatsFailure.InvalidStat);
        Assert.AreEqual(2L, owner.Revision);
    }

    [TestMethod]
    public void MalformedEffectsAndCausesAreRejectedWithoutStateChange()
    {
        var owner = Owner();
        var before = Values(Snap(owner));
        foreach (var effect in new StatEffectDefinition?[]
        {
            null, new(1, default), new(1, ImmutableArray<StatModifier>.Empty), new(-1, Modifiers(1)), new(1, Modifiers(8)),
            Effect(1, new StatModifier((CharacterStat)99, StatModifierOperator.Add, 1)),
            Effect(1, new StatModifier(CharacterStat.Strength, (StatModifierOperator)99, 1)),
            Effect(1, new StatModifier(CharacterStat.Strength, StatModifierOperator.Divide, 0)),
            Effect(1, Add(CharacterStat.Strength, 1001)), Effect(1, Add(CharacterStat.Strength, int.MinValue)),
        })
            Rejected(owner.AddEffect(effect, EffectCause.Spell), CharacterStatsFailure.InvalidEffect);
        foreach (var cause in new[] { EffectCause.Race, EffectCause.Gender, (EffectCause)99, (EffectCause)(-1) })
            Rejected(owner.AddEffect(Effect(1, Add(CharacterStat.Strength, 1)), cause), CharacterStatsFailure.InvalidCause);
        Rejected(owner.RemoveEffect(new(12345)), CharacterStatsFailure.UnknownEffect);
        Added(owner.AddEffect(new(1, Modifiers(7)), EffectCause.Spell));
        Assert.AreEqual(1L, owner.Revision);
        Assert.AreEqual(15, Snap(owner).GetEffective(CharacterStat.Strength));
        CollectionAssert.AreNotEqual(before, Values(Snap(owner)));
    }

    [TestMethod]
    public void EffectCapacityIsExplicitAndExcludesInnateEffects()
    {
        var created = CharacterStatsOwner.Create(new(CharacterRace.Dwarf, CharacterGender.Female, SyntheticInnate(),
            new(2, 7, 1000)));
        var owner = created.Owner!;
        Assert.AreEqual(2, Snap(owner).AppliedEffects.Length);
        Added(owner.AddEffect(Effect(1, Add(CharacterStat.Beauty, 1)), EffectCause.Item));
        Added(owner.AddEffect(Effect(2, Add(CharacterStat.Beauty, 1)), EffectCause.Item));
        Rejected(owner.AddEffect(Effect(3, Add(CharacterStat.Beauty, 1)), EffectCause.Item),
            CharacterStatsFailure.EffectCapacityExceeded);
        Assert.AreEqual(10, Snap(owner).GetEffective(CharacterStat.Beauty));
    }

    [TestMethod]
    public void MalformedDefinitionsFailBeforeOwnership()
    {
        var valid = Definition();
        foreach (var definition in new CharacterStatsDefinition?[]
        {
            null, valid with { Innate = null! }, valid with { Innate = new(null!, InnateEffectTable.Empty.GenderEffects) },
            valid with { Innate = new(InnateEffectTable.Empty.RaceEffects, null!) },
            valid with { Limits = default }, valid with { Limits = new(0, 7, 1000) }, valid with { Limits = new(1, 0, 1000) },
            valid with { Limits = new(1, 7, 0) }, valid with { Limits = new(4097, 7, 1000) },
            valid with { Limits = new(1, 65, 1000) }, valid with { Limits = new(1, 7, 1_000_001) },
            valid with { Innate = new(new Dictionary<CharacterRace, StatEffectDefinition> { [(CharacterRace)42] = Effect(1, Add(CharacterStat.Strength, 1)) },
                InnateEffectTable.Empty.GenderEffects) },
            valid with { Innate = new(InnateEffectTable.Empty.RaceEffects,
                new Dictionary<CharacterGender, StatEffectDefinition> { [CharacterGender.Female] = new(1, default) }) },
        })
        {
            var result = CharacterStatsOwner.Create(definition);
            Assert.AreEqual(CharacterStatsFailure.InvalidDefinition, result.Failure);
            Assert.IsNull(result.Owner);
        }
        Assert.AreEqual(CharacterStatsFailure.InvalidRace, CharacterStatsOwner.Create(valid with { Race = (CharacterRace)11 }).Failure);
        Assert.AreEqual(CharacterStatsFailure.InvalidGender, CharacterStatsOwner.Create(valid with { Gender = (CharacterGender)2 }).Failure);
    }

    [TestMethod]
    public void InnateTableIsCopiedAtCreation()
    {
        var races = new Dictionary<CharacterRace, StatEffectDefinition>();
        var owner = Owner(innate: new(races, InnateEffectTable.Empty.GenderEffects));
        races[CharacterRace.Elf] = Effect(1, Add(CharacterStat.Dexterity, 5));
        Changed(owner.SetRace(CharacterRace.Elf));
        Assert.AreEqual(8, Snap(owner).GetEffective(CharacterStat.Dexterity));
        Assert.AreEqual(0, Snap(owner).AppliedEffects.Length);
    }

    [TestMethod]
    public void QueryRejectsOutOfRangeContextAndUnknownStatAccessThrows()
    {
        var owner = Owner();
        foreach (var adjustment in new[] { 101, -101, int.MinValue, int.MaxValue })
        {
            var result = owner.Query(new(adjustment));
            Assert.AreEqual(CharacterStatsFailure.InvalidContext, result.Failure);
            Assert.IsNull(result.Snapshot);
        }
        var snapshot = Snap(owner);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetEffective((CharacterStat)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetRange((CharacterStat)(-1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.RangeOf((CharacterStat)99));
    }
}
