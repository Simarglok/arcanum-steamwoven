using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-2371: the payment drains the mana store (field 92) of the worn items first, the session keeps the new store and sets the recharge
// event up (ledger BC10; exe 0x450420, 0x459590). The hero's worn staff is kit:1 on slot 1004 with an own store of 10; records and numbers are invented.
public sealed partial class SessionSpellCasterTests
{
    private const string StoreStaff = "kit:1";

    private sealed class StoreMap(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id == SessionSpellCaster.Hero
            ? new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints) : null;
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) StoreSession(SessionSpellRecord record, int store = 10)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        owner.SelectRules(RuleMode.Steamwoven);
        // The hero wears the starting kit's weapon; its store is the item's own field here.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, StoreStaff, Arcanum.Simulation.Equipment.EquipmentSlot.Weapon,
            EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade), EquipmentStateTests.Drawn, _ => SessionInsertVerdict.Accepted).Disposition);
        return (owner, new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new StoreMap(owner), new Events(),
            id => id == StoreStaff ? new SessionItemSpells(id, SessionSpellCaster.Hero, 5, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, store) : null));
    }

    private static void CastOnce(GameSessionOwner owner, SessionSpellCaster caster, SessionSpellRecord record)
    {
        var run = Start(owner, caster, record);
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Release(run)!.Disposition);
    }

    [TestMethod]
    public void A_cast_drains_the_worn_store_before_fatigue_and_sets_the_recharge_event_up()
    {
        var record = SelfBolt(cost: 7);
        var (owner, caster) = StoreSession(record);
        using var _ = owner;
        var before = Hero(owner);
        Cast(owner, caster, record);
        Assert.AreEqual(3, owner.QueryItemManaStore(StoreStaff), "10 - 7");
        Assert.AreEqual(before.Fatigue, Hero(owner).Fatigue, "the store paid it all: no fatigue");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.FatiguePaid));
        var drained = caster.Log.Single(e => e.Kind == SpellCastEventKind.ManaStoreDrained);
        Assert.AreEqual((StoreStaff, 3, 7), (drained.Object, drained.Value, drained.Detail));
        var recharge = owner.QueryManaRecharges().Single();
        Assert.AreEqual((StoreStaff, 7), (recharge.Entity, recharge.Pending));
        Assert.AreEqual(3, caster.Item(StoreStaff)!.ManaStore, "the item lookup shows the session's copy");

        Cast(owner, caster, record);
        Assert.AreEqual(0, owner.QueryItemManaStore(StoreStaff), "the store is emptied; the rest goes to fatigue");
        Assert.AreEqual(before.Fatigue - 4, Hero(owner).Fatigue, "7 - 3 = 4 points of fatigue");
        Assert.AreEqual((StoreStaff, 10), owner.QueryManaRecharges().Single() is var merged ? (merged.Entity, merged.Pending) : default, "the second drain merged into the first event");
    }

    [TestMethod]
    public void An_empty_store_is_skipped_and_the_fatigue_pays_all_with_no_event()
    {
        var record = SelfBolt(cost: 7);
        var (owner, caster) = StoreSession(record, store: 0);
        using var _ = owner;
        var before = Hero(owner);
        Cast(owner, caster, record);
        Assert.AreEqual(before.Fatigue - 7, Hero(owner).Fatigue);
        Assert.IsNull(owner.QueryItemManaStore(StoreStaff));
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
    }

    [TestMethod]
    public void A_store_larger_than_the_cost_keeps_the_rest_and_the_recharge_takes_the_drained_part()
    {
        var record = SelfBolt(cost: 4);
        var (owner, caster) = StoreSession(record, store: 100);
        using var _ = owner;
        Cast(owner, caster, record);
        Assert.AreEqual(96, owner.QueryItemManaStore(StoreStaff));
        Assert.AreEqual((StoreStaff, 4), owner.QueryManaRecharges().Single() is var e ? (e.Entity, e.Pending) : default);
    }
}
