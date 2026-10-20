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
        transfers.ScriptRunner = (_, row) => new AreaUseScriptHost(session, map.RuntimeMap, map.Snapshot.Objects, [], content).Run(row, session.Rules.Scripts);
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
    public void Every_guarded_use_site_of_the_mode_runs_or_is_refused_by_its_set()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var first = AreaLoadOperation.Execute(1, Arguments(root, "crematorium-lev 1"), default);
        var outcomes = new Dictionary<AreaScriptedUseOutcome, int>();
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
        }
        // 21 guarded use sites on 6 maps: 9 run and their guards hold the teleport back; 12 need conditions or actions
        // outside the set (immediate calls, loops, other conditions) and are refused.
        CollectionAssert.AreEquivalent(new[] { (AreaScriptedUseOutcome.NoTeleport, 9), (AreaScriptedUseOutcome.Refused, 12) },
            outcomes.Select(p => (p.Key, p.Value)).ToArray());
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
