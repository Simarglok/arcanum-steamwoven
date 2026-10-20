using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Presentation.Audio;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
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
public partial class TerrainViewerApp
{
    private AreaConversationHost? _conversationHost;
    private AreaConversationPanel? _conversationPanel;
    private string? _conversationNotice;
    private AreaSpeaker? _talkAfterRoute;
    // ARC-WORLD-016: the session party port of the open conversation, for a refused JO/UW's reason.
    private Arcanum.Session.SessionDialogPartyPort? _conversationParty;
    private bool ConversationOpen => _conversationPanel?.IsOpen == true;

    private void BindAreaConversations(TerrainViewerArguments arguments, AreaLoadResult area)
    {
        EndConversation();
        // The old host may borrow content that is released below; it is never used again.
        _conversationHost = null;
        _conversationNotice = null; _talkAfterRoute = null;
        // ARC-SCRIPT-006: dialog consequences live in the area session, which outlives map changes.
        // ARC-WORLD-016: Virgil's JO/WA/UW go through the session party with his measured follow inputs.
        // ARC-SCRIPT-011: scripts and dialogs come from the game's content stack, found from the module archive.
        if (_areaSession is { } session)
        {
            var content = ConversationContent(arguments.ModulePath, area.Snapshot.Inputs.SingleOrDefault(input => input.Key.Role == AreaSourceRole.Module)?.Sha256);
            // ARC-WORLD-037: in map mode every speaker with follow facts from the map's data joins through the session party.
            _conversationHost = new AreaConversationHost(area.Snapshot, content.Content, session, inventory: _inventoryHost,
                party: arguments.AreaMapName is null || _companionData?.Data is not null, contentDetail: $"{content.Status}: {content.Detail}",
                companions: arguments.AreaMapName is null ? null : MapCompanionFacts, npcAdvancement: AreaNpcAdvancement);
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
        _conversationHost = null; _conversationNotice = null; _talkAfterRoute = null; _voiceStart = null; _storyStatus = null;
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
    private AreaSpeaker? SpeakerAtScreen(Vector2 screen)
    {
        if (!TalkMapActive || AreaObjectAtScreen(screen) is not { } identity) return null;
        return _conversationHost!.Speakers.FirstOrDefault(candidate => candidate.Row.Key == identity && SpeakerPresent(candidate));
    }

    /// <summary>A guarded left press (see <c>WorldRouteClick</c>): true when it hit a dialog NPC.</summary>
    private bool TryAreaTalk(Vector2 screen)
    {
        if (ConversationOpen || ActorCell() is not { } cell || SpeakerAtScreen(screen) is not { } speaker) return false;
        CancelTalkApproach("another NPC click replaced it"); CancelAreaUseApproach("a talk click replaced it"); CancelInventoryApproach("a talk click replaced it");
        var distance = AreaConversationHost.Distance(speaker, cell.X, cell.Y, SpeakerCell);
        if (distance <= _conversationHost!.Profile.Reach)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_TALK");
            BeginConversation(speaker);
            return true;
        }
        var (speakerX, speakerY) = SpeakerCell?.Invoke(speaker) ?? (speaker.WorldX, speaker.WorldY);
        if (_worldActor.Session is AreaGridSession session && WorldRoute(session) is { } route
            && ApproachCell(session, speakerX, speakerY, _conversationHost.Profile.Reach) is { } goal)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_TALK");
            route.SetTarget(goal, WorldTime); _talkAfterRoute = speaker;
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{goal.X},{goal.Y} walk to talk";
            _conversationNotice = $"walking to talk: {distance} tiles away, goal {goal.X},{goal.Y}";
            GD.Print($"ARC-SCRIPT-005 talk-approach distance={distance} goal={goal}");
        }
        else _conversationNotice = $"dialog NPC is {distance} tiles away and no standable cell within {_conversationHost.Profile.Reach} was found";
        RefreshReadyStatus();
        return true;
    }

    private void CancelTalkApproach(string why)
    {
        if (_talkAfterRoute is null) return;
        _talkAfterRoute = null; _conversationNotice = "walk to talk cancelled: " + why;
    }

