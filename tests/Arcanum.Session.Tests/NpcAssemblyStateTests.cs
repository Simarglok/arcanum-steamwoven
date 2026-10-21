using System.Collections.Immutable;
using Arcanum.Data.Technology;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The NPC schematic crafter (ARC-TECHNOLOGY-006). The smith (alive) holds two neutral parts (prototype 2120), a technological gear
// (2220, discipline 3, complexity −10) and a worn part; the corpse (dead) holds two parts. The product is a generic item (2320).
// Everything is invented.
[TestClass]
public sealed class NpcAssemblyStateTests
{
    private static readonly SessionItemKind Part = new(SessionItemType.Generic, 2120, 1, 2);
    private static readonly SessionItemKind Gear = new(SessionItemType.Generic, 2220, 3, 4);
    private static readonly SessionItemKind Product = new(SessionItemType.Generic, 2320, 5, 6);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionCraftLookup InBag = _ => new SessionCraftFacts(10);

    private static ComponentFacts? Facts(int prototype) => prototype switch { 2120 => new(0, 3), 2220 => new(-10, 3), _ => null };
    private static SessionItemKind? Kinds(int prototype) => prototype == 2320 ? Product : null;

    private static SchematicAlternatives Alternatives(params int[] bases) => new(Array.AsReadOnly(bases), bases.Length);

    private static SchematicRecord Schematic(int[] first, int[] second, int quantity = 1) => new()
    {
        Number = 2000, Kind = SchematicKind.Learned, NameText = 1, DescriptionText = 2, Art = 3, Component1 = Alternatives(first),
        Component2 = Alternatives(second), Product = Alternatives([2300]), Quantity = quantity, MissingLines = Array.AsReadOnly(new bool[7]),
    };

    private static SessionItemFacts ItemFacts(SessionItemKind kind, int location = 10) => new(kind, 5, 0, location, null, []);

    private static SessionMapInventorySeed Seed() => new(
        [
            new("smith", SessionHolderKind.Npc, false, false, [], ["p1", "p2", "gear", "worn"]),
            new("corpse", SessionHolderKind.Npc, true, false, [], ["c1", "c2"]),
        ],
        [
            new("p1", ItemFacts(Part), null), new("p2", ItemFacts(Part), null), new("gear", ItemFacts(Gear), null),
            new("worn", ItemFacts(Part, 1004), null), new("c1", ItemFacts(Part), null), new("c2", ItemFacts(Part), null),
            new("apple", ItemFacts(Part), new CellCoord(1, 1)),
        ]);

    private static SessionNpcCrafter Crafter(string npc = "smith", int intelligence = 8, int gearDegree = 0) =>
        new(new(npc, new(CharacterRace.Human, false, [8, 8, 8, 8, intelligence, 8, 8, 8], 0, 0, 1, 0, [.. new int[12]], [.. new int[4]],
            [.. new int[16]], [0, 0, 0, gearDegree, 0, 0, 0, 0], 1, 2, 0, 0), 0), AdvancementModifiers.None);

