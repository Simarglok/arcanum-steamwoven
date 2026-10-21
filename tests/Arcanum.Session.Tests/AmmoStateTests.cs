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

    [TestMethod]
    public void The_npc_crafter_spends_one_unit_and_its_ammunition_product_tracks_its_seed_stack()
    {
        static Arcanum.Data.Technology.SchematicAlternatives Alternatives(params int[] bases) => new(Array.AsReadOnly(bases), bases.Length);
        static Arcanum.Data.Technology.SchematicRecord Schematic(int first, int second, int product, int quantity) => new()
        {
            Number = 2000, Kind = Arcanum.Data.Technology.SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
            Component1 = Alternatives(first), Component2 = Alternatives(second), Product = Alternatives(product), Quantity = quantity,
            MissingLines = Array.AsReadOnly(new bool[7]),
        };
        var seed = new SessionMapInventorySeed([new("smith", SessionHolderKind.Npc, false, false, [], ["smith-cells", "smith-bullets"])],
            [new("smith-cells", Ammo(CellPrototype, 2, 5), null), new("smith-bullets", Ammo(BulletPrototype, 1, 1), null)]);
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed).Disposition);
        var crafter = new SessionNpcCrafter(new("smith", new(Arcanum.Simulation.Character.CharacterRace.Human, false, [8, 8, 8, 8, 8, 8, 8, 8], 0, 0, 1, 0,
            [.. new int[12]], [.. new int[4]], [.. new int[16]], [0, 0, 0, 0, 0, 0, 0, 0], 1, 2, 0, 0), 0), Arcanum.Simulation.Character.AdvancementModifiers.None);
        static Arcanum.Simulation.Technology.ComponentFacts? Facts(int prototype) => new(0, 3);
        static SessionItemKind? Kinds(int prototype) => new(SessionItemType.Ammo, prototype, 100, 200);
        static (int, int)? ProductAmmo(int prototype) => prototype == CellPrototype ? (2, 10) : (1, 10);
        SessionCraftLookup inBag = _ => new SessionCraftFacts(10);
        // Cells ×1 from cells and bullets: the seed cells become a tracked stack (5 − 1 + 10), the last bullet goes.
        var made = owner.AssembleSchematicForNpc(owner.Version, crafter, Schematic(CellPrototype - 20, BulletPrototype - 20, CellPrototype - 20, 1), inBag, Facts,
            Kinds, Accept, ProductAmmo);
        Assert.AreEqual((GameSessionDisposition.Changed, "smith-cells"), (made.Disposition, made.Made.Single()));
        Assert.AreEqual(new SessionAmmoRecord("smith-cells", 2, 14), owner.QueryAmmo("smith-cells"));
        Assert.IsTrue(owner.IsItemDestroyed("smith-bullets"));
        // Bullets ×2 without a bullet stack and the cells component: a new made stack of 20.
        Assert.AreEqual(GameSessionFailure.AssemblyRefused, owner.AssembleSchematicForNpc(owner.Version, crafter, Schematic(CellPrototype - 20, BulletPrototype - 20,
            BulletPrototype - 20, 2), inBag, Facts, Kinds, Accept, ProductAmmo).Failure, "no bullets left for component 2");
    }

    [TestMethod]
    public void The_record_budget_refuses_a_new_stack_before_any_effect()
    {
        using var owner = CreateWith(HalfElf(), new GameSessionLimits(maxAmmoRecords: 1));
        owner.AttachMapInventory(Exterior, Seed());
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.PickUpItem(owner.Version, "bullets", Accept).Failure, "a second stack");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.SpendAmmo(owner.Version, new SessionAmmoHolder("smith"), 2, 1).Failure, "an untracked seed stack");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AddItemToNpc(owner.Version, "smith", new(SessionItemType.Ammo, BulletPrototype, 100, 200), Accept, (1, 2)).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells-b", Accept).Disposition, "a merge adds no record");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        // A refused insert check on a merge changes nothing.
        var before = owner.QueryAmmo("cells-a");
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.LootItem(owner.Version, "corpse", "corpse-cells", _ => SessionInsertVerdict.TooHeavy).Failure);
        Assert.AreEqual((before, false), (owner.QueryAmmo("cells-a"), owner.IsItemDestroyed("corpse-cells")));
    }

    [TestMethod]
    public void A_made_stack_looted_from_an_npc_merges_or_joins_the_made_items_and_its_check_sees_the_units()
    {
        using var owner = Hero(new SessionMapInventorySeed([new("corpse", SessionHolderKind.Npc, true, false, [], [])],
            [new("cells-a", Ammo(CellPrototype, 2, 10), new CellCoord(101, 200))]));
        var cells = owner.AddItemToNpc(owner.Version, "corpse", new(SessionItemType.Ammo, CellPrototype, 100, 200), Accept, (2, 6)).Item!;
        var bullets = owner.AddItemToNpc(owner.Version, "corpse", new(SessionItemType.Ammo, BulletPrototype, 100, 200), Accept, (1, 3)).Item!;
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        SessionInventoryItem? seen = null;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", cells, item => { seen = item; return SessionInsertVerdict.Accepted; }).Disposition);
        Assert.AreEqual((6, 2), (seen!.Facts.Quantity, seen.Facts.AmmoType), "the insert check sees the stack's units");
        Assert.AreEqual(16, owner.QueryAmmo("cells-a")!.Value.Units);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", bullets, Accept).Disposition);
        Assert.AreEqual(bullets, owner.QueryMadeItems().Single().Entity, "no bullet stack held: it joins the made items");
        Assert.AreEqual(new SessionAmmoRecord(bullets, 1, 3), owner.QueryHeroAmmo().Last());
        Assert.AreEqual(16, owner.HeroChooser().Ammunition[2], "the best-items choice sees the held ammunition");
        Assert.AreEqual(3, owner.HeroChooser().Ammunition[1]);
    }

    [TestMethod]
    public void Restore_and_decode_refuse_malformed_stacks()
    {
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        owner.PickUpItem(owner.Version, "bullets", Accept);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string rows = "{\"entity\":\"bullets\",\"ammoType\":1,\"units\":7},{\"entity\":\"cells-a\",\"ammoType\":2,\"units\":10}";
        StringAssert.Contains(text, rows);
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(text.Replace(rows,
            "{\"entity\":\"cells-a\",\"ammoType\":2,\"units\":10},{\"entity\":\"bullets\",\"ammoType\":1,\"units\":7}")).Failure, "unsorted");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(text.Replace("\"ammoType\":1", "\"ammoType\":4")).Failure, "type 4");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(text.Replace("\"units\":7", "\"units\":-1")).Failure, "negative units");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!,
            RestoreOptions(new GameSessionLimits(maxAmmoRecords: 1))).Failure, "over the caller's record limit");
        owner.SpendAmmo(owner.Version, SessionAmmoHolder.Hero, 1, 7);
        var spent = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(spent, "\"destroyed\":[\"bullets\"]");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(spent.Replace("\"ammo\":[",
            "\"ammo\":[{\"entity\":\"bullets\",\"ammoType\":1,\"units\":1},")).Snapshot!, RestoreOptions()).Failure, "a deleted stack");
    }

    [TestMethod]
    public void An_item_payer_pays_by_the_heros_aptitude_and_a_payer_mismatch_is_refused()
    {
        using var owner = Hero();
        owner.PickUpItem(owner.Version, "cells-a", Accept);
        var record = new SpellPaymentRecord(ChargesFromCells: true, MaintainPeriod: 0, DurationSeconds: 0);
        var device = owner.HeroCellSource(itemComplexity: -50)!;
        var aptitude = owner.QueryHeroItemCellPayer(-50, null)!.HolderAptitude;
        var need = Arcanum.Simulation.Technology.ChargeCellRules.Need(owner.QueryHeroItemCellPayer(-50, null)!, 3, owner.Rules.Technology)!.Value;
        Assert.AreEqual(SpellPaymentStatus.Paid, SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Item), 3, true, 147, record, device).Status);
        Assert.AreEqual(10 - need, owner.QueryHeroAmmo().Single().Units, $"aptitude {aptitude}");
        var units = owner.QueryHeroAmmo().Single().Units;
        Assert.AreEqual(SpellPaymentStatus.CellsRefused, SpellPaymentRules.Pay(new SpellPayer(SpellPayerKind.Critter, CurrentFatigue: 50), 1, true, 147, record, device).Status,
            "a critter payment through an item's source");
        Assert.AreEqual(units, owner.QueryHeroAmmo().Single().Units);
    }
}
