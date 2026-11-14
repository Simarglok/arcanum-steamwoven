using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private int _talkSmokeExaminesBefore;
    private bool _talkSmokeBarsOn;

    private bool _talkSmokeGateEnded;

    private bool _finalLineCaptured;

    /// <summary>
    /// ARC-UI-2028: when ARC_AREA_DIALOG_CAPTURE names a folder, one dialog's end is saved twice as PNG for the graphics report (never committed): after.png is the game view with the line floating over the NPC and
    /// no window; before.png is the old picture, the ended conversation shown in the panel (the NPC line, the end note and Close) over the same view. A dialog that really ended on its last line is captured as it is;
    /// without one on the map (they are rare) <paramref name="said"/> is a synthetic line floated over the last speaker and the panel shows <paramref name="note"/>. Nothing is judged and nothing is written without the variable.
    /// </summary>
    private async Task CaptureFinalLineAsync(AreaSpeaker speaker, DialogConversation conversation, string? said = null, string? note = null)
    {
        if (_finalLineCaptured || System.Environment.GetEnvironmentVariable("ARC_AREA_DIALOG_CAPTURE") is not { Length: > 0 } folder) return;
        _finalLineCaptured = true;
        Directory.CreateDirectory(folder);
        var owner = AreaConversationState.EntityId(speaker.Row);
        if (said is not null) Bubble(owner, said);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, "after.png"));
        _floaters?.Bubbles.Remove(owner); _floaters?.Redraw();
        ShowConversationPanel($"Dialog NPC (script {speaker.ScriptId}, prototype {speaker.Row.PrototypeNumber})", conversation, note);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, "before.png"));
        CloseWindowMode(InterfaceMode.Dialog);
        if (said is not null) Bubble(owner, said);
        GD.Print($"ARC-UI-2028 final line capture saved in {folder} script={speaker.ScriptId} synthetic={said is not null}");
    }

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
        (AreaSpeaker Speaker, DialogConversation Conversation)? lastTalk = null;
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
                    _voiceStart = null;
                    _talkSmokeExaminesBefore = _dialogExaminesForSelfTest; _talkSmokeBarsOn = InterfaceBarsOn;
                    var finalLineStarts = _finalLineStartsForSelfTest;
                    AreaSmokeClick(pixel);
                    // ARC-DEV-324: a speaker whose dialog point ran default shows the generic greeting (a floating line) and opens no window; that is no dialog to check here.
                    for (frames = 0; frames < 600 && !ConversationOpen && _voiceStart is null && _finalLineStartsForSelfTest == finalLineStarts; frames++) AreaSmokeTalkFrame();
                    if (ConversationOpen) { opened = true; walked = from is not null; break; }
                    // ARC-UI-2028: a dialog that starts on the NPC's last line floats it and opens no window.
                    if (_finalLineStartsForSelfTest != finalLineStarts) { walkStopped = "the dialog starts on the NPC's last line: it floats, no window"; break; }
                    if (_voiceStart is { Status: AreaConversationStartStatus.GenericGreeting }) { walkStopped = "the dialog point ran default: the generic greeting floats, no window"; break; }
                    Require(from is not null, $"script {speaker.ScriptId}: a click within reach opened the conversation ({_conversationNotice})");
                    walkStopped = _conversationNotice ?? "the walk did not open it";
                    CancelTalkApproach("smoke retry"); StopWorldRoute("ROUTE_REPLACED_BY_TALK");
                }
                if (!opened) { tried.Add(new { speaker.ScriptId, Skipped = walkStopped ?? "no standable cell or not pickable in view" }); continue; }
                Require(WorldModal && !_worldGate.IsOpen, "the panel is modal and the actor gate is closed");
                var conversation = _conversationPanel!.Conversation;
                var offered = conversation is { Phase: DialogConversationPhase.AwaitingSelection };
                var status = conversation is null ? _conversationPanel.OutcomeForSelfTest : "Started";
                // ARC-UI-1120 (HK25): the dialog start described the speaker for the hero (0x567460 → 0x57CD60): a started talk ran its examine step once with the interface on (the smoke runs with it off: none).
                Require(conversation is null || _dialogExaminesForSelfTest == _talkSmokeExaminesBefore + (_talkSmokeBarsOn ? 1 : 0),  $"script {speaker.ScriptId}: the dialog start examined the speaker as many times as the interface asks (bars={_talkSmokeBarsOn} {_dialogExaminesForSelfTest - _talkSmokeExaminesBefore})");
                // A start the content or the source refused is a regression of the wiring, not a gap of the dialog runtime.
                Require(conversation is not null || !(status.Contains(nameof(AreaConversationStartStatus.ContentUnavailable)) || status.Contains(nameof(AreaConversationStartStatus.EntryError)) || status.Contains(nameof(AreaConversationStartStatus.ReactionRejected))
                    || status.Contains(nameof(AreaConversationStartStatus.SourceChanged))), $"script {speaker.ScriptId}: {status}");
                // ARC-UI-043: the first NPC line's voice plays on the voice channel when the module has its file.
                var played = _voicesPlayed;
                TickDialogVoice();
                if (System.Environment.GetEnvironmentVariable("ARC_AREA_TALK_CAPTURE") is { Length: > 0 } talkCapture && conversation is { Phase: DialogConversationPhase.AwaitingSelection, NpcLine.Text.Length: > 0 }
                    && !System.IO.File.Exists($"{talkCapture}.png"))
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var talkImage = GetViewport().GetTexture().GetImage();
                    talkImage.SavePng($"{talkCapture}.png");
                    GD.Print($"ARC-UI-2027 capture saved {talkCapture}.png");
                }
                var identity = _voiceStart?.VoiceIdentity(conversation?.NpcLine) ?? -1;
                var gender = host.Profile.Pc.Gender == DialogPcGender.Female ? Arcanum.Data.Audio.SpeechGender.Female : Arcanum.Data.Audio.SpeechGender.Male;
                var speech = identity == -1 || AreaSoundCatalog() is not { } voices ? null : voices.ResolveSpeech(identity, gender);
                if (_audio is not null && speech?.Status == Arcanum.Data.Audio.SpeechStatus.Found)
                    Require(_voicesPlayed == played + 1 && _voiceHandle is not null, $"script {speaker.ScriptId}: the voiced line played its voice ({_voiceNotice})");
                var voice = speech is null ? "none" : $"{speech.Folder}/{speech.Line} {speech.Status}";
                var turns = 0; var phrases = 0;
                // ARC-UI-2027 (0x5684C0, 0x567E30, 0x5678D0): the NPC's line is a persistent bubble over the speaker, the panel does not repeat it, a reply replaces it, the end gives it its default life.
                var bubbleOwner = AreaConversationState.EntityId(speaker.Row);
                var bubbleChecks = 0;
                void RequireLineBubble(string when)
                {
                    if (conversation is not { Phase: DialogConversationPhase.AwaitingSelection, NpcLine.Text.Length: > 0 }) return;
                    var bubble = _floaters?.Bubbles.Of(bubbleOwner);
                    Require(bubble is { Persistent: true } && bubble.Text == conversation.NpcLine.Text, $"script {speaker.ScriptId} {when}: the NPC line is a persistent bubble ({bubble?.Text ?? "none"} / {conversation.NpcLine.Text})");
                    Require(_conversationPanel!.ArtTextsForSelfTest().All(text => text.Name != "npc"), $"script {speaker.ScriptId} {when}: the panel has no NPC line");
                    bubbleChecks++;
                }
                RequireLineBubble("at the start");
                // ARC-SCRIPT-043: with the module's texts loaded, every offered reply that is an exact phrase marker (and the
                // no-reply farewell) shows a phrase from the generated-dialog files, never the placeholder.
                // A speaker whose stored fields give no greeting facts keeps empty phrases.
                var textsReady = host.GreetingTexts?.Status == AreaGreetingTextsStatus.Ready
                    && AreaGreeting.FactsFor(Arcanum.Data.Combat.CombatFieldReader.Read(speaker.Row).Fields, host.Profile.Pc, 0, false, false) is not null;
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
                    // ARC-UI-2028: a reply that leads to the NPC's last line is taken when offered, so the real data exercises the close-at-once path.
                    var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == DialogOptionTransition.FinalLine) ?? conversation.Options.FirstOrDefault(o => o.Block is null);
                    if (option is null) break;
                    _conversationPanel.Choose(option.Number); turns++;
                    TickDialogVoice();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (!ReferenceEquals(_conversationPanel.Conversation, conversation)) break;
                    RequireLineBubble("after a reply");
                }
                // ARC-UI-2028: a reply that led to the NPC's last line closed the window in the same call and floated the line over the NPC.
                if (conversation is { End.Kind: DialogEndKind.NpcFinalLine } && !string.IsNullOrWhiteSpace(conversation.NpcLine?.Text))
                {
                    Require(!ConversationOpen && _floaters?.Bubbles.Of(AreaConversationState.EntityId(speaker.Row)) is { Persistent: false }, $"script {speaker.ScriptId}: the last line floats as a timed bubble and the window is gone");
                    await CaptureFinalLineAsync(speaker, conversation);
                }
                var effects = conversation?.Effects.Count ?? 0; var phase = conversation?.Phase.ToString();
                // A conversation that started awaiting a reply had its line checked at least once; one still awaiting at the close ends with its line's bubble no longer persistent (the default life from the end).
                Require(!(conversation is { NpcLine.Text.Length: > 0 } && offered) || bubbleChecks > 0, $"script {speaker.ScriptId}: the line bubble was checked");
                var awaitingAtClose = conversation is { Phase: DialogConversationPhase.AwaitingSelection, NpcLine.Text.Length: > 0 };
                var lastLine = conversation?.NpcLine?.Text;
                // Close; a dialog script that opens the next conversation is closed again.
                for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && !WorldModal, "Close ends the modal panel");
                Require(_floaters?.Bubbles.Of(bubbleOwner) is not { Persistent: true }, $"script {speaker.ScriptId}: the end left no persistent bubble");
                Require(!awaitingAtClose || _floaters?.Bubbles.Of(bubbleOwner) is { Persistent: false } kept && kept.Text == lastLine, $"script {speaker.ScriptId}: the line stays as a timed bubble after the end");
                // ARC-WORLD-043: every close ends the NPC's reaction conversation, so a started talk leaves it remembering the PC.
                Require(host.ReactionOpen is null, $"script {speaker.ScriptId}: Close ends the NPC's reaction conversation");
                var metBefore = session.QueryReactionRecord(AreaConversationState.EntityId(speaker.Row)) is { MetBefore: true, InConversation: false };
                Require(conversation is null || metBefore, $"script {speaker.ScriptId}: the ended talk is remembered");
                // ARC-DEV-172: the NPC that remembers the hero is hovered by its own name (examine 0x45F970), not the not-yet-met one.
                if (metBefore && HoverIndex() is { } hoverIndex && hoverIndex.Contains(speaker.Row.Key))
                    Require(hoverIndex.Name(speaker.Row.Key) == AreaHoverIndex.Text(hoverIndex.Row(speaker.Row.Key), hoverIndex.Catalog, Arcanum.Data.Messages.CritterRecognition.Recognized),
                        $"script {speaker.ScriptId}: a met NPC is hovered by its own name");
                // ARC-DEV-177 (GT138): the AI gate's dialog-end callback (an NPC partner that turned to combat or fled) closes an open conversation at the frame's next safe point; checked once, after the speaker's own
                // checks, on a second talk with the same NPC (a speaker whose met-before talk does not open is skipped).
                if (!_talkSmokeGateEnded)
                {
                    BeginConversation(speaker);
                    if (ConversationOpen)
                    {
                        _talkSmokeGateEnded = true;
                        _conversationEndPending = AreaConversationState.EntityId(speaker.Row);
                        AreaSmokeFrame();
                        Require(!ConversationOpen && !WorldModal && host.ReactionOpen is null, $"script {speaker.ScriptId}: the gate's dialog-end callback closed the conversation and its reaction");
                    }
                }
                var record = new { speaker.ScriptId, Prototype = speaker.Row.PrototypeNumber, Walked = walked, WalkStopped = walkStopped, Hover = hover.ToString(), Frames = frames,
                    Status = status, Offered = offered, Phase = phase, Frontier = conversation?.Frontier is { } f ? $"{f.Kind} {f.Source}" : null, Turns = turns, Phrases = phrases, Effects = effects, SessionChanges = session.Version.Revision - revision, MetBefore = metBefore, Voice = voice, VoicesPlayed = _voicesPlayed - played };
                tried.Add(record); opens++;
                if (conversation is not null) lastTalk = (speaker, conversation);
                // A capture run goes on to the next speaker until one has ended on its last line.
                if (offered) { passed ??= record; if (_finalLineCaptured || System.Environment.GetEnvironmentVariable("ARC_AREA_DIALOG_CAPTURE") is not { Length: > 0 }) break; }
                started ??= conversation is not null ? record : null;
            }
            if (!_finalLineCaptured && lastTalk is { } last) await CaptureFinalLineAsync(last.Speaker, last.Conversation, "Farewell, traveler.", "[dialog ended with the NPC's final line]");
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
