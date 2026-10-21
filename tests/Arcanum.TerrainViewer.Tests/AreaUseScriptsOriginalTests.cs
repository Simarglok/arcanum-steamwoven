using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-SCRIPT-015 checks (DAT_ROOT = the game folder): map mode runs the use scripts of guarded use sites
/// over the installed content, and a script's teleport of the player queues the transfer.</summary>
[TestClass]
public sealed class AreaUseScriptsOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void A_door_behind_a_global_flag_prints_its_line_and_opens_once_the_flag_is_set()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, Arguments(root, "tulla interior"), default);
        var map = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, result.Area.Plan.Chart);
        var transfers = new AreaMapTransfers(map, result.MapTransitions!, result.MapDestinations!);
        // Seven guarded use sites on this map: six doors out to the start map (5000) and one door inside it (script 2353).
        Assert.AreEqual(7, transfers.ScriptedTriggers.Count);
        var door = transfers.ScriptedTriggers.Single(key => transfers.ScriptedSite(key)!.Script == 2353);
        using var session = ConversationSessionFixture.Create(map.RuntimeMap);
        transfers.ScriptRunner = (_, row, refusal) => new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).Run(row, session.Rules.Scripts, refusal);
        var at = new AreaStepCell(transfers.Object(door).WorldX!.Value, transfers.Object(door).WorldY!.Value);

        var shut = transfers.RequestUse(door, at);
        Assert.AreEqual(AreaUseOutcome.ScriptEnded, shut.Outcome, shut.Reason);
        Assert.AreEqual(1, transfers.LastScripted!.Lines.Count, "the door's line from its DLG");

        // Global flag 2376 opens it: the script teleports the player to (168,218) on this map.
        var flags = session.Flags;
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(2376), true)).Disposition);
        var open = transfers.RequestUse(door, at);
        Assert.AreEqual(AreaUseOutcome.Accepted, open.Outcome, open.Reason);
        Assert.AreEqual((true, new AreaStepCell(168, 218)), (transfers.Pending!.SameMap, transfers.Pending.Arrival));
        Assert.AreEqual(0, transfers.LastScripted!.Lines.Count);
    }

    [TestMethod, TestCategory("RealData")]
    public void An_exit_tile_script_of_iron_clan_hq_takes_the_player_to_the_start_map()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, Arguments(root, "iron clan hq"), default);
        var map = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, result.Area.Plan.Chart);
        var transfers = new AreaMapTransfers(map, result.MapTransitions!, result.MapDestinations!);
        using var session = ConversationSessionFixture.Create(map.RuntimeMap);
        transfers.PlacedRunner = (placed, refusal) => new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).RunPlaced(placed, session.Rules.Scripts, refusal);
        Assert.AreEqual(18, transfers.ScriptTiles.Count);
        var entered = transfers.Enter(new(166, 114)).Single();
        Assert.AreEqual(AreaUseOutcome.Accepted, entered.Outcome, entered.Reason);
        Assert.AreEqual((5000, new AreaStepCell(96948, 75722), 2203), (transfers.Pending!.To.Catalogue, transfers.Pending.Arrival, transfers.Pending.Site.Script));
    }

    [TestMethod, TestCategory("RealData")]
    public void The_unconditional_tile_teleports_of_void_still_fire_when_their_script_is_refused()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, Arguments(root, "void"), default);
        var map = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, result.Area.Plan.Chart);
        var transfers = new AreaMapTransfers(map, result.MapTransitions!, result.MapDestinations!);
        using var session = ConversationSessionFixture.Create(map.RuntimeMap);
        transfers.PlacedRunner = (placed, refusal) => new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).RunPlaced(placed, session.Rules.Scripts, refusal);
        var cells = transfers.TriggerCells.Where(c => transfers.CellSite(c)!.Trigger == Arcanum.Data.Maps.MapTransitionTrigger.TileEntry).ToArray();
        Assert.AreEqual(4, cells.Length);
        foreach (var cell in cells)
        {
            var entered = transfers.Enter(cell).Single();
            // Their scripts need actions outside the set, so the ARC-WORLD-031 teleport is taken.
            Assert.AreEqual((AreaScriptedUseOutcome.Refused, AreaUseOutcome.Accepted), (transfers.LastScripted!.Outcome, entered.Outcome), entered.Reason);
            Assert.IsTrue(transfers.Clear()!.SameMap);
        }
    }

    [TestMethod, TestCategory("RealData")]
    public void Every_guarded_use_site_and_every_tile_and_sector_script_runs_or_is_refused_by_its_set()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        var outcomes = new Dictionary<AreaScriptedUseOutcome, int>();
        var placedOutcomes = new Dictionary<(Arcanum.Data.Maps.MapTransitionTrigger, AreaScriptedUseOutcome), int>();
        var teleports = new List<string>();
        foreach (var destination in first.MapDestinations!.Values)
        {
            var result = AreaLoadOperation.Execute(1, Arguments(root, destination.Name), default);
            var map = new AreaMapRuntime(result.Area!.Snapshot, result.AreaStepSnapshot!, result.Area.Plan.Chart);
            var transfers = new AreaMapTransfers(map, result.MapTransitions!, result.MapDestinations!);
            foreach (var key in transfers.ScriptedTriggers)
            {
                // A new game's state: no flag set, no quest, nothing carried.
                using var session = ConversationSessionFixture.Create(map.RuntimeMap);
                var ran = new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).Run(transfers.Object(key), session.Rules.Scripts);
                outcomes[ran.Outcome] = outcomes.GetValueOrDefault(ran.Outcome) + 1;
            }
            // ARC-SCRIPT-017: every tile and sector script of the map, each in a new game's state.
            foreach (var placed in result.MapTransitions!.Placed)
            {
                using var session = ConversationSessionFixture.Create(map.RuntimeMap);
                var ran = new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).RunPlaced(placed, session.Rules.Scripts);
                placedOutcomes[(placed.Trigger, ran.Outcome)] = placedOutcomes.GetValueOrDefault((placed.Trigger, ran.Outcome)) + 1;
                if (ran.Outcome == AreaScriptedUseOutcome.Teleport) teleports.Add($"{destination.Name}:{placed.Script}");
            }
        }
        // 21 guarded use sites on 6 maps: 9 run and their guards hold the teleport back; 12 need conditions or actions
        // outside the set (immediate calls, loops, other conditions) and are refused.
        CollectionAssert.AreEquivalent(new[] { (AreaScriptedUseOutcome.NoTeleport, 9), (AreaScriptedUseOutcome.Refused, 12) },
            outcomes.Select(p => (p.Key, p.Value)).ToArray(), string.Join(", ", outcomes.Select(p => $"{p.Key}={p.Value}")));
        // 3,012 tile and 20 sector scripts on the 79 maps map mode opens (the start map holds 2,439 tile scripts): since
        // ARC-SCRIPT-018 (call script, assign object, get object type) 644 tile scripts run; the 6 that teleport are the exit tiles
        // of `iron clan hq` to the start map and one tile of `void-kerghans castle` (script 3070, through a call).
        var tile = Arcanum.Data.Maps.MapTransitionTrigger.TileEntry; var sector = Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry;
        CollectionAssert.AreEquivalent(Enumerable.Repeat("iron clan hq:2203", 5).Append("void-kerghans castle:3070").ToArray(), teleports, string.Join(", ", teleports));
        CollectionAssert.AreEquivalent(new[]
        {
            ((tile, AreaScriptedUseOutcome.Teleport), 6), ((tile, AreaScriptedUseOutcome.NoTeleport), 638), ((tile, AreaScriptedUseOutcome.Refused), 2368),
            ((sector, AreaScriptedUseOutcome.NoTeleport), 1), ((sector, AreaScriptedUseOutcome.Refused), 19),
        }, placedOutcomes.Select(p => (p.Key, p.Value)).ToArray(), string.Join(", ", placedOutcomes.Select(p => $"{p.Key}={p.Value}")));
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root) return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in use script check; this is not a synthetic pass.");
        return string.Empty;
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
