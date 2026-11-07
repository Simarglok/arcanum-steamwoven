using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Presentation.Audio;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-005: the original left click on a dialog NPC talks in crash-site-area mode. It arrives through the
// guarded ground-click path (WorldRouteClick: focus, modal, gate, lifecycle, pending transfer). Within the
// diagnostic reach the dialog opens at once; farther away the click-to-move route walks within reach first and
// the talk starts on arrival, like a walk-to-use. The panel is modal: the actor gate stays closed, replies are
// chosen by mouse (as in the source) or controller menu, and keyboard keys are not redirected to dialog actions
// (the WORLD keyboard route reserves them). Speakers come from the exterior map; in the cave only a companion that
// stands there (ARC-WORLD-017) can be talked to, and on the exterior a companion away in the cave cannot.
// ARC-WORLD-032: in map mode (--area-map) the speakers are the dialog NPCs of the map's own snapshot, talking through
// the same host with the game's script content and the session the map carries, so their consequences go with it to
// the next map; since ARC-WORLD-037 the party commands of a speaker with follow facts go through the session party.
// ARC-WORLD-043: each conversation opens the speaker's reaction conversation slot and every close ends it, so ME reads
// "met before" and an insult's change lands in the talk.
// ARC-WORLD-048: a companion visiting the shown map from another map talks over its home map's dialog state, so the talk is the same
// wherever it stands. ARC-DEV-081 (MIG5): through the one host, which lists such a companion as one more speaker
// (AreaConversationHost.AdmitVisitor); there is no second host and no routing between hosts.
public partial class TerrainViewerApp
{
    private AreaConversationHost? _conversationHost;
    private AreaConversationPanel? _conversationPanel;
    private string? _conversationNotice;
    // ARC-DEV-169: the dialog goal 21's last node asked for, opened at the frame's next safe point (ProcessTalkAfterRoute).
    private AreaSpeaker? _talkOpenPending;
    // ARC-WORLD-016: the session party port of the open conversation, for a refused JO/UW's reason.
    private Arcanum.Session.SessionDialogPartyPort? _conversationParty;
    private bool ConversationOpen => WindowOpen(InterfaceMode.Dialog);

    private void BindAreaConversations(TerrainViewerArguments arguments, AreaLoadResult area)
    {
        EndConversation();
        // The old host may borrow content that is released below; it is never used again.
        _conversationHost = null;
        _conversationNotice = null; _talkOpenPending = null; _conversationEndPending = null;
        // ARC-SCRIPT-006: dialog consequences live in the area session, which outlives map changes.
        // ARC-WORLD-016: Virgil's JO/WA/UW go through the session party with his measured follow inputs.
        // ARC-SCRIPT-011: scripts and dialogs come from the game's content stack, found from the module archive.
        if (_areaSession is { } session)
        {
            var content = ConversationContent(arguments.ModulePath, area.Snapshot.Inputs.SingleOrDefault(input => input.Key.Role == AreaSourceRole.Module)?.Sha256);
            // ARC-WORLD-037: in map mode every speaker with follow facts from the map's data joins through the session party.
            _conversationHost = new AreaConversationHost(area.Snapshot, content.Content, session, inventory: _inventoryHost,
                party: arguments.AreaMapName is null || _companionData?.Data is not null, contentDetail: $"{content.Status}: {content.Detail}",
                companions: arguments.AreaMapName is null ? null : MapCompanionFacts, npcAdvancement: AreaNpcAdvancement, npcSheet: AreaFollowerSheet);
            // ARC-SCRIPT-050: a talk's guard conditions, random draws, float lines and get area are answered by the shown map's use host.
            // Only while that snapshot is shown: the Crash Site's cave swaps the shown area without rebinding the talks, and a use host built
            // for a snapshot not shown would replace the shown one's traps.
            var snapshot = area.Snapshot;
            // ARC-DEV-179: a new gold pile of a speaker paid by $$ takes the stored name of the gold prototype once the barter data has loaded.
            _conversationHost.GoldKindSource = () => { AdoptBarterData(); return _barterData?.Data?.GoldKind(); };
            // ARC-DEV-190: a co action is the hero's attack event on the speaker, run by the shown map's AI host (none without AI: the action stays unadmitted).
            _conversationHost.HeroAttackAvailable = () => AreaAiHostNow() is not null;
            _conversationHost.HeroAttack = entity => AreaAiHostNow() is { } ai && ai.OrderAttack(new Arcanum.Session.CompanionOrderAttack(Arcanum.Session.SessionCombatant.Hero, Arcanum.Session.SessionCombatant.Critter(entity), Arcanum.Simulation.Ai.Loudness.Normal, Arcanum.Simulation.Ai.AttackEventFlags.None));
            // ARC-DEV-227: an nk action is a script's kill of the speaker (a death with no killer through the session's life domain) on the shown map.
            _conversationHost.KillNpc = entity => KillByScript(new Arcanum.Scripting.Scr.ScrObjectRef(CurrentAreaMap, entity));
            // ARC-DEV-193: the reply step's hostile check is asked of the same AI host (alive and kill-on-sight toward the hero).
            _conversationHost.NpcHostile = entity => AreaAiHostNow() is { } hostileAi && hostileAi.HostileToHero(entity);
            _conversationHost.Scripts.QueryHost = () => ReferenceEquals(_readyArea?.Snapshot, snapshot) ? UseScriptHost(snapshot) : null;
        }
        else _conversationHost = null;
        if (_conversationPanel is null)
        {
            _conversationPanel = new AreaConversationPanel(); AddChild(_conversationPanel);
            _conversationPanel.Picked += () => { if (_audio is not null) ExecuteVoice(DialogVoicePlayer.Picked()); };
            // Only the player's close resumes the dialog script; teardown, reset and map changes just close.
            _conversationPanel.CloseRequested += () => EndConversation(resume: true);
        }
    }

