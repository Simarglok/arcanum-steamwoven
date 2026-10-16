using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>FR-CHAR-011: derived stats are read-only formulas over effective stats and context.</summary>
[TestClass]
public sealed class DerivedStatTests
{
    private static readonly CharacterStat[] Derived =
    [
        CharacterStat.CarryWeight, CharacterStat.DamageBonus, CharacterStat.AcAdjustment, CharacterStat.Speed,
        CharacterStat.HealRate, CharacterStat.PoisonRecovery, CharacterStat.ReactionModifier,
        CharacterStat.MaxFollowers, CharacterStat.MagickTechAptitude,
    ];

    [TestMethod]
    public void DefaultCharacterDerivedValues()
    {
        var snapshot = Snap(Owner());
        CollectionAssert.AreEqual(new[] { 4000, -1, -2, 8, 3, 8, -7, 2, 0 },
            Derived.Select(snapshot.GetEffective).ToArray());
        CollectionAssert.AreEqual(Derived.Select(snapshot.GetEffective).ToArray(),
            Derived.Select(snapshot.GetBase).ToArray());
    }

    [TestMethod]
    [DataRow(CharacterRace.Human, 1, 500, -4)]
    [DataRow(CharacterRace.Human, 9, 4500, 0)]
    [DataRow(CharacterRace.Human, 10, 5000, 0)]
    [DataRow(CharacterRace.Human, 11, 5500, 1)]
    [DataRow(CharacterRace.Human, 19, 9500, 9)]
    [DataRow(CharacterRace.Human, 20, 10000, 20)]
    [DataRow(CharacterRace.Ogre, 26, 10000, 32)]
    public void StrengthDrivesCarryWeightAndDamageBonus(CharacterRace race, int strength, int carry, int damage)
    {
        var owner = Owner(race);
        Accepted(owner.SetBase(CharacterStat.Strength, strength));
        var snapshot = Snap(owner);
        Assert.AreEqual(carry, snapshot.GetEffective(CharacterStat.CarryWeight));
        Assert.AreEqual(damage, snapshot.GetEffective(CharacterStat.DamageBonus));
        Assert.AreEqual(strength >= 20, snapshot.IsExtraordinary(CharacterStat.Strength));
    }

    [TestMethod]
    [DataRow(CharacterRace.Human, 1, -9, 1)]
    [DataRow(CharacterRace.Human, 19, 9, 19)]
    [DataRow(CharacterRace.Human, 20, 10, 25)]
    [DataRow(CharacterRace.Halfling, 22, 12, 27)]
    public void DexterityDrivesArmorAdjustmentAndSpeed(CharacterRace race, int dexterity, int armor, int speed)
    {
        var owner = Owner(race);
        Accepted(owner.SetBase(CharacterStat.Dexterity, dexterity));
        var snapshot = Snap(owner);
        Assert.AreEqual(armor, snapshot.GetEffective(CharacterStat.AcAdjustment));
        Assert.AreEqual(speed, snapshot.GetEffective(CharacterStat.Speed));
    }

    [TestMethod]
    [DataRow(CharacterRace.Human, 1, 0, 1)]
    [DataRow(CharacterRace.Human, 8, 3, 8)]
    [DataRow(CharacterRace.Human, 17, 6, 17)]
    [DataRow(CharacterRace.Human, 20, 6, 20)]
    [DataRow(CharacterRace.Orc, 22, 6, 20)]
    public void ConstitutionDrivesHealRateAndPoisonRecovery(CharacterRace race, int constitution, int heal, int poison)
    {
        var owner = Owner(race);
        Accepted(owner.SetBase(CharacterStat.Constitution, constitution));
        var snapshot = Snap(owner);
        Assert.AreEqual(heal, snapshot.GetEffective(CharacterStat.HealRate));
        Assert.AreEqual(poison, snapshot.GetEffective(CharacterStat.PoisonRecovery));
    }

