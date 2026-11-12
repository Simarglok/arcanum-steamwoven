using Arcanum.Input;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1113: the original's skill pane in the bottom strip (docs/world/spell-cast-host.md#the-skill-pane-in-the-bottom-strip-arc-ui-1113;
// ledger SKP1–SKP6). While the bars show and the Skills toggle is down (the Skills button, or K) and the pane holds the message window
// (ML8), the window draws bar mode 2: the background art 274 and the skill bar's four buttons (prowling, pick pocket, disarm traps, repair).
// A button is activated on the release over it (0x54BAF5 → 0x57EFA0, 0x579FA0): the latest recent action first, then the skill-bar press the
// hotkey bar's skill entries run (ActivateSkillButton); a right press starts the hotkey drag (0x57E5D0); the pointer entering a button writes
// its name (skill_ui.mes 100 + button) in the mode's text area (0x5508C0, 0x5509C0). K requests the mode as the Skills button does, without its sound
// (0x54DBF0). Without the bars or the pane's art the Skills button only toggles.
public partial class TerrainViewerApp
{
    private AreaSkillBarView? _skillBarView;
    private (GameSessionOwner Session, object? Art)? _skillBarShown;
    private int _skillBarPressesForSelfTest;
    private string? _skillBarOutcome;

    /// <summary>Per frame while the bars show (after the hotkey bar, whose art the pane shares): the view on the bottom strip, shown in bar
    /// mode 2, redrawn when the session or the art changed.</summary>
    private void SyncSkillBar()
    {
        if (_gameBarPanel is null || _areaSession is not { } session) return;
        if (_skillBarView is null)
        {
            _skillBarView = new AreaSkillBarView { Visible = false };
            // Under the message window's drawing: a hover's name is written over the mode's background (0x5509C0).
            _gameBarPanel.AddPartUnderMessageLog(_skillBarView);
            _skillBarView.ButtonPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "skill pane button");
            _skillBarView.ButtonReleased += SkillBarReleased;
            _skillBarView.ButtonRightPressed += SkillBarDrag;
            _skillBarView.Entered += SkillBarEntered;
        }
        var shown = SkillPaneInStrip && _gameBar.IsOn(AreaBarButton.Skills) && _messageLog.PaneMode && !_skillBarHiddenForCapture;
        if (_skillBarView.Visible != shown) _skillBarView.Visible = shown;
        if (!shown) return;
        var stamp = (session, (object?)_hotkeyBarArt);
        if (_skillBarShown == stamp) return;
        _skillBarShown = stamp;
        _skillBarView.Bind(new(First(Art(SkillPaneRules.BackgroundArt)), [.. Enumerable.Range(0, SkillPaneRules.Buttons).Select(b => Art(SkillPaneRules.ButtonArt(b)!.Value))]));
    }

    /// <summary>Whether the bottom strip can draw the skill pane: the bars show and the pane's background art read.</summary>
    private bool SkillPaneInStrip => _gameBarPanel is { Visible: true } && Art(SkillPaneRules.BackgroundArt) is not null;

    /// <summary>The bars went away: no pane is drawn.</summary>
    private void HideSkillBar()
    {
        if (_skillBarView is not null) _skillBarView.Visible = false;
    }

    /// <summary>0x54BAF5: the release over a button activates its skill, the latest recent action first (0x57EFA0, then 0x579FA0 with the PC:
    /// nothing for a hero who is dead or unconscious).</summary>
    private void SkillBarReleased(int button, bool over)
    {
        if (!over) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "skill pane button");
        _skillBarPressesForSelfTest++;
        // The in-game filter passes no button event while another combatant acts in a turn-based round (GB12, CT6).
        if (InTurnBasedRound && _areaSession?.QueryCombatRound() is { Actor.IsHero: false }) { _gameBarNotice = "skills: another critter's turn"; RefreshAreaStatus(); return; }
        if (!HotkeysActive || _areaSession is not { } session) { _gameBarNotice = "skills: needs a hero on a map"; return; }
        RecordRecentAction(HotkeyEntry.ForSkill((SkillBarButton)button));
        var life = session.QueryHeroLife()?.Status;
        _skillBarOutcome = !HotkeyRules.PcCanActivate(false, true, life?.IsDead == true, life?.IsUnconscious == true, false)
            ? "the hero cannot act"
            : ActivateSkillButton(session, (SkillBarButton)button);
        _gameBarNotice = $"skills: {_skillBarOutcome}";
        GD.Print($"ARC-UI-1113 skill-pane button={(SkillBarButton)button} outcome={_skillBarOutcome}");
        _hotkeyBarShown = null;
        RefreshAreaStatus();
    }

    /// <summary>The pointer entered a button (0x5508C0): in the skills mode only the name, in the mode's text area. Leaving keeps it (0x550720
    /// acts in the message mode only).</summary>
    private void SkillBarEntered(int button)
    {
        if (!_messageLog.PaneMode || _gameBarContent is not { } content) return;
        if (_messageLog.ShowPaneName(SkillPaneRules.BarMode, content.Texts.SkillButtonName(button))) _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>A right press on a button (0x57E5D0): the drag onto the hotkey bar starts with that skill-bar button.</summary>
    private void SkillBarDrag(int button) => StartHotkeyDrag(HotkeyDragSourceKind.SkillButton, -1, HotkeyEntry.ForSkill((SkillBarButton)button));

    /// <summary>The key K (acting on its release, as the original's scancode handler does, SU14; 0x54DBF0 with the Skills toggle): the same toggle as
    /// the Skills button, with no press sound; Spells goes up when Skills comes down. Returns true when the key belongs to it, so it is not
    /// reported as a reserved, unimplemented original key.</summary>
    private bool RouteSkillKey(WorldKeyDecision decision)
    {
        if (decision.Key != WorldKey.K || _areaSession?.QueryHero() is null) return false;
        if (decision.Pressed || decision.Echo) return true;
        // The original refuses only the main menu (0x541680, no bars then); like the button, the key works over the windows the bars stay over.
        if (!_worldFocused || WorldUiFocus) _gameBarNotice = "K ignored: palette, dialog or UI focus owns input";
        else if (_gameBarPanel is not { Visible: true }) _gameBarNotice = "K: the interface bars are not shown";
        else ToggleSkillsPane();
        RefreshAreaStatus();
        return true;
    }

    /// <summary>The Skills toggle comes down (the button or K): the spell pane goes, and with it the weapon's spells pane (bar mode 8) and the
    /// spell notice, as the mode request replaces the mode shown.</summary>
    private void TakeWindowFromSpells()
    {
        _spellPane?.HidePane();
        _spellNotice = null;
        _itemPaneItem = null; _weaponPaneCache = null;
    }

    private void ToggleSkillsPane()
    {
        var on = !_gameBar.IsOn(AreaBarButton.Skills);
        _gameBarNotice = null;
        if (on) { TakeWindowFromSpells(); _gameBar.SetOn(AreaBarButton.Spells, false); }
        else ClearMessagePaneName();
        _gameBar.SetOn(AreaBarButton.Skills, on);
        if (on && !SkillPaneInStrip) _gameBarNotice = "skills: the pane's art did not read";
        _gameBarPanel?.Render();
    }
}
