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

    // Intelligence 20 (heal's key stat), heal points and training as asked; the kit's items 4 and 5 are healing items.
    private static GameSessionOwner Healer(int points, SkillTraining training, GameSessionLimits? limits = null, ulong seed = 0)
    {
        var stats = Defaults(); stats[4] = 20;
        int[] basic = new int[12];
        basic[(int)Skill.Heal] = points | (int)training << 6;
        var hero = HalfElf(stats) with
        {
            Kit = [.. Kit, new(new(SessionItemType.Generic, 15179, 6000, 6001)), new(new(SessionItemType.Generic, 15179, 6000, 6001))],
            Advancement = SessionHeroAdvancement.None with { BasicSkills = [.. basic] },
        };
        var created = GameSessionOwner.Create(Descriptor(limits) with { Hero = hero, RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    // The hero and one wolf on the exterior, for a turn-based round.
    private sealed class World : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) =>
            [new(SessionCombatant.Hero, new(0, 0)), new(SessionCombatant.Critter("oid:wolf"), new(105, 200))];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) =>
            new() { Speed = 7, Life = new LifeFacts() };
    }

    // Starts a round and ends every subturn before the hero's.
    private static World HeroSubturn(GameSessionOwner owner)
    {
        var world = new World();
        Assert.AreEqual(GameSessionFailure.None, owner.StartCombatRound(owner.Version, world).Failure);
        for (var i = 0; i < 4 && owner.QueryCombatRound()!.Actor != SessionCombatant.Hero; i++)
            owner.EndCombatTurn(owner.Version, owner.QueryCombatRound()!.Actor, world);
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryCombatRound()!.Actor);
        return world;
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

    [TestMethod]
    public void InTheHerosSubturnFourActionPointsArePaidFirst()
    {
        using var owner = Healer(5, SkillTraining.Master);
        Hurt(owner, 10);
        var world = HeroSubturn(owner);
        var points = owner.QueryCombatRound()!.ActionPoints;
        var before = owner.Version.Revision;
        var result = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50), world);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionActionPointUse.Paid), (result.Disposition, result.Charge!.Charge));
        Assert.AreEqual((points - TurnBasedRules.UseItemCost, before + 2), (owner.QueryCombatRound()!.ActionPoints, owner.Version.Revision),
            "the charge and the heal, one revision each");
        Assert.AreEqual(0, Damage(owner));
        // Without a world the caller charges nothing, even in the hero's subturn.
        Assert.IsNull(owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50)).Charge);
        Assert.AreEqual(points - TurnBasedRules.UseItemCost, owner.QueryCombatRound()!.ActionPoints);
    }

    [TestMethod]
    public void TheDurabilityBudgetCountsADestroyedItemAndIsCheckedBeforeTheCharge()
    {
        using var owner = Healer(5, SkillTraining.Master, new GameSessionLimits(maxDurabilityRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50)).Disposition);
        var other = Item(uses: 1, bonus: 50) with { Entity = "kit:5" };
        var revision = owner.Version.Revision;
        var draws = owner.QueryRandom().Draws;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, other).Failure, "a new destroyed entry");
        Assert.AreEqual((revision, draws), (owner.Version.Revision, owner.QueryRandom().Draws));
        var world = HeroSubturn(owner);
        var points = owner.QueryCombatRound()!.ActionPoints;
        revision = owner.Version.Revision;
        var charged = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, other with { Uses = 5 }, world);
        Assert.AreEqual((GameSessionFailure.BudgetExceeded, (SessionCombatResult?)null), (charged.Failure, charged.Charge));
        Assert.AreEqual((points, revision), (owner.QueryCombatRound()!.ActionPoints, owner.Version.Revision), "nothing charged");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50), world).Disposition,
            "the recorded item needs no new record");
    }

    [TestMethod]
    public void ADeadHeroCannotHeal()
    {
        using var owner = Healer(5, SkillTraining.Master);
        Hurt(owner, 1_000);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.HealUnavailable, owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(bonus: 50)).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void ACriticalFailureBelowExpertSpendsFiveUses()
    {
        // An untrained level-0 healer always fails; the first critical failure among the seeds spends five uses.
        for (ulong seed = 0; seed < 400; seed++)
        {
            using var owner = Healer(0, SkillTraining.None, seed: seed);
            var result = owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: 7));
            Assert.IsFalse(result.Check!.Success);
            if (!result.Check.Critical)
            {
                Assert.AreEqual(6, result.UsesLeft);
                continue;
            }
            Assert.AreEqual((5, 2), (result.Heal!.Value.UsesSpent, result.UsesLeft));
            Assert.AreEqual(new SessionItemUses(Bandage, 2), owner.QueryDurability().Uses.Single());
            return;
        }
        Assert.Fail("no critical failure in 400 seeds");
    }

    [TestMethod]
    public void TheUsesListIsCanonicalAndNeverNamesADestroyedItem()
    {
        using var owner = Healer(5, SkillTraining.Master);
        owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: 7, bonus: 50));
        owner.HealWithItem(owner.Version, SessionLifeTarget.Hero, Item(uses: 9, bonus: 50) with { Entity = "kit:5" });
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Listed = "\"uses\":[{\"entity\":\"kit:4\",\"uses\":6},{\"entity\":\"kit:5\",\"uses\":8}]";
        StringAssert.Contains(text, Listed);
        void Fails(string to, SnapshotDecodeFailure failure)
        {
            Assert.AreEqual(failure, GameSessionSnapshotCodec.TryDecode(text.Replace(Listed, to, StringComparison.Ordinal)).Failure, to);
        }
        Fails("\"uses\":[{\"entity\":\"kit:5\",\"uses\":8},{\"entity\":\"kit:4\",\"uses\":6}]", SnapshotDecodeFailure.NotCanonical);
        Fails("\"uses\":[{\"entity\":\"kit:4\",\"uses\":6},{\"entity\":\"kit:4\",\"uses\":8}]", SnapshotDecodeFailure.NotCanonical);
        Fails("\"uses\":[{\"entity\":\"kit:4\"}]", SnapshotDecodeFailure.MissingField);
        Fails("\"uses\":[{\"entity\":\"kit:4\",\"uses\":6,\"extra\":1}]", SnapshotDecodeFailure.UnknownField);
        Fails("\"uses\":[{\"entity\":\"kit:4\",\"uses\":1000001}]", SnapshotDecodeFailure.OutOfRange);
        // A uses record for a destroyed item is refused by Restore.
        var destroyed = text.Replace(Listed, "\"uses\":[{\"entity\":\"kit:4\",\"uses\":6}]", StringComparison.Ordinal)
            .Replace("\"destroyed\":[]", "\"destroyed\":[\"kit:4\"]", StringComparison.Ordinal);
        Assert.AreNotEqual(text, destroyed);
        Assert.AreEqual(GameSessionFailure.InvalidDurability, GameSessionOwner.Restore(Decode(destroyed), RestoreOptions()).Failure);
    }
}
