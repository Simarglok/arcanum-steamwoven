using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-WORLD-2261 (ledger TT20, tail of ARC-WORLD-2038) native observation (stage "talk-teleport", only when named, map mode with --hero, the town on the main map):
    // the hero's teleport a script run of the talk and deferred-call host asks for is queued as the map transfer and applies at the viewer's next safe point.
    // (1) With a conversation open (a free speaker of the town), the installed script 2591 (the door script of K'na Tha: the hero to a cell of map 5067, then "run default") is run
    // as a deferred call: the transfer queues as a script one (`ByTalkScript`), the original's ping (0x4D3330) does not wait for the dialog, so the next frame changes the map and the
    // change ends the dialog (0x4D3E20 → 0x4601D0 → 0x568830 → 0x5678D0). (2) With no window open, the installed script 2595 (a door script of map 5068: the hero to the main map)
    // is run the same way from the new map and brings the hero back. Both moves must land the hero on the pending transfer's arrival cell with no transfer left pending.
    // Viewer lines: `ARC-WORLD-2038 script-teleport hero outcome=...` (the queue) and the PASS line below.
    private const int TalkTeleportToKnaTha = 2591, TalkTeleportToMain = 2595;

    private async Task RunAreaTalkTeleportSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _conversationHost is not { } conversation || _conversationPanel is null || _mapTransfers is null)
        { GD.Print($"ARC-WORLD-2261 native-smoke talk-teleport NONE map={_readyMapName} (needs --hero, an area session, the conversation host and the map's transfers)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-2261 talk-teleport smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TALK_TELEPORT_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = _mapTransfers.Map;
            var template = _companionTemplates.FirstOrDefault(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker) && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free);
            if (template is null) { GD.Print($"ARC-WORLD-2261 native-smoke talk-teleport NO-SPEAKER map={map.RuntimeMap}"); return; }
            var from = _readyMapName;
            AreaSmokePlace(map, AreaSmokeStandable(map, template.HomeOrigin, 2));
            BeginConversation(template.Speaker);
            AreaSmokeFramesUntilConversation();
            Require(ConversationOpen, "a conversation with the speaker opened: " + _conversationNotice);
            var withDialog = await RunTalkTeleportAsync(conversation, TalkTeleportToKnaTha, Require);
            Require(!ConversationOpen && conversation.ReactionOpen is null, "the change of map ended the open conversation and closed its reaction slot");
            // The hero is on another map now: the second move runs from its host, with no window open.
            Require(_conversationHost is { } arrivedHost && _mapTransfers is not null, "the new map has a conversation host and transfers");
            var withoutDialog = await RunTalkTeleportAsync(_conversationHost!, TalkTeleportToMain, Require);
            GD.Print("ARC-WORLD-2261 native-smoke talk-teleport PASS " + System.Text.Json.JsonSerializer.Serialize(new { From = from, WithDialog = withDialog, WithoutDialog = withoutDialog }));
        }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused; }
    }

    /// <summary>Runs <paramref name="script"/> as a deferred call of the hero, requires the script transfer to queue, the next frames to apply it (waiting for the map load) and the hero to stand on its arrival cell.</summary>
    private async Task<object> RunTalkTeleportAsync(AreaConversationHost host, int script, Action<bool, string> require)
    {
        var transfers = _mapTransfers!;
        var map = transfers.Map;
        var before = _readyMapName;
        var pc = new SessionScriptRole(new SessionMapId(map.RuntimeMap), SessionEventKinds.PlayerEntity);
        var withDialog = ConversationOpen;
        require(transfers.Pending is null, "no transfer is pending before the run");
        var ran = host.RunCall(new(script, 0, pc, pc), out var detail);
        require(ran, $"script {script} ran as a deferred call: {detail}");
        require(transfers.Pending is { ByTalkScript: true } && transfers.Pending.To.Runtime != map.RuntimeMap, $"script {script}'s teleport queued as a script transfer to another map ({detail})");
        var pending = transfers.Pending!;
        AreaSmokeFrame();
        require(_mapHandoff is not null || !SameMapName(_readyMapName, before), $"the transfer is taken at the next frame{(withDialog ? " with the dialog open" : "")}: {_areaUseStatus}");
        await (_lastLoad ?? Task.CompletedTask);
        require(SameMapName(_readyMapName, pending.To.Name) && _worldActor.Session is AreaGridSession, $"the destination map is shown: {_readyMapName} {_areaUseStatus}");
        var arrived = (AreaGridSession)_worldActor.Session!;
        var at = arrived.Chart.ToWorld(arrived.State.CommittedCell);
        require(at == pending.Arrival, $"the hero stands on the arrival cell {pending.Arrival}: {at}");
        require(_mapTransfers is { Pending: null }, "no transfer is left pending");
        return new { Script = script, WithDialog = withDialog, From = before, To = pending.To.Name, Arrival = pending.Arrival };
    }
}
