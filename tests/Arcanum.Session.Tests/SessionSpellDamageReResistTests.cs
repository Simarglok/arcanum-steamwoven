using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1143 (ledger RH1–RH5): the entangle's re-resistance on a hit that is not a weapon's: a spell's Damage component (0x4518D0 → 0x433020) and a native trap's damage (the attack end 0x4B6930 through
// context flag 0x80000) ask an entangled victim's save again; a burn (0x42A720) never does (no hit effect in the original). An invented map (the hero, an NPC caster, a victim NPC and a trap) and invented
// records: the entangle's save modifier is +100 (every d20 is at most the save: it always resists again) or -100 (never).
[TestClass]
public sealed class SessionSpellDamageReResistTests
{
    private const string Npc = "npc-a", Victim = "oid:victim", Trap = "oid:trap";
    private const int Entangling = 51, Hurting = 90;

    private sealed class Map(GameSessionOwner owner, int victimFlags2 = 0, int victimObjectFlags = 0) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, new(7, 5), HitPoints: 10),
            Victim => new(id, SpellTargetObject.Npc, new(8, 5), HitPoints: 40),
            Trap => new(id, 17, new(6, 6)),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity switch
        {
            Npc => new(SessionCritterCombat.From(entity, AttackStateTests.Npc(Thinker())).Combat!, 0, 0, 0, 0, 0),
            Victim => new(SessionCritterCombat.From(entity, AttackStateTests.Npc(Thinker())).Combat!, 0, victimObjectFlags, 0, 0, victimFlags2),
            _ => null,
        };
        private static int[] Thinker() { var stats = AttackStateTests.Stats(); stats[4] = 10; return stats; }
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SpellComponent Part(SpellAction action, SpellComponentType type, SpellComponentData data) => new(action, type, new(0x4, 0, 0, 0, -1), 0, 0, data);

    // An Entangle-like record (ObjFlag entangled at BEGIN, off at END; a maintained spell with a strength save of the given modifier).
    private static SessionSpellRecord Entangle(int modifier) =>
        SessionSpellRecords.Translate(Record(number: Entangling, beginTargets: 0x4, info: SpellInfo.Aggressive, components:
            [Part(SpellAction.Begin, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag, true)),
             Part(SpellAction.End, SpellComponentType.ObjFlag, new ObjectFlagData(SessionSpellCaster.SpellFlagsField, SessionSpellCaster.EntangledSpellFlag, false))])
        with { Cost = 0, MaintainCost = 0, MinimumWillpower = 0, MaintainPeriod = 2, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = 0, ResistModifier = modifier,
            NoStack = 0, DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = SessionSpellCaster.EntangledSpellFlag,
            Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    // A friendly spell whose Damage component deals normal damage of the given amount to the target object at BEGIN.
    private static SessionSpellRecord Hurt(int amount) =>
        SessionSpellRecords.Translate(Record(number: Hurting, beginTargets: 0x4, info: SpellInfo.Friendly, components: [Damage(SpellAction.Begin, 0, amount, amount, 0)])
        with { Cost = 0, MaintainCost = 0, MinimumWillpower = 0, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0,
            Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Setup(int victimFlags2 = 0, int victimObjectFlags = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var records = new[] { Entangle(100), Hurt(5) };
        return (owner, new SessionSpellCaster(owner, n => records.FirstOrDefault(r => r.Data.Number == n), new Map(owner, victimFlags2, victimObjectFlags), new Events()));
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Setup(SessionSpellRecord entangle, SessionSpellRecord hurt)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var records = new[] { entangle, hurt };
        return (owner, new SessionSpellCaster(owner, n => records.FirstOrDefault(r => r.Data.Number == n), new Map(owner), new Events()));
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record, string parent, string target)
    {
        var by = caster.Facts(parent)!;
        var source = caster.Facts(parent == SessionSpellCaster.Hero ? Npc : SessionSpellCaster.Hero)!;
        var to = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, by, source, to, to.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.Act(new(SessionSpellRunActionKind.Begin, run));
        return run;
    }

    private static int HeroHitPoints(GameSessionOwner owner) => owner.QueryHeroLife()!.Status.HitPoints;

    private static bool Asked(SessionSpellCaster caster, int run) => caster.Log.Any(e => e.Run == run && e.Kind is SpellCastEventKind.EntangleHeld or SpellCastEventKind.EntangleBroken);

    [TestMethod]
    public void A_spells_damage_asks_the_entangled_victims_save_again_and_a_made_save_ends_the_run()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
        var before = HeroHitPoints(owner);
        Cast(owner, caster, Hurt(5), Npc, SessionSpellCaster.Hero);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Damaged && e.Object == SessionSpellCaster.Hero), "the damage landed");
        Assert.IsTrue(HeroHitPoints(owner) < before);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken && e.Run == entangle && e.Object == SessionSpellCaster.Hero), "a save of 100 + strength is always made");
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.Resisted && e.Run == entangle));
    }

    [TestMethod]
    public void A_missed_save_after_a_spells_damage_keeps_the_hold()
    {
        var (owner, caster) = Setup(Entangle(-100), Hurt(5));
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(-100), Npc, SessionSpellCaster.Hero);
        Cast(owner, caster, Hurt(5), Npc, SessionSpellCaster.Hero);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleHeld && e.Run == entangle), "asked, and held");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken));
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
    }

    [TestMethod]
    public void A_spell_that_dealt_no_damage_or_hit_a_critter_that_is_not_entangled_asks_nothing()
    {
        var (owner, caster) = Setup(Entangle(100), Hurt(0));
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        Cast(owner, caster, Hurt(0), Npc, SessionSpellCaster.Hero);
        Assert.IsFalse(Asked(caster, entangle), "no damage dealt: the damage effect does not run (flag 0x200000 unset)");
        Assert.IsFalse(caster.SpellDamageHit(SessionSpellCaster.Hero, AttackDamageOf(0, 0, DamageByType.Zero)));
        // A fatigue-only step counts (the flag is set for fatigue damage above 0 too); a poison-only one does not.
        Assert.IsFalse(caster.SpellDamageHit(SessionSpellCaster.Hero, AttackDamageOf(0, 0, new(0, 4, 0, 0, 0))), "poison damage is not damage dealt");
        Assert.IsTrue(caster.SpellDamageHit(SessionSpellCaster.Hero, AttackDamageOf(0, 3, DamageByType.Zero)), "fatigue damage is");
        var (o, c) = Setup();
        using var __ = o;
        Cast(o, c, Hurt(5), Npc, SessionSpellCaster.Hero);
        Assert.IsFalse(c.Log.Any(e => e.Kind is SpellCastEventKind.EntangleHeld or SpellCastEventKind.EntangleBroken), "not entangled: nothing to ask");
    }

    [TestMethod]
    public void A_prevented_step_runs_no_damage_effect()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        Assert.IsFalse(caster.SpellDamageHit(SessionSpellCaster.Hero, AttackDamageOf(5, 0, new(5, 0, 0, 0, 0)) with { Prevented = DamagePrevention.Invulnerable }));
        Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.EntangleHeld or SpellCastEventKind.EntangleBroken));
    }

    [TestMethod]
    public void The_damage_effects_critter_flag_gate_and_a_destroyed_victim_stop_the_hit_effect()
    {
        // Victim NPC flags: critter flags 2 bit 0x100 (0x433020 returns), and object flags 0x2 (off).
        foreach (var (flags2, objectFlags, name) in new[] { (0x100, 0, "flag 0x100"), (0, 0x2, "off"), (0, 0x1, "destroyed") })
        {
            var (owner, caster) = Setup(flags2, objectFlags);
            using var _ = owner;
            var run = Cast(owner, caster, Entangle(100), SessionSpellCaster.Hero, Victim);
            Assert.AreEqual((true, false, false), caster.Holds(Victim), name);
            Assert.IsFalse(caster.SpellDamageHit(Victim, AttackDamageOf(5, 0, new(5, 0, 0, 0, 0))), name);
            Assert.IsFalse(Asked(caster, run), name);
        }
        // The same victim with neither flag is asked.
        var (o, c) = Setup();
        using var __ = o;
        var entangle = Cast(o, c, Entangle(100), SessionSpellCaster.Hero, Victim);
        Assert.IsTrue(c.SpellDamageHit(Victim, AttackDamageOf(5, 0, new(5, 0, 0, 0, 0))));
        Assert.IsTrue(Asked(c, entangle));
    }

    [TestMethod]
    public void A_trap_hit_runs_the_attack_end_even_with_no_positive_slot_and_before_the_damage_step()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        // No positive hit-point slot: the hit effect is called directly (0x4B6930 → 0x459A30); positive: through the damage effect.
        Assert.IsTrue(caster.TrapDamageHit(SessionSpellCaster.Hero, AttackDamageOf(0, 0, DamageByType.Zero)));
        Assert.IsTrue(Asked(caster, entangle));
        // The attack end reads the amounts left after resistance: a fully resisted trap hit calls the hit effect directly, so even a flagged critter is asked.
        var (r, rc) = Setup(victimFlags2: 0x100);
        using var ___ = r;
        var resisted = Cast(r, rc, Entangle(100), SessionSpellCaster.Hero, Victim);
        Assert.IsTrue(rc.TrapDamageHit(Victim, AttackDamageOf(0, 0, new(5, 0, 0, 0, 0)) with { Final = DamageByType.Zero }));
        Assert.IsTrue(Asked(rc, resisted));
        // A flagged critter is skipped by the damage effect only: with a positive slot it is not asked, with none it is.
        foreach (var (amount, asked) in new[] { (5, false), (0, true) })
        {
            var (o, c) = Setup(victimFlags2: 0x100);
            using var __ = o;
            var run = Cast(o, c, Entangle(100), SessionSpellCaster.Hero, Victim);
            Assert.AreEqual(asked, c.TrapDamageHit(Victim, AttackDamageOf(0, 0, new(amount, 0, 0, 0, 0))), $"amount {amount}");
            Assert.AreEqual(asked, Asked(c, run), $"amount {amount}");
        }
    }

    [TestMethod]
    public void A_damage_trap_asks_the_entangled_victims_save_again_through_the_cast_host()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        var firing = new SessionTrapFiring(Trap, SessionSpellCaster.Hero, TrapRules.NativeRun(30001, TrapRules.NonmagicalFlag)!.Value, 6 << 8 | 3, 0);
        var before = HeroHitPoints(owner);
        var result = caster.ApplyTrapEffect(firing);
        Assert.AreEqual(SessionTrapHit.Damaged, result.Victims.Single().Hit);
        Assert.IsTrue(HeroHitPoints(owner) < before);
        Assert.IsTrue(caster.Log.Any(e => e.Kind == SpellCastEventKind.EntangleBroken && e.Run == entangle && e.Object == SessionSpellCaster.Hero));
    }

    [TestMethod]
    public void A_burn_never_asks_the_save_again_the_original_runs_no_hit_effect_for_it()
    {
        // 0x42A720 builds its packet with 0x4B2210, sets no context flag 0x80000 and calls only the damage step 0x4B4390: no attack end, no damage effect, so an entangled burning victim keeps the hold.
        var (owner, caster) = Setup();
        using var _ = owner;
        var entangle = Cast(owner, caster, Entangle(100), Npc, SessionSpellCaster.Hero);
        var before = HeroHitPoints(owner);
        var burn = caster.ApplyBurn(SessionSpellCaster.Hero, FireDamageRules.Damage(false, false), "scenery:1");
        Assert.IsTrue(burn.Applied && burn.HitPointDamage > 0, "the burn hurt");
        Assert.IsTrue(HeroHitPoints(owner) < before);
        Assert.IsFalse(Asked(caster, entangle));
        Assert.AreEqual((true, false, false), caster.Holds(SessionSpellCaster.Hero));
    }

    // A damage packet the way the damage step draws one: the per-type amounts in <paramref name="rolled"/> (rolled and final alike), hit-point and fatigue damage as given.
    private static AttackDamage AttackDamageOf(int hitPoints, int fatigue, DamageByType rolled) =>
        new(DamageRecipient.Target, rolled, rolled, hitPoints, fatigue, 0, DamageMultiplier.None, false, CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None);
}
