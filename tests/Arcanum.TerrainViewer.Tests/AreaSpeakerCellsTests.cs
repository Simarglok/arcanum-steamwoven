using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1601 (exe 0x406DA0 field 2): a <c>d:</c>/<c>x:</c> reply measures from where the speaker stands now: a companion's cell, else the AI host's cell of the NPC, else its T0 cell.</summary>
[TestClass]
public sealed class AreaSpeakerCellsTests
{
    private static readonly WorldAreaTable Table = WorldAreaTable.Create(["0, 0, 0, 0/Unknown/Nothing/", "1000, 20, 0, 0/Alpha/First/"]);

    [TestMethod]
    public void A_speaker_the_ai_host_moved_stands_on_the_hosts_cell_and_tells_the_way_from_there()
    {
        var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (20, 25), (20, 25)));
        var row = AreaAiRouteTests.Npc(0, [], 0, (20, 25), (20, 25));
        var speaker = new AreaSpeaker(2, row, 1, AreaAiRouteTests.NpcCell.X, AreaAiRouteTests.NpcCell.Y);
        var cells = AreaSpeakerCells.Live(null, scene.Host.NpcCell);

        Assert.AreEqual((10L, 20L), cells(speaker), "not moved: its T0 cell");
        var port = new AreaDialogDirections(Table, speaker, cells);
        var npc = AreaConversationState.Npc(2);
        Assert.AreEqual(new DialogBearing(5, 990), port.Bearing(npc, 1), "toward +x from the T0 cell");

        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(new CellCoord(20, 25), scene.Host.NpcCell(scene.Npc), "the host walked it to its standpoint");
        Assert.AreEqual((20L, 25L), cells(speaker), "the host's cell, not the T0 cell");
        Assert.AreEqual(new DialogBearing(5, 980), port.Bearing(npc, 1), "the way is told from where it stands now");
        Assert.IsNull(scene.Host.NpcCell("oid:nobody"), "an entity the host does not hold has no cell");
    }

    [TestMethod]
    public void A_companion_cell_wins_and_a_speaker_nobody_holds_stays_on_its_t0_cell()
    {
        var speaker = new AreaSpeaker(2, AreaAiRouteTests.Npc(0, [], 0, (20, 25), (20, 25)), 1, 10, 20);
        Assert.AreEqual((77L, 78L), AreaSpeakerCells.Live(_ => (77, 78), _ => new CellCoord(1, 2))(speaker), "a companion's committed cell");
        Assert.AreEqual((1L, 2L), AreaSpeakerCells.Live(_ => null, _ => new CellCoord(1, 2))(speaker), "not a companion: the AI host's cell");
        Assert.AreEqual((10L, 20L), AreaSpeakerCells.Live(_ => null, _ => null)(speaker), "nobody holds it: the T0 cell");
        Assert.AreEqual((10L, 20L), AreaSpeakerCells.Live(null, null)(speaker), "no hosts at all: the T0 cell");
    }
}
