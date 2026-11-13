using Arcanum.Session;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Simulation.Combat;
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
        if (key is not { } hit) return;
        DescribeObject(hit, panel, pointer: true);
    }

    /// <summary>
    /// The object window of <paramref name="hit"/> (0x57CCF0 → 0x553BE0). <paramref name="panel"/> is null when the viewer draws no bars (the window is then only held in the message log); <paramref name="pointer"/> is the pointer entering the object (the bars' panel is then set): the window is
    /// remembered for the redraws and goes when the pointer leaves. ARC-UI-1411 (ledger XR4): an Identify that names a portal or scenery draws the same
    /// window with no pointer on the object: it stays until the message window shows something else (as the item description of Identify does); a
    /// critter's portrait and bars belong to the pointer's window only. Returns whether the window was drawn.
    /// </summary>
    private bool DescribeObject(AreaObjectKey hit, AreaGameBarPanel? panel, bool pointer)
    {
        if (_hoverIndex is not { } index || !index.Contains(hit)) return false;
        var row = index.Row(hit);
        // ARC-DEV-326 (B18, OH1): the pointer entering an object describes it for the hero (0x57CCF0 → 0x57CD60), whose first step is the object's
        // examine script; one that answers skip default leaves the window without a description (the highlight stays: it is drawn after, 0x43C270).
        // ARC-UI-1281: a follower the pointer rests on by his portrait was examined when it entered (once); the answer is kept for the redraws.
        if (!(pointer && hit == _portraitHoverKey && _portraitAllows is { } examined ? examined : ExamineAllows(row))) return false;
        var kind = AreaHoverWindow.Kind(row.Type);
        var name = index.Name(hit);
        if (kind == AreaHoverWindowKind.None || string.IsNullOrEmpty(name)) return false;
        if (_messageLog.PaneMode)
        {
            // 0x553BE0 in another bar mode: only the name, in the pane's text area (HK21).
            if (_messageLog.ShowPaneName(MessagePaneMode, name, item: kind == AreaHoverWindowKind.Item)) panel?.MessageLog.Render();
            return true;
        }
        if (kind == AreaHoverWindowKind.Trap)
        {
            // 0x553BE0 posts a trap's name as a hover text (type 6: shown only after the quiet time, ML2).
            PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, name), "hover");
            return true;
        }
        Texture2D? portrait = null;
        AreaMessageEntry? entry;
        if (kind == AreaHoverWindowKind.Item)
        {
            var entity = InventoryActive ? AreaInventoryTargetOf(hit)?.Entity : null;
            var identified = entity is not null && _areaSession?.QueryItemIdentified(entity) == true;
            var described = _hotkeyItemIcons?.Describe(default, row, identified, _hoverNames?.Catalog, name, ItemWindowContext(_areaSession, entity))
                ?? new AreaItemDescription(null, name, AreaMessageColour.White);
            entry = AreaHoverDescriptions.ItemWindow(described);
        }
        else if (kind == AreaHoverWindowKind.Critter)
        {
            // ARC-UI-109 (OH7, OH11): the second line, and the alignment image of a hero who detects alignment (then no portrait).
            var critterLife = HoverLife(hit);
            var facts = HoverCritterFacts(hit, row, critterLife);
            portrait = facts is { ViewerDetectsAlignment: true } ? null : HoverPortrait(hit);
            // ARC-MAGIC-1551: the race of the image is the stored stat 27 with the entries a TraitIdx spell wrote over it.
            var image = portrait is not null ? null : AreaWrittenCritterFields.Read(_areaSession, row) is { Critter: { } critter } fields
                ? AreaLootArtLayout.HolderImage(fields.Object.Type, critter.Flags, AreaHoverWindow.CritterStats(fields, null).Race, 0) : null;
            entry = facts is { } critterFacts ? AreaHoverWindow.CritterWindow(name, image, critterFacts, _gameBarContent?.Texts) : AreaHoverWindow.Window(kind, name, image);
            _hoverWindowDead = facts is { Dead: true };
            // ARC-UI-124 (OH7, OH11, OH19): with the hero in combat mode the combat panel takes the image's place on a living critter's window.
            if (entry is not null && facts is { Dead: false } && HoverCombatPanel(hit, row) is { } combatPanel)
            {
                entry = entry with { CombatPanel = combatPanel };
                portrait = null;
            }
        }
        else
        {
            // ARC-UI-107 (OH6, OH10): a portal's and container's lock state and the hit points on the fourth line, read from the session now.
            var condition = _areaSession is { } session && ObjectTargets()?.Target(session, hit, AreaTrapped) is { } target ? AreaHoverWindow.Condition(session, target) : null;
            entry = AreaHoverWindow.Window(kind, name, AreaHoverWindow.Image(row), condition, _gameBarContent?.Texts);
        }
        // WM127: the scenery window ends by marking the town map when the scenery's flag asks for it.
        if (kind == AreaHoverWindowKind.Scenery) MarkTownMapByScenery(row, name);
        if (entry is null || !_messageLog.ShowItem(entry)) return false;
        if (!pointer) { panel?.ShowHoverPortrait(null); panel?.MessageLog.Render(); return true; }
        _hoverWindowEntry = entry;
        panel!.ShowHoverPortrait(portrait);
        if (kind == AreaHoverWindowKind.Critter && !_hoverWindowDead && HoverLife(hit) is { } life)
        {
            if (_hoverWindowBars is null || !IsInstanceValid(_hoverWindowBars)) { _hoverWindowBars = new AreaWindowLifeBars(); panel.AddPart(AreaBarStrip.Bottom, _hoverWindowBars); }
            _hoverWindowBars.Show(life);
        }
        panel.MessageLog.Render();
        return true;
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
        // ARC-MAGIC-1551: the stored level and alignment with the entries a TraitIdx spell wrote over them.
        if (_areaSession is not { } session || AreaWrittenCritterFields.Read(session, row) is not { Critter: not null } fields) return null;
        var entity = HoverEntity(key);
        var (level, alignment, _) = AreaHoverWindow.CritterStats(fields, entity is not null && session.QueryNpcAdvancement(entity) is { } saved ? saved.Sheet.Level : null);
        var type = fields.Object.Type;
        var sneaks = session.IsConcealed(SessionCombatant.Hero);
        var npc = type == AreaHoverWindow.NpcType && entity is not null;
        int? reaction = npc ? _aiHost?.StandardReaction(entity!) : null;
        int? excess = sneaks && npc && _aiHost is { } ai ? ai.SneakExcess(entity!) : null;
        var detects = SpellCasterNow()?.PlayerSpellFlags is { } flags && (flags & HoverDetectingAlignment) != 0;
        return new(type, life is { Life.Health: <= 0 }, level, alignment, reaction, sneaks, excess, detects);
    }

    private const int HoverDetectingAlignment = 0x80;

    /// <summary>
    /// ARC-UI-124 (OH19): the combat panel (0x554830) of a hovered critter, read when the pointer enters it, while the hero is in combat
    /// mode: the hit chance of the hero's wielded weapon against it from where the hero stands (the tile distance, and for a weapon on
    /// the ranged path the line of fire the attack reads, ARC-COMBAT-015) and the terms that made it harder. Null outside combat mode, for
    /// a critter without combat values (a follower carried onto a map whose home map is not held has none, so it gets no panel) or a hero that cannot attack.
    /// </summary>
    private AreaCombatPanel? HoverCombatPanel(AreaObjectKey key, AreaObjectRecord row)
    {
        if (!HeroInCombatMode || _areaSession is not { } session || _combatHost is not { } host || _worldActor.Session is not AreaGridSession grid) return null;
        if (host.Target(session, key) is not { Build.Combat: { } combat }) return null;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var distance = Chebyshev(row, (cell.X, cell.Y));
        var line = RangedPath(session.QueryHeroAttack()) ? ShotLine(TraceLineOfFire(new(checked((int)cell.X), checked((int)cell.Y)), row), row) : null;
        if (session.QueryHeroAttackChance(combat, (int)Math.Min(distance, AttackResolver.MaxMagnitude), line, AreaRowLightLevel(row)) is not { } chance) return null;
        var label = (_gameBarContent?.Texts ?? AreaGameBarTexts.Project("hover")).Text(AreaCombatPanelLayout.ToHitMessage);
        return AreaCombatPanelLayout.Panel(chance, label);
    }

    /// <summary>The identity of a hovered critter in the session (a follower's, else an NPC's through the combat host), or null.</summary>
    private string? HoverEntity(AreaObjectKey key)
    {
        if (_areaSession is not { } session || _combatHost is not { } host || _readyMode != TerrainViewerMode.Area) return null;
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == key) is { } companion) return companion.Entity;
        return host.Target(session, key) is { Build.Combat: not null } target ? target.Entity : null;
    }
}
