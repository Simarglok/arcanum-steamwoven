using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-TECHNOLOGY-1619 (TT12): the shooter candidates of a 30002 trap around the triggerer. Invented objects.</summary>
[TestClass]
public sealed class AreaTrapShootersTests
{
    private static SpellTargetObject At(string id, int type, uint x, uint y) => new(id, type, new(x, y));

    [TestMethod]
    public void The_candidates_are_the_listed_objects_of_the_isometric_window_with_a_use_script_in_the_squares_order()
    {
        var asked = new List<(SpellLocation Centre, int Half, int Mask)>();
        IReadOnlyList<SpellTargetObject> Square(SpellLocation centre, int half, int mask)
        {
            asked.Add((centre, half, mask));
            return [At("near", 3, 52, 50), At("far", 3, 80, 50), At("diagonal", 3, 60, 60), At("noscript", 1, 50, 51), At("last", 0, 49, 49)];
        }
        var scripts = new Dictionary<string, (int, int)> { ["near"] = (30007, 7), ["far"] = (30007, 8), ["diagonal"] = (30007, 9), ["last"] = (30007, 10) };

        var found = AreaTrapShooters.Candidates(new(50, 50), Square, id => scripts.TryGetValue(id, out var use) ? use : null);

        CollectionAssert.AreEqual(new[] { (new SpellLocation(50, 50), AreaTrapShooters.HalfTiles, TrapEffectRules.ShooterTypeMask) }, asked);
        // "far" lies 30 columns away (outside), "diagonal" is 10 and 10 away: |dx - dy| = 0 but |dx + dy| = 20 (outside); "noscript" has no use slot.
        CollectionAssert.AreEqual(new[] { new TrapShooterCandidate("near", 30007, 7), new TrapShooterCandidate("last", 30007, 10) }, found.ToArray());
    }

    [TestMethod]
    public void Without_a_triggerer_there_is_no_list_and_no_square_query()
    {
        var queried = false;
        var found = AreaTrapShooters.Candidates(null, (_, _, _) => { queried = true; return []; }, _ => null);
        Assert.AreEqual((0, false), (found.Count, queried));
    }
}
