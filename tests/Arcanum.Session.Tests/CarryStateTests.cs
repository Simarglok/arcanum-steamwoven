using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-085: the hero's carried weight, carry weight and encumbrance level in the session and the insert check's weight part
// (ledger WE1–WE6). The half-elf fixture hero has strength 8 (carry weight 4 000: thresholds 600, 1 200, 1 800, 2 400, 3 000, 3 600)
// and magick/tech aptitude 5; entities, prototypes and weights are invented.
[TestClass]
public sealed class CarryStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Item(SessionItemType type, int prototype, int weight, int magicWeight = 0, int complexity = 0, int? units = null) =>
        new(new(type, prototype, 10, 20), weight, complexity, 0, units, []) { MagicWeight = magicWeight, AmmoType = type == SessionItemType.Ammo ? 2 : null };

    private static readonly SessionHeroKitItem[] HeavyKit =
    [
        new(new(SessionItemType.Weapon, 6049, 4021, 6029)) { Weight = new(1_000) },
        new(new(SessionItemType.Armor, 8188, 2850, 8168)) { Weight = new(500) },
        new(new(SessionItemType.Armor, 8069, 2851, 8049)),
    ];

    private static SessionMapInventorySeed Seed() => new(
        [new("smith", SessionHolderKind.Npc, false, false, [], [])],
        [
            new("anvil", Item(SessionItemType.Generic, 5001, 2_300), new CellCoord(101, 200)),
            new("anvil-plus", Item(SessionItemType.Generic, 5002, 2_301), new CellCoord(102, 200)),
            new("charm", Item(SessionItemType.Generic, 5003, 100, -40, 50), new CellCoord(103, 200)),
            new("gadget", Item(SessionItemType.Generic, 5004, 5_000, 0, -30), new CellCoord(104, 200)),
            new("cells", Item(SessionItemType.Ammo, 7060, 8, units: 100), new CellCoord(105, 200)),
            new("widget", Item(SessionItemType.Generic, 5005, 10, 0, -30), new CellCoord(106, 200)),
        ]);

    private static GameSessionOwner Hero(SessionHeroDefinition? hero = null)
    {
        var owner = CreateWith(hero ?? HalfElf() with { Kit = HeavyKit });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    [TestMethod]
    public void The_kit_weighs_with_its_definition_and_sets_the_level()
    {
        using var owner = Hero();
        Assert.AreEqual(new SessionHeroCarry(1_500, 4_000, 2), owner.QueryHeroCarry(), "1 500 is past 1 200 (30 %) and below 1 800");
        Assert.AreEqual(new SessionItemWeight(1_000), owner.QueryItemWeight("kit:1"));
        Assert.IsNull(owner.QueryItemWeight("kit:3"), "a weightless item keeps no row");
        using var empty = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.IsNull(empty.QueryHeroCarry(), "no hero, nothing carried");
        Assert.AreEqual(GameSessionFailure.InvalidHero,
            GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Kit = [new(Kit[0].Kind) { Weight = new(1_000_001) }] } }).Failure);
    }

    [TestMethod]
    public void A_pickup_fits_up_to_the_carry_weight_inclusive_and_the_host_may_refuse_the_weight_too()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "anvil", Accept).Disposition);
        Assert.AreEqual(new SessionHeroCarry(3_800, 4_000, 6), owner.QueryHeroCarry(), "3 800 is past 3 600 (90 %)");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells", Accept).Disposition, "8 × 100 / 4 = 200: exactly 4 000");
        Assert.AreEqual(4_000, owner.QueryHeroCarry()!.Weight);
        var revision = owner.Version.Revision;
        var refused = owner.PickUpItem(owner.Version, "charm", Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, SessionInsertVerdict.TooHeavy), (refused.Failure, refused.Verdict), "one more item is too heavy");
        Assert.AreEqual(revision, owner.Version.Revision);

        using var other = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, other.PickUpItem(other.Version, "anvil-plus", Accept).Disposition);
        refused = other.PickUpItem(other.Version, "cells", Accept);
        Assert.AreEqual(SessionInsertVerdict.TooHeavy, refused.Verdict, "3 801 + 200 is one above");
        refused = other.PickUpItem(other.Version, "widget", _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual(SessionInsertVerdict.TooHeavy, refused.Verdict, "the host's weight answer counts as well");
        refused = other.PickUpItem(other.Version, "widget", _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual(SessionInsertVerdict.NoRoom, refused.Verdict, "the host still answers the room");
    }

    [TestMethod]
    public void The_weight_is_checked_before_the_technophobia_refusal()
    {
        using var owner = Hero(HalfElf() with { Kit = HeavyKit, Background = SessionBackgrounds.Technophobia });
        Assert.AreEqual(SessionInsertVerdict.TooHeavy, owner.PickUpItem(owner.Version, "gadget", Accept).Verdict, "heavy and technological: weight first");
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, owner.PickUpItem(owner.Version, "widget", Accept).Verdict);
    }

    [TestMethod]
    public void The_magic_weight_adjustment_follows_the_heros_power_and_the_rows_are_saved()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "charm", Accept).Disposition);
        // Aptitude 5, complexity 50: power (50 + 5) / 2 = 27, so −40 × 27 / 50 = −21 and the charm weighs 79.
        Assert.AreEqual(1_579, owner.QueryHeroCarry()!.Weight);
        Assert.AreEqual(new SessionItemWeight(100, -40, 50), owner.QueryItemWeight("charm"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "cells", Accept).Disposition);

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"weights\":[{\"entity\":\"cells\",\"weight\":8,\"magicWeight\":0,\"complexity\":0}," +
            "{\"entity\":\"charm\",\"weight\":100,\"magicWeight\":-40,\"complexity\":50}," +
            "{\"entity\":\"kit:1\",\"weight\":1000,\"magicWeight\":0,\"complexity\":0},{\"entity\":\"kit:2\",\"weight\":500,\"magicWeight\":0,\"complexity\":0}],\"junkPiles\":[],\"identified\":[],\"lastDiscardedMade\":0,\"grid\":[");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(owner.QueryHeroCarry(), restored.QueryHeroCarry());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));

        // A row of an object the inventory does not hold or a weightless row is refused.
        void Refused(string from, string to) => Assert.AreEqual(GameSessionFailure.InvalidSnapshot,
            GameSessionOwner.Restore(Decode(text.Replace(from, to)), RestoreOptions()).Failure, to);
        Refused("{\"entity\":\"cells\",\"weight\":8", "{\"entity\":\"cellz\",\"weight\":8");
        Refused("\"weight\":100,\"magicWeight\":-40", "\"weight\":0,\"magicWeight\":0");
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(text.Replace("\"weight\":100,", "\"weight\":1000001,")).Failure,
            "a weight out of range");
    }

    [TestMethod]
    public void A_created_object_has_its_prototypes_weight_and_keeps_it_through_moves()
    {
        using var owner = Hero();
        var made = owner.AddItemToNpc(owner.Version, "smith", new(SessionItemType.Generic, 5100, 1, 2), Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition);
        Assert.IsNull(owner.QueryItemWeight(made.Item!), "without the host's weights a created object weighs 0");
        owner.AttachItemWeights(prototype => prototype == 5100 ? new SessionItemWeight(300) : prototype == 5101 ? new SessionItemWeight(2_000_000) : null);
        var weighed = owner.AddItemToNpc(owner.Version, "smith", new(SessionItemType.Generic, 5100, 1, 2), Accept);
        Assert.AreEqual(new SessionItemWeight(300), owner.QueryItemWeight(weighed.Item!));
        var invalid = owner.AddItemToNpc(owner.Version, "smith", new(SessionItemType.Generic, 5101, 1, 2), Accept);
        Assert.IsNull(owner.QueryItemWeight(invalid.Item!), "facts the rules refuse count as none");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "smith", weighed.Item!, Accept).Disposition);
        Assert.AreEqual(1_800, owner.QueryHeroCarry()!.Weight, "the made item keeps its weight in the hero's list");
        SessionItemFacts? seen = null;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "smith", "kit:1", item => { seen = item.Facts; return SessionInsertVerdict.Accepted; }).Disposition);
        Assert.AreEqual(1_000, seen!.Weight, "the NPC's insert check sees the item's weight");
        Assert.AreEqual(800, owner.QueryHeroCarry()!.Weight);
        Assert.AreEqual(new SessionItemWeight(1_000), owner.QueryItemWeight("kit:1"), "a given item keeps its row at the NPC");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, "smith", "kit:1", Accept).Disposition);
        Assert.AreEqual(1_800, owner.QueryHeroCarry()!.Weight);
    }
}
