using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-057: the world's hovered object as the original shows it while the interface bars are drawn (docs/rendering/terrain-viewer.md
// "Hovered object window and highlight"; ledger OH1–OH9). The object is described in the message window (0x553BE0): an item's window
// (its type icon and its name in its colour), a portal's, container's or scenery's image and name, a critter's image or portrait, name
// and bars, a trap's name as a hover text; in a pane mode only the name, in the pane's text area. Leaving it shows the viewed entry again
// (the original blanks the window, ML7). The floating tooltip at the pointer stays for the viewer without the bars.
public partial class TerrainViewerApp
{
    private AreaObjectKey? _hoverWindowKey;
    private AreaMessageEntry? _hoverWindowEntry;
    private AreaWindowLifeBars? _hoverWindowBars;

    /// <summary>The object window the message window shows now for the hovered object (null none).</summary>
    internal AreaMessageEntry? HoverWindowForSelfTest => HoverWindowShown ? _hoverWindowEntry : null;
    internal (int Health, string HealthText, int Fatigue, string FatigueText, bool Poisoned)? HoverWindowBarsForSelfTest =>
        _hoverWindowBars is { } bars && IsInstanceValid(bars) ? bars.ValuesForSelfTest : null;

    private bool HoverWindowShown => _hoverWindowEntry is { } entry && ReferenceEquals(_messageLog.Shown, entry) && !_messageLog.PaneMode;

    /// <summary>Describes <paramref name="key"/> (null: no object) in the message window when it is another object than the last one; for
    /// the same object still shown, a critter's bars follow its life (the session's revision is in the hover's stamp).</summary>
    private void SyncHoverWindow(AreaObjectKey? key)
    {
        if (_gameBarPanel is not { Visible: true } panel) { ResetHoverWindow(); return; }
        if (key == _hoverWindowKey)
        {
            if (key is { } same && HoverWindowShown && _hoverWindowBars is { } shown && IsInstanceValid(shown)) shown.Show(HoverLife(same));
            return;
        }
        _hoverWindowKey = key;
        LeaveHoverWindow();
        if (key is not { } hit || _hoverIndex is not { } index || !index.Contains(hit)) return;
        var row = index.Row(hit);
        var kind = AreaHoverWindow.Kind(row.Type);
        var name = index.Name(hit);
        if (kind == AreaHoverWindowKind.None || string.IsNullOrEmpty(name)) return;
        if (_messageLog.PaneMode)
        {
            // 0x553BE0 in another bar mode: only the name, in the pane's text area (HK21).
            if (_messageLog.ShowPaneName(MessagePaneMode, name, item: kind == AreaHoverWindowKind.Item)) panel.MessageLog.Render();
            return;
        }
        if (kind == AreaHoverWindowKind.Trap)
        {
            // 0x553BE0 posts a trap's name as a hover text (type 6: shown only after the quiet time, ML2).
            PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, name), "hover");
            return;
        }
        Texture2D? portrait = null;
        AreaMessageEntry? entry;
        if (kind == AreaHoverWindowKind.Item)
        {
            var identified = InventoryActive && AreaInventoryTargetOf(hit) is { Entity: { } entity } && _areaSession?.QueryItemIdentified(entity) == true;
            var described = _hotkeyItemIcons?.Describe(default, row, identified, _hoverNames?.Catalog, name) ?? new AreaItemDescription(null, name, AreaMessageColour.White);
            entry = AreaHoverDescriptions.ItemWindow(described);
        }
        else if (kind == AreaHoverWindowKind.Critter)
        {
            portrait = HoverPortrait(hit);
            var image = portrait is not null ? null : CombatFieldReader.Read(row).Fields is { Critter: { } critter } fields
                ? AreaLootArtLayout.HolderImage(fields.Object.Type, critter.Flags, critter.BaseStats.Count > 27 ? critter.BaseStats[27] : -1, 0) : null;
            entry = AreaHoverWindow.Window(kind, name, image);
        }
        else entry = AreaHoverWindow.Window(kind, name, AreaHoverWindow.Image(row));
        if (entry is null || !_messageLog.ShowItem(entry)) return;
        _hoverWindowEntry = entry;
        panel.ShowHoverPortrait(portrait);
        if (kind == AreaHoverWindowKind.Critter && HoverLife(hit) is { } life)
        {
            if (_hoverWindowBars is null || !IsInstanceValid(_hoverWindowBars)) { _hoverWindowBars = new AreaWindowLifeBars(); panel.AddPart(AreaBarStrip.Bottom, _hoverWindowBars); }
            _hoverWindowBars.Show(life);
        }
        panel.MessageLog.Render();
    }

    /// <summary>Per frame: the portrait and the bars belong to the object window and go when the window shows something else (a new entry,
    /// a pane, a bar button's hover).</summary>
    private void TrackHoverWindow()
    {
        if (_hoverWindowEntry is null || HoverWindowShown) return;
        _hoverWindowEntry = null;
        _gameBarPanel?.ShowHoverPortrait(null);
        if (_hoverWindowBars is { } bars && IsInstanceValid(bars)) bars.Show(null);
    }

    /// <summary>The pointer left the object: its window goes and the message window shows the viewed entry again (ML7).</summary>
    private void LeaveHoverWindow()
    {
        if (HoverWindowShown) { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
        TrackHoverWindow();
    }

    /// <summary>The bars were hidden: the window is forgotten (the bars' hide clears the message window's hover).</summary>
    private void ResetHoverWindow()
    {
        _hoverWindowKey = null;
        if (_hoverWindowEntry is null) return;
        _hoverWindowEntry = null;
        if (_hoverWindowBars is { } bars && IsInstanceValid(bars)) bars.Show(null);
        _gameBarPanel?.ShowHoverPortrait(null);
    }
}