    // ARC-SCRIPT-011: the module's script content, opened once per module archive (path and the area's module digest) and
    // kept for reloads and the cave; a content that saw a file change is opened again at the next publication.
    private (string Module, string? ModuleSha256, AreaScriptContentLoad Load)? _conversationContent;

    private AreaScriptContentLoad ConversationContent(string module, string? moduleSha256)
    {
        if (_conversationContent is { } cached && cached.Module == module && cached.ModuleSha256 == moduleSha256 && cached.Load.Content?.Changed != true)
            return cached.Load;
        ReleaseConversationContent();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var load = AreaContentRoot.OpenScripts(module);
        GD.Print($"ARC-SCRIPT-011 script content status={load.Status} detail={load.Detail} ms={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}");
        _conversationContent = (module, moduleSha256, load);
        return load;
    }

    /// <summary>Disposes the script content; the caller has dropped the conversation host that borrowed it.</summary>
    private void ReleaseConversationContent()
    {
        _conversationContent?.Load.Content?.Dispose();
        _conversationContent = null;
    }

    private void UnbindAreaConversations()
    {
        EndConversation();
        UnbindAreaCompanions();
        _conversationHost = null; _conversationNotice = null; _talkOpenPending = null; _conversationEndPending = null; _voiceStart = null; _storyStatus = null;
    }

    private bool TalkMapActive => _conversationHost is not null && (!InCave || _areaCompanions is { Companions.Count: > 0 });

    private (long X, long Y)? ActorCell()
    {
        if (_worldActor.Session is not AreaGridSession session) return null;
        var world = session.Chart.ToWorld(session.State.CommittedCell);
        return (world.X, world.Y);
    }

    /// <summary>The dialog NPC that is the frontmost pickable object under the screen point, if any (ARC-UI-005 pick:
    /// an NPC behind another opaque, not click-through sprite is not clicked through it).</summary>
    private AreaSpeaker? SpeakerAtScreen(Vector2 screen) =>
        !TalkMapActive || AreaObjectAtScreen(screen) is not { } identity ? null : PresentSpeaker(identity);

    /// <summary>The speaker of the object <paramref name="identity"/> standing in the shown map's world: one of the map's own dialog NPCs, or
    /// (ARC-DEV-081, MIG5) a companion carried here from another map, listed by the one host as it is asked for; null for anything else.</summary>
    private AreaSpeaker? PresentSpeaker(AreaObjectKey identity)
    {
        AdmitCompanionSpeakers();
        return _conversationHost!.Speakers.FirstOrDefault(candidate => candidate.Row.Key == identity && SpeakerPresent(candidate));
    }

