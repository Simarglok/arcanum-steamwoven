using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1533 (ledger DIR3): the AREA host's directions seam: the area names and locations of the module's area table, the speaker's location (its T0 cell or where it stands now).</summary>
[TestClass]
public sealed class AreaDialogDirectionsTests
{
    private static readonly WorldAreaTable Table = WorldAreaTable.Create([
        "0, 0, 0, 0/Unknown/Nothing/", "1000, 0, 0, 0/Alpha/First/", "0, 6336, 0, 0/Beta/Second/", "0, 0, 0, 0/Here/Third/", "-5000, -5000, 0, 0/Far/Fourth/"]);

    private static AreaSpeaker Speaker(long x, long y)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        return new AreaSpeaker(2, source.Owner.LoadStrict().Objects.First(), 1, x, y);
    }

    [TestMethod]
    public void Names_come_from_the_table_and_a_number_outside_it_has_none()
    {
        var port = new AreaDialogDirections(Table, Speaker(0, 0), null);
        Assert.AreEqual("Alpha", port.AreaName(1));
        Assert.AreEqual("Unknown", port.AreaName(0));
        Assert.IsNull(port.AreaName(5));
        Assert.IsNull(port.AreaName(-1));
    }

    [TestMethod]
    public void The_bearing_is_the_rotation_and_the_chebyshev_distance_from_the_speaker_to_the_area()
    {
        var speaker = Speaker(0, 0);
        var port = new AreaDialogDirections(Table, speaker, null);
        var npc = AreaConversationState.Npc(2);
        Assert.AreEqual(new DialogBearing(5, 1000), port.Bearing(npc, 1), "the area is toward +x");
        Assert.AreEqual(new DialogBearing(3, 6336), port.Bearing(npc, 2), "toward +y");
        Assert.AreEqual(new DialogBearing(5, 0), port.Bearing(npc, 3), "the same place: rotation 5");
        Assert.AreEqual(new DialogBearing(0, 5000), port.Bearing(npc, 4), "toward -x -y");
        Assert.IsNull(port.Bearing(npc, 5), "outside the table");
        Assert.IsNull(port.Bearing(AreaConversationState.Npc(3), 1), "another speaker");

        var moved = new AreaDialogDirections(Table, speaker, _ => (1000, 6336));
        Assert.AreEqual(new DialogBearing(7, 6336), moved.Bearing(npc, 1), "a companion measures from where it stands now");
        Assert.IsNull(new AreaDialogDirections(Table, speaker, _ => (1L << 40, 0)).Bearing(npc, 1), "a location outside the 32-bit range has no answer");
    }
}
