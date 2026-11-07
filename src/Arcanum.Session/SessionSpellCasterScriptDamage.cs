using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>What a script's damage did to one victim (<see cref="SessionSpellCaster.ApplyScriptDamage"/>): whether the life or the object took it, the hit points taken, and the life's outcome (its death, for the host's fall).</summary>
public readonly record struct SessionScriptHit(bool Applied, int HitPointDamage, LifeOutcome? Outcome, AttackDamage? Step);

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-DEV-243 (MIG13 S14g, GT194–GT195): a script's damage (60) or apply unresistable damage (138) to <paramref name="victim"/>: the shared damage apply (0x4B4390) with a fresh context (no attacker) holding
    /// <paramref name="amount"/> in the damage type <paramref name="type"/> (0 normal, 1 poison, 2 electrical, 3 fire, 4 fatigue). The damage step runs as for any damage: resistance (skipped for the unresistable damage, the only
    /// difference of 138), invulnerability and the walls; no armour step, no difficulty scaling (that is for a PC attacker) and no critical effect, so no stun or knock-down (a knock-down comes only from fatigue damage through the
    /// life step); a negative amount heals a critter. The life takes it as the burn's does (a death starts from the session's life step with no killer). A type outside 0..4 does nothing (the original does not check it and
    /// overwrites other fields of the context; a project choice). NOT READY: the victim's script at attachment point 0x17 that can cancel the damage, the fire damage to the worn armour (+10), the leader notification of a follower falling below 20 %, the floaters and a blood effect.
    /// </summary>
    public SessionScriptHit ApplyScriptDamage(string victim, int amount, int type, bool unresistable)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (type is < 0 or > 4 || _map.Object(victim) is null && victim != Hero) return new(false, 0, null, null);
        // The original does not check the amount; the damage step does (+-100 000), so a larger script amount is cut to it (a project choice).
        amount = Math.Clamp(amount, -AttackResolver.MaxMagnitude, AttackResolver.MaxMagnitude);
        var who = new SpellRunObject(victim, victim == Hero ? 0 : _map.Object(victim)?.Type ?? 0);
        var kind = Kind(who);
        var step = AttackResolver.DamageStep(new()
        {
            Attacker = new Combatant(), Target = Combatant(who), TargetKind = kind, Difficulty = Difficulty,
            Damage = DamageByType.Zero.With((DamageType)type, amount),
            Weapon = unresistable ? new CombatWeapon { Kind = WeaponKind.Weapon, IgnoresResistance = true } : CombatWeapon.Unarmed(),
        });
        if (step.Prevented != DamagePrevention.None) return new(false, 0, null, step);
        if (kind != AttackTargetKind.Critter) return new(_map.DamageObject(victim, step), step.HitPointDamage, null, step);
        if (LifeTarget(victim) is not { } target || LifeDamage.FromStep(step) is not { } packet) return new(false, 0, null, step);
        var applied = _owner.ApplyDamage(_owner.Version, target, packet, new(null, null));
        _itemEvents.AddRange(applied.ItemEvents);
        FirePendingItems();
        return applied.Disposition == GameSessionDisposition.Changed && applied.Outcome is { } outcome ? new(true, outcome.HitPointDamage, outcome, step) : new(false, 0, null, step);
    }
}