    /// <summary>Lists every companion standing here as a speaker of the host (idempotent; a companion of the map's own is already listed).</summary>
    private void AdmitCompanionSpeakers()
    {
        foreach (var companion in _areaCompanions?.Companions ?? []) AdmitCompanion(companion.Template);
    }

    /// <summary>ARC-DEV-081: admits one companion to the host (<see cref="AreaConversationHost.AdmitVisitor"/>); a companion whose
    /// stored dialog state the session cannot read (a damaged save) is refused, named once on the status line and in the log.</summary>
    private AreaSpeaker? AdmitCompanion(AreaCompanionTemplate template)
    {
        if (_conversationHost is not { } host) return null;
        var before = host.Speakers.Count;
        var speaker = host.AdmitVisitor(template, out var refusal);
        if (refusal is not null)
        {
            _conversationNotice = $"companion script {template.Speaker.ScriptId} not talkable: its home-map dialog state is unreadable ({refusal})";
            GD.Print($"ARC-DEV-081 companion refused script={template.Speaker.ScriptId} home={template.Home.RuntimeMap}: {refusal}");
        }
        else if (host.Speakers.Count > before)
            GD.Print($"ARC-DEV-081 companion admitted script={template.Speaker.ScriptId} home={template.Home.RuntimeMap} shown={host.Map.RuntimeMap} index={speaker!.Index} inventory={host.InventoryWired}");
        return speaker;
    }

    /// <summary>The host that lists a speaker: the shown map's; null for a speaker it does not list (a speaker's index means something
    /// only in the host that holds it). The self-test's synthetic map has no dialog NPC, so its stand-in speaker
    /// (<see cref="_selfTestStandInSpeaker"/>) talks through the shown host.</summary>
    private AreaConversationHost? HostOf(AreaSpeaker speaker) =>
        _conversationHost is { } host && (host.Speakers.Contains(speaker) || (SelfTestBootstrap && ReferenceEquals(speaker, _selfTestStandInSpeaker))) ? host : null;

    // The viewer self-test's stand-in speaker (a map row; the synthetic core has no dialog NPC), routed to the shown host.
    private AreaSpeaker? _selfTestStandInSpeaker;

