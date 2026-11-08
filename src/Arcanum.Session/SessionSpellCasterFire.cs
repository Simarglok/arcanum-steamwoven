using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>What one burn did to one victim (<see cref="SessionSpellCaster.ApplyBurn"/>): the damage step it drew, and the hit points the life took (0 for a prevented, a dead or an unknown victim).</summary>
public readonly record struct SessionBurnHit(bool Applied, int HitPointDamage, AttackDamage? Step, bool Vetoed = false);

public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// ARC-DEV-144 (MIG13 S9c, GT94–GT97): one burn of the burning object's damage (0x42A720 → 0x4B4390) to <paramref name="victim"/>: the damage step of <paramref name="damage"/> with the burning object as a combatant with no stats and no
    /// attacker party (resistance, armour and the difficulty rule as for any damage), applied to the critter's life (a stun, a fall or a death starts from the session's life step) or, for another object, through the map. A dead critter
    /// takes nothing (0x4B4784). ARC-DEV-261 (GT94, GT198): the victim's taking-damage script (point 0x17) is asked first (<see cref="TakingDamage"/>) with the burning object <paramref name="burner"/> as the attacker (null: unknown) and can cancel the burn (<see cref="SessionBurnHit.Vetoed"/>). The burn awards no experience and rouses no hostility with no source (goal 77's; the eye candy's source is not passed yet).
    /// </summary>
    public SessionBurnHit ApplyBurn(string victim, DamageByType damage, string? burner = null)
    {
        ArgumentNullException.ThrowIfNull(victim);
        if (TakingDamage is { } asks && !asks(victim, burner)) return new(false, 0, null, Vetoed: true);
        var who = new SpellRunObject(victim, victim == Hero ? 0 : _map.Object(victim)?.Type ?? 0);
        var kind = Kind(who);
        var step = AttackResolver.DamageStep(new()
        {
            Attacker = new Combatant(), Target = Combatant(who), TargetKind = kind, Difficulty = Difficulty, Damage = damage,
        });
        if (step.Prevented != DamagePrevention.None) return new(false, 0, step);
        if (kind != AttackTargetKind.Critter) return new(_map.DamageObject(victim, step), step.HitPointDamage, step);
        if (LifeTarget(victim) is not { } target || LifeDamage.FromStep(step) is not { } packet) return new(false, 0, step);
        var applied = _owner.ApplyDamage(_owner.Version, target, packet, new(null, null));
        _itemEvents.AddRange(applied.ItemEvents);
        FirePendingItems();
        Struck(victim, applied);
        return applied.Disposition == GameSessionDisposition.Changed && applied.Outcome is { } outcome ? new(true, outcome.HitPointDamage, step) : new(false, 0, step);
    }
}
