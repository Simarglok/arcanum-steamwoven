using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-069: the hero's background and its two item refusals (ledger BG rows): Magick Allergy (69) cannot wear a magickal item,
// Technophobia (72) cannot carry a technological one. Items and numbers are invented.
[TestClass]
public sealed class BackgroundStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionMapInventorySeed Seed() => new([new("corpse", SessionHolderKind.Npc, true, false, [], ["gadget"])],
    [
        new("cog", new(new(SessionItemType.Generic, 5600, 1, 2), 1, -5, 0, null, []), new CellCoord(101, 200)),
        new("charm", new(new(SessionItemType.Generic, 5601, 3, 4), 1, 5, 0, null, []), new CellCoord(102, 200)),
        new("gadget", new(new(SessionItemType.Generic, 5602, 5, 6), 1, -1, 0, null, []), null),
    ]);

    private static GameSessionOwner Hero(int background)
    {
        var owner = CreateWith(HalfElf() with { Background = background });
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    [TestMethod]
    public void A_technophobe_cannot_take_a_technological_item_and_the_weight_answer_comes_first()
    {
        using var owner = Hero(SessionBackgrounds.Technophobia);
        Assert.AreEqual(SessionBackgrounds.Technophobia, owner.QueryHeroBackground());
        var refused = owner.PickUpItem(owner.Version, "cog", Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused, 0L),
            (refused.Failure, refused.Verdict, owner.Version.Revision), "code 15, nothing taken");
        Assert.AreEqual(SessionInsertVerdict.TooHeavy, owner.PickUpItem(owner.Version, "cog", _ => SessionInsertVerdict.TooHeavy).Verdict, "the weight first");
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, owner.PickUpItem(owner.Version, "cog", _ => SessionInsertVerdict.NoRoom).Verdict, "before the room");
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, owner.LootItem(owner.Version, "corpse", "gadget", Accept).Verdict, "loot too");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "charm", Accept).Disposition, "a magickal item is carried");
        using var plain = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, plain.PickUpItem(plain.Version, "cog", Accept).Disposition);
    }

    [TestMethod]
    public void A_technophobe_refuses_a_technological_item_from_in_and_keeps_neutral_ones_and_host_answers_pass()
    {
        using var owner = Hero(SessionBackgrounds.Technophobia);
        var given = owner.GiveItemToPc(owner.Version, "corpse", 5, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused, 0L),
            (given.Failure, given.Verdict, owner.Version.Revision), "negative IN");
        using var neutral = CreateWith(HalfElf() with { Background = SessionBackgrounds.Technophobia });
        Assert.AreEqual(GameSessionDisposition.Changed, neutral.AttachMapInventory(Exterior, new([],
            [new("stone", new(new(SessionItemType.Generic, 5603, 7, 8), 1, 0, 0, null, []), new CellCoord(103, 200))])).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, neutral.PickUpItem(neutral.Version, "stone", Accept).Disposition, "complexity 0 is carried");
        // The host's own answer passes unchanged for a hero who is no technophobe.
        using var plain = Hero(SessionBackgrounds.None);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, plain.PickUpItem(plain.Version, "cog", _ => SessionInsertVerdict.TechnologyRefused).Verdict);
    }

    [TestMethod]
    public void A_technophobe_cannot_take_back_a_placed_technological_seed_item()
    {
        using var plain = Hero(SessionBackgrounds.None);
        Assert.AreEqual(GameSessionDisposition.Changed, plain.PickUpItem(plain.Version, "cog", Accept).Disposition);
        plain.PlaceHeroItem("cog", new CellCoord(120, 210));
        var text = GameSessionSnapshotCodec.Encode(plain.Capture()).Replace(",\"background\":0,\"backgroundDescription\":0}", ",\"background\":72,\"backgroundDescription\":0}");
        using var owner = GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!, RestoreOptions()).Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        var refused = owner.PickUpItem(owner.Version, "cog", Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused),
            (refused.Failure, refused.Verdict), "the seed's complexity");
        Assert.AreEqual("cog", owner.QueryPlacedItems().Single().Entity, "still on the ground");
    }

    [TestMethod]
    public void A_technophobe_cannot_take_off_a_worn_technological_item()
    {
        using var owner = CreateWith(HalfElf() with { Background = SessionBackgrounds.Technophobia });
        var gear = new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, MagickTechComplexity = -10,
            Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium, AcAdjustment = 12 },
        };
        // ARC-UI-019: the move into an empty slot runs the insert check too (ledger IW12), so the hero cannot put it on from the grid.
        var into = owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, gear), (_, _) => true, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused), (into.Failure, into.Verdict));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.EquipBest(owner.Version, entity => entity == "kit:2" ? new(Kit[1].Kind, [], false, gear) : null,
            (_, _) => true, Accept).Disposition, "the start kit's item is worn");
        var revision = owner.Version.Revision;
        var off = owner.Unequip(owner.Version, EquipmentSlot.Armor, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused, revision),
            (off.Failure, off.Verdict, owner.Version.Revision), "the unwield runs the insert check");
        var swap = owner.Equip(owner.Version, "kit:3", EquipmentSlot.Armor, new(Kit[2].Kind, [], false, gear with { MagickTechComplexity = 0 }),
            (_, _) => true, Accept);
        Assert.AreEqual((GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.TechnologyRefused), (swap.Failure, swap.Verdict), "the occupant");
        var best = owner.EquipBest(owner.Version, entity => entity switch
        {
            "kit:2" => new(Kit[1].Kind, [], false, gear) { Worth = 1 },
            "kit:3" => new(Kit[2].Kind, [], false, gear with { MagickTechComplexity = 0 }) { Worth = 50 },
            _ => null,
        }, (_, _) => true, Accept);
        Assert.AreEqual(SessionInsertVerdict.TechnologyRefused, best.Steps.Single(step => step.Slot == EquipmentSlot.Armor).Verdict, "the best-items choice");
        Assert.AreEqual("kit:2", owner.QueryEquipment().Worn.Single().Entity);
    }

    [TestMethod]
    public void A_magick_allergic_hero_cannot_wear_a_magickal_item()
    {
        using var owner = CreateWith(HalfElf() with { Background = SessionBackgrounds.MagickAllergy });
        var mail = new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor, MagickTechComplexity = 10,
            Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium, AcAdjustment = 12 },
        };
        var result = owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, new(Kit[1].Kind, [], false, mail), (_, _) => true, Accept);
        Assert.AreEqual((GameSessionFailure.WearRefused, EquipmentRefusal.WieldMagicItems), (result.Failure, result.Refusal), "code 18");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor,
            new(Kit[1].Kind, [], false, mail with { MagickTechComplexity = -10 }), (_, _) => true, Accept).Disposition, "a technological item is worn");
    }

    [TestMethod]
    public void The_best_items_choice_skips_a_magickal_item_for_a_magick_allergic_hero()
    {
        var coat = new EquipmentItem
        {
            Kind = EquipmentItemKind.Armor,
            Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium, AcAdjustment = 5 },
        };
        foreach (var (background, worn) in new[] { (SessionBackgrounds.MagickAllergy, "kit:3"), (SessionBackgrounds.None, "kit:2") })
        {
            using var owner = CreateWith(HalfElf() with { Background = background });
            var result = owner.EquipBest(owner.Version, entity => entity switch
            {
                "kit:2" => new(Kit[1].Kind, [], false, coat with { MagickTechComplexity = 10 }) { Worth = 500 },
                "kit:3" => new(Kit[2].Kind, [], false, coat) { Worth = 10 },
                _ => null,
            }, (_, _) => true, Accept);
            Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
            Assert.AreEqual(worn, owner.QueryEquipment().Worn.Single().Entity, $"background {background}");
        }
    }

    [TestMethod]
    public void The_background_is_saved_and_bounded_and_npcs_have_none()
    {
        using var owner = Hero(SessionBackgrounds.Technophobia);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"background\":72,\"backgroundDescription\":0}");
        using var restored = GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(text).Snapshot!, RestoreOptions()).Owner!;
        Assert.AreEqual(72, restored.QueryHeroBackground());
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Restore(GameSessionSnapshotCodec.TryDecode(
            text.Replace(",\"background\":72,\"backgroundDescription\":0}", ",\"background\":-1,\"backgroundDescription\":0}")).Snapshot!, RestoreOptions()).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidHero, GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() with { Background = 1000 } }).Failure);
        using var heroless = Create();
        Assert.IsNull(heroless.QueryHeroBackground());
        // An NPC has no background (the original's reader answers 0 for a non-PC): a technological item enters its list.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, "corpse", new(SessionItemType.Generic, 5602, 5, 6), Accept).Disposition);
    }

    [TestMethod]
    public void A_technophobe_cannot_take_a_technological_product()
    {
        var part = new SessionItemKind(SessionItemType.Generic, 2120, 1, 2);
        var product = new SessionItemKind(SessionItemType.Generic, 2320, 5, 6);
        static Arcanum.Data.Technology.SchematicAlternatives Alternatives(params int[] bases) => new(Array.AsReadOnly(bases), bases.Length);
        var schematic = new Arcanum.Data.Technology.SchematicRecord
        {
            Number = 2000, Kind = Arcanum.Data.Technology.SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3,
            Component1 = Alternatives(2100), Component2 = Alternatives(2100), Product = Alternatives(2300), Quantity = 1,
            MissingLines = Array.AsReadOnly(new bool[7]),
        };
        Arcanum.Simulation.Technology.ComponentFacts? Facts(int prototype) => prototype == 2320 ? new(-5, 3) : new(0, 3);
        foreach (var (background, refused) in new[] { (SessionBackgrounds.Technophobia, true), (SessionBackgrounds.None, false) })
        {
            using var owner = CreateWith(HalfElf() with { Background = background, Kit = [new(part), new(part)] });
            var made = owner.AssembleSchematic(owner.Version, schematic, _ => new SessionCraftFacts(10), Facts, _ => product, Accept);
            // ARC-WORLD-070: a refused product lies on the hero's tile (the original leaves it there) and the assembly succeeds.
            Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition, $"background {background}");
            Assert.AreEqual(refused ? SessionInsertVerdict.TechnologyRefused : null, made.Verdict);
            Assert.AreEqual(refused ? 1 : 0, made.OnGround.Count);
            Assert.AreEqual(refused ? 0 : 1, made.Made.Count);
        }
    }
}
