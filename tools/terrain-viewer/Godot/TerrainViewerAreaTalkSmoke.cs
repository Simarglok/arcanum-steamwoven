using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-032 native observation (map mode): a dialog NPC of the map, seen from a few cells away, hovers as a walk to
// talk; a left click through the production ground-click seam walks within reach and opens the conversation (a walk that
// stops is tried again from within reach); the first reply a player could choose is taken until the dialog ends or
// stops; Close resumes the dialog script and ends the modal panel.
// The first speakers in object order are tried until one offers replies; a conversation that starts at a frontier
// (a line the dialog runtime does not run yet) or a typed start status is recorded and the next speaker is tried.
public partial class TerrainViewerApp
{
    private async Task RunAreaTalkSmokeAsync()
    {
        if (_conversationHost is not { } host || _areaSession is not { } session || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-WORLD-032 native-smoke talk NONE map={_readyMapName} (no session)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-032 talk smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TALK_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        var speakers = host.Speakers.Where(s => SpeakerPresent(s) && commands.ContainsKey(s.Row.Key)).ToArray();
        if (speakers.Length == 0)
        {
            GD.Print($"ARC-WORLD-032 native-smoke talk NONE map={_readyMapName} speakers={host.Speakers.Count} dead={host.Speakers.Count(SpeakerDead)} undrawn={host.Speakers.Count(s => !commands.ContainsKey(s.Row.Key))}");
            return;
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var tried = new List<object>(); object? passed = null, started = null; var opens = 0;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            foreach (var speaker in speakers.Take(8))
            {
                var at = map.Chart.ToChart(new(speaker.WorldX, speaker.WorldY));
                // A standable cell 4..6 cells away, so the click walks within reach first; otherwise one within reach.
                CellCoord? start = null;
                foreach (var (dx, dy) in new[] { (5, 0), (-5, 0), (0, 5), (0, -5), (4, 4), (-4, -4), (4, -4), (-4, 4) })
                {
                    var near = new CellCoord(at.X + dx, at.Y + dy);
                    if (!map.Chart.Contains(near)) continue;
                    CellCoord cell;
                    try { cell = AreaSmokeStandable(map, near, 1); } catch (InvalidOperationException) { continue; }
                    if (Math.Max(Math.Abs(cell.X - at.X), Math.Abs(cell.Y - at.Y)) > host.Profile.Reach) { start = cell; break; }
                }
                // The walk from afar first; a walk that stops (a wall, a counter, a closed door between) is tried again from a
                // cell within reach, where the click must open the dialog at once.
                string? walkStopped = null; var walked = false; var frames = 0; var hover = AreaHoverAction.None; long revision = 0; var opened = false;
                foreach (var from in new[] { start, null })
                {
                    if (from is null && start is not null && walkStopped is null) break;
                    CellCoord placed;
                    try { placed = from ?? AreaSmokeStandable(map, at, host.Profile.Reach); } catch (InvalidOperationException) { break; }
                    AreaSmokePlace(map, placed);
                    if (await HoverPointAsync(this, commands[speaker.Row.Key]) is not { } pixel) continue;
                    HoverAtForSelfTest(pixel);
                    hover = HoverActionForSelfTest;
                    HoverReleaseForSelfTest();
                    Require(hover is AreaHoverAction.Talk or AreaHoverAction.WalkToTalk, $"script {speaker.ScriptId} hover {hover}");
                    revision = session.Version.Revision;
                    AreaSmokeClick(pixel);
                    for (frames = 0; frames < 600 && !ConversationOpen; frames++) AreaSmokeFrame();
                    if (ConversationOpen) { opened = true; walked = from is not null; break; }
                    Require(from is not null, $"script {speaker.ScriptId}: a click within reach opened the conversation ({_conversationNotice})");
                    walkStopped = _conversationNotice ?? "the walk did not open it";
                    CancelTalkApproach("smoke retry"); StopWorldRoute("ROUTE_REPLACED_BY_TALK");
                }
                if (!opened) { tried.Add(new { speaker.ScriptId, Skipped = walkStopped ?? "no standable cell or not pickable in view" }); continue; }
                Require(WorldModal && !_worldGate.IsOpen, "the panel is modal and the actor gate is closed");
                var conversation = _conversationPanel!.Conversation;
                var offered = conversation is { Phase: DialogConversationPhase.AwaitingSelection };
                var status = conversation is null ? _conversationPanel.OutcomeForSelfTest : "Started";
                // A start the content or the source refused is a regression of the wiring, not a gap of the dialog runtime.
                Require(conversation is not null || !(status.Contains(nameof(AreaConversationStartStatus.ContentUnavailable)) || status.Contains(nameof(AreaConversationStartStatus.EntryError)) || status.Contains(nameof(AreaConversationStartStatus.ReactionRejected))
                    || status.Contains(nameof(AreaConversationStartStatus.SourceChanged))), $"script {speaker.ScriptId}: {status}");
                // ARC-UI-043: the first NPC line's voice plays on the voice channel when the module has its file.
                var played = _voicesPlayed;
                TickDialogVoice();
                var identity = _voiceStart?.VoiceIdentity(conversation?.NpcLine) ?? -1;
                var gender = host.Profile.Pc.Gender == DialogPcGender.Female ? Arcanum.Data.Audio.SpeechGender.Female : Arcanum.Data.Audio.SpeechGender.Male;
                var speech = identity == -1 || AreaSoundCatalog() is not { } voices ? null : voices.ResolveSpeech(identity, gender);
                if (_audio is not null && speech?.Status == Arcanum.Data.Audio.SpeechStatus.Found)
                    Require(_voicesPlayed == played + 1 && _voiceHandle is not null, $"script {speaker.ScriptId}: the voiced line played its voice ({_voiceNotice})");
                var voice = speech is null ? "none" : $"{speech.Folder}/{speech.Line} {speech.Status}";
                var turns = 0; var phrases = 0;
                // ARC-SCRIPT-043: with the module's texts loaded, every offered reply that is an exact phrase marker (and the
                // no-reply farewell) shows a phrase from the generated-dialog files, never the placeholder.
                var textsReady = host.GreetingTexts?.Status == AreaGreetingTextsStatus.Ready;
                void CountPhrases()
                {
                    foreach (var shown in conversation!.Options.Where(o => o.TextKind == DialogOptionTextKind.Fallback
                        || o.TextKind == DialogOptionTextKind.Generated && "abefhiklnpswy".Contains(o.Marker!.Value.Letter)))
                    {
                        Require(!textsReady || shown.TextOrigin is not null, $"script {speaker.ScriptId}: reply {shown.ResponseId.Value} shows its phrase");
                        phrases++;
                    }
                }
                while (conversation is { Phase: DialogConversationPhase.AwaitingSelection } && conversation.Turn < 32)
                {
                    CountPhrases();
                    var option = conversation.Options.FirstOrDefault(o => o.Block is null);
                    if (option is null) break;
                    _conversationPanel.Choose(option.Number); turns++;
                    TickDialogVoice();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (!ReferenceEquals(_conversationPanel.Conversation, conversation)) break;
                }
                var effects = conversation?.Effects.Count ?? 0; var phase = conversation?.Phase.ToString();
                // Close; a dialog script that opens the next conversation is closed again.
                for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && !WorldModal, "Close ends the modal panel");
                // ARC-WORLD-043: every close ends the NPC's reaction conversation, so a started talk leaves it remembering the PC.
                Require(host.ReactionOpen is null, $"script {speaker.ScriptId}: Close ends the NPC's reaction conversation");
                var metBefore = session.QueryReactionRecord(AreaConversationState.EntityId(speaker.Row)) is { MetBefore: true, InConversation: false };
                Require(conversation is null || metBefore, $"script {speaker.ScriptId}: the ended talk is remembered");
                var record = new { speaker.ScriptId, Prototype = speaker.Row.PrototypeNumber, Walked = walked, WalkStopped = walkStopped, Hover = hover.ToString(), Frames = frames,
                    Status = status, Offered = offered, Phase = phase, Frontier = conversation?.Frontier is { } f ? $"{f.Kind} {f.Source}" : null, Turns = turns, Phrases = phrases, Effects = effects, SessionChanges = session.Version.Revision - revision, MetBefore = metBefore, Voice = voice, VoicesPlayed = _voicesPlayed - played };
                tried.Add(record); opens++;
                if (offered) { passed = record; break; }
                started ??= conversation is not null ? record : null;
            }
            var verdict = passed is not null ? "PASS" : started is not null ? "FRONTIER" : opens > 0 ? "NOT-STARTED" : "NO-REACHABLE-SPEAKER";
            GD.Print($"ARC-WORLD-032 native-smoke talk {verdict} " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Speakers = host.Speakers.Count, Tried = tried, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
