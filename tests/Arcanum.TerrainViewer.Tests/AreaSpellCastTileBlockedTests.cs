using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-055 (PB1, 0x4D7110): the cast map's tile-blocked test is the step evidence's terrain verdict, so the "empty tile" areas (Wall of Fire, Wall of Stone, Fireflash) refuse
/// blocking terrain; before it asked only whether the tile's sector was admitted. Invented evidence, no original data.</summary>
[TestClass]
public sealed class AreaSpellCastTileBlockedTests
{
    private static AreaStepTerrainEvidence Open(int x) => new(new(x, 0), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
    private static AreaStepTerrainEvidence Blocked(int x) => new(new(x, 0), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, AreaStepTerrainSource.TileName, "TEST");
    private static AreaStepTerrainEvidence Unknown(int x) => new(new(x, 0), 0, CardinalTraversalCoverage.Unknown, null, null, "TEST");

    [TestMethod]
    public void A_tile_is_blocked_when_the_terrain_verdict_blocks_or_cannot_speak_or_the_sector_is_not_admitted_ARC_MAGIC_055()
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence> { [new(0, 0)] = Open(0), [new(1, 0)] = Blocked(1), [new(2, 0)] = Unknown(2) };
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var map = new AreaSpellCastMap(ConversationSessionFixture.Create(), 1, steps, [], new AreaCombatHost(new AreaInventoryHost([])), () => null, () => null);
        Assert.IsFalse(map.TileBlocked(new SpellLocation(0, 0)), "open terrain in an admitted sector");
        Assert.IsTrue(map.TileBlocked(new SpellLocation(1, 0)), "blocking terrain");
        Assert.IsTrue(map.TileBlocked(new SpellLocation(2, 0)), "terrain the evidence cannot speak for counts as blocked, as the drop map and the path request count it");
        Assert.IsTrue(map.TileBlocked(new SpellLocation(3, 0)), "an admitted sector's tile without a verdict");
        Assert.IsTrue(map.TileBlocked(new SpellLocation(64, 0)), "a sector that is not admitted");
        Assert.IsTrue(map.PathExists(new SpellTargetObject("s", 16, new SpellLocation(0, 0)), new SpellLocation(0, 0)), "a path to an open tile");
        Assert.IsFalse(map.PathExists(new SpellTargetObject("s", 16, new SpellLocation(0, 0)), new SpellLocation(1, 0)), "no path to a blocked tile");
    }
}
