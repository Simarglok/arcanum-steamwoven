using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int OpeningHeartbeatScript = 1100, OpeningDialogScript = 1324;

    // Native observation (ARC-SCRIPT-047, ARC_AREA_MAP_SMOKE_ONLY=opening, map mode on the start map with --hero): the AI turns on with the
    // hero where the new game puts it. Virgil's heartbeat 1100 runs from his AI tick; once the PC is within his vicinity (10 tiles on both
    // axes) it calls his dialog point 1324 at line 4, whose dialog action opens his talk with the PC at node 1 of DLG 1324, and removes
    // itself. The smoke waits for the conversation without any input, checks it is a talk at the dialog point (its "return to script" resumes
    // his dialog slot) and closes it; the heartbeat runs nothing afterwards.
    private async Task RunAreaOpeningTalkSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-047 opening smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_OPENING_SMOKE_FAILED");
        }
        Require(_areaSession is { } && _areaSession.QueryHero() is not null && _arguments?.Hero is not null, "needs a session hero (--hero)");
        Require(_readyArea is not null && _conversationHost is not null && _conversationContent?.Load.Content is not null && _worldActor.Session is AreaGridSession,
            "needs the start map with the game's scripts");
        var session = _areaSession!;
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var virgil = map.Snapshot.Objects.SingleOrDefault(r => r.Type == 16 && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Dialog) is { Script: OpeningDialogScript });
        Require(virgil is not null, $"Virgil (dialog script {OpeningDialogScript}) is on map {map.RuntimeMap}");
        Require(AreaUseScriptHost.LoadedSlot(virgil!, ScrPoint.Heartbeat) is { Script: OpeningHeartbeatScript }, $"his heartbeat is {OpeningHeartbeatScript}");
        var entity = AreaUseScriptHost.EntityId(virgil!);
        var grid = (AreaGridSession)_worldActor.Session!;
        var pc = grid.Chart.ToWorld(grid.State.CommittedCell);
        var reach = Math.Max(Math.Abs(pc.X - virgil!.WorldX!.Value), Math.Abs(pc.Y - virgil.WorldY!.Value));
        GD.Print($"ARC-SCRIPT-047 opening smoke start pc=({pc.X},{pc.Y}) virgil=({virgil.WorldX},{virgil.WorldY}) reach={reach}");
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // A fresh actor on the PC's own cell, so the smoke's frame clock starts from 0 (the load's frames are past).
            AreaSmokePlace(map, grid.State.CommittedCell);
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            Require(AreaAiHostNow() is not null, "the AI host is built");
            var hostMs = 40_000_000L;
            TickAreaClock(hostMs);
            var frames = 0;
            for (; frames < 2000 && !ConversationOpen; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(ConversationOpen, $"his conversation opened by itself in {frames} frames (reach {reach}): {_conversationNotice}");
            Require(_conversationSpeaker?.Row.Key == virgil.Key && _voiceStart is { Status: AreaConversationStartStatus.Started, DialogScript: OpeningDialogScript },
                $"the DLG of script {OpeningDialogScript} with Virgil: {_voiceStart?.Status} {_voiceStart?.Detail}");
            Require(!_conversationScripted, "a talk at the dialog point resumes his dialog slot");
            var node = _conversationPanel!.Conversation?.CurrentNode.Value;
            var opened = frames;
            var host = UseScriptHost(_readyArea!.Snapshot);
            Require(host?.Slot(new(map.RuntimeMap, entity), ScrPoint.Heartbeat) is { Script: 0 }, "the heartbeat removed itself");
            for (var close = 0; close < 8 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "his conversation closed");
            // Later ticks run no heartbeat script: no second talk.
            for (var f = 0; f < 200 && !ConversationOpen; f++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(!ConversationOpen, "no second opening talk");
            GD.Print("ARC-SCRIPT-047 native-smoke opening PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = map.RuntimeMap, Pc = new { pc.X, pc.Y }, Virgil = new { X = virgil.WorldX, Y = virgil.WorldY }, Reach = reach, Frames = opened, Node = node,
                Script = OpeningDialogScript, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
        await Task.CompletedTask;
    }
}
