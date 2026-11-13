using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1530 (ledger VB2-6): the host's enter-combat and exit-combat scripts (slots 13 and 14) of the danger setter 0x4AABE0, asked after the commit, with the entering call's rules 8 (reaction lowered) and 9 (focus := target) written
// after the script, as the original does. NPC ids, reaction facts and AI parameter 10 (30) are invented.
[TestClass]
public sealed class DangerHookScriptTests
{
    private const string Wolf = "oid:wolf", Sogg = "oid:sogg";
    private static readonly SessionCombatant Hero = SessionCombatant.Hero, SoggHandle = SessionCombatant.Critter(Sogg);
    private static readonly ReactionNpcFacts Npc60 = new(60, CharacterRace.Human, false, false);
    private static readonly ReactionPcFacts Pc = new(CharacterRace.HalfElf, 0, 0, [], [], [], false);
    private static readonly SessionDangerReaction Reaction = new(Npc60, Pc, 30);

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Rules = rules });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionDangerRequest Fight(string entity, SessionCombatant target) =>
        new() { Entity = entity, State = DangerState.CombatFocus, Target = target, Reaction = Reaction };

    [TestMethod]
    public void TheEnterCombatScriptRunsAfterTheStateBitsAndBeforeRules8And9()
    {
        using var owner = Owner();
        var seen = new List<(SessionDangerHook Hook, DangerFlags Flags, SessionCombatant? Focus, int? Remembered)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(hook.Attachee).Flags, owner.QueryDanger(hook.Attachee).Focus, owner.QueryReactionRecord(hook.Attachee)?.Remembered));

        var fight = owner.SetDangerState(owner.Version, Fight(Wolf, Hero));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (fight.Disposition, fight.Failure));
        var run = seen.Single();
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.EnterCombat, Hero, Wolf), run.Hook);
        Assert.AreEqual(DangerFlags.Fighting | DangerFlags.CheckWield | DangerFlags.CheckGrenade, run.Flags, "the bits are written before the script");
        Assert.IsNull(run.Focus, "rule 9 comes after the script");
        Assert.IsNull(run.Remembered, "rule 8 comes after the script");

        var hostile = ReactionRules.Effective(60, Npc60, Pc, ReactionQuery.Hostility);
        Assert.AreEqual(Hero, owner.QueryDanger(Wolf).Focus);
        Assert.AreEqual(60 + 30 - hostile, owner.QueryReactionRecord(Wolf)!.Value.Remembered);
        Assert.AreEqual(30 - hostile, fight.Reaction!.Value.Delta, "the result still reports the reaction change");
        Assert.AreEqual(owner.Version, fight.Version);

        // Already fighting: no entering, no script; the focus moves to the new target with the rest.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Fight(Wolf, SoggHandle)).Disposition);
        Assert.AreEqual(1, seen.Count);
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).Focus);
    }

    [TestMethod]
    public void AScriptThatMovesTheFocusLosesItToRule9()
    {
        using var owner = Owner();
        var inside = new List<SessionCombatant?>();
        owner.DangerHookScript = hook =>
        {
            // The script sends the NPC after Sogg (the target lock's setter call moves the focus at once, as the script's own attack order would).
            if (hook.Point != DangerScriptPoint.EnterCombat) return;
            Assert.IsTrue(owner.LockDangerTarget(owner.Version, Fight(hook.Attachee, SoggHandle)).Locked);
            inside.Add(owner.QueryDanger(hook.Attachee).Focus);
        };
        var fight = owner.SetDangerState(owner.Version, Fight(Wolf, Hero));
        Assert.AreEqual(GameSessionDisposition.Changed, fight.Disposition);
        Assert.AreEqual(SoggHandle, inside.Single(), "the script's own focus write stands while it runs");
        Assert.AreEqual(Hero, owner.QueryDanger(Wolf).Focus, "rule 9 writes the target after the script");
        Assert.AreEqual(owner.Version, fight.Version, "the call reports the version after the script");
    }

    [TestMethod]
    public void TheTargetLockIsDecidedAfterTheScriptAndRules8And9()
    {
        foreach (var rules in new[] { RuleMode.Steamwoven, RuleMode.Original })
        {
            using var owner = Owner(rules);
            // The script calms the NPC at once (the setter for NONE), so the NPC does not read COMBAT_FOCUS when the lock is decided.
            owner.DangerHookScript = hook =>
            {
                if (hook.Point == DangerScriptPoint.EnterCombat)
                    Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, new() { Entity = hook.Attachee, State = DangerState.None }).Disposition);
            };
            var locked = owner.LockDangerTarget(owner.Version, Fight(Wolf, SoggHandle));
            Assert.IsFalse(locked.Locked, rules.ToString());
            Assert.IsFalse(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.TargetLocked), rules.ToString());
        }
    }

    [TestMethod]
    public void TheExitCombatScriptRunsAfterTheBitsAreCleared()
    {
        using var owner = Owner();
        var seen = new List<(SessionDangerHook Hook, DangerFlags Flags)>();
        owner.DangerHookScript = hook => seen.Add((hook, owner.QueryDanger(hook.Attachee).Flags));
        owner.SetDangerState(owner.Version, Fight(Wolf, SoggHandle));
        seen.Clear();
        var calm = owner.SetDangerState(owner.Version, new() { Entity = Wolf, State = DangerState.None, Target = SoggHandle });
        Assert.AreEqual(GameSessionDisposition.Changed, calm.Disposition);
        var run = seen.Single();
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.ExitCombat, SoggHandle, Wolf), run.Hook);
        Assert.AreEqual(DangerFlags.DemaintainSpells | DangerFlags.CheckWield | DangerFlags.CheckGrenade, run.Flags, "FIGHTING is gone and DEMAINTAIN_SPELLS set before the script");
        // Not fighting any more: a second calm-down runs nothing.
        owner.SetDangerState(owner.Version, new() { Entity = Wolf, State = DangerState.None });
        Assert.AreEqual(1, seen.Count);
    }

    [TestMethod]
    public void AHostThatThrowsIsIgnoredAndTheRulesStillRun()
    {
        using var owner = Owner();
        owner.DangerHookScript = _ => throw new InvalidOperationException("no script");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetDangerState(owner.Version, Fight(Wolf, Hero)).Disposition);
        Assert.AreEqual(Hero, owner.QueryDanger(Wolf).Focus);
        Assert.IsNotNull(owner.QueryReactionRecord(Wolf));
    }

    [TestMethod]
    public void TheTargetLockAsksTheScriptToo()
    {
        using var owner = Owner();
        var hooks = new List<SessionDangerHook>();
        owner.DangerHookScript = hooks.Add;
        var locked = owner.LockDangerTarget(owner.Version, Fight(Wolf, SoggHandle));
        Assert.IsTrue(locked.Locked);
        Assert.AreEqual(new SessionDangerHook(DangerScriptPoint.EnterCombat, SoggHandle, Wolf), hooks.Single());
        Assert.AreEqual(SoggHandle, owner.QueryDanger(Wolf).Focus);
    }

    [TestMethod]
    public void WithNoHostTheRulesAreWrittenWithTheRestAsBefore()
    {
        using var owner = Owner();
        var fight = owner.SetDangerState(owner.Version, Fight(Wolf, Hero));
        Assert.AreEqual(1, owner.Version.Revision, "one revision for the whole call");
        Assert.AreEqual(Hero, owner.QueryDanger(Wolf).Focus);
        Assert.IsNotNull(fight.Reaction);
    }
}
