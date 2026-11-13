using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-107: a fired native trap for <see cref="SessionSpellCaster.ApplyTrapEffect"/>: the trapped object (the run's attachee),
/// the triggerer (null: none), the run <see cref="GameSessionOwner.TriggerTrap"/> answered, and the use script's counters and the
/// trapped object's object flags as the trap state reports them. <see cref="Shooters"/> are the objects a 30002 trap searches for its
/// shooter (ledger TT12): the host lists the walls, portals, scenery and trap objects in the screen-sized rectangle around the
/// triggerer, in the original's order, each with its use script and counters; empty for any other trap.
/// </summary>
public sealed record SessionTrapFiring(string Trap, string? Triggerer, NativeTrapRun Run, int Counters, int ObjectFlags)
{
    public IReadOnlyList<TrapShooterCandidate> Shooters { get; init; } = [];
}

/// <summary>What the trap's damage or shot did to one victim.</summary>
public enum SessionTrapHit
{
    /// <summary>A spell trap (30000): no damage, only the spell.</summary>
    None,
    /// <summary>The damage step dealt its damage (to a critter's life, or to an object through the map).</summary>
    Damaged,
    /// <summary>The damage step prevented it (an invulnerable or nigh-invulnerable target, a wall).</summary>
    Prevented,
    /// <summary>The session or the map did not take the damage (a dead critter, an object the map does not damage).</summary>
    NotApplied,
    /// <summary>ARC-DEV-261 (GT198–GT199): the victim's taking-damage script (point 0x17) skipped the default, which cancels the damage step: no amount reaches the victim.</summary>
    Vetoed,
    /// <summary>A shot (30002) found no shooter: nothing happens to the victim, no spell either. A shot with a shooter reports the damage step's own result
    /// (<see cref="Damaged"/>, <see cref="Prevented"/>, <see cref="NotApplied"/>, <see cref="Vetoed"/>) with <see cref="SessionTrapVictim.Shooter"/> set.</summary>
    NoShooter,
    /// <summary>ARC-TECHNOLOGY-1635 (TT15): the shot's missile is in flight; the strike (the attack event, then the damage) comes where it meets the victim (<see cref="SessionSpellCaster.StrikeTrapShot"/>) or never,
    /// when the victim left the line (<see cref="SessionTrapVictim.Shooter"/> is set, the amount drawn is <see cref="SessionTrapVictim.Amount"/>).</summary>
    InFlight,
    /// <summary>ARC-TECHNOLOGY-1635 (TT15, 0x4F5270 kind 3): the hero's armed critical-miss fate turned the shot's hit into a miss: the shot's critical-miss procedure ran (0x4B6410) and the missile popped on its own
    /// tile, so nothing struck; the fate is used up.</summary>
    Missed,
}

/// <summary>ARC-TECHNOLOGY-1635 (TT15, 0x4B24F0): a trap's shot leaving its shooter object, as <see cref="SessionSpellCaster.LaunchTrapShot"/> is told: the session's shot (<see cref="GameSessionOwner.QueryTrapShot"/>), the
/// shooter object and the victim (the cast map's ids), the shooter's tile and the victim's tile, the missile's destination.</summary>
public sealed record SessionTrapShotLaunch(SessionShot Shot, string Shooter, string Victim, CellCoord From, CellCoord Destination);

/// <summary>One victim's effect: the drawn amount, the damage step (null without one), the hit points the session took, the shooter
/// of a shot, and the spell cast at the victim (<see cref="SpellCastAttemptOutcome.Nothing"/> when none was cast or the spell is
/// unknown, <see cref="SpellCastAttemptOutcome.Refused"/> when the session refused the run) with its run.</summary>
public sealed record SessionTrapVictim(string Entity, SessionTrapHit Hit, int Amount, AttackDamage? Step, int HitPointDamage, string? Shooter,
    SpellCastAttemptOutcome Spell, int? SpellRun);

/// <summary>The effect of one fired native trap: the decoded effect (null: the run does nothing, 30007), the damage source the kill
/// credits (the player for a PC-placed trap, else none) and each victim in the original's order.</summary>
public sealed record SessionTrapEffectResult(TrapEffect? Effect, string? Killer, IReadOnlyList<SessionTrapVictim> Victims)
{
    public static SessionTrapEffectResult None { get; } = new(null, null, []);
}

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-TECHNOLOGY-1635 (TT15): the host's projectile launch for a trap's shot (the dispatcher's ranged path: one projectile of the default art from the shooter's tile to the victim's location, which carries the
    /// amount). True when the host launched it: the strike then comes where the missile meets the victim (<see cref="StrikeTrapShot"/>) and the shot ends at the host's call (<see cref="GameSessionOwner.EndShot"/>). False,
    /// or no launch hook, and the shot strikes at once, as it did before the flight was drawn.
    /// </summary>
    public Func<SessionTrapShotLaunch, bool>? LaunchTrapShot { get; set; }

    /// <summary>
    /// ARC-WORLD-107: the effect of a native trap script the session fired (0x4BCDC0 for each victim of 0x4BCB70, ledger TT8,
    /// TT11–TT14). The victims are every PC and NPC of the map in the run's square around the trapped object (the map's square
    /// query), or the triggerer alone for a run of radius 0 (none without a triggerer). For each victim in order: a damage
    /// (30001, 30003..30006) draws its amount and takes the damage step (<see cref="AttackResolver.DamageStep"/>, the same as a
    /// spell's) with the trapped object as the attacker and its own weapon (a non-weapon, so no weapon flag counts), the player as the
    /// damage source of a PC-placed trap, applied to the critter's life or through the map to an object; a shot (30002) looks for its
    /// shooter (<see cref="TrapEffectRules.Shooter"/>; none: nothing at all) and shoots it (<see cref="TrapShot"/>);
    /// the item events a victim's damage step calls for fire before its spell and the next victim (as inside 0x4BCDC0); then the
    /// spell — always for 30000, else the top byte when not 0 — starts as a run with no parent and no source at the victim (0x455A20, 0x455AC0),
    /// whose BEGIN comes as an event. The run's floating line (<see cref="TrapEffectRules.FiredLine"/> over the triggerer) and its eye
    /// candy lines (<see cref="NativeTrapRun"/>) are the host's to show. Nothing for 30007.
    /// </summary>
    public SessionTrapEffectResult ApplyTrapEffect(SessionTrapFiring firing)
    {
        ArgumentNullException.ThrowIfNull(firing);
        ArgumentNullException.ThrowIfNull(firing.Trap);
        ArgumentNullException.ThrowIfNull(firing.Shooters);
        if (firing.Run.Effect == NativeTrapEffect.None || TrapEffectRules.Of(firing.Run.Script, firing.Counters) is not { } effect)
            return SessionTrapEffectResult.None;
        // TT15: the shot's context is rebuilt where its missile strikes (the shooter is the damage source), so a PC-placed trap's player credit is lost in a shot.
        var killer = effect.Kind != TrapEffectKind.Shot && TrapEffectRules.CreditsPlayer(firing.ObjectFlags) ? Hero : null;
        var victims = TrapVictims(firing).Select(victim => TrapHit(effect, firing, victim, killer)).ToArray();
        return new(effect, killer, victims);
    }

    // The victims of 0x4BCB70: the PCs and NPCs in the square (0x4AE4E0 → 0x440B40), or the triggerer alone.
    private IReadOnlyList<string> TrapVictims(SessionTrapFiring firing)
    {
        if (firing.Run.Radius == 0) return firing.Triggerer is null ? [] : [firing.Triggerer];
        return _map.Object(firing.Trap) is { } trap
            ? _map.ObjectsInSquare(trap.Location, firing.Run.Radius, TrapEffectRules.VictimTypeMask).Select(o => o.Id).ToArray()
            : [];
    }

    private SessionTrapVictim TrapHit(TrapEffect effect, SessionTrapFiring firing, string victim, string? killer)
    {
        if (effect.Kind == TrapEffectKind.Shot) return TrapShot(effect, firing, victim);
        var (hit, amount, step, hitPoints) = (SessionTrapHit.None, 0, (AttackDamage?)null, 0);
        if (effect.Kind == TrapEffectKind.Damage)
        {
            amount = TrapEffectRules.Amount(effect, _owner.CombatRolls);
            (hit, step, hitPoints) = TrapDamage(effect, amount, victim, firing.Trap, killer, hitEffectFirst: true);
        }
        var (spell, run) = effect.CastsSpell ? CastWithoutCaster(effect.Spell, victim) : (SpellCastAttemptOutcome.Nothing, null);
        return new(victim, hit, amount, step, hitPoints, null, spell, run);
    }

    // ARC-DEV-261 (GT198): the victim's taking-damage script comes first, before any gate or step; the attacker is the trap (TT13) or the shot's shooter. The amount is drawn already.
    // A veto cancels the damage step only; the trap's spell (a separate component of the effect) is cast all the same.
    private (SessionTrapHit Hit, AttackDamage? Step, int HitPoints) TrapDamage(TrapEffect effect, int amount, string victim, string attacker, string? killer, bool hitEffectFirst)
    {
        if (TakingDamage is { } asks && !asks(victim, attacker)) return (SessionTrapHit.Vetoed, null, 0);
        var who = new SpellRunObject(victim, _map.Object(victim)?.Type ?? 0);
        var kind = Kind(who);
        var step = AttackResolver.DamageStep(new()
        {
            Attacker = new Combatant(), Target = Combatant(who), TargetKind = kind, Difficulty = Difficulty,
            Damage = TrapEffectRules.Damage(effect, amount),
        });
        var (hit, hitPoints) = step.Prevented != DamagePrevention.None ? (SessionTrapHit.Prevented, 0)
            : ApplyTrapDamage(victim, kind, step, attacker, killer, hitEffectFirst);
        FirePendingItems();
        return (hit, step, hitPoints);
    }

    /// <summary>
    /// ARC-TECHNOLOGY-1619 (TT15, VB2-2 gap d): the shot of a 30002 trap at one victim (0x4BCDC0 case 0x7532 → the dispatcher 0x4B3170). Read in the executable: the shooter (found by the tag, none: the victim takes nothing and gets no spell)
    /// is the attack's attacker with no weapon (the weapon is the shooter itself, kind 3, until the trap clears it), the context holds flags 0x300 (an automatic hit, ranged) and the normal slot drawn from the trap's range (the
    /// amount, one draw per victim). The dispatcher draws the hit location (0x4B65A0: one d100) and the technology-failure roll (one d100 against a chance of 0 for a context of kind 3), decides a hit at once (flag 0x100)
    /// and, kind 3 being a ranged attack with no weapon, raises no attack event and no taunt or attack end at the shot; its ranged path flies a projectile of the default art (0x40000000, flag 0x100) from the
    /// shooter's tile to the victim's, which carries the amount in field 0x4F (with 0x100 the slot is the "hit location" it stores) and no weapon. The trap's spell (top byte) is cast at the victim as soon as the dispatcher
    /// returns, before the missile arrives. Where the projectile reaches the victim's tile (0x4B2870) a new context is built (attacker the shooter, target the struck object, hit location 0, the amount back in the normal slot, flag
    /// 0x300 kept, the damage source the shooter and not the trap's player: a PC-placed trap's kill credit is lost in the shot): the projectile's end raises the attack event (0x4B2690 → 0x4A9650,
    /// <see cref="AiNotice"/> with the shooter object as the attacker and the victim: an NPC victim goes to COMBAT_FOCUS on the object), then the hit runs (0x4B2F60: the victim's taking-damage script,
    /// the damage step, the damage effect, the experience of a kill) and the attack end (0x4B6930, the hit effect of a PC or NPC victim). ARC-TECHNOLOGY-1635: with a host that flies the missile (<see cref="LaunchTrapShot"/>) the strike is <see cref="StrikeTrapShot"/>, where the missile meets the victim; without one the strike follows the dispatcher at once (the spell, then the event, then the damage).
    /// </summary>
    private SessionTrapVictim TrapShot(TrapEffect effect, SessionTrapFiring firing, string victim)
    {
        if (TrapEffectRules.Shooter(firing.Shooters, effect.ShooterTag) is not { } shooter)
            return new(victim, SessionTrapHit.NoShooter, 0, null, 0, null, SpellCastAttemptOutcome.Nothing, null);
        // 0x4BCDC0: the slot's draw comes first, then the dispatcher's two d100 rolls (0x4B65A0, the technology-failure roll); a shot's hit is decided (0x100) whatever they give.
        var amount = TrapEffectRules.Amount(effect, _owner.CombatRolls);
        _owner.CombatRolls.Roll(CombatRollPurpose.HitLocation, 1, 100);
        _owner.CombatRolls.Roll(CombatRollPurpose.TechnologyFailure, 1, 100);
        // ARC-TECHNOLOGY-1635 (0x4B3170 after the hit is decided, TT15): a PC victim's armed critical-miss fate (0x4F5270 kind 3, used up) turns the hit into a critical miss; the shot's critical-miss procedure (0x4B6410: no
        // weapon, the shooter no critter, so its one d100 and no effect, 0x4B5F40 refuses a non-critter) makes the shooter the target, and the ranged path flies the missile to the shooter's own tile: it pops there at once, so
        // the victim is not struck, no event goes out and no damage comes. The trap's spell is cast as the dispatcher returns all the same.
        var missed = victim == Hero && _owner.HeroFateArmed(FateKind.CriticalMiss) && _owner.UseHeroFate(_owner.Version, FateKind.CriticalMiss).Disposition == GameSessionDisposition.Changed;
        if (missed) _owner.CombatRolls.Roll(CombatRollPurpose.CriticalSelfHit, 1, 100);
        // The spell is cast when the dispatcher returns (0x4BD06E), before the missile reaches the victim.
        var (spell, run) = effect.CastsSpell ? CastWithoutCaster(effect.Spell, victim) : (SpellCastAttemptOutcome.Nothing, null);
        if (missed) return new(victim, SessionTrapHit.Missed, amount, null, 0, shooter, spell, run);
        // ARC-TECHNOLOGY-1635 (0x4B24F0): the missile leaves the shooter's tile for the victim's location, with the amount in its field 0x4F; the event and the damage come where it meets the victim.
        if (LaunchTrapShot is { } launcher && _map.Object(shooter)?.Location is { } from && _map.Object(victim)?.Location is { } to && _owner.BeginTrapShot(shooter, victim, amount) is { } shot)
        {
            if (launcher(new(shot, shooter, victim, new(checked((int)from.X), checked((int)from.Y)), new(checked((int)to.X), checked((int)to.Y)))))
                return new(victim, SessionTrapHit.InFlight, amount, null, 0, shooter, spell, run);
            _owner.EndShot(shot);
        }
        // No flight: the projectile's end follows the dispatcher at once.
        var (hit, step, hitPoints) = ShotStrike(effect, shooter, victim, amount);
        return new(victim, hit, amount, step, hitPoints, shooter, spell, run);
    }

    /// <summary>
    /// ARC-TECHNOLOGY-1635 (TT15, 0x4B2870 → 0x4B2690 → 0x4B2F60): the strike of a trap's shot in flight (<see cref="GameSessionOwner.QueryTrapShot"/>) on <paramref name="struck"/>, the object the missile met: the victim it
    /// was shot at (where it stands on the line), or another critter it met when it flew past (a stray strike). The shot ends with the strike. Null for a shot not in flight. The strike is as the shot without a flight:
    /// the attack event of the shooter object goes out (<see cref="AiNotice"/>), the victim's taking-damage script is asked, the damage step takes the amount in the normal slot, and the hit effect of a PC or NPC victim
    /// runs. The context names the shooter as the damage source, so the player credit of a PC-placed trap is lost (TT15). The result's <see cref="SessionTrapVictim.Spell"/> is nothing: the spell went with the shot.
    /// </summary>
    public SessionTrapVictim? StrikeTrapShot(SessionShot shot, string struck)
    {
        ArgumentNullException.ThrowIfNull(struck);
        if (_owner.QueryTrapShot(shot) is not { } facts) return null;
        _owner.EndShot(shot);
        var effect = TrapEffectRules.Of(TrapShotScript, 0) ?? throw new InvalidOperationException("TRAP_SHOT_SCRIPT");
        var (hit, step, hitPoints) = ShotStrike(effect, facts.Shooter, struck, facts.Amount);
        return new(struck, hit, facts.Amount, step, hitPoints, facts.Shooter, SpellCastAttemptOutcome.Nothing, null);
    }

    private const int TrapShotScript = 30002;

    // The projectile's end (0x4B2690): the attack event of the shooter object, then the hit (0x4B2F60) and the attack end (0x4B6930).
    private (SessionTrapHit Hit, AttackDamage? Step, int HitPoints) ShotStrike(TrapEffect effect, string shooter, string victim, int amount)
    {
        AiNotice?.Invoke(shooter, victim);
        var (hit, step, hitPoints) = TrapDamage(effect, amount, victim, shooter, killer: null, hitEffectFirst: false);
        // 0x4B6930 runs after the hit whatever the damage step did: for a PC or NPC victim the damage effect (a positive amount) or the critter hit (none) asks an entangled victim's save again. A veto and an invulnerable
        // target leave the slots as drawn (0x4B4390 returns early), a nigh-invulnerable critter's are zeroed.
        if (Kind(new SpellRunObject(victim, _map.Object(victim)?.Type ?? 0)) == AttackTargetKind.Critter)
        {
            var positive = hit switch
            {
                SessionTrapHit.Vetoed => amount > 0,
                SessionTrapHit.Prevented => step!.Prevented != DamagePrevention.NighInvulnerable && amount > 0,
                _ => step is { Final: var final } && (final.Normal > 0 || final.Poison > 0 || final.Electrical > 0 || final.Fire > 0),
            };
            if (positive) DamageEffect(victim); else CritterHit(victim);
        }
        return (hit, step, hitPoints);
    }

    private (SessionTrapHit Hit, int HitPoints) ApplyTrapDamage(string victim, AttackTargetKind kind, AttackDamage step, string trap, string? killer, bool hitEffectFirst)
    {
        if (kind != AttackTargetKind.Critter)
            return _map.DamageObject(victim, step) ? (SessionTrapHit.Damaged, step.HitPointDamage) : (SessionTrapHit.NotApplied, 0);
        if (LifeTarget(victim) is not { } who || LifeDamage.FromStep(step) is not { } damage) return (SessionTrapHit.NotApplied, 0);
        // TT13: the trapped object is the attacker, the player of a PC-placed trap the damage source.
        var maxHitPoints = Life(victim)?.Status.MaxHitPoints ?? 1;
        // ARC-MAGIC-1143 (RH4): the trap's context flag 0x80000 runs the attack end (0x4B6930) inside the damage step, after the resistance step and before the dead check: the hit effect asks an entangled victim's save again.
        if (hitEffectFirst) TrapDamageHit(victim, step);
        var applied = _owner.ApplyDamage(_owner.Version, who, damage, new(trap, killer));
        _itemEvents.AddRange(applied.ItemEvents);
        // ARC-DEV-295 (XP9, XP10): the damage source (+0x30), a PC-placed trap's player, is the killer for the pool share and the kill award.
        if (applied.Outcome is { } lifeStep)
        {
            QueueDamageStep(victim, step, lifeStep, trap);
            RecordDamageExperience(victim, lifeStep, maxHitPoints, killer, -1, applied.DeathStopped);
        }
        // ARC-DEV-292: the experience is the damage step's before its AI pass (0x4B52FC).
        Struck(victim, applied);
        return applied.Disposition == GameSessionDisposition.Changed && applied.Outcome is { } outcome
            ? (SessionTrapHit.Damaged, outcome.HitPointDamage) : (SessionTrapHit.NotApplied, 0);
    }

    // A spell with no parent and no source at the target object (0x455A20 with no source, then 0x455AC0): the invocation check and the
    // run; the run's BEGIN comes as an event (a run without a parent has no casting goal).
    private (SpellCastAttemptOutcome Outcome, int? Run) CastWithoutCaster(int spell, string target)
    {
        if (_records(spell) is not { } record || Facts(target) is not { } to) return (SpellCastAttemptOutcome.Nothing, null);
        var invocation = new SpellInvocation(spell, null, null, to, SpellLocation.None);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, InvocationWorld(), _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run) return (SpellCastAttemptOutcome.Refused, null);
        _itemChains.Remove(run.Id);
        return (SpellCastAttemptOutcome.Started, run.Id);
    }
}