    /// <summary>ARC-DEV-081: before a deferred call runs, lists its attachee when that is a companion standing here, so the host takes
    /// it as one of its own objects (<see cref="AreaScriptHost.Local"/>).</summary>
    private void AdmitCallAttachee(Arcanum.Session.SessionScriptCall call)
    {
        if (call.Attachee is { } attachee && _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == attachee.Entity) is { } companion)
            AdmitCompanion(companion.Template);
    }

    /// <summary>The nearest speaker in reach: the shown map's present speakers and the companions standing here.</summary>
    private AreaSpeaker? NearestTalkable(long x, long y)
    {
        AdmitCompanionSpeakers();
        return _conversationHost!.Nearest(x, y, SpeakerCell, SpeakerPresent);
    }

    /// <summary>
    /// A guarded left press (see <c>WorldRouteClick</c>): true when it hit a dialog NPC and the click rules (GS26) made it a talk. ARC-DEV-169 (MIG13 S11a): the click is an <see cref="ObjectClickRequest"/>; the hero's talk is goal 21 of the
    /// goal table (<see cref="AreaHeroRunsHost.Talk"/>): its steps test the range, walk the hero next to the NPC (goal 8) and ask for the dialog in the last node (<see cref="ViewerHeroRuns.OpenDialog"/>). There is no walk-to-talk intent any more.
    /// </summary>
    private bool TryAreaTalk(Vector2 screen)
    {
        if (ConversationOpen || ActorCell() is null || SpeakerAtScreen(screen) is not { } speaker) return false;
        if (HeroRunsHost() is null)
        {
            // No session, so no table (the synthetic fixtures): nothing runs the goal, so a speaker within reach talks at once and a farther one is not approached.
            if (ActorCell() is not { } here) return false;
            if (AreaConversationHost.Distance(speaker, here.X, here.Y, SpeakerCell) <= _conversationHost!.Profile.Reach) { StopWorldRoute("ROUTE_REPLACED_BY_TALK"); BeginConversation(speaker); }
            else _conversationNotice = "no goal table here: the talk goal cannot walk to the NPC";
            return true;
        }
        CancelAreaUseApproach("a talk click replaced it"); CancelInventoryApproach("a talk click replaced it");
        _talkOpenPending = null; // a dialog the last talk asked for and that has not opened yet is dropped by a new order
        var request = HeroRequests.Apply(new ObjectClickRequest(TalkTargetOf(speaker)));
        if (request.Click != Arcanum.Simulation.Goals.HeroClickGoal.Talk) return false;
        _conversationNotice = request.Outcome == HeroRequestOutcome.Done ? $"talking: goal 21 started at script {speaker.ScriptId}" : "the talk was refused";
        GD.Print($"ARC-DEV-169 talk-click outcome={request.Outcome} goal={request.Click} script={speaker.ScriptId}");
        RefreshReadyStatus();
        return true;
    }

    /// <summary>The target string a talk goal holds for <paramref name="speaker"/>: its session entity (a creature of the shown map, or a companion carried here), else the speaker's index in the host.</summary>
    private string TalkTargetOf(AreaSpeaker speaker) =>
        (_combatHost is not null && _areaSession is not null ? AreaTargetOf(speaker.Row.Key)?.Entity : null) ?? "speaker:" + speaker.Index;

    /// <summary>The speaker a talk goal's target string names, or null when it is not one the host lists.</summary>
    private AreaSpeaker? SpeakerOfTarget(string target) =>
        _conversationHost?.Speakers.FirstOrDefault(speaker => TalkTargetOf(speaker) == target);

    /// <summary>
    /// The old walk-to-talk cancel: the talk goal ends (its walk stops with it) and a dialog still asked for is dropped. The click that replaces the talk with another action interrupts the run
    /// through the table already; the sites that call this for a quick load, a downed hero, the stick, a reset or a round's start are the ones the table's own sweeps do not reach.
    /// </summary>
    private void CancelTalkApproach(string why)
    {
        var ended = ShownHeroRunsHost?.EndTalk(why) == true; // (a host that still belongs to a replaced session has given its runs up already)
        if (_talkOpenPending is null && !ended) return;
        _talkOpenPending = null; _conversationNotice = "walk to talk cancelled: " + why;
    }

    /// <summary>The hero's talk goal reached its last node (0x42AF00): the dialog is asked for, and opens at the frame's next safe point under the click guards (focus, modal windows, reset/reload).</summary>
    private void RequestTalkDialog(string target)
    {
        if (SpeakerOfTarget(target) is { } speaker) _talkOpenPending = speaker;
        else _conversationNotice = "talk target is no longer a speaker here";
    }

    // ARC-DEV-177: the NPC whose conversation the AI gate ended (its partner turned to combat or fled): the dialog closes at the frame's next safe point.
    private string? _conversationEndPending;

    /// <summary>
    /// ARC-DEV-177 (MIG13 S11e, GT138): the dialog-end callback of the AI gate (0x5678D0): the dialog with that NPC ends as any exit does (the reaction conversation closes, the NPC remembers the PC). In the original a dialog under a window (the barter) is suspended and the callback does nothing; here the barter closes the dialog
    /// first (the merchant stays held through the open barter), so nothing is open to end. The partner is named until this runs, so the gate may call once more next frame (idempotent). No goal or turn follows.
    /// </summary>
    private void ProcessConversationEnd()
    {
        if (_conversationEndPending is not { } npc) return;
        _conversationEndPending = null;
        if (!ConversationOpen || _conversationSpeaker is not { } speaker || AreaConversationState.EntityId(speaker.Row) != npc) return;
        GD.Print($"ARC-DEV-177 the gate ended the conversation with {npc}");
        _conversationNotice = "the conversation ended: the NPC turned to combat or fled";
        EndConversation();
    }

    /// <summary>The talk that goal 21 asked for opens now; dropped under the click guards.</summary>
    private void ProcessTalkAfterRoute()
    {
        ProcessConversationEnd();
        if (_talkOpenPending is not { } speaker) return;
        _talkOpenPending = null;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) _conversationNotice = "walk to talk cancelled: reset/reload";
        else if (!_worldFocused || WorldModal || WorldUiFocus || !TalkMapActive || !SpeakerPresent(speaker)) _conversationNotice = "walk to talk dropped: input quarantined/modal";
        else { BeginConversation(speaker); return; }
        RefreshReadyStatus();
    }

    private void BeginConversation(AreaSpeaker speaker)
    {
        if (_conversationHost is null || _conversationPanel is null || !WindowCanOpen(InterfaceMode.Dialog)) return;
        // ARC-COMBAT-004: a dialog first leaves combat mode, and does not start while the hero is under attack.
        if (!LeaveCombatModeForDialog()) { RefreshReadyStatus(); return; }
        QuarantineWorld();
        _conversationNotice = null;
        // ARC-WORLD-045: talking interrupts the speaker's goals, its follow goal too (OL19).
        _areaCompanions?.InterruptGoals(speaker.Row.Key);
        AreaConversationStart start;
        if (HostOf(speaker) is not { } host) { _conversationNotice = $"dialog NPC script {speaker.ScriptId} has no host here"; RefreshReadyStatus(); return; }
        try { start = host.Begin(speaker); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DatException)
        {
            start = new(AreaConversationStartStatus.EntryError, null, speaker, exception.GetType().Name);
        }
        OpenConversation(speaker, start);
        GD.Print($"ARC-SCRIPT-005 conversation status={start.Status} detail={start.Detail} phase={start.Conversation?.Phase}");
        // ARC-WORLD-058: the greeting texts (read at the host's first conversation) and where the first NPC line came from.
        if (host.GreetingTexts is { } texts)
            GD.Print($"ARC-WORLD-058 greeting texts status={texts.Status} detail={texts.Detail} line={start.Conversation?.NpcLine?.Origin?.Kind.ToString() ?? "record"}");
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-SCRIPT-046: opens the conversation a tile script (or a script it called) asked for with its dialog action, the PC with
    /// <paramref name="speaker"/>, as a talk opens (combat mode left first; none while the hero is under attack or a window is modal).
    /// The run's effects are applied already; the DLG starts at the action's node without a dialog script run. A native smoke holds it
    /// unless its stage observes scripted conversations (ARC-WORLD-118).
    /// </summary>
    private void BeginScriptedConversation(AreaSpeaker speaker, Arcanum.Scripting.Scr.ScrDialogStart dialog)
    {
        // ARC-WORLD-118: in a native smoke a conversation a script opens on its own (a companion's or the PC's step onto a script tile,
        // a sector run) would quarantine the world and stop the smoke's own walk, so only a stage that observes it lets it open; the
        // script's run stands, the conversation is logged as held.
        if (_nativeSmoke && !_smokeTakesScriptedConversations)
        {
            GD.Print($"ARC-WORLD-118 scripted conversation held by the smoke script={dialog.Script} node={dialog.Node} speaker={speaker.ScriptId}");
            return;
        }
        if (_conversationHost is null || _conversationPanel is null || ConversationOpen || WorldModal)
        {
            GD.Print($"ARC-SCRIPT-046 scripted conversation not opened script={dialog.Script} node={dialog.Node}: a conversation or window is open");
            return;
        }
        if (HostOf(speaker) is not { } host) { _conversationNotice = $"dialog NPC script {speaker.ScriptId} has no host here"; RefreshReadyStatus(); return; }
        if (!LeaveCombatModeForDialog()) { RefreshReadyStatus(); return; }
        QuarantineWorld();
        _conversationNotice = null; _talkOpenPending = null; _conversationEndPending = null;
        _areaCompanions?.InterruptGoals(speaker.Row.Key);
        AreaConversationStart start;
        try { start = host.BeginScripted(speaker, dialog); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DatException)
        {
            start = new(AreaConversationStartStatus.EntryError, null, speaker, exception.GetType().Name);
        }
        OpenConversation(speaker, start);
        // ARC-SCRIPT-047: a start at the dialog point (a heartbeat's call-ex) resumes the speaker's dialog slot on "return to script".
        _conversationScripted = dialog.Point != Arcanum.Scripting.Scr.ScrPoint.Dialog;
        GD.Print($"ARC-SCRIPT-046 scripted conversation status={start.Status} detail={start.Detail} phase={start.Conversation?.Phase}");
        RefreshReadyStatus();
    }

    /// <summary>ARC-SCRIPT-046: as above for the conversation a tile or sector run asked for: the speaker is the dialog NPC of the shown
    /// map whose entity the action's attachee names (the host refused any other); none here is a notice.</summary>
    private void BeginScriptedConversation(Arcanum.Scripting.Scr.ScrDialogStart dialog)
    {
        AdmitCompanionSpeakers();
        if (_conversationHost?.Speakers.FirstOrDefault(s => AreaConversationState.EntityId(s.Row) == dialog.Npc?.Code) is { } speaker) { BeginScriptedConversation(speaker, dialog); return; }
        _conversationNotice = $"script {dialog.Script} asked for a conversation with {dialog.Npc?.Code ?? "no NPC"}, which is no dialog NPC here";
        GD.Print("ARC-SCRIPT-046 scripted conversation not opened: " + _conversationNotice);
        RefreshReadyStatus();
    }

    // ARC-SCRIPT-046: the open conversation was started by a script's dialog action, so a "return to script" end has no dialog script
    // of the speaker to resume.
    private bool _conversationScripted;

    // ARC-WORLD-118: set by a native smoke stage that observes scripted conversations (the wreck and opening stages); the others hold them.
    private bool _smokeTakesScriptedConversations;

    private void OpenConversation(AreaSpeaker speaker, AreaConversationStart start)
    {
        if (!WindowCanOpen(InterfaceMode.Dialog)) return;
        _conversationScripted = false;
        _conversationParty = start.Party;
        _conversationSpeaker = speaker;
        _voiceStart = start;
        var title = $"Dialog NPC (script {speaker.ScriptId}, prototype {speaker.Row.PrototypeNumber})";
        ShowConversationPanel(title, start.Conversation, start.Status == AreaConversationStartStatus.Started ? null : $"[{start.Status}: {start.Detail}]");
    }

    /// <summary>ARC-DEV-109: shows the conversation panel through the stack (mode 3); the smoke that holds a conversation open uses it too.</summary>
    private bool ShowConversationPanel(string title, DialogConversation? conversation, string? note) =>
        OpenWindowMode(InterfaceMode.Dialog, () =>
        {
            // ARC-UI-071 (B29): the panel's texts in the original's font art (null without a game folder: the viewer's font).
            _conversationPanel!.SetFonts(TextFonts());
            _conversationPanel.Open(title, conversation, note);
        }, () => _conversationPanel!.CloseView());

    // ARC-SCRIPT-009: the speaker of the open conversation, whose dialog script resumes when the DLG returns to it.
    private AreaSpeaker? _conversationSpeaker;

    private void EndConversation() => EndConversation(resume: false);

    private void EndConversation(bool resume)
    {
        if (_conversationPanel is null || !ConversationOpen) return;
        var conversation = _conversationPanel.Conversation;
        GD.Print($"ARC-SCRIPT-005 conversation closed phase={conversation?.Phase} end={conversation?.End?.Kind} turns={conversation?.Turn} effects={conversation?.Effects.Count}");
        CloseWindowMode(InterfaceMode.Dialog);
        // ARC-WORLD-043: every exit closes the NPC's reaction conversation (the original's dialog end), before a resumed
        // script may start the next one; the NPC now remembers the PC.
        if (_conversationHost is { } talk && !talk.EndReaction() && talk.ReactionFailure != Arcanum.Session.GameSessionFailure.None)
            _conversationNotice = $"the session refused to end the NPC's reaction conversation ({talk.ReactionFailure})";
        // ARC-UI-045 (ARC-UI-043 voice): the voice plays on after a close, but the conversation must not outlive it: its ports reach the area
        // host and its snapshot, which a reload or map change would otherwise keep alive.
        _voiceStart = null; _dialogVoice.Release();
        // ARC-WORLD-016: a refused follow names its reason (the generated refusal line text is not loaded).
        if (_conversationParty?.LastRefusal is { } refused) _conversationNotice = $"follow refused: {refused}";
        _conversationParty = null;
        if (_areaSession is { } party) _areaCompanions?.Refresh(party);
        // ARC-WORLD-037: in map mode a speaker that joined (or left) becomes (or stops being) a moving companion.
        if (_readyMapName is not null && _areaSession is not null && _conversationHost is { PartyWired: true }) { RecordAreaCompanions("dialog"); BindAreaCompanions(); }
        // ARC-SCRIPT-009: a "return to script" end resumes the speaker's dialog script through the dispatcher; a new
        // dialog action opens the next conversation, anything else is a notice.
        var speaker = _conversationSpeaker; _conversationSpeaker = null;
        var talkHost = _conversationHost;
        var scripted = _conversationScripted; _conversationScripted = false;
        // ARC-TECHNOLOGY-006: SU ended the talk; the window opens with the NPC as crafter and the talk does not resume (NC2).
        if (conversation?.End is { Kind: DialogEndKind.SchematicWindow } && talkHost?.TakeSchematicCrafter() is { } crafter && _areaSession is not null)
            QueueDialogSchematics(crafter);
        // ARC-UI-050: b: ended the talk; the barter window opens with the speaker as merchant and the talk does not resume (BT7).
        if (conversation?.End is { Kind: DialogEndKind.BarterWindow } && talkHost?.TakeBarterMerchant() is { } merchant && _areaSession is not null)
            QueueDialogBarter(talkHost, merchant);
        if (resume && scripted && conversation?.End is { Kind: DialogEndKind.ReturnToScript })
            _conversationNotice = "the script that started this conversation does not continue here (not modelled)";
        else if (resume && conversation?.End is { Kind: DialogEndKind.ReturnToScript, ScriptLine: { } line } && speaker is not null && talkHost is not null)
        {
            AreaConversationStart resumed;
            try { resumed = talkHost.Resume(speaker, line); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DatException)
            {
                resumed = new(AreaConversationStartStatus.EntryError, null, speaker, exception.GetType().Name);
            }
            GD.Print($"ARC-SCRIPT-009 script resumed line={line.Value} status={resumed.Status} detail={resumed.Detail}");
            if (resumed.Status == AreaConversationStartStatus.Started) { OpenConversation(speaker, resumed); RefreshReadyStatus(); return; }
            _conversationNotice = $"script continued: {resumed.Status}: {resumed.Detail}";
        }
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>While the panel is open it owns every key: none is redirected to a dialog or original action.</summary>
    private static bool HandleConversationKey(InputEventKey key) => true;

    private void HandleConversationPulse(IntentFrame intent)
    {
        if (intent.Cancel) _conversationPanel!.Leave();
        else if (intent.MenuPressedDirection != 0) _conversationPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _conversationPanel!.Confirm();
    }

    private string ConversationStatus()
    {
        if (_conversationHost is null) return string.Empty;
        if (ConversationOpen) return " | conversation open: click a reply";
        return ConversationPrompt() + StoryStatus();
    }

    private string ConversationPrompt()
    {
        if (_conversationNotice is { } notice) return $" | {notice}";
        if (TalkMapActive) AdmitCompanionSpeakers();
        if (!TalkMapActive || _conversationHost!.Speakers.Count == 0)
            return " | talk: no dialog NPC on this map";
        return ActorCell() is { } cell && NearestTalkable(cell.X, cell.Y) is { } speaker
            ? $" | dialog NPC script {speaker.ScriptId} in reach: left-click it to talk"
            : $" | talk: left-click a dialog NPC ({_conversationHost!.Speakers.Count(SpeakerPresent)} on this map; walks within {_conversationHost.Profile.Reach} tiles first)";
    }

    // ARC-SCRIPT-006: what the dialogs left in the session (survives the cave trip). The status line is rebuilt
    // every frame, so the text is cached per session revision (every story or NPC change moves it).
    private (AreaConversationHost Host, long Revision, int Speakers, string Text)? _storyStatus;

    private string StoryStatus()
    {
        var host = _conversationHost!;
        var revision = _areaSession!.Version.Revision;
        if (_storyStatus is { } cached && ReferenceEquals(cached.Host, host) && cached.Revision == revision && cached.Speakers == host.Speakers.Count) return cached.Text;
        var story = host.State.Story;
        var followers = Enumerable.Range(0, host.Speakers.Count).Count(host.State.Follows);
        var text = $" | session story: quests {story.Quests.Count}, rumors {story.Rumors.Count}, followers {followers}";
        _storyStatus = (host, revision, host.Speakers.Count, text);
        return text;
    }
}
