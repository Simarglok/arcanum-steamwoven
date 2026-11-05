using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-004: the hero's combat mode in the AREA view, in real time (the viewer has no turn-based setting yet). The
// original Ready/Unready key R toggles it through the session (refused while the hero cannot: unconscious, stunned, dead);
// leaving goes through the exit gate, which the viewer's NPCs never close (no AI combat focus yet). A dialog start and a
// map change leave combat mode first, as the source's dialog and teleport do. Running in combat mode charges one fatigue
// every fifth run step of a move (fatigue-limiting and -draining heroes do not exist yet); a hero out of fatigue walks.
// The status line shows the mode and the hero's fatigue. In combat mode the original's left click attacks a living NPC;
// that click is ARC-COMBAT-005's, so a click still talks here, and the dialog leaves combat mode first. ARC-COMBAT-022: a right
// release in the world leaves combat mode too (WorldRightRelease).
public partial class TerrainViewerApp
{
    private string _combatModeNotice = "R toggles combat mode, a right click leaves it";
    // A right press the world took (a macOS Ctrl+click, closing the spell cursor): its release does nothing more.
    private bool _rightPressTaken;
    // Run steps of the current move in combat mode (the source counts them per move goal).
    private int _combatRunSteps;
    private (GameSessionOwner Session, long Revision, string Text)? _combatModeStatus;

    private bool CombatModeActive => _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    private bool HeroInCombatMode => _areaSession is { } session && session.QueryHero() is not null && session.QueryCombatMode(SessionCombatant.Hero);

