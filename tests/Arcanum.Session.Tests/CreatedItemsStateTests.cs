using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-081: the items a script's create object adds to the ground (the plant generator of template sectors), on an invented open map.
[TestClass]
public sealed class CreatedItemsStateTests
{
    private static readonly SessionItemKind Herb = new(SessionItemType.Food, 10081, 7001, 10081);
    private static readonly CellCoord Marker = new(300, 400);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    // Every step is open: a path of one step a tile.
    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    private static GameSessionOwner Owner(RuleMode rules = RuleMode.Steamwoven, GameSessionLimits? limits = null, bool hero = false) =>
        GameSessionOwner.Create(Descriptor(limits) with { Hero = hero ? HalfElf() : null, Rules = rules }).Owner!;

    private static InventoryMutationResult Create(GameSessionOwner owner) => owner.CreateGroundItem(owner.Version, Herb, Exterior, Marker, new OpenMap());

    [TestMethod]
    public void A_created_item_lies_next_to_its_object_without_a_hero_and_is_saved()
    {
        using var owner = Owner();
        owner.AttachItemWeights(prototype => prototype == Herb.Prototype ? new SessionItemWeight(2, 0, 5) : null);
        var revision = owner.Version.Revision;
        var created = Create(owner);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", revision + 1), (created.Disposition, created.Item, owner.Version.Revision));
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual((SessionPlacedSource.Created, Herb, Exterior, 5), (placed.Source, placed.Kind, placed.Map, placed.Complexity));
        Assert.IsTrue(CreatedItemRules.Near(Marker, placed.Cell));
        Assert.AreEqual(new SessionItemWeight(2, 0, 5), owner.QueryItemWeight("made:1"));
        // No decay (ledger ID9): nothing waits in the event queue.
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "{\"entity\":\"made:1\",\"source\":\"created\",\"origin\":0,\"map\":79,");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryPlacedItems().ToArray(), restored.QueryPlacedItems().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
    }

    [TestMethod]
    public void Steamwoven_stops_at_the_limit_near_one_object_and_the_original_does_not()
    {
        using var owner = Owner();
        for (var i = 0; i < CreatedItemRules.Limit; i++) Assert.AreEqual(GameSessionDisposition.Changed, Create(owner).Disposition);
        Assert.AreEqual(0, owner.QueryCreatedItemRoom(Exterior, Marker));
        var revision = owner.Version.Revision;
        var full = Create(owner);
        Assert.AreEqual((GameSessionDisposition.NoChange, (string?)null, revision), (full.Disposition, full.Item, owner.Version.Revision));
        // Another object a sector away has room; the rule mode switches live.
        Assert.AreEqual(CreatedItemRules.Limit, owner.QueryCreatedItemRoom(Exterior, new(Marker.X + 64, Marker.Y)));
        owner.SelectRules(RuleMode.Original);
        Assert.IsNull(owner.QueryCreatedItemRoom(Exterior, Marker));
        Assert.AreEqual(GameSessionDisposition.Changed, Create(owner).Disposition);
        Assert.AreEqual(CreatedItemRules.Limit + 1, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void Created_items_have_their_own_budget_and_a_pickup_needs_the_heros_room()
    {
        using var owner = Owner(RuleMode.Original, new GameSessionLimits(maxPcItems: 3, maxCreatedItems: 2), hero: true);
        Assert.AreEqual(GameSessionDisposition.Changed, Create(owner).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, Create(owner).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, Create(owner).Failure);
        // The half-elf's three kit items fill the hero's budget: the created item stays on the ground.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start with { Cell = Marker }).Disposition);
        var first = owner.QueryPlacedItems()[0].Entity;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.PickUpItem(owner.Version, first, Accept).Failure);
    }

    [TestMethod]
    public void A_picked_up_created_item_joins_the_made_items()
    {
        using var owner = Owner(hero: true);
        var entity = Create(owner).Item!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, Start with { Cell = Marker }).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, Accept).Disposition);
        Assert.AreEqual(new SessionMadeItem(entity, Herb), owner.QueryMadeItems().Single());
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        // The next one takes the next made number.
        Assert.AreEqual("made:2", Create(owner).Item);
    }

    [TestMethod]
    public void Ammunition_gold_and_keys_are_not_created_and_a_stale_version_is_refused()
    {
        using var owner = Owner();
        foreach (var type in new[] { SessionItemType.Ammo, SessionItemType.Gold, SessionItemType.Key })
            Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.CreateGroundItem(owner.Version, Herb with { Type = type }, Exterior, Marker, new OpenMap()).Failure);
        var stale = owner.Version;
        Create(owner);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.CreateGroundItem(stale, Herb, Exterior, Marker, new OpenMap()).Failure);
    }
    [TestMethod]
    public void Next_to_a_critter_nothing_is_capped_and_the_refusal_check_matches_the_creation()
    {
        using var owner = Owner();
        for (var i = 0; i < CreatedItemRules.Limit + 2; i++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.CreateGroundItem(owner.Version, Herb, Exterior, Marker, new OpenMap(), DropOwner.PcSide).Disposition);
        Assert.AreEqual(0, owner.QueryCreatedItemRoom(Exterior, Marker), "the object's patch is full, but beside a critter nothing is held back");
        Assert.IsNull(owner.QueryCreateGroundItemRefusal(Herb, Exterior, Marker));
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.QueryCreateGroundItemRefusal(Herb with { Type = SessionItemType.Ammo }, Exterior, Marker));
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.QueryCreateGroundItemRefusal(Herb, Exterior, new(-1, 0)));
        using var small = Owner(limits: new GameSessionLimits(maxCreatedItems: 3));
        Assert.IsNull(small.QueryCreateGroundItemRefusal(Herb, Exterior, Marker, 3));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, small.QueryCreateGroundItemRefusal(Herb, Exterior, Marker, 4));
    }

    [TestMethod]
    public void Created_rows_decode_with_their_own_budget_and_restore_refuses_what_a_script_cannot_create()
    {
        using var owner = Owner();
        owner.AttachItemWeights(_ => new SessionItemWeight(1, 0, -7));
        Create(owner);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"complexity\":-7");
        // A created ammunition row, or one with an origin, is refused; a created row needs no hero.
        Assert.AreEqual(GameSessionFailure.InvalidItem, RestoreFailure(text.Replace("\"type\":\"Food\",\"prototype\":10081", "\"type\":\"Gold\",\"prototype\":10081")));
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical,
            GameSessionSnapshotCodec.TryDecode(text.Replace("\"source\":\"created\",\"origin\":0", "\"source\":\"created\",\"origin\":79")).Failure);
        // Over the policy's created budget the decoder refuses before restore.
        var row = text[(text.IndexOf("{\"entity\":\"made:1\"", StringComparison.Ordinal))..];
        row = row[..(row.IndexOf('}') + 1)];
        var many = string.Join(",", Enumerable.Range(1, GameSessionLimits.PolicyMaxCreatedItems + 1).Select(n => row.Replace("made:1", $"made:{n}")));
        Assert.AreEqual(SnapshotDecodeFailure.BudgetExceeded, GameSessionSnapshotCodec.TryDecode(text.Replace(row, many)).Failure);
    }

    private static GameSessionFailure RestoreFailure(string text) => GameSessionOwner.Restore(Decode(text), RestoreOptions()).Failure;
}

