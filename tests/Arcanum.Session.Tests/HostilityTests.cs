using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Peace, stop attacking, the attack event's writes, the enemy-list pick and the enemy list's reaction effect in the session
// (ARC-WORLD-080, AI slice I4). The hero is the half-elf fixture; NPC ids, reaction facts and AI parameter 10 (30) are invented.
[TestClass]
public sealed class HostilityTests
{
    private const string Wolf = "oid:wolf", Virgil = "oid:virgil", Sogg = "oid:sogg";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant WolfHandle = SessionCombatant.Critter(Wolf), VirgilHandle = SessionCombatant.Critter(Virgil),
        SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionDangerReaction Reaction = new(Npc60, Pc, 30);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };
    private static readonly SessionPeaceFacts WithReaction = new() { Reaction = new(Npc60, Pc) };

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Fight(string entity, SessionCombatant? target) =>
        new() { Entity = entity, State = DangerState.CombatFocus, Target = target, Reaction = Reaction };

    private static void Changed(GameSessionDisposition disposition) => Assert.AreEqual(GameSessionDisposition.Changed, disposition);

    private static void Join(GameSessionOwner owner, string entity) =>
        Changed(owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);

    [TestMethod]
    public void PeaceWithTheHerosSideClearsBothSidesListsAndRaisesTheReactionToFifty()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)).Disposition);
        Changed(owner.AddDangerEnemyTeam(owner.Version, Wolf, Hero).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, SoggHandle).Disposition);
        Changed(owner.SetDangerHitBy(owner.Version, Wolf, VirgilHandle).Disposition);
        Changed(owner.SetDangerState(owner.Version, Fight(Virgil, WolfHandle)).Disposition);
        Changed(owner.SetDangerHitBy(owner.Version, Virgil, WolfHandle).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Virgil, WolfHandle).Disposition);
        Assert.AreEqual(0, owner.QueryReaction(Wolf, Npc60, Pc).View!.Effective, "the listed hero reads 0");
        var revision = owner.Version.Revision;

        var request = new SessionPeaceRequest { Entity = Wolf, Other = VirgilHandle };
        var missing = owner.MakePeace(owner.Version, request);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidDangerFacts, revision),
            (missing.Disposition, missing.Failure, owner.Version.Revision), "a peace with the hero's side needs the reaction facts");

        var peace = owner.MakePeace(owner.Version, request with { Facts = WithReaction });
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (peace.Disposition, owner.Version.Revision));
        var step = peace.Steps.Single();
        Assert.AreEqual((Wolf, Hero, new SessionDangerHook(DangerScriptPoint.ExitCombat, null, Wolf)), (step.Entity, step.Head, step.Hook));
        CollectionAssert.AreEqual(new[] { Virgil }, step.Followers.ToArray());
        var wolf = owner.QueryDanger(Wolf);
        Assert.AreEqual((DangerState.None, (SessionCombatant?)null, (SessionCombatant?)null), (wolf.State, wolf.Focus, wolf.HitBy));
        CollectionAssert.AreEqual(new[] { SoggHandle }, wolf.Enemies.ToArray(), "only the hero's side leaves the list");
        var virgil = owner.QueryDanger(Virgil);
        Assert.AreEqual(((SessionCombatant?)null, (SessionCombatant?)null, 0), (virgil.Focus, virgil.HitBy, virgil.Enemies.Count));
        Assert.AreEqual((DangerState.CombatFocus, (SessionCombatant?)null), (virgil.State, virgil.Source), "the follower's state is not touched");
        var lowered = 60 + 30 - ReactionRules.Effective(60, Npc60, Pc, ReactionQuery.Hostility);
        Assert.AreEqual(50 - ReactionRules.Effective(lowered, Npc60, Pc), step.Reaction!.Value.Delta, "raised from the standard reaction");
        Assert.AreEqual(50, owner.QueryReaction(Wolf, Npc60, Pc).View!.Effective);
    }

    [TestMethod]
    public void PeaceWithAnNpcIsOneSided()
    {
        using var owner = Owner();
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, SoggHandle)).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, SoggHandle).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Sogg, WolfHandle).Disposition);
        var peace = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = SoggHandle });
        Assert.AreEqual((GameSessionDisposition.Changed, SoggHandle, 0), (peace.Disposition, peace.Steps.Single().Head, peace.Steps.Single().Followers.Count));
        Assert.AreEqual(0, owner.QueryDanger(Wolf).Enemies.Count);
        CollectionAssert.AreEqual(new[] { WolfHandle }, owner.QueryDanger(Sogg).Enemies.ToArray());
        Assert.IsNull(peace.Steps.Single().Reaction);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.MakePeace(owner.Version, new() { Entity = "bad id", Other = SoggHandle }).Failure);
    }

    [TestMethod]
    public void StopAttackingMakesPeaceLeavesCombatModeAndStopsEachFollower()
    {
        foreach (var (rules, survives) in new[] { (RuleMode.Steamwoven, false), (RuleMode.Original, true) })
        {
            using var owner = Owner(rules);
            Join(owner, Virgil);
            Join(owner, Sogg);
            Changed(owner.SetCombatMode(owner.Version, VirgilHandle, true, Living).Disposition);
            var locked = owner.LockDangerTarget(owner.Version, Fight(Virgil, WolfHandle));
            Assert.IsTrue(locked.Locked);
            Changed(owner.AddDangerEnemy(owner.Version, Virgil, WolfHandle).Disposition);
            var revision = owner.Version.Revision;

            var hero = new SessionStopAttackingRequest { Combatant = Hero };
            Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, owner.StopAttacking(owner.Version, hero).Failure, "Virgil is in combat mode");
            Assert.AreEqual(revision, owner.Version.Revision);
            var stop = owner.StopAttacking(owner.Version, hero with { Facts = new Dictionary<string, SessionPeaceFacts> { [Virgil] = new() { CombatMode = Living } } });
            Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (stop.Disposition, owner.Version.Revision), rules.ToString());
            Assert.AreEqual(2, stop.Steps.Count);
            var (virgil, sogg) = (stop.Steps[0], stop.Steps[1]);
            Assert.AreEqual((Virgil, WolfHandle, true, true, true), (virgil.Entity, virgil.Head, virgil.Interrupted, virgil.LeftCombatMode, virgil.NoAttack));
            Assert.AreEqual((Sogg, (SessionCombatant?)null, false, true), (sogg.Entity, sogg.Head, sogg.Interrupted, sogg.NoAttack));
            Assert.IsFalse(owner.QueryCombatMode(VirgilHandle));
            Assert.AreEqual(DangerFlags.CheckWield | DangerFlags.CheckGrenade | DangerFlags.DemaintainSpells | DangerFlags.NoAttack,
                owner.QueryDanger(Virgil).Flags, "out of the fight, unlocked, stopped");
            Assert.AreEqual(0, owner.QueryDanger(Virgil).Enemies.Count);
            Assert.AreEqual(DangerFlags.NoAttack, owner.QueryDanger(Sogg).Flags);

            // A stopped follower refuses a fight (setter rule 4) until the hero allows it again.
            var refused = owner.SetDangerState(owner.Version, Fight(Virgil, WolfHandle));
            Assert.AreEqual(DangerSetterRule.NoAttack, refused.Transition!.Value.Rule);

            // FO7: a follower that leaves loses NO_ATTACK in Steamwoven; the Original setting keeps it.
            Changed(owner.LeaveParty(owner.Version, Sogg, new PartyLeave(true)).Disposition);
            Assert.AreEqual(survives, owner.QueryDanger(Sogg).Flags.HasFlag(DangerFlags.NoAttack), rules.ToString());
        }
    }

    [TestMethod]
    public void StopAttackingAnNpcTheHeroDoesNotLeadSetsNoNoAttack()
    {
        using var owner = Owner();
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)).Disposition);
        var request = new SessionStopAttackingRequest { Combatant = WolfHandle };
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, owner.StopAttacking(owner.Version, request).Failure, "peace with the hero");
        var stop = owner.StopAttacking(owner.Version, request with { Facts = new Dictionary<string, SessionPeaceFacts> { [Wolf] = WithReaction } });
        Assert.AreEqual(GameSessionDisposition.Changed, stop.Disposition);
        Assert.AreEqual((Hero, false, false), (stop.Steps.Single().Head, stop.Steps.Single().NoAttack, stop.Steps.Single().LeftCombatMode));
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Wolf).State);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.StopAttacking(owner.Version, new() { Combatant = SoggHandle }).Disposition);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.StopAttacking(owner.Version, request with
        {
            Facts = new Dictionary<string, SessionPeaceFacts> { ["bad id"] = new() },
        }).Failure);
    }

    [TestMethod]
    public void TheAttackEventRecordsTheAttackerAndAddsTheTeamsBothWays()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        var both = new AttackEventPlan { WritesHitBy = true, AddsAttackerTeam = true, AddsVictimTeam = true };
        var result = owner.ApplyAttackEvent(owner.Version, new() { Attacker = WolfHandle, Victim = Virgil, Plan = both });
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(WolfHandle, owner.QueryDanger(Virgil).HitBy);
        CollectionAssert.AreEqual(new[] { WolfHandle }, owner.QueryDanger(Virgil).Enemies.ToArray());
        CollectionAssert.AreEqual(new[] { Hero, VirgilHandle }, owner.QueryDanger(Wolf).Enemies.ToArray(), "Virgil's team is the hero's");

        // The hero's attack: its team onto the victim's list; an accidental hit between one faction writes no hit-by.
        var accidental = new AttackEventPlan { AddsAttackerTeam = true };
        Changed(owner.ApplyAttackEvent(owner.Version, new() { Attacker = Hero, Victim = Sogg, Plan = accidental }).Disposition);
        Assert.AreEqual(((SessionCombatant?)null, 2), (owner.QueryDanger(Sogg).HitBy, owner.QueryDanger(Sogg).Enemies.Count));
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts,
            owner.ApplyAttackEvent(owner.Version, new() { Attacker = Hero, Victim = Sogg, Plan = both }).Failure);
        var stopped = owner.ApplyAttackEvent(owner.Version, new() { Attacker = WolfHandle, Victim = Sogg, Plan = both with { Stop = AttackEventStop.VictimDead } });
        Assert.AreEqual(GameSessionDisposition.NoChange, stopped.Disposition);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget,
            owner.ApplyAttackEvent(owner.Version, new() { Attacker = WolfHandle, Victim = Wolf, Plan = both }).Failure);
    }

    [TestMethod]
    public void AStaleVersionOrBadFactsApplyNothing()
    {
        using var owner = Owner();
        Join(owner, Virgil);
        Changed(owner.SetCombatMode(owner.Version, VirgilHandle, true, Living).Disposition);
        Changed(owner.SetDangerState(owner.Version, Fight(Virgil, WolfHandle)).Disposition);
        var stale = new GameSessionVersion(owner.Identity, owner.Version.Revision - 1);
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.MakePeace(stale, new() { Entity = Virgil, Other = WolfHandle }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.StopAttacking(stale, new() { Combatant = Hero }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.ApplyAttackEvent(stale, new()
        {
            Attacker = WolfHandle, Victim = Virgil, Plan = new() { WritesHitBy = true, AddsAttackerTeam = true },
        }).Failure);
        var broken = new SessionCombatModeContext { Facts = new() { Speed = int.MaxValue, Life = new LifeFacts() } };
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, owner.StopAttacking(owner.Version, new()
        {
            Combatant = Hero, Facts = new Dictionary<string, SessionPeaceFacts> { [Virgil] = new() { CombatMode = broken } },
        }).Failure);
        Assert.AreEqual(before, GameSessionSnapshotCodec.Encode(owner.Capture()), "nothing applied");
    }

    [TestMethod]
    public void TheEnemyListPickDrawsFromTheSessionGeneratorAlsoForAnEmptyList()
    {
        using var owner = Owner();
        long Position() => owner.Roll("test", 1, 2).Roll!.Position;
        var before = Position();
        Assert.AreEqual(new SessionEnemyPickResult(GameSessionFailure.None, null), owner.PickDangerEnemy(Wolf, _ => true));
        Assert.AreEqual(before + 2, Position(), "an empty list draws once");

        Changed(owner.AddDangerEnemy(owner.Version, Wolf, SoggHandle).Disposition);
        before = Position();
        Assert.AreEqual(SoggHandle, owner.PickDangerEnemy(Wolf, _ => true).Pick);
        Assert.AreEqual(before + 1, Position(), "a one-entry list draws nothing");
        Assert.IsNull(owner.PickDangerEnemy(Wolf, _ => false).Pick);

        Changed(owner.AddDangerEnemy(owner.Version, Wolf, VirgilHandle).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, Hero).Disposition);
        var picks = Enumerable.Range(0, 30).Select(_ => owner.PickDangerEnemy(Wolf, _ => true).Pick).ToHashSet();
        Assert.AreEqual(3, picks.Count, "every start comes up");
        Assert.AreEqual(VirgilHandle, owner.PickDangerEnemy(Wolf, entry => entry == VirgilHandle).Pick);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.PickDangerEnemy("bad id", _ => true).Failure);
    }

    [TestMethod]
    public void TheSessionsEnemyListDecidesTheReactionsEnemyListFact()
    {
        using var owner = Owner();
        Assert.AreEqual(ReactionRules.Effective(60, Npc60, Pc), owner.QueryReaction(Wolf, Npc60 with { PcOnEnemyList = true }, Pc).View!.Effective,
            "the caller's fact is replaced");
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, Hero).Disposition);
        Assert.AreEqual(0, owner.QueryReaction(Wolf, Npc60, Pc).View!.Effective);
        // A deliberate hit then lowers nothing: the hostility variant already reads 0.
        var hit = owner.ReactToPcHit(owner.Version, Wolf, Npc60, Pc, new(5, 30), true);
        Assert.AreEqual(GameSessionDisposition.NoChange, hit.Disposition);
    }
}
