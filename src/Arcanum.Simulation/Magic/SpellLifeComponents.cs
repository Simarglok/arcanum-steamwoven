using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Magic;

/// <summary>
/// The world a Damage or Heal component asks (ARC-MAGIC-012): an object's combat facts and kind for the damage step, the weapon
/// the combat context carries for an attacker (the original's 0x4B2210: a critter's wielded weapon, unarmed without one), the
/// game difficulty, the target's current hit points and fatigue, and a critter's worn items for the acid.
/// </summary>
public interface ISpellDamageWorld
{
    Combatant Combatant(SpellRunObject who);
    AttackTargetKind Kind(SpellRunObject who);
    CombatWeapon WieldedWeapon(SpellRunObject who);
    GameDifficulty Difficulty { get; }
    int CurrentHitPoints(SpellRunObject who);
    int CurrentFatigue(SpellRunObject who);
    IReadOnlyList<SpellAcidItem> WornItems(SpellRunObject critter);
}

/// <summary>One damage step for the host to apply: the object, the step's result and, for a critter, its life damage (null when
/// prevented). An acid's worn items are objects too; the host applies an object's damage through its object rules.</summary>
public sealed record SpellDamageStep(string Object, AttackDamage Damage, LifeDamage? Life);

/// <summary>
/// A Damage component's result: the packet, the steps in order (none when cancelled), and the object that counts as the damage's
/// source for experience and the AI (the context's +0x30: the run's other object, else its parent).
/// </summary>
public sealed record SpellDamageResult(SpellDamagePacket Packet, IReadOnlyList<SpellDamageStep> Steps, SpellRunObject? Source);

/// <summary>
/// ARC-MAGIC-012: the Damage and Heal components behind the run system's component port (<see cref="ISpellRunHost.Apply"/>), for
/// any critter or object. They return what the host applies — the life damage through the session's damage step, the heal
/// through its heal with the target's raise script as the veto — so the host keeps its other duties: the AI's notice of the
/// attack (0x453F20), the experience share, the item effects (a dropped weapon, armour wear) and the damage's eye candy.
/// </summary>
public static class SpellLifeComponents
{
    /// <summary>A Damage component on <paramref name="target"/>: the packet (<see cref="SpellDamageRules.Damage"/>) and its steps
    /// through <see cref="AttackResolver.DamageStep"/> with the run's parent as the attacker and, in the original, its wielded
    /// weapon (MG36); the full and death flags take the current hit points (MG35 in the Steamwoven setting: no difficulty share).</summary>
    public static SpellDamageResult Damage(SpellDamageComponent component, SpellRun run, SpellRunObject target, ISpellDamageWorld world,
        ISpellRollSource rolls, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var packet = SpellDamageRules.Damage(component, run, world.CurrentFatigue(target), rolls, profile);
        var source = SpellDamageRules.Source(run);
        if (packet.Route == SpellDamageRoute.Cancelled) return new(packet, [], source);
        var attacker = run.Parent is { } parent ? world.Combatant(parent) : new Combatant();
        var weapon = run.Parent is { } holder && profile.SpellDamageUsesCastersWeapon ? world.WieldedWeapon(holder) : CombatWeapon.Unarmed();
        SpellDamageStep Step(SpellRunObject on, DamageByType damage)
        {
            var kind = world.Kind(on);
            var step = AttackResolver.DamageStep(new()
            {
                Attacker = attacker, Target = world.Combatant(on), TargetKind = kind, Weapon = weapon, Difficulty = world.Difficulty,
                Damage = damage, Effects = packet.Effects, FullAmount = packet.FullAmount ? world.CurrentHitPoints(on) : null,
                DifficultyScalesFullAmount = profile.DifficultyScalesKillDamage,
            });
            return new(on.Id, step, kind == AttackTargetKind.Critter ? LifeDamage.FromStep(step, HitLocation.Torso, packet.PlainDeath) : null);
        }
        if (packet.Route == SpellDamageRoute.Step) return new(packet, [Step(target, packet.Damage)], source);
        // The acid (0x4B5810): its plan gives the items and whether the wearer takes a share; the amounts chain through the steps
        // themselves, as the original's step lowers the context's amounts in place (none for a prevented step, and none when the
        // context's weapon ignores resistance, MG36).
        var acid = SpellDamageRules.Acid(packet, target.IsCritter, target.IsCritter ? world.WornItems(target) : [], profile);
        var steps = new List<SpellDamageStep>();
        var amount = packet.Damage;
        foreach (var (item, _) in acid.Items)
        {
            var step = Step(new SpellRunObject(item.Id, 0), amount);
            if (step.Damage.Prevented == DamagePrevention.None) amount = step.Damage.Final;
            steps.Add(step);
        }
        if (acid.Wearer is not null) steps.Add(Step(target, amount with { Normal = amount.Normal / 3 }));
        return new(packet, steps, source);
    }

    /// <summary>A Heal component (<see cref="SpellDamageRules.Heal"/>): what the host's heal applies to the target.</summary>
    public static LifeHeal Heal(SpellDamageComponent component, SpellRun run, ISpellRollSource rolls) => SpellDamageRules.Heal(component, run, rolls);
}
