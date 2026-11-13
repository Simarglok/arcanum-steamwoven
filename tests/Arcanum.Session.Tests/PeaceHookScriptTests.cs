using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1634 (ledger VB2-6): the exit-combat script (slot 14) of the setter call inside peace (0x4AA300) and stop attacking (0x4AA4A0) runs at that call: after the setter's bits, the focus, hit-by, enemy-list and reaction writes
// before it, and before the followers' loop of peace and the combat-mode exit, NO_ATTACK and unlock of stop attacking. NPC ids, reaction facts and AI parameter 10 (30) are invented.
[TestClass]
public sealed class PeaceHookScriptTests
{
    private const string Wolf = "oid:wolf", Virgil = "oid:virgil", Sogg = "oid:sogg";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero;
    private static readonly SessionCombatant WolfHandle = SessionCombatant.Critter(Wolf), VirgilHandle = SessionCombatant.Critter(Virgil), SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionDangerReaction Reaction = new(Npc60, Pc, 30);
    private static readonly SessionCombatModeContext Living = new() { Facts = new() { Speed = 7, Life = new LifeFacts() } };
    private static readonly SessionPeaceFacts WithReaction = new() { Reaction = new(Npc60, Pc) };

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = RuleMode.Steamwoven });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Fight(string entity, SessionCombatant? target) =>
        new() { Entity = entity, State = DangerState.CombatFocus, Target = target, Reaction = Reaction };

    private static void Changed(GameSessionDisposition disposition) => Assert.AreEqual(GameSessionDisposition.Changed, disposition);

    private static void Join(GameSessionOwner owner, string entity) =>
        Changed(owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);

    // The wolf fights the hero's side (the hero and Virgil on its list, Virgil its last attacker); Virgil, a follower, fights the wolf.
    private static GameSessionOwner Fighting()
    {
        var owner = Owner();
        Join(owner, Virgil);
        Changed(owner.SetDangerState(owner.Version, Fight(Wolf, Hero)).Disposition);
        Changed(owner.AddDangerEnemyTeam(owner.Version, Wolf, Hero).Disposition);
        Changed(owner.SetDangerHitBy(owner.Version, Wolf, VirgilHandle).Disposition);
        Changed(owner.SetDangerState(owner.Version, Fight(Virgil, WolfHandle)).Disposition);
        Changed(owner.AddDangerEnemy(owner.Version, Virgil, WolfHandle).Disposition);
        return owner;
    }

    [TestMethod]
    public void PeaceRunsTheExitScriptAtTheSetterCallBeforeTheFollowersLoop()
    {
        using var owner = Fighting();
        var seen = new List<(SessionDangerHook Hook, SessionDanger Wolf, SessionDanger Virgil, int Reaction)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(Wolf), owner.QueryDanger(Virgil), owner.QueryReaction(Wolf, Npc60, Pc).View!.Effective));
        seen.Clear();

        var peace = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle, Facts = WithReaction });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (peace.Disposition, peace.Failure));
        var run = seen.Single(s => s.Hook.Attachee == Wolf);
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.ExitCombat, null, Wolf), run.Hook);
        // Written before the setter call: the NPC's focus, hit-by and the head off its list, the reaction raised to 50, then the setter's bits.
        Assert.AreEqual(((SessionCombatant?)null, (SessionCombatant?)null), (run.Wolf.Focus, run.Wolf.HitBy));
        Assert.IsFalse(run.Wolf.Enemies.Contains(Hero));
        Assert.AreEqual(50, run.Reaction);
        Assert.AreEqual(DangerState.None, run.Wolf.State);
        Assert.IsFalse(run.Wolf.Flags.HasFlag(DangerFlags.Fighting), "FIGHTING is gone before the script");
        // Not yet written: the followers' loop (the follower forgets the NPC, the follower leaves the NPC's list).
        Assert.AreEqual(WolfHandle, run.Virgil.Focus, "the follower's loop comes after the script");
        Assert.IsTrue(run.Wolf.Enemies.Contains(VirgilHandle), "the follower is still on the NPC's list");
        // After the call: the loop is done.
        Assert.IsNull(owner.QueryDanger(Virgil).Focus);
        Assert.IsFalse(owner.QueryDanger(Wolf).Enemies.Contains(VirgilHandle));
        Assert.AreEqual(owner.Version, peace.Version, "the call reports the version after the rest");
        CollectionAssert.AreEqual(new[] { Virgil }, peace.Steps.Single().Followers.ToArray());
        Assert.AreEqual(Hero, peace.Steps.Single().Head);
    }

    [TestMethod]
    public void TheRestOfThePeaceReadsTheStateTheScriptLeft()
    {
        using var owner = Fighting();
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, SoggHandle).Disposition);
        // The script sets Virgil's and the wolf's last attacker to Sogg: the followers' loop forgets only what names the wolf.
        owner.DangerHookScript = hook =>
        {
            if (hook.Attachee != Wolf) return;
            Changed(owner.SetDangerHitBy(owner.Version, Virgil, SoggHandle).Disposition);
            Changed(owner.SetDangerHitBy(owner.Version, Wolf, SoggHandle).Disposition);
        };
        var peace = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle, Facts = WithReaction });
        Assert.AreEqual(GameSessionDisposition.Changed, peace.Disposition);
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Virgil).HitBy, "the script's write stands");
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).HitBy, "the script's write on the NPC stands");
        Assert.IsTrue(owner.QueryDanger(Wolf).Enemies.Contains(SoggHandle));
        Assert.IsNull(owner.QueryDanger(Virgil).Focus, "the loop still cleared the follower's focus on the wolf");
        Assert.IsFalse(owner.QueryDanger(Wolf).Enemies.Contains(VirgilHandle));
    }

    [TestMethod]
    public void StopAttackingRunsTheExitScriptBeforeTheCombatModeExitNoAttackAndUnlock()
    {
        using var owner = Fighting();
        Join(owner, Sogg);
        Changed(owner.SetCombatMode(owner.Version, VirgilHandle, true, Living).Disposition);
        Assert.IsTrue(owner.LockDangerTarget(owner.Version, Fight(Virgil, WolfHandle)).Locked);
        var seen = new List<(SessionDangerHook Hook, bool InCombatMode, DangerFlags Virgil, DangerFlags Sogg, SessionCombatant? Focus)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryCombatMode(VirgilHandle), owner.QueryDanger(Virgil).Flags, owner.QueryDanger(Sogg).Flags, owner.QueryDanger(Virgil).Focus));
        seen.Clear();

        var stop = owner.StopAttacking(owner.Version, new SessionStopAttackingRequest
        {
            Combatant = Hero,
            Facts = new Dictionary<string, SessionPeaceFacts> { [Virgil] = new() { CombatMode = Living } },
        });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (stop.Disposition, stop.Failure));
        var run = seen.Single(s => s.Hook.Attachee == Virgil);
        Assert.AreEqual(DangerScriptPoint.ExitCombat, run.Hook.Point);
        Assert.IsNull(run.Focus, "peace cleared the focus before the script");
        Assert.IsTrue(run.InCombatMode, "the combat-mode exit comes after the script");
        Assert.IsFalse(run.Virgil.HasFlag(DangerFlags.NoAttack), "NO_ATTACK comes after the script");
        Assert.AreEqual(DangerFlags.None, run.Sogg & DangerFlags.NoAttack, "the second follower is stopped after the first one's script");
        // After the call all of it is done, in the original's order for each follower.
        Assert.IsFalse(owner.QueryCombatMode(VirgilHandle));
        Assert.IsTrue(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
        Assert.IsFalse(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.TargetLocked));
        Assert.IsTrue(owner.QueryDanger(Sogg).Flags.HasFlag(DangerFlags.NoAttack));
        Assert.AreEqual(2, stop.Steps.Count);
        var (virgil, sogg) = (stop.Steps[0], stop.Steps[1]);
        Assert.AreEqual((Virgil, WolfHandle, true, true, true), (virgil.Entity, virgil.Head, virgil.Interrupted, virgil.LeftCombatMode, virgil.NoAttack));
        Assert.AreEqual((Sogg, (SessionCombatant?)null, false, true), (sogg.Entity, sogg.Head, sogg.Interrupted, sogg.NoAttack));
        Assert.AreEqual(owner.Version, stop.Version);
    }

    [TestMethod]
    public void ARefusedCallRunsNoScriptAndWritesNothingEvenWhenAHostIsAttached()
    {
        using var owner = Fighting();
        Changed(owner.SetCombatMode(owner.Version, VirgilHandle, true, Living).Disposition);
        var hooks = new List<SessionDangerHook>();
        owner.DangerHookScript = hooks.Add;
        var revision = owner.Version.Revision;
        // Virgil is in combat mode and the facts of its exit are missing: the whole call is refused before the script of the first setter call.
        var stop = owner.StopAttacking(owner.Version, new SessionStopAttackingRequest { Combatant = Hero });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidDangerFacts), (stop.Disposition, stop.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(0, hooks.Count);
        Assert.AreEqual(WolfHandle, owner.QueryDanger(Virgil).Focus);
        // A peace with the hero's side needs its reaction facts: refused the same way.
        var peace = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidDangerFacts, revision), (peace.Disposition, peace.Failure, owner.Version.Revision));
        Assert.AreEqual(0, hooks.Count);
    }

    [TestMethod]
    public void WithNoHostPeaceAndStopAttackingAreOneRevisionAsBefore()
    {
        using var owner = Fighting();
        var revision = owner.Version.Revision;
        var peace = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle, Facts = WithReaction });
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (peace.Disposition, owner.Version.Revision));
    }

    [TestMethod]
    public void ASetterCallThatQueuesNoScriptDoesNotCutThePlan()
    {
        using var owner = Fighting();
        var hooks = new List<SessionDangerHook>();
        owner.DangerHookScript = hooks.Add;
        // Wolf is not fighting (peace twice): the second call's setter queues no exit script, so it is still one revision.
        Changed(owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle, Facts = WithReaction }).Disposition);
        hooks.Clear();
        Changed(owner.AddDangerEnemy(owner.Version, Wolf, VirgilHandle).Disposition);
        var revision = owner.Version.Revision;
        var again = owner.MakePeace(owner.Version, new() { Entity = Wolf, Other = VirgilHandle, Facts = WithReaction });
        Assert.AreEqual((GameSessionDisposition.Changed, revision + 1), (again.Disposition, owner.Version.Revision));
        Assert.AreEqual(0, hooks.Count);
    }
}