    [TestMethod]
    [DataRow(CharacterRace.Human, 1, -65)]
    [DataRow(CharacterRace.Human, 8, -7)]
    [DataRow(CharacterRace.Human, 10, 0)]
    [DataRow(CharacterRace.Human, 19, 65)]
    [DataRow(CharacterRace.Human, 20, 100)]
    [DataRow(CharacterRace.Elf, 21, 110)]
    public void BeautyDrivesReactionModifier(CharacterRace race, int beauty, int reaction)
    {
        var owner = Owner(race);
        Accepted(owner.SetBase(CharacterStat.Beauty, beauty));
        Assert.AreEqual(reaction, Snap(owner).GetEffective(CharacterStat.ReactionModifier));
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(4, 1)]
    [DataRow(8, 2)]
    [DataRow(20, 5)]
    public void CharismaDrivesMaxFollowers(int charisma, int followers)
    {
        var owner = Owner();
        Accepted(owner.SetBase(CharacterStat.Charisma, charisma));
        Assert.AreEqual(followers, Snap(owner).GetEffective(CharacterStat.MaxFollowers));
    }

    [TestMethod]
    [DataRow(10, 0, 0, 50)]
    [DataRow(0, 10, 0, -55)]
    [DataRow(10, 10, 0, -5)]
    [DataRow(0, 1, 0, -5)]
    [DataRow(1, 0, 0, 5)]
    [DataRow(10, 0, 7, 57)]
    [DataRow(0, 0, -100, -100)]
    [DataRow(210, 0, 0, 100)]
    [DataRow(0, 210, 0, -100)]
    public void AptitudeUsesPointsAndSectorContext(int magick, int tech, int sector, int aptitude)
    {
        var owner = Owner();
        if (magick != 0) Accepted(owner.SetBase(CharacterStat.MagickPoints, magick));
        if (tech != 0) Accepted(owner.SetBase(CharacterStat.TechPoints, tech));
        var snapshot = Snap(owner, sector);
        Assert.AreEqual(aptitude, snapshot.GetEffective(CharacterStat.MagickTechAptitude));
        Assert.AreEqual(sector, snapshot.Context.SectorAptitudeAdjustment);
    }

    [TestMethod]
    public void PrimaryEffectsFlowIntoDerivedAndDerivedEffectsClamp()
    {
        var owner = Owner();
        Added(owner.AddEffect(Effect(1, Add(CharacterStat.Strength, 12)), EffectCause.Bless));
        Added(owner.AddEffect(Effect(2, Add(CharacterStat.MaxFollowers, 1), new StatModifier(CharacterStat.CarryWeight, StatModifierOperator.Percent, -90)),
            EffectCause.Item));
        var snapshot = Snap(owner);
        Assert.IsTrue(snapshot.IsExtraordinary(CharacterStat.Strength));
        Assert.AreEqual(20, snapshot.GetEffective(CharacterStat.DamageBonus));
        Assert.AreEqual(10000, snapshot.GetBase(CharacterStat.CarryWeight));
        Assert.AreEqual(1000, snapshot.GetEffective(CharacterStat.CarryWeight));
        Assert.AreEqual(2, snapshot.GetBase(CharacterStat.MaxFollowers));
        Assert.AreEqual(3, snapshot.GetEffective(CharacterStat.MaxFollowers));
        Assert.IsFalse(snapshot.IsExtraordinary(CharacterStat.CarryWeight), "only primary stats are extraordinary");

        Added(owner.AddEffect(Effect(3, new StatModifier(CharacterStat.CarryWeight, StatModifierOperator.Divide, 100)),
            EffectCause.Curse));
        Assert.AreEqual(300, Snap(owner).GetEffective(CharacterStat.CarryWeight), "clamped to the derived minimum");
    }

    [TestMethod]
    public void WritingDerivedStatIsRejectedAndChangesNothing()
    {
        var owner = Owner();
        var before = Values(Snap(owner));
        foreach (var stat in Derived)
        {
            Rejected(owner.SetBase(stat, 5), CharacterStatsFailure.DerivedStatReadOnly);
            Assert.AreEqual(Snap(owner).GetRange(stat), owner.RangeOf(stat));
        }
        Assert.AreEqual(0L, owner.Revision);
        CollectionAssert.AreEqual(before, Values(Snap(owner)));
    }
}
