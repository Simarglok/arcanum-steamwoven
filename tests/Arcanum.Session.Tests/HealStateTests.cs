using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero heals with a healing item (ARC-SKILLS-005, ledger HL1–HL5). The item's values are invented.
[TestClass]
public sealed class HealStateTests
{
    private const string Bandage = "kit:4";
    private static readonly ItemCondition Sound = new(10, 0, 0, false);

    private static SessionHealingItem Item(int uses = 5, int bonus = 0, uint flags = SessionHealingItem.HealingFlag, int[]? scripts = null) =>
        new(Bandage, flags, bonus, uses, Sound, scripts ?? []);

    // Intelligence 20 (heal's key stat), heal points and training as asked; the kit's fourth item is the healing item.
    private static GameSessionOwner Healer(int points, SkillTraining training)
    {
        var stats = Defaults(); stats[4] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.Heal] = points | (int)training << 6;
        var hero = HalfElf(stats) with
        {
            Kit = [.. Kit, new(new(SessionItemType.Generic, 15179, 6000, 6001))],
            Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] },
        };
        return CreateWith(hero);
    }

    private static void Hurt(GameSessionOwner owner, int hitPoints) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = hitPoints }).Disposition);

    private static int Damage(GameSessionOwner owner) => owner.QueryHeroLife()!.Record.State.HitPointDamage;

    [TestMethod]
    public void AMasterHealsTheMaximumAndSpendsOneUseInOneRevision()
    {
        using var owner = Healer(5, SkillTraining.Master);
        Hurt(owner, 25);
        var before = owner.Version.Revision;
        var draws = owner.QueryRandom().Draws;
        var result = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.IsTrue(result.Check!.Success);
        Assert.AreEqual(new HealOutcome(30, true, 1, result.Check.Critical), result.Heal);
        Assert.AreEqual(0, Damage(owner), "30 heals the 25");
        Assert.AreEqual((4, false, before + 1), (result.UsesLeft, result.ItemDestroyed, owner.Version.Revision));
        Assert.AreEqual(draws + 2, owner.QueryRandom().Draws, "the check and its critical; the full amount draws nothing");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50)).Disposition);
        Assert.AreEqual(new SessionItemUses(Bandage, 3), owner.QueryDurability().Uses.Single(), "the session's count, not the host's baseline");
    }

    [TestMethod]
    public void AnUntrainedZeroLevelHealerAlwaysFailsAndTheLastUseDestroysTheItem()
    {
        using var owner = Healer(0, SkillTraining.None);
        Hurt(owner, 5);
        var result = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: 1));
        Assert.IsFalse(result.Check!.Success);
        Assert.AreEqual(result.Check.Critical ? 5 : 1, result.Heal!.Value.UsesSpent);
        Assert.IsNull(result.Life);
        Assert.AreEqual((true, (int?)null), (result.ItemDestroyed, result.UsesLeft));
        Assert.AreEqual(5, Damage(owner));
        Assert.IsTrue(owner.IsItemDestroyed(Bandage));
        Assert.IsFalse(owner.QueryHeroKit().Any(item => item.Entity == Bandage));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item()).Failure);
    }

    [TestMethod]
    public void AnArmedHealFateMakesACriticalSuccessAndIsUsed()
    {
        using var owner = Healer(0, SkillTraining.None);
        Hurt(owner, 5);
        owner.GrantHeroFatePoint(owner.Version);
        owner.ArmHeroFate(owner.Version, FateKind.Heal);
        Assert.AreEqual(FateKind.Heal, owner.HeroSkillFate(Skill.Heal));
        var result = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item());
        Assert.IsTrue(result.FateUsed && result.Check!.Success && result.Check.Critical);
        Assert.AreEqual(new HealOutcome(1, true, 1, true), result.Heal, "level 0 heals at least 1, the full amount");
        Assert.AreEqual(4, Damage(owner));
        Assert.IsFalse(owner.HeroFateArmed(FateKind.Heal));
    }

    [TestMethod]
    public void AnNpcTargetIsHealedThroughItsLifeRecord()
    {
        using var owner = Healer(5, SkillTraining.Master);
        var npc = SessionLifeTarget.Critter("oid:rat", new LifeFacts(), new LifeState(6, 0, 0, LifeConditions.CrippledLegsBoth, 1));
        var result = owner.HealWithItem(owner.Version, npc, Item(bonus: 50));
        Assert.AreEqual(new LifeState(0, 0, 0, LifeConditions.None, 0), owner.QueryLifeRecord("oid:rat")!.Value.State);
        Assert.IsTrue(result.Heal!.Value.Cure);
    }

    [TestMethod]
    public void RefusalsComeBeforeAnyRollOrCharge()
    {
        using var owner = Healer(5, SkillTraining.Master);
        var draws = owner.QueryRandom().Draws;
        void Refused(SessionHealResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
        }
        Refused(owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(flags: 0x02)), GameSessionFailure.InvalidHealingItem);
        Refused(owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: -1)), GameSessionFailure.InvalidHealingItem);
        Refused(owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(scripts: [SessionHealingItem.UseScript])), GameSessionFailure.ItemScriptUnsupported);
        Refused(owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item() with { Entity = "kit:9" }), GameSessionFailure.ItemNotAvailable);
        Refused(owner.HealWithItem(new(owner.Identity, 5), SessionLifeTarget.Hero, Item()), GameSessionFailure.StaleVersion);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(GameSessionFailure.HealUnavailable, heroless.HealWithItem(heroless.Version, SessionLifeTarget.Hero, Item()).Failure);
    }

    [TestMethod]
    public void UsesRoundTripThroughTheSnapshotAndABadCountIsRefused()
    {
        using var owner = Healer(5, SkillTraining.Master);
        owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: 7, bonus: 50));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"uses\":[{\"entity\":\"kit:4\",\"uses\":6}]");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionItemUses(Bandage, 6), restored.QueryDurability().Uses.Single());
        var zero = GameSessionSnapshotCodec.TryDecode(text.Replace("\"uses\":6}", "\"uses\":0}"));
        Assert.AreEqual((SnapshotDecodeFailure.OutOfRange, "$.durability.uses[0].uses"), (zero.Failure, zero.Path));
    }
}
