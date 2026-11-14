using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-RENDER-1794 (ledger ER5, OH2): the blood eye candy of a hit. The cast host's damage effect (0x433020) asks the animation list's entry that HitGoreRules picks (a HitGore log entry the viewer plays), for a weapon's attack end
// (0x4B6930: kind 1 + the largest positive slot, none for a critical hit), a spell's Damage component and a trap's damage. An invented map of the hero and one victim NPC whose flags the tests set.
[TestClass]
public sealed class SessionHitGoreTests
{
    private const string Victim = "oid:victim";

    private sealed class Map(int objectFlags, int spellFlags, int critterFlags, int critterFlags2) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: 40),
            Victim => new(id, SpellTargetObject.Npc, new(8, 5), HitPoints: 40),
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
        public SpellCastCritter? Critter(string entity) => entity == Victim
            ? new(SessionCritterCombat.From(entity, AttackStateTests.Npc(AttackStateTests.Stats())).Combat!, 0, objectFlags, spellFlags, critterFlags, critterFlags2) : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Setup(int objectFlags = 0, int spellFlags = 0, int critterFlags = 0, int critterFlags2 = 0, bool filter = false)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var caster = new SessionSpellCaster(owner, _ => null, new Map(objectFlags, spellFlags, critterFlags, critterFlags2), new Events()) { ViolenceFilter = () => filter };
        return (owner, caster);
    }

    private static AttackDamage Damage(int hitPoints, DamageByType final) =>
        new(DamageRecipient.Target, final, final, hitPoints, 0, 0, DamageMultiplier.None, false, CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None);

    private static int[] Asked(SessionSpellCaster caster, string victim = Victim) => caster.Log.Where(e => e.Kind == SpellCastEventKind.HitGore && e.Object == victim)
        .Select(e => EyeCandyEntryRef.ListOf(e.Value) == EyeCandyList.Anim ? EyeCandyEntryRef.IndexOf(e.Value) : -1).ToArray();

    [TestMethod]
    public void A_weapon_hit_asks_the_blood_of_its_largest_damage_type_and_the_damage_tier()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        caster.AttackEnd(Victim, Damage(3, new(3, 0, 0, 0, 0)), false);
        caster.AttackEnd(Victim, Damage(8, new(8, 0, 0, 0, 0)), false);
        caster.AttackEnd(Victim, Damage(12, new(12, 0, 0, 0, 0)), false);
        caster.AttackEnd(Victim, Damage(20, new(20, 0, 0, 0, 0)), false);
        // Normal blood (index 0) up to 5 and the tiers 8, 9 and 10 above; another type starts at its own number and keeps it whatever the damage; a tie keeps the normal one, which takes the tier again.
        caster.AttackEnd(Victim, Damage(20, new(2, 20, 0, 0, 0)), false);
        caster.AttackEnd(Victim, Damage(20, new(2, 3, 5, 0, 0)), false);
        caster.AttackEnd(Victim, Damage(20, new(2, 3, 5, 9, 0)), false);
        caster.AttackEnd(Victim, Damage(20, new(4, 4, 0, 0, 0)), false);
        CollectionAssert.AreEqual(new[] { 0, 8, 9, 10, 1, 2, 3, 10 }, Asked(caster).ToArray());
        Assert.IsTrue(caster.Log.Where(e => e.Kind == SpellCastEventKind.HitGore).All(e => e.Detail == HitGoreRules.PlayLimit && e.Run == -1), "the add's limit of 3 and no run");
    }

    [TestMethod]
    public void A_critical_hit_and_a_hit_with_no_positive_slot_run_the_hit_effect_without_blood()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        // The packet's critical flag makes the kind 6 but skips the slot loop, so the test of the largest slot sends the hit straight to the hit effect (0x4B69BA): index 5 is never asked for.
        caster.AttackEnd(Victim, Damage(30, new(30, 0, 0, 0, 0)), true);
        caster.AttackEnd(Victim, Damage(0, DamageByType.Zero), false);
        caster.AttackEnd(Victim, null, false);
        Assert.AreEqual(0, Asked(caster).Length);
    }

    [TestMethod]
    public void The_violence_filter_makes_the_clean_effect_whatever_the_damage_and_the_flags_pick_the_others()
    {
        foreach (var damage in new[] { 3, 8, 20 })
        {
            var (o, c) = Setup(filter: true);
            using var _ = o;
            c.AttackEnd(Victim, Damage(damage, new(damage, 0, 0, 0, 0)), false);
            CollectionAssert.AreEqual(new[] { HitGoreRules.CleanIndex }, Asked(c).ToArray(), $"damage {damage}");
        }
        var (undead, u) = Setup(critterFlags: (int)BloodPoolRules.UndeadFlag);
        using var _u = undead;
        u.AttackEnd(Victim, Damage(20, new(20, 0, 0, 0, 0)), false);
        var (stoned, s) = Setup(spellFlags: unchecked((int)HitGoreRules.StonedSpellFlag));
        using var _s = stoned;
        s.AttackEnd(Victim, Damage(20, new(20, 0, 0, 0, 0)), false);
        CollectionAssert.AreEqual(new[] { HitGoreRules.UndeadIndex }, Asked(u).ToArray(), "critter flag 0x4");
        CollectionAssert.AreEqual(new[] { HitGoreRules.CleanIndex }, Asked(s).ToArray(), "a stoned critter");
    }

    [TestMethod]
    public void A_critter_that_forbids_blood_or_is_destroyed_or_off_gets_neither_the_blood_nor_the_hit_effect()
    {
        foreach (var (objectFlags, flags2) in new[] { (0, SessionSpellCaster.NoDamageEffectCritterFlag2), (SpellTargetObject.Destroyed, 0), (SpellTargetObject.Off, 0) })
        {
            var (o, c) = Setup(objectFlags: objectFlags, critterFlags2: flags2);
            using var _ = o;
            c.AttackEnd(Victim, Damage(20, new(20, 0, 0, 0, 0)), false);
            Assert.AreEqual(0, Asked(c).Length, $"object flags {objectFlags}, critter flags 2 {flags2}");
        }
    }

    [TestMethod]
    public void A_spells_damage_and_a_traps_damage_ask_the_blood_too()
    {
        var (owner, caster) = Setup();
        using var _ = owner;
        caster.SpellDamageHit(Victim, Damage(12, new(12, 0, 0, 0, 0)));
        caster.TrapDamageHit(Victim, Damage(4, new(0, 0, 4, 0, 0)));
        caster.SpellDamageHit(Victim, Damage(0, DamageByType.Zero));
        // A native trap's attack end runs inside the damage step before the packet's hit-point field is written (0): its normal blood is index 0 whatever the damage, never a tier.
        caster.TrapDamageHit(Victim, Damage(12, new(12, 0, 0, 0, 0)));
        CollectionAssert.AreEqual(new[] { 9, 2, 0 }, Asked(caster).ToArray(), "a spell is kind 1 (tier 9 for 12), a trap's electrical slot kind 3, a spell that dealt nothing asks none, a trap's normal damage has no tier");
    }
}
