using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-021: the AREA hero (exterior, cave and map mode; real time and turn-based) falls when the session makes it unconscious
// (fatigue at or below 0, 0x45D800) and gets up when the recovery brings it round, as the original (ledger UC1–UC4; AreaHeroKnockdown).
// Going down stops the walk, the attack, the approaches and the spell's target cursor; while the hero lies (falling, down, getting up)
// world presses and the walk keys and stick do nothing (the original's world press handler 0x54ED30 returns at once for a PC that is
// not active, 0x45D790). In a turn-based round the hero gets up only on its own subturn, paying the get-up's 5 action points (A3, CT3).
public partial class TerrainViewerApp
{
    private readonly AreaHeroKnockdown _heroKnockdown = new();
    // The session's identity and the map generation the knockdown follows (no reference to the result, which a reload must free).
    private (Arcanum.Session.GameSessionOwnerIdentity? Session, long? Generation, ActorDiagnosticProfile? Profile) _heroKnockdownFor;
    private long? _heroKnockdownMs;
    private int _heroKnockdownOctant;
    // ARC-DEV-189 (MIG13 S13c): the hero's knock-down goal (62) forces the fall while it runs: a knock-down result leaves the hero conscious, so its life state alone does not show it.
    private bool _heroForcedDown;

    /// <summary>The knock-down goal's step forces the fall (true) or releases it (false).</summary>
    internal void HeroForcedDown(bool on) => _heroForcedDown = on;

    /// <summary>The fall has played to its last frame and the hero lies.</summary>
    internal bool HeroFellDownNow => _heroKnockdown.Stage == AreaKnockdownStage.Down;

    /// <summary>The hero stands on its feet: nothing falls, lies or gets up.</summary>
    internal bool HeroStandsUpNow => !_heroKnockdown.Lying;

    /// <summary>The hero lies or is down in the session: it takes no world command.</summary>
    private bool HeroLyingDown => _heroKnockdown.Lying || _areaSession?.QueryHeroLife()?.Status is { IsDead: true } or { IsUnconscious: true };

    /// <summary>Per world frame, before the hero's input: follows the session hero's consciousness and moves the fall or the get-up on
    /// by the world clock.</summary>
    private void ProcessHeroKnockdown()
    {
        if (_readyArea is not { } area || _readyMode != TerrainViewerMode.Area || _areaSession is not { } session
            || _worldActor.Session is not AreaGridSession grid || session.QueryHeroLife()?.Status is not { } status)
        {
            // No hero drawn on an AREA map: nothing lies, and the next one is rebased.
            if (_heroKnockdown.Lying) _heroKnockdown.Rebase(false, null);
            if (_worldActor.DownPose is not null) { _worldActor.DownPose = null; PublishWorldSelection(); }
            _heroKnockdownFor = default;
            return;
        }
        var profile = _worldActor.Profile;
        AreaKnockdownClip? Clip(ActorDiagnosticResource? art) => art is null ? null : new(art.FramesPerGroup, art.RawFramesPerSecond);
        var (fall, getUp) = (Clip(profile.FallDown), Clip(profile.GetUp));
        var down = status.IsDead || status.IsUnconscious || _heroForcedDown;
        var now = WorldMilliseconds();
        if (_heroKnockdownFor.Session != session.Version.Owner || _heroKnockdownFor.Generation != area.Generation)
        {
            // A new session or map (a load, a transfer, a window move): a hero down already lies, none falls.
            _heroKnockdown.Rebase(down, fall);
            _heroKnockdownOctant = _worldActor.State.Facing;
            _heroKnockdownMs = now;
            if (down) LogHeroKnockdown(status, "rebase");
        }
        else if (!ReferenceEquals(_heroKnockdownFor.Profile, profile) && _heroKnockdown.Lying)
            // Another body (its ARTs read, armour changed): the lying hero keeps its stage on the new body's last fall frame.
            _heroKnockdown.Rebase(down || _heroKnockdown.Stage != AreaKnockdownStage.GettingUp, fall);
        _heroKnockdownFor = (session.Version.Owner, area.Generation, profile);
        var elapsed = Math.Max(0, now - (_heroKnockdownMs ?? now));
        _heroKnockdownMs = now;
        var settled = grid.State.Phase == MovementPhase.Settled;
        if (!_heroKnockdown.Lying && down && settled) _heroKnockdownOctant = _worldActor.State.Facing;
        var wasLying = _heroKnockdown.Lying;
        var stage = _heroKnockdown.Stage;
        _heroKnockdown.Tick(elapsed, down, settled, HeroMayGetUp, fall, getUp);
        if (!wasLying && down) StopHeroForKnockdown();
        if (_heroKnockdown.Stage != stage) LogHeroKnockdown(status, "tick");
        (ActorDiagnosticAction, int, int)? pose = _heroKnockdown.Stage switch
        {
            AreaKnockdownStage.Falling or AreaKnockdownStage.Down when fall is not null => (ActorDiagnosticAction.FallDown, _heroKnockdownOctant, _heroKnockdown.Frame),
            AreaKnockdownStage.GettingUp when getUp is not null => (ActorDiagnosticAction.GetUp, _heroKnockdownOctant, _heroKnockdown.Frame),
            _ => null,
        };
        if (_worldActor.DownPose == pose) return;
        _worldActor.DownPose = pose;
        PublishWorldSelection();
    }

    /// <summary>A conscious hero lying down gets up when it is active (UC3: not stunned, paralysed or stoned either): at once in real
    /// time; in a turn-based round only on its own subturn and when the get-up's action points are paid (A3; a refusal passes the
    /// subturn as any refused charge).</summary>
    private bool HeroMayGetUp()
    {
        if (_areaSession?.QueryHeroLife()?.Status is not { IsActive: true }) return false;
        if (!InTurnBasedRound) return true;
        if (_areaSession?.QueryCombatRound() is not { Actor.IsHero: true }) return false;
        return ChargeHeroAction(TurnBasedRules.GetUpCost, "get up");
    }

    /// <summary>The hero went down: what it was doing ends (the original's going unconscious ends its goals with the knockdown).</summary>
    private void StopHeroForKnockdown()
    {
        StopWorldRoute("HERO_DOWN");
        CancelAreaAttack("the hero is down");
        CancelTalkApproach("the hero is down"); CancelAreaUseApproach("the hero is down"); CancelInventoryApproach("the hero is down");
        CancelPortalApproach("the hero is down");
        CancelSpellTargeting("the hero is down");
        _stickHeld = false;
    }

    private void LogHeroKnockdown(LifeStatus status, string why) =>
        GD.Print($"ARC-COMBAT-021 hero knockdown stage={_heroKnockdown.Stage} frame={_heroKnockdown.Frame} why={why} fatigue={status.Fatigue}/{status.MaxFatigue}"
            + $" dead={status.IsDead} unconscious={status.IsUnconscious} art={(_worldActor.Profile.FallDown is null ? "none" : "fall+getup")}");

    private string HeroKnockdownStatus() => _heroKnockdown.Lying && _areaSession is not null
        ? $" | hero: {_heroKnockdown.Stage switch { AreaKnockdownStage.Falling => "falling", AreaKnockdownStage.GettingUp => "getting up", _ => _areaSession.QueryHeroLife()?.Status.IsDead == true ? "dead" : "unconscious" }}"
        : string.Empty;
}
