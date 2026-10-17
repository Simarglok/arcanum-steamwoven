namespace Arcanum.Simulation.Combat;

/// <summary>The weapon slot of a critter's ART id (the source's <c>TIG_ART_WEAPON_TYPE_*</c>, in its order).</summary>
public enum AttackArtWeapon
{
    NoWeapon, Unarmed, Dagger, Sword, Axe, Mace, Pistol, TwoHandedSword, Bow, Type9, Rifle, Type11, Type12, Staff, Type14,
}

/// <summary>The two attack animations of a critter ART (the source's <c>TIG_ART_ANIM_ATTACK</c> and <c>ATTACK_LOW</c>).</summary>
public enum AttackAnimation { Attack, AttackLow }

/// <summary>Why a critter's combat mode did not change (<see cref="RealTimeCombatRules.ToggleRefusal"/>).</summary>
public enum CombatModeRefusal
{
    None,
    /// <summary>Stoned: combat mode can neither be entered nor left.</summary>
    Stoned,
    /// <summary>Paralysed: combat mode can neither be entered nor left.</summary>
    Paralysed,
    /// <summary>Stunned: combat mode cannot be left.</summary>
    Stunned,
    /// <summary>Not active (unconscious, stunned, paralysed, stoned): combat mode cannot be entered.</summary>
    Inactive,
    /// <summary>An NPC in its concealed animation cannot toggle.</summary>
    Concealed,
}

/// <summary>
/// What the toggle reads about one critter: dead (which skips every refusal), active (alive, conscious, neither stunned
/// nor paralysed nor stoned), stoned, paralysed, stunned, whether it plays its concealed animation and whether it is the
/// local PC (the PC may toggle while concealed).
/// </summary>
public readonly record struct CombatModeFacts(bool Dead, bool Active, bool Stoned, bool Paralysed, bool Stunned, bool Concealed, bool LocalPc);

/// <summary>
/// What the real-time attack repeat reads (the source's auto-attack check at the end of every attack): the attacker in
/// combat mode and a PC, the <c>auto_attack</c> setting, no turn-based round, the target neither destroyed nor switched
/// off nor dead nor busted, the attack run not flagged to stop (the run flag 0x800), the attacker not
/// flagged non-lethal and no boomerang of its in flight.
/// </summary>
public readonly record struct AutoAttackFacts(bool AttackerInCombatMode, bool AttackerIsPc, bool AutoAttackSetting, bool RoundActive,
    bool TargetDestroyedOrOff, bool TargetDead, bool TargetBusted, bool StopRequested, bool NonLethal, bool UsingBoomerang);

/// <summary>
/// One attack animation as the attack goal plays it: its frame count, its action frame (the frame whose display runs the
/// hit), the pause per frame (<see cref="RealTimeCombatRules.AttackFramePauseMs"/>) and the weapon slot of the ART (pistols,
/// rifles and bows repeat their shot in real time).
/// </summary>
public sealed record AttackClip
{
    public AttackClip(int frames, int actionFrame, int pauseMs, AttackArtWeapon weapon)
    {
        if (frames is < 1 or > RealTimeCombatRules.MaxFrames) throw new ArgumentOutOfRangeException(nameof(frames));
        if (actionFrame is < 0 or > RealTimeCombatRules.MaxFrames) throw new ArgumentOutOfRangeException(nameof(actionFrame));
        if (pauseMs is < RealTimeCombatRules.MinFramePauseMs or > RealTimeCombatRules.MaxFramePauseMs) throw new ArgumentOutOfRangeException(nameof(pauseMs));
        if (!Enum.IsDefined(weapon)) throw new ArgumentOutOfRangeException(nameof(weapon));
        Frames = frames; ActionFrame = actionFrame; PauseMs = pauseMs; Weapon = weapon;
    }

    public int Frames { get; }
    public int ActionFrame { get; }
    public int PauseMs { get; }
    public AttackArtWeapon Weapon { get; }
}

/// <summary>
/// A playing attack animation: the frame shown, whether the action frame was reached and whether its hit already ran
/// (the source's run flags 0x04 and 0x08), and whether it still plays.
/// </summary>
public readonly record struct AttackPlayback(int Frame, bool ActionReached, bool HitDone, bool Playing);

/// <summary>What one update of a playing attack did.</summary>
public enum AttackUpdateKind
{
    /// <summary>The next frame is shown.</summary>
    Advanced,
    /// <summary>The next frame is shown and it is the action frame: the hit runs now. The host first checks the target is
    /// still in range with a clear line of fire; if not, it ends the attack without a hit (<see cref="RealTimeCombatRules.Abort"/>).</summary>
    Hit,
    /// <summary>A pistol, rifle or bow went back three frames to fire again (real time only).</summary>
    Rewound,
    /// <summary>The animation ended (its last frame was shown for a pause), or the attacker can no longer act.</summary>
    Ended,
}

