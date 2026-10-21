using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-007: target areas (ledger TA1–TA16, MG18, MG21–MG25). Objects, masks and the world double are invented; the masks are
// ORs of the original's target-name values.
[TestClass]
public sealed class SpellTargetRulesTests
{
    private const ulong Self = 0x1, Source = 0x2, Object = 0x4, ObjRadius = 0x10, Critter = 0x44, NoSelf = 0x10000004, NoDead = 0x80000004,
        Summoned = 0x400000000000, Tile = 0x800000000000, TileSelf = 0x1000000000000, TileEmpty = 0x4800000000000, TileRadius = 0x20800000000000,
        Wall = 0x40800000000000, Cone = 0x200000000000000, AllParty = 0x400000000000044, NonParty = 0x1000000000000004, ParentObj = 0x2000000000000004,
        List = 0x8000000000000000;
    private const ulong NotInvulnerable = SpellTargetRules.NotInvulnerable;

    private static readonly SpellLocation Here = new(100, 100);
    private static readonly SpellTargetObject Mage = new("mage", 15, Here, HitPoints: 30);

    private sealed class World : ISpellTargetWorld
    {
        public List<SpellTargetObject> Objects { get; } = [];
        public HashSet<SpellLocation> Blocked { get; } = [];
        public List<SpellTargetObject> PartyMembers { get; } = [];
        public int Facing { get; init; } = 1;

