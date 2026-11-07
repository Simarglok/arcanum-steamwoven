using Arcanum.Data.Messages;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-008: the quest and rumor logbook of the session's PC in crash-site-area (exterior, cave and map mode). The original
// Logbook key (L, logical key down) opens it from the world, as the original's in-game key handler calls the logbook toggle
// for the local PC (ledger LB1); L again, Escape or a controller cancel closes it. Like the character sheet it is modal (gate
// closed, clock paused, a walk stopped). It opens with or without a hero and not for a dead hero. The tab is kept across
// close and open, as the original keeps it, and starts at the rumors for a new session (the original resets it with the
// game). The texts come from the module's own data, loaded once per module in the background.
public partial class TerrainViewerApp
{
    private AreaLogbookPanel? _logbookPanel;
    private string? _logbookStatus;
    private (GameSessionOwner Session, long Revision, AreaLogbookTab Tab, LogbookTexts? Texts)? _logbookStamp;
    private AreaLogbookTab _logbookTab;
    private GameSessionOwner? _logbookTabSession;
    private Task<AreaLogbookTextsLoad>? _logbookTextsTask;
    private string? _logbookTextsModule;
    private AreaLogbookTextsLoad? _logbookTexts;
    private bool LogbookOpen => WindowOpen(InterfaceMode.Logbook);
    private bool LogbookActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    /// <summary>Starts loading the logbook texts for this module once; a reload, the cave or a map transfer reuses them.</summary>
    private void BindAreaLogbook(TerrainViewerArguments arguments)
    {
        if (_logbookTextsModule == arguments.ModulePath) return;
        _logbookTextsModule = arguments.ModulePath; _logbookTexts = null;
        var module = arguments.ModulePath;
        _logbookTextsTask = Task.Run(() => AreaLogbook.LoadTexts(module));
    }

    private void AdoptLogbookTexts()
    {
        if (_logbookTexts is not null || _logbookTextsTask is not { IsCompleted: true } task) return;
        _logbookTexts = task.IsCompletedSuccessfully ? task.Result
            : new(AreaLogbookTextsStatus.TextsFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _logbookTextsTask = null; _logbookStamp = null;
        GD.Print($"ARC-UI-008 logbook texts status={_logbookTexts.Status} detail={_logbookTexts.Detail}");
    }

    private AreaLogbookBuild BuildLogbook() =>
        AreaLogbook.Build(_areaSession!, _logbookTexts?.Texts, _logbookTab, AreaHoverIndex.TextProfile);

    private void OpenLogbook()
    {
        if (!LogbookActive || !WindowCanOpen(InterfaceMode.Logbook)) return;
        AdoptLogbookTexts();
        if (!ReferenceEquals(_logbookTabSession, _areaSession)) { _logbookTab = AreaLogbookTab.Rumors; _logbookTabSession = _areaSession; _logbookPanel?.ResetPages(); }
        var build = BuildLogbook();
        if (build.View is not { } view)
        {
            _logbookStatus = "refused: the hero is dead";
            GD.Print($"ARC-UI-008 logbook refused={build.Refusal}");
            RefreshAreaStatus();
            return;
        }
        if (_logbookPanel is null)
        {
            _logbookPanel = new AreaLogbookPanel(); AddChild(_logbookPanel);
            _logbookPanel.CloseRequested += CloseLogbookByPlayer;
            _logbookPanel.TabRequested += SelectLogbookTab;
            _logbookPanel.SoundRequested += id => PlayInterfaceSound(id, "logbook page");
            _logbookPanel.HintRequested += LogbookHint;
            // ARC-UI-098: the lens shows the game view around the hero (LB22, IW33).
            _logbookPanel.HolderView = InventoryHolderView;
        }
        // ARC-UI-074: the original's book when the game's art and fonts read; the module can change between two openings.
        if (LogbookArtLook() is { } look) _logbookPanel.SetArt(look.Art, look.Fonts);
        else if (_logbookPanel.ArtSet) _logbookPanel.ClearArt();
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        // ARC-UI-098 (LB22): the create centres the game view on the logbook's object, the PC (0x53F27A → 0x4B8CE0), for the lens.
        CentreGameViewOnHolder();
        if (!OpenWindowMode(InterfaceMode.Logbook, () => _logbookPanel!.Open(view), () => _logbookPanel!.CloseView())) return;
        _logbookStamp = Stamp(); _logbookStatus = null;
        // SN37: opening selects the tab (3011, then the tab's 3010) and then plays the book's 3008.
        PlayInterfaceSound(BookSwitchSound, "logbook open"); PlayInterfaceSound(BookPageSound, "logbook open"); PlayInterfaceSound(BookOpenSound, "logbook open");
        GD.Print($"ARC-UI-008 logbook open tab={view.Tab} entries={view.Entries.Count} low={view.LowIntelligence} note={view.Note ?? "none"}");
        RefreshReadyStatus();
    }

    // ARC-UI-098 (LB22): a hovered tab's hint is the tab's message of logbk_ui.mes, written to the message window (0x550750, type 6): shown only when
    // 3 s passed since the last stored message, as the world map's button hints (WM61); leaving the tab clears it (0x550720).
    private void LogbookHint(string? text)
    {
        if (text is null) { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
        else PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, text), "logbook hint");
    }

