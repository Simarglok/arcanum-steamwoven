using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-2304: the original's magick weapon button and pane, bar mode 8 (ledger BM6, IP1–IP10). While the bars show, the bottom strip draws
// the button `MagickWeapon_Butt` (563) at (161, 443) whenever the wielded weapon qualifies (0x556EF0), with or without a pane up; its release
// toggles mode 8 for the wielded weapon (0x556EA0 → 0x5506C0), which takes the message window in place of the spells or skills mode. Mode 8
// draws `MagickWeapon_Rot` (564) and an icon button per shown slot (0x551660) at slot i's place; the release over a slot activates its spell
// with the weapon as the source (0x57C040 → 0x57BC70: the cast pays the weapon's charge, not the hero's fatigue), the pointer entering one
// writes the spell's name in mode 8's text area (0x5507E0 → 0x4B1600), a right press starts a hotkey drag (IP9). The mode closes when the
// weapon leaves its slot (0x5570D0). Without the bars or the arts the diagnostic spell pane keeps its weapon button as before.
public partial class TerrainViewerApp
{
    private AreaWeaponBarView? _weaponBarView;
    private (GameSessionOwner Session, AreaWeaponSpellPane? Pane, bool Button, bool Open, object? Data)? _weaponBarShown;
    private int _weaponBarReleasesForSelfTest;

    /// <summary>Whether the bottom strip draws the weapon button and pane: the bars show and the button's and the pane's arts read.</summary>
    private bool WeaponPaneInStrip =>
        !_weaponStripOffForSelfTest && _gameBarPanel is { Visible: true } && Art(MagickWeaponPaneRules.ButtonArt) is not null && Art(MagickWeaponPaneRules.BackgroundArt) is not null;

    /// <summary>Per frame while the bars show (after the hotkey bar, whose art the pane shares): the button and, in bar mode 8, the pane.</summary>
    private void SyncWeaponBar()
    {
        if (_gameBarPanel is null) return;
        if (_weaponBarView is null)
        {
            _weaponBarView = new AreaWeaponBarView();
            // Under the message window's drawing: a hover's name is written over the mode's background (0x5509C0).
            _gameBarPanel.AddPartUnderMessageLog(_weaponBarView);
            _weaponBarView.ButtonPressed += () => PlayBarSound(AreaGameBarLayout.PressSound, "magick weapon button");
            _weaponBarView.ButtonReleased += WeaponBarButtonReleased;
            _weaponBarView.SlotPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "magick weapon slot");
            _weaponBarView.SlotReleased += WeaponBarSlotReleased;
            _weaponBarView.SlotRightPressed += HotkeyWeaponPaneDrag;
            _weaponBarView.SlotEntered += WeaponBarSlotEntered;
        }
        if (_areaSession is not { } session || !WeaponPaneInStrip || !SpellMapActive) { HideWeaponBar(); return; }
        if (!_weaponBarView.Visible) _weaponBarView.Visible = true;
        var pane = WeaponPaneNow();
        var button = pane is { ButtonShown: true };
        var open = _itemPaneItem is not null && pane is { Open: true } && _messageLog.PaneMode;
        var stamp = (session, pane, button, open, (object?)_spellData?.Data);
        if (_weaponBarShown == stamp) return;
        _weaponBarShown = stamp;
        _weaponBarView.Bind(WeaponBarLook(pane, button, open));
    }

    /// <summary>The bars went away or the arts did not read: nothing of the weapon in the strip.</summary>
    private void HideWeaponBar()
    {
        _weaponBarShown = null;
        if (_weaponBarView is { Visible: true }) _weaponBarView.Visible = false;
    }

    private AreaWeaponBarLook WeaponBarLook(AreaWeaponSpellPane? pane, bool button, bool open)
    {
        // The pane shows only spells that are not technological (0x459FC0), so each icon is the spell's own or the generic art 36 (0x4B1570).
        var slots = open && pane is not null
            ? pane.Buttons.Select(choice => new AreaWeaponBarSlot(choice.Slot, choice.Spell, Art(MagickWeaponPaneRules.SpellIcon(choice.Spell, technological: false)))).ToArray()
            : [];
        return new(Art(MagickWeaponPaneRules.ButtonArt), button, First(Art(MagickWeaponPaneRules.BackgroundArt)), open, slots);
    }

    /// <summary>0x54B5D0 → 0x556EA0: the release over the weapon button toggles bar mode 8 for the wielded weapon.</summary>
    private void WeaponBarButtonReleased(bool over)
    {
        // ARC-UI-2024 (DB2): consumed without effect while the dialog's hook is set.
        if (!over || ConversationOpen) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "magick weapon button");
        _weaponBarReleasesForSelfTest++;
        ToggleWeaponPaneInStrip();
    }

    /// <summary>0x556EA0 with no object, then 0x5506C0 with 8 (<see cref="MagickWeaponPaneRules.ModeAfterButton"/>): mode 8 for the wielded
    /// weapon, or the message mode when it is up. Mode 8 replaces the spells or skills mode, so their toggles come up (the original leaves
    /// the toggle's drawing as it was: ledger IP10, owner decision #2312).</summary>
    private void ToggleWeaponPaneInStrip()
    {
        var mode = MagickWeaponPaneRules.ModeAfterButton(_itemPaneItem is not null ? MagickWeaponPaneRules.BarMode : 0);
        if (mode != MagickWeaponPaneRules.BarMode) _itemPaneItem = null;
        else if (WeaponPaneNow() is { ButtonShown: true } pane)
        {
            TakeWindowFromSpells();
            _gameBar.SetOn(AreaBarButton.Spells, false);
            _gameBar.SetOn(AreaBarButton.Skills, false);
            _itemPaneItem = pane.Item;
        }
        _weaponPaneCache = null;
        ClearMessagePaneName();
        GD.Print($"ARC-UI-2304 weapon-pane open={_itemPaneItem ?? "no"}");
        _gameBarPanel?.Render();
        RefreshAreaStatus();
    }

    /// <summary>0x54B5D0 → 0x57C040: the release over a slot's button activates the slot's spell for the weapon.</summary>
    private void WeaponBarSlotReleased(int slot, bool over)
    {
        if (!over || ConversationOpen) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "magick weapon slot");
        _weaponBarReleasesForSelfTest++;
        ActivateItemSlot(slot);
        RefreshAreaStatus();
    }

    /// <summary>The pointer entered a slot's button (0x5507E0 with the slot's spell): in mode 8 the spell's name (0x4B1600, spell.mes line =
    /// the spell's number), in the mode's text area (0x5509C0). Leaving keeps it.</summary>
    private void WeaponBarSlotEntered(int slot)
    {
        if (!_messageLog.PaneMode || _itemPaneItem is not { } item || SpellCasterNow()?.Item(item) is not { } spells || slot < 0 || slot >= spells.Spells.Count) return;
        AdoptSpellData();
        var name = AreaSpellsPage.SpellName(_spellData?.Data, spells.Spells[slot]);
        if (_messageLog.ShowPaneName(MagickWeaponPaneRules.BarMode, name)) _gameBarPanel?.MessageLog.Render();
    }
}
