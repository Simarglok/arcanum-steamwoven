using Arcanum.Data.Area;
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
// addition, hidden in the Original rule mode. Shown only while a round runs. ARC-UI-1888: the lamp panel is the original's art (panel 294, lamps
// 295, 296 and 315, end-turn button 297, CH1, CH2, CT5) when the bars' art read; the turn-order list stays a plain control (no original art).
public partial class TerrainViewerApp
{
    private AreaCombatHud? _combatHud;
    // The pending cost (the last action-point check's) and the session it belongs to; a new session or the end of a round resets it.
    private int _apPending;
    private WeakReference<GameSessionOwner>? _apPendingSession;
    // The subturns the session had started when the pending cost was last kept: a subturn start runs the check with cost 0 (CH4, 0x4B75D0).
    private long _apSeenSubturns = -1;
    private (GameSessionOwner? Session, int Turn, int Current, int Points, int Participants, int Pending, int Cue, bool Shown, bool InBars, string? List, Texture2D? Art, bool ButtonActive)? _combatHudStamp;
    // The running lights' clock: it advances only while another participant acts and starts at 0 with the round (CH5).
    private long _cueMs;
    private ulong _cueTick;
    // Automated runs draw the HUD only when their smoke asks (or the interface bars are drawn), as the gauges (ARC-UI-020).
    private bool _combatHudForSelfTest;
    // ARC-UI-1888: synthetic art for the smoke (an interface art number to its frames), in place of the bars' art that an automated run does not read.
    private Func<int, Texture2D[]?>? _combatHudArtForSelfTest;

    /// <summary>A subturn start clears the pending cost (the original's subturn start runs the action-point check with 0, CH4); called before any
    /// read or write of the pending cost of <paramref name="round"/>.</summary>
    private void NotePendingSubturn(SessionCombatRound round)
    {
        if (round.SubturnStarts == _apSeenSubturns) return;
        _apSeenSubturns = round.SubturnStarts;
        _apPending = 0;
    }

    /// <summary>Per frame: the HUD while a round runs, else hidden. Cheap when nothing it reads has changed.</summary>
    private void RefreshCombatHud()
    {
        var session = _areaSession;
        var round = InTurnBasedRound ? session?.QueryCombatRound() : null;
        var sameSession = session is not null && _apPendingSession is { } remembered && remembered.TryGetTarget(out var known) && ReferenceEquals(known, session);
        var now = Time.GetTicksMsec();
        if (round is null || !sameSession)
        {
            // CH4: the end of a round and a replaced session start afresh (the original's load resets the pending cost, its build the lights).
            _apPending = 0; _apSeenSubturns = -1; _cueMs = 0; _cueTick = now;
            _apPendingSession = session is null ? null : new(session);
        }
        if (round is not null) NotePendingSubturn(round);
        var inBars = _gameBarPanel is { Visible: true };
        var shown = round is not null && _readyArea is not null && session!.QueryHero() is not null && _readyMode == TerrainViewerMode.Area
            && (inBars || !WorldModal) && (!ViewerAutomatedStorage.Automated || _combatHudForSelfTest || _gameBarForSelfTest);
        var heroTurn = round?.Actor.IsHero == true;
        // The lights step only on another participant's subturn; a frame's step is capped so a stalled frame does not skip round the lamps.
        if (shown && !heroTurn) _cueMs += (long)Math.Min(now - _cueTick, 250);
        _cueTick = now;
        var cue = shown && !heroTurn ? ActionPointRules.CueIndex(_cueMs) : 0;
        // The list is a Steamwoven addition and sits over the world: it hides behind any window (the lamps stay with the bars).
        var list = shown && !WorldModal && !session!.Rules.Interface.TurnOrderListHidden;
        IReadOnlyList<(string, bool)>? rows = null; var hidden = 0;
        if (list)
        {
            var window = TurnOrderRules.Window(round!.Participants.Count, round.Current);
            rows = window.Rows.Select(r => (ParticipantName(round.Participants[r.Index]), r.Current)).ToArray();
            hidden = window.Hidden;
        }
        var listKey = rows is null ? null : string.Join("\n", rows.Select(r => (r.Item2 ? ">" : " ") + r.Item1)) + "\n+" + hidden;
        Func<int, Texture2D[]?> artOf = _combatHudArtForSelfTest ?? (number => _gameBarPanel?.ArtFrames(number));
        var art = AreaCombatLampLayout.ArtNumbers.All(number => artOf(number) is { Length: > 0 }) ? artOf(AreaCombatLampLayout.PanelArt)![0] : null;
        // A window over the world owns the pointer: the end-turn button (above every window's layer) takes none then (the release is refused anyway, CT5).
        var buttonActive = !WorldModal;
        _combatHud?.SetEndTurnActive(buttonActive);
        var stamp = (session, round?.Turn ?? 0, round?.Current ?? 0, round?.ActionPoints ?? 0, round?.Participants.Count ?? 0, _apPending, cue, shown, inBars, listKey, art, buttonActive);
        if (_combatHudStamp == stamp) return;
        _combatHudStamp = stamp;
        if (!shown) { _combatHud?.Show(null, null); return; }
        if (_combatHud is null)
        {
            _combatHud = new AreaCombatHud();
            AddChild(_combatHud);
            _combatHud.EndTurnPressed += () => PlayBarSound(AreaGameBarLayout.PressSound, "end turn");
            _combatHud.EndTurnReleased += over => { if (!over) return; PlayBarSound(AreaGameBarLayout.ReleaseSound, "end turn"); EndTurnFromButton(); };
        }
        _combatHud.SetArt(_combatHudArtForSelfTest ?? (number => _gameBarPanel?.ArtFrames(number)));
        // CH4: the hero's subturn shows its points (A) against the pending cost (P); another participant's shows the running lights.
        var lamps = heroTurn ? ActionPointRules.Lamps(round!.ActionPoints, _apPending) : ActionPointRules.CueLampStates(cue);
        _combatHud.Show(new AreaCombatHudView(lamps, heroTurn, round!.Turn, rows, hidden), inBars ? _gameBarPanel : null);
    }

    /// <summary>The end-turn button's release (ARC-UI-1888, CT5): the same end of the hero's subturn as E, which a window over the world refuses.</summary>
    private void EndTurnFromButton()
    {
        if (!CombatModeActive || WorldModal) { _turnBasedNotice = "end turn ignored: a window or dialog owns input"; RefreshAreaStatus(); return; }
        EndHeroSubturn();
    }

    /// <summary>Presses and releases the end-turn button as the pointer would (the smoke); false when the button is not drawn.</summary>
    internal bool EndTurnClickForSelfTest()
    {
        if (_combatHud is not { EndTurnShownForSelfTest: true } hud) return false;
        hud.EndTurnClickForSelfTest();
        return true;
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
        if (!InTurnBasedRound || _areaSession is not { } session || session.QueryCombatRound() is not { Actor.IsHero: true } round
            || _worldActor.Session is not AreaGridSession grid) return;
        NotePendingSubturn(round);
        var distance = 0;
        // A walk goes to the picked cell, even when the pointer is over an object that offers no action of its own.
        if (action != AreaHoverAction.Walk && key is { } hit && _hoverIndex is { } index && index.Contains(hit) && index.Row(hit) is { WorldX: not null, WorldY: not null } row)
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
