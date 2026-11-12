using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1114 (slice 2 of docs/research/combat-hud.md; ledger CH2-CH6, CH13): the turn-based round's action-point lamps and turn-order
// list. The lamps show the hero's points left after the previewed action, the action's cost and the part the points do not cover; the
// pending cost is the last action-point check's (the hover preview of 0x557370 and the hero's own charges set it; a charge does not
// clear it, CH4). On another participant's subturn three lights run along the lamps. The list of the round's participants is a Steamwoven
// addition, hidden in the Original rule mode. Plain Godot controls; shown only while a round runs.
public partial class TerrainViewerApp
{
    private AreaCombatHud? _combatHud;
    // The pending cost (the last action-point check's) and the session it belongs to; a new session or the end of a round resets it.
    private int _apPending;
    private WeakReference<GameSessionOwner>? _apPendingSession;
    private (GameSessionOwner? Session, int Turn, int Current, int Points, int Participants, int Pending, int Cue, bool Shown, bool InBars, bool List)? _combatHudStamp;
    private ulong _cueStartMs;
    // Automated runs draw the HUD only when their smoke asks (or the interface bars are drawn), as the gauges (ARC-UI-020).
    private bool _combatHudForSelfTest;

    /// <summary>Per frame: the HUD while a round runs, else hidden. Cheap when nothing it reads has changed.</summary>
    private void RefreshCombatHud()
    {
        var session = _areaSession;
        var round = InTurnBasedRound ? session?.QueryCombatRound() : null;
        var sameSession = session is not null && _apPendingSession is { } remembered && remembered.TryGetTarget(out var known) && ReferenceEquals(known, session);
        if (round is null || !sameSession)
        {
            // CH4: only a load resets the pending cost in the original; the end of a round and a replaced session start a round afresh.
            _apPending = 0;
            _apPendingSession = session is null ? null : new(session);
            _cueStartMs = Time.GetTicksMsec();
        }
        var inBars = _gameBarPanel is { Visible: true };
        var shown = round is not null && _readyArea is not null && session!.QueryHero() is not null && _readyMode == TerrainViewerMode.Area
            && (inBars || !WorldModal) && (!ViewerAutomatedStorage.Automated || _combatHudForSelfTest || _gameBarForSelfTest);
        var heroTurn = round?.Actor.IsHero == true;
        var cue = shown && !heroTurn ? ActionPointRules.CueIndex((long)(Time.GetTicksMsec() - _cueStartMs)) : 0;
        var list = shown && !session!.Rules.Interface.TurnOrderListHidden;
        var stamp = (session, round?.Turn ?? 0, round?.Current ?? 0, round?.ActionPoints ?? 0, round?.Participants.Count ?? 0, _apPending, cue, shown, inBars, list);
        if (_combatHudStamp == stamp) return;
        _combatHudStamp = stamp;
        if (!shown) { _combatHud?.Show(null, null); return; }
        if (_combatHud is null) { _combatHud = new AreaCombatHud(); AddChild(_combatHud); }
        IReadOnlyList<(string, bool)>? rows = null; var hidden = 0;
        if (list)
        {
            var window = TurnOrderRules.Window(round!.Participants.Count, round.Current);
            rows = window.Rows.Select(r => (ParticipantName(round.Participants[r.Index]), r.Current)).ToArray();
            hidden = window.Hidden;
        }
        // CH4: the hero's subturn shows its points (A) against the pending cost (P); another participant's shows the running lights.
        var lamps = heroTurn ? ActionPointRules.Lamps(round!.ActionPoints, _apPending) : ActionPointRules.CueLampStates(cue);
        _combatHud.Show(new AreaCombatHudView(lamps, heroTurn, round!.Turn, rows, hidden), inBars ? _gameBarPanel : null);
    }

    /// <summary>The round list's name of a participant: the hero, an NPC as the hover names it (its description), else its map number.</summary>
    private string ParticipantName(SessionCombatant who)
    {
        if (who.IsHero) return "Hero";
        var entity = who.Entity!;
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row)
            && _hoverIndex is { } index && index.Contains(row.Key) && index.Name(row.Key) is { Length: > 0 } name) return name;
        return AiName(entity);
    }

    /// <summary>
    /// The pending cost the action under the pointer previews (<see cref="AreaActionPointPreview"/>, the original's hover check):
    /// stored while it is the hero's subturn of a round; an action that runs no check leaves the pending cost as it was.
    /// </summary>
    private void PreviewHoverCost(AreaHoverAction action, AreaObjectKey? key, Vector2? screen)
    {
        if (!InTurnBasedRound || _areaSession is not { } session || session.QueryCombatRound() is not { Actor.IsHero: true }
            || _worldActor.Session is not AreaGridSession grid) return;
        var distance = 0;
        if (key is { } hit && _hoverIndex is { } index && index.Contains(hit) && index.Row(hit) is { WorldX: not null, WorldY: not null } row)
        {
            var actor = grid.Chart.ToWorld(grid.State.CommittedCell);
            distance = (int)Math.Min(Chebyshev(row, (actor.X, actor.Y)), TurnBasedRules.MaxPathSteps);
        }
        else if (action == AreaHoverAction.Walk && screen is { } at && _canvas is not null)
        {
            var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * at;
            if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is { } cell)
            {
                var from = grid.State.CommittedCell;
                distance = (int)Math.Min(Math.Max(Math.Abs((long)cell.X - from.X), Math.Abs((long)cell.Y - from.Y)), TurnBasedRules.MaxPathSteps);
            }
        }
        var hero = session.QueryHeroAttack() is { Supported: true } attack ? attack : null;
        var cost = AreaActionPointPreview.Cost(action, distance, hero is null ? 0 : TurnBasedRules.AttackCost(hero.WeaponSpeed), hero?.Range ?? 0, _alwaysRunOption);
        if (cost is { } value) _apPending = value;
    }

    internal AreaCombatHud? CombatHudForSelfTest => _combatHud;
    internal int ActionPointPendingForSelfTest => _apPending;
}
