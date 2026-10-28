using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-035: the hover descriptions of the hotkey bar (docs/world/spell-cast-host.md#the-hotkey-bars-hover-descriptions-arc-ui-035; ledger
// HK19–HK23). The pointer entering a slot or a recent-action button shows its entry's description in the message window: a skill-bar
// button's hint, a spell's hint, an item's window (its type icon and its name in its colour); in a pane mode only the name, in the pane's
// text area. Leaving the slot takes the hint or item window away (the original blanks the window, Steamwoven shows the viewed entry again,
// ML7). The inventory window describes the item under a still pointer the same way (0x573C74).
public partial class TerrainViewerApp
{
    private int _hotkeyDescriptionsForSelfTest;

    private void HotkeySlotEntered(int entry)
    {
        if (_areaSession is not { } session) return;
        var dragged = _hotkeyDrag is { Kind: Arcanum.Presentation.Hud.HotkeyDragSourceKind.Entry } drag && drag.Entry == entry;
        DescribeHotkey(session, session.QueryHotkeys().Entries[entry], dragged);
    }

    private void HotkeyRecentEntered(int index)
    {
        if (_areaSession is not { } session) return;
        DescribeHotkey(session, session.QueryHotkeys().Recent[index], dragged: false);
    }

    /// <summary>The pointer left a slot or a recent-action button (0x550720): in the message mode the hint or item window goes; a name
    /// in a pane's text area stays.</summary>
    private void HotkeyDescriptionLeft()
    {
        if (_messageLog.PaneMode) return;
        _messageLog.ClearHover();
        _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>0x550150 for <paramref name="entry"/>: the skill or spell hint is posted (shown only in the message mode and when no entry
    /// was stored in the last 3 s), the item window drawn; in a pane mode the name goes into the pane's text area.</summary>
    private void DescribeHotkey(GameSessionOwner session, HotkeyEntry entry, bool dragged)
    {
        if (!HotkeysActive || _gameBarContent is not { } content) return;
        var hover = AreaHoverDescriptions.Dispatch(entry, dragged);
        if (hover.Kind == AreaHotkeyHoverKind.None) return;
        AdoptSkillTexts(); AdoptSpellData();
        _hotkeyDescriptionsForSelfTest++;
        if (hover.Kind == AreaHotkeyHoverKind.Item) { DescribeItem(session, hover.Item!); return; }
        if (_messageLog.PaneMode)
        {
            if (_messageLog.ShowPaneName(MessagePaneMode, AreaHoverDescriptions.PaneName(hover, content.Texts, _spellData?.Data, item => item)))
                _gameBarPanel?.MessageLog.Render();
            return;
        }
        var data = _spellData?.Data;
        var hint = hover.Kind == AreaHotkeyHoverKind.Skill
            ? AreaHoverDescriptions.SkillHint(_skillTexts?.Texts, content.Texts, hover.Index)
            : AreaHoverDescriptions.SpellHint(hover.Index, AreaHoverDescriptions.SpellRow(session, data, hover.Index), data, content.Texts);
        PostMessage(hint, hover.Kind == AreaHotkeyHoverKind.Skill ? "skill hint" : "spell hint");
    }

    /// <summary>The weapon's spells replaced the spells or the other way round: the old mode's name goes (0x5506C0's mode build).</summary>
    private void ClearMessagePaneName()
    {
        if (_messageLog.PaneName is null) return;
        _messageLog.ClearPaneName();
        _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>The bar mode a pane holds the window in: 8 the weapon's spells, 1 the spells, 2 the skills (ML8).</summary>
    private int MessagePaneMode => _itemPaneItem is not null ? 8 : _gameBar.IsOn(AreaBarButton.Spells) ? 1 : 2;

    /// <summary>0x57CCF0 for an item the hero holds: the item window in the message mode (0x554F10's first part), its name in a pane's
    /// text area. The item's examine script is not run (not ready, HK22).</summary>
    private void DescribeItem(GameSessionOwner session, string item)
    {
        if (HeroItemKind(session, item) is not { } kind) return;
        AreaObjectRecord? row = null;
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var found)) { row = found; break; }
        var catalog = _hoverNames?.Catalog;
        var fallback = row is not null && _inventoryHost is { } host ? host.Name(item, catalog) : AreaInventoryHost.KindName(kind, catalog);
        var described = _hotkeyItemIcons?.Describe(kind, row, session.QueryItemIdentified(item), catalog, fallback)
            ?? new AreaItemDescription(null, fallback, AreaMessageColour.White);
        var shown = _messageLog.PaneMode ? _messageLog.ShowPaneName(MessagePaneMode, described.Name, item: true)
            : _messageLog.ShowItem(AreaHoverDescriptions.ItemWindow(described));
        if (shown) _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>The inventory window's still pointer rested on an item (or on none): a new item under it is described (0x573C74); the
    /// window keeps the last description when the pointer rests on no item.</summary>
    private void InventoryPointerStill(string? item)
    {
        if (item is null || _areaSession is not { } session || _gameBarPanel is not { Visible: true }) return;
        _hotkeyDescriptionsForSelfTest++;
        DescribeItem(session, item);
    }
}
