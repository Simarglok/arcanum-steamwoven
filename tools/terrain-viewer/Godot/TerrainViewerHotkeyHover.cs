using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    /// <summary>0x57CCF0 for an item the hero holds: the item's examine script first (<see cref="ExamineAllows"/>, ARC-DEV-326), then the item
    /// window in the message mode (0x554F10's first part), its name in a pane's text area.</summary>
    private void DescribeItem(GameSessionOwner session, string item)
    {
        if (InventoryItemKind(session, item) is not { } kind) return;
        AreaObjectRecord? row = null;
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var found)) { row = found; break; }
        if (row is not null && !ExamineAllows(row)) return;
        if (row is null && !ExamineAllowsMade(session, item)) return;
        var catalog = _hoverNames?.Catalog;
        var fallback = row is not null && _inventoryHost is { } host ? host.Name(item, catalog) : AreaInventoryHost.KindName(kind, catalog);
        var described = _hotkeyItemIcons?.Describe(kind, row, session.QueryItemIdentified(item), catalog, fallback, ItemWindowContext(session, item))
            ?? new AreaItemDescription(null, fallback, AreaMessageColour.White);
        var shown = _messageLog.PaneMode ? _messageLog.ShowPaneName(MessagePaneMode, described.Name, item: true)
            : _messageLog.ShowItem(AreaHoverDescriptions.ItemWindow(described));
        if (shown) _gameBarPanel?.MessageLog.Render();
    }

    /// <summary>
    /// ARC-DEV-326 (B18, ledger HK22): the first step of every object description (0x57CD60, the call at 0x57CD93): the object's examine script
    /// (attachment point 0) runs with the hero as the triggerer, and the description follows only when it does not answer "skip default".
    /// The run's printed lines go to the message window as the original's print line does, its effects are the session's. An object with no
    /// script, a viewer without the script content or a run the viewer cannot apply (<see cref="AreaUseScriptHost.ExamineSkips"/>) describes as before.
    /// </summary>
    private bool ExamineAllows(AreaObjectRecord row)
    {
        if (_readyArea is not { } area || _areaSession is not { } session || UseScriptHost(area.Snapshot) is not { } host) return true;
        var ran = Awarded(host, host.RunExamine(row, session.Rules.Scripts));
        if (ran.Run is null && ran.Outcome == AreaScriptedUseOutcome.NoTeleport) return true;
        _examineRunsForSelfTest++;
        GD.Print($"ARC-DEV-326 examine entity={AreaUseScriptHost.EntityId(row)} outcome={ran.Outcome} run={ran.Run?.Outcome.ToString() ?? "-"} detail={ran.Detail}");
        foreach (var entry in ScriptLineEntries(ran.Printed)) PostMessage(entry, "script line");
        return !AreaUseScriptHost.ExamineSkips(ran);
    }

    /// <summary>
    /// ARC-SCRIPT-1119 (HK24): the same first step for an item the session made (<c>made:n</c>), which has no object row: its examine script is the
    /// prototype's point 0 (or the slot a run kept on the item), run by the session as an item run with the hero as the triggerer
    /// (<see cref="GameSessionOwner.RunItemExamine"/>). The description follows only when the answer is not "skip default"; the printed lines go to the
    /// message window. An item with no script, a viewer without the script content or a run the session cannot apply (refused, nothing applied)
    /// describes as before.
    /// </summary>
    private bool ExamineAllowsMade(GameSessionOwner session, string item)
    {
        if (!item.StartsWith("made:", StringComparison.Ordinal) || _inventoryHost?.ItemScripts(session) is not { } host) return true;
        var ran = session.RunItemExamine(session.Version, item, host);
        if (ran.Scripts.Count == 0) return true;
        _examineRunsForSelfTest++;
        GD.Print($"ARC-SCRIPT-1119 examine entity={item} disposition={ran.Disposition} failure={ran.Failure} declined={ran.DeclinedBy?.ToString() ?? "-"}");
        foreach (var entry in AreaScriptMessages.Entries(ran.Messages.Select(message => new AreaPrintedLine(message.Kind, message.Text)))) PostMessage(entry, "script line");
        return ran.DeclinedBy is null;
    }

    private int _examineRunsForSelfTest;

    /// <summary>ARC-UI-108: the texts the item window shown now draws (the name and every non-empty line after it, 0x554F10); 0 when none is shown.</summary>
    internal int ItemWindowTextsForSelfTest => _messageLog.Shown is { Kind: AreaMessageKind.Item, ItemLines: var lines }
        ? 1 + (lines is null ? 0 : new[] { lines.Speed, lines.Second, lines.Third, lines.ThirdRight, lines.Weight, lines.HitPoints }.Count(text => text.Length > 0)) : 0;

    /// <summary>ARC-UI-108: the lines of the item window shown now (null none, or the item's facts were not known).</summary>
    internal AreaItemLines? ItemWindowLinesForSelfTest => _messageLog.Shown is { Kind: AreaMessageKind.Item } entry ? entry.ItemLines : null;

    /// <summary>ARC-UI-108: what the item window reads of the local PC and of the item's own state (null without a hero or the bars' texts).</summary>
    private AreaItemWindowContext? ItemWindowContext(GameSessionOwner? session, string? entity) =>
        session?.QueryItemViewer() is { } viewer && _gameBarContent?.Texts is { } texts ? new(viewer, texts, session, entity) : null;

    /// <summary>The inventory window's still pointer rested on an item (or on none): a new item under it is described (0x573C74); the
    /// window keeps the last description when the pointer rests on no item.</summary>
    private void InventoryPointerStill(string? item)
    {
        if (item is null || _areaSession is not { } session || _gameBarPanel is not { Visible: true }) return;
        _hotkeyDescriptionsForSelfTest++;
        DescribeItem(session, item);
    }
}
