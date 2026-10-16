using Arcanum.Data;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-005: the original left click on a dialog NPC talks in crash-site-area mode. The NPC must be
// within the diagnostic reach because walking there (click-to-move) is still pending. The panel is modal:
// the actor gate stays closed, replies are chosen by mouse (as in the source) or controller menu, and
// keyboard keys are not redirected to dialog actions (the WORLD keyboard route reserves them).
public partial class TerrainViewerApp
{
    private AreaConversationHost? _conversationHost;
    private AreaConversationPanel? _conversationPanel;
    private string? _conversationNotice;
    private bool ConversationOpen => _conversationPanel?.IsOpen == true;

    private void BindAreaConversations(TerrainViewerArguments arguments, AreaLoadResult area)
    {
        EndConversation();
        _conversationNotice = null;
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
        _conversationHost = null; _conversationNotice = null;
    }

    private (long X, long Y)? ActorCell() =>
        _worldActor.Session is AreaGridSession session ? (AreaGridSession.Cell(session.State.CommittedCell).X, AreaGridSession.Cell(session.State.CommittedCell).Y) : null;

    /// <summary>LMB at a viewport position: talk when it hits a dialog NPC in reach; elsewhere it stays a no-op.</summary>
    private void TalkAt(Vector2 screen)
    {
        if (_conversationHost is null || _readyArea is null || ConversationOpen || ActorCell() is not { } cell) return;
        var point = GetViewport().GetCanvasTransform().AffineInverse() * screen;
        var speaker = _conversationHost.SpeakerAt(_readyArea.Plan.Commands, point.X, point.Y);
        if (speaker is null) return;
        if (AreaConversationHost.Distance(speaker, cell.X, cell.Y) > _conversationHost.Profile.Reach)
        {
            _conversationNotice = $"dialog NPC script {speaker.ScriptId} is {AreaConversationHost.Distance(speaker, cell.X, cell.Y)} tiles away; walking there is pending, come within {_conversationHost.Profile.Reach}";
            RefreshReadyStatus();
            return;
        }
        BeginConversation(speaker);
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
        return ActorCell() is { } cell && _conversationHost.Nearest(cell.X, cell.Y) is { } speaker
            ? $" | dialog NPC script {speaker.ScriptId} in reach: left-click it to talk"
            : $" | talk: left-click a dialog NPC within {_conversationHost.Profile.Reach} tiles ({_conversationHost.Speakers.Count} in core)";
    }
}
