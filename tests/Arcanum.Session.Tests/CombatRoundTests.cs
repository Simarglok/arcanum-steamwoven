using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;
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

    // ARC-DEV-136 (MIG13 S5b): the completion step the round host ran every frame before is two things in the original, the animation system's callback (0x4B7080: the stall detector
    // sees the participant, the turn-based event is asked for) and the event's handler (0x4B7150: the forced spend and the pass). A check here is both, as one frame of the old host was.
    private static SessionCombatResult Check(GameSessionOwner owner, ISessionCombatWorld world)
    {
        owner.AnimationsDone();
        return owner.CompleteCombatActions(owner.Version, world);
    }

    private sealed class OpenPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalHandle StartRun(GameSessionOwner owner, string entity, GoalType goal, GoalStartPlan? plan = null)
    {
        var result = owner.StartGoal(new GoalStartRequest(entity, plan ?? GoalStarters.NoInterrupt, new GoalFrame(goal)), new OpenPorts());
        Assert.AreEqual(SessionGoalFailure.None, result.Failure);
        Assert.IsTrue(result.Started, result.Outcome?.ToString());
        return result.Handle!.Value;
    }

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
    public void ARoundStopsGameTimeOnlyAndItsEndJumpsGameAndAnimationBySecond()
    {
        // MIG12 S3 (TL38): in a round a frame admits real and animation time but no game time; the round's second is a jump of both.
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var before = owner.QueryFrameClocks();
        var first = owner.BeginFrame(0);
        Assert.AreEqual(FrameClockFailure.None, first.Failure);
        first.Frame!.End();
        var admitted = owner.BeginFrame(100);
        Assert.AreEqual((FrameClockFailure.None, new FrameAdmission(100, 0, 800)), (admitted.Failure, admitted.Frame!.Admission));
        var spent = admitted.Frame.Step(800);
        Assert.AreEqual((true, 0L), (spent.Ok, spent.GameMs), "a round stops game time, not animation or real time");
        admitted.Frame.End();
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        var after = owner.QueryFrameClocks();
        Assert.AreEqual(before.RealMs + 100, after.RealMs);
        Assert.AreEqual(before.GameMs + TurnBasedRules.RoundGameTimeMs, after.GameMs);
        Assert.AreEqual(before.AnimationMs + 800 + TurnBasedRules.RoundGameTimeMs, after.AnimationMs);
        Assert.AreEqual(before.WorldMs + 100, after.WorldMs, "the round's jump leaves world time alone");
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

        var passed = Ok(Check(owner, world), owner);
        CollectionAssert.AreEqual(new[] { Ended(Hero), Began(2), Started(Wolf, 7) }, passed.Events.ToArray());
    }

    [TestMethod]
    public void TheHeroKeepsItsSubturnWhileItHasActionPoints()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null);
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Round(owner, 1, Hero, 9, Hero);
        Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition);
        Ok(owner.ConsumeActionPoints(owner.Version, Hero, 9, world), owner);
        var wrapped = Ok(Check(owner, world), owner);
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
            Ok(Check(owner, world), owner).Events.ToArray());
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
            Ok(Check(owner, world), owner).Events.ToArray());
    }

    [TestMethod]
    public void TheStallCheckStartsEmptyEveryRound()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7));
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition,
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
            Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition, "first check");
            var second = Check(owner, world);
            if (original)
            {
                // The reference was reset to 0 when the round began, so the wolf needs a third check.
                Assert.AreEqual(GameSessionDisposition.NoChange, second.Disposition);
                second = Check(owner, world);
            }
            CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, Ok(second, owner).Events.ToArray(), mode.ToString());

            // The bandit spends between its checks; the original compares its second check with the wolf's 3.
            Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 2, world), owner);
            Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition);
            Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 3, world), owner);
            var check = Check(owner, world);
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
        CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, Ok(Check(owner, world), owner).Events.ToArray());
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
    public void TheSubturnStartsAreCountedWhicheverCallBeganThemAndSavedWithTheRound()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Owner(mode);
            var world = Fight();
            Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:virgil").Disposition);
            world.With(Virgil, 101, 200, Npc(6));
            Assert.AreEqual(0, owner.QueryCombatSubturnStarts());
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Assert.AreEqual(1, owner.QueryCombatSubturnStarts(), "the round's first subturn");
            // A refused charge passes the turn on: the bandit's subturn starts.
            Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 8, world), owner);
            Assert.AreEqual(2, owner.QueryCombatSubturnStarts());
            // A refused call starts nothing.
            Assert.AreEqual(GameSessionDisposition.Rejected, owner.EndCombatTurn(new GameSessionVersion(owner.Identity, 0), Bandit, world).Disposition);
            Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition);
            Assert.AreEqual(2, owner.QueryCombatSubturnStarts());
            Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
            world.Facts["oid:virgil"] = null;
            // Past the end through the skip loop A9 starts the wolf's subturn twice in the Original setting: both starts count.
            Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
            Assert.AreEqual(mode == RuleMode.Original ? 5 : 4, owner.QueryCombatSubturnStarts(), mode.ToString());
            // GR19 (ARC-DEV-078): saved with the round, so the count goes on after a load.
            using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions() with { Rules = mode });
            Assert.AreEqual(owner.QueryCombatSubturnStarts(), restored.QueryCombatSubturnStarts(), mode.ToString());
            Assert.AreEqual(owner.QueryCombatSubturnStarts(), restored.QueryCombatRound()!.SubturnStarts);
        }
    }

    [TestMethod]
    public void TheRoundSurvivesCanonicalTextAndRestore()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 3, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 1, world), owner);
        Assert.AreEqual(GameSessionDisposition.NoChange, Check(owner, world).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"reactions\":{\"records\":[]},\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:bandit\",null]," +
            "\"current\":0,\"actionPoints\":3,\"subturnStarts\":1},\"combatMode\":{\"hero\":false,\"critters\":[]},\"concealment\":{\"hero\":false,\"npcs\":[]},\"danger\":{\"records\":[]},\"sound\":");
        using var restored = Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Round(restored, 1, Wolf, 3, Wolf, Bandit, Hero);
        // Steamwoven resumes the loaded round as it was saved (GR19).
        Assert.AreEqual(GameSessionDisposition.NoChange, restored.ResumeLoadedCombatRound(restored.Version, world).Disposition);
        Round(restored, 1, Wolf, 3, Wolf, Bandit, Hero);
        // The stalled-actor check is not saved: after the restore the wolf's next check is a first one again.
        Assert.AreEqual(GameSessionDisposition.NoChange, Check(restored, world).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, Check(owner, world).Disposition);
    }

    [TestMethod]
    public void RestoreAndDecodeRefuseRoundsTheRulesCannotProduce()
    {
        using var owner = Owner();
        Ok(owner.StartCombatRound(owner.Version, Fight()), owner);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string saved = "\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:bandit\",null],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}";
        StringAssert.Contains(text, saved);

        void RestoreFails(string replacement, GameSessionFailure failure)
        {
            var result = GameSessionOwner.Restore(Decode(text.Replace(saved, replacement)), RestoreOptions());
            Assert.AreEqual((null, failure), (result.Owner, result.Failure), replacement);
        }
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",\"oid:wolf\",null],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",null,null],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\"],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[\"oid:wolf\",null],\"current\":2,\"actionPoints\":7,\"subturnStarts\":1}", GameSessionFailure.InvalidCombatRound);
        RestoreFails("\"combat\":{\"turn\":1,\"participants\":[],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", GameSessionFailure.InvalidCombatRound);
        using var golden = GoldenSession();
        var heroless = GameSessionSnapshotCodec.Encode(golden.Capture()).Replace(NoCombat, ",\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":5,\"subturnStarts\":0}");
        var noHero = GameSessionOwner.Restore(Decode(heroless), RestoreOptions());
        Assert.AreEqual((null, GameSessionFailure.InvalidCombatRound), (noHero.Owner, noHero.Failure));

        void DecodeFails(string replacement, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(saved, replacement));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), replacement);
        }
        DecodeFails("\"combat\":{\"turn\":0,\"participants\":[null],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", SnapshotDecodeFailure.OutOfRange, "$.combat.turn");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":1001,\"subturnStarts\":0}", SnapshotDecodeFailure.OutOfRange, "$.combat.actionPoints");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":-1,\"actionPoints\":7,\"subturnStarts\":1}", SnapshotDecodeFailure.OutOfRange, "$.combat.current");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[\"oid wolf\"],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", SnapshotDecodeFailure.OutOfRange, "$.combat.participants[0]");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[3],\"current\":0,\"actionPoints\":7,\"subturnStarts\":1}", SnapshotDecodeFailure.WrongType, "$.combat.participants[0]");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0}", SnapshotDecodeFailure.MissingField, "$.combat.actionPoints");
        DecodeFails("\"combat\":[]", SnapshotDecodeFailure.WrongType, "$.combat");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":7}", SnapshotDecodeFailure.MissingField, "$.combat.subturnStarts");
        DecodeFails("\"combat\":{\"turn\":1,\"participants\":[null],\"current\":0,\"actionPoints\":7,\"subturnStarts\":-1}", SnapshotDecodeFailure.OutOfRange, "$.combat.subturnStarts");
    }

    // GR19 (ARC-DEV-078): a fight saved in round 2 on the bandit's subturn with 5 action points left; 5 subturn starts.
    private static string SavedRoundTwo()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Hero, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 3, world), owner);
        Round(owner, 2, Bandit, 5, Wolf, Bandit, Hero);
        Assert.AreEqual(5, owner.QueryCombatSubturnStarts());
        return GameSessionSnapshotCodec.Encode(owner.Capture());
    }

    private static GameSessionOwner Loaded(string text, RuleMode rules) => Restore(Decode(text), RestoreOptions() with { Rules = rules });

    // The square after the load: a dog came in front of the bandit, the wolf now stands after it.
    private static World AfterLoad() => new World().With(Hero, 0, 0, null).With(Dog, 100, 205, Npc(5)).With(Bandit, 95, 195, Npc(8))
        .With(Wolf, 105, 200, Npc(7));

    [TestMethod]
    public void GR19TheOriginalRebuildsALoadedRoundAsRoundOneWithTheSavedActor()
    {
        var text = SavedRoundTwo();
        using var owner = Loaded(text, RuleMode.Original);
        Round(owner, 2, Bandit, 5, Wolf, Bandit, Hero);
        var revision = owner.Version.Revision;
        var clock = Now(owner);
        var world = AfterLoad();
        var resumed = Ok(owner.ResumeLoadedCombatRound(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { Began(1), Started(Bandit, 5) }, resumed.Events.ToArray());
        Round(owner, 1, Bandit, 5, Dog, Bandit, Wolf, Hero);
        Assert.AreEqual((revision + 1, clock, 6L), (owner.Version.Revision, Now(owner), owner.QueryCombatSubturnStarts()), "no clock step");
        Assert.AreEqual(1, world.Scans);
        // Once only: the round is no longer the loaded one.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ResumeLoadedCombatRound(owner.Version, world).Disposition);
        Assert.AreEqual(1, world.Scans);
        // The rebuilt round plays on: the bandit's subturn ends, the wolf's starts.
        CollectionAssert.AreEqual(new[] { Ended(Bandit), Started(Wolf, 7) }, Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner).Events.ToArray());
    }

    [TestMethod]
    public void GR19AnOriginalRebuildThatReadsAsTheSavedRoundStillTakesARevision()
    {
        // Saved at the first subturn of round 1, the same square at the load: only the started subturn differs.
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        using var loaded = Loaded(GameSessionSnapshotCodec.Encode(owner.Capture()), RuleMode.Original);
        Round(loaded, 1, Wolf, 7, Wolf, Bandit, Hero);
        var revision = loaded.Version.Revision;
        var resumed = Ok(loaded.ResumeLoadedCombatRound(loaded.Version, world), loaded);
        CollectionAssert.AreEqual(new[] { Began(1), Started(Wolf, 7) }, resumed.Events.ToArray());
        Round(loaded, 1, Wolf, 7, Wolf, Bandit, Hero);
        Assert.AreEqual((revision + 1, 2L), (loaded.Version.Revision, loaded.QueryCombatSubturnStarts()), "the saved subturn starts moved");
    }

    [TestMethod]
    public void GR19SteamwovenResumesALoadedRoundExactly()
    {
        var text = SavedRoundTwo();
        using var owner = Loaded(text, RuleMode.Steamwoven);
        var world = AfterLoad();
        var revision = owner.Version.Revision;
        var resumed = owner.ResumeLoadedCombatRound(owner.Version, world);
        Assert.AreEqual((GameSessionDisposition.NoChange, 0), (resumed.Disposition, resumed.Events.Count));
        Round(owner, 2, Bandit, 5, Wolf, Bandit, Hero);
        Assert.AreEqual((revision, 5L, 0), (owner.Version.Revision, owner.QueryCombatSubturnStarts(), world.Scans));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(owner.Capture()), "the save it was loaded from");
        // A later switch to the Original setting does not rebuild it: the load rule ran at the first call.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ResumeLoadedCombatRound(owner.Version, world).Disposition);
        Round(owner, 2, Bandit, 5, Wolf, Bandit, Hero);
    }

    [TestMethod]
    public void GR19TheLoadRuleIsTheModeActiveAtTheResume()
    {
        var text = SavedRoundTwo();
        using var owner = Loaded(text, RuleMode.Steamwoven);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SelectRules(RuleMode.Original));
        Ok(owner.ResumeLoadedCombatRound(owner.Version, AfterLoad()), owner);
        Round(owner, 1, Bandit, 5, Dog, Bandit, Wolf, Hero);
        using var other = Loaded(text, RuleMode.Original);
        Assert.AreEqual(GameSessionDisposition.Changed, other.SelectRules(RuleMode.Steamwoven));
        Assert.AreEqual(GameSessionDisposition.NoChange, other.ResumeLoadedCombatRound(other.Version, AfterLoad()).Disposition);
        Round(other, 2, Bandit, 5, Wolf, Bandit, Hero);
    }

    [TestMethod]
    public void GR19AMissingSavedActorLoadsInTheOriginalFromTheHeadOfTheRebuiltOrder()
    {
        using var owner = Loaded(SavedRoundTwo(), RuleMode.Original);
        // The bandit is gone from the square: the original's load fails here; it goes on from the dog, with the dog's own points.
        var world = new World().With(Hero, 0, 0, null).With(Dog, 100, 205, Npc(5)).With(Wolf, 105, 200, Npc(7));
        var resumed = Ok(owner.ResumeLoadedCombatRound(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { Began(1), Started(Dog, 5) }, resumed.Events.ToArray());
        Round(owner, 1, Dog, 5, Dog, Wolf, Hero);
    }

    [TestMethod]
    public void GR19ARebuildInWhichNobodyCanActEndsTheRoundInsteadOfFailingTheLoad()
    {
        using var owner = Loaded(SavedRoundTwo(), RuleMode.Original);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { Fatigue = 28 }).Disposition);
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7, new LifeState(0, 30, 0, LifeConditions.None, 0)));
        var resumed = Ok(owner.ResumeLoadedCombatRound(owner.Version, world), owner);
        CollectionAssert.AreEqual(new[] { new SessionCombatEvent(SessionCombatEventKind.RoundEnded, null, 0) }, resumed.Events.ToArray());
        Assert.IsNull(owner.QueryCombatRound());
    }

    [TestMethod]
    public void GR19ARoundCallBeforeTheResumeOrARefusedResumeKeepTheRoundAsLoaded()
    {
        var text = SavedRoundTwo();
        using (var owner = Loaded(text, RuleMode.Original))
        {
            var revision = owner.Version.Revision;
            Refused(owner.ResumeLoadedCombatRound(new GameSessionVersion(owner.Identity, revision + 7), AfterLoad()), owner,
                GameSessionFailure.StaleVersion, revision);
            Refused(owner.ResumeLoadedCombatRound(owner.Version, new World().With(Bandit, 95, 195, Npc(8))), owner,
                GameSessionFailure.InvalidCombatFacts, revision);
            Round(owner, 2, Bandit, 5, Wolf, Bandit, Hero);
            // Still pending after the refusals.
            Ok(owner.ResumeLoadedCombatRound(owner.Version, AfterLoad()), owner);
            Round(owner, 1, Bandit, 5, Dog, Bandit, Wolf, Hero);
        }
        using (var owner = Loaded(text, RuleMode.Original))
        {
            var world = AfterLoad();
            Ok(owner.ConsumeActionPoints(owner.Version, Bandit, 1, world), owner);
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.ResumeLoadedCombatRound(owner.Version, world).Disposition);
            Round(owner, 2, Bandit, 4, Wolf, Bandit, Hero);
        }
        using (var plain = Owner(RuleMode.Original))
            Assert.AreEqual(GameSessionDisposition.NoChange, plain.ResumeLoadedCombatRound(plain.Version, Fight()).Disposition);
    }

    // ---- ARC-DEV-136 (MIG13 S5b, goal-table.md §3.7, TL39): the table's callback and the turn-based event ----

    [TestMethod]
    public void TheCallbackDoesNothingWithoutARoundOrWhileTheRoundStartHoldsIt()
    {
        using var owner = Owner();
        Assert.IsFalse(owner.AnimationsDone(), "no round");
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        using (owner.SuppressRoundCallback())
        {
            using (owner.SuppressRoundCallback()) Assert.IsFalse(owner.AnimationsDone(), "held (the round start's interrupts come before the callback is registered, TH10)");
            Assert.IsFalse(owner.AnimationsDone(), "the hold nests");
        }
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        Assert.AreEqual(0, owner.AnimationCallbacks);
        Assert.IsTrue(owner.AnimationsDone(), "released");
        Assert.AreEqual(1, owner.AnimationCallbacks);
    }

    [TestMethod]
    public void TheCallbackAsksForTheTurnBasedEventTwoMillisecondsAheadOnceAndAHeroWithPointsKeepsItsTurn()
    {
        using var owner = Owner();
        var world = new World().With(Hero, 0, 0, null).With(Wolf, 105, 200, Npc(7));
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Round(owner, 1, Hero, 9, Wolf, Hero);
        Assert.IsFalse(owner.AnimationsDone(), "an active hero with action points keeps its subturn (TB6)");
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        Assert.AreEqual(0, owner.AnimationCallbacks, "the early return is no callback of the stall detector");
        Ok(owner.ConsumeActionPoints(owner.Version, Hero, 9, world), owner);
        Round(owner, 1, Hero, 0, Wolf, Hero);
        var now = owner.QueryFrameClocks().RealMs;
        Assert.IsTrue(owner.AnimationsDone());
        Assert.AreEqual(now + GameSessionOwner.TurnBasedEventDelayMs, owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        Assert.IsTrue(owner.AnimationsDone(), "one pending at most: the second callback keeps the first deadline");
        Assert.AreEqual(now + GameSessionOwner.TurnBasedEventDelayMs, owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        Assert.AreEqual(2, owner.AnimationCallbacks);
    }

    [TestMethod]
    public void AFreeThatLeavesNoSignificantGoalCallsTheCallbackAndOneThatLeavesSomeDoesNot()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        var wolfWalk = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        var banditAttack = StartRun(owner, Bandit.Entity!, GoalType.Attack);
        Assert.AreEqual(2, owner.QueryGoals().SignificantGoalCount);
        Assert.IsTrue(owner.InterruptGoal(wolfWalk, GoalInterruptRules.MaxLevel).Succeeded);
        Assert.AreEqual(1, owner.QueryGoals().SignificantGoalCount);
        Assert.AreEqual(0, owner.AnimationCallbacks, "the bandit's attack still counts: the free of the wolf's walk calls nothing");
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
        Assert.IsTrue(owner.InterruptGoal(banditAttack, GoalInterruptRules.MaxLevel).Succeeded);
        Assert.AreEqual(0, owner.QueryGoals().SignificantGoalCount);
        Assert.AreEqual(1, owner.AnimationCallbacks, "the last significant goal's free calls it");
        Assert.IsNotNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
    }

    [TestMethod]
    public void AFreeWithTheCountAtZeroCallsTheCallbackWhateverTheFreedRunWasAndEveryEndedRunCallsIt()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        // A fidget (priority 1) is not counted, but its free still sees the counter at 0 and calls the callback (0x44CF20, GT11).
        var fidget = StartRun(owner, Wolf.Entity!, GoalType.Fidget);
        Assert.AreEqual(0, owner.QueryGoals().SignificantGoalCount);
        Assert.IsTrue(owner.KillGoal(fidget).Succeeded);
        Assert.AreEqual(1, owner.AnimationCallbacks);
        // Two runs freed by one sweep: each free is a call.
        StartRun(owner, Wolf.Entity!, GoalType.Fidget);
        StartRun(owner, Bandit.Entity!, GoalType.Fidget);
        var before = owner.AnimationCallbacks;
        owner.SweepGoalsForRoundStart();
        Assert.AreEqual(before + 2, owner.AnimationCallbacks, "one call for each ended run whose free left no significant goal");
    }

    [TestMethod]
    public void AFreeCallsNothingOutsideARound()
    {
        using var owner = Owner();
        var run = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        Assert.IsTrue(owner.InterruptGoal(run, GoalInterruptRules.MaxLevel).Succeeded);
        Assert.AreEqual(0, owner.AnimationCallbacks);
        Assert.IsNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
    }

    [TestMethod]
    public void TheStallDetectorLivesInTheCallbackAndTheEventsHandlerOnlySpendsWhatItMarked()
    {
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            var original = mode == RuleMode.Original;
            using var owner = Owner(mode);
            var world = Fight();
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
            // The handler observes nothing: any number of passes without a callback force nobody.
            for (var i = 0; i < 4; i++) Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition);
            Round(owner, 1, Wolf, 3, Wolf, Bandit, Hero);
            // The first callback only stores the wolf; with A11 the reference action points are the round's reset 0, so the second differs and the third forces.
            owner.AnimationsDone();
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition, "first callback");
            owner.AnimationsDone();
            if (original)
            {
                Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition, "second callback (A11)");
                owner.AnimationsDone();
            }
            var forced = owner.CompleteCombatActions(owner.Version, world);
            CollectionAssert.AreEqual(new[] { Ended(Wolf), Started(Bandit, 8) }, Ok(forced, owner).Events.ToArray(), mode.ToString());
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition, "the forced flag was spent");
        }
    }

    [TestMethod]
    public void AStalledFlagIsSpentOnWhoeverIsCurrentInTheOriginalAndOnlyOnTheMarkedParticipantInSteamwoven()
    {
        // GT23, GT24: the flag is global and the event comes 2 ms after the callback; when the wolf's subturn ends in between (its hook finds nothing to do), the bandit is current when the event falls due.
        foreach (var mode in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            var original = mode == RuleMode.Original;
            using var owner = Owner(mode);
            var world = Fight();
            Ok(owner.StartCombatRound(owner.Version, world), owner);
            Ok(owner.ConsumeActionPoints(owner.Version, Wolf, 4, world), owner);
            for (var i = 0; i < (original ? 3 : 2); i++) owner.AnimationsDone();
            Round(owner, 1, Wolf, 3, Wolf, Bandit, Hero);

            Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
            Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
            var handled = owner.CompleteCombatActions(owner.Version, world);

            if (original) CollectionAssert.AreEqual(new[] { Ended(Bandit), Started(Hero, 9) }, Ok(handled, owner).Events.ToArray(), "the bandit paid the wolf's stall");
            else
            {
                Assert.AreEqual(GameSessionDisposition.NoChange, handled.Disposition, "the bandit keeps its turn");
                Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
            }
            Assert.AreEqual(GameSessionDisposition.NoChange, owner.CompleteCombatActions(owner.Version, world).Disposition, $"{mode}: the flag was spent or dropped");
        }
    }

    [TestMethod]
    public void AMapCloseThatEndsTheLastSignificantRunsCallsTheCallbackOnceForTheFreeThatLeavesNoneAndNotForTheFreeBeforeIt()
    {
        using var owner = Owner();
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        StartRun(owner, Bandit.Entity!, GoalType.MoveToTile);
        Assert.AreEqual(2, owner.QueryGoals().SignificantGoalCount);
        var callbacks = owner.AnimationCallbacks;
        if (owner.QueryCombatRound() is null) Assert.Inconclusive("the round ended with the map change before the table closed");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);

        Assert.AreEqual(0, owner.QueryGoals().InUseCount, "the close ended both runs at level 6");
        Assert.AreEqual(callbacks + 1, owner.AnimationCallbacks, "the first free left a significant goal (no callback), the second left none (TL39, GT11)");
    }

    [TestMethod]
    public void ATurnSetsInterruptOfThePreviousCrittersRunCallsTheCallbackForTheNewParticipant()
    {
        using var owner = Owner(RuleMode.Original);
        var world = Fight();
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        // The wolf's run is interrupted by the turn set after the bandit became current (TH1, TH10): the free calls the callback, which sees the bandit and its action points.
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
        var run = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        owner.InterruptGoals(Wolf.Entity!, GoalInterruptRules.OrdinaryActions);
        Assert.AreEqual(1, owner.AnimationCallbacks);
        Assert.AreEqual(0, owner.QueryGoals().InUseCount);
        Assert.IsFalse(owner.QueryGoals().TryGet(run, out _));
        Assert.IsNotNull(owner.QueryRealStep(SessionRealStep.TurnBasedStep));
    }

    [TestMethod]
    public void TheFirstFrameOfARunForAnOwnerThatIsNotTheCurrentParticipantComesOneMillisecondAfterTheStart()
    {
        using var owner = Owner();
        var world = Fight();
        var outside = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        Assert.AreEqual(owner.QueryFrameClocks().AnimationMs + GameSessionOwner.FirstGoalStepDelayMs, owner.QueryGoalStep(outside), "outside a round: 5 ms (GS7)");
        owner.KillGoals(Wolf.Entity!);
        Ok(owner.StartCombatRound(owner.Version, world), owner);
        Round(owner, 1, Wolf, 7, Wolf, Bandit, Hero);
        var animation = owner.QueryFrameClocks().AnimationMs;
        var current = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        Assert.AreEqual(animation + GameSessionOwner.FirstGoalStepDelayMs, owner.QueryGoalStep(current), "the current participant: 5 ms");
        var other = StartRun(owner, Bandit.Entity!, GoalType.Attack);
        Assert.AreEqual(animation + GameSessionOwner.RoundFirstGoalStepDelayMs, owner.QueryGoalStep(other), "another participant: 1 ms straight in the live list (TL41)");
        var stranger = StartRun(owner, "oid:stranger", GoalType.MoveToTile);
        Assert.AreEqual(animation + 1, owner.QueryGoalStep(stranger), "a critter outside the round fails the gates the same way");
        owner.KillGoals(Wolf.Entity!);
        Ok(owner.EndCombatTurn(owner.Version, Wolf, world), owner);
        Round(owner, 1, Bandit, 8, Wolf, Bandit, Hero);
        var turned = StartRun(owner, Wolf.Entity!, GoalType.MoveToTile);
        Assert.AreEqual(owner.QueryFrameClocks().AnimationMs + 1, owner.QueryGoalStep(turned), "the wolf's turn is over");
        // The hero's turn: every critter's run is another's.
        Ok(owner.EndCombatTurn(owner.Version, Bandit, world), owner);
        Round(owner, 1, Hero, 9, Wolf, Bandit, Hero);
        var npc = StartRun(owner, Bandit.Entity!, GoalType.HitByWeapon);
        Assert.IsTrue(owner.QueryGoalStep(npc) <= owner.QueryFrameClocks().AnimationMs + 1);
    }
}
