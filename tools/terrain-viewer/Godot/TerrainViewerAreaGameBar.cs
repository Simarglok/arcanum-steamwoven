using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-018: the original's in-game interface bars over an AREA session (docs/rendering/terrain-viewer.md#interface-bars-arc-ui-018;
// checked in the 1.0.7.4 executable, ledger GB1–GB7). The bar buttons open the viewer's windows as their keys do; a button whose
// window another bar window covers switches to it (the original's interface mode change leaves the old mode first). The bars are
// over the windows they open, which lay themselves out in the part the bars leave free; they hide under the windows they cannot
// reach or that do not fit (conversation, barter, reading, save list, palette, menu, world map). Automated runs draw no bars unless
// their smoke asks, so world presses at fixed screen points are not taken by them. ARC-UI-023: the bottom strip's message window draws
// the message log (ledger ML1–ML8), and the bar's hover names are hover entries of that log. ARC-MAGIC-026: the top strip's maintain bar
// (TerrainViewerMaintainBar.cs).
public partial class TerrainViewerApp
{
    private AreaGameBarPanel? _gameBarPanel;
    private AreaGameBar _gameBar = new();
    private AreaGameBarContent? _gameBarContent;
    private string? _gameBarModule;
    private string? _gameBarNotice;
    private bool _gameBarForSelfTest;
    private readonly List<(int Id, bool Started)> _gameBarSoundsForSelfTest = [];
    // ARC-UI-023: whether a pane (spells, the weapon's spells, skills) held the message window at the last frame.
    private bool _messagePaneShown;

    private bool GameBarActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area
        && !MainMenuOpen && !ConversationOpen && !BarterOpen && !SaveListOpen && !ReadingOpen && !PaletteOpen && !WorldMapOpen
        && (!ViewerAutomatedStorage.Automated || _gameBarForSelfTest);

    /// <summary>Per frame: shows the bars while an area session plays (reading their art once a run) and follows the spell pane.</summary>
    private void SyncGameBar()
    {
        // ARC-MAGIC-026: a drop of the maintain bar's open count ends the closed slots' spells whether the bars show or not.
        SyncMaintainClosing();
        if (!GameBarActive)
        {
            if (_gameBarPanel is { Visible: true } hidden)
            {
                // No leave or release reaches hidden bars: the pointer state starts afresh when they show again.
                hidden.Visible = false; _gameBar.Reset(); _messageLog.ClearHover(); hidden.Render(); hidden.ShowHoverPortrait(null); ResetHoverWindow(); _hud?.SetTopInset(0);
            }
            HideHotkeyBar();
            HideSpellBar();
            return;
        }
        if (_gameBarPanel is null)
        {
            _gameBarPanel = new AreaGameBarPanel(); AddChild(_gameBarPanel);
            _gameBarPanel.Effect += GameBarEffect;
            _gameBarPanel.PointerMoved += GameBarPointerMoved;
        }
        var module = _arguments?.ModulePath;
        if (_gameBarContent is null || module != _gameBarModule)
        {
            _gameBarModule = module;
            _gameBarContent = SelfTestBootstrap || module is null ? AreaGameBarContent.Empty("self-test: no game folder") : AreaGameBarContent.Load(module);
            GD.Print($"ARC-UI-018 bars art ready={_gameBarContent.ArtReady} texts fromGame={_gameBarContent.Texts.FromGame} {_gameBarContent.Detail}");
            _gameBarPanel.Bind(GameBarTextures(_gameBarContent), _gameBar, _messageLog);
        }
        // ARC-DEV-156: the message window's texts in the original's font art (read once a module, null without a game folder).
        _gameBarPanel.MessageLog.SetFonts(TextFonts());
        // The M key shows and hides the same pane the Spells button does; showing it is the original's spells mode (Skills goes up).
        var spells = _spellPane?.Shown == true;
        if (spells != _gameBar.IsOn(AreaBarButton.Spells) || (spells && _gameBar.IsOn(AreaBarButton.Skills)))
        {
            _gameBar.SetOn(AreaBarButton.Spells, spells);
            if (spells) _gameBar.SetOn(AreaBarButton.Skills, false);
            _gameBarPanel.Render();
        }
        // ARC-UI-023: a pane taking the message window (its button down) or giving it back (ML8).
        var pane = spells || _gameBar.IsOn(AreaBarButton.Skills);
        if (pane != _messagePaneShown)
        {
            _messagePaneShown = pane;
            if (pane) _messageLog.EnterPane(); else _messageLog.EnterMessages();
            _gameBarPanel.MessageLog.Render();
        }
        _gameBarPanel.Visible = true;
        _hud?.SetTopInset(AreaGameBarPanel.TopInset);
        SyncHotkeyBar();
        SyncMaintainBar();
        SyncSpellBar();
    }

