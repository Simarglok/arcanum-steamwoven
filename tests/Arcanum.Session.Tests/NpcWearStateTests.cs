using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-103: an NPC wields its best items at the end of its regeneration (0x463E20 → 0x4654F0, ledger RK15), and the session keeps
// what it wears (schema 57). A synthetic seed: the guard (a pile of 10 coins, a worn club, a persistent worn ring, a persistent spare
// helmet in its pack) and its chest; the set creates a sword and a coat. Entities and numbers are invented.
[TestClass]
public sealed class NpcWearStateTests
{
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Sword = new(SessionItemType.Weapon, 4021, 2003, 32003);
    private static readonly SessionItemKind Coat = new(SessionItemType.Armor, 4030, 2010, 32010);
    private static readonly SessionItemKind Bow = new(SessionItemType.Weapon, 4040, 2020, 32020);
    private const int GuardSource = 5, ChestSource = 7;

    private static EquipmentItem Weapon(int min, int max, CombatSkill skill = CombatSkill.Melee) => new()
    {
        Kind = EquipmentItemKind.Weapon,
        Weapon = new EquipmentWeapon { Skill = skill, Damage = DamageRanges.None with { Normal = new(min, max) } },
    };

    private static EquipmentItem Armour(ArmorCoverage coverage) => new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = coverage, Sizes = ArmorSizes.Medium, AcAdjustment = 5 },
    };

    private class World : ISessionRestockWorld
    {
        public Dictionary<string, RestockHolder> Facts { get; } = new()
        {
            ["guard"] = new(RestockHolderKind.Npc, GuardSource, false, false, 0, false),
            ["chest"] = new(RestockHolderKind.Container, ChestSource, false, false, 0x1, false),
        };
        public HashSet<string> Kept { get; } = ["ring", "helmet"];
        public Dictionary<int, SessionRestockSet> Sets { get; } = new()
        {
            [GuardSource] = new(SessionRestockSetStatus.Read, new(25, 25, [new(100, 1), new(100, 2)])),
            [ChestSource] = new(SessionRestockSetStatus.Read, new(40, 40, [new(100, 1)])),
        };
        // The wear facts by kind (a made item) or entity (a seed item); a missing one is worn nowhere.
        public Dictionary<string, SessionWearableItem?> Wear { get; } = new()
        {
            ["club"] = new(new(SessionItemType.Weapon, 1000 + 2001, 2001, 32001), [], false, Weapon(1, 4)) { Worth = 5 },
            ["ring"] = new(new(SessionItemType.Armor, 1000 + 2805, 2805, 32805), [], false, Armour(ArmorCoverage.Ring)) { Worth = 40 },
            ["helmet"] = new(new(SessionItemType.Armor, 1000 + 2806, 2806, 32806), [], false, Armour(ArmorCoverage.Helmet)) { Worth = 30 },
            [Sword.Prototype.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new(Sword, [], false, Weapon(10, 30)) { Worth = 100 },
            [Coat.Prototype.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new(Coat, [], false, Armour(ArmorCoverage.Torso)) { Worth = 60 },
            [Bow.Prototype.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new(Bow, [], false, Weapon(50, 90, CombatSkill.Bow)) { Worth = 300 },
        };
        public bool Wields { get; set; } = true;
        public int Lookups { get; private set; }
        public SessionInsertCheck Insert { get; set; } = _ => SessionInsertVerdict.Accepted;

        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => Facts.TryGetValue(entity, out var facts) ? facts : null;
        public string? RestockMerchant(SessionMapId map, string container) => "guard";
        public bool Persistent(SessionMapId map, string item) => Kept.Contains(item);
        public SessionRestockSet RestockSet(int source) => Sets[source];
        public SessionRestockItem? RestockItem(int basicPrototype) => basicPrototype switch { 1 => new(Sword), 2 => new(Coat), 3 => new(Bow), _ => null };
        public SessionItemKind RestockPileKind => GoldPile;
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => Insert;
        public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => !Wields ? null
            : new(new EquipmentWearer(), new EquipmentChooser { MeleeLevel = 10, BowLevel = 10, IsPc = true }, item =>
            {
                Lookups++;
                var key = SessionMadeItem.Number(item.Entity) is null ? item.Entity : item.Facts.Kind.Prototype.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Wear.GetValueOrDefault(key);
            }, (_, _) => true);
    }

    private static SessionMapInventorySeed Seed() => new(
        [
            new("guard", SessionHolderKind.Npc, false, true, [], ["purse", "club", "ring", "helmet"]),
            new("chest", SessionHolderKind.Container, false, false, [], ["gold-a"]),
        ],
        [
            new("purse", Facts(SessionItemType.Gold, 3200, quantity: 10), null),
            new("club", Facts(SessionItemType.Weapon, 2001, location: 1004), null),
            new("ring", Facts(SessionItemType.Armor, 2805, location: 1001), null),
            new("helmet", Facts(SessionItemType.Armor, 2806, location: 3), null),
            new("gold-a", Facts(SessionItemType.Gold, 3200, quantity: 30), null),
        ]);

    private static GameSessionOwner Owner(GameSessionLimits? limits = null)
    {
        var owner = GameSessionOwner.Create(Descriptor(limits) with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static SessionRestockFiring Fire(GameSessionOwner owner, ISessionEventWorld world, string holder = "guard")
    {
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ScheduleRestock(owner.Version, holder, hasSource: true).Disposition);
        Assert.AreEqual(ClockMutationDisposition.Changed,
            owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, RestockRules.MaxDelayMs, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        return owner.ProcessEvents(world).Restocks.Single();
    }

    private static (EquipmentSlot, string)[] Worn(GameSessionOwner owner, string npc = "guard") =>
        owner.QueryNpcWorn(npc)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray();

    // The inventory locations the session's list shows, in list order.
    private static (string, int)[] Locations(GameSessionOwner owner, string holder = "guard") =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => (item.Entity, item.Facts.InventoryLocation)).ToArray();

    [TestMethod]
    public void A_regenerated_npc_wields_its_best_items_in_the_same_revision()
    {
        using var owner = Owner();
        var world = new World();
        Assert.IsNull(owner.QueryNpcWorn("guard"), "an NPC the session never dressed wears what its map data says");
        CollectionAssert.AreEqual(new[] { ("purse", 0), ("club", 1004), ("ring", 1001), ("helmet", 3) }, Locations(owner));
        var done = Fire(owner, world).Regenerations.Single();
        Assert.AreEqual(SessionRestockOutcome.Regenerated, done.Outcome);
        CollectionAssert.AreEqual(new[] { "made:2", "made:3" }, done.Created.ToArray(), "a sword and a coat after the pile made:1");
        Assert.AreEqual(GameSessionFailure.None, done.Wield!.Failure);
        // The persistent ring keeps its slot as the worth to beat; the persistent helmet goes on; the new sword and coat go on.
        CollectionAssert.AreEqual(new[]
        {
            new SessionEquipBestStep(EquipmentSlot.Helmet, null, "helmet", EquipmentRefusal.None, null),
            new SessionEquipBestStep(EquipmentSlot.Weapon, null, "made:2", EquipmentRefusal.None, null),
            new SessionEquipBestStep(EquipmentSlot.Armor, null, "made:3", EquipmentRefusal.None, null),
        }, done.Wield.Steps.ToArray());
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helmet"), (EquipmentSlot.Ring1, "ring"), (EquipmentSlot.Weapon, "made:2"), (EquipmentSlot.Armor, "made:3") },
            Worn(owner));
        CollectionAssert.AreEqual(new[] { ("ring", 1001), ("helmet", 1000), ("made:1", -1), ("made:2", 1004), ("made:3", 1006) }, Locations(owner));
        Assert.AreEqual(5, world.Lookups, "every item of the list once, the pile included");
        // A worn item stays: the crafter's deletion refuses it as for the map data's worn items.
        Assert.AreEqual(GameSessionFailure.WornItemUnsupported, owner.RemoveItemFromNpc(owner.Version, "guard", "made:2").Failure);
        Assert.AreEqual(GameSessionFailure.WornItemUnsupported, owner.RemoveItemFromNpc(owner.Version, "guard", "helmet").Failure);
        // Off screen the chest regenerates through its merchant: the guard first, dressed again at the end of its own regeneration, then
        // the chest without a wield (RK15: an NPC only).
        var both = Fire(owner, world, "chest").Regenerations;
        CollectionAssert.AreEqual(new[] { "guard", "chest" }, both.Select(r => r.Holder).ToArray());
        Assert.AreEqual((GameSessionFailure.None, (SessionNpcWield?)null), (both[0].Wield!.Failure, both[1].Wield));
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helmet"), (EquipmentSlot.Ring1, "ring"), (EquipmentSlot.Weapon, "made:5"), (EquipmentSlot.Armor, "made:6") },
            Worn(owner));
    }

    [TestMethod]
    public void A_second_regeneration_dresses_the_npc_again_and_its_cleared_items_leave_the_record()
    {
        using var owner = Owner();
        var world = new World();
        Fire(owner, world);
        world.Sets[GuardSource] = new(SessionRestockSetStatus.Read, new(0, 0, [new(100, 3)]));
        var again = Fire(owner, world).Regenerations.Single();
        Assert.AreEqual(GameSessionFailure.None, again.Wield!.Failure);
        // The coat left with the clear, so the armour slot is empty; the bow wins the weapon slot (the slot always starts empty, WB1).
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helmet"), (EquipmentSlot.Ring1, "ring"), (EquipmentSlot.Weapon, "made:4") }, Worn(owner));
        Assert.AreEqual(3, owner.Capture().Inventory.LastDiscardedMade, "the sword, coat and pile were discarded");
        CollectionAssert.AreEqual(new[] { ("ring", 1001), ("helmet", 1000), ("made:4", 1004) }, Locations(owner));

        // An unreachable set clears and generates nothing, and the NPC still wields what stayed (the call follows the lines, RK15).
        world.Sets[GuardSource] = new(SessionRestockSetStatus.Unreachable, null);
        var cleared = Fire(owner, world).Regenerations.Single();
        Assert.AreEqual((SessionRestockOutcome.Cleared, GameSessionFailure.None), (cleared.Outcome, cleared.Wield!.Failure));
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helmet"), (EquipmentSlot.Ring1, "ring") }, Worn(owner));
    }

    [TestMethod]
    public void The_heros_take_and_the_crafters_components_see_the_worn_view()
    {
        using var owner = Owner();
        var world = new World();
        // The persistent club stays through the clear; the weapon slot starts empty (WB1), so the sword replaces it and it goes to the pack.
        world.Kept.Add("club");
        var done = Fire(owner, world).Regenerations.Single();
        Assert.AreEqual(GameSessionFailure.None, done.Wield!.Failure);
        Assert.AreEqual((EquipmentSlot.Weapon, "made:2"), Worn(owner).Single(worn => worn.Item1 == EquipmentSlot.Weapon));
        CollectionAssert.Contains(Locations(owner), ("club", -1));
        Assert.AreEqual(GameSessionFailure.WornItemUnsupported,
            owner.TakeItemFromNpc(owner.Version, "guard", "made:2", _ => SessionInsertVerdict.Accepted).Failure, "a wielded made item is worn");
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.TakeItemFromNpc(owner.Version, "guard", "club", _ => SessionInsertVerdict.Accepted).Disposition, "the map data's worn club is in the pack now");
        Assert.IsFalse(Locations(owner).Any(item => item.Item1 == "club"));
        // Back in the NPC's list later it is not worn (its record entry left with it).
        Assert.IsFalse(owner.QueryNpcWorn("guard")!.Worn.Any(worn => worn.Item == "club"));
    }

    [TestMethod]
    public void Without_the_hosts_wield_facts_the_npc_keeps_what_the_clear_left_it()
    {
        using var owner = Owner();
        var world = new World { Wields = false };
        var done = Fire(owner, world).Regenerations.Single();
        Assert.AreEqual(SessionRestockOutcome.Regenerated, done.Outcome);
        Assert.AreEqual(GameSessionFailure.EquipmentUnavailable, done.Wield!.Failure);
        Assert.IsNull(owner.QueryNpcWorn("guard"));
        CollectionAssert.AreEqual(new[] { ("ring", 1001), ("helmet", 3), ("made:1", -1), ("made:2", -1), ("made:3", -1) }, Locations(owner));
    }

    [TestMethod]
    public void Facts_the_session_cannot_trust_leave_the_npc_undressed()
    {
        void Refused(Action<World> change, GameSessionFailure failure)
        {
            using var owner = Owner();
            var world = new World();
            change(world);
            var done = Fire(owner, world).Regenerations.Single();
            Assert.AreEqual((SessionRestockOutcome.Regenerated, failure), (done.Outcome, done.Wield!.Failure));
            Assert.AreEqual(0, done.Wield.Steps.Count);
            Assert.IsNull(owner.QueryNpcWorn("guard"));
        }
        Refused(world => world.Wear["ring"] = null, GameSessionFailure.InventoryMismatch);
        Refused(world => world.Wear["ring"] = world.Wear["ring"]! with { Kind = Coat }, GameSessionFailure.InventoryMismatch);
        Refused(world => world.Wear["ring"] = world.Wear["ring"]! with { Item = Armour(ArmorCoverage.Torso) }, GameSessionFailure.InventoryMismatch);
        Refused(world => world.Wear["helmet"] = world.Wear["helmet"]! with { Weight = -1 }, GameSessionFailure.InvalidItem);
    }

    [TestMethod]
    public void A_choice_with_a_move_script_is_not_wielded_and_a_refused_take_off_keeps_the_item()
    {
        using var owner = Owner();
        var world = new World();
        world.Wear[Sword.Prototype.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new(Sword, [SessionScriptPoint.WieldOn], false, Weapon(10, 30)) { Worth = 100 };
        var done = Fire(owner, world).Regenerations.Single();
        var weapon = done.Wield!.Steps.Single(step => step.Slot == EquipmentSlot.Weapon);
        Assert.AreEqual(("made:2", GameSessionFailure.ItemScriptUnsupported, (string?)null), (weapon.Unsupported, weapon.Failure, weapon.Wielded));
        Assert.IsFalse(Worn(owner).Any(worn => worn.Item1 == EquipmentSlot.Weapon));
    }

    [TestMethod]
    public void The_record_is_saved_and_a_damaged_one_is_refused()
    {
        using var owner = Owner();
        Fire(owner, new World());
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Saved = "\"npcWorn\":[{\"npc\":\"guard\",\"worn\":[{\"slot\":1000,\"item\":\"helmet\"},{\"slot\":1001,\"item\":\"ring\"}," +
            "{\"slot\":1004,\"item\":\"made:2\"},{\"slot\":1006,\"item\":\"made:3\"}]}]";
        StringAssert.Contains(text, ",\"generated\":[]," + Saved + "},\"objects\":{\"junkPiles\":[],\"bloodPools\":[],\"lastJunkNumber\":0,\"lastPoolNumber\":0,\"state\":[],\"links\":[],\"created\":[],\"lastCreatedNumber\":0}");
        StringAssert.Contains(text, "\"schemaVersion\":91");
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
            Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
            CollectionAssert.AreEqual(Worn(owner), Worn(restored));
            CollectionAssert.AreEqual(Locations(owner), Locations(restored));
        }
        // Slots out of order or outside 1000..1008, an item worn twice, an NPC that is no map object, NPCs out of order.
        foreach (var (damaged, decodes) in new[]
        {
            (text.Replace("{\"slot\":1000,\"item\":\"helmet\"},{\"slot\":1001,\"item\":\"ring\"}", "{\"slot\":1001,\"item\":\"ring\"},{\"slot\":1000,\"item\":\"helmet\"}"), false),
            (text.Replace("{\"slot\":1000,\"item\":\"helmet\"}", "{\"slot\":999,\"item\":\"helmet\"}"), false),
            (text.Replace("\"item\":\"helmet\"", "\"item\":\"ring\""), true),
            (text.Replace("\"npc\":\"guard\"", "\"npc\":\"made:9\""), true),
            (text.Replace("\"npcWorn\":[{", "\"npcWorn\":[{\"npc\":\"zed\",\"worn\":[]},{"), false),
        })
        {
            Assert.AreNotEqual(text, damaged);
            var decoded = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual(decodes, decoded.Snapshot is not null, damaged);
            if (decodes) Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure);
        }
    }
}
