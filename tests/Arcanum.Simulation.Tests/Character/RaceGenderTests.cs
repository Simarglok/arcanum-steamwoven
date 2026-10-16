using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>FR-CHAR-009 (innate effects) and FR-CHAR-010 (racial set-time limits).</summary>
[TestClass]
public sealed class RaceGenderTests
{
    [TestMethod]
    public void CreationAppliesBothInnateEffectsWithoutRevision()
    {
        var snapshot = Snap(Owner(CharacterRace.Dwarf, CharacterGender.Female, SyntheticInnate()));
        Assert.AreEqual(0L, snapshot.Revision);
        Assert.AreEqual(1, Count(snapshot, EffectCause.Race));
        Assert.AreEqual(1, Count(snapshot, EffectCause.Gender));
        Assert.AreEqual(7, snapshot.GetEffective(CharacterStat.Strength));
        Assert.AreEqual(11, snapshot.GetEffective(CharacterStat.Constitution));
        Assert.AreEqual(7, snapshot.GetEffective(CharacterStat.Charisma));
        foreach (var stat in Primary) Assert.AreEqual(8, snapshot.GetBase(stat));
    }

    [TestMethod]
    public void RepeatedRaceSwitchingReplacesInsteadOfAccumulating()
    {
        var owner = Owner(innate: SyntheticInnate());
        Changed(owner.SetRace(CharacterRace.Dwarf));
        var firstDwarf = Snap(owner);
        var firstId = firstDwarf.AppliedEffects.Single().Id;
        Changed(owner.SetRace(CharacterRace.Elf));
        var elf = Snap(owner);
        Assert.AreEqual(1, Count(elf, EffectCause.Race));
        Assert.AreEqual(902, elf.AppliedEffects.Single().Definition.EffectId);
        Assert.AreEqual(9, elf.GetEffective(CharacterStat.Dexterity));
        Assert.AreEqual(7, elf.GetEffective(CharacterStat.Constitution));
        Assert.AreEqual(8, elf.GetEffective(CharacterStat.Charisma));

        Changed(owner.SetRace(CharacterRace.Dwarf));
        var secondDwarf = Snap(owner);
        CollectionAssert.AreEqual(Values(firstDwarf), Values(secondDwarf));
        Assert.AreEqual(1, Count(secondDwarf, EffectCause.Race));
        Assert.AreNotEqual(firstId, secondDwarf.AppliedEffects.Single().Id, "a fresh application, not a stack");

        NoChange(owner.SetRace(CharacterRace.Dwarf));
        Changed(owner.SetRace(CharacterRace.Human));
        Assert.AreEqual(0, Snap(owner).AppliedEffects.Length, "a race without a table entry has no effect");
        Assert.AreEqual(4L, owner.Revision);
    }

    [TestMethod]
    public void RepeatedGenderSwitchingRemovesPreviousEffectFirst()
    {
        var owner = Owner(CharacterRace.Dwarf, innate: SyntheticInnate());
        for (var round = 0; round < 3; round++)
        {
            Changed(owner.SetGender(CharacterGender.Female));
            var female = Snap(owner);
            Assert.AreEqual(1, Count(female, EffectCause.Gender));
            Assert.AreEqual(7, female.GetEffective(CharacterStat.Strength));
            Assert.AreEqual(11, female.GetEffective(CharacterStat.Constitution));
            Changed(owner.SetGender(CharacterGender.Male));
            var male = Snap(owner);
            Assert.AreEqual(0, Count(male, EffectCause.Gender));
            Assert.AreEqual(1, Count(male, EffectCause.Race));
            Assert.AreEqual(8, male.GetEffective(CharacterStat.Strength));
            Assert.AreEqual(10, male.GetEffective(CharacterStat.Constitution));
        }
        NoChange(owner.SetGender(CharacterGender.Male));
        Rejected(owner.SetGender((CharacterGender)2), CharacterStatsFailure.InvalidGender);
        Rejected(owner.SetRace((CharacterRace)(-1)), CharacterStatsFailure.InvalidRace);
        Assert.AreEqual(6L, owner.Revision);
    }

    [TestMethod]
    public void InnateEffectsCannotBeRemovedDirectly()
    {
        var owner = Owner(CharacterRace.Elf, CharacterGender.Female, SyntheticInnate());
        foreach (var effect in Snap(owner).AppliedEffects)
            Rejected(owner.RemoveEffect(effect.Id), CharacterStatsFailure.InvalidCause);
        Assert.AreEqual(2, Snap(owner).AppliedEffects.Length);
    }

    // Maxima in stat order ST DX CN BE IN PE WP CH; minimum is 1 for every race.
    [TestMethod]
    [DataRow(CharacterRace.Human, new[] { 20, 20, 20, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.Dwarf, new[] { 21, 20, 21, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.Elf, new[] { 20, 21, 20, 21, 20, 20, 21, 20 })]
    [DataRow(CharacterRace.HalfElf, new[] { 20, 21, 20, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.Gnome, new[] { 20, 20, 20, 20, 20, 20, 22, 20 })]
    [DataRow(CharacterRace.Halfling, new[] { 20, 22, 20, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.HalfOrc, new[] { 21, 20, 21, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.HalfOgre, new[] { 24, 20, 20, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.DarkElf, new[] { 20, 21, 20, 21, 20, 20, 21, 20 })]
    [DataRow(CharacterRace.Ogre, new[] { 26, 20, 20, 20, 20, 20, 20, 20 })]
    [DataRow(CharacterRace.Orc, new[] { 22, 20, 22, 20, 20, 20, 20, 20 })]
    public void RacialLimitsBelowAtAndAboveBoundary(CharacterRace race, int[] maxima)
    {
        var owner = Owner(race);
        for (var index = 0; index < Primary.Length; index++)
        {
            var stat = Primary[index];
            var max = maxima[index];
            Assert.AreEqual(new StatRange(1, max), owner.RangeOf(stat));
            Changed(owner.SetBase(stat, max - 1));
            Changed(owner.SetBase(stat, max));
            var revision = owner.Revision;
            Rejected(owner.SetBase(stat, max + 1), CharacterStatsFailure.OutOfRange);
            Rejected(owner.SetBase(stat, 0), CharacterStatsFailure.OutOfRange);
            Rejected(owner.SetBase(stat, int.MinValue), CharacterStatsFailure.OutOfRange);
            Assert.AreEqual(revision, owner.Revision);
            var snapshot = Snap(owner);
            Assert.AreEqual(max, snapshot.GetBase(stat), "rejected write leaves the stored value");
            Assert.AreEqual(max, snapshot.GetEffective(stat));
            Assert.AreEqual(new StatRange(1, max), snapshot.GetRange(stat));
            Changed(owner.SetBase(stat, 1));
        }
    }

    [TestMethod]
    public void RaceChangeKeepsStoredValueButClampsEffective()
    {
        var owner = Owner(CharacterRace.Ogre);
        Changed(owner.SetBase(CharacterStat.Strength, 26));
        Changed(owner.SetRace(CharacterRace.Human));
        var snapshot = Snap(owner);
        Assert.AreEqual(26, snapshot.GetBase(CharacterStat.Strength));
        Assert.AreEqual(20, snapshot.GetEffective(CharacterStat.Strength));
        Assert.AreEqual(new StatRange(1, 20), owner.RangeOf(CharacterStat.Strength));
        Rejected(owner.SetBase(CharacterStat.Strength, 26), CharacterStatsFailure.OutOfRange);
        Changed(owner.SetBase(CharacterStat.Strength, 20));
    }
}
