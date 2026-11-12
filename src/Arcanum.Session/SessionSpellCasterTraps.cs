using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
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
    /// <summary>A shot (30002) found its shooter; the shot itself (the attack dispatcher 0x4B3170 with a non-critter attacker) is
    /// not ready, so nothing is applied yet: no amount is drawn and its spell is not cast, which the shot slice adds together.</summary>
    ShotNotReady,
    /// <summary>A shot found no shooter: nothing happens to the victim, no spell either.</summary>
    NoShooter,
}

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
    /// ARC-WORLD-107: the effect of a native trap script the session fired (0x4BCDC0 for each victim of 0x4BCB70, ledger TT8,
    /// TT11–TT14). The victims are every PC and NPC of the map in the run's square around the trapped object (the map's square
    /// query), or the triggerer alone for a run of radius 0 (none without a triggerer). For each victim in order: a damage
    /// (30001, 30003..30006) draws its amount and takes the damage step (<see cref="AttackResolver.DamageStep"/>, the same as a
    /// spell's) with the trapped object as the attacker and its own weapon (a non-weapon, so no weapon flag counts), the player as the
    /// damage source of a PC-placed trap, applied to the critter's life or through the map to an object; a shot (30002) looks for its
    /// shooter (<see cref="TrapEffectRules.Shooter"/>) and stops at <see cref="SessionTrapHit.ShotNotReady"/> with nothing applied;
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
        var killer = TrapEffectRules.CreditsPlayer(firing.ObjectFlags) ? Hero : null;
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
        var (hit, amount, step, hitPoints) = (SessionTrapHit.None, 0, (AttackDamage?)null, 0);
        if (effect.Kind == TrapEffectKind.Shot)
        {
            var shooter = TrapEffectRules.Shooter(firing.Shooters, effect.ShooterTag);
            return new(victim, shooter is null ? SessionTrapHit.NoShooter : SessionTrapHit.ShotNotReady, 0, null, 0, shooter,
                SpellCastAttemptOutcome.Nothing, null);
        }
        else if (effect.Kind == TrapEffectKind.Damage)
        {
            amount = TrapEffectRules.Amount(effect, _owner.CombatRolls);
            // ARC-DEV-261 (GT198): the victim's taking-damage script comes first, before any gate or step; the trap is the damage's attacker (TT13). The amount is drawn already.
            // A veto cancels the damage step only; the trap's spell (a separate component of the effect) is cast all the same.
            if (TakingDamage is { } asks && !asks(victim, firing.Trap)) hit = SessionTrapHit.Vetoed;
            else
            {
                var who = new SpellRunObject(victim, _map.Object(victim)?.Type ?? 0);
                var kind = Kind(who);
                step = AttackResolver.DamageStep(new()
                {
                    Attacker = new Combatant(), Target = Combatant(who), TargetKind = kind, Difficulty = Difficulty,
                    Damage = TrapEffectRules.Damage(effect, amount),
                });
                (hit, hitPoints) = step.Prevented != DamagePrevention.None ? (SessionTrapHit.Prevented, 0) : ApplyTrapDamage(victim, kind, step, firing.Trap, killer);
                FirePendingItems();
            }
        }
        var (spell, run) = effect.CastsSpell ? CastWithoutCaster(effect.Spell, victim) : (SpellCastAttemptOutcome.Nothing, null);
        return new(victim, hit, amount, step, hitPoints, null, spell, run);
    }

    private (SessionTrapHit Hit, int HitPoints) ApplyTrapDamage(string victim, AttackTargetKind kind, AttackDamage step, string trap, string? killer)
    {
        if (kind != AttackTargetKind.Critter)
            return _map.DamageObject(victim, step) ? (SessionTrapHit.Damaged, step.HitPointDamage) : (SessionTrapHit.NotApplied, 0);
        if (LifeTarget(victim) is not { } who || LifeDamage.FromStep(step) is not { } damage) return (SessionTrapHit.NotApplied, 0);
        // TT13: the trapped object is the attacker, the player of a PC-placed trap the damage source.
        var maxHitPoints = Life(victim)?.Status.MaxHitPoints ?? 1;
        // ARC-MAGIC-1143 (RH4): the trap's context flag 0x80000 runs the attack end (0x4B6930) inside the damage step, after the resistance step and before the dead check: the hit effect asks an entangled victim's save again.
        TrapDamageHit(victim, step);
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
