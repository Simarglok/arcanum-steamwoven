using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    // ARC-COMBAT-1793: the dying animation (7, 17, 18 or 19) the hero's dying goal (31) named at its death, read from the goal's frame before the fall starts; 0 when none was seen (a hero that was already
    // down when killed, a death found at a rebase after a load or a map change, a goal the host did not run): the hero then falls plain, as before.
    private int _heroDyingAnimation;

    /// <summary>The knock-down goal's step forces the fall (true) or releases it (false).</summary>
    internal void HeroForcedDown(bool on) => _heroForcedDown = on;

    /// <summary>The fall has played to its last frame and the hero lies.</summary>
    internal bool HeroFellDownNow => _heroKnockdown.Stage == AreaKnockdownStage.Down;

    /// <summary>The hero stands on its feet: nothing falls, lies or gets up.</summary>
    internal bool HeroStandsUpNow => !_heroKnockdown.Lying;

    /// <summary>
    /// Whether the viewer can go on playing the hero's fall or get-up, so a knock-down or get-up run may keep waiting for it: an AREA map with a hero drawn, and for the get-up a hero that is active and, in a turn-based round,
    /// on its own subturn (a refused charge passes the turn: the run ends, the viewer's own rule tries again on a later subturn).
    /// </summary>
    internal bool HeroStateMayWait(bool getUp)
    {
        if (_readyArea is null || _readyMode != TerrainViewerMode.Area || _worldActor.Session is not AreaGridSession || _areaSession?.QueryHeroLife()?.Status is not { } status) return false;
        if (!getUp) return true;
        return status.IsActive && (!InTurnBasedRound || _areaSession.QueryCombatRound() is { Actor.IsHero: true });
    }

    /// <summary>The hero lies or is down in the session: it takes no world command.</summary>
    // ARC-DEV-189: a knock-down result on a conscious hero lies from the start of its goal 62 (the forced fall), before the viewer's fall plays; the viewer's own get-up stage is the machine's Lying.
    private bool HeroLyingDown => _heroKnockdown.Lying || _heroForcedDown || _areaSession?.QueryHeroLife()?.Status is { IsDead: true } or { IsUnconscious: true }
        || ShownHeroRunsHost?.DownRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.KnockDown };

    /// <summary>Per world frame, before the hero's input: follows the session hero's consciousness and moves the fall or the get-up on
    /// by the world clock.</summary>
    private void ProcessHeroKnockdown()
    {
        if (_readyArea is not { } area || _readyMode != TerrainViewerMode.Area || _areaSession is not { } session
            || _worldActor.Session is not AreaGridSession grid || session.QueryHeroLife()?.Status is not { } status)
        {
            // No hero drawn on an AREA map: nothing lies, and the next one is rebased.
            if (_heroKnockdown.Lying) _heroKnockdown.Rebase(false, null);
            _heroForcedDown = false;
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
            // A new session or map (a load, a transfer, a window move): a hero down already lies, none falls. A forced fall belongs to a run of the old session.
            _heroForcedDown = false;
            _heroDyingAnimation = 0;
            down = status.IsDead || status.IsUnconscious;
            _heroKnockdown.Rebase(down, fall);
            _heroKnockdownOctant = _worldActor.State.Facing;
            _heroKnockdownMs = now;
            if (down) LogHeroKnockdown(status, "rebase");
        }
        else if (!ReferenceEquals(_heroKnockdownFor.Profile, profile) && _heroKnockdown.Lying)
            // Another body (its ARTs read, armour changed): the lying hero keeps its stage on the new body's last fall frame.
            _heroKnockdown.Rebase(down || _heroKnockdown.Stage != AreaKnockdownStage.GettingUp, fall);
        _heroKnockdownFor = (session.Version.Owner, area.Generation, profile);
        // ARC-COMBAT-1793: a hero killed standing (or getting up) plays the dying animation its dying goal names (the goal's frame, GS: the starter 0x435080); a hero killed while it lies or falls has a goal with
        // count 1 and keeps its fall, as an NPC does (0x45D730). The goal may start a frame late (a kill inside a spell run waits for the spell system's end), so the frame is read whenever it shows.
        if (!status.IsDead) _heroDyingAnimation = 0;
        else if (_heroDyingAnimation == 0 && ShownHeroRunsHost?.DownRun is { Bottom: { Goal: Arcanum.Simulation.Goals.GoalType.Dying, Count: 0 } dying })
            _heroDyingAnimation = DyingAnimationRules.Chosen(dying.Animation, _gameSettings.ViolenceFilter);
        var dyingArt = status.IsDead ? profile.DyingArt(_heroDyingAnimation) : null;
        if (dyingArt is { Resource: var dyingResource } && Clip(dyingResource) is { } dyingClip && dyingClip != fall)
        {
            fall = dyingClip;
            // A dead hero lying already (a goal a load kept) lies on the last frame of the bloody ART.
            if (_heroKnockdown.Stage == AreaKnockdownStage.Down && _heroKnockdown.Frame != dyingClip.Frames - 1) _heroKnockdown.Rebase(true, fall);
        }
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
            // ARC-COMBAT-1793: a dead hero shows the bloody dying ART its goal named when this body has it, else the plain fall.
            AreaKnockdownStage.Falling or AreaKnockdownStage.Down when fall is { } clip => (dyingArt?.Action ?? ActorDiagnosticAction.FallDown, _heroKnockdownOctant, Math.Min(_heroKnockdown.Frame, clip.Frames - 1)),
            AreaKnockdownStage.GettingUp when getUp is not null => (ActorDiagnosticAction.GetUp, _heroKnockdownOctant, _heroKnockdown.Frame),
            _ => null,
        };
        // ARC-COMBAT-1741: the hero's stun run writes the stunned art (animation 23) frame by frame; a hero standing in it is drawn in that pose, facing where it stood.
        if (pose is null && profile.Stunned is not null && _aiHost?.ArtOf(Arcanum.Simulation.Ai.AiSchedulerRules.Pc) is { Animation: AreaFallenPoses.Stunned } stunnedArt && settled)
            pose = (ActorDiagnosticAction.Stunned, _worldActor.State.Facing, Math.Clamp(stunnedArt.Frame, 0, profile.Stunned.FramesPerGroup - 1));
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
        // ARC-DEV-189: the table's runs were ended by the knock-down goal's own start (its interrupts); the raw stop leaves the knock-down run itself alone (a level-3 interrupt would end it).
        StopWorldRouteOnly("HERO_DOWN");
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
