using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-2403 (ledger BC10): a payment writes the mana store of every item a critter wears, as the original does: no seed to attach, no shared record budget, and a second payment of
// one event pass is planned against the store the first left. Records and numbers are invented.
public sealed partial class SessionSpellCasterTests
{
    private const string StoreNpc = "npc:caster", NpcStaff = "npcitem:1";

    // One NPC on the map wearing one item on slot 1004; the session tracks no such item (no seed of its map is attached).
    private sealed class NpcStoreMap(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            StoreNpc => new(id, SpellTargetObject.Npc, new(7, 5), HitPoints: 10),
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
        public SpellCastCritter? Critter(string entity) => entity == StoreNpc ? new(SessionCritterCombat.From(entity, AttackStateTests.Npc(Thinker())).Combat!, 0, 0, 0, 0, 0) : null;
        private static int[] Thinker() { var stats = AttackStateTests.Stats(); stats[4] = 10; return stats; }
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => entity == StoreNpc ? [new(NpcStaff, 1004, false, 0)] : [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, int Run) NpcStoreSession(int store, GameSessionLimits? limits = null)
    {
        var record = SelfBolt();
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        owner.SelectRules(RuleMode.Steamwoven);
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new NpcStoreMap(owner), new Events(),
            id => id == NpcStaff ? new SessionItemSpells(id, StoreNpc, 5, [10000, 10000, 10000, 10000, 10000], 0, 0, 0, store) : null);
        var npc = caster.Facts(StoreNpc)!;
        var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, npc, npc, npc, npc.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return (owner, caster, started.Creation!.Run!.Id);
    }

    // What a run step leaves for the host: a payment planned just now against the payer's state.
    private static SpellRunNote PlannedNote(SessionSpellCaster caster, int run, string payer, int cost) =>
        new(SpellRunNoteKind.Payments, run, Payments: [SpellPaymentRules.Pay(caster.Payer(caster.RunObject(payer)!)!, cost, true, 90, default, caster.Cells)]);