    private static GameSessionOwner Owner(GameSessionLimits? limits = null)
    {
        var owner = Create(limits);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string npc) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == npc).Items.Select(i => i.Entity).ToArray();

    [TestMethod]
    public void The_npc_crafter_uses_its_own_list_and_expertise_and_keeps_the_product()
    {
        using var owner = Owner();
        var schematic = Schematic([2100], [2100], quantity: 2);
        var plan = owner.QueryNpcAssembly(Crafter(), schematic, InBag, Facts)!;
        Assert.AreEqual((AssemblyButton.CanAssemble, "p1", "p2"), (plan.Button, plan.First.Item, plan.Second.Item), "the worn part is skipped");
        var result = owner.AssembleSchematicForNpc(owner.Version, Crafter(), schematic, InBag, Facts, Kinds, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, result.Made.ToArray());
        CollectionAssert.AreEqual(new[] { "p1", "p2" }, result.Consumed.ToArray());
        Assert.AreEqual(1, owner.Version.Revision, "one revision");
        CollectionAssert.AreEqual(new[] { "gear", "worn", "made:1", "made:2" }, List(owner, "smith"));
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Product), new SessionMadeItem("made:2", Product) }, owner.QueryNpcInventory("smith").ToArray());
        Assert.IsTrue(owner.IsItemDestroyed("p1") && owner.IsItemDestroyed("p2"));
        Assert.AreEqual(0, owner.QueryMadeItems().Count + owner.QueryPcInventory().Count, "the PC gets nothing");
    }

    [TestMethod]
    public void The_npc_expertise_comes_from_its_sheet_and_effective_intelligence()
    {
        using var owner = Owner();
        var technology = owner.QueryNpcTechnology(Crafter(gearDegree: 1))!.Value;
        Assert.AreEqual((1, 1, 10), (technology[3].Degree, technology[3].CountingDegree, technology[3].Expertise));
        var dull = owner.QueryNpcTechnology(Crafter(intelligence: 4, gearDegree: 1))!.Value;
        Assert.AreEqual((1, 0, 0), (dull[3].Degree, dull[3].CountingDegree, dull[3].Expertise), "intelligence 4 is below degree 1's minimum 5");
        var boosted = owner.QueryNpcTechnology(Crafter(intelligence: 4, gearDegree: 1) with
        {
            Modifiers = AdvancementModifiers.None with { Stats = [new(CharacterStat.Intelligence, StatModifierOperator.Add, 2)] },
        })!.Value;
        Assert.AreEqual(10, boosted[3].Expertise, "the crafter's stat changes count");
        var gear = Schematic([2100], [2200]);
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, owner.QueryNpcAssembly(Crafter(), gear, InBag, Facts)!.Button);
        Assert.AreEqual(AssemblyButton.CanAssemble, owner.QueryNpcAssembly(Crafter(gearDegree: 1), gear, InBag, Facts)!.Button);
        Assert.IsNull(owner.QueryNpcAssembly(Crafter("stranger"), gear, InBag, Facts), "an NPC the session does not know");
    }

    [TestMethod]
    public void Added_items_are_components_and_the_session_record_wins_over_the_map_data()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, "smith", Gear, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveItemFromNpc(owner.Version, "smith", "gear").Disposition);
        var plan = owner.QueryNpcAssembly(Crafter(gearDegree: 1), Schematic([2100], [2200]), InBag, Facts)!;
        Assert.AreEqual((AssemblyButton.CanAssemble, "made:1"), (plan.Button, plan.Second.Item), "the added gear serves as a component");
        var result = owner.AssembleSchematicForNpc(owner.Version, Crafter(gearDegree: 1), Schematic([2100], [2200]), InBag, Facts, Kinds, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { "made:2" }, result.Made.ToArray(), "the numbering continues past the consumed made item");
        CollectionAssert.AreEqual(new[] { "made:2" }, owner.QueryNpcInventory("smith").Select(i => i.Entity).ToArray());
    }

    [TestMethod]
    public void Refusals_change_nothing()
    {
        using var owner = Owner();
        var parts = Schematic([2100], [2100]);
        void Refused(SessionAssemblyResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(0, owner.Version.Revision);
            CollectionAssert.AreEqual(new[] { "p1", "p2", "gear", "worn" }, List(owner, "smith"));
        }
        var badSheet = Crafter() with { Loaded = Crafter().Loaded with { Entity = "bad id" } };
        Refused(owner.AssembleSchematicForNpc(owner.Version, badSheet, parts, InBag, Facts, Kinds, Accept), GameSessionFailure.InvalidAdvancement);
        Refused(owner.AssembleSchematicForNpc(new GameSessionVersion(owner.Identity, 3), Crafter(), parts, InBag, Facts, Kinds, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter("stranger"), parts, InBag, Facts, Kinds, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter("corpse"), parts, InBag, Facts, Kinds, Accept), GameSessionFailure.InvalidLifeTarget);
        var lacking = owner.AssembleSchematicForNpc(owner.Version, Crafter(), Schematic([2100], [2200]), InBag, Facts, Kinds, Accept);
        Refused(lacking, GameSessionFailure.AssemblyRefused);
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, lacking.Plan!.Button);
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter(), parts, InBag, Facts, _ => null, Accept), GameSessionFailure.ItemTypeUnsupported);
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter(), parts, InBag, Facts, Kinds, _ => SessionInsertVerdict.NoRoom), GameSessionFailure.InsertRefused);
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter(), Schematic([2100], [2100], quantity: 0), InBag, Facts, Kinds, Accept), GameSessionFailure.InvalidSchematics);
        // A seed attached from the insert check makes the assembly stale.
        Refused(owner.AssembleSchematicForNpc(owner.Version, Crafter(), parts, InBag, Facts, Kinds, _ =>
        {
            owner.AttachMapInventory(Exterior, Seed());
            return SessionInsertVerdict.Accepted;
        }), GameSessionFailure.StaleVersion);
        using var tight = Owner(new GameSessionLimits(maxNpcItems: 1));
        var budget = tight.AssembleSchematicForNpc(tight.Version, Crafter(), Schematic([2100], [2100], quantity: 2), InBag, Facts, Kinds, Accept);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, budget.Failure);
        using var records = Owner(new GameSessionLimits(maxDurabilityRecords: 1));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, records.AssembleSchematicForNpc(records.Version, Crafter(), parts, InBag, Facts, Kinds, Accept).Failure);
    }

    [TestMethod]
    public void The_session_advancement_record_wins_over_the_map_sheet()
    {
        var created = GameSessionOwner.Create(Descriptor() with { AutoLevel = AdvancementStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        using var owner = created.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, Seed()).Disposition);
        // The session levels the smith from map data with no degree; the crafter's later map data claim degree 1 in discipline 3.
        var map = Crafter().Loaded with { AutoLevelScheme = 62 };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LevelFollower(owner.Version, map, AdvancementModifiers.None, new(1, 2)).Disposition);
        var claimed = Crafter(gearDegree: 1);
        Assert.AreEqual(0, owner.QueryNpcTechnology(claimed)!.Value[3].Degree, "the record's degree, not the map data's");
        Assert.AreEqual(AssemblyButton.ExpertiseLacking, owner.QueryNpcAssembly(claimed, Schematic([2100], [2200]), InBag, Facts)!.Button);
    }

    [TestMethod]
    public void A_crafter_killed_by_the_session_or_known_through_the_party_or_with_a_full_list()
    {
        using var owner = Owner();
        var killed = owner.Kill(owner.Version, SessionLifeTarget.Critter("smith", new(), Arcanum.Simulation.Combat.LifeState.Unharmed));
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.IsTrue(owner.QueryNpcDead("smith"));
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget,
            owner.AssembleSchematicForNpc(owner.Version, Crafter(), Schematic([2100], [2100]), InBag, Facts, Kinds, Accept).Failure);

        // A party record without any seed is a known crafter; its list holds only its added items.
        using var party = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, party.ForceJoinParty(party.Version, "oid:smith").Disposition);
        Assert.IsTrue(party.QueryNpcKnown("oid:smith"));
        Assert.AreEqual(GameSessionDisposition.Changed, party.AddItemToNpc(party.Version, "oid:smith", Part, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, party.AddItemToNpc(party.Version, "oid:smith", Part, Accept).Disposition);
        var made = party.AssembleSchematicForNpc(party.Version, Crafter("oid:smith"), Schematic([2100], [2100]), InBag, Facts, Kinds, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, made.Disposition, made.Failure.ToString());
        CollectionAssert.AreEqual(new[] { "made:3" }, party.QueryNpcInventory("oid:smith").Select(i => i.Entity).ToArray());

        // A list at its 512-item bound cannot take more products than the components it frees.
        using var full = Create();
        var items = Enumerable.Range(0, SessionMapInventorySeed.MaxListLength).Select(i => new SessionSeedItem($"i{i}", ItemFacts(Part), null)).ToArray();
        Assert.AreEqual(GameSessionDisposition.Changed, full.AttachMapInventory(Exterior,
            new([new("smith", SessionHolderKind.Npc, false, false, [], [.. items.Select(i => i.Entity)])], items)).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded,
            full.AssembleSchematicForNpc(full.Version, Crafter(), Schematic([2100], [2100], quantity: 3), InBag, Facts, Kinds, Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed,
            full.AssembleSchematicForNpc(full.Version, Crafter(), Schematic([2100], [2100], quantity: 2), InBag, Facts, Kinds, Accept).Disposition);
    }
}
