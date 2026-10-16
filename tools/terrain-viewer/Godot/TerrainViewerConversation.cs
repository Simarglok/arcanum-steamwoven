using Arcanum.Data;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-005: the original left click on a dialog NPC talks in crash-site-area mode. It arrives through the
// guarded ground-click path (WorldRouteClick: focus, modal, gate, lifecycle, pending transfer). Within the
// diagnostic reach the dialog opens at once; farther away the click-to-move route walks within reach first and
// the talk starts on arrival, like a walk-to-use. The panel is modal: the actor gate stays closed, replies are
// chosen by mouse (as in the source) or controller menu, and keyboard keys are not redirected to dialog actions
// (the WORLD keyboard route reserves them). Speakers come from the exterior map; the cave has none.
public partial class TerrainViewerApp
{
    private AreaConversationHost? _conversationHost;
    private AreaConversationPanel? _conversationPanel;
    private string? _conversationNotice;
    private AreaSpeaker? _talkAfterRoute;
    private bool ConversationOpen => _conversationPanel?.IsOpen == true;

    private void BindAreaConversations(TerrainViewerArguments arguments, AreaLoadResult area)
    {
        EndConversation();
        _conversationNotice = null; _talkAfterRoute = null;
        _conversationHost = arguments.AreaEffectsPath is { } dialogs ? new AreaConversationHost(area.Snapshot, arguments.ResourcePath, dialogs) : null;
        if (_conversationPanel is null)
        {
            _conversationPanel = new AreaConversationPanel(); AddChild(_conversationPanel);
            _conversationPanel.CloseRequested += EndConversation;
        }
    }

    private void UnbindAreaConversations()
    {
        EndConversation();
        _conversationHost = null; _conversationNotice = null; _talkAfterRoute = null;
    }

    private bool TalkMapActive => _conversationHost is not null && !InCave;

    private (long X, long Y)? ActorCell()
    {
        if (_worldActor.Session is not AreaGridSession session) return null;
        var world = session.Chart.ToWorld(session.State.CommittedCell);
        return (world.X, world.Y);
    }

    /// <summary>The dialog NPC whose drawn sprite has an opaque pixel under the screen point, if any.</summary>
    private AreaSpeaker? SpeakerAtScreen(Vector2 screen)
    {
        if (!TalkMapActive || _readyArea is not { } area) return null;
        var transform = AreaDrawItem.RasterTransform(GetViewport());
        var sx = (int)Math.Floor(screen.X); var sy = (int)Math.Floor(screen.Y);
        for (var index = area.Plan.Commands.Count - 1; index >= 0; index--)
        {
            var command = area.Plan.Commands[index];
            if (command.Identity is not { } identity || command.Additive) continue;
            var speaker = _conversationHost!.Speakers.FirstOrDefault(candidate => candidate.Row.Key == identity);
            if (speaker is not null && AreaAlphaAt(command, area.Frames, transform, sx, sy) != 0) return speaker;
        }
        return null;
    }

    /// <summary>A guarded left press (see <c>WorldRouteClick</c>): true when it hit a dialog NPC.</summary>
    private bool TryAreaTalk(Vector2 screen)
    {
        if (ConversationOpen || ActorCell() is not { } cell || SpeakerAtScreen(screen) is not { } speaker) return false;
        CancelTalkApproach("another NPC click replaced it"); CancelAreaUseApproach("a talk click replaced it");
        var distance = AreaConversationHost.Distance(speaker, cell.X, cell.Y);
        if (distance <= _conversationHost!.Profile.Reach)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_TALK");
            BeginConversation(speaker);
            return true;
        }
        if (_worldActor.Session is AreaGridSession session && WorldRoute(session) is { } route
            && ApproachCell(session, speaker.WorldX, speaker.WorldY, _conversationHost.Profile.Reach) is { } goal)
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
        if (route.Status != Arcanum.Simulation.Navigation.RouteFollowStatus.Arrived) _conversationNotice = "walk to talk stopped: " + (route.StopReason ?? route.Status.ToString());
        else if (!_worldFocused || WorldModal || WorldUiFocus || !TalkMapActive) _conversationNotice = "walk to talk dropped: input quarantined/modal";
        else if (AreaConversationHost.Distance(speaker, cell.X, cell.Y) > _conversationHost!.Profile.Reach) _conversationNotice = "walk to talk ended out of reach";
        else { BeginConversation(speaker); return; }
        RefreshReadyStatus();
    }

    private void BeginConversation(AreaSpeaker speaker)
    {
        if (_conversationHost is null || _conversationPanel is null || ConversationOpen) return;
        QuarantineWorld();
        _conversationNotice = null;
        AreaConversationStart start;
        try { start = _conversationHost.Begin(speaker); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DatException)
        {
            start = new(AreaConversationStartStatus.EntryError, null, speaker, exception.GetType().Name);
        }
        var title = $"Dialog NPC (script {speaker.ScriptId}, prototype {speaker.Row.PrototypeNumber})";
        _conversationPanel.Open(title, start.Conversation, start.Status == AreaConversationStartStatus.Started ? null : $"[{start.Status}: {start.Detail}]");
        GD.Print($"ARC-SCRIPT-005 conversation status={start.Status} detail={start.Detail} phase={start.Conversation?.Phase}");
        RefreshReadyStatus();
    }

    private void EndConversation()
    {
        if (_conversationPanel is null || !_conversationPanel.IsOpen) return;
        var conversation = _conversationPanel.Conversation;
        GD.Print($"ARC-SCRIPT-005 conversation closed phase={conversation?.Phase} end={conversation?.End?.Kind} turns={conversation?.Turn} effects={conversation?.Effects.Count}");
        _conversationPanel.CloseView();
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
        if (_conversationNotice is { } notice) return $" | {notice}";
        if (!TalkMapActive) return " | talk: no dialog NPC on this map";
        return ActorCell() is { } cell && _conversationHost.Nearest(cell.X, cell.Y) is { } speaker
            ? $" | dialog NPC script {speaker.ScriptId} in reach: left-click it to talk"
            : $" | talk: left-click a dialog NPC ({_conversationHost.Speakers.Count} in core; walks within {_conversationHost.Profile.Reach} tiles first)";
    }
}