    // ARC-UI-089: frames with an HD key draw their HD redraw while the HD profile is on (InterfaceHd).
    private static IReadOnlyDictionary<int, Texture2D[]> GameBarTextures(AreaGameBarContent content) =>
        content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready).ToDictionary(load => load.Number,
            load => load.Frames.Select(InterfaceHd.Texture).ToArray());

    private void PlayBarSound(int id, string what)
    {
        var started = PlayInterfaceSound(id, what);
        if (_gameBarForSelfTest) _gameBarSoundsForSelfTest.Add((id, started));
    }

    private void GameBarEffect(AreaBarEffect effect)
    {
        foreach (var id in effect.Sounds) PlayBarSound(id, "bar button");
        if (effect.HoverChanged) GameBarHover();
        if (effect.Act is { } button) RunBarButton(button, effect);
        if (effect.Sounds.Count > 0 || effect.Act is not null) RefreshAreaStatus();
    }

    private void RunBarButton(AreaBarButton button, AreaBarEffect effect)
    {
        GD.Print($"ARC-UI-018 bar button {button}");
        _gameBarNotice = null;
        switch (button)
        {
            case AreaBarButton.Character: BarWindow("character sheet", InterfaceMode.Character, SheetActive, SheetOpen, OpenSheet, EndSheet); break;
            case AreaBarButton.Logbook: BarWindow("logbook", InterfaceMode.Logbook, LogbookActive, LogbookOpen, OpenLogbook, CloseLogbookByPlayer); break;
            case AreaBarButton.Inventory: BarWindow("inventory", InterfaceMode.Inventory, InventoryActive, InventoryOpen, () => OpenInventory(), EndInventory); break;
            case AreaBarButton.Schematics: BarWindow("schematics", InterfaceMode.Schematic, SchematicsActive, SchematicsOpen, () => OpenSchematics(), CloseSchematicsByPlayer); break;
            case AreaBarButton.Map: BarWindow("world map", InterfaceMode.WorldMap, WorldMapActive, WorldMapOpen, OpenWorldMap, CloseWorldMapByPlayer); break;
            case AreaBarButton.Combat:
                if (!CombatModeActive) _gameBarNotice = "combat: not available here";
                else if (WorldModal) _gameBarNotice = "combat: ignored while a window is open";
                else ToggleCombatMode();
                break;
            case AreaBarButton.Spells:
                // A refused Spells press gives the pair back as it was.
                if (!BarSpells() && effect.LetGo is { } other) _gameBar.SetOn(other, true);
                break;
            case AreaBarButton.Skills:
                // The original's skills mode shows the skill buttons in the message window; the viewer has no such pane yet.
                if (_gameBar.IsOn(AreaBarButton.Skills)) { _spellPane?.HidePane(); _gameBarNotice = "skills: the skill pane is not built"; }
                break;
            case AreaBarButton.FatePoints: _gameBarNotice = "fate points: the window is not built"; break;
            case AreaBarButton.Sleep:
                // ARC-WORLD-1160: the bar button runs the same opener as the key S (its own decision answers a dead or unconscious hero).
                if (!SleepActive) _gameBarNotice = "sleep: not available here";
                else if (SleepOpen) OpenSleep(); // the same decision as the key: a dead hero gets nothing, even the close
                else if (PaletteOpen || ConversationOpen || SaveListOpen || MainMenuOpen) _gameBarNotice = "sleep: ignored while another window owns input";
                else OpenSleep();
                break;
        }
        _gameBarPanel?.Render();
    }

    /// <summary>A window button: closes its window when open; otherwise closes the other window that is open and opens its own (the
    /// original's opener pops and pushes, SA40; ARC-DEV-111). Only the dialog (its modal hook swallows the buttons) and the panels outside
    /// the stack (palette, save list, menu) keep the press from acting.</summary>
    private void BarWindow(string name, InterfaceMode mode, bool active, bool open, Action openWindow, Action close)
    {
        if (!active) { _gameBarNotice = $"{name}: not available here"; return; }
        if (open) { close(); return; }
        if (PaletteOpen || ConversationOpen || SaveListOpen || MainMenuOpen) { _gameBarNotice = $"{name}: ignored while another window owns input"; return; }
        if (WindowRefusedForHero(mode)) { _gameBarNotice = $"{name}: not available for the hero as it is"; return; }
        CloseOtherInterfaceWindows();
        openWindow();
    }

    /// <summary>ARC-DEV-111 (SA40): the window that is open leaves through its own End routine (its sounds, its session closes), so a
    /// window opened in its place replaces it. The dialog is not closed here: it refuses every opener.</summary>
    private void CloseOtherInterfaceWindows()
    {
        if (SheetOpen) EndSheet();
        if (LogbookOpen) CloseLogbookByPlayer();
        if (InventoryOpen) EndInventory();
        if (SchematicsOpen) CloseSchematicsByPlayer();
        if (WorldMapOpen) CloseWorldMapByPlayer();
        if (BarterOpen) CloseBarterByPlayer();
        if (ReadingOpen) CloseReadingByPlayer();
        if (SleepOpen) EndSleep();
    }

    /// <summary>The Spells toggle changed: on shows the spell pane, off hides it. False when an on press is refused.</summary>
    private bool BarSpells()
    {
        var on = _gameBar.IsOn(AreaBarButton.Spells);
        if (!on) { _spellPane?.HidePane(); _spellNotice = null; return true; }
        if (!SpellMapActive || _areaSession?.QueryHero() is null) _gameBarNotice = "spells: needs a hero on a map";
        else if (WorldModal) _gameBarNotice = "spells: ignored while a window is open";
        else { ShowSpellPane(); return true; }
        _gameBar.SetOn(AreaBarButton.Spells, false);
        return false;
    }

    /// <summary>ARC-UI-023: the bar's hover name is a hover entry of the message log (GB5, ML2): written when the pointer enters a
    /// button and again on each move while it is there, shown only in the message mode and when no entry was stored in the last 3 s;
    /// leaving the button takes it away.</summary>
    private void GameBarHover()
    {
        if (_gameBar.HoverMessage is { } message && _gameBarContent is { } content)
            PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, content.Texts.Text(message)), "hover");
        else { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
    }

    private void GameBarPointerMoved(Vector2 screen)
    {
        if (_gameBar.HoverMessage is not null) GameBarHover();
        // A move over the bottom strip gives the window back to the pane whose button is still down (0x54B70B); a move above it
        // gives the spells or skills pane's window to the log (0x54B7C0), not the weapon's spells.
        var down = _gameBar.IsOn(AreaBarButton.Spells) || _gameBar.IsOn(AreaBarButton.Skills);
        if (_messageLog.PointerMoved((int)MathF.Floor(screen.X), (int)MathF.Floor(screen.Y), down, weaponPane: _itemPaneItem is not null))
            _gameBarPanel?.MessageLog.Render();
    }

    private string GameBarStatus() => _gameBarPanel is not { Visible: true } ? "" : " | interface bars" + (_gameBarNotice is null ? "" : ": " + _gameBarNotice) + MaintainBarStatus();
}