    /// <summary>Starts the talk once a walk-to-talk route has ended settled; dropped under the click guards.</summary>
    private void ProcessTalkAfterRoute()
    {
        if (_talkAfterRoute is not { } speaker || _worldActor.Session is not AreaGridSession walking || walking.State.Phase != Arcanum.Simulation.Movement.MovementPhase.Settled
            || !ReferenceEquals(_worldRouteSession, walking) || _worldRoute is not { } route || route.Status == Arcanum.Simulation.Navigation.RouteFollowStatus.Following) return;
        _talkAfterRoute = null;
        var cell = ActorCell()!.Value;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) _conversationNotice = "walk to talk cancelled: reset/reload";
        else if (route.Status != Arcanum.Simulation.Navigation.RouteFollowStatus.Arrived) _conversationNotice = "walk to talk stopped: " + (route.StopReason ?? route.Status.ToString());
        else if (!_worldFocused || WorldModal || WorldUiFocus || !TalkMapActive || !SpeakerPresent(speaker)) _conversationNotice = "walk to talk dropped: input quarantined/modal";
        else if (AreaConversationHost.Distance(speaker, cell.X, cell.Y, SpeakerCell) > _conversationHost!.Profile.Reach) _conversationNotice = "walk to talk ended out of reach";
        else { BeginConversation(speaker); return; }
        RefreshReadyStatus();
    }

    private void BeginConversation(AreaSpeaker speaker)
    {
        if (_conversationHost is null || _conversationPanel is null || ConversationOpen) return;
        // ARC-COMBAT-004: a dialog first leaves combat mode, and does not start while the hero is under attack.
        if (!LeaveCombatModeForDialog()) { RefreshReadyStatus(); return; }
        QuarantineWorld();
        _conversationNotice = null;
        AreaConversationStart start;
        try { start = _conversationHost.Begin(speaker); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DatException)
        {
            start = new(AreaConversationStartStatus.EntryError, null, speaker, exception.GetType().Name);
        }
        OpenConversation(speaker, start);
        GD.Print($"ARC-SCRIPT-005 conversation status={start.Status} detail={start.Detail} phase={start.Conversation?.Phase}");
        RefreshReadyStatus();
    }

    private void OpenConversation(AreaSpeaker speaker, AreaConversationStart start)
    {
        _conversationParty = start.Party;
        _conversationSpeaker = speaker;
        _voiceStart = start;
        var title = $"Dialog NPC (script {speaker.ScriptId}, prototype {speaker.Row.PrototypeNumber})";
        _conversationPanel!.Open(title, start.Conversation, start.Status == AreaConversationStartStatus.Started ? null : $"[{start.Status}: {start.Detail}]");
    }

    // ARC-SCRIPT-009: the speaker of the open conversation, whose dialog script resumes when the DLG returns to it.
    private AreaSpeaker? _conversationSpeaker;

    private void EndConversation() => EndConversation(resume: false);

    private void EndConversation(bool resume)
    {
        if (_conversationPanel is null || !_conversationPanel.IsOpen) return;
        var conversation = _conversationPanel.Conversation;
        GD.Print($"ARC-SCRIPT-005 conversation closed phase={conversation?.Phase} end={conversation?.End?.Kind} turns={conversation?.Turn} effects={conversation?.Effects.Count}");
        _conversationPanel.CloseView();
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
        if (resume && conversation?.End is { Kind: DialogEndKind.ReturnToScript, ScriptLine: { } line } && speaker is not null && _conversationHost is not null)
        {
            AreaConversationStart resumed;
            try { resumed = _conversationHost.Resume(speaker, line); }
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
        if (!TalkMapActive || _conversationHost!.Speakers.Count == 0) return " | talk: no dialog NPC on this map";
        return ActorCell() is { } cell && _conversationHost!.Nearest(cell.X, cell.Y, SpeakerCell, SpeakerPresent) is { } speaker
            ? $" | dialog NPC script {speaker.ScriptId} in reach: left-click it to talk"
            : $" | talk: left-click a dialog NPC ({_conversationHost!.Speakers.Count(SpeakerPresent)} on this map; walks within {_conversationHost.Profile.Reach} tiles first)";
    }

    // ARC-SCRIPT-006: what the dialogs left in the session (survives the cave trip). The status line is rebuilt
    // every frame, so the text is cached per session revision (every story or NPC change moves it).
    private (AreaConversationHost Host, long Revision, string Text)? _storyStatus;

    private string StoryStatus()
    {
        var host = _conversationHost!;
        var revision = _areaSession!.Version.Revision;
        if (_storyStatus is { } cached && ReferenceEquals(cached.Host, host) && cached.Revision == revision) return cached.Text;
        var story = host.State.Story;
        var followers = Enumerable.Range(0, host.Speakers.Count).Count(host.State.Follows);
        var text = $" | session story: quests {story.Quests.Count}, rumors {story.Rumors.Count}, followers {followers}";
        _storyStatus = (host, revision, text);
        return text;
    }
}
