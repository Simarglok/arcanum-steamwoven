using Arcanum.Data.Lighting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// ARC-DEV-335 (V7d, ledger LT12): a critter's carried light is light ART 1 with one colour per size. Invented masks; no original data.
[TestClass]
public sealed class CarriedLightTests
{
    private static StationaryLightFrame Frame(int number)
    {
        var mask = new byte[4 * 4 * 4];
        for (var at = 0; at < mask.Length; at += 4) { mask[at] = 80; mask[at + 3] = 255; }
        return AreaStationaryLightSource.Create(5000, [], [(number, 4, 4, 2, 2, mask)]).Frames[number];
    }

    [TestMethod]
    public void Each_size_has_its_own_colour_and_none_has_none()
    {
        Assert.AreEqual(new AmbientColor(244, 255, 255), CarriedLightRules.Color(CarriedLightSize.ExtraLarge));
        Assert.AreEqual(new AmbientColor(255, 214, 172), CarriedLightRules.Color(CarriedLightSize.Large));
        Assert.AreEqual(new AmbientColor(236, 138, 85), CarriedLightRules.Color(CarriedLightSize.Medium));
        Assert.AreEqual(new AmbientColor(167, 88, 58), CarriedLightRules.Color(CarriedLightSize.Small));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarriedLightRules.Color(CarriedLightSize.None));
    }

    [TestMethod]
    public void A_carried_light_is_a_light_of_art_one_with_flags_zero_at_the_critters_place()
    {
        var light = CarriedLightRules.ToLight(new(92928, 82560, 3, -4, CarriedLightSize.Medium));
        Assert.AreEqual(1, light.ArtNumber);
        Assert.AreEqual(0, light.Frame);
        Assert.AreEqual(StationaryLightFlags.None, light.Flags);
        Assert.AreEqual(new AmbientColor(236, 138, 85), light.Color);
        Assert.AreEqual((92928L, 82560L, 3, -4), (light.X, light.Y, light.OffsetX, light.OffsetY));
        Assert.IsNull(light.Owner);
        Assert.ThrowsExactly<ArgumentException>(() => CarriedLightRules.ToLight(new(1, 1, 0, 0, CarriedLightSize.None)));
    }

    [TestMethod]
    public void The_source_holds_the_lights_over_the_one_frame_and_refuses_another_art()
    {
        var source = AreaStationaryLightSource.ForCarried(5000, Frame(1), [new(92928, 82560, 0, 0, CarriedLightSize.Large), new(92930, 82560, 0, 0, CarriedLightSize.Small)]);
        Assert.AreEqual(2, source.Lights.Count);
        Assert.AreEqual(1, source.Frames.Count);
        Assert.ThrowsExactly<ArgumentException>(() => AreaStationaryLightSource.ForCarried(5000, Frame(2), []));
        // The field places each light's rectangle at the critter's tile plus its offsets and the (40, 20) tile centre, less the hotspot.
        var field = new StationaryLightField(source, 92928, 82560);
        var rect = field.Placements[0].Rect;
        Assert.AreEqual((40 - 2, 20 - 2, 4, 4), (rect.X, rect.Y, rect.Width, rect.Height));
    }
}
