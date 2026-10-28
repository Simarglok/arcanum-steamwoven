using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's turn-based round (ARC-COMBAT-003). The hero is the half-elf fixture: speed 9, perception 8 (scan radius 10),
// 28 fatigue; it stands at (100, 200) on map 79. NPC ids and facts are invented: 30 hit points and 30 fatigue each.
[TestClass]
public sealed class CombatRoundTests
{
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant Wolf = SessionCombatant.Critter("oid:wolf");
    private static readonly SessionCombatant Bandit = SessionCombatant.Critter("oid:bandit");
    private static readonly SessionCombatant Dog = SessionCombatant.Critter("oid:dog");
    private static readonly SessionCombatant Virgil = SessionCombatant.Critter("oid:virgil");

    private static SessionCombatantFacts Npc(int speed, LifeState loaded = default, bool hidden = false, bool sleeping = false) =>
        new() { Speed = speed, Life = new LifeFacts(), Loaded = loaded, Hidden = hidden, Sleeping = sleeping };

    private sealed class World : ISessionCombatWorld
    {
        internal List<SessionCombatCandidate> Scan { get; set; } = [];
        internal Dictionary<string, SessionCombatantFacts?> Facts { get; } = new(StringComparer.Ordinal);
        internal Action? DuringFacts { get; set; }
        internal int Scans { get; private set; }

        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map)
        {
            Assert.AreEqual(Exterior, map);
            Scans++;
            return Scan;
        }

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity)
        {
            Assert.AreEqual(Exterior, map);
            DuringFacts?.Invoke();
            return Facts.TryGetValue(entity, out var facts) ? facts : null;
        }

        internal World With(SessionCombatant combatant, int x, int y, SessionCombatantFacts? facts)
        {
            Scan.Add(new(combatant, new(x, y)));
            if (!combatant.IsHero) Facts[combatant.Entity!] = facts;
            return this;
        }
    }

    // In scan order: hero, wolf (7), bandit (8); every NPC within the square.
    private static World Fight() => new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7)).With(Bandit, 95, 195, Npc(8));

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionCombatResult Ok(SessionCombatResult result, GameSessionOwner owner)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(owner.Version, result.Version);
        return result;
    }

    private static void Refused(SessionCombatResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(0, result.Events.Count);
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), owner.Version);
    }

    private static SessionCombatEvent Began(int turn) => new(SessionCombatEventKind.RoundBegan, null, turn);
    private static SessionCombatEvent Started(SessionCombatant combatant, int points) => new(SessionCombatEventKind.SubturnStarted, combatant, points);
    private static SessionCombatEvent Ended(SessionCombatant combatant) => new(SessionCombatEventKind.SubturnEnded, combatant, 0);

    private static void Round(GameSessionOwner owner, int turn, SessionCombatant actor, int points, params SessionCombatant[] participants)
    {
        var round = owner.QueryCombatRound()!;
        Assert.AreEqual((turn, actor, points), (round.Turn, round.Actor, round.ActionPoints));
        CollectionAssert.AreEqual(participants, round.Participants.ToArray());
    }

    private static long Now(GameSessionOwner owner) => owner.QueryClockSnapshot().GameTimestampMs;

    [TestMethod]
    public void ARoundTakesTheSquareAroundTheHeroWithItsPartyLast()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:virgil").Disposition);
        var world = new World().With(Virgil, 101, 201, Npc(6)).With(Hero, 0, 0, null).With(Wolf, 110, 190, Npc(7))
            .With(Bandit, 111, 200, Npc(8)).With(Dog, 90, 210, Npc(5));
        var start = Ok(owner.StartCombatRound(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { Began(1), Started(Wolf, 7) }, start.Events.ToArray());
        Assert.AreEqual(2, owner.Version.Revision, "one revision for the round");
        Round(owner, 1, Wolf, 7, Wolf, Dog, Virgil, Hero);
        Assert.AreEqual(1, world.Scans);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.StartCombatRound(owner.Version, world).Disposition);
    }

    [TestMethod]
    public void StartingNeedsAHeroAndAWellFormedScan()
    {
        using (var plain = Create())
            Refused(plain.StartCombatRound(plain.Version, Fight()), plain, GameSessionFailure.CombatUnavailable, 0);
        using var owner = Owner();
        Refused(owner.StartCombatRound(owner.Version, new World().With(Wolf, 105, 200, Npc(7))), owner, GameSessionFailure.InvalidCombatFacts, 0);
        Refused(owner.StartCombatRound(owner.Version, Fight().With(Wolf, 105, 200, Npc(7))), owner, GameSessionFailure.InvalidCombatFacts, 0);
        Refused(owner.StartCombatRound(owner.Version, Fight().With(Hero, 1, 1, null)), owner, GameSessionFailure.InvalidCombatFacts, 0);
        var badFacts = Fight();
        badFacts.Facts["oid:wolf"] = Npc(TurnBasedRules.MaxStat + 1);
        Refused(owner.StartCombatRound(owner.Version, badFacts), owner, GameSessionFailure.InvalidCombatFacts, 0);
        Refused(owner.StartCombatRound(new GameSessionVersion(owner.Identity, 5), Fight()), owner, GameSessionFailure.StaleVersion, 0);
        Assert.IsNull(owner.QueryCombatRound());
    }

    [TestMethod]
    public void InactiveGoneAndHiddenAwakeParticipantsLoseTheirSubturns()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null)
            .With(Wolf, 105, 200, Npc(7, new LifeState(HitPointDamage: 30, 0, 0, LifeConditions.None, 0)))
            .With(Bandit, 104, 200, Npc(8, hidden: true))
            .With(SessionCombatant.Critter("oid:ghost"), 103, 200, null)
            .With(Dog, 102, 200, Npc(5, hidden: true, sleeping: true));
        CollectionAssert.AreEqual(new[] { Began(1), Started(Dog, 5) }, Ok(owner.StartCombatRound(owner.Version, world), owner).Events.ToArray());
    }

    [TestMethod]
    public void NobodyAbleToActRefusesTheStart()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 28 }).Disposition);
        var revision = owner.Version.Revision;
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7, new LifeState(0, 30, 0, LifeConditions.None, 0)));
        Refused(owner.StartCombatRound(owner.Version, world), owner, GameSessionFailure.CombatUnavailable, revision);
        Assert.IsNull(owner.QueryCombatRound());
    }

    [TestMethod]
    public void CostsArePaidAndAnNpcThatCannotPayLosesItsSubturn()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var paid = Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 5, world), owner);
        Assert.AreEqual((SessionActionPointUse.Paid, 0), (paid.Charge, paid.Events.Count));
        Round(owner, 1, Wolf, 2, Wolf, Bandit, Hero);
        var free = owner.ConsumeActionPoints(owner.Version, Bandit, 5, world);
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionActionPointUse.Free), (free.Disposition, free.Charge), "not its subturn");
        var zero = owner.ConsumeActionPoints(owner.Version, Wolf, 0, world);
        Assert.AreEqual((GameSessionDisposition.NoChange, SessionActionPointUse.Paid), (zero.Disposition, zero.Charge));
        var refused = Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 3, world), owner);
        Assert.AreEqual(SessionActionPointUse.Refused, refused.Charge);
        CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, refused.Events.ToArray());
        Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
        Refused(owner.ConsumeActionPoints(owner.Version, Bandit, -1, world), owner, GameSessionFailure.InvalidCombatCost, owner.Version.Revision);
        Refused(owner.ConsumeActionPoints(owner.Version, SessionCombatant.Critter("bad id"), 1, world), owner,
            GameSessionFailure.InvalidCombatant, owner.Version.Revision);
    }

    [TestMethod]
    public void TheLastSubturnEndsTheRoundOneGameSecondLater()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var start = Now(owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Round(owner, 1, Hero, 9, Wolf, Bandit, Hero);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndCombatTurn(owner.Version, Wolf, world).Disposition, "not its subturn");

        // The next round rescans: the bandit is gone from the scan but stays, a newcomer goes first.
        world.Scan.RemoveAll(candidate => candidate.Combatant == Bandit);
        world.With(Dog, 99, 199, Npc(5));
        var wrapped = Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Dog, 5) }, wrapped.Events.ToArray());
        Assert.AreEqual(start + TurnBasedRules.RoundGameTimeMs, Now(owner));
        Round(owner, 2, Dog, 5, Dog, Wolf, Bandit, Hero);
    }

    [TestMethod]
    public void NewcomersOfALaterRoundGoInFrontLastFoundFirst()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7));
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        world.With(Bandit, 101, 200, Npc(8)).With(Dog, 102, 200, Npc(5));
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        Round(owner, 2, Dog, 5, Dog, Bandit, Wolf, Hero);
    }

    [TestMethod]
    public void A9TheOriginalStartsTheFirstSubturnTwiceAfterSkippingToTheEnd()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Owner(mode);
            var world = Fight();
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:virgil").Disposition);
            world.With(Virgil, 101, 200, Npc(6));
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
            Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
            world.Facts["oid:virgil"] = null;
            var skipped = Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
            var expected = mode == RuleMode.Original
                ? new[] { Ended(Hero), Began(2), Started(Wolf, 7), Started(Wolf, 7) }
                : new[] { Ended(Hero), Began(2), Started(Wolf, 7) };
            CollectionAssert.AreEqual(expected, skipped.Events.ToArray(), mode.ToString());
            Round(owner, 2, Wolf, 7, Wolf, Bandit, Hero, Virgil);

            // Reaching the end without a skip starts it once in both modes.
            world.Facts["oid:virgil"] = Npc(6);
            Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
            Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
            Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
            CollectionAssert.AreEqual(new[] { Ended(Virgil), Began(3), Started(Wolf, 7) },
                Ok(owner.EndCombatTurn(owner.Version, Virgil, world), owner).Events.ToArray(), mode.ToString());
        }
    }

    [TestMethod]
    public void TheHeroOverexertsAndKeepsItsSubturnUntilItsActionsComplete()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        var revision = owner.Version.Revision;
        Assert.IsTrue(owner.QueryActionPointCheck(Hero, 12));
        var over = Ok(owner.ConsumeActionPoints(owner.Version, Hero, 12, world), owner);
        Assert.AreEqual((SessionActionPointUse.Overexerted, 0), (over.Charge, over.Events.Count));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the damage and the round are one step");
        Assert.AreEqual(2, over.Life!.Outcome!.After.FatigueDamage);
        Assert.AreEqual(2, owner.QueryHeroLife()!.Record.State.FatigueDamage);
        Round(owner, 1, Hero, 0, Wolf, Bandit, Hero);
        Assert.IsFalse(owner.QueryActionPointCheck(Hero, 1), "no action points left");
        Assert.AreEqual(1, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.HeroFatigue), "the recovery timer is set");

        var passed = Ok(owner.CompleteCombatActions(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Wolf, 7) }, passed.Events.ToArray());
    }

    [TestMethod]
    public void TheHeroKeepsItsSubturnWhileItHasActionPoints()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null);
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Round(owner, 1, Hero, 9, Hero);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition);
        Ok(owner.ConsumeActionPoints(owner.Version, Hero, 9, world), owner);
        var wrapped = Ok(owner.CompleteCombatActions(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Hero, 9) }, wrapped.Events.ToArray());
    }

    [TestMethod]
    public void A12AnOverexertionKnockoutCostsTheNextParticipantItsActionPointsInTheOriginal()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Owner(mode);
            Assert.AreEqual(GameSessionDisposition.Changed,
                owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 26 }).Disposition);
            var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7));
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
            var knockout = Ok(owner.ConsumeActionPoints(owner.Version, Hero, 10, world), owner);
            Assert.AreEqual(SessionActionPointUse.Overexerted, knockout.Charge);
            Assert.IsTrue(knockout.Life!.Outcome!.Events.HasFlag(LifeEvents.LostConsciousness));
            CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Wolf, 7) }, knockout.Events.ToArray());
            Round(owner, 2, Wolf, mode == RuleMode.Original ? 0 : 7, Wolf, Hero);
        }
    }

    [TestMethod]
    public void AnOverexertionKnockoutOfTheLastActiveParticipantEndsTheRound()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Owner(mode);
            Assert.AreEqual(GameSessionDisposition.Changed,
                owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 26 }).Disposition);
            var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7, new LifeState(30, 0, 0, LifeConditions.None, 0)));
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Round(owner, 1, Hero, 9, Wolf, Hero);
            var start = Now(owner);
            var knockout = Ok(owner.ConsumeActionPoints(owner.Version, Hero, 10, world), owner);
            CollectionAssert.AreEqual(new[] { Ended(Hero), new SessionCombatEvent(SessionCombatEventKind.RoundEnded, null, 0) },
                knockout.Events.ToArray(), mode.ToString());
            Assert.IsNull(owner.QueryCombatRound());
            Assert.AreEqual(start + TurnBasedRules.RoundGameTimeMs, Now(owner), "the round's end still moved the clock");
        }
    }

    [TestMethod]
    public void AStunEndsTheVictimsSubturnAndAnInactiveHeroNeverHoldsTheRound()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Effects = CriticalHitEffects.Stun }).Disposition);
        Round(owner, 1, Hero, 0, Wolf, Bandit, Hero);
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Wolf, 7) },
            Ok(owner.CompleteCombatActions(owner.Version, world), owner).Events.ToArray());
    }

    [TestMethod]
    public void AHeroKilledOnItsSubturnDoesNotHoldTheRound()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 100 }).Disposition);
        Assert.AreEqual(9, owner.QueryCombatRound()!.ActionPoints, "a death keeps the action points");
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Wolf, 7) },
            Ok(owner.CompleteCombatActions(owner.Version, world), owner).Events.ToArray());
    }

    [TestMethod]
    public void TheStallCheckStartsEmptyEveryRound()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7));
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition,
            "the wolf's 3 from the last round is no reference");
        Round(owner, 2, Wolf, 3, Wolf, Hero);
    }

    [TestMethod]
    public void A11StalledActorsAreForcedToSpendTheRest()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            var original = mode == RuleMode.Original;
            using var owner = Owner(mode);
            var world = Fight();
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition, "first check");
            var second = owner.CompleteCombatActions(owner.Version, world);
            if (original)
            {
                // The reference was reset to 0 when the round began, so the wolf needs a third check.
                Assert.AreEqual(GameSessionDisposition.NoChange, second.Disposition);
                second = owner.CompleteCombatActions(owner.Version, world);
            }
            CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, Ok(second, owner).Events.ToArray(), mode.ToString());

            // The bandit spends between its checks; the original compares its second check with the wolf's 3.
            Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 2, world), owner);
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition);
            Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 3, world), owner);
            var check = owner.CompleteCombatActions(owner.Version, world);
            if (original) CollectionAssert.AreEqual(new[] { Ended(Bandit), Started(Hero, 9) }, Ok(check, owner).Events.ToArray());
            else
            {
                Assert.AreEqual(GameSessionDisposition.NoChange, check.Disposition);
                Round(owner, 1, Bandit, 3, Wolf, Bandit, Hero);
            }
        }
    }

    [TestMethod]
    public void AKnockoutEndsTheVictimsSubturnAtItsNextCompletion()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var wolf = SessionLifeTarget.Critter("oid:wolf", new LifeFacts(), default);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, wolf, new LifeDamage { Fatigue = 30 }).Disposition);
        Round(owner, 1, Wolf, 0, Wolf, Bandit, Hero);
        CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, Ok(owner.CompleteCombatActions(owner.Version, world), owner).Events.ToArray());
        var bandit = SessionLifeTarget.Critter("oid:bandit", new LifeFacts(), default);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyDamage(owner.Version, bandit, new LifeDamage { Fatigue = 5 }).Disposition);
        Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
    }

    [TestMethod]
    public void TheGateAndThePreviewFollowTheSubturn()
    {
        using var owner = Owner();
        var world = Fight();
        var tired = Npc(8, new LifeState(0, 20, 0, LifeConditions.None, 0));
        Assert.AreEqual(SessionCombatGate.Allowed, owner.QueryCombatActionGate(Bandit, 50, tired), "no round");
        Assert.AreEqual(SessionCombatGate.Inactive, owner.QueryCombatActionGate(Bandit, 0, null));
        Assert.IsTrue(owner.QueryActionPointCheck(Wolf, 99));
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Assert.AreEqual(SessionCombatGate.NotTheirTurn, owner.QueryCombatActionGate(Bandit, 1, tired));
        Assert.AreEqual(SessionCombatGate.NotTheirTurn, owner.QueryCombatActionGate(Hero, 1));
        Assert.AreEqual(SessionCombatGate.Allowed, owner.QueryCombatActionGate(Wolf, 7, tired));
        Assert.AreEqual(SessionCombatGate.LowFatigue, owner.QueryCombatActionGate(Wolf, 8, tired), "10 of 30 fatigue is 33 %");
        Assert.AreEqual(SessionCombatGate.Allowed, owner.QueryCombatActionGate(Wolf, 8, Npc(7)));
        Assert.AreEqual(SessionCombatGate.Inactive, owner.QueryCombatActionGate(Wolf, 1, Npc(7, new LifeState(0, 0, 0, LifeConditions.Stunned, 0))));
        Assert.IsTrue(owner.QueryActionPointCheck(Wolf, 7));
        Assert.IsFalse(owner.QueryActionPointCheck(Wolf, 8));
        Assert.IsTrue(owner.QueryActionPointCheck(Bandit, 99), "not its subturn");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.QueryActionPointCheck(Wolf, -1));
    }

    [TestMethod]
    public void AJoiningNpcIsAppendedBeforeTheParty()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.AddCombatant(owner.Version, Dog), owner);
        Round(owner, 1, Bandit, 8, Wolf, Bandit, Dog, Hero);
        Assert.AreEqual(1, owner.QueryCombatRound()!.Current);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.AddCombatant(owner.Version, Dog).Disposition);
        Refused(owner.AddCombatant(owner.Version, Hero), owner, GameSessionFailure.InvalidCombatant, owner.Version.Revision);
        world.Facts["oid:dog"] = Npc(5);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Assert.AreEqual(Dog, owner.QueryCombatRound()!.Actor);
        Ok(owner.EndCombatTurn(owner.Version, Dog, world), owner);

        // Joining while the hero acts moves the hero's index; its subturn goes on.
        Ok(owner.AddCombatant(owner.Version, Virgil), owner);
        Round(owner, 1, Hero, 9, Wolf, Bandit, Dog, Virgil, Hero);
        Assert.AreEqual(4, owner.QueryCombatRound()!.Current);
    }

    [TestMethod]
    public void OrdinaryGameTimeStopsDuringARound()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var before = Now(owner);
        var token = owner.QueryClockSnapshot().Token;
        Assert.AreEqual(ClockMutationDisposition.NoChange, owner.AdvanceGameTime(token, 5000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(ClockFailure.InvalidDuration, owner.AdvanceGameTime(token, -1, ClockAdvanceReason.OrdinaryAdvance).Failure);
        Assert.AreEqual(before, Now(owner));
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(token, 5000, ClockAdvanceReason.ExplicitWait).Disposition);

        var ended = Ok(owner.EndCombatRound(owner.Version), owner);
        Assert.AreEqual(0, ended.Events.Count);
        Assert.IsNull(owner.QueryCombatRound());
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndCombatRound(owner.Version).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
    }

    [TestMethod]
    public void AWorldThatChangesTheSessionMidCallRefusesTheStep()
    {
        using var owner = Owner();
        var world = Fight();
        world.DuringFacts = () => owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("x", "y"), 1);
        var result = owner.StartCombatRound(owner.Version, world);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (result.Disposition, result.Failure));
        Assert.IsNull(owner.QueryCombatRound());
    }

    [TestMethod]
    public void TheRoundSurvivesCanonicalTextAndRestore()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 3, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 1, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"reactions\":{\"records\":[]},\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:bandit\",null]," +
            "\"current\":0,\"actionPoints\":3},\"combatMode\":{\"hero\":false,\"critters\":[]},\"concealment\":{\"hero\":false,\"npcs\":[]},\"danger\":{\"records\":[]},\"sound\":");
        using var restored = Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Round(restored, 1, Wolf, 3, Wolf, Bandit, Hero);
        // The stalled-actor check is not saved: after the restore the wolf's next check is a first one again.
        Assert.AreEqual(GameSessionDisposition.NoChange, restored.CompleteCombatActions(restored.Version, world).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CompleteCombatActions(owner.Version, world).Disposition);
    }

    [TestMethod]
    public void RestoreAndDecodeRefuseRoundsTheRulesCannotProduce()
    {
        using var owner = Owner();
        Ok(owner.StartCombatRound(owner.Version, Fight()), owner);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string saved = "\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:bandit\",null],\"current\":0,\"actionPoints\":7}";
        StringAssert.Contains(text, saved);

        void RestoreFails(string replacement, GameSessionFailure failure)
        {
            var result = GameSessionOwner.Restore(Decode(text.Replace(saved, replacement)), RestoreOptions());
            Assert.AreEqual((null, failure), (result.Owner, result.Failure), replacement);
        }
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:wolf\",null],\"current\":0,\"actionPoints\":7}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",null,null],\"current\":0,\"actionPoints\":7}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\"],\"current\":0,\"actionPoints\":7}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",null],\"current\":2,\"actionPoints\":7}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[],\"current\":0,\"actionPoints\":7}", GameSessionFailure.InvalidCombatRound);
        using var golden = GoldenSession();
        var heroless = GameSessionSnapshotCodec.Encode(golden.Capture()).Replace(NoCombat, ",\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":5}");
        var noHero = GameSessionOwner.Restore(Decode(heroless), RestoreOptions());
        Assert.AreEqual((null, GameSessionFailure.InvalidCombatRound), (noHero.Owner, noHero.Failure));

        void DecodeFails(string replacement, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(saved, replacement));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), replacement);
        }
        DecodeFails("\"combat\":{\"turn\":0,\"participants\":[null],\"current\":0,\"actionPoints\":7}", SnapshotDecodeFailure.OutOfRange, "$.combat.turn");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":1001}", SnapshotDecodeFailure.OutOfRange, "$.combat.actionPoints");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":-1,\"actionPoints\":7}", SnapshotDecodeFailure.OutOfRange, "$.combat.current");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[\"oid wolf\"],\"current\":0,\"actionPoints\":7}", SnapshotDecodeFailure.OutOfRange, "$.combat.participants[0]");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[3],\"current\":0,\"actionPoints\":7}", SnapshotDecodeFailure.WrongType, "$.combat.participants[0]");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0}", SnapshotDecodeFailure.MissingField, "$.combat.actionPoints");
        DecodeFails("\"combat\":[]", SnapshotDecodeFailure.WrongType, "$.combat");
    }
}