/// <summary>One update's result: the playback after it and the real milliseconds until the next update (none once it ended).</summary>
public readonly record struct AttackUpdate(AttackUpdateKind Kind, AttackPlayback Playback, int? NextUpdateMs);

/// <summary>One moment of an attack's plan (<see cref="RealTimeCombatRules.Schedule"/>), in real milliseconds from its start.</summary>
public readonly record struct AttackMoment(long AtMs, AttackUpdateKind Kind, int Frame);

/// <summary>
/// Real-time combat (ARC-COMBAT-004; the source's combat mode and attack animation, ARC-RES-030 C1–C5) as pure functions:
/// entering and leaving combat mode and the PC's exit gate, the frame rate and pause of an attack animation from the
/// attacker's Speed and weapon, the attack goal's frame-by-frame playback with the action-frame hit and the pistol, rifle
/// and bow repeat, the auto-attack repeat, and the fatigue charge of running in combat mode. Numbers are checked in the
/// original 1.0.7.4 executable (ledger rows RT1–RT12,
/// docs/world/real-time-combat-runtime.md). Every function validates its inputs (<see cref="ArgumentException"/>).
/// </summary>
public static class RealTimeCombatRules
{
    /// <summary>Clamps of a frame pause, real milliseconds.</summary>
    public const int MinFramePauseMs = 30;
    public const int MaxFramePauseMs = 800;
    /// <summary>Real milliseconds between the end of an attack and the start of its auto-attack repeat.</summary>
    public const int RepeatDelayMs = 5;
    /// <summary>Real milliseconds between the start of an attack (its first frame) and its first update.</summary>
    public const int FirstUpdateDelayMs = 0;
    /// <summary>Frames a pistol, rifle or bow goes back to fire again.</summary>
    public const int RepeatRewindFrames = 3;
    /// <summary>A critter's attack start rolls <c>1..AttackSoundSides</c> and plays its attacking sound on 1.</summary>
    public const int AttackSoundSides = 4;
    /// <summary>Run steps between two fatigue charges of a runner in combat mode, before the fatigue-limiting and
    /// fatigue-draining factors.</summary>
    public const int RunChargeSteps = 5;
    /// <summary>Fatigue damage each run charge adds (the original, anomaly A4).</summary>
    public const int RunChargeFatigue = 1;

    /// <summary>The <c>auto attack</c> setting's registered default in the original: on (ledger RT6). A
    /// value already in the player's configuration wins.</summary>
    public const bool AutoAttackDefault = true;
    /// <summary>The <c>turn-based</c> setting's registered default: off.</summary>
    public const bool TurnBasedDefault = false;

    /// <summary>Bound of a Speed or weapon speed these rules read, and of an ART frame rate.</summary>
    public const int MaxStat = 1000;
    /// <summary>Bound of an ART's frame count and action frame.</summary>
    public const int MaxFrames = 1000;

    /// <summary>
    /// Whether a critter's combat mode may toggle (the source's toggle): a dead critter always may; otherwise stoned and
    /// paralysed critters never, a critter in combat mode not while stunned, one out of it only while active, and an NPC
    /// (not the local PC) not while it plays its concealed animation. <paramref name="inCombatMode"/> is its current mode.
    /// </summary>
    public static CombatModeRefusal ToggleRefusal(bool inCombatMode, CombatModeFacts facts)
    {
        if (!facts.Dead)
        {
            if (facts.Stoned) return CombatModeRefusal.Stoned;
            if (facts.Paralysed) return CombatModeRefusal.Paralysed;
            if (inCombatMode && facts.Stunned) return CombatModeRefusal.Stunned;
            if (!inCombatMode && !facts.Active) return CombatModeRefusal.Inactive;
        }
        return facts.Concealed && !facts.LocalPc && !facts.Dead ? CombatModeRefusal.Concealed : CombatModeRefusal.None;
    }

    /// <summary>
    /// The exit gate of the PC's combat-mode key, a dialog, sleep and the world map (the source's
    /// 0x4B3D90): always true out of combat mode or when not active; false while an attack is
    /// being attempted; false when an active, fighting NPC on screen has the critter or one of its followers as combat
    /// focus (<paramref name="threatened"/>).
    /// </summary>
    [OriginalFunction(0x4B3D90, OriginalFunctionScope.Partial)]
    public static bool CanExitCombatMode(bool inCombatMode, bool active, bool attemptingAttack, bool threatened) =>
        !inCombatMode || !active || !attemptingAttack && !threatened;

