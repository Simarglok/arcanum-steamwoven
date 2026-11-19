using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const string KerghanTalkCode = "ARC-SCRIPT-2504";
    private const int KerghanScript = 1105;

    // The replies of Kerghan's DLG 1105 the stage takes, by response id (the numbers are the file's, the order on the screen is the player's IQ and flags): the first talk of the story to the
    // reply that ends on script line 100 (the game clock moves by three hours and movie 6 plays, action 103), and the talk after the task (node 612), whose last reply ends on script
    // line 80 (the change of shape: action 40, the three eye candies, the deferred call of line 86). Node 612 offers the reply 614 (a hero that holds the item named 5616) whose action sets global flag 2674 (the truth: line
    // 86 goes to line 91 and plays movie 3) or 615 (item 2008, flag 2675, line 88: movie 2) before the chain goes on at node 618; the stage sets flag 2674 itself and opens the dialog at node 618.
    private static readonly int[] KerghanStoryReplies = [12, 31, 80, 85, 89, 93, 98];
    private const int KerghanReturnNode = 618, KerghanTruthFlag = 2674;
    private static readonly int[][] KerghanReturnReplies = [[619], [625], [631], [637]];

    // ARC-SCRIPT-2504 native observation (stage "kerghan-talk", only when named, with --hero, on Kerghan's castle map `void-kerghans castle`, opened around his sector 1,10): the installed dialog script 1105 of Kerghan through the
    // AREA conversation host. 1. The story talk is a conversation from the greeting the script's own lines choose (script line 10, DLG node 1): the hero stands beside him, seven replies by response id lead to the reply that
    // ends the dialog on script line 100; Close resumes the script there: the game clock moves by 10,800 s (three hours, flag 2) and movie 6 is asked for (it plays through the real decoder when ffmpeg is installed and is
    // passed over, said so, when it is not; a key ends it). 2. The talk after the task is opened the way a script's dialog action opens it, at the DLG node 612 (what the script's counters choose after the four beings
    // are killed is not played here), and its four replies after it end on script line 80: Close runs the change of shape: Kerghan is destroyed, a created critter of prototype 27368 stands on his tile, the deferred call of
    // line 86 waits in the session naming the made critter, and when the clock reaches its due time the call plays movie 3 and asks for the end of the game (the stage ends there; the ending itself is the `slides` stage's).
    // Exe facts: ledger TI5b (F8, F10, F12), TI5c.
    private async Task RunAreaKerghanTalkSmokeAsync(TerrainLoadResult? mapMode = null)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _conversationHost is not { } host || _readyArea is not { } area || !InventoryActive)
        { GD.Print($"{KerghanTalkCode} native-smoke kerghan-talk SKIP (needs --hero, an area session, its conversation host and the inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-2504 kerghan-talk smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_KERGHAN_TALK_SMOKE_FAILED");
        }
        var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
            ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
        var speakers = host.Speakers.Where(s => s.ScriptId == KerghanScript && SpeakerPresent(s) && s.Row.WorldX is not null && s.Row.WorldY is not null).ToArray();
        if (speakers.Length == 0) { GD.Print($"{KerghanTalkCode} native-smoke kerghan-talk NONE map={map.RuntimeMap} (no living speaker of script {KerghanScript}; speakers={host.Speakers.Count})"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var takesScripted = _smokeTakesScriptedConversations;
        var moviesBefore = _moviesForSelfTest; var startupDone = _startupMoviesDone; var pathBefore = _moviePathForSelfTest; var tableBefore = _movieTableForSelfTest; var sourceBefore = _movieSourceForSelfTest;
        _worldFocusSimulationForSelfTest = true; _smokeTakesScriptedConversations = true;
        var ended = false; var aiBefore = _aiForSmoke;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // The AI host takes the made critter (the event pass knows the objects of the AI host and the combat host: a call that names an unknown one is dropped at its due time, F12).
            _aiForSmoke = true;
            for (var wait = 0; wait < 600 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is not null, "the AI host of the map is built");
            var speaker = speakers[0];
            var entity = AreaConversationState.EntityId(speaker.Row);
            var at = map.Chart.ToChart(new(speaker.Row.WorldX!.Value, speaker.Row.WorldY!.Value));
            CellCoord place;
            try { place = AreaSmokeStandable(map, at, 1); } catch (InvalidOperationException) { place = AreaSmokeStandable(map, at, host.Profile.Reach); }
            AreaSmokePlace(map, place);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            for (var i = 0; i < 10; i++) AreaSmokeFrame();
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);

            // The replies are taken by response id: the option the player sees with that id, whatever its number on the screen.
            DialogOption Offered(int[] ids, string when)
            {
                var conversation = _conversationPanel?.Conversation;
                Require(ConversationOpen && conversation is { Phase: DialogConversationPhase.AwaitingSelection }, $"{when}: the dialog awaits a reply ({_conversationNotice})");
                var option = conversation!.Options.FirstOrDefault(o => ids.Contains(o.ResponseId.Value));
                Require(option is not null, $"{when}: one of the replies {string.Join("/", ids)} is offered (offered {string.Join(",", conversation.Options.Select(o => o.ResponseId.Value))})");
                return option!;
            }
            void CloseAndRun()
            {
                _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                for (var i = 0; i < 5; i++) AreaSmokeFrame();
            }

            // 1. The story talk: the dialog opens with Kerghan's greeting; seven replies lead to script line 100.
            var clockBefore = session.QueryClockSnapshot().GameTimestampMs;
            // Movie 6 plays through the real decoder when it can; the other stages' movies stay off.
            _moviesForSelfTest = true; _moviePathForSelfTest = null; _movieTableForSelfTest = null; _movieSourceForSelfTest = null; _startupMoviesDone = true;
            var movies = MovieRefusal() is null;
            var playedBefore = _scriptFadesPlayed;
            GD.Print($"{KerghanTalkCode} kerghan-talk movie 6 {(movies ? "plays (installed table, real decoder)" : "passed over: " + MovieRefusal())}");
            BeginConversation(speaker, replaceWindows: true);
            Require(ConversationOpen && _conversationPanel!.Conversation is { Phase: DialogConversationPhase.AwaitingSelection, NpcLine.Text.Length: > 0 }, $"the story dialog opened with Kerghan's greeting ({_conversationNotice})");
            var storyReplies = 0;
            var finalStory = _conversationPanel!.Conversation;
            foreach (var id in KerghanStoryReplies)
            {
                var option = Offered([id], $"story reply {storyReplies + 1}");
                finalStory = _conversationPanel!.Conversation;
                _conversationPanel!.Choose(option.Number); storyReplies++;
                for (var i = 0; i < 2; i++) AreaSmokeFrame();
            }
            Require(finalStory is { Phase: DialogConversationPhase.Ended, End: { Kind: DialogEndKind.ReturnToScript, ScriptLine.Value: 100 } }, $"the last story reply ends the dialog on script line 100: {finalStory?.Phase} {finalStory?.End?.Kind} {finalStory?.End?.ScriptLine}");
            // The Original setting closes the window at once on a reply that returns to the script (ARC-SCRIPT-2761); the diagnostic
            // chrome (ARC_DIALOG_DIAGNOSTICS=1) keeps the finished dialog and its Close, and the script resumes after that.
            if (DialogDiagnostics)
            {
                Require(session.QueryClockSnapshot().GameTimestampMs - clockBefore is >= 0 and < 60_000 && _scriptFades.Count == 0, "the clock and the fades wait for the dialog's close (F8: the script resumes after it)");
                CloseAndRun();
            }
            var jumped = session.QueryClockSnapshot().GameTimestampMs - clockBefore;
            Require(jumped is >= 10_800_000 and < 10_800_000 + 60_000, $"line 100 moved the game clock by 10,800 s (flag 2): {jumped} ms");
            if (movies)
            {
                for (var i = 0; i < 300 && !_movieActive; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncScriptFades(); }
                Require(_movieActive && MovieCovering && _scriptFadesPlayed == playedBefore + 1, $"movie 6 started and covers the game (played {_scriptFadesPlayed - playedBefore}, failed {_moviesFailed})");
                for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_moviePanel is { FramesDrawn: > 0 }, "movie 6 drew frames");
                HandleMovieInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
                for (var i = 0; i < 300 && _movieActive; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(!_movieActive, "a key ended movie 6");
            }
            else Require(!_movieActive && _scriptFadesPlayed == playedBefore, "no movie played (passed over)");
            Require(ConversationOpen && _conversationPanel.Conversation is { Phase: DialogConversationPhase.AwaitingSelection }, "the dialog went on with the node after line 100");
            EndConversation();
            Require(!ConversationOpen, "the story dialog closed");
            GD.Print($"{KerghanTalkCode} kerghan-talk story replies={storyReplies} clock-jump-ms={jumped} movie={(movies ? "played" : "passed-over")}");

            // 2. The talk after the task, from node 612 to the reply that ends on script line 80.
            var madeBefore = session.QueryCreatedObjects(new SessionMapId(map.RuntimeMap)).Select(o => o.Entity).ToHashSet(StringComparer.Ordinal);
            Require(!AreaUseScriptHost.Destroyed(session, map.RuntimeMap, entity) && SpeakerPresent(speaker), "Kerghan stands before the change of shape");
            Require(session.QueryScriptCalls().All(c => c.Call.Script != KerghanScript), "no deferred call of 1105 waits yet");
            var truth = session.Flags.Write(new(new(Guid.NewGuid()), session.Flags.Version, new(KerghanTruthFlag), true));
            Require(session.Flags.Read(new(new(Guid.NewGuid()), session.Flags.Version, new(KerghanTruthFlag))).Value == true, $"the truth flag {KerghanTruthFlag} is set, as the reply of node 612 sets it ({truth})");
            BeginScriptedConversation(speaker, new ScrDialogStart(new ScrObjectRef(map.RuntimeMap, AreaScriptHost.PcCode), new ScrObjectRef(map.RuntimeMap, entity), KerghanScript, 0, KerghanReturnNode));
            Require(ConversationOpen, $"the dialog opened at node {KerghanReturnNode} ({_conversationNotice})");
            var returnReplies = 0;
            var finalReturn = _conversationPanel!.Conversation;
            foreach (var ids in KerghanReturnReplies)
            {
                var option = Offered(ids, $"return reply {returnReplies + 1}");
                finalReturn = _conversationPanel!.Conversation;
                _conversationPanel!.Choose(option.Number); returnReplies++;
                for (var i = 0; i < 2; i++) AreaSmokeFrame();
            }
            Require(finalReturn is { Phase: DialogConversationPhase.Ended, End: { Kind: DialogEndKind.ReturnToScript, ScriptLine.Value: 80 } }, $"the last reply ends the dialog on script line 80: {finalReturn?.Phase} {finalReturn?.End?.Kind} {finalReturn?.End?.ScriptLine}");
            if (DialogDiagnostics)
            {
                Require(!AreaUseScriptHost.Destroyed(session, map.RuntimeMap, entity), "Kerghan is not changed before the dialog's close");
                CloseAndRun();
            }
            else for (var i = 0; i < 5; i++) AreaSmokeFrame();
            Require(!ConversationOpen, "the change of shape's dialog closed");
            var made = session.QueryCreatedObjects(new SessionMapId(map.RuntimeMap)).Where(o => o.Kind == SessionCreatedKind.Critter && !madeBefore.Contains(o.Entity)).ToArray();
            Require(made.Length == 1 && made[0].Prototype - Arcanum.World.AreaPrototypeItems.PrototypeOffset == 27368, $"the change of shape made one created critter of prototype 27368 ({string.Join(",", made.Select(m => m.Prototype))})");
            Require(made[0].Cell.X == at.X && made[0].Cell.Y == at.Y, $"it stands on Kerghan's tile: {made[0].Cell} / {at}");
            Require(AreaUseScriptHost.Destroyed(session, map.RuntimeMap, entity), "the old Kerghan is destroyed (the map change of a script destroy)");
            for (var i = 0; i < 6; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); ProcessAreaAi(); }
            Require(AreaAiHostNow()!.Scene.Npcs.Any(n => n.Entity == made[0].Entity) && CreatedCrittersRow(made[0].Entity) is not null, "the AI host holds the made critter and it has the row the viewer draws it from");
            var call = session.QueryScriptCalls().Where(c => c.Call.Script == KerghanScript).ToArray();
            Require(call.Length == 1 && call[0].Call.Line == 86 && call[0].Call.Attachee?.Entity == made[0].Entity, $"the deferred call of line 86 waits and names the made critter ({string.Join(",", call.Select(c => c.Call.Line + ":" + c.Call.Attachee?.Entity))})");
            GD.Print($"{KerghanTalkCode} kerghan-talk change of shape replies={returnReplies} made={made[0].Entity} call-due-ms={call[0].DeadlineMs}");

            // 3. The call's due time: the clock reaches it and the call runs line 86 (movie 3 with the truth flag, then the end of the game).
            var gameBefore = session.QueryClockSnapshot();
            var step = Math.Max(1000L, call[0].DeadlineMs - gameBefore.GameTimestampMs + 1000);
            var playedBeforeCall = _scriptFadesPlayed; var refusalsBefore = AreaEvents.CallRefusals;
            Require(session.JumpClocks(gameBefore.Token, step).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the clock moved to the call's due time");
            SpellCasterNow()?.ProcessEvents();
            for (var f = 0; f < 300 && session.QueryScriptCalls().Any(c => c.Call.Script == KerghanScript); f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(session.QueryScriptCalls().All(c => c.Call.Script != KerghanScript), "the deferred call ran at its due time");
            GD.Print($"{KerghanTalkCode} kerghan-talk call ran refusals={AreaEvents.CallRefusals} last={AreaEvents.LastCallRefusal} fades={_scriptFades.Count} played={_scriptFadesPlayed - playedBeforeCall} end-asked={_endGameAsked} movie-active={_movieActive} clock={session.QueryClockSnapshot().GameTimestampMs}");
            if (movies)
            {
                for (var f = 0; f < 300 && !_movieActive; f++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncScriptFades(); }
                Require(_movieActive && _scriptFadesPlayed == playedBeforeCall + 1 && _movieStatus?.Contains("playing 3", StringComparison.Ordinal) == true, $"the call played movie 3 ({_movieStatus}, played {_scriptFadesPlayed - playedBeforeCall}, failed {_moviesFailed})");
                HandleMovieInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
                for (var f = 0; f < 300 && _movieActive; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            else Require(!_movieActive && _scriptFadesPlayed == playedBeforeCall, "no movie played (passed over; movie 3 is checked with the decoder only)");
            Require(AreaEvents.CallRefusals == refusalsBefore, $"the call was not refused ({AreaEvents.LastCallRefusal})");
            var endGame = _endGameAsked || _heroDeath.Kind == AreaSlideKind.EndGame;
            for (var f = 0; f < 300 && !endGame; f++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); endGame = _endGameAsked || _heroDeath.Kind == AreaSlideKind.EndGame; }
            Require(endGame, $"the call asked for the end of the game (asked {_endGameAsked}, show {_heroDeath.Kind}/{_heroDeath.Stage}, fades {_scriptFades.Count})");
            ended = true;
            GD.Print("ARC-SCRIPT-2504 native-smoke kerghan-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map.RuntimeMap, Speaker = KerghanScript, StoryReplies = storyReplies, ClockJumpMs = jumped, Movie6 = movies ? "played" : "passed over (no ffmpeg)",
                ReturnReplies = returnReplies, Made = 27368, CallLine = 86, EndGame = true, Parity = "NOT_VERIFIED (script facts read in the original executable, TI5b; no original-runtime run)" }));
        }
        finally
        {
            // The call's end game resets the session to the main menu: nothing below touches the session then.
            _moviesForSelfTest = moviesBefore; _startupMoviesDone = startupDone; _moviePathForSelfTest = pathBefore; _movieTableForSelfTest = tableBefore; _movieSourceForSelfTest = sourceBefore;
            if (!ended)
            {
                if (ConversationOpen) EndConversation();
                if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
                if (!aiBefore) { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; }
                ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
                _worldFocused = focused; RestoreWorldTestTime(testTime);
                PublishWorldSelection();
            }
        }
    }
}
