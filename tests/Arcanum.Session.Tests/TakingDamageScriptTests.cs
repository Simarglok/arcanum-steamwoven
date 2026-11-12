using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1141 (GT198, GT210–GT212): the victim's taking-damage script (point 0x17) asked by the two session life calls no hit path covers: the script kill 0x45D900 and the poison damage tick 0x4B1170, both with no attacker.
// A vetoed kill takes no hit points and starts no death but still pins the hit-point damage at 32000; a vetoed poison tick writes nothing and goes on. Entity ids and facts are invented.
[TestClass]
public sealed class TakingDamageScriptTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;
    private static readonly LifeFacts Wolf = new();

    private static SessionLifeTarget Npc(string entity = "oid:wolf") => SessionLifeTarget.Critter(entity, Wolf, default);

    private static GameSessionOwner Seeded()
    {
        var result = GameSessionOwner.Create(Descriptor() with { RandomSeed = Seed });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void A_kill_with_no_host_or_a_run_default_script_kills_as_before()
    {
        using var none = Seeded();
        Assert.IsTrue(none.Kill(none.Version, Npc()).Outcome!.Events.HasFlag(LifeEvents.Died));
        using var allowed = Seeded();
        var asked = new List<SessionTakingDamageRequest>();
        allowed.TakingDamageScript = request => { asked.Add(request); return true; };
        var killed = allowed.Kill(allowed.Version, Npc());
        Assert.IsTrue(killed.Outcome!.Events.HasFlag(LifeEvents.Died));
        Assert.AreEqual(new SessionTakingDamageRequest("oid:wolf", allowed.QueryActor().Map.RuntimeMap, null), asked.Single(), "once, with no attacker");
        Assert.IsNotNull(allowed.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs);
    }

    [TestMethod]
    public void A_vetoed_kill_takes_no_hit_points_starts_no_death_and_still_pins_the_damage_at_32000()
    {
        using var owner = Seeded();
        var deaths = 0;
        owner.DyingScript = _ => { deaths++; return true; };
        owner.KillNotice = _ => deaths++;
        owner.TakingDamageScript = _ => false;
        var killed = owner.Kill(owner.Version, Npc());
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.IsFalse(killed.Outcome!.Events.HasFlag(LifeEvents.Died), "the death flag's step never ran");
        Assert.AreEqual((0, DeathAnimation.None), (killed.Outcome.HitPointDamage, killed.Outcome.Death));
        var record = owner.QueryLifeRecord("oid:wolf")!.Value;
        Assert.AreEqual(LifeLimits.DeathHitPointDamage, record.State.HitPointDamage, "the kill's own pin follows the vetoed step");
        Assert.IsNull(record.DeathTimeMs, "no death procedure: no death time");
        Assert.AreEqual(0, deaths, "no dying script and no kill notice");
        Assert.IsTrue(owner.QueryLife(Npc()).View!.Status.IsDead, "the critter reads as dead all the same");
    }

    [TestMethod]
    public void The_script_is_asked_for_a_dead_or_invulnerable_victim_too_and_a_veto_still_pins()
    {
        // GT198: the script runs at the start of the damage apply, before the destroyed and invulnerable gates.
        using var owner = Seeded();
        var asked = 0;
        owner.TakingDamageScript = _ => { asked++; return false; };
        var invulnerable = SessionLifeTarget.Critter("oid:golem", new LifeFacts { Traits = LifeTraits.Invulnerable }, default);
        owner.Kill(owner.Version, invulnerable);
        Assert.AreEqual(LifeLimits.DeathHitPointDamage, owner.QueryLifeRecord("oid:golem")!.Value.State.HitPointDamage);
        owner.TakingDamageScript = _ => { asked++; return true; };
        owner.Kill(owner.Version, Npc("oid:corpse"));
        owner.Kill(owner.Version, Npc("oid:corpse"));
        Assert.AreEqual(3, asked, "every kill asked once, the second of a corpse included");
    }

    [TestMethod]
    public void The_hero_is_never_asked_and_a_host_that_throws_counts_as_run_default()
    {
        using var owner = Seeded();
        var asked = 0;
        owner.TakingDamageScript = _ => { asked++; return false; };
        Assert.IsTrue(owner.Kill(owner.Version, Npc("oid:one")).Outcome!.Events == LifeEvents.None, "the NPC is vetoed");
        using var hero = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        hero.TakingDamageScript = _ => { asked++; return false; };
        Assert.AreEqual(GameSessionDisposition.Changed, hero.Kill(hero.Version, SessionLifeTarget.Hero).Disposition);
        Assert.IsTrue(hero.QueryHeroLife()!.Status.IsDead, "the hero has no script: its kill goes on");
        Assert.AreEqual(1, asked, "only the NPC was asked");
        using var throwing = Seeded();
        throwing.TakingDamageScript = _ => throw new InvalidOperationException("host failure");
        Assert.IsTrue(throwing.Kill(throwing.Version, Npc()).Outcome!.Events.HasFlag(LifeEvents.Died));
    }

    [TestMethod]
    public void A_host_write_during_the_question_does_not_make_the_callers_version_stale()
    {
        using var owner = Seeded();
        owner.TakingDamageScript = _ =>
        {
            owner.ApplyDamage(owner.Version, Npc("oid:other"), new LifeDamage { HitPoints = 1 });
            return false;
        };
        var version = owner.Version;
        var killed = owner.Kill(version, Npc());
        Assert.AreEqual(GameSessionFailure.None, killed.Failure, "the host's own write is no stale call");
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        // A caller that was already stale stays stale.
        var stale = owner.Kill(version, Npc("oid:late"));
        Assert.AreEqual(GameSessionFailure.StaleVersion, stale.Failure);
    }

    [TestMethod]
    public void A_vetoed_poison_tick_writes_nothing_and_an_allowed_one_deals_its_damage()
    {
        using var owner = Seeded();
        var poisoned = SessionLifeTarget.Critter("oid:wolf", Wolf, default);
        owner.ApplyDamage(owner.Version, poisoned, new LifeDamage { Poison = 250 });
        var asked = new List<SessionTakingDamageRequest>();
        var allow = false;
        owner.TakingDamageScript = request => { asked.Add(request); return allow; };
        var vetoed = owner.PoisonDamageTick(owner.Version, poisoned);
        Assert.AreEqual(GameSessionDisposition.NoChange, vetoed.Disposition);
        Assert.AreEqual((0, 250), (owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage, owner.QueryLifeRecord("oid:wolf")!.Value.State.PoisonLevel));
        allow = true;
        var through = owner.PoisonDamageTick(owner.Version, poisoned);
        Assert.AreEqual(GameSessionDisposition.Changed, through.Disposition);
        Assert.AreEqual(2, owner.QueryLifeRecord("oid:wolf")!.Value.State.HitPointDamage, "level 250 deals 2");
        Assert.AreEqual(2, asked.Count);
        Assert.IsTrue(asked.All(request => request.Attacker is null && request.Victim == "oid:wolf"));
    }
}