    /// <summary>
    /// Frames per second of an attack animation (the source's Speed-scaled rate): the ART's own rate for an ART that is not a
    /// critter, monster or unique NPC, under a body-of-element spell, or for a weapon slot without an entry; otherwise from
    /// the slot's low, middle and high values (no dwarf or halfling offset for attacks): Speed below 8 gives
    /// <c>low + Speed·(middle − low)/8</c>, 8 the middle, below 30 <c>middle + Speed·(high − middle)/30</c>, else the high
    /// value; divisions truncate toward zero.
    /// </summary>
    public static int AttackFramesPerSecond(AttackAnimation animation, AttackArtWeapon weapon, int speed, int artFramesPerSecond,
        bool critterArt = true, bool elementalBody = false)
    {
        if (!Enum.IsDefined(animation)) throw new ArgumentOutOfRangeException(nameof(animation));
        if (!Enum.IsDefined(weapon)) throw new ArgumentOutOfRangeException(nameof(weapon));
        if (speed is < 1 or > MaxStat) throw new ArgumentOutOfRangeException(nameof(speed));
        if (artFramesPerSecond is < 1 or > MaxStat) throw new ArgumentOutOfRangeException(nameof(artFramesPerSecond));
        if (!critterArt || elementalBody || Rates(animation, weapon) is not var (middle, low, high)) return artFramesPerSecond;
        if (speed < 8) return low + speed * (middle - low) / 8;
        if (speed == 8) return middle;
        return speed < 30 ? middle + speed * (high - middle) / 30 : high;
    }

    // (middle, low, high) per weapon slot; null keeps the ART rate.
    private static (int Middle, int Low, int High)? Rates(AttackAnimation animation, AttackArtWeapon weapon) => animation == AttackAnimation.Attack
        ? weapon switch
        {
            AttackArtWeapon.Unarmed or AttackArtWeapon.Sword => (15, 7, 23),
            AttackArtWeapon.Dagger or AttackArtWeapon.Bow => (10, 6, 14),
            AttackArtWeapon.Axe or AttackArtWeapon.Mace or AttackArtWeapon.TwoHandedSword => (14, 8, 20),
            AttackArtWeapon.Pistol => (8, 3, 12),
            AttackArtWeapon.Rifle => (6, 3, 10),
            AttackArtWeapon.Staff => (12, 8, 16),
            _ => null,
        }
        : weapon switch
        {
            AttackArtWeapon.Unarmed => (13, 8, 18),
            AttackArtWeapon.Dagger or AttackArtWeapon.Bow or AttackArtWeapon.Staff => (10, 6, 14),
            AttackArtWeapon.Sword => (14, 8, 20),
            AttackArtWeapon.Axe or AttackArtWeapon.Mace => (11, 7, 15),
            AttackArtWeapon.Pistol or AttackArtWeapon.Rifle => (6, 3, 10),
            AttackArtWeapon.TwoHandedSword => (13, 9, 17),
            _ => null,
        };

    /// <summary>
    /// The pause per attack frame, real milliseconds: <c>1000 / fps</c> clamped to 30..800, then less
    /// <c>10 × (weapon speed − 10)</c> and clamped again (weapon speed: <see cref="TurnBasedRules.WeaponSpeed"/>, 10 unarmed).
    /// </summary>
    public static int AttackFramePauseMs(int framesPerSecond, int weaponSpeed)
    {
        if (framesPerSecond is < 1 or > MaxStat) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (Math.Abs(weaponSpeed) > MaxStat) throw new ArgumentOutOfRangeException(nameof(weaponSpeed));
        var pause = Math.Clamp(1000 / framesPerSecond, MinFramePauseMs, MaxFramePauseMs);
        return Math.Clamp(pause - 10 * (weaponSpeed - 10), MinFramePauseMs, MaxFramePauseMs);
    }

    /// <summary>
    /// The start of an attack animation: its first frame, the action already reached when the action frame is 0. The first
    /// update follows after <see cref="FirstUpdateDelayMs"/>.
    /// </summary>
    public static AttackPlayback Begin(AttackClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return new(0, clip.ActionFrame < 1, false, true);
    }

