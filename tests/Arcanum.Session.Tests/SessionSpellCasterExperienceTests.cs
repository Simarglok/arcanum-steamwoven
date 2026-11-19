using Arcanum.Data.Magic;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-295 (B39, ledger XP9/XP10): the experience steps live inside the original's damage function and death notification, so a
// spell's damage pays them as a weapon's does: the pool share of a hit goes to the hero when the damage source is the hero, the kill
// award to a source the hero leads, and a step with no source (a script's damage, a burn) pays nothing. Records and numbers are invented.
[TestClass]
public sealed class SessionSpellCasterExperienceTests
{
    private const string Rat = "oid:rat", Thug = "oid:thug", Hero = SessionSpellCaster.Hero;
    private static readonly SpellLocation HeroTile = new(5, 5), RatTile = new(6, 5), ThugTile = new(7, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter rat, SpellCastCritter thug) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Rat => new(id, SpellTargetObject.Npc, RatTile, HitPoints: 20),
            Thug => new(id, SpellTargetObject.Npc, ThugTile, HitPoints: 20),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) =>
            new[] { Hero, Rat, Thug }.Select(Object).Where(o => o is not null && o.Location == tile).Select(o => o!).ToArray();
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity == Rat ? rat : entity == Thug ? thug : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    // The thug can cast: intelligence 12 and willpower 9, as the AI tests' mage.
    private static SpellCastCritter Npc(string entity, int worth = 160, int pool = 160, uint objectFlags = 0, uint spellFlags = 0)
    {
        var stats = AttackStateTests.Stats(willpower: 9);
        stats[4] = 12;
        return new(SessionCritterCombat.From(entity, AttackStateTests.Npc(stats: stats, critterFlags: 0, worth: worth, pool: pool, objectFlags: objectFlags, spellFlags: spellFlags)).Combat!,
            0, 0, 0, 0, 0);
    }

    // An aggressive spell at a critter object (BEGIN targets 0x44: the object bit and the critter bit) whose BEGIN deals `damage` normal damage to its target.
    private static SessionSpellRecord Bolt(int damage) =>
        SessionSpellRecords.Translate(Record(number: 90, beginTargets: 0x44, components: [new(SpellAction.Begin, SpellComponentType.Damage, new(0x44, 0, 0, 0, -1), 0, 0, new DamageData(0, damage, damage, 0))])
        with { Cost = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, DisallowedCasterSpellFlags = 0,
            DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0x4, MinimumWillpower = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(SessionSpellRecord record, SpellCastCritter? rat = null)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf(), Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        var owner = result.Owner!;
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner, rat ?? Npc(Rat), Npc(Thug)), new Events()));
    }

    // Casts `record` from `parent` at the rat and releases it.
    private static void CastAtRat(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent = Hero)
    {
        var from = caster.Facts(parent)!;
        var to = caster.Facts(Rat)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, from, from, to, to.Location, ParentIsLocalPlayer: parent == Hero), Free: true),
            record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged), string.Join(" | ", caster.Log));
    }

    private static int MaxHitPoints(GameSessionOwner owner, SessionSpellCaster caster, string entity) =>
        owner.QueryLife(caster.LifeTarget(entity)!).View!.Status.MaxHitPoints;

    private static int HeroExperience(GameSessionOwner owner) => owner.QueryHeroExperience()!.Experience;

    [TestMethod]
    public void A_hero_spell_that_hurts_an_npc_pays_the_pool_share_to_the_hero()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var maxHitPoints = MaxHitPoints(owner, caster, Rat);
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        var damage = caster.Log.Single(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat).Value;
        Assert.IsTrue(damage > 0);
        var (award, pool) = ExperienceRules.DamageShare(160, 160, damage, maxHitPoints);
        Assert.IsTrue(award > 0, "the fixture's numbers must pay something");
        Assert.AreEqual(before + award, HeroExperience(owner));
        Assert.AreEqual(new SessionNpcExperience(Rat, 160, pool), owner.QueryNpcExperience(Rat));
        var given = caster.Log.Single(e => e.Kind == SpellCastEventKind.ExperienceGiven);
        Assert.AreEqual((award, 0, Rat), (given.Value, given.Detail, given.Object));
        Assert.AreEqual(award, caster.TakeExperienceAwards().Single().Award);
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count, "the awards are taken once");
    }

    [TestMethod]
    public void A_hero_spell_that_kills_an_npc_pays_the_pool_share_and_a_fifth_of_the_worth_once()
    {
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var maxHitPoints = MaxHitPoints(owner, caster, Rat);
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        var damage = caster.Log.Single(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat).Value;
        var (share, _) = ExperienceRules.DamageShare(160, 160, damage, maxHitPoints);
        var kill = ExperienceRules.KillAward(160);
        Assert.AreEqual(32, kill);
        Assert.AreEqual(before + share + kill, HeroExperience(owner));
        Assert.AreEqual(0, owner.QueryNpcExperience(Rat)!.Value.Worth, "the same NPC never pays twice");
        var given = caster.Log.Single(e => e.Kind == SpellCastEventKind.ExperienceGiven);
        Assert.AreEqual((share, kill), (given.Value, given.Detail));
        Assert.AreEqual(2, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void A_stoned_npc_loses_no_pool_to_a_spell()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record, Npc(Rat, spellFlags: 0x10000));
        using var _ = owner;
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat));
        Assert.AreEqual(before, HeroExperience(owner));
        Assert.IsNull(owner.QueryNpcExperience(Rat));
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ExperienceGiven));
    }

    [TestMethod]
    public void Another_npcs_spell_empties_the_pool_for_nothing_and_its_kill_pays_nothing()
    {
        // XP10: the pool loses the share whoever attacked and only a PC source gets it; XP9: a kill by a critter the hero does not lead pays nobody and keeps the worth.
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record, Thug);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat));
        Assert.AreEqual(before, HeroExperience(owner));
        var saved = owner.QueryNpcExperience(Rat)!.Value;
        Assert.AreEqual(160, saved.Worth, "the worth stays");
        Assert.IsTrue(saved.Pool < 160, "the pool lost its share");
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void A_script_damage_without_a_source_pays_nothing_and_leaves_the_pool()
    {
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var before = HeroExperience(owner);
        var hit = caster.ApplyScriptDamage(Rat, 1000, 0, unresistable: true);
        Assert.IsTrue(hit.Applied);
        Assert.AreEqual(before, HeroExperience(owner));
        Assert.IsNull(owner.QueryNpcExperience(Rat), "no source: neither the pool nor the worth changes");
        Assert.AreEqual(0, caster.TakeExperienceAwards().Count);
    }

    [TestMethod]
    public void Every_damage_step_on_a_critter_is_queued_for_the_host_once_with_its_attacker()
    {
        // ARC-DEV-296 (B42): the steps the host draws floaters from: a spell's (the run's parent attacks), a script's (no attacker).
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        CastAtRat(owner, caster, record);
        var spell = caster.TakeDamageSteps().Single();
        Assert.AreEqual((Rat, (string?)Hero), (spell.Victim, spell.Attacker));
        Assert.IsTrue(spell.Damage.HitPointDamage > 0 && spell.Life.HitPointDamage > 0);
        Assert.AreEqual(0, caster.TakeDamageSteps().Count, "taken once");
        Assert.IsTrue(caster.ApplyScriptDamage(Rat, 2, 0, unresistable: true).Applied);
        var script = caster.TakeDamageSteps().Single();
        Assert.AreEqual((Rat, (string?)null), (script.Victim, script.Attacker));
    }

    [TestMethod]
    public void Only_a_scripts_damage_step_is_marked_as_the_scripts_so_the_host_asks_the_near_death_line_once()
    {
        // ARC-AI-1638: the host asks the follower's near-death line for the steps a spell, a burn or a trap made; a script's damage asks it in its own answer, so its step says FromScript.
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        CastAtRat(owner, caster, record);
        Assert.IsFalse(caster.TakeDamageSteps().Single().FromScript, "a spell's step");
        Assert.IsTrue(caster.ApplyBurn(Rat, Arcanum.Simulation.Combat.FireDamageRules.Damage(false, false)).Applied);
        Assert.IsFalse(caster.TakeDamageSteps().Single().FromScript, "a burn's step");
        Assert.IsTrue(caster.ApplyScriptDamage(Rat, 2, 0, unresistable: true).Applied);
        Assert.IsTrue(caster.TakeDamageSteps().Single().FromScript, "a script's step");
    }

    [TestMethod]
    public void The_damage_step_callback_runs_as_each_step_lands_and_sees_the_state_after_that_step_only()
    {
        // ARC-AI-1680 (0x4B4390 asks the near-death line 0x4AEAB0 inside the step): two steps in one frame, the callback of the first sees the critter after the first and before the second, so a speaker's can-speak gate is read then, not when the host drains the steps.
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var seen = new List<(int Before, int After, int NowHitPoints)>();
        caster.DamageStepApplied = step =>
        {
            var now = owner.QueryLife(caster.LifeTarget(Rat)!).View!.Status;
            seen.Add((step.Life.Before.HitPointDamage, step.Life.After.HitPointDamage, now.MaxHitPoints - now.HitPoints));
        };
        var burn = Arcanum.Simulation.Combat.FireDamageRules.Damage(false, false);
        Assert.IsTrue(caster.ApplyBurn(Rat, burn).Applied);
        Assert.AreEqual(1, seen.Count, "asked as the first step lands, before the next one");
        Assert.AreEqual(seen[0].After, seen[0].NowHitPoints, "the first callback reads the state after the first step");
        Assert.IsTrue(caster.ApplyBurn(Rat, burn).Applied);
        Assert.AreEqual(2, seen.Count);
        Assert.AreEqual(seen[1].After, seen[1].NowHitPoints, "the second callback reads the state after the second step");
        Assert.IsTrue(seen[0].After < seen[1].After, "the second step took more");
        Assert.AreEqual(2, caster.TakeDamageSteps().Count, "the steps stay queued for the floaters");
        caster.TakeDamageSteps();
        // A script's step calls it too (the host skips its own near-death ask for a FromScript step).
        Assert.IsTrue(caster.ApplyScriptDamage(Rat, 2, 0, unresistable: true).Applied);
        Assert.AreEqual(3, seen.Count);
    }

    [TestMethod]
    public void The_damage_step_callback_runs_before_the_steps_experience_award()
    {
        // ARC-AI-1680: in the original's damage function the near-death ask (0x4B4390 calls 0x4AEAB0) comes before the experience and the AI pass, so a spell's callback sees the hero's experience as it was before the step's pool share.
        var record = Bolt(3);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var before = HeroExperience(owner);
        var atCallback = new List<int>();
        caster.DamageStepApplied = _ => atCallback.Add(HeroExperience(owner));
        CastAtRat(owner, caster, record);
        Assert.AreEqual(1, atCallback.Count);
        Assert.AreEqual(before, atCallback[0], "the callback ran before the experience was written");
        Assert.IsTrue(HeroExperience(owner) > before, "the step's pool share was paid after it");
    }

    // ARC-SCRIPT-2859 (LK1–LK3): the kill step's leader-killing loop (0x45DA20, point 20) runs after the award for each direct follower of the hero, for a kill whose killer is the hero or a critter the hero leads.
    private const string Cat = "oid:cat";

    private static List<(SessionLeaderKillingRequest Request, int Experience)> Watch(GameSessionOwner owner, params string[] followers)
    {
        foreach (var follower in followers) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, follower).Disposition);
        var asked = new List<(SessionLeaderKillingRequest, int)>();
        owner.LeaderKillingScript = request => asked.Add((request, HeroExperience(owner)));
        return asked;
    }

    [TestMethod]
    public void A_hero_kill_asks_each_followers_leader_killing_script_in_order_after_the_award()
    {
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var asked = Watch(owner, Thug, Cat);
        var maxHitPoints = MaxHitPoints(owner, caster, Rat);
        var before = HeroExperience(owner);
        CastAtRat(owner, caster, record);
        var damage = caster.Log.Single(e => e.Kind == SpellCastEventKind.Damaged && e.Object == Rat).Value;
        var (share, _) = ExperienceRules.DamageShare(160, 160, damage, maxHitPoints);
        var map = owner.QueryActor().Map.RuntimeMap;
        CollectionAssert.AreEqual(new[] { new SessionLeaderKillingRequest(Rat, map, Thug), new SessionLeaderKillingRequest(Rat, map, Cat) }, asked.Select(a => a.Request).ToArray());
        Assert.IsTrue(asked.All(a => a.Experience == before + share + ExperienceRules.KillAward(160)), "the loop follows the kill award");
        Assert.IsNotNull(owner.QueryLifeRecord(Rat)!.Value.DeathTimeMs, "the answer decides nothing: the death was processed");
    }

    [TestMethod]
    public void A_follower_that_died_earlier_is_not_asked_but_the_victim_that_is_a_follower_is()
    {
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        var asked = Watch(owner, Cat, Thug, Rat);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Critter(Cat, new LifeFacts(), default), new LifeDamage { HitPoints = 1_000 }).Disposition);
        Assert.IsNotNull(owner.QueryLifeRecord(Cat)!.Value.DeathTimeMs);
        CastAtRat(owner, caster, record);
        CollectionAssert.AreEqual(new[] { Thug, Rat }, asked.Select(a => a.Request.Follower).ToArray(), "the dead Cat left the array; the dying Rat is still in it when the loop runs");
    }

    [TestMethod]
    public void A_kill_by_a_follower_of_the_hero_asks_the_loop_and_one_by_another_npc_does_not()
    {
        var record = Bolt(1000);
        var (led, ledCaster) = Session(record);
        using var _ = led;
        var asked = Watch(led, Thug);
        CastAtRat(led, ledCaster, record, Thug);
        Assert.AreEqual(1, asked.Count);
        Assert.AreEqual(new SessionLeaderKillingRequest(Rat, led.QueryActor().Map.RuntimeMap, Thug), asked[0].Request);

        var (other, otherCaster) = Session(record);
        using var __ = other;
        var none = Watch(other, Cat);
        CastAtRat(other, otherCaster, record, Thug);
        Assert.AreEqual(0, none.Count, "the killer has no PC leader: no award and no loop");
    }

    [TestMethod]
    public void A_hurt_a_script_kill_and_a_death_the_dying_script_stopped_ask_no_leader_killing_script()
    {
        var (hurt, hurtCaster) = Session(Bolt(3));
        using var _ = hurt;
        var hurtAsked = Watch(hurt, Thug);
        CastAtRat(hurt, hurtCaster, Bolt(3));
        Assert.AreEqual(0, hurtAsked.Count, "no death");

        var record = Bolt(1000);
        var (script, scriptCaster) = Session(record);
        using var __ = script;
        var scriptAsked = Watch(script, Thug);
        Assert.IsTrue(scriptCaster.ApplyScriptDamage(Rat, 1000, 0, unresistable: true).Applied);
        Assert.AreEqual(0, scriptAsked.Count, "no source, no PC leader");

        var (stopped, stoppedCaster) = Session(record);
        using var ___ = stopped;
        stopped.DyingScript = _ => false;
        var stoppedAsked = Watch(stopped, Thug);
        CastAtRat(stopped, stoppedCaster, record);
        Assert.AreEqual(0, stoppedAsked.Count, "skip default ends the kill step before its loop");
    }

    [TestMethod]
    public void A_host_that_throws_ends_the_loop_and_the_kill_is_still_committed()
    {
        var record = Bolt(1000);
        var (owner, caster) = Session(record);
        using var _ = owner;
        Watch(owner, Thug, Cat);
        var calls = 0;
        owner.LeaderKillingScript = _ => { calls++; throw new InvalidOperationException("host failure"); };
        CastAtRat(owner, caster, record);
        Assert.AreEqual(1, calls);
        Assert.IsNotNull(owner.QueryLifeRecord(Rat)!.Value.DeathTimeMs);
    }
}
