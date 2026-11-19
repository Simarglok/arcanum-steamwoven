using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const string ScriptTriggersCode = "ARC-DEV-2816";
    private const int ScriptTriggersMaxScripts = 12, ScriptTriggersMaxNpcScripts = 4, ScriptTriggersWrittenItemType = 13;

    // ARC-DEV-2816 (epic #2814, docs/research/script-trigger-conditions.md; FR-SCRIPT-001, FR-SCRIPT-002) native observation (stage "script-triggers", only when named, start map with --hero, AI on): the guard that
    // fails when an installed script a host is meant to run is refused or does not run at a trigger Steamwoven hosts. It plays the triggers one after the other and reads the outcome of each:
    //  - TQ13/TQ27: Virgil's opening heartbeat runs and opens his dialog, whose script line runs (the `opening` stage's observation, reused whole: it fails when the heartbeat or the talk is refused or stops);
    //  - TQ5: every tile script of the window the placed set admits from line 0 (up to a dozen scripts) runs for the hero as the tile entry does, and for an NPC of the shown map as the AI's step does (up to four);
    //  - TQ18: every written item (object type 13) of the window whose use script the item-use set admits from line 0 runs its use script as the item use does.
    // A script a host admits statically must not come back Refused, EffectsRejected or ContentUnavailable: that is the bug class of #2788 (a tile script refused for a carried follower) and #2789 (a note's use script refused).
    // The scripts the census baseline names as refused (tests ScriptTriggerRefusalBaseline.txt) are not played: the static admission check below leaves them out, so a gap issue of the epic stays an issue, not a failing run.
    private async Task RunAreaScriptTriggersStageAsync(TerrainLoadResult? mapMode)
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{ScriptTriggersCode} script-triggers smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SCRIPT_TRIGGERS_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _mapTransfers is not { PlacedRunner: not null } transfers || !InventoryActive
            || _conversationContent?.Load.Content is not { } content || UseScriptHost(area.Snapshot) is not { } host)
        { GD.Print($"{ScriptTriggersCode} native-smoke script-triggers SKIP (needs --hero, an area session, the inventory, the script content and the map's placed-script runner)"); return; }
        // The heartbeat and the dialog line: the opening talk, whole.
        await RunAreaOpeningTalkSmokeAsync(mapMode);
        bool Admitted(int script, ScrAdmissionSet set) => content.Script(script) is { Status: ScriptContentStatus.Read, Program: { } program } && ScrAdmission.AdmitFrom(program, set, 0).IsAdmitted;
        static bool Failed(AreaScriptedUse? use) => use is null || use.Outcome is AreaScriptedUseOutcome.Refused or AreaScriptedUseOutcome.EffectsRejected or AreaScriptedUseOutcome.ContentUnavailable;
        // Tile scripts: the first tile of each distinct script the placed set admits.
        var tiles = transfers.ScriptTiles.Select(cell => (Cell: cell, Placed: transfers.TileScript(cell))).Where(t => t.Placed is { Script: > 0 } && Admitted(t.Placed.Script, AreaUseScriptHost.PlacedAdmission))
            .OrderBy(t => t.Placed!.Script).ThenBy(t => t.Cell.Y).ThenBy(t => t.Cell.X).GroupBy(t => t.Placed!.Script).Select(g => g.First()).Take(ScriptTriggersMaxScripts).ToArray();
        Require(tiles.Length > 0, $"the window has tile scripts the placed set admits ({transfers.ScriptTiles.Count} script tiles)");
        var heroRuns = 0; var npcRuns = 0;
        foreach (var (cell, placed) in tiles)
        {
            var use = transfers.PlacedRunner!(placed!, _ => null);
            Require(!Failed(use), $"the tile {cell.X},{cell.Y} script {placed!.Script} runs for the hero: {use?.Outcome} {use?.Detail}");
            Awarded(host, use!); heroRuns++;
        }
        var npc = area.Snapshot.Objects.Where(r => r.Type == 16).Select(AreaUseScriptHost.EntityId).FirstOrDefault();
        if (npc is not null)
            foreach (var (cell, placed) in tiles.Take(ScriptTriggersMaxNpcScripts))
            {
                if (transfers.EnterByCritter(cell, npc) is not { } use) continue;
                Require(!Failed(use), $"the tile {cell.X},{cell.Y} script {placed!.Script} runs for the NPC {npc}: {use.Outcome} {use.Detail}");
                Awarded(host, use); npcRuns++;
            }
        // Written items: the use script of each distinct admitted script runs as the item use does.
        var items = area.Snapshot.Objects.Where(r => r.Type == ScriptTriggersWrittenItemType && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Use) is { Script: > 0 } slot && Admitted(slot.Script, AreaUseScriptHost.ItemUseAdmission))
            .GroupBy(r => AreaUseScriptHost.LoadedSlot(r, ScrPoint.Use)!.Value.Script).Select(g => g.First()).Take(ScriptTriggersMaxScripts).ToArray();
        var itemRuns = 0;
        foreach (var row in items)
        {
            var script = AreaUseScriptHost.LoadedSlot(row, ScrPoint.Use)!.Value.Script;
            var use = Awarded(host, host.RunItemUse(row.Key.RuntimeMap, AreaUseScriptHost.EntityId(row), session.Rules.Scripts));
            Require(!Failed(use), $"the written item {AreaUseScriptHost.EntityId(row)} use script {script} runs: {use.Outcome} {use.Detail}");
            itemRuns++;
        }
        // A half that played nothing is no guard: the stage says so instead of passing.
        Require(npcRuns > 0, $"an NPC of the shown map ran a tile script ({npc ?? "no NPC row"}, {tiles.Length} tile scripts)");
        Require(itemRuns > 0, "a written item of the window ran its use script (none of type 13 with an admitted use script)");
        GD.Print("ARC-DEV-2816 native-smoke script-triggers PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, TileScripts = tiles.Length, HeroTileRuns = heroRuns, NpcTileRuns = npcRuns, WrittenItemRuns = itemRuns,
            Parity = "NOT_VERIFIED" }));
    }
}
