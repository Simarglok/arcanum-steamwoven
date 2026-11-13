using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1648 (ledger VB2-6): the audit of the multi-call danger plans after ARC-AI-1634 cut peace and stop attacking at their setter call. A plan is cut (DangerPlan.Then) only where a script reached at the
// setter call can see or write a difference. The kill notification and set no-flee call the setter for NONE from FLEE, whose exit script needs FIGHTING, which no FLEE state holds (the setter's FLEE and SURRENDER
// entries clear it through that same exit step), so they run no script at the call; the follower rule and the attack order make one setter call whose enter script runs before rules 8 and 9 and the lock, with
// every write that precedes the call in the original already in the revision. NPC ids and facts are invented.
[TestClass]
public sealed class DangerPlanHookAuditTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg", Wolf = "oid:wolf";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero, WolfHandle = SessionCombatant.Critter(Wolf);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionDangerReaction Reaction = new(Npc60, Pc, 30);
    private static readonly SessionPeaceFacts Combat = new() { Reaction = new(Npc60, Pc) };

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = RuleMode.Steamwoven });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        foreach (var entity in new[] { Virgil, Sogg })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.JoinParty(owner.Version, entity, PartyStateTests.Facts(maxFollowers: 5), true).Disposition);
        return owner;
    }

    private static SessionDangerRequest Request(string entity, DangerState state, SessionCombatant target) =>
        new() { Entity = entity, State = state, Target = target, Reaction = Reaction };

    private static void Changed(SessionDangerResult result) => Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));

    [TestMethod]
    public void FleeingAndSurrenderingFromCombatFocusClearFightingAtTheCall()
    {
        using var owner = Owner();
        Changed(owner.SetDangerState(owner.Version, Request(Wolf, DangerState.CombatFocus, Hero)));
        Changed(owner.SetDangerState(owner.Version, Request(Sogg, DangerState.CombatFocus, Hero)));
        var seen = new List<(SessionDangerHook Hook, DangerFlags Flags)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(hook.Attachee).Flags));

        Changed(owner.SetDangerState(owner.Version, Request(Wolf, DangerState.Flee, Hero)));
        Changed(owner.SetDangerState(owner.Version, Request(Sogg, DangerState.Surrender, Hero)));
        Assert.AreEqual(2, seen.Count);
        foreach (var (hook, flags) in seen)
            Assert.AreEqual((DangerScriptPoint.ExitCombat, false), (hook.Point, flags.HasFlag(DangerFlags.Fighting)), "the exit script runs at the entry call, FIGHTING already cleared");
        Assert.IsTrue(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.Fleeing));
        Assert.IsFalse(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.Fighting));
        Assert.IsFalse(owner.QueryDanger(Sogg).Flags.HasFlag(DangerFlags.Fighting));
    }

    [TestMethod]
    public void TheKillNotificationOfAFleeingVictimRunsNoScriptAtItsStopFleeingCall()
    {
        using var owner = Owner();
        // The victim fought, then fled (its exit script ran then); an opponent of the killer fights the hero.
        Changed(owner.SetDangerState(owner.Version, Request(Sogg, DangerState.CombatFocus, Hero)));
        Changed(owner.SetDangerState(owner.Version, Request(Sogg, DangerState.Flee, Hero)));
        Changed(owner.SetDangerState(owner.Version, Request(Wolf, DangerState.CombatFocus, Hero)));
        var seen = new List<SessionDangerHook>();
        owner.DangerHookScript = seen.Add;

        var kill = owner.ApplyKillNotification(owner.Version, new SessionKillNotificationRequest
        {
            Victim = SessionCombatant.Critter(Sogg), Plan = new KillNotificationPlan(new StopFleeingStep(true, true), [Wolf]),
        });
        Changed(kill);
        Assert.AreEqual(0, seen.Count, "FLEE → NONE has no FIGHTING to exit: no script at the call");
        Assert.AreEqual(DangerState.None, owner.QueryDanger(Sogg).State);
        Assert.IsTrue(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.BackingOff));
    }

    [TestMethod]
    public void SetNoFleeOfAFleeingNpcRunsNoScriptAtItsStopFleeingCall()
    {
        using var owner = Owner();
        Changed(owner.SetDangerState(owner.Version, Request(Wolf, DangerState.CombatFocus, Hero)));
        Changed(owner.SetDangerState(owner.Version, Request(Wolf, DangerState.Flee, Hero)));
        var seen = new List<SessionDangerHook>();
        owner.DangerHookScript = seen.Add;

        Changed(owner.SetNoFlee(owner.Version, Wolf));
        Assert.AreEqual(0, seen.Count);
        Assert.AreEqual((DangerState.None, true), (owner.QueryDanger(Wolf).State, owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.NoFlee)));
    }

    [TestMethod]
    public void TheFollowerRulesFocusRunsTheEnterScriptBeforeRule9AfterTheBits()
    {
        using var owner = Owner();
        var seen = new List<(SessionDangerHook Hook, DangerFlags Flags, SessionCombatant? Focus)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(hook.Attachee).Flags, owner.QueryDanger(hook.Attachee).Focus));
        var revision = owner.Version.Revision;

        var focus = owner.ApplyFollowerRule(owner.Version, new()
        {
            Outcome = new(Virgil, FollowerRuleStep.Focus, UpsetReason.None, false), Leader = Hero, Target = WolfHandle, Setter = new DangerSetterFacts(),
        });
        Changed(focus);
        var run = seen.Single();
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.EnterCombat, WolfHandle, Virgil), run.Hook);
        Assert.IsTrue(run.Flags.HasFlag(DangerFlags.Fighting), "the bits are written before the script");
        Assert.IsNull(run.Focus, "rule 9 follows the script");
        Assert.AreEqual(WolfHandle, owner.QueryDanger(Virgil).Focus);
        Assert.AreEqual(revision + 2, owner.Version.Revision, "the bits, then rule 9 after the script (its own revision)");
    }

    [TestMethod]
    public void TheAttackOrderHasNoAttackLiftedWhenTheEnterScriptRunsAndLocksAfterIt()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.StopAttacking(owner.Version, new() { Combatant = Hero }).Disposition);
        Assert.IsTrue(owner.QueryDanger(Virgil).Flags.HasFlag(DangerFlags.NoAttack));
        var seen = new List<(SessionDangerHook Hook, DangerFlags Flags, SessionCombatant? Focus)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(hook.Attachee).Flags, owner.QueryDanger(hook.Attachee).Focus));

        CompanionOrderFacts Facts(string entity) => new(entity, entity, 2, false, PartyStateTests.Facts(maxFollowers: 5), Combat: Combat);
        var order = owner.IssueCompanionOrder(owner.Version, new(CompanionOrderType.Attack, CompanionOrderAddressing.All, [Facts(Virgil), Facts(Sogg)])
        {
            Target = WolfHandle, World = new CompanionCombatOrderTests.World(),
        });
        Assert.AreEqual(GameSessionDisposition.Changed, order.Disposition);
        Assert.AreEqual(2, seen.Count);
        foreach (var (hook, flags, focus) in seen)
        {
            Assert.AreEqual(DangerScriptPoint.EnterCombat, hook.Point);
            Assert.IsTrue(flags.HasFlag(DangerFlags.Fighting));
            Assert.IsFalse(flags.HasFlag(DangerFlags.NoAttack), "the stop is lifted before the event, so before the script");
            Assert.IsFalse(flags.HasFlag(DangerFlags.TargetLocked), "the lock is decided after the script");
            Assert.IsNull(focus, "rule 9 follows the script");
        }
        foreach (var entity in new[] { Virgil, Sogg })
        {
            var danger = owner.QueryDanger(entity);
            Assert.AreEqual((WolfHandle, true), (danger.Focus, danger.Flags.HasFlag(DangerFlags.TargetLocked)));
        }
        Assert.IsTrue(order.Outcomes.All(o => o.Locked));
    }
}
