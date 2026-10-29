using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-026: the original's maintain bar on the top strip (docs/world/spell-cast-host.md#the-maintain-bar-arc-magic-026; ledger
// MB1–MB9). The view draws the session's bar (GameSessionOwner.QueryMaintainBar): an empty open slot its SplSlt art, a slot holding a
// maintained spell its icon, a closed slot (at or beyond the hero's intelligence / 4) its plugged art. A press plays 3000 and the
// release over the slot 3001; the release over a slot that shows a run stops that spell through the cast host (0x57C370 → 0x457110).
// The pointer entering such a slot shows the spell's description as the hotkey bar does (0x57C3F0 → 0x5507E0). When the open count
// drops, a slot it closes while showing a run is clicked, so its spell stops (0x57C4B0 → 0x553670).
public partial class TerrainViewerApp
{
    private AreaMaintainBarView? _maintainBarView;
    private (GameSessionOwner Session, GameSessionVersion Version, int Open, int GameArt, int HotkeyArt)? _maintainBarShown;
    // The open count the bar last showed (the original's 0x5CB3D0), per session.
    private (GameSessionOwner Session, int Open)? _maintainBarOpen;
    private int _maintainStopsForSelfTest;
    private string? _maintainNotice;

    /// <summary>Per frame while the bars show: the bar's view on the top strip, a drop of the open count's closing stops, and its look
    /// redrawn when the session, the open count or the art changed.</summary>
    private void SyncMaintainBar()
    {
        if (_gameBarPanel is null || _areaSession is not { } session) return;
        if (_maintainBarView is null)
        {
            _maintainBarView = new AreaMaintainBarView();
            _gameBarPanel.AddPart(AreaBarStrip.Top, _maintainBarView);
            _maintainBarView.SlotPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "maintain slot");
            _maintainBarView.SlotReleased += MaintainSlotReleased;
            _maintainBarView.SlotEntered += MaintainSlotEntered;
            _maintainBarView.SlotLeft += _ => HotkeyDescriptionLeft();
        }
        var open = session.QueryMaintainBarOpenSlots();
        var before = _maintainBarOpen is { } known && ReferenceEquals(known.Session, session) ? known.Open : (int?)null;
        if (before != open)
        {
            var closing = AreaMaintainBarLayout.ClosingRuns(session.QueryMaintainBar(), before, open);
            // The stops need the cast host; without one the count stays as it was and the closing is tried again.
            if (closing.Count == 0 || SpellCasterNow() is not null)
            {
                foreach (var slot in closing) StopMaintainSlot(session, slot, "closed");
                _maintainBarOpen = (session, open);
            }
        }
        var shown = (session, session.Version, open, _gameBarContent?.Art.Count ?? 0, _hotkeyBarArt.Count);
        if (_maintainBarShown == shown) return;
        _maintainBarShown = shown;
        var runs = session.QuerySpellRuns();
        var looks = AreaMaintainBarLayout.Looks(session.QueryMaintainBar(), open, run => runs[run]?.Spell);
        _maintainBarView.Bind(new AreaMaintainBarLook(looks, number => _gameBarPanel.ArtFrames(number) ?? Art(number)));
    }

    private void MaintainSlotReleased(int slot, bool over)
    {
        if (!over || _areaSession is not { } session) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "maintain slot");
        StopMaintainSlot(session, slot, "click");
        RefreshAreaStatus();
    }

    /// <summary>A click on maintain slot <paramref name="slot"/> (0x57C370): a slot showing a run stops its spell through the cast host.</summary>
    private void StopMaintainSlot(GameSessionOwner session, int slot, string why)
    {
        if (MaintainBarRules.Hovered(session.QueryMaintainBar(), slot) is not { } run) return;
        if (SpellCasterNow() is not { } caster) { _maintainNotice = "maintained spell: no cast host on this map"; return; }
        var spell = session.QuerySpellRuns()[run]?.Spell;
        var result = caster.StopMaintainedSpell(slot);
        _maintainStopsForSelfTest++;
        _maintainNotice = null;
        GD.Print($"ARC-MAGIC-026 maintain-stop slot={slot} run={run} spell={spell} why={why} result={result.Disposition} {result.Failure} "
            + $"ended={session.QuerySpellRuns()[run] is null}");
        FlushSpellLog(null);
        _maintainBarShown = null;
    }

    /// <summary>The pointer entered maintain slot <paramref name="slot"/> (0x57C3F0): a slot showing a run describes its spell as a
    /// hotkey spell entry does (0x5507E0). The hovered object of the original's second step (0x43C270) is not built.</summary>
    private void MaintainSlotEntered(int slot)
    {
        if (_areaSession is not { } session || MaintainBarRules.Hovered(session.QueryMaintainBar(), slot) is not { } run
            || session.QuerySpellRuns()[run] is not { } spell) return;
        DescribeHotkey(session, new HotkeyEntry(HotkeyKind.Spell, Index: spell.Spell), dragged: false);
    }

    private string MaintainBarStatus() => _maintainNotice is null ? "" : " | " + _maintainNotice;
}
