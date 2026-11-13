using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-1652 (ledger GS5, the copy 0x4EF920 → 0x406010): a monster generator's copy holds fresh copies of the items the generator holds, in the generator's order, with the facts the session keeps for a made item. Invented numbers and a synthetic seed only.</summary>
[TestClass]
public sealed class GeneratorItemCopyTests
{
    private const int Prototype = 28359;
    private const string Generator = "oid:gen-1", Bystander = "oid:smith";
    private static readonly CellCoord Home = new(100, 201);
    private static readonly SessionItemKind Sword = new(SessionItemType.Weapon, 4021, 2003, 32003), Arrows = new(SessionItemType.Ammo, 7058, 2100, 32100), Gold = new(SessionItemType.Gold, 9056, 3200, 33200),
        Bread = new(SessionItemType.Food, 10094, 2500, 32500), Key = new(SessionItemType.Key, 14001, 2600, 32600), Mail = new(SessionItemType.Armor, 8076, 2700, 32700);

    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who) => DropPath.None;
        public bool HasCritter(CellCoord cell) => false;
    }

    private static SessionSeedItem Item(string entity, SessionItemKind kind, int location = -1, int? quantity = null, int weight = 0, int complexity = 0, int? ammoType = null) =>
        new(entity, new(kind, weight, complexity, location, quantity, []) { AmmoType = ammoType, MagicWeight = weight / 2 }, null);

    // The generator holds a worn sword and a worn mail, a pile of 25 coins, 17 arrows (type 0), a bread and a key; a bystander holds one bread of its own.
    private static SessionMapInventorySeed Seed() => new(
        [new(Generator, SessionHolderKind.Npc, false, false, [], ["g-sword", "g-gold", "g-arrows", "g-bread", "g-key", "g-mail"]), new(Bystander, SessionHolderKind.Npc, false, false, [], ["s-bread"])],
        [Item("g-sword", Sword, 1004, weight: 40, complexity: 3), Item("g-gold", Gold, quantity: 25), Item("g-arrows", Arrows, quantity: 17, ammoType: 0), Item("g-bread", Bread, 2), Item("g-key", Key), Item("g-mail", Mail, 1006, weight: 120),
            Item("s-bread", Bread, 0)]);

    private static SessionGeneratorRun Run(int cap = 1, int slot = 5, int budget = 9, int options = GeneratorSettings.ByDay) => new()
    {
        Entity = Generator, Map = Exterior, Cell = Home, Prototype = Prototype, Packed = new GeneratorSettings(options, slot, cap, budget, 0).Pack(), Rate = 3, DropMap = new OpenMap(),
    };

    private static GameSessionOwner Owner(bool attach = true)
    {
        var owner = Create();
        if (attach) Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionHolderInventory View(GameSessionOwner owner, string holder) => owner.QueryMapInventory(Exterior)!.Holders.Single(candidate => candidate.Entity == holder);

    [TestMethod]
    public void A_copy_holds_new_items_of_the_generators_kinds_in_its_order_and_the_generators_list_is_unchanged()
    {
        using var owner = Owner();
        var before = View(owner, Generator).Items.Select(item => item.Entity).ToArray();
        var copy = owner.RunGenerator(owner.Version, Run()).Made.Single();
        var items = View(owner, copy.Entity).Items;
        // The key is not copied (the lists admit no key); every other item is, in the generator's order, as a new made item.
        CollectionAssert.AreEqual(new[] { Sword, Gold, Arrows, Bread, Mail }, items.Select(item => item.Facts.Kind).ToArray());
        Assert.IsTrue(items.All(item => item.Entity.StartsWith("made:", StringComparison.Ordinal)), "new identities");
        Assert.AreEqual(5, items.Select(item => item.Entity).Distinct().Count());
        CollectionAssert.AreEqual(before, View(owner, Generator).Items.Select(item => item.Entity).ToArray(), "the generator keeps its own items");
        Assert.AreEqual(1, View(owner, Bystander).Items.Count, "no other holder is touched");
    }

    [TestMethod]
    public void The_copies_items_keep_the_coins_the_ammunition_the_weight_facts_and_the_worn_slots()
    {
        using var owner = Owner();
        var copy = owner.RunGenerator(owner.Version, Run()).Made.Single();
        var items = View(owner, copy.Entity).Items.ToDictionary(item => item.Facts.Kind.Prototype);
        Assert.AreEqual(25, items[Gold.Prototype].Facts.Quantity);
        Assert.AreEqual((17, 0), (items[Arrows.Prototype].Facts.Quantity, items[Arrows.Prototype].Facts.AmmoType));
        Assert.AreEqual(3, items[Sword.Prototype].Facts.Complexity);
        Assert.AreEqual(new SessionItemWeight(40, 20, 3), owner.QueryItemWeight(items[Sword.Prototype].Entity), "the weight facts the item had");
        Assert.AreEqual(1004, items[Sword.Prototype].Facts.InventoryLocation, "the copy wears what the generator wears");
        Assert.AreEqual(1006, items[Mail.Prototype].Facts.InventoryLocation);
        Assert.AreEqual(-1, items[Bread.Prototype].Facts.InventoryLocation, "an item in the pack stays in the pack");
        var worn = owner.QueryNpcWorn(copy.Entity)!;
        CollectionAssert.AreEqual(new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor }, worn.Worn.Select(item => item.Slot).ToArray());
    }

    [TestMethod]
    public void Every_copy_gets_its_own_items_and_the_ammunition_and_coins_are_not_shared()
    {
        using var owner = Owner();
        var made = owner.RunGenerator(owner.Version, Run(cap: 3, options: GeneratorSettings.ByDay | GeneratorSettings.Group)).Made;
        Assert.AreEqual(3, made.Count);
        var all = made.SelectMany(copy => View(owner, copy.Entity).Items).ToArray();
        Assert.AreEqual(15, all.Select(item => item.Entity).Distinct().Count());
        Assert.AreEqual(3, all.Count(item => item.Facts.Kind == Gold && item.Facts.Quantity == 25));
        Assert.AreEqual(15, owner.QueryNpcInventory(made[0].Entity).Count + owner.QueryNpcInventory(made[1].Entity).Count + owner.QueryNpcInventory(made[2].Entity).Count);
    }

    [TestMethod]
    public void A_generator_the_session_has_no_seed_for_makes_its_copy_with_no_items()
    {
        using var owner = Owner(attach: false);
        var made = owner.RunGenerator(owner.Version, Run()).Made;
        Assert.AreEqual(1, made.Count);
        Assert.AreEqual(0, owner.QueryNpcInventory(made[0].Entity).Count);
    }

    [TestMethod]
    public void A_copy_made_while_the_generator_holds_less_copies_what_it_holds_then()
    {
        using var owner = Owner();
        var generator = View(owner, Generator).Items.Single(item => item.Facts.Kind == Gold);
        var first = owner.RunGenerator(owner.Version, Run(slot: 6)).Made.Single();
        Assert.AreEqual(5, View(owner, first.Entity).Items.Count);
        // The generator's own pile is destroyed (the durability deletion): later copies do not get it.
        Assert.AreEqual(GameSessionFailure.None, owner.RemoveItemFromNpc(owner.Version, Generator, generator.Entity).Failure);
        var second = owner.RunGenerator(owner.Version, Run(slot: 7)).Made.Single();
        Assert.IsFalse(View(owner, second.Entity).Items.Any(item => item.Facts.Kind == Gold));
        Assert.AreEqual(4, View(owner, second.Entity).Items.Count);
    }

    [TestMethod]
    public void The_copied_items_are_saved_and_restored_with_the_session_without_a_new_schema_field()
    {
        using var owner = Owner();
        var copy = owner.RunGenerator(owner.Version, Run()).Made.Single();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var back = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));
        CollectionAssert.AreEqual(owner.QueryNpcInventory(copy.Entity).ToArray(), back.QueryNpcInventory(copy.Entity).ToArray());
        Assert.AreEqual(GameSessionFailure.None, back.AttachMapInventory(Exterior, Seed()).Failure);
        CollectionAssert.AreEqual(View(owner, copy.Entity).Items.Select(item => (item.Entity, item.Facts)).ToArray(), View(back, copy.Entity).Items.Select(item => (item.Entity, item.Facts)).ToArray());
        CollectionAssert.AreEqual(owner.QueryNpcWorn(copy.Entity)!.Worn.ToArray(), back.QueryNpcWorn(copy.Entity)!.Worn.ToArray());
    }

    [TestMethod]
    public void Destroying_the_copy_destroys_its_items_with_it()
    {
        using var owner = Owner();
        var copy = owner.RunGenerator(owner.Version, Run()).Made.Single();
        Assert.AreEqual(5, owner.QueryNpcInventory(copy.Entity).Count);
        Assert.AreEqual(GameSessionFailure.None, owner.DestroyCreatedObject(owner.Version, copy.Entity).Failure);
        Assert.AreEqual(0, owner.QueryNpcInventory(copy.Entity).Count);
        Assert.IsNull(owner.QueryNpcWorn(copy.Entity));
    }
}
