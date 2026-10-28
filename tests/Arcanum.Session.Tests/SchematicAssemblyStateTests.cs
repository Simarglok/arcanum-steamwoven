using Arcanum.Data.Technology;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's schematic assembly (ARC-TECHNOLOGY-004). The hero's kit holds two neutral parts (kit:1, kit:2, prototype 2120 =
// base 2100) and a technological gear (kit:3, prototype 2220, discipline 3, complexity −10); the product is a generic item (base
// 2300, prototype 2320). Everything is invented.
[TestClass]
public sealed class SchematicAssemblyStateTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Gear = new(SessionItemType.Generic, 2220, 3, 4);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);

    private static ComponentFacts? Facts(int prototype) => prototype switch { 2120 => new(0, 3), 2220 => new(-10, 3), _ => null };
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;

    private static SchematicAlternatives Alternatives(params int[] bases) => new(Array.AsReadOnly(bases), bases.Length);

    private static SchematicRecord Schematic(int[] first, int[] second, int quantity = 1, int[]? product = null) => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3, Component1 = Alternatives(first),
        Component2 = Alternatives(second), Product = Alternatives(product ?? [2300]), Quantity = quantity, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    private static GameSessionOwner Owner(int[]? degrees = null, GameSessionLimits? limits = null)
    {
        var stats = Defaults();
        stats[4] = 15;
        var hero = HalfElf(stats) with
        {
            Kit = [new(Part), new(Part), new(Gear)],
            Advancement = SessionHeroAdvancement.None with { TechDegrees = [.. degrees ?? [0, 0, 0, 0, 0, 0, 0, 0]] },
        };
        return CreateWith(hero, limits);
    }

    [TestMethod]
    public void Two_parts_become_the_product_and_leave_the_inventory()
    {
        using var owner = Owner();
        var schematic = Schematic([2100], [2100], quantity: 2);
        Assert.AreEqual(AssemblyButton.CanAssemble, owner.QueryHeroAssembly(schematic, InBag, Facts)!.Button);
        var result = owner.AssembleSchematic(owner.Version, schematic, InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, result.Made.ToArray());
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2" }, result.Consumed.ToArray(), "component 1 then component 2");
        Assert.AreEqual(new GameSessionVersion(owner.Identity, 1), owner.Version, "one revision");
        CollectionAssert.AreEqual(new[] { "kit:3" }, owner.QueryHeroKit().Select(i => i.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product), new SessionMadeItem("made:2", Product) }, owner.QueryMadeItems().ToArray());
        Assert.IsTrue(owner.IsItemDestroyed("kit:1") && owner.IsItemDestroyed("kit:2"));
    }

    [TestMethod]
    public void The_gate_refuses_a_missing_item_or_lacking_expertise_and_nothing_changes()
    {
        using var owner = Owner();
        var gear = Schematic([2100], [2200]);
        var refused = owner.AssembleSchematic(owner.Version, gear, InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionFailure.AssemblyRefused, AssemblyButton.ExpertiseLacking), (refused.Failure, refused.Plan!.Button), "degree 0 gives 0 < 10");
        Assert.AreEqual(new GameSessionVersion(owner.Identity, 0), owner.Version);
        Assert.AreEqual(3, owner.QueryHeroKit().Count);

        using var expert = Owner([0, 0, 0, 1, 0, 0, 0, 0]);
        SessionCraftLookup noGear = entity => entity == "kit:3" ? null : new SessionCraftFacts(10);
        var missing = expert.AssembleSchematic(expert.Version, gear, noGear, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionFailure.AssemblyRefused, AssemblyButton.ItemMissing), (missing.Failure, missing.Plan!.Button));
        Assert.AreEqual(GameSessionDisposition.Changed, expert.AssembleSchematic(expert.Version, gear, InBag, Facts, Kinds, Accept).Disposition,
            "degree 1 gives 10 ≥ 10");
    }

    [TestMethod]
    public void Worn_broken_and_unlisted_items_are_not_components()
    {
        using var owner = Owner();
        var parts = Schematic([2100], [2100]);
        // The host knows no facts of kit:2: it is not a component.
        SessionCraftLookup onlyFirst = entity => entity == "kit:1" ? new SessionCraftFacts(10) : null;
        Assert.AreEqual(AssemblyButton.ItemMissing, owner.QueryHeroAssembly(parts, onlyFirst, Facts)!.Button);
        Assert.AreEqual(AssemblyButton.ItemMissing, owner.QueryHeroAssembly(parts, e => new SessionCraftFacts(e == "kit:2" ? 1006 : 10), Facts)!.Button);
        Assert.AreEqual(AssemblyButton.ItemMissing, owner.QueryHeroAssembly(parts, e => new SessionCraftFacts(10, DescriptionChanged: e == "kit:2"), Facts)!.Button);
        // The session's durability record wins over the host's facts.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BreakItem(owner.Version, "kit:2", new(10, 0, 0, false)).Disposition);
        Assert.AreEqual(AssemblyButton.ItemMissing, owner.QueryHeroAssembly(parts, InBag, Facts)!.Button);
    }

    [TestMethod]
    public void Nothing_is_consumed_when_the_products_cannot_be_held_TC3()
    {
        // A product the inventory refuses lies on the ground since ARC-WORLD-070 (AssemblyGroundStateTests); a product the session
        // cannot hold at all still refuses before anything is consumed.
        using var owner = Owner();
        var parts = Schematic([2100], [2100], quantity: 3);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, _ => null, Accept).Failure);
        var ammo = new SessionItemKind(SessionItemType.Ammo, 2320, 5, 6);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, _ => ammo, Accept).Failure,
            "an ammunition product without its ammunition facts");
        Assert.AreEqual((0L, 3), (owner.Version.Revision, owner.QueryHeroKit().Count));
    }

    [TestMethod]
    public void Refusals_for_no_or_a_dead_hero_a_stale_version_and_the_budgets()
    {
        using var none = Create();
        Assert.IsNull(none.QueryHeroAssembly(Schematic([2100], [2100]), InBag, Facts));
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, none.AssembleSchematic(none.Version, Schematic([2100], [2100]), InBag, Facts, Kinds, Accept).Failure);
        using var owner = Owner(limits: new GameSessionLimits(maxPcItems: 3));
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.AssembleSchematic(new(owner.Identity, 5), Schematic([2100], [2100]), InBag, Facts, Kinds, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AssembleSchematic(owner.Version, Schematic([2100], [2100], quantity: 3), InBag, Facts, Kinds, Accept).Failure,
            "3 − 2 + 3 items over a limit of 3");
        owner.Kill(owner.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, owner.AssembleSchematic(owner.Version, Schematic([2100], [2100]), InBag, Facts, Kinds, Accept).Failure);
    }

    [TestMethod]
    public void Made_items_are_saved_numbered_past_deleted_ones_and_refused_when_malformed()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, Schematic([2100], [2100]), InBag, Facts, Kinds, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, "made:1", new(10, 0, 0, false)).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"made\":[],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[],\"weights\":[],\"junkPiles\":[],\"identified\":[],\"lastDiscardedMade\":0},");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.Failure);
        using var back = restored.Owner!;
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));

        // A made item takes the number past every made or deleted one.
        using var again = Owner();
        again.AssembleSchematic(again.Version, Schematic([2100], [2100]), InBag, Facts, Kinds, Accept);
        var saved = GameSessionSnapshotCodec.Encode(again.Capture());
        StringAssert.Contains(saved, ",\"made\":[{\"entity\":\"made:1\",\"type\":\"Generic\",\"prototype\":2320,\"name\":5,\"description\":6,\"complexity\":0}],\"npcs\":[],\"placed\":[],\"ammo\":[],\"given\":[],\"piles\":[],\"scriptSlots\":[],\"weights\":[],\"junkPiles\":[],\"identified\":[],\"lastDiscardedMade\":0},");
        GameSessionFailure Restore(string json) => GameSessionOwner.Restore(Decode(json), RestoreOptions()).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"made:1\"", "\"made:0\"", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"made:1\"", "\"forged\"", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"type\":\"Generic\",\"prototype\":2320", "\"type\":\"Ammo\",\"prototype\":2320", StringComparison.Ordinal)),
            "a made ammunition item without its stack (ARC-WORLD-066)");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"destroyed\":[\"kit:1\",\"kit:2\"]", "\"destroyed\":[\"kit:1\",\"kit:2\",\"made:1\"]", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Made_numbers_go_past_deleted_ones_across_a_restore_and_made_items_can_be_worn()
    {
        var stats = Defaults();
        stats[4] = 15;
        var hero = HalfElf(stats) with { Kit = [new(Part), new(Part), new(Part), new(Part)] };
        using var owner = CreateWith(hero);
        var parts = Schematic([2100], [2100]);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, Kinds, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, "made:1", new(10, 0, 0, false)).Disposition);
        var restored = GameSessionOwner.Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.Failure);
        using var back = restored.Owner!;
        var second = back.AssembleSchematic(back.Version, parts, InBag, Facts, Kinds, Accept);
        CollectionAssert.AreEqual(new[] { "made:2" }, second.Made.ToArray(), "made:1 was deleted: its number is not reused");

        // A made item is held like any other: a seed may not list the session's prefix.
        var seed = new SessionMapInventorySeed([], [new("made:9", new(Product, 1, 0, 0, null, []), new(1, 1))]);
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, back.AttachMapInventory(Exterior, seed).Failure);
    }

    [TestMethod]
    public void One_part_cannot_serve_both_lists_and_the_other_alternative_names_the_product_TC4()
    {
        var stats = Defaults();
        stats[4] = 15;
        using var single = CreateWith(HalfElf(stats) with { Kit = [new(Part)] });
        Assert.AreEqual(AssemblyButton.ItemMissing, single.QueryHeroAssembly(Schematic([2100], [2100]), InBag, Facts)!.Button);
        // Component 1 found at alternative 1 (2100): the product is alternative 1.
        using var owner = Owner([0, 0, 0, 1, 0, 0, 0, 0]);
        SessionItemKind? Both(int prototype) => prototype is 2320 or 2420 ? Product with { Prototype = prototype } : null;
        var result = owner.AssembleSchematic(owner.Version, Schematic([2900, 2100], [2200], product: [2300, 2400]), InBag, Facts, Both, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        Assert.AreEqual(2420, owner.QueryMadeItems().Single().Kind.Prototype);
    }

    [TestMethod]
    public void A_host_callback_that_changes_the_session_and_a_spent_durability_budget_refuse()
    {
        using var owner = Owner(limits: new GameSessionLimits(maxDurabilityRecords: 2));
        var parts = Schematic([2100], [2100]);
        SessionInsertCheck meddling = _ => { owner.BreakItem(owner.Version, "kit:3", new(10, 0, 0, false)); return SessionInsertVerdict.Accepted; };
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, Kinds, meddling).Failure);
        Assert.AreEqual(2, owner.QueryHeroKit().Count(item => item.Kind == Part), "nothing consumed");
        // kit:3's break used one of two records; the two deletions need two more.
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, Kinds, Accept).Failure);
        var empty = Schematic([2100], [2100], product: []);
        Assert.AreEqual(GameSessionFailure.InvalidSchematics, owner.AssembleSchematic(owner.Version, empty, InBag, Facts, Kinds, Accept).Failure);
    }
}