    [TestMethod]
    public void An_npc_payers_worn_store_is_drained_and_recharged_though_the_session_tracks_no_such_item()
    {
        var (owner, caster, run) = NpcStoreSession(store: 10);
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.WriteItemManaStore(owner.Version, NpcStaff, 10, 3).Failure, "the plain write still wants a tracked item");
        caster.Apply([PlannedNote(caster, run, StoreNpc, 7)], id => owner.QuerySpellRuns()[id]);
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ManaStoreNotSaved), "the store is written, not lost");
        Assert.AreEqual(3, owner.QueryItemManaStore(NpcStaff), "10 - 7");
        var recharge = owner.QueryManaRecharges().Single();
        Assert.AreEqual((NpcStaff, 7), (recharge.Entity, recharge.Pending));
        Assert.AreEqual(3, caster.Item(NpcStaff)!.ManaStore, "the item lookup shows the session's copy");
        // The recharge goes on for an item the session does not track: it holds the store.
        Assert.IsTrue(owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 480_000, Arcanum.Simulation.Clock.ClockAdvanceReason.OrdinaryAdvance).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed);
        owner.ProcessEvents(new NoWorld());
        Assert.AreEqual(3 + 7, owner.QueryItemManaStore(NpcStaff), "4 units of 120 s restore 2 × 4 points, at most the 7 pending");
    }

    [TestMethod]
    public void A_full_durability_budget_does_not_lose_the_payments_store()
    {
        var record = SelfBolt(cost: 7);
        var (owner, caster) = StoreSession(record, limits: new GameSessionLimits(maxDurabilityRecords: 1));
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SpendItemCharge(owner.Version, "kit:3", 5).Disposition, "one other record fills the shared budget");
        Cast(owner, caster, record);
        Assert.AreEqual(3, owner.QueryItemManaStore(StoreStaff), "the store has a budget of its own");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ManaStoreNotSaved));
        Assert.AreEqual((StoreStaff, 7), owner.QueryManaRecharges().Single() is var e ? (e.Entity, e.Pending) : default);
    }

    [TestMethod]
    public void The_store_budget_is_its_own_and_an_item_whose_store_is_held_is_always_written()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), new GameSessionLimits(maxManaStores: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, "kit:2", 30, 10).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.WriteItemManaStore(owner.Version, "kit:3", 30, 10).Failure, "a second item's store does not fit");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, "kit:2", 30, 4).Disposition, "a store the session holds is written on");
        Assert.AreEqual(4, owner.QueryItemManaStore("kit:2"));
    }

    // The notes of an event pass are applied after it, but the original pays at once: the second payment sees what the first left, the fatigue part included.
    [TestMethod]
    public void A_second_payment_of_one_pass_drains_what_the_first_left_and_the_rest_goes_to_fatigue()
    {
        var record = SelfBolt(cost: 8);
        var (owner, caster) = StoreSession(record, store: 10);
        using var _ = owner;
        var run = Start(owner, caster, record);
        var before = Hero(owner);
        // Both payments were planned against the store before the pass: 8 of 10 each, no fatigue.
        var notes = new[] { PlannedNote(caster, run, SessionSpellCaster.Hero, 8), PlannedNote(caster, run, SessionSpellCaster.Hero, 8) };
        Assert.IsTrue(notes.All(n => n.Payments![0].FatigueDamageAdded == 0 && n.Payments![0].Drains.Single().Drained == 8));
        caster.ApplyPass([[notes[0]], [notes[1]]], id => owner.QuerySpellRuns()[id]);
        Assert.AreEqual(0, owner.QueryItemManaStore(StoreStaff), "8, then the 2 left (two firings of one pass)");
        Assert.AreEqual(before.Fatigue - 6, Hero(owner).Fatigue, "the second payment's other 6 points are fatigue, as when paid at once");
        var drains = caster.Log.Where(e => e.Kind == SpellCastEventKind.ManaStoreDrained).Select(e => (e.Value, e.Detail)).ToArray();
        CollectionAssert.AreEqual(new[] { (2, 8), (0, 2) }, drains);
        Assert.AreEqual((StoreStaff, 10), owner.QueryManaRecharges().Single() is var e ? (e.Entity, e.Pending) : default, "one merged event for the 10 drained points");
    }

    // The first payment of a payer keeps the plan its step made: the original pays at the stage, before the step's components change anything.
    [TestMethod]
    public void The_first_payment_of_a_payer_is_written_as_planned_whatever_the_store_became_since()
    {
        var record = SelfBolt(cost: 8);
        var (owner, caster) = StoreSession(record, store: 10);
        using var _ = owner;
        var run = Start(owner, caster, record);
        var note = PlannedNote(caster, run, SessionSpellCaster.Hero, 8);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, StoreStaff, 10, 4).Disposition, "a component of the step lowered the store");
        caster.Apply([note], id => owner.QuerySpellRuns()[id]);
        Assert.AreEqual(2, owner.QueryItemManaStore(StoreStaff), "the plan's store left (10 - 8)");
    }

    // The second payment of an NPC payer in one pass, as the hero's.
    [TestMethod]
    public void An_npc_payers_second_payment_of_one_pass_drains_what_the_first_left()
    {
        var (owner, caster, run) = NpcStoreSession(store: 10);
        using var _ = owner;
        var before = owner.QueryLife(caster.LifeTarget(StoreNpc)!).View!.Status.Fatigue;
        var notes = new[] { PlannedNote(caster, run, StoreNpc, 8), PlannedNote(caster, run, StoreNpc, 8) };
        caster.ApplyPass([[notes[0]], [notes[1]]], id => owner.QuerySpellRuns()[id]);
        Assert.AreEqual(0, owner.QueryItemManaStore(NpcStaff));
        Assert.AreEqual(before - 6, owner.QueryLife(caster.LifeTarget(StoreNpc)!).View!.Status.Fatigue, "6 points of the second payment are fatigue");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ManaStoreNotSaved));
    }
}
