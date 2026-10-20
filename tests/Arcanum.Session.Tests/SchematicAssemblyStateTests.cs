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
        using var owner = Owner();
        var parts = Schematic([2100], [2100], quantity: 3);
        var full = owner.AssembleSchematic(owner.Version, parts, InBag, Facts, Kinds, _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual(GameSessionFailure.InsertRefused, full.Failure);
        var calls = 0;
        var third = owner.AssembleSchematic(owner.Version, parts, InBag, Facts, Kinds, _ => ++calls < 3 ? SessionInsertVerdict.Accepted : SessionInsertVerdict.TooHeavy);
        Assert.AreEqual(GameSessionFailure.InsertRefused, third.Failure, "every unit is checked before anything is consumed");
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, _ => null, Accept).Failure);
        var ammo = new SessionItemKind(SessionItemType.Ammo, 2320, 5, 6);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.AssembleSchematic(owner.Version, parts, InBag, Facts, _ => ammo, Accept).Failure,
            "the hero's lists hold no ammunition");
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
        StringAssert.Contains(text, ",\"made\":[]},");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.Failure);
        using var back = restored.Owner!;
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));

        // A made item takes the number past every made or deleted one.
        using var again = Owner();
        again.AssembleSchematic(again.Version, Schematic([2100], [2100]), InBag, Facts, Kinds, Accept);
        var saved = GameSessionSnapshotCodec.Encode(again.Capture());
        StringAssert.Contains(saved, ",\"made\":[{\"entity\":\"made:1\",\"type\":\"Generic\",\"prototype\":2320,\"name\":5,\"description\":6}]},");
        GameSessionFailure Restore(string json) => GameSessionOwner.Restore(Decode(json), RestoreOptions()).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"made:1\"", "\"made:0\"", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"made:1\"", "\"forged\"", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.InvalidItem, Restore(saved.Replace("\"type\":\"Generic\",\"prototype\":2320", "\"type\":\"Ammo\",\"prototype\":2320", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restore(saved.Replace("\"destroyed\":[\"kit:1\",\"kit:2\"]", "\"destroyed\":[\"kit:1\",\"kit:2\",\"made:1\"]", StringComparison.Ordinal)));
    }
}