        public SpellTargetObject? Object(string id) => Objects.FirstOrDefault(o => o.Id == id);

        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) =>
            Objects.Where(o => SpellLocation.Distance(o.Location, centre) <= radius && (typeMask & (1 << o.Type)) != 0).ToArray();

        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) =>
            Objects.Where(o => o.Location == tile && (typeMask & (1 << o.Type)) != 0).ToArray();

        public bool TileBlocked(SpellLocation tile) => Blocked.Contains(tile);

        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;

        public int SectorKind(SpellLocation location) => 0;

        public int Direction(SpellLocation from, SpellLocation to) => Facing;

        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => PartyMembers;
    }

    private static bool Passes(ulong mask, SpellTargetObject candidate, SpellTargetObject? self = null, SpellTargetObject? other = null,
        MagicAnomalyProfile? profile = null) =>
        SpellTargetRules.Test(new(new(mask), self ?? Mage, self ?? Mage, Here, candidate, Other: other), new World(), profile ?? MagicAnomalyProfile.Steamwoven).Passes;

    [TestMethod]
    public void Mg18TheNotDeadNameSparesEveryLivingObjectAsTheOriginalIntends()
    {
        const ulong bolt = Object | NoSelf | NoDead | NotInvulnerable;   // record 33's areas
        Assert.IsTrue(Passes(bolt, new("door", 1, Here, HitPoints: 40)), "a door with hit points");
        Assert.IsTrue(Passes(bolt, new("chest", 2, Here, HitPoints: 20)));
        Assert.IsTrue(Passes(bolt, new("orc", 16, Here, HitPoints: 12)));
        Assert.IsFalse(Passes(bolt, new("corpse", 16, Here, HitPoints: 0)), "the not-dead test runs without the critter bit");
        Assert.IsFalse(Passes(bolt, new("wreck", 2, Here, HitPoints: 0)), "for any object without hit points");
        Assert.IsFalse(Passes(bolt, Mage), "not the caster");
        Assert.IsFalse(Passes(bolt, new("golem", 16, Here, ObjectFlags: SpellTargetObject.Invulnerable)));
        Assert.IsFalse(Passes(bolt | Critter, new("door", 1, Here, HitPoints: 40)), "the critter bit would narrow it to critters");
    }

    [TestMethod]
    public void Mg21NonPartySparesObjectsAndCrittersOutsideBothParties()
    {
        var friend = new SpellTargetObject("friend", 16, Here, PcLeader: "mage");
        var ally = new SpellTargetObject("ally", 16, Here, PcLeader: "hero");
        var hero = new SpellTargetObject("hero", 15, Here);
        Assert.IsTrue(Passes(NonParty, new("door", 1, Here)));
        Assert.IsTrue(Passes(NonParty, new("wolf", 16, Here)));
        Assert.IsFalse(Passes(NonParty, friend), "a follower of the caster");
        Assert.IsFalse(Passes(NonParty, ally, other: hero), "a follower of the other object");
        Assert.IsFalse(Passes(NonParty, hero, other: hero), "the other object itself");
        Assert.IsTrue(Passes(NonParty, Mage), "a PC caster is not excluded in single player");
        Assert.IsFalse(Passes(NonParty | Critter, new("door", 1, Here)), "with the critter bit only critters");
    }

    [TestMethod]
    public void Mg22RadiusIsAnObjectKindAndMg23TileSelfIsGatheredWithoutTheTileBit()
    {
        var near = new SpellTargetObject("near", 16, new(102, 100));
        var far = new SpellTargetObject("far", 16, new(104, 100));
        var world = new World();
        SpellTargetContext Context(SpellTargetObject o) => new(new(ObjRadius, Radius: 2), Mage, Mage, Here, o, TargetTile: Here);
        Assert.IsTrue(SpellTargetRules.Test(Context(near), world, MagicAnomalyProfile.Steamwoven).Passes);
        Assert.IsFalse(SpellTargetRules.Test(Context(far), world, MagicAnomalyProfile.Steamwoven).Passes, "the distance from the target tile");
        var tileSelf = SpellTargetRules.Collect(new(new(TileSelf), Mage, Mage, Here), world, MagicAnomalyProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { new SpellTargetEntry(null, Here) }, tileSelf.Entries.ToArray(), "the source's tile");
        Assert.IsTrue(SpellTargetRules.Test(new(new(TileSelf), Mage, Mage, Here, CandidateTile: new(9, 9)), world, MagicAnomalyProfile.Steamwoven).Passes,
            "without the tile bit no tile test runs");
    }

    [TestMethod]
    public void TheTileTestsNeedTheTileBitAndTheRadiusTestNeverRefuses()
    {
        var world = new World();
        world.Blocked.Add(new(5, 5));
        SpellTargetContext At(ulong mask, SpellLocation tile, int radius = 0) => new(new(mask, Radius: radius), Mage, Mage, Here, CandidateTile: tile);
        Assert.IsFalse(SpellTargetRules.Test(At(TileEmpty, new(5, 5)), world, MagicAnomalyProfile.Steamwoven).Passes);
        Assert.IsTrue(SpellTargetRules.Test(At(TileEmpty & ~Tile, new(5, 5)), world, MagicAnomalyProfile.Steamwoven).Passes);
        world.Objects.Add(new("barrel", 3, new(6, 6)));
        world.Objects.Add(new("coin", 9, new(7, 7)));
        Assert.IsFalse(SpellTargetRules.Test(At(TileEmpty, new(6, 6)), world, MagicAnomalyProfile.Steamwoven).Passes, "scenery blocks");
        Assert.IsTrue(SpellTargetRules.Test(At(TileEmpty, new(7, 7)), world, MagicAnomalyProfile.Steamwoven).Passes, "items do not");
        Assert.IsTrue(SpellTargetRules.Test(At(TileRadius, new(900, 900), 1), world, MagicAnomalyProfile.Steamwoven).Passes, "the origin's distance to itself");
        Assert.IsFalse(SpellTargetRules.Test(At(TileRadius, new(900, 900), -1), world, MagicAnomalyProfile.Steamwoven).Passes);
        Assert.IsFalse(SpellTargetRules.Test(At(Tile, SpellLocation.None), world, MagicAnomalyProfile.Steamwoven).Passes);
        Assert.IsTrue(SpellTargetRules.Test(At(Tile | List, SpellLocation.None), world, MagicAnomalyProfile.Steamwoven).Passes);
    }

    [TestMethod]
    public void Mg25TheSummonedTestReadsTheSpellFlagInSteamwoven()
    {
        var surrendered = new SpellTargetObject("thrall", 16, Here, CritterFlags: SpellTargetObject.Surrendered);
        var summoned = new SpellTargetObject("imp", 16, Here, SpellFlags: SpellTargetObject.Summoned);
        const ulong mask = Object | Critter | Summoned;   // records 183 and 217 join these bits
        Assert.IsTrue(Passes(mask, surrendered, profile: MagicAnomalyProfile.Original), "the original reads the critter flags word");
        Assert.IsFalse(Passes(mask, summoned, profile: MagicAnomalyProfile.Original));
        Assert.IsFalse(Passes(mask, surrendered, profile: MagicAnomalyProfile.Steamwoven));
        Assert.IsTrue(Passes(mask, summoned, profile: MagicAnomalyProfile.Steamwoven));
        Assert.IsTrue(Passes(Object | Summoned, summoned, profile: MagicAnomalyProfile.Original), "without the critter bit both read the spell flag");
    }

    [TestMethod]
    public void TheCollectionGathersInTheOriginalsOrderWithoutDuplicates()
    {
        var world = new World();
        var source = new SpellTargetObject("wand", 6, new(101, 100), Owner: "mage");
        world.Objects.Add(Mage);
        var target = new SpellTargetObject("orc", 16, new(103, 100));
        var imp = new SpellTargetObject("imp", 16, new(99, 99));
        world.PartyMembers.Add(Mage);
        world.PartyMembers.Add(new("virgil", 16, Here, PcLeader: "mage"));
        var context = new SpellTargetContext(new(Self | Source | Object | Summoned | Tile | AllParty | ParentObj), Mage, source, Here, Target: target,
            TargetTile: new(103, 100), Summoned: imp, SummonedList: [imp, target]);
        var ids = SpellTargetRules.Collect(context, world, MagicAnomalyProfile.Steamwoven).Entries.Select(e => e.Object?.Id ?? $"{e.Tile.X},{e.Tile.Y}");
        CollectionAssert.AreEqual(new[] { "mage", "wand", "orc", "imp", "103,100", "virgil" }, ids.ToArray());
        Assert.AreEqual(0, SpellTargetRules.Collect(context with { Area = new(Self | 0x10000000) }, world, MagicAnomalyProfile.Steamwoven).Entries.Count, "no-self");
        var gone = Mage with { ObjectFlags = SpellTargetObject.Off };
        Assert.AreEqual(0, SpellTargetRules.Collect(context with { Area = new(Self), Self = gone }, world, MagicAnomalyProfile.Steamwoven).Entries.Count);
        Assert.AreEqual(1, SpellTargetRules.Collect(context with { Area = new(Object), Target = gone }, world, MagicAnomalyProfile.Steamwoven).Entries.Count,
            "the target object is only refused when destroyed");
    }

    [TestMethod]
    public void RadiiConesAndWallsAreSquaresAndLinesWithTheCount()
    {
        var world = new World();
        const ulong radiusOnly = TileRadius & ~Tile;   // the name carries the tile bit, which would add the target tile first
        var context = new SpellTargetContext(new(radiusOnly, Radius: 1), Mage, Mage, Here, TargetTile: Here);
        var square = SpellTargetRules.Collect(context, world, MagicAnomalyProfile.Steamwoven).Entries;
        Assert.AreEqual(9, square.Count);
        Assert.AreEqual((new SpellLocation(99, 99), new SpellLocation(101, 101)), (square[0].Tile, square[8].Tile), "rows first, from the top left");
        Assert.AreEqual(4, SpellTargetRules.Collect(context with { Area = new(radiusOnly, Radius: 1, Count: 4) }, world, MagicAnomalyProfile.Steamwoven).Entries.Count);
        var wall = SpellTargetRules.Collect(context with { Area = new(Wall, Radius: 2), Origin = new(100, 90) }, world, MagicAnomalyProfile.Steamwoven).Entries;
        CollectionAssert.AreEqual(new[] { new SpellLocation(100, 100), new(100, 99), new(100, 101), new(100, 98), new(100, 102) },
            wall.Select(e => e.Tile).ToArray(), "direction 1 steps (0, −1) both ways from the centre");
        world.Objects.Add(new("wolf", 16, new(102, 98)));
        world.Objects.Add(new("rock", 3, new(101, 101)));
        var cone = SpellTargetRules.Collect(context with { Area = new(Cone | Self | Critter | NoSelf, Radius: 2), TargetTile = SpellLocation.None }, world,
            MagicAnomalyProfile.Steamwoven).Entries;
        CollectionAssert.AreEqual(new[] { "wolf" }, cone.Select(e => e.Object!.Id).ToArray(), "the 'cone' is a square of objects with the self bit");
        Assert.AreEqual(25, SpellTargetRules.Collect(context with { Area = new(Cone, Radius: 2) }, world, MagicAnomalyProfile.Steamwoven).Entries.Count,
            "without it a square of tiles");
    }

    [TestMethod]
    public void Mg24TheListHoldsAtMost256Entries()
    {
        var context = new SpellTargetContext(new(TileRadius, Radius: 9), Mage, Mage, Here, TargetTile: Here);
        Assert.AreEqual(SpellTargetRules.Capacity, SpellTargetRules.Collect(context, new World(), MagicAnomalyProfile.Original).Entries.Count,
            "19 × 19 tiles would overflow the original's list; both modes stop at 256");
    }

    [TestMethod]
    public void TheRunListIsBuiltOnceInReverseAndReusedAfter()
    {
        var world = new World();
        var orc = new SpellTargetObject("orc", 16, new(101, 100));
        var wolf = new SpellTargetObject("wolf", 16, new(102, 100));
        world.Objects.AddRange([orc, wolf]);
        var context = new SpellTargetContext(new(ObjRadius | Critter | NoSelf | List, Radius: 3), Mage, Mage, Here, TargetTile: Here, RunList: []);
        var first = SpellTargetRules.Collect(context, world, MagicAnomalyProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { "wolf", "orc" }, first.BuiltRunList!.Select(o => o.Id).ToArray());
        var later = SpellTargetRules.Collect(new(new(List), Mage, Mage, RunList: first.BuiltRunList), world, MagicAnomalyProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { "wolf", "orc" }, later.Entries.Select(e => e.Object!.Id).ToArray());
        Assert.IsNull(later.BuiltRunList);
        Assert.AreEqual(0, SpellTargetRules.Collect(new(new(List), Mage, Mage), world, MagicAnomalyProfile.Steamwoven).Entries.Count, "no run list");
    }

    [TestMethod]
    public void ComponentsFilterReAimAndTellTheAi()
    {
        Assert.IsTrue(SpellTargetRules.TriggersAllow(recordHasTriggers: false, 0x4, 0));
        Assert.IsTrue(SpellTargetRules.TriggersAllow(true, 0, 0x2), "a component without triggers");
        Assert.IsFalse(SpellTargetRules.TriggersAllow(true, 0x4, 0x2));
        Assert.IsFalse(SpellTargetRules.TriggersAllow(true, 0x104, 0x4));
        Assert.IsTrue(SpellTargetRules.TriggersAllow(true, 0x104, 0x104));

        var orc = new SpellTargetObject("orc", 16, new(5, 5));
        var imp = new SpellTargetObject("imp", 16, new(7, 7));
        Assert.AreEqual((orc, Here), SpellTargetRules.ReAim(0, orc, Here, Mage, null, imp, new(1, 1), null));
        Assert.AreEqual((Mage, Here), SpellTargetRules.ReAim(Self, orc, Here, Mage, null, imp, new(1, 1), null));
        Assert.AreEqual(((SpellTargetObject?)null, Here), SpellTargetRules.ReAim(Self | Source, orc, Here, Mage, null, imp, new(1, 1), null), "source after self");
        Assert.AreEqual((imp, new SpellLocation(1, 1)), SpellTargetRules.ReAim(Summoned | Tile, orc, Here, Mage, null, imp, new(1, 1), null));
        Assert.AreEqual((imp, new SpellLocation(7, 7)), SpellTargetRules.ReAim(Summoned | 0x80800000000000, orc, Here, Mage, null, imp, new(1, 1), null));
        Assert.AreEqual((orc, new SpellLocation(9, 9)), SpellTargetRules.ReAim(Tile, orc, Here, Mage, null, imp, SpellLocation.None, orc with { Location = new(9, 9) }));

        var (notices, remembered) = SpellTargetRules.AiNotices(true, 0, "orc", "mage", "hero", null);
        CollectionAssert.AreEqual(new[] { ((string?)"mage", "orc"), ("hero", "orc") }, notices.ToArray());
        Assert.AreEqual("orc", remembered);
        Assert.AreEqual(0, SpellTargetRules.AiNotices(true, 0, "orc", "mage", null, "orc").Notices.Count, "once per target in a row");
        Assert.AreEqual(0, SpellTargetRules.AiNotices(true, 2, "orc", "mage", null, null).Notices.Count, "not at END");
        Assert.AreEqual(0, SpellTargetRules.AiNotices(false, 0, "orc", "mage", null, null).Notices.Count);
        Assert.AreEqual(1, SpellTargetRules.AiNotices(true, 0, "orc", "mage", "mage", null).Notices.Count, "the other object only when it is not the parent");
    }

    [TestMethod]
    public void TheInvocationCheckAsksTheAreaRules()
    {
        var world = new World();
        var shaman = new SpellObjectFacts("shaman", 16, new(10, 10), Intelligence: 10);
        var hero = new SpellObjectFacts("hero", 15, new(14, 10));
        world.Objects.Add(new("hero", 15, new(14, 10)));
        var areas = new SpellInvocationAreas(world, spell => spell == 1 ? Tile | TileEmpty : Object | Critter, (_, _) => new(0, null), MagicAnomalyProfile.Steamwoven);
        var atHero = new SpellInvocation(1, shaman, shaman, hero, SpellLocation.None);
        Assert.IsTrue(areas.BeginAreaTakesTiles(1));
        Assert.IsFalse(areas.BeginAreaTakesTiles(2));
        Assert.IsFalse(areas.TargetMatchesBeginArea(atHero), "a tile spell aimed at an object without a tile");
        Assert.AreEqual(new SpellLocation(12, 8), areas.FirstTileInBeginArea(atHero, SpellInvocationRules.FallbackRadius),
            "the hero's own tile is taken; the radius-2 square from its top left gives the first empty tile");
        Assert.IsTrue(areas.TargetMatchesBeginArea(atHero with { Spell = 2 }));
        var result = SpellInvocationRules.Check(atHero, new(false, 10, 0, 0, 0), areas, new NoRolls());
        Assert.AreEqual((SpellInvocationRefusal.None, new SpellLocation(12, 8)), (result.Refusal, result.ReplacedTile));
    }

    [TestMethod]
    public void ReviewEdgesConeTypesMissingCentresAndTheLeaderClause()
    {
        var world = new World();
        world.Objects.Add(new("wolf", 16, new(101, 100)));
        world.Objects.Add(new("rock", 3, new(100, 101)));
        // The cone asks every type only with the object bit; the radius bit alone keeps the scenery-only query (no target tile:
        // the cone centres on the source, the radius gather on nothing near).
        var cone = new SpellTargetContext(new(Cone | Self | 0x10000000 | ObjRadius, Radius: 1), Mage, Mage, Here);
        Assert.AreEqual(0, SpellTargetRules.Collect(cone, world, MagicAnomalyProfile.Steamwoven).Entries.Count(e => e.Object?.Id == "wolf"));
        Assert.AreEqual(1, SpellTargetRules.Collect(cone with { Area = cone.Area with { Targets = cone.Area.Targets | Object } }, world,
            MagicAnomalyProfile.Steamwoven).Entries.Count(e => e.Object?.Id == "wolf"));
        // MG26: a tile radius or wall with neither target tile nor target object gathers nothing.
        var nowhere = new SpellTargetContext(new((TileRadius | Wall) & ~Tile, Radius: 1), Mage, Mage, Here);
        Assert.AreEqual(0, SpellTargetRules.Collect(nowhere, world, MagicAnomalyProfile.Original).Entries.Count);
        Assert.IsFalse(SpellTargetRules.Test(new(new(Tile | TileSelf), Mage, Mage, Here, CandidateTile: Here), world, MagicAnomalyProfile.Original).Passes,
            "tile-self without a candidate object has no location to match");
        Assert.AreEqual(SpellLocation.None, SpellTargetRules.ReAim(Tile, null, Here, Mage, null, null, SpellLocation.None, null).Tile);
        // Non-party: a follower casting spares its own PC leader.
        var hero = new SpellTargetObject("hero", 15, Here);
        var virgil = new SpellTargetObject("virgil", 16, Here, PcLeader: "hero");
        Assert.IsFalse(Passes(NonParty, hero, self: virgil), "the candidate is the caster's PC leader");
    }

    private sealed class NoRolls : ISpellRollSource
    {
        public int Roll(SpellRollPurpose purpose, int min, int max) => throw new AssertFailedException("no roll");
    }
}