    /// <summary>
    /// One update of a playing attack (the source's attack update and the goal steps after it). An attacker that can no
    /// longer act (stoned, paralysed, stunned) or a shown last frame ends it. Reaching the frame before the action frame
    /// marks the action; then in real time (<paramref name="roundActive"/> false) a critter's pistol or rifle two frames
    /// after the action frame, or its bow on the action frame, goes back three frames and turns to the target when
    /// <paramref name="repeatShot"/> holds (ammunition left, a PC or an NPC not backing off, the target still valid); else
    /// the next frame is shown. The first update that shows the action reached runs the hit once. The next update follows
    /// one pause later.
    /// </summary>
    public static AttackUpdate Update(AttackClip clip, AttackPlayback playback, bool attackerCanAct, bool roundActive, bool repeatShot)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!playback.Playing) throw new ArgumentException("The attack no longer plays.", nameof(playback));
        if (playback.Frame < 0 || playback.Frame >= clip.Frames) throw new ArgumentOutOfRangeException(nameof(playback));
        if (!attackerCanAct || playback.Frame == clip.Frames - 1) return new(AttackUpdateKind.Ended, playback with { Playing = false }, null);
        var reached = playback.ActionReached || playback.Frame == clip.ActionFrame - 1;
        var rewind = !roundActive && repeatShot && playback.Frame >= RepeatRewindFrames && playback.Frame == clip.Weapon switch
        {
            AttackArtWeapon.Pistol or AttackArtWeapon.Rifle => clip.ActionFrame + 2,
            AttackArtWeapon.Bow => clip.ActionFrame,
            _ => -1,
        };
        if (rewind) return new(AttackUpdateKind.Rewound, new(playback.Frame - RepeatRewindFrames, false, false, true), clip.PauseMs);
        var next = new AttackPlayback(playback.Frame + 1, reached, playback.HitDone, true);
        if (next.ActionReached && !next.HitDone) return new(AttackUpdateKind.Hit, next with { HitDone = true }, clip.PauseMs);
        return new(AttackUpdateKind.Advanced, next, clip.PauseMs);
    }

    /// <summary>The hit found the target out of range or behind cover: the attack ends without damage.</summary>
    public static AttackPlayback Abort(AttackPlayback playback) => playback with { Playing = false };

    /// <summary>
    /// The plan of one attack with no repeat shot and an attacker that keeps acting: every update from the start in real
    /// milliseconds (frame <c>f ≥ 1</c> appears at <c>(f − 1) × pause</c>, the hit with the action frame's display, the end
    /// one pause after the last frame appears). An action frame on the last frame still hits; one past it never does.
    /// </summary>
    public static IReadOnlyList<AttackMoment> Schedule(AttackClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var moments = new List<AttackMoment>(clip.Frames + 1);
        var playback = Begin(clip);
        long at = FirstUpdateDelayMs;
        while (true)
        {
            var update = Update(clip, playback, true, false, false);
            moments.Add(new(at, update.Kind, update.Playback.Frame));
            if (update.NextUpdateMs is not { } delay) return moments;
            at += delay;
            playback = update.Playback;
        }
    }

    /// <summary>
    /// Whether the attack goal starts again on the same target <see cref="RepeatDelayMs"/> after it ended (the source's
    /// auto-attack check): only for a PC in combat mode with the <c>auto_attack</c> setting on and no round active, a target
    /// that is still there, alive and not busted, a run not flagged to stop, and neither non-lethal combat nor a boomerang
    /// in flight.
    /// </summary>
    public static bool Repeats(AutoAttackFacts facts) =>
        facts.AttackerInCombatMode && facts.AttackerIsPc && facts.AutoAttackSetting && !facts.RoundActive && !facts.TargetDestroyedOrOff &&
        !facts.TargetDead && !facts.TargetBusted && !facts.StopRequested && !facts.NonLethal && !facts.UsingBoomerang;

    /// <summary>
    /// Run steps per fatigue charge of a runner in combat mode: 5, ×4 for a fatigue-limiting critter, then ÷4 (at least 1)
    /// for a fatigue-draining one.
    /// </summary>
    public static int RunChargePeriod(bool fatigueLimiting, bool fatigueDraining)
    {
        var period = RunChargeSteps * (fatigueLimiting ? 4 : 1);
        return fatigueDraining ? Math.Max(1, period / 4) : period;
    }

    /// <summary>
    /// One run step of a runner in combat mode with fatigue above 0 (the source's step cost): the counter of its move goes
    /// up; reaching the period resets it and charges <see cref="RunChargeFatigue"/>. A runner without fatigue walks and
    /// counts nothing; a new move goal starts the counter at 0.
    /// </summary>
    public static (int Counter, bool Charge) RunStep(int counter, int period)
    {
        if (period is < 1 or > RunChargeSteps * 4) throw new ArgumentOutOfRangeException(nameof(period));
        if (counter < 0 || counter >= period) throw new ArgumentOutOfRangeException(nameof(counter));
        var next = counter + 1;
        return next >= period ? (0, true) : (next, false);
    }
}
