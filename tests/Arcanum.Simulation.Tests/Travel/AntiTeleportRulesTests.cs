using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Travel;

// ARC-MAGIC-038: the anti-teleport test 0x4BDD30 on invented zones and places.
[TestClass]
public sealed class AntiTeleportRulesTests
{
    private static readonly AntiTeleportZones Zones = new([new(new(1000, 2000), 640), new(new(5000, 100), 64)], [8, 70]);
    private static AntiTeleportSubject On(int map, long x, long y) => new(true, map, 1, new TravelLocation(x, y));

    [TestMethod]
    public void No_object_refuses_before_anything_else_and_a_module_that_is_off_allows_everything()
    {
        Assert.IsFalse(AntiTeleportRules.Allowed(null, new(false, 1, 1, null)));
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, new(false, 1, 1, new TravelLocation(0, 0))));
        Assert.IsTrue(AntiTeleportRules.Allowed(null, On(8, 1000, 2000), new TravelLocation(1000, 2000)), "off: nothing is measured, not even a disabled map");
    }

    [TestMethod]
    public void A_disabled_map_refuses_wherever_the_object_stands()
    {
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, On(8, 90000, 90000)));
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, On(70, 90000, 90000)));
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, On(9, 90000, 90000)));
    }

    [TestMethod]
    public void The_distance_is_chebyshev_and_strictly_below_the_radius()
    {
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, On(1, 1000 + 639, 2000 + 639)));
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, On(1, 1000 + 640, 2000)), "a distance equal to the radius is outside");
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, On(1, 1000 + 640, 2000 + 639)), "the larger axis counts");
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, On(1, 5000, 100 + 63)), "the second region");
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, On(1, 5000 + 64, 100)));
    }

    [TestMethod]
    public void A_map_without_a_list_row_refuses_and_another_map_is_measured_from_its_area()
    {
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, new(true, 5, 1, null)));
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, new(true, 5, 1, new TravelLocation(1010, 2010))), "the area's place is inside a region");
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, new(true, 5, 1, new TravelLocation(40000, 40000))));
    }

    [TestMethod]
    public void A_target_is_measured_the_same_way_unless_it_is_the_zero_location()
    {
        var from = On(1, 40000, 40000);
        Assert.IsFalse(AntiTeleportRules.Allowed(Zones, from, new TravelLocation(1100, 2100)));
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, from, new TravelLocation(0, 0)));
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, from, new TravelLocation(40000, 41000)));
        Assert.IsTrue(AntiTeleportRules.Allowed(Zones, from, null));
    }

    [TestMethod]
    public void A_negative_radius_excludes_nothing_and_an_empty_module_allows()
    {
        var odd = new AntiTeleportZones([new(new(0, 0), -64)], []);
        Assert.IsTrue(AntiTeleportRules.Allowed(odd, On(1, 0, 0)));
        Assert.IsTrue(AntiTeleportRules.Allowed(new AntiTeleportZones([], []), On(1, 0, 0)));
    }
}
