using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

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

    // ARC-SCRIPT-046: at the plane wreck tile script 1695 waits for Virgil (name 6409) to step onto its tile; then it calls 1696, whose
    // dialog asks for node 1 of its own DLG with the player and Virgil. The player stepping on it, or the flag 2304 its DLG sets, end it
    // with nothing.
    [TestMethod, TestCategory("RealData")]
    public void Virgil_stepping_onto_the_wreck_tile_asks_for_his_flying_machine_conversation()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed", "--area-map-sector", "1451,1289"]);
        var result = AreaLoadOperation.Execute(1, arguments, default);
        var snapshot = result.Area!.Snapshot;
        var wreck = result.MapTransitions!.Placed.Single(p => p.Script == 1695 && p.Location == new Arcanum.Data.Maps.WorldLocation(92915, 82552));
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        var host = new AreaUseScriptHost(session, snapshot.RuntimeMap, snapshot.Objects, [], content);
        var virgil = snapshot.Objects.Where(r => r.Type == 16).Select(r => new ScrObjectRef(snapshot.RuntimeMap, AreaUseScriptHost.EntityId(r)))
            .Single(r => host.NameOf(r) == 6409);

        Assert.IsNull(host.RunPlaced(wreck, session.Rules.Scripts).Dialog, "the player is not Virgil");
        var ran = host.RunPlaced(wreck, session.Rules.Scripts, _ => "no teleport", virgil);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
        Assert.AreEqual(new ScrDialogStart(host.Pc, virgil, 1696, 0, 1) { Point = ScrPoint.Use }, ran.Dialog);
        Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 1696, 1, session.Rules.Data).Status);

        var flags = session.Flags;
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(2304), true)).Disposition);
        Assert.IsNull(host.RunPlaced(wreck, session.Rules.Scripts, _ => "no teleport", virgil).Dialog, "once seen, never again");
    }

    // ARC-SCRIPT-052 (audit slice 8): Virgil stepping onto the altar tiles of script 1328 runs his own dialog point from line 14 through
    // call-ex (81), with the player as the triggerer: line 14 of his dialog script 1324 asks for node 250 of its DLG. The tile sets global
    // flag 2380 first, so it runs once.
    [TestMethod, TestCategory("RealData")]
    public void Virgil_stepping_onto_the_altar_tile_asks_for_his_altar_conversation()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed", "--area-map-sector", "1452,1292"]);
        var result = AreaLoadOperation.Execute(1, arguments, default);
        var snapshot = result.Area!.Snapshot;
        var altars = result.MapTransitions!.Placed.Where(p => p.Script == 1328).ToArray();
        Assert.IsTrue(altars.Length > 0, "the altar tiles are in the window");
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        var host = new AreaUseScriptHost(session, snapshot.RuntimeMap, snapshot.Objects, [], content);
        var virgil = snapshot.Objects.Where(r => r.Type == 16).Select(r => new ScrObjectRef(snapshot.RuntimeMap, AreaUseScriptHost.EntityId(r)))
            .Single(r => host.NameOf(r) == 6409);
        var altar = altars[0];

        var pc = host.RunPlaced(altar, session.Rules.Scripts);
        Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, (ScrDialogStart?)null), (pc.Outcome, pc.Dialog), "the player is not Virgil: " + pc.Detail);
        var ran = host.RunPlaced(altar, session.Rules.Scripts, _ => "no teleport", virgil);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
        Assert.AreEqual(new ScrDialogStart(host.Pc, virgil, 1324, 14, 250) { Point = ScrPoint.Dialog }, ran.Dialog);
        Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 1324, 250, session.Rules.Data).Status);
        Assert.IsTrue(host.GlobalFlag(2380), "the tile set its flag");
        Assert.IsNull(host.RunPlaced(altars[^1], session.Rules.Scripts, _ => "no teleport", virgil).Dialog, "once seen, never again");
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
            // Their scripts (2549-2558: script eye candy, then the teleport) needed an action outside the set, so the ARC-WORLD-031 teleport was taken; since ARC-SCRIPT-1481 (set S43: the use host admits
            // script eye candy, which no host plays) they run to their own teleport.
            Assert.AreEqual((AreaScriptedUseOutcome.Teleport, AreaUseOutcome.Accepted), (transfers.LastScripted!.Outcome, entered.Outcome), entered.Reason);
            Assert.IsTrue(transfers.Clear()!.SameMap);
        }
    }

    // ARC-SCRIPT-020: the use scripts of the two readable prototypes with one (note 14086: script 1146, book 14090: script 1891) run
    // in a new game's state and let the reading go on, with their story effects in the session. The item rows are synthetic.
    [TestMethod, TestCategory("RealData")]
    public void The_use_scripts_of_the_readable_note_and_book_let_the_reading_go_on()
    {
        var root = Root();
        using var content = ConversationSessionFixture.Scripts(root);
        using var session = ConversationSessionFixture.Create();
        static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
        static AreaObjectRecord Item(int index, int script) => new(Key(index), 0, 13, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
                [new AreaSparseElement(1, [0, 0, script])], null)], true, true, true);
        var note = "oid:" + Key(1).ObjectIdSha256; var book = "oid:" + Key(2).ObjectIdSha256;
        static SessionItemFacts Facts(int prototype) => new(new(SessionItemType.Written, prototype, 0, 0), 1, 0, 0, null, [1]);
        var items = new AreaInventoryMap(1, new([], [new(note, Facts(14086), new CellCoord(11, 20)), new(book, Facts(14090), new CellCoord(12, 20))]),
            new Dictionary<string, AreaObjectRecord> { [note] = Item(1, 1146), [book] = Item(2, 1891) });
        var host = new AreaUseScriptHost(session, 1, [], [items], content);

        var read = host.RunItemUse(1, note, session.Rules.Scripts);
        Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.RunDefault), (read.Outcome, read.Run!.Outcome), read.Detail);
        Assert.AreEqual((15, SessionQuestState.Completed), (session.QueryStoryState(), session.QueryQuest(1016)!.Value.State));
        Assert.IsNotNull(session.QueryRumor(2107));

        var opened = host.RunItemUse(1, book, session.Rules.Scripts);
        Assert.AreEqual((AreaScriptedUseOutcome.NoTeleport, ScrOutcome.RunDefault), (opened.Outcome, opened.Run!.Outcome), opened.Detail);
        CollectionAssert.Contains(session.QueryKnownAreas().ToArray(), 44);
        Assert.AreEqual(SessionQuestState.Accepted, session.QueryQuest(1103)!.Value.State, "the PC does not hold item 2021");
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
        var tileReasons = new Dictionary<string, int>(); var tileSamples = new Dictionary<string, string>();
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
                if (placed.Trigger == Arcanum.Data.Maps.MapTransitionTrigger.TileEntry && ran.Outcome is AreaScriptedUseOutcome.Refused or AreaScriptedUseOutcome.EffectsRejected)
                {
                    // The refusal without the script number and line, to count the first stops by kind.
                    var reason = ran.Outcome + ": " + System.Text.RegularExpressions.Regex.Replace(ran.Detail ?? "", @"^script \d+ SCR line \d+: ", "");
                    tileReasons[reason] = tileReasons.GetValueOrDefault(reason) + 1; tileSamples.TryAdd(reason, $"{destination.Name}:{placed.Script}");
                }
            }
        }
        // ARC_TILE_CENSUS, when set, names a file the first stops of the refused tile scripts are written to (reason, count, one sample script; no game data).
        if (Environment.GetEnvironmentVariable("ARC_TILE_CENSUS") is { Length: > 0 } reasonFile)
            File.WriteAllLines(reasonFile, tileReasons.OrderByDescending(p => p.Value).Select(p => $"{p.Value}\t{p.Key}\t{tileSamples[p.Key]}"));
        // 21 guarded use sites on 6 maps: 11 run and their guards hold the teleport back (one more since ARC-SCRIPT-1165 admitted the story and time commands, one more since ARC-SCRIPT-1772 admitted the critter getters, toggle invulnerability and rename); 10 need conditions or actions
        // outside the set (immediate calls, loops, other conditions) and are refused.
        CollectionAssert.AreEquivalent(new[] { (AreaScriptedUseOutcome.NoTeleport, 11), (AreaScriptedUseOutcome.Refused, 10) },
            outcomes.Select(p => (p.Key, p.Value)).ToArray(), string.Join(", ", outcomes.Select(p => $"{p.Key}={p.Value}")));
        // 3,012 tile and 20 sector scripts on the 79 maps map mode opens (the start map holds 2,439 tile scripts): since
        // ARC-SCRIPT-018 (call script, assign object, get object type) 644 tile scripts run, since ARC-SCRIPT-019 (story state and
        // story actions) 684; the 6 that teleport are the exit tiles
        // of `iron clan hq` to the start map and one tile of `void-kerghans castle` (script 3070, through a call). Since ARC-WORLD-081 (set
        // S15) the 15 own sector files with the plant generator 30285 run (here with no marker near the session's actor), and the 130 tiles of
        // script 1629 on the start map run to their create object of prototype 28464 (eight critters next to the PC), which the viewer does
        // not create, so their effects are refused. Since ARC-SCRIPT-052 (call-ex, destroy and stop attacking in the tile set) 829 run. Since
        // ARC-DEV-100 (#629, set S21: global variable, PC variable and PC flag operands) 27 more run (850 run without a teleport, 2,026 refused) and
        // since ARC-DEV-157 (#683, set S22: get stat in the tile and use set, vicinity loops in the tile and sector set) 330 more
        // (1,180 without a teleport, 1,696 refused, 1,186 run in all); the teleports (6), the rejected effects (130) and the sector counts do not
        // move. Measured at d07ea71d and at 7758742b on 2026-11-07 (ARC-DEV-166). Since then the sets admitted the casts, damage, heal, walk, animation, steal and prone
        // actions (S29, S30 and the walk slices) and the sector scripts took 17 runs and 3 refusals (unfog town map, ARC-DEV-205): at af8eb52b plus ARC-DEV-298 (float line) 1,216
        // tile scripts run without a teleport, 1,612 are refused by their set and 178 run to effects this host (no AI host, no viewer session) cannot apply (create object of
        // script 1629, damage, casts); the sector counts are 17 and 3. Since ARC-SCRIPT-1165 (set S34, the story and time commands) 62 more tile scripts teleport (the Tulla battle interiors' exits, script 2698), 30 more run without one and 92 fewer are refused (68 teleports, 1,246 without, 1,520 refused, 178 rejected). Since ARC-SCRIPT-1163 (set S36, the NPC state actions: disband, standpoints, change script) 189 more tile scripts run without a teleport and 189 fewer are refused (68 teleports, 1,435 without, 1,331 refused, 178 rejected). ARC_TILE_CENSUS lists the first stops (the biggest: script 30286 not modelled, 584 tiles; see the B23 note).
        // Since ARC-SCRIPT-1164 (set S37, the world-object commands: the sector block pair, the generator pair, lock state, open toggle, create item, touch art) 151 more tile scripts run without a teleport and 151 fewer are refused (1,586 and 1,180),
        // and 2 more sector scripts run (19 and 1).
        // Re-measured on 2026-11-13 (ARC-SCRIPT-1294; the numbers had not moved with the later sets): 35 more tile scripts run without a teleport and 35 fewer are refused (1,621 and 1,145), the same with and without the spell stop (130) of set S42.
        // Since ARC-SCRIPT-1481 (set S43: the use host admits script eye candy, magick tech adjustment, the vitals commands and heal poison) the 68 exit tiles of the void maps (scripts 2549-2558, script eye candy then the teleport)
        // run to their teleport (136 teleports and 1,077 refused) and the last refused sector script runs (20 without a teleport, none refused).
        // Since ARC-SCRIPT-1772 (the critter getters, toggle invulnerability and rename of set S56) 44 more tile scripts run without a teleport and 44 fewer are refused (1,665 and 1,033); the teleports, the rejected effects and the sector counts do not move.
        // Re-measured on 2026-11-14 at f5823e48a (main after ARC-SCRIPT-1772): the later sets S53 to S56 together run 135 more tile scripts than the 1,621 above (1,756 without a teleport, 942 refused); the teleports (136), the rejected effects (178) and the sector counts (20) did not move.
        var tile = Arcanum.Data.Maps.MapTransitionTrigger.TileEntry; var sector = Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry;
        // Since ARC-SCRIPT-1165 (set S34: the story and time commands) the 62 exit tiles of script 2698 on the Tulla battle interiors run to their teleport too.
        static bool IsTullaExit(string t) => t.StartsWith("tulla interior - battle - ", StringComparison.Ordinal) && t.EndsWith(":2698", StringComparison.Ordinal);
        Assert.AreEqual(62, teleports.Count(IsTullaExit));
        // Since ARC-SCRIPT-1481 the exit tiles of the void maps (scripts 2549-2558: script eye candy, then the teleport) run to their teleport too.
        static bool IsVoidExit(string t) => t.StartsWith("void:25", StringComparison.Ordinal);
        CollectionAssert.AreEquivalent(Enumerable.Repeat("iron clan hq:2203", 5).Append("void-kerghans castle:3070").ToArray(), teleports.Where(t => !IsTullaExit(t) && !IsVoidExit(t)).ToArray(), string.Join(", ", teleports));
        CollectionAssert.AreEquivalent(new[]
        {
            ((tile, AreaScriptedUseOutcome.Teleport), 136), ((tile, AreaScriptedUseOutcome.NoTeleport), 1756), ((tile, AreaScriptedUseOutcome.Refused), 942),
            ((tile, AreaScriptedUseOutcome.EffectsRejected), 178), ((sector, AreaScriptedUseOutcome.NoTeleport), 20),
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
