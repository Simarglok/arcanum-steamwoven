using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>What a script's damage did to one victim (<see cref="SessionSpellCaster.ApplyScriptDamage"/>): whether the life or the object took it, the hit points taken, and the life's outcome (its death, for the host's fall).</summary>
public readonly record struct SessionScriptHit(bool Applied, int HitPointDamage, LifeOutcome? Outcome, AttackDamage? Step, bool Vetoed = false);

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-DEV-254 (MIG13 S14j, GT198–GT203): the host's taking-damage script (attachment point 0x17) of a victim, asked by the damage apply (0x4B4390) before its destroyed, invulnerable and wall gates and before resistance: the victim and the damage's
    /// attacker (null: none) in, whether the damage goes on out (false: the script skipped the default, which cancels the whole step). Null: no host, the damage goes on.
    /// </summary>
    public Func<string, string?, bool>? TakingDamage { get; set; }

    /// <summary>
    /// ARC-MAGIC-1167 (MA-10, ledger RZ1–RZ3): the host's raise script (attachment point 0x12) of a dead target, asked by a Heal component with the raise bit (<see cref="LifeHeal.Resurrect"/>) before the life step: the target and the heal's parent
    /// (the healer, null: none) in, whether the raise goes on out (false: the script skipped the default, which refuses the raise). Null: no host, the raise goes on.
    /// </summary>
    public Func<string, string?, bool>? RaiseScript { get; set; }

    /// <summary>
    /// ARC-DEV-243 (MIG13 S14g, GT194–GT195): a script's damage (60) or apply unresistable damage (138) to <paramref name="victim"/>: the shared damage apply (0x4B4390) with a fresh context (no attacker) holding
    /// <paramref name="amount"/> in the damage type <paramref name="type"/> (0 normal, 1 poison, 2 electrical, 3 fire, 4 fatigue). The damage step runs as for any damage: resistance (skipped for the unresistable damage, the only
    /// difference of 138), invulnerability and the walls; no armour step, no difficulty scaling (that is for a PC attacker) and no critical effect, so no stun or knock-down (a knock-down comes only from fatigue damage through the
    /// life step); a negative amount heals a critter. The life takes it as the burn's does (a death starts from the session's life step with no killer). A type outside 0..4 does nothing (the original does not check it and
    /// overwrites other fields of the context; a project choice). The fire damage to the worn armour (+10, <see cref="GameSessionOwner.ApplyArmorWear"/>) and the follower's near-death comment (<c>FollowerRules.NearDeathOfHit</c>) are the host's, after this step (ARC-DEV-245). The victim's script at attachment point 0x17 is asked first (<see cref="TakingDamage"/>, ARC-DEV-254), before any gate or step, and can cancel the damage. The floaters are the host's (the step is queued, ARC-DEV-296, <see cref="TakeDamageSteps"/>). NOT READY: a blood effect.
    /// </summary>
    public SessionScriptHit ApplyScriptDamage(string victim, int amount, int type, bool unresistable)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (type is < 0 or > 4 || _map.Object(victim) is null && victim != Hero) return new(false, 0, null, null);
        // GT198: the victim's taking-damage script comes before every gate and step; a script that skips the default cancels the damage, with no attacker (a script's own damage).
        if (TakingDamage is { } asks && !asks(victim, null)) return new(false, 0, null, null, Vetoed: true);
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
        if (applied.Outcome is { } scripted) QueueDamageStep(victim, step, scripted, null, fromScript: true);
        FirePendingItems();
        Struck(victim, applied);
        return applied.Disposition == GameSessionDisposition.Changed && applied.Outcome is { } outcome ? new(true, outcome.HitPointDamage, outcome, step) : new(false, 0, null, step);
    }
}
