using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The flee-spell step, perform fleeing, stop fleeing, the back-off bits and the kill notification's writes in the session
// (ARC-WORLD-082, AI slice I5). The hero is the half-elf fixture; NPC ids are invented.
[TestClass]
public sealed class MoraleTests
{
    private const string Wolf = "oid:wolf", Sogg = "oid:sogg";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant WolfHandle = SessionCombatant.Critter(Wolf), SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Flee(string entity, SessionCombatant? from) => new() { Entity = entity, State = DangerState.Flee, Target = from };

    [TestMethod]
    public void TheFleeSpellStepSetsThenClearsSpellFleeAndRoundTrips()
    {
        using var owner = Owner();
        var notFleeing = owner.RunFleeSpellStep(owner.Version, Wolf, () => true);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidDangerFacts), (notFleeing.Disposition, notFleeing.Failure));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, Hero)).Disposition);
        var revision = owner.Version.Revision;

        var none = owner.RunFleeSpellStep(owner.Version, Wolf, () => false);
        Assert.AreEqual((GameSessionDisposition.NoChange, FleeSpellOutcome.NoneChosen, revision), (none.Disposition, none.Outcome, owner.Version.Revision));
        var chosen = owner.RunFleeSpellStep(owner.Version, Wolf, () => true);
        Assert.AreEqual((GameSessionDisposition.Changed, FleeSpellOutcome.Chosen, revision + 1), (chosen.Disposition, chosen.Outcome, owner.Version.Revision));
        Assert.AreEqual(DangerFlags.Fleeing | DangerFlags.SpellFlee, owner.QueryDanger(Wolf).Flags);

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"flags\":[\"Fleeing\",\"SpellFlee\"],\"focus\":null,\"fleeingFrom\":\"\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));

        var asked = false;
        var cleared = owner.RunFleeSpellStep(owner.Version, Wolf, () => asked = true);
        Assert.AreEqual((GameSessionDisposition.Changed, FleeSpellOutcome.ClearsFlag, false), (cleared.Disposition, cleared.Outcome, asked));
        Assert.AreEqual(DangerFlags.Fleeing, owner.QueryDanger(Wolf).Flags);

        var stale = owner.RunFleeSpellStep(new GameSessionVersion(owner.Identity, revision), Wolf, () => true);
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.RunFleeSpellStep(owner.Version, "bad id", () => true).Failure);
    }

    [TestMethod]
    public void Mr7ClearsSpellFleeWhenTheFlightTurnsIntoAFight()
    {
        foreach (var (rules, keeps) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            using var owner = Owner(rules);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, Hero)).Disposition);
            Assert.AreEqual(FleeSpellOutcome.Chosen, owner.RunFleeSpellStep(owner.Version, Wolf, () => true).Outcome);
            var fight = new SessionDangerRequest { Entity = Wolf, State = DangerState.CombatFocus, Target = SoggHandle };
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, fight).Disposition);
            Assert.AreEqual(keeps, owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.SpellFlee), rules.ToString());
        }
    }

    [TestMethod]
    public void PerformFleeingSurrendersToTheFleeingFromAndMr9DecidesTheFleeGoal()
    {
        foreach (var (rules, refusedGoal) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            using var owner = Owner(rules);
            Assert.AreEqual(GameSessionFailure.InvalidDangerFacts,
                owner.PerformFleeing(owner.Version, new SessionPerformFleeingRequest { Entity = Wolf }).Failure, "only a fleeing NPC");
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, WolfHandle, true, Living).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, SoggHandle)).Disposition);
            var revision = owner.Version.Revision;
            Assert.AreEqual(GameSessionFailure.InvalidDangerFacts,
                owner.PerformFleeing(owner.Version, new SessionPerformFleeingRequest { Entity = Wolf }).Failure, "combat-mode facts needed");
            Assert.AreEqual(revision, owner.Version.Revision);

            var refused = owner.PerformFleeing(owner.Version, new SessionPerformFleeingRequest
            {
                Entity = Wolf, Facts = new(LeaderExtraordinaryCharisma: true), CombatMode = Living,
            });
            Assert.AreEqual((DangerState.CombatFocus, refusedGoal), (refused.Transition!.Value.Applied, refused.StartsFleeGoal), rules.ToString());
            Assert.AreEqual(DangerState.CombatFocus, owner.QueryDanger(Wolf).State);

            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, SoggHandle)).Disposition);
            var surrender = owner.PerformFleeing(owner.Version, new SessionPerformFleeingRequest { Entity = Wolf, CombatMode = Living });
            Assert.AreEqual((GameSessionDisposition.Changed, DangerState.Surrender, true, true),
                (surrender.Disposition, surrender.Transition!.Value.Applied, surrender.StartsFleeGoal, surrender.LeftCombatMode));
            var record = owner.QueryDanger(Wolf);
            Assert.AreEqual((DangerState.Surrender, SoggHandle), (record.State, record.Source));
        }
    }

    [TestMethod]
    public void StopFleeingCalmsAFleeingNpcAndKeepsASurrender()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.StopFleeing(owner.Version, Wolf, new()).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, Hero)).Disposition);
        var stop = owner.StopFleeing(owner.Version, Wolf, new());
        Assert.AreEqual((GameSessionDisposition.Changed, DangerState.None), (stop.Disposition, stop.Transition!.Value.Applied));
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Wolf).State);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Wolf, Hero)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = Wolf, State = DangerState.Surrender, Target = Hero }).Disposition);
        var kept = owner.StopFleeing(owner.Version, Wolf, new());
        Assert.AreEqual((GameSessionDisposition.NoChange, (DangerTransition?)null), (kept.Disposition, kept.Transition));
        Assert.AreEqual(DangerState.Surrender, owner.QueryDanger(Wolf).State);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.StopFleeing(owner.Version, "bad id", new()).Failure);
    }

    [TestMethod]
    public void TheBackOffBitsAndTheKillNotificationAreWrittenInOneRevision()
    {
        using var owner = Owner();
        var start = owner.StartBackOff(owner.Version, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, start.Disposition);
        Assert.AreEqual(DangerFlags.BackingOff | DangerFlags.CheckGrenade, owner.QueryDanger(Wolf).Flags);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EndBackOff(owner.Version, Wolf).Disposition);
        Assert.AreEqual(DangerFlags.CheckGrenade, owner.QueryDanger(Wolf).Flags);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EndBackOff(owner.Version, Wolf).Disposition);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Flee(Sogg, Hero)).Disposition);
        var revision = owner.Version.Revision;
        var plan = new KillNotificationPlan(new StopFleeingStep(true, true), [Wolf]);
        var bad = owner.ApplyKillNotification(owner.Version, new SessionKillNotificationRequest
        {
            Victim = SessionCombatant.Critter(Sogg), Plan = plan with { BacksOff = ["bad id"] },
        });
        Assert.AreEqual((GameSessionFailure.InvalidDangerTarget, revision), (bad.Failure, owner.Version.Revision));

        var kill = owner.ApplyKillNotification(owner.Version, new SessionKillNotificationRequest { Victim = SessionCombatant.Critter(Sogg), Plan = plan });
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1, DangerState.None), (kill.Disposition, owner.Version.Revision, kill.Transition!.Value.Applied));
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Sogg).State);
        Assert.AreEqual(DangerFlags.CheckGrenade | DangerFlags.BackingOff, owner.QueryDanger(Wolf).Flags, "the kill back-off sets no CHECK_GRENADE");

        var hero = owner.ApplyKillNotification(owner.Version, new SessionKillNotificationRequest { Victim = Hero, Plan = plan with { BacksOff = [] } });
        Assert.AreEqual((GameSessionDisposition.NoChange, (DangerTransition?)null), (hero.Disposition, hero.Transition));
    }
}
