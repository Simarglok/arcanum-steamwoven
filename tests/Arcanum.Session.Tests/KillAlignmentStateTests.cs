using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-2873: the kill step's alignment step (0x45DA20 calls 0x45DC90 with flag 1 right after the experience award): the killer's PC
// leader moves its stored alignment toward the victim's, without the theft's second halving. Numbers: victim a, leader b, change = -trunc(a / 20)
// for a >= 0 (bound -100 - a), trunc(a / 20) for a < 0 and b <= a (bound a - 100), none for a < 0 and b > a.
[TestClass]
public sealed class KillAlignmentStateTests
{
    private const string Rat = "oid:rat", Other = "oid:other";

    private sealed class OpenDropMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    private static CombatFields Named(int alignment)
    {
        var stats = Stats();
        stats[19] = alignment;
        return Npc(stats);
    }

    private static SessionCritterCombat Combat(int alignment, string entity = Rat)
    {
        var build = SessionCritterCombat.From(entity, Named(alignment));
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    private static GameSessionOwner Owner(bool experience = true, int heroAlignment = 0)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Alignment = heroAlignment }, Experience = experience ? ExperienceStateTests.Rules() : null });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static int Alignment(GameSessionOwner owner) => owner.QueryHero()!.Alignment;

    private static void KillByHero(GameSessionOwner owner, SessionCritterCombat rat)
    {
        for (var attempt = 0; attempt < 400 && !owner.QueryNpcKilled(rat.Entity); attempt++)
        {
            var result = owner.AttackCritter(owner.Version, new(rat));
            Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
        }
        Assert.IsTrue(owner.QueryNpcKilled(rat.Entity));
    }

    [TestMethod]
    public void AHeroKillMovesTheHerosAlignmentTowardsTheVictimsWithoutTheSecondHalving()
    {
        foreach (var (victim, hero, expected) in new[] { (200, 0, -10), (119, 0, -5), (39, 0, -1), (19, 0, 0), (-200, -250, -260), (-200, 0, 0), (-39, -500, -500), (900, -950, -995) })
            foreach (var experience in new[] { true, false })
            {
                using var owner = Owner(experience, hero);
                KillByHero(owner, Combat(victim));
                Assert.AreEqual(expected, Alignment(owner), $"victim {victim}, hero {hero}, experience {experience}");
            }
    }

    [TestMethod]
    public void TheAlignmentChangeStopsAtTheBoundAndANonNegativeChangeIsNotApplied()
    {
        // a = 200: bound -300. A hero at -298 goes to -300 (the full change -10 would pass it); one at -300 or below stays.
        foreach (var (hero, expected) in new[] { (-298, -300), (-300, -300), (-400, -400) })
        {
            using var owner = Owner(true, hero);
            KillByHero(owner, Combat(200));
            Assert.AreEqual(expected, Alignment(owner), $"hero {hero}");
        }
    }

    [TestMethod]
    public void TheLeaderKillingLoopRunsAfterTheAwardTheAlignmentStepAndTheKillLog()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:mid").Disposition);
        var asked = new List<(int Alignment, int Experience, int Kills)>();
        owner.LeaderKillingScript = _ => asked.Add((Alignment(owner), owner.QueryHeroExperience()!.Experience, owner.QueryHeroKillLog()!.Total));
        var experience = owner.QueryHeroExperience()!.Experience;
        KillByHero(owner, Combat(200));
        Assert.AreEqual(-10, Alignment(owner));
        var (alignment, experienceThen, kills) = asked.Single();
        Assert.AreEqual(-10, alignment, "the loop runs after the alignment step");
        Assert.AreEqual(owner.QueryHeroExperience()!.Experience, experienceThen, "and after the award");
        Assert.IsTrue(experienceThen > experience);
        Assert.AreEqual(1, kills, "and after the kill log");
    }

    [TestMethod]
    public void AVictimTheHeroSummonedChangesNothing()
    {
        foreach (var summoner in new[] { SessionEventKinds.PlayerEntity, "oid:other" })
        {
            using var owner = Owner();
            var made = owner.CreateSummonedCritter(owner.Version, 1234, Exterior, new(100, 201), summoner, null).Object!;
            KillByHero(owner, Combat(200, made.Entity));
            Assert.AreEqual(summoner == SessionEventKinds.PlayerEntity ? 0 : -10, Alignment(owner), $"summoned by {summoner}");
        }
        // A critter the hero's script made carries no summoned flag: the step runs.
        using var scripted = Owner();
        var made2 = scripted.CreateCritterNear(scripted.Version, 1234, Exterior, new(100, 201), new OpenDropMap()).Object!;
        KillByHero(scripted, Combat(200, made2.Entity));
        Assert.AreEqual(-10, Alignment(scripted));
    }

    [TestMethod]
    public void ASpellOrTrapKillByTheHeroOrAFollowerMovesTheAlignmentWithAndWithoutExperienceData()
    {
        foreach (var experience in new[] { true, false })
            foreach (var (source, stopped, expected) in new[] { ("hero", false, -10), ("hero", true, 0), ("led", false, -10), ("stranger", false, 0) })
            {
                using var owner = Owner(experience);
                var victim = Combat(200, Other);
                if (source == "led") Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Rat, PartyStateTests.Facts(maxFollowers: 5), false).Disposition);
                var died = new LifeOutcome(default, default, LifeEvents.Died, 100, DeathAnimation.Plain);
                var combatant = source == "hero" ? SessionCombatant.Hero : SessionCombatant.Critter(source == "led" ? Rat : "oid:stranger");
                var result = owner.RecordDamageExperience(owner.Version, victim, died, 10, combatant, deathStopped: stopped);
                Assert.AreEqual(GameSessionFailure.None, result.Failure);
                Assert.AreEqual(expected, Alignment(owner), $"{source}, stopped {stopped}, experience {experience}");
            }
    }

    [TestMethod]
    public void AFollowersKillMovesTheHeroAndAStrangersDoesNot()
    {
        foreach (var led in new[] { false, true })
        {
            using var owner = Owner();
            if (led) Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, Rat, PartyStateTests.Facts(maxFollowers: 5), false).Disposition);
            var rat = Combat(0);
            var victim = Combat(200, Other);
            for (var attempt = 0; attempt < 600 && !owner.QueryNpcKilled(Other); attempt++)
            {
                var result = owner.AttackByNpc(owner.Version, new(rat) { Target = victim });
                Assert.AreEqual((GameSessionFailure.None, GameSessionFailure.None), (result.Failure, result.StepFailure));
            }
            Assert.IsTrue(owner.QueryNpcKilled(Other));
            Assert.AreEqual(led ? -10 : 0, Alignment(owner), $"led {led}");
        }
    }
}