    private (GameSessionOwner, long, AreaLogbookTab, LogbookTexts?) Stamp() =>
        (_areaSession!, _areaSession!.Version.Revision, _logbookTab, _logbookTexts?.Texts);

    private void SelectLogbookTab(AreaLogbookTab tab)
    {
        if (!LogbookOpen) return;
        // SN37: a tab press plays the page sound (the original's tab handler has no same-tab test).
        PlayInterfaceSound(BookPageSound, "logbook tab");
        _logbookTab = tab;
        SyncAreaLogbook();
    }

    // The player closed it (L, Escape, Cancel, the button): the book's close sound plays; a teardown closes it silently.
    private void CloseLogbookByPlayer() => EndLogbook(byPlayer: true);

    private void EndLogbook() => EndLogbook(byPlayer: false);

    private void EndLogbook(bool byPlayer)
    {
        if (_logbookPanel is null || !LogbookOpen) return;
        GD.Print("ARC-UI-008 logbook closed");
        if (byPlayer) PlayInterfaceSound(BookCloseSound, "logbook close");
        CloseWindowMode(InterfaceMode.Logbook); _logbookStamp = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>Per frame while open: a changed or replaced session, another tab or newly loaded texts rebuild the logbook;
    /// a session that can no longer show it (gone, or its hero died) closes it.</summary>
    private void SyncAreaLogbook()
    {
        if (!LogbookOpen) { AdoptLogbookTexts(); return; }
        if (!LogbookActive) { EndLogbook(); return; }
        AdoptLogbookTexts();
        var stamp = Stamp();
        if (Equals(stamp, _logbookStamp)) return;
        _logbookStamp = stamp;
        if (BuildLogbook().View is { } view) _logbookPanel!.Render(view);
        else EndLogbook();
    }

    /// <summary>The original Logbook key (L) acts on its logical key down while an area session exists. Returns true when the
    /// key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteLogbookKey(WorldKeyDecision decision)
    {
        if (!LogbookActive || decision.Key != WorldKey.L) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _logbookStatus = "L ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenLogbook();
        return true;
    }

    /// <summary>While the logbook is open it owns every key: L and Escape close it, Tab switches the tab, on a fresh press.</summary>
    private void HandleLogbookKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed || key.Echo) return;
        if (routed == Key.L && !CharacterKeySilenced(key, routed)) CloseLogbookByPlayer();
        else if (routed == Key.Tab) _logbookPanel!.Confirm();
        // PROJECT: the original turns pages with its two buttons only; the arrow and page keys turn them as well.
        else if (routed is Key.Left or Key.Pageup) _logbookPanel!.Turn(-1);
        else if (routed is Key.Right or Key.Pagedown) _logbookPanel!.Turn(1);
    }

    private void HandleLogbookPulse(IntentFrame intent)
    {
        if (intent.Cancel) CloseLogbookByPlayer();
        else if (intent.Confirm) _logbookPanel!.Confirm();
        // PROJECT: the controller's previous / next turn the book's pages (the original has its two buttons only).
        else if (intent.Previous) _logbookPanel!.Turn(-1);
        else if (intent.Next) _logbookPanel!.Turn(1);
    }

    private string AreaLogbookStatus() => !LogbookActive ? "" : LogbookOpen ? " | logbook open: L closes"
        : " | L logbook" + (_logbookStatus is null ? "" : ": " + _logbookStatus);
}
