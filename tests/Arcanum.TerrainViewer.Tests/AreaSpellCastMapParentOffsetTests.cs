using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-COMBAT-1934 (0x4ADE00, ledger RA7, VC56): the line of fire of a cast starts at the pixel offset of its parent. The cast map asks its <see cref="AreaSpellCastMap.ParentOffset"/> hook
/// (the viewer wires the AI host's shooter offset) with the parent's id; with no hook, or no map, the line starts at the cell centre. Invented rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaSpellCastMapParentOffsetTests
{
    private static AreaSpellCastMap Map(AreaAiHostRebindTests.Fixture f)
    {
        var steps = AreaAiHostRebindTests.Sector(0, 0);
        return new AreaSpellCastMap(f.Session, 1, steps, [f.RowA, f.RowB], f.Combat, () => new CellCoord(58, 12),
            () => new AreaLineOfFire(steps, [f.RowA, f.RowB], new HashSet<AreaObjectKey>(), new Dictionary<CellCoord, AreaObjectRecord>(), _ => false, _ => false, _ => null));
    }

    [TestMethod]
    public void The_cast_line_asks_the_parent_offset_with_the_parents_id()
    {
        using var f = AreaAiHostRebindTests.Open();
        var map = Map(f);
        var asked = new List<string>();
        map.ParentOffset = id => { asked.Add(id); return (-40, 20); };
        var parent = new SpellObjectFacts(f.A, 16, new(60, 10), 0, 0);
        var line = map.LineOfFire(parent, new(60, 14));
        CollectionAssert.AreEqual(new[] { f.A }, asked);
        Assert.IsNull(line.Blocker);
    }

    [TestMethod]
    public void The_cast_line_without_a_hook_starts_at_the_cell_centre()
    {
        using var f = AreaAiHostRebindTests.Open();
        var map = Map(f);
        var parent = new SpellObjectFacts(f.A, 16, new(60, 10), 0, 0);
        var line = map.LineOfFire(parent, new(60, 14));
        Assert.IsNull(line.Blocker);
        Assert.IsNull(map.ParentOffset);
    }
}