    /// <summary>The original Ready/Unready key (R) toggles the hero's combat mode on a fresh press. True when the key
    /// belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteCombatModeKey(WorldKeyDecision decision)
    {
        if (!CombatModeActive || decision.Key != WorldKey.R) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _combatModeNotice = "R ignored: palette, dialog or UI focus owns input"; RefreshAreaStatus(); return true; }
        ToggleCombatMode();
        return true;
    }

    private void ToggleCombatMode()
    {
        var session = _areaSession!;
        if (session.QueryHero() is null) { _combatModeNotice = "combat mode needs a hero (start with --hero)"; RefreshAreaStatus(); return; }
        // ARC-COMBAT-005: an attack animation playing keeps the hero in combat mode (the exit gate's attempt-attack test).
        // ARC-COMBAT-020: with the turn-based setting on, entering starts a round and leaving ends it.
        PlaceHeroForRound();
        var result = session.ToggleHeroCombatMode(session.Version, CombatModeContext(),
            AttackPlaying ? new SessionCombatExit { AttemptingAttack = true } : SessionCombatExit.Clear);
        if (!HeroInCombatMode) CancelAreaAttack("left combat mode");
        if (result.Round is { Disposition: GameSessionDisposition.Changed } && session.QueryCombatRound() is not null) InterruptForRound();
        LogRound(result.Round?.Events ?? []);
        _combatModeNotice = result switch
        {
            { Disposition: GameSessionDisposition.Changed, InCombatMode: true } => session.QueryCombatRound() is { } round
                ? $"entered combat mode: round {round.Turn} began" : _turnBasedSetting ? "entered combat mode (turn-based, no round)" : "entered combat mode (real time)",
            { Disposition: GameSessionDisposition.Changed } => "left combat mode",
            { Disposition: GameSessionDisposition.NoChange } => $"combat mode unchanged: {Refusal(result.Refusal)}",
            _ => $"combat mode refused: {result.Failure}",
        };
        _combatRunSteps = 0;
        GD.Print($"ARC-COMBAT-004 combat-mode disposition={result.Disposition} refusal={result.Refusal} failure={result.Failure} on={result.InCombatMode}");
        RefreshAreaStatus();
    }

    /// <summary>
    /// ARC-COMBAT-022: the game view's mouse right release (the original's 0x54DE50, subtype 3, ledger RR1–RR4), under the world press's
    /// guards and only on the hero's own turn-based subturn: an idle, active hero standing in combat mode toggles it, which leaves
    /// through the exit gate (refused with "cannot leave combat mode when under attack", the original's message 24); out of combat
    /// mode an idle concealed hero stops prowling. A walking, attacking or casting hero keeps its mode. The release never enters
    /// combat mode. The original then interrupts the hero's animations and turns it toward the pointer, which the viewer does not.
    /// An armed targeting (spell, lockpicks, pick pocket, a follower's walk or attack order or its open menu) takes no release.
    /// </summary>
    private void WorldRightRelease()
    {
        if (!CombatModeActive || _areaSession is not { } session || session.QueryHeroLife() is not { } life) return;
        if (!WorldClickInputOpen || AreaTransferPending) return;
        if (_spellTargeting is not null || _lockpickArmed is not null || PickPocketArmed || _pendingOrder is not null
            || _followerPortraits is { MenuShown: true }) return;
        if (session.QueryCombatRound() is { } round && !round.Actor.IsHero) return;
        // The viewer's hero never fidgets (the original starts fidgets for NPCs only, ledger TH9).
        var action = RealTimeCombatRules.RightRelease(new(life.Status.IsActive, _heroKnockdown.Lying, HeroInCombatMode, HeroConcealed,
            Animating: !HeroIdleForRound(), Fidgeting: false));
        GD.Print($"ARC-COMBAT-022 right-release action={action}");
        if (action == RightReleaseAction.ToggleCombatMode) ToggleCombatMode();
        else if (action == RightReleaseAction.LeaveConcealment)
        {
            var result = session.ToggleHeroConcealment(session.Version);
            _prowlNotice = result.Disposition == GameSessionDisposition.Rejected ? $"prowl refused: {result.Failure}" : "prowling off (right click)";
            BindGaitPalette(); RefreshReadyStatus();
        }
    }

    private static string Refusal(SessionCombatModeRefusal refusal) => refusal switch
    {
        SessionCombatModeRefusal.Inactive => "the hero cannot act (unconscious or stunned)",
        SessionCombatModeRefusal.Stunned => "a stunned hero cannot leave it",
        SessionCombatModeRefusal.UnderAttack => "cannot leave combat mode when under attack",
        SessionCombatModeRefusal.HeroDead => "the hero is dead",
        _ => refusal.ToString(),
    };

    /// <summary>Before a dialog starts: false when the hero must stay in combat mode (under attack); a refused deactivation
    /// of a stunned hero lets the dialog go ahead, as the source does.</summary>
    private bool LeaveCombatModeForDialog()
    {
        if (!HeroInCombatMode) return true;
        var session = _areaSession!;
        var result = session.LeaveHeroCombatMode(session.Version, CombatModeContext(), SessionCombatExit.Clear);
        GD.Print($"ARC-COMBAT-004 dialog-leave disposition={result.Disposition} refusal={result.Refusal} on={result.InCombatMode}");
        if (result.Refusal == SessionCombatModeRefusal.UnderAttack) { _combatModeNotice = "no dialog: " + Refusal(result.Refusal); return false; }
        if (result.Disposition == GameSessionDisposition.Changed) _combatModeNotice = "left combat mode to talk";
        return true;
    }

    /// <summary>A new move starts its run counter at 0 (the source counts per move goal).</summary>
    private void ResetCombatRunSteps() => _combatRunSteps = 0;

    /// <summary>The gait a step starts with: a hero out of fatigue walks (the source's step cost), and a concealed hero runs only
    /// as a master of prowling (ARC-SKILLS-008).</summary>
    // ARC-WORLD-087: from encumbrance level 4 the hero walks too (ledger WE9).
    private MovementGait StepGait() => _worldGait == MovementGait.Run && (HeroFatigue() is <= 0 || !StealthAllowsRun() || _areaSession?.QueryHeroCarry() is { CanRun: false })
        ? MovementGait.Walk : _worldGait;

    private int? HeroFatigue() => _areaSession?.QueryHeroLife()?.Status.Fatigue;

    /// <summary>
    /// After an advance: every run step the hero started in combat mode counts, and every fifth charges one fatigue through
    /// the session (<see cref="RealTimeCombatRules.RunStep"/>). A step the hero started out of fatigue walked and counts
    /// nothing; when a charge empties the fatigue, the rest of the move walks.
    /// </summary>
    private void ObserveCombatRunSteps(GridAdvance advance)
    {
        if (!CombatModeActive || !HeroInCombatMode) return;
        var period = RealTimeCombatRules.RunChargePeriod(false, false);
        foreach (var evidence in advance.AreaEvidence)
        {
            if (evidence.Gait != MovementGait.Run || (evidence.Live?.Disposition ?? evidence.Assessment.Disposition) != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
            (_combatRunSteps, var charge) = RealTimeCombatRules.RunStep(_combatRunSteps, period);
            if (!charge) continue;
            var session = _areaSession!;
            var result = session.ChargeCombatRun(session.Version, SessionLifeTarget.Hero);
            GD.Print($"ARC-COMBAT-004 run-charge disposition={result.Disposition} failure={result.Failure} fatigue={HeroFatigue()}");
            if (HeroFatigue() is <= 0) { SetWorldGait(MovementGait.Walk, "out of fatigue", newMove: false); _combatModeNotice = "out of fatigue: walking"; }
        }
    }

    /// <summary>The hero leaves combat mode with a map change (the source's teleport).</summary>
    private void LeaveCombatModeForMapChange()
    {
        if (!HeroInCombatMode) return;
        var session = _areaSession!;
        var result = session.SetCombatMode(session.Version, SessionCombatant.Hero, false, CombatModeContext());
        GD.Print($"ARC-COMBAT-004 map-change-leave disposition={result.Disposition} refusal={result.Refusal}");
        if (result.Disposition == GameSessionDisposition.Changed) _combatModeNotice = "left combat mode with the map change";
    }

    private string CombatModeStatus()
    {
        if (!CombatModeActive) return string.Empty;
        var session = _areaSession!;
        if (_combatModeStatus is not { } cached || !ReferenceEquals(cached.Session, session) || cached.Revision != session.Version.Revision)
        {
            var text = session.QueryHero() is null ? "combat mode: needs a hero"
                : $"combat mode: {(session.QueryCombatMode(SessionCombatant.Hero) ? session.QueryCombatRound() is null ? "ON (real time)" : "ON (turn-based)" : "off")}, fatigue {HeroFatigue()}/{session.QueryHeroLife()?.Status.MaxFatigue}";
            _combatModeStatus = cached = (session, session.Version.Revision, text);
        }
        return $" | {cached.Text} ({_combatModeNotice})";
    }
}
