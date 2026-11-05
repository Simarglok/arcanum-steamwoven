using Arcanum.Data.Maps;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-113 (MIG11 S2): the request a viewer transfer makes (ledger TR1–TR3; action 102 read again at 0x449A85).
[TestClass]
public sealed class AreaChangeOfPlaceTests
{
    [TestMethod]
    public void A_jump_point_is_its_own_caller_and_every_other_site_a_scripts_teleport()
    {
        Assert.AreEqual(ChangeOfPlaceCaller.JumpPoint, AreaChangeOfPlace.Caller(MapTransitionTrigger.JumpPoint));
        foreach (var trigger in new[] { MapTransitionTrigger.TileEntry, MapTransitionTrigger.SectorEntry, MapTransitionTrigger.Use })
            Assert.AreEqual(ChangeOfPlaceCaller.Script, AreaChangeOfPlace.Caller(trigger));
    }

    [TestMethod]
    public void A_plain_teleport_requests_with_flags_zero()
    {
        Assert.AreEqual((ChangeOfPlaceFlags.None, 0), AreaChangeOfPlace.Flags(null));
    }

    [TestMethod]
    public void A_fade_and_teleport_sets_its_flags_as_the_action_does()
    {
        const ChangeOfPlaceFlags fades = ChangeOfPlaceFlags.FadeOut | ChangeOfPlaceFlags.FadeIn;
        Assert.AreEqual((fades, 0), AreaChangeOfPlace.Flags(new ScrTeleportFade(0, 0, 0)));
        Assert.AreEqual((fades | ChangeOfPlaceFlags.AdvanceClock | ChangeOfPlaceFlags.SoundEffect | ChangeOfPlaceFlags.FirstMovie, 3600),
            AreaChangeOfPlace.Flags(new ScrTeleportFade(3600, 5, 2)));
        // The seconds and the sound count only when positive; the movie for any non-zero number (an unsigned test).
        Assert.AreEqual((fades | ChangeOfPlaceFlags.FirstMovie, 0), AreaChangeOfPlace.Flags(new ScrTeleportFade(-5, -1, -1)));
    }

    [TestMethod]
    public void The_largest_fade_passes_the_request_rules_with_its_seconds_bounded()
    {
        var (flags, seconds) = AreaChangeOfPlace.Flags(new ScrTeleportFade(int.MaxValue, 1, 1));
        Assert.AreEqual(ChangeOfPlaceRules.MaxAdvanceSeconds, seconds, "cut to the bound instead of wrapping (CD3)");
        var request = new ChangeOfPlaceRequest("pc", ChangeOfPlaceCaller.Script, 1, new(1, 1), flags, seconds, true, true);
        Assert.AreEqual(ChangeOfPlaceFailure.None, ChangeOfPlaceRules.Validate(request));
    }
}
