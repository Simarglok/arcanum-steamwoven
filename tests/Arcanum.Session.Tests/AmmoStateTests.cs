using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-066: ammunition stacks in the session (ledger AM rows). Two ground stacks of cells and one of bullets, a corpse and a
// living NPC carrying cells; entities, prototypes and numbers are invented (the cell prototype follows the installed 7060).
[TestClass]
public sealed class AmmoStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private const int CellPrototype = 7060, BulletPrototype = 7059;

    private static SessionItemFacts Ammo(int prototype, int type, int units) =>
        new(new(SessionItemType.Ammo, prototype, 100, 200), 1, 0, 0, units, []) { AmmoType = type };

    private static SessionMapInventorySeed Seed(int groundUnits = 10) => new(
        [
            new("corpse", SessionHolderKind.Npc, true, false, [], ["corpse-cells"]),
            new("smith", SessionHolderKind.Npc, false, false, [], ["smith-cells"]),
        ],
        [
            new("cells-a", Ammo(CellPrototype, 2, groundUnits), new CellCoord(101, 200)),
            new("cells-b", Ammo(CellPrototype, 2, 4), new CellCoord(102, 200)),
            new("bullets", Ammo(BulletPrototype, 1, 7), new CellCoord(103, 200)),
            new("corpse-cells", Ammo(CellPrototype, 2, 5), null),
            new("smith-cells", Ammo(CellPrototype, 2, 5), null),
        ]);

    private static GameSessionOwner Hero(SessionMapInventorySeed? seed = null)
    {
        var owner = CreateWith(HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed ?? Seed()).Disposition);
        return owner;
    }

    [TestMethod]
    public void A_stack_merges_into_the_held_stack_of_its_prototype_and_the_inserted_item_goes()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells-a", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("cells-a", 2, 10) }, owner.QueryHeroAmmo().ToArray(), "the first stack enters the list");
        var looted = owner.LootItem(owner.Version, "corpse", "corpse-cells", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, "corpse-cells"), (looted.Disposition, looted.Item));
        Assert.AreEqual(new SessionAmmoRecord("cells-a", 2, 15), owner.QueryHeroAmmo().Single());
        Assert.IsTrue(owner.IsItemDestroyed("corpse-cells"), "the inserted stack is destroyed (0x466640)");
        Assert.AreEqual(1, owner.QueryPcInventory().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "bullets", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("cells-a", 2, 15), new SessionAmmoRecord("bullets", 1, 7) }, owner.QueryHeroAmmo().ToArray(),
            "another prototype is another stack");
        Assert.AreEqual(new SessionAmmoRecord("smith-cells", 2, 5), owner.QueryAmmo("smith-cells"), "a seed stack's units");
        Assert.IsNull(owner.QueryAmmo("cells-a-none"));
    }

    [TestMethod]
    public void Spending_takes_units_and_removes_the_stack_at_its_last_unit()
    {
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        var spent = owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, 6);
        Assert.AreEqual((GameSessionDisposition.Changed, "cells-a", 4, false), (spent.Disposition, spent.Stack, spent.UnitsAfter, spent.Removed));
        Assert.AreEqual((GameSessionDisposition.NoChange, 4), (owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, 0).Disposition, owner.QueryHeroAmmo().Single().Units));
        var revision = owner.Version.Revision;
        var shortOf = owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, 5);
        Assert.AreEqual((GameSessionFailure.AmmoShort, 4, revision), (shortOf.Failure, shortOf.UnitsAfter, owner.Version.Revision), "too few: nothing spent");
        var last = owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, 4);
        Assert.AreEqual((true, 0), (last.Removed, owner.QueryHeroAmmo().Count));
        Assert.IsTrue(owner.IsItemDestroyed("cells-a"));
        Assert.AreEqual(GameSessionFailure.AmmoShort, owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, 1).Failure, "no stack");
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 2, -1).Failure, "a negative count");
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 4, 1).Failure, "a type out of 0..3");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SpendAmmo(new GameSessionVersion(owner.Identity, 0), SessionAmmoHolder.Hero, 2, 1).Failure);
        using var heroless = Create();
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, heroless.SpendAmmo(heroless.Version, SessionAmmoHolder.Hero, 2, 1).Failure);
    }

    [TestMethod]
    public void An_npc_spends_from_its_list_and_its_view_shows_the_units()
    {
        using var owner = Hero();
        var spent = owner.SpendAmmo(owner.Version, new SessionAmmoHolder("smith"), 2, 2);
        Assert.AreEqual((GameSessionDisposition.Changed, "smith-cells", 3), (spent.Disposition, spent.Stack, spent.UnitsAfter));
        Assert.AreEqual(3, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "smith").Items.Single().Facts.Quantity);
        Assert.IsTrue(owner.SpendAmmo(owner.Version, new SessionAmmoHolder("smith"), 2, 3).Removed);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "smith").Items.Count);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.SpendAmmo(owner.Version, new SessionAmmoHolder("nobody"), 2, 1).Failure);
        // An NPC receives cells: they join its stack of the prototype.
        using var other = Hero();
        var added = other.AddItemToNpc(other.Version, "smith", new(SessionItemType.Ammo, CellPrototype, 100, 200), Accept, (2, 6));
        Assert.AreEqual(("smith-cells", 11), (added.Item, other.QueryAmmo("smith-cells")!.Value.Units));
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, other.AddItemToNpc(other.Version, "smith", new(SessionItemType.Ammo, CellPrototype, 100, 200), Accept).Failure);
        var bullets = other.AddItemToNpc(other.Version, "smith", new(SessionItemType.Ammo, BulletPrototype, 100, 200), Accept, (1, 3));
        Assert.AreEqual(new SessionAmmoRecord(bullets.Item!, 1, 3), other.QueryAmmo(bullets.Item!), "a new stack when the NPC holds none");
    }

    [TestMethod]
    public void Overflowing_stacks_and_bad_seed_ammunition_are_refused()
    {
        using var owner = Hero(Seed(groundUnits: int.MaxValue));
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.PickUpItem(owner.Version, "cells-b", Accept).Failure, "the sum does not fit 32 bits (both settings)");
        Assert.AreEqual(revision, owner.Version.Revision);
        using var fresh = CreateWith(HalfElf());
        var noType = new SessionMapInventorySeed([], [new("x", new(new(SessionItemType.Ammo, 7060, 1, 2), 1, 0, 0, 5, []), new CellCoord(1, 1))]);
        Assert.AreEqual(GameSessionDisposition.Rejected, fresh.AttachMapInventory(Exterior, noType).Disposition, "ammunition without its type");
        var badType = new SessionMapInventorySeed([], [new("x", Ammo(7060, 4, 5), new CellCoord(1, 1))]);
        Assert.AreEqual(GameSessionDisposition.Rejected, fresh.AttachMapInventory(Exterior, badType).Disposition, "a type out of the original's tables");
    }

    [TestMethod]
    public void The_stacks_survive_the_snapshot_and_a_placed_stack_merges_when_picked_up()
    {
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        owner.PickUpItem(owner.Version, "bullets", Accept);
        owner.SpendAmmo(owner.Version, new SessionAmmoHolder("smith"), 2, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"ammo\":[{\"entity\":\"bullets\",\"ammoType\":1,\"units\":7},{\"entity\":\"cells-a\",\"ammoType\":2,\"units\":10}," +
            "{\"entity\":\"smith-cells\",\"ammoType\":2,\"units\":4}]");
        using var restored = GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!, RestoreOptions()).Owner!;
        CollectionAssert.AreEqual(owner.QueryHeroAmmo().ToArray(), restored.QueryHeroAmmo().ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        Assert.AreEqual(4, restored.QueryAmmo("smith-cells")!.Value.Units);
        // A held stack placed on the ground keeps its units and merges back into a held stack of its prototype.
        restored.PlaceHeroItem("bullets", new CellCoord(110, 200));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "cells-b", Accept).Disposition, "cells-b merges into cells-a");
        Assert.AreEqual(14, restored.QueryAmmo("cells-a")!.Value.Units);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "bullets", Accept).Disposition, "the placed bullets come back");
        Assert.AreEqual(new SessionAmmoRecord("bullets", 1, 7), restored.QueryHeroAmmo().Last());
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(
            text.Replace("{\"entity\":\"bullets\",\"ammoType\":1,\"units\":7},", "")).Snapshot!, RestoreOptions()).Failure, "a held stack without its record");
    }

    [TestMethod]
    public void The_heros_cell_source_spends_an_applied_payment_and_never_an_affordability_check()
    {
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        var cells = owner.HeroCellSource()!;
        var record = new SpellPaymentRecord(ChargesFromCells: true, MaintainPeriod: 0, DurationSeconds: 0);
        var payer = new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50);
        var revision = owner.Version.Revision;
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(payer, 7, false, 168, record, cells).Status);
        Assert.AreEqual((revision, 10), (owner.Version.Revision, owner.QueryHeroAmmo().Single().Units), "an affordability check writes nothing");
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(payer, 7, true, 168, record, cells).Status);
        Assert.AreEqual(3, owner.QueryHeroAmmo().Single().Units);
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(payer, 7, true, 168, record, cells).Status);
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(payer, 3, true, 168, record, cells).Status);
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count, "the last cells remove the stack");
        using var heroless = Create();
        Assert.IsNull(heroless.HeroCellSource());
    }

    [TestMethod]
    public void Assembly_spends_one_unit_of_an_ammunition_component_and_merges_an_ammunition_product()
    {
        static Arcanum.Data.Technology.SchematicAlternatives Alternatives(params int[] bases) => new(Array.AsReadOnly(bases), bases.Length);
        static Arcanum.Data.Technology.SchematicRecord Schematic(int first, int second, int product, int quantity) => new()
        {
            Number = 2000, Kind = Arcanum.Data.Technology.SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
            Component1 = Alternatives(first), Component2 = Alternatives(second), Product = Alternatives(product), Quantity = quantity,
            MissingLines = Array.AsReadOnly(new bool[7]),
        };
        static Arcanum.Simulation.Technology.ComponentFacts? Facts(int prototype) => prototype is CellPrototype or BulletPrototype ? new(0, 3) : null;
        static SessionItemKind? Kinds(int prototype) => new(SessionItemType.Ammo, prototype, 100, 200);
        static (int, int)? ProductAmmo(int prototype) => prototype == CellPrototype ? (2, 10) : (1, 10);
        SessionCraftLookup inBag = _ => new SessionCraftFacts(10);
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        owner.PickUpItem(owner.Version, "bullets", Accept);
        // Cells and bullets make cells ×2: one unit of each component goes, the products join the held cells (10 − 1 + 2 × 10).
        var made = owner.AssembleSchematic(owner.Version, Schematic(CellPrototype - 20, BulletPrototype - 20, CellPrototype - 20, 2), inBag, Facts, Kinds, Accept, ProductAmmo);
        Assert.AreEqual((GameSessionDisposition.Changed, "cells-a"), (made.Disposition, made.Made.Single()));
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("cells-a", 2, 29), new SessionAmmoRecord("bullets", 1, 6) }, owner.QueryHeroAmmo().ToArray());
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.AssembleSchematic(owner.Version, Schematic(CellPrototype - 20, BulletPrototype - 20,
            CellPrototype - 20, 1), inBag, Facts, Kinds, Accept).Failure, "an ammunition product needs its ammunition facts");
        // With the bullets down to one unit, bullets ×1 from cells and bullets: the bullet stack goes and a new one is made.
        owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 1, 5);
        var fresh = owner.AssembleSchematic(owner.Version, Schematic(CellPrototype - 20, BulletPrototype - 20, BulletPrototype - 20, 1), inBag, Facts, Kinds, Accept, ProductAmmo);
        Assert.AreEqual(GameSessionDisposition.Changed, fresh.Disposition);
        Assert.IsTrue(owner.IsItemDestroyed("bullets"));
        CollectionAssert.AreEqual(new[] { new SessionAmmoRecord("cells-a", 2, 28), new SessionAmmoRecord(fresh.Made.Single(), 1, 10) }, owner.QueryHeroAmmo().ToArray());
    }
}
