using Arcanum.Session;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-057: the world's hovered object as the original shows it while the interface bars are drawn (docs/rendering/terrain-viewer.md
// "Hovered object window and highlight"; ledger OH1–OH9). The object is described in the message window (0x553BE0): an item's window
// (its type icon and its name in its colour), a portal's, container's or scenery's image and name, a critter's image or portrait, name
// and bars, a trap's name as a hover text; in a pane mode only the name, in the pane's text area. ARC-UI-107: a portal's, container's and
// scenery's fourth line, read when the pointer enters the object (the original draws it once, then keeps it): the lock state left and the
// hit points right, both red. Leaving it shows the viewed entry again
// (the original blanks the window, ML7). The floating tooltip at the pointer stays for the viewer without the bars.
public partial class TerrainViewerApp
{
    private AreaObjectKey? _hoverWindowKey;
    private AreaMessageEntry? _hoverWindowEntry;
    private AreaWindowLifeBars? _hoverWindowBars;
    // ARC-UI-109 (OH7): the shown critter window is a dead critter's, which has no bars (0x553F70 returns after its dead line).
    private bool _hoverWindowDead;

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
            if (key is { } same && HoverWindowShown && _hoverWindowBars is { } shown && IsInstanceValid(shown)) shown.Show(_hoverWindowDead ? null : HoverLife(same));
            return;
        }
        _hoverWindowKey = key;
        LeaveHoverWindow();
        if (key is not { } hit || _hoverIndex is not { } index || !index.Contains(hit)) return;
        var row = index.Row(hit);
        // ARC-DEV-326 (B18, OH1): the pointer entering an object describes it for the hero (0x57CCF0 → 0x57CD60), whose first step is the object's
        // examine script; one that answers skip default leaves the window without a description (the highlight stays: it is drawn after, 0x43C270).
        if (!ExamineAllows(row)) return;
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
            // ARC-UI-109 (OH7, OH11): the second line, and the alignment image of a hero who detects alignment (then no portrait).
            var critterLife = HoverLife(hit);
            var facts = HoverCritterFacts(hit, row, critterLife);
            portrait = facts is { ViewerDetectsAlignment: true } ? null : HoverPortrait(hit);
            var image = portrait is not null ? null : CombatFieldReader.Read(row).Fields is { Critter: { } critter } fields
                ? AreaLootArtLayout.HolderImage(fields.Object.Type, critter.Flags, critter.BaseStats.Count > 27 ? critter.BaseStats[27] : -1, 0) : null;
            entry = facts is { } critterFacts ? AreaHoverWindow.CritterWindow(name, image, critterFacts, _gameBarContent?.Texts) : AreaHoverWindow.Window(kind, name, image);
            _hoverWindowDead = facts is { Dead: true };
        }
        else
        {
            // ARC-UI-107 (OH6, OH10): a portal's and container's lock state and the hit points on the fourth line, read from the session now.
            var condition = _areaSession is { } session && ObjectTargets()?.Target(session, hit, AreaTrapped) is { } target ? AreaHoverWindow.Condition(session, target) : null;
            entry = AreaHoverWindow.Window(kind, name, AreaHoverWindow.Image(row), condition, _gameBarContent?.Texts);
        }
        // WM127: the scenery window ends by marking the town map when the scenery's flag asks for it.
        if (kind == AreaHoverWindowKind.Scenery) MarkTownMapByScenery(row, name);
        if (entry is null || !_messageLog.ShowItem(entry)) return;
        _hoverWindowEntry = entry;
        panel.ShowHoverPortrait(portrait);
        if (kind == AreaHoverWindowKind.Critter && !_hoverWindowDead && HoverLife(hit) is { } life)
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

    /// <summary>
    /// ARC-UI-109 (OH7, OH11): the facts of a hovered critter's second line, read when the pointer enters it (the original draws the window
    /// once): its type, whether it is dead (hit points 0 or less, <paramref name="life"/>), its level (a follower's saved sheet first, else
    /// the stored stat 17), its alignment (stat 19), its effective reaction toward the hero and how well the hero hides from it (the AI host's
    /// sight and hearing checks), and the hero's own state (concealed; spell flag 0x80, detecting alignment). Null for an object with no
    /// critter fields.
    /// </summary>
    private AreaHoverCritter? HoverCritterFacts(AreaObjectKey key, AreaObjectRecord row, (AreaLife Life, bool PcSide)? life)
    {
        if (_areaSession is not { } session || CombatFieldReader.Read(row).Fields is not { Critter: { } critter } fields) return null;
        var stats = critter.BaseStats;
        var entity = HoverEntity(key);
        var level = entity is not null && session.QueryNpcAdvancement(entity) is { } saved ? saved.Sheet.Level : stats.Count > HoverLevelStat ? stats[HoverLevelStat] : 0;
        var alignment = stats.Count > HoverAlignmentStat ? stats[HoverAlignmentStat] : 0;
        var type = fields.Object.Type;
        var sneaks = session.IsConcealed(SessionCombatant.Hero);
        var npc = type == AreaHoverWindow.NpcType && entity is not null;
        int? reaction = npc ? _aiHost?.StandardReaction(entity!) : null;
        int? excess = sneaks && npc && _aiHost is { } ai ? ai.SneakExcess(entity!) : null;
        var detects = SpellCasterNow()?.PlayerSpellFlags is { } flags && (flags & HoverDetectingAlignment) != 0;
        return new(type, life is { Life.Health: <= 0 }, level, alignment, reaction, sneaks, excess, detects);
    }

    private const int HoverLevelStat = 17, HoverAlignmentStat = 19, HoverDetectingAlignment = 0x80;

    /// <summary>The identity of a hovered critter in the session (a follower's, else an NPC's through the combat host), or null.</summary>
    private string? HoverEntity(AreaObjectKey key)
    {
        if (_areaSession is not { } session || _combatHost is not { } host || _readyMode != TerrainViewerMode.Area) return null;
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == key) is { } companion) return companion.Entity;
        return host.Target(session, key) is { Build.Combat: not null } target ? target.Entity : null;
    }
}
