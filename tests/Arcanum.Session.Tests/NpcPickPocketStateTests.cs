using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-246 (MIG13 S14k, GT204-GT209): an NPC's pick pocket of gold. The thief is "virgil" of InventoryStateTests' seed (no gold pile), the
// victims the half-elf hero (385 gold) and "merchant" (a pile of 5 coins). The thief's sheet is invented: pick pocket `points` with `training`.
[TestClass]
public sealed class NpcPickPocketStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);

    private static GameSessionOwner Owner()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionNpcPickPocket Steal(string victim, int points, SkillTraining training, string thief = "virgil")
    {
        int[] basic = new int[12];
        basic[(int)Skill.PickPocket] = points | (int)training << 6;
        var sheet = new SessionNpcAdvancement(thief, new(CharacterRace.Human, false, [8, 8, 8, 8, 20, 8, 8, 8], 0, 0, 1, 0,
            [.. basic], [.. new int[4]], [.. new int[16]], [.. new int[8]], 0, 0, 0, 0), 0);
        return new(SessionLifeTarget.Critter(thief, new LifeFacts(), new LifeState(0, 0, 0, LifeConditions.None, 0)), sheet, AdvancementModifiers.None, victim, GoldPile, Accept);
    }

    [TestMethod]
    public void AThiefTakesAHundredCoinsFromTheHeroOnASuccessAndNothingOnAFailure()
    {
        using var owner = Owner();
        var succeeded = 0; var failed = 0;
        for (var attempt = 0; attempt < 40 && (succeeded == 0 || failed == 0); attempt++)
        {
            var gold = owner.QueryGold();
            var theirs = owner.QueryNpcGold("virgil");
            var result = owner.PickPocketByNpc(owner.Version, Steal(SessionSpellCaster.Hero, 4, SkillTraining.None) with { Perception = 14 });
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
            if (result.Check!.Success)
            {
                succeeded++;
                Assert.AreEqual(100, result.Moved, "min(385, 100) coins (PP3)");
                Assert.AreEqual((gold - 100, theirs + 100), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
                Assert.IsFalse(result.Noticed);
                Assert.IsNull(result.PerceptionRoll);
            }
            else
            {
                failed++;
                Assert.AreEqual(0, result.Moved);
                Assert.AreEqual((gold, theirs), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "a failed check moves nothing");
                // An untrained thief's failure is noticed by the victim's perception die (PP4); a critical failure always is.
                Assert.IsTrue(result.Critical() || result.PerceptionRoll is not null);
                Assert.AreEqual(result.Critical() || result.PerceptionRoll <= 14, result.Noticed);
            }
        }
        Assert.IsTrue(succeeded > 0 && failed > 0, $"both outcomes came up in 40 draws: {succeeded}/{failed}");
    }

    [TestMethod]
    public void AThiefTakesWhatAnNpcHoldsWhenItIsLessThanAHundred()
    {
        using var owner = Owner();
        // A master against a victim with no perception: the first draw that succeeds moves the merchant's five coins (the pile is destroyed).
        for (var attempt = 0; attempt < 40 && owner.QueryNpcGold("merchant") > 0; attempt++)
        {
            var result = owner.PickPocketByNpc(owner.Version, Steal("merchant", 20, SkillTraining.Master));
            Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
            if (result.Check!.Success) Assert.AreEqual(5, result.Moved);
        }
        Assert.AreEqual((0, 5, 385), (owner.QueryNpcGold("merchant"), owner.QueryNpcGold("virgil"), owner.QueryGold()), "the merchant's five coins reached the thief; the hero's gold is untouched");
        Assert.IsTrue(owner.IsItemDestroyed("coins"));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickPocketByNpc(owner.Version, Steal("merchant", 20, SkillTraining.Master)).Failure, "no gold left");
    }

    [TestMethod]
    public void AnAttemptIsRefusedBeforeAnythingIsRolled()
    {
        using var owner = Owner();
        void Refused(SessionNpcPickPocket steal, GameSessionFailure failure, GameSessionVersion? version = null)
        {
            var result = owner.PickPocketByNpc(version ?? owner.Version, steal);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 385, 5), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("merchant")));
        }
        var good = Steal(SessionSpellCaster.Hero, 10, SkillTraining.Expert);
        Refused(good with { Thief = SessionLifeTarget.Hero }, GameSessionFailure.PickPocketUnavailable);
        Refused(good with { Loaded = good.Loaded with { Entity = "oid:other" } }, GameSessionFailure.PickPocketUnavailable);
        Refused(Steal("virgil", 10, SkillTraining.Expert), GameSessionFailure.InvalidPickPocketVictim, null);
        Refused(good with { PileKind = GoldPile with { Type = SessionItemType.Generic } }, GameSessionFailure.InvalidPickPocketVictim);
        Refused(good with { Perception = GameSessionOwner.MaxPickPocketStat + 1 }, GameSessionFailure.InvalidPickPocketVictim);
        Refused(Steal("ghost", 10, SkillTraining.Expert), GameSessionFailure.ItemNotAvailable);
        Refused(Steal("virgil", 10, SkillTraining.Expert, thief: "merchant") with { Victim = "virgil" }, GameSessionFailure.ItemNotAvailable);
        Refused(good, GameSessionFailure.StaleVersion, new(owner.Identity, 7));
    }
}

internal static class NpcPickPocketResultExtensions
{
    public static bool Critical(this SessionNpcPickPocketResult result) => result.Check?.Critical == true;
}
