using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-292: the immediate AI pass of a struck NPC. The original's damage step (0x4B4390) calls the AI pass once at its end for a victim that survives (0x4B52FC), and the ranged and melee miss
/// paths call it too (0x4B30F5, 0x4B3948); there is no tick and no distance test, so a hit NPC reacts at the hit and not at its next tick (up to 5 s later). Invented sectors and NPCs (the rebind
/// tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiStruckTests
{
    private static readonly AttackResolution Missed = Resolution(AttackRefusal.None, null);

    private static AttackResolution Resolution(AttackRefusal refusal, AttackDamage? damage) => new(refusal, AttackOutcome.Miss, AttackMissCause.None, HitLocation.Torso, false, false, null, null, null,
        FateUse.None, false, damage, []);

    private static AttackDamage Damage(DamageRecipient recipient, DamagePrevention prevented) => new(recipient, DamageByType.Zero, DamageByType.Zero, 0, 0, 0, default, false,
        CriticalHitEffects.None, CriticalMissEffects.None, false, prevented);

    private static SessionAttackResult Result(AttackResolution? resolution, GameSessionDisposition disposition = GameSessionDisposition.Changed) =>
        new(disposition, GameSessionFailure.None, resolution, null, null, null, null, GameSessionFailure.None, default!);

    [TestMethod]
    public void A_struck_npc_runs_one_pass_at_once_with_no_tick()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.IsNull(host.TraceOf(f.A)?.Struck, "nothing ran before the hit");
        Assert.IsNull(f.Session.QueryAiTick(f.A), "the host schedules no tick by itself here");

        Assert.IsTrue(host.Struck(f.A), "a living NPC of the host gets a pass");

        StringAssert.StartsWith(host.TraceOf(f.A)?.Struck, "None", "the session ran the pass: " + host.TraceOf(f.A)?.Struck);
        Assert.IsNull(f.Session.QueryAiTick(f.A), "no tick was scheduled or consumed: the pass is the hit's own (0x4B52FC), not an event");
    }

    [TestMethod]
    public void A_victim_the_hero_attacked_gets_a_pass_that_reaches_perform_after_the_attack_event()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        host.HeroAttacked(f.A);
        Assert.IsNull(host.RunOf(f.A), "the attack event alone starts no run: the victim acts at its next pass");
        var danger = f.Session.QueryDanger(f.A);

        Assert.IsTrue(host.Struck(f.A));

        // The pass ran the pipeline to its perform stage, whatever the hit NPC then chose.
        Assert.IsNotNull(host.TraceOf(f.A)?.Perform, "the immediate pass reached the perform stage (danger " + danger.State + "): " + host.TraceOf(f.A)?.Struck);
    }

    [TestMethod]
    public void The_hero_an_unknown_critter_and_a_dead_npc_get_no_pass()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        Assert.IsFalse(host.Struck(AiSchedulerRules.Pc), "the damage step calls the pass for an NPC victim only (object type 16)");
        Assert.IsFalse(host.Struck(f.B), "an NPC no window of the host holds is not run here");

        var target = f.Combat.Build(f.Session, f.A).Combat?.Target ?? throw new AssertFailedException("the NPC has combat values");
        var killed = f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { HitPoints = 10_000, PlainDeath = true });
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.IsFalse(host.Struck(f.A), "a dead victim (hit points 0 or below, 0x45D8D0) gets no pass");
        Assert.IsNull(host.TraceOf(f.A)?.Struck);
    }

    [TestMethod]
    public void Two_hits_in_a_row_each_get_their_own_pass()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        // The guards (a pass of the victim running, its own goal step running) hold only while those run; they are not exercised here.
        Assert.IsTrue(host.Struck(f.A));
        Assert.IsTrue(host.Struck(f.A));
    }

    [TestMethod]
    public void An_attack_result_reaches_the_victims_pass_unless_refused_vetoed_in_flight_or_prevented()
    {
        Assert.IsTrue(AreaAiHost.VictimReached(Result(Missed)), "a miss reaches the miss path (0x4B30F5, 0x4B3948)");
        Assert.IsTrue(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Target, DamagePrevention.None)))), "a hit that was applied");
        Assert.IsTrue(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Attacker, DamagePrevention.Invulnerable)))), "a critical miss that hit the attacker is the victim's miss");

        Assert.IsFalse(AreaAiHost.VictimReached(null));
        Assert.IsFalse(AreaAiHost.VictimReached(Result(null)));
        Assert.IsFalse(AreaAiHost.VictimReached(Result(Missed, GameSessionDisposition.Rejected)), "a rejected attack");
        Assert.IsFalse(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.TargetDead, null))), "a refused attack runs no event and no pass");
        var hit = Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Target, DamagePrevention.None)));
        Assert.IsFalse(AreaAiHost.VictimReached(hit with { Vetoed = true }), "the victim's taking-damage script cancelled the step");
        Assert.IsFalse(AreaAiHost.VictimReached(hit with { StepFailure = GameSessionFailure.BudgetExceeded }), "the damage never landed");
        Assert.IsTrue(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Attacker, DamagePrevention.None))) with { Vetoed = true }),
            "the attacker's own script vetoed the critical miss that turned on it: the victim's miss path still ran");
        Assert.IsFalse(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Target, DamagePrevention.Invulnerable)))), "the damage step ends before the pass");
        Assert.IsFalse(AreaAiHost.VictimReached(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Target, DamagePrevention.Wall)))));
    }

    [TestMethod]
    public void The_attack_end_reads_the_damage_the_victim_took_and_the_critical_flag_of_a_hit_that_reached_it()
    {
        // ARC-RENDER-1794 (0x4B6930): a hit with a damage step on the target hands it on; a critical hit says so; a miss, a hit on the attacker and a refused attack run no attack end.
        var step = Damage(DamageRecipient.Target, DamagePrevention.None);
        var hit = Result(Resolution(AttackRefusal.None, step) with { Outcome = AttackOutcome.Hit });
        Assert.AreEqual((step, false), AreaAiHost.AttackEndOf(hit));
        Assert.AreEqual((step, true), AreaAiHost.AttackEndOf(Result(Resolution(AttackRefusal.None, step) with { Outcome = AttackOutcome.CriticalHit })));
        Assert.AreEqual((null, false), AreaAiHost.AttackEndOf(Result(Resolution(AttackRefusal.None, null) with { Outcome = AttackOutcome.Hit })), "a hit with no damage step: the hit effect only");
        Assert.AreEqual((null, false), AreaAiHost.AttackEndOf(Result(Resolution(AttackRefusal.None, Damage(DamageRecipient.Attacker, DamagePrevention.None)) with { Outcome = AttackOutcome.Hit })), "the damage fell on the attacker");
        Assert.IsNull(AreaAiHost.AttackEndOf(Result(Missed)));
        Assert.IsNull(AreaAiHost.AttackEndOf(null));
        Assert.IsNull(AreaAiHost.AttackEndOf(hit with { Vetoed = true }));
        Assert.IsNull(AreaAiHost.AttackEndOf(Result(Resolution(AttackRefusal.TargetDead, null) with { Outcome = AttackOutcome.Hit })));
    }
}
