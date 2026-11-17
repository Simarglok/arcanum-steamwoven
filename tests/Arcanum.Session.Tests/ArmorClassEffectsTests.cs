using Arcanum.Data.Characters;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1931 (ledger VC36, A6): the armour class function 0x43D880 puts the stat-10 (AC adjustment) effects over base + worn and then adds the effective
// stat 10, which carries those effects already, so an effect counts twice in the Original setting; the Steamwoven setting counts it once. The effects and
// numbers are invented; the source's stat index 10 is the AC adjustment (effect attribute 10).
[TestClass]
public sealed class ArmorClassEffectsTests
{
    private const int AcAdjustmentAttribute = 10;
    private const string Wolf = "oid:wolf";

    private static EffectChange AcChange(int value, EffectChangeOperator op = EffectChangeOperator.Add) => new(EffectAttributeKind.Stat, AcAdjustmentAttribute, op, value);

    private static readonly SessionEffectDefinition PlusTen = new(160, [AcChange(10)]);
    private static readonly SessionEffectDefinition PlusHalf = new(161, [AcChange(50, EffectChangeOperator.Percent)]);

    private static EquipmentItem Armour(int ac) => new()
    {
        Kind = EquipmentItemKind.Armor, HitPoints = 20, MaxHitPoints = 20,
        Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Small | ArmorSizes.Medium | ArmorSizes.Large, AcAdjustment = ac },
    };

    private static GameSessionOwner Hero(RuleMode mode, int worn = 0)
    {
        var owner = CreateWith(HalfElf());
        owner.SelectRules(mode);
        if (worn != 0)
            Assert.AreEqual(GameSessionFailure.None,
                owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, Armour(worn)), (_, _) => true, _ => SessionInsertVerdict.Accepted).Failure);
        return owner;
    }

    private static int Adjustment(GameSessionOwner owner) =>
        owner.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.AcAdjustment);

    [TestMethod]
    public void WithoutAnEffectBothSettingsDefendWithTheSameArmourClass()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Hero(mode, worn: 12);
            Assert.AreEqual(12 + Adjustment(owner), owner.QueryHeroAttack()!.Combatant.ArmorClass, mode.ToString());
        }
    }

    [TestMethod]
    public void AnAdditiveAcEffectOfTheHeroCountsOnceInSteamwovenAndTwiceInOriginal()
    {
        foreach (var (mode, effectsOnBase) in new[] { (RuleMode.Steamwoven, 0), (RuleMode.Original, 10) })
        {
            using var owner = Hero(mode, worn: 12);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, PlusTen, EffectCause.Spell).Disposition);
            // The effective adjustment carries the effect (DEX 9 − 10 + 10 = 9). Steamwoven: 12 + 9. Original: (12 + 10) + 9.
            Assert.AreEqual(9, Adjustment(owner), "the effect is in the effective adjustment");
            Assert.AreEqual(12 + effectsOnBase + 9, owner.QueryHeroAttack()!.Combatant.ArmorClass, mode.ToString());
        }
    }

    [TestMethod]
    public void APercentAcEffectOfTheHeroScalesBasePlusWornTheSecondTime()
    {
        using var owner = Hero(RuleMode.Original, worn: 20);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddHeroEffect(owner.Version, PlusHalf, EffectCause.Spell).Disposition);
        // The percent scales the adjustment too (−1 × 150 / 100 truncates toward zero to −1); base + worn 20 becomes 30 the second time.
        Assert.AreEqual(30 + Adjustment(owner), owner.QueryHeroAttack()!.Combatant.ArmorClass, "30 from base + worn under the percent, then the effective adjustment");
    }

    [TestMethod]
    public void TheHeroDollShowsTheArmourClassTheOriginalDefendsWith()
    {
        using var steamwoven = Hero(RuleMode.Steamwoven, worn: 12);
        using var original = Hero(RuleMode.Original, worn: 12);
        Assert.AreEqual(steamwoven.QueryHeroDollRatings(_ => true)!.Armour, original.QueryHeroDollRatings(_ => true)!.Armour, "no effect, no difference");
        Assert.AreEqual(GameSessionDisposition.Changed, steamwoven.AddHeroEffect(steamwoven.Version, PlusTen, EffectCause.Spell).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, original.AddHeroEffect(original.Version, PlusTen, EffectCause.Spell).Disposition);
        Assert.IsTrue(original.QueryHeroDollRatings(_ => true)!.Armour > steamwoven.QueryHeroDollRatings(_ => true)!.Armour);
    }

    [TestMethod]
    public void AnNpcUnderAnAcEffectDefendsTwiceCountedInOriginalOnlyAndItsDollMatches()
    {
        using var owner = CreateWith(HalfElf());
        var status = new LifeStatus(10, 10, 10, 10, false, false, true);
        var build = SessionCritterCombat.From(Wolf, AttackStateTests.Npc(stats: AttackStateTests.Stats(dexterity: 16), ac: 4, critterFlags: 0));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        var wolf = build.Combat!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddNpcEffect(owner.Version, wolf.Target, PlusTen, EffectCause.Spell).Disposition);
        var effected = owner.WithNpcEffects(wolf);
        // DEX 16: adjustment 6, plus the effect 16. Steamwoven 4 + 16 = 20; Original (4 + 10) + 16 = 30.
        Assert.AreEqual(20, effected.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven).ArmorClass);
        Assert.AreEqual(30, effected.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Original).ArmorClass);
        // The shared build, without the list, is the plain 4 + 6 in both.
        Assert.AreEqual(10, wolf.Combatant(status, LifeState.Unharmed, CombatAnomalyProfile.Original).ArmorClass);
        Assert.IsTrue(effected.DollValues(status, LifeState.Unharmed, CombatAnomalyProfile.Original, 0, _ => true).Armour >
            effected.DollValues(status, LifeState.Unharmed, CombatAnomalyProfile.Steamwoven, 0, _ => true).Armour);
    }
}
