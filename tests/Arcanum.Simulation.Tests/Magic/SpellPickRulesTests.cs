using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-021: the targeting cursor's pick (0x4F28A0, 0x4F2680; ledger SU15–SU19). Objects, masks and the world double are
// invented; the masks are ORs of the original's target-name values.
[TestClass]
public sealed class SpellPickRulesTests
{
    private const ulong Object = 0x4, NoSelf = 0x10000004, NoDead = 0x80000004, Tile = 0x800000000000, NonParty = 0x1000000000000004;

    private static readonly SpellLocation Clicked = new(100, 100), Elsewhere = new(103, 101);
    private static readonly SpellTargetObject Hero = new("hero", SpellTargetObject.Pc, Clicked, HitPoints: 30);
    private static readonly string[] NoFollowers = [];

    private sealed class World : ISpellTargetWorld
    {
        public List<SpellTargetObject> Objects { get; } = [];
        public List<int> AskedTypes { get; } = [];

        public SpellTargetObject? Object(string id) => Objects.FirstOrDefault(o => o.Id == id);
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];

        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask)
        {
            AskedTypes.Add(typeMask);
            return Objects.Where(o => o.Location == tile && (typeMask & (1 << o.Type)) != 0).ToArray();
        }

        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
    }

    // A test double that passes the named candidates (an object id, or "tile x,y") and logs every call in order.
    private sealed class Tests(params string[] passing)
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, ulong> Reasons { get; } = [];

        public SpellTargetTest Run(SpellTargetObject? o, SpellLocation tile)
        {
            var name = o is null ? $"tile {tile.X},{tile.Y}" : tile.IsNone ? o.Id : $"{o.Id}@{tile.X},{tile.Y}";
            Calls.Add(name);
            return new(passing.Contains(name), Reasons.GetValueOrDefault(name));
        }
    }

    private static SpellPick Pick(ulong mask, World world, Tests tests, SpellTargetObject? atPoint, bool noTile = false,
        IReadOnlyCollection<string>? followers = null) =>
        SpellUiRules.Pick(mask, Hero.Id, followers ?? NoFollowers, atPoint, noTile ? null : Clicked, world, tests.Run);

    [TestMethod]
    public void The_object_under_the_point_comes_first_then_the_tile_list_then_the_tile()
    {
        var front = new SpellTargetObject("front", SpellTargetObject.Npc, Elsewhere);
        var world = new World();
        world.Objects.AddRange([Hero, new("chest", SpellTargetObject.Container, Clicked), new("orc", SpellTargetObject.Npc, Clicked)]);

        var hit = Pick(Object, world, new Tests("front", "chest"), front);
        Assert.AreEqual("front", hit.Object?.Id, "the screen pick's object wins");

        var tests = new Tests("orc", "tile 100,100");
        var listed = Pick(Object, world, tests, front);
        Assert.AreEqual("orc", listed.Object?.Id);
        CollectionAssert.AreEqual(new[] { "front", "front@103,101", "hero", "hero@100,100", "chest", "chest@100,100", "orc" }, tests.Calls,
            "each object is tried as itself, then as its own tile, in the tile list's order");
        CollectionAssert.AreEqual(new[] { SpellUiRules.PickTileTypes }, world.AskedTypes, "every type but projectiles");

        var onTile = Pick(Object | Tile, world, new Tests("tile 100,100"), null);
        Assert.AreEqual(((SpellTargetObject?)null, Clicked, true), (onTile.Object, onTile.Tile, onTile.Found), "the clicked tile last");

        var offMap = new Tests("tile 100,100", "orc");
        Assert.IsFalse(Pick(Object | Tile, world, offMap, front, noTile: true).Found, "no tile under the point: no tile list, no tile");
        CollectionAssert.AreEqual(new[] { "front", "front@103,101" }, offMap.Calls);
    }

    [TestMethod]
    public void An_object_that_fails_stands_for_its_own_tile()
    {
        // A tile spell clicked on a body standing elsewhere than the clicked tile casts at the body's tile (0x4F2680's second test).
        var body = new SpellTargetObject("body", SpellTargetObject.Npc, Elsewhere);
        var pick = Pick(Tile, new World(), new Tests("body@103,101", "tile 100,100"), body);
        Assert.AreEqual(((SpellTargetObject?)null, Elsewhere, true), (pick.Object, pick.Tile, pick.Found));
    }

    [TestMethod]
    public void A_click_through_object_is_refused_untested_and_a_zero_mask_finds_nothing()
    {
        var glass = new SpellTargetObject("glass", SpellTargetObject.Scenery, Elsewhere, SpellUiRules.ClickThrough);
        var tests = new Tests("glass", "glass@103,101", "tile 100,100");
        Assert.AreEqual(Clicked, Pick(Tile, new World(), tests, glass).Tile);
        CollectionAssert.AreEqual(new[] { "tile 100,100" }, tests.Calls);

        var zero = new Tests("glass", "tile 100,100");
        Assert.IsFalse(Pick(0, new World(), zero, null).Found, "a mask of 0 refuses whatever the area test says");
    }

    [TestMethod]
    public void The_mask_spares_the_pc_its_followers_and_the_dead()
    {
        var virgil = new SpellTargetObject("virgil", SpellTargetObject.Npc, Clicked, PcLeader: "hero");
        var corpse = new SpellTargetObject("corpse", SpellTargetObject.Npc, Clicked, HitPoints: 0);
        var wreck = new SpellTargetObject("wreck", SpellTargetObject.Container, Clicked, HitPoints: 0);
        string[] followers = ["virgil"];
        Assert.IsTrue(SpellUiRules.PickSpares(NoSelf, Hero, Hero.Id, followers));
        Assert.IsFalse(SpellUiRules.PickSpares(Object, Hero, Hero.Id, followers));
        Assert.IsTrue(SpellUiRules.PickSpares(NonParty, virgil, Hero.Id, followers));
        Assert.IsFalse(SpellUiRules.PickSpares(NonParty, Hero, Hero.Id, followers), "the follower list holds no PC");
        Assert.IsFalse(SpellUiRules.PickSpares(Object, virgil, Hero.Id, followers));
        Assert.IsTrue(SpellUiRules.PickSpares(NoDead, corpse, Hero.Id, followers));
        Assert.IsFalse(SpellUiRules.PickSpares(NoDead, wreck, Hero.Id, followers), "only dead critters");

        var world = new World();
        world.Objects.AddRange([Hero, virgil, corpse]);
        var tests = new Tests("hero", "virgil", "corpse", "tile 100,100");
        var spared = Pick(NoSelf | NonParty | NoDead, world, tests, null, followers: followers);
        Assert.AreEqual(((SpellTargetObject?)null, Clicked), (spared.Object, spared.Tile));
        CollectionAssert.AreEqual(new[] { "tile 100,100" }, tests.Calls, "the spared objects are refused untested");
    }

    [TestMethod]
    public void A_scenery_object_stands_for_its_occupant_and_destroyed_or_off_objects_are_not_listed()
    {
        var sleeper = new SpellTargetObject("sleeper", SpellTargetObject.Npc, Clicked);
        var bed = new SpellTargetObject("bed", SpellTargetObject.Scenery, Clicked, Occupant: "sleeper");
        var world = new World();
        world.Objects.AddRange([sleeper, new("ash", SpellTargetObject.Npc, Clicked, SpellTargetObject.Destroyed),
            new("lamp", SpellTargetObject.Scenery, Clicked, SpellTargetObject.Off), new("arrow", 4, Clicked)]);
        Assert.AreEqual("sleeper", Pick(Object, world, new Tests("sleeper"), bed).Object?.Id, "0x4F2C60");

        var tests = new Tests();
        Pick(Object, world, tests, null);
        CollectionAssert.AreEqual(new[] { "sleeper", "sleeper@100,100", "tile 100,100" }, tests.Calls, "no destroyed, switched-off or projectile object");
    }

    [TestMethod]
    public void A_pick_that_finds_nothing_answers_the_last_reason_its_tests_left()
    {
        var hurt = new SpellTargetObject("hurt", SpellTargetObject.Npc, Clicked);
        var world = new World();
        world.Objects.Add(hurt);
        var tests = new Tests();
        tests.Reasons["hurt"] = SpellUiRules.DamagedBit;
        var pick = Pick(Object, world, tests, null);
        Assert.AreEqual((false, SpellUiRules.DamagedBit), (pick.Found, pick.Test.Reason), "a later test without a reason keeps it");
        Assert.AreEqual(0UL, Pick(Object, world, new Tests(), null).Test.Reason, "no earlier pick's reason (MG42)");
    }
}
