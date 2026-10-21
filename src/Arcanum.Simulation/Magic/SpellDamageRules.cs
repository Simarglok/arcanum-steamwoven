using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Magic;

/// <summary>
/// A Damage or Heal component's data (the spell list's <c>DmgType:</c> index — 0 normal, 1 poison, 2 electrical, 3 fire, 4 the
/// "magic" fatigue slot, 5 acid (Damage only) —, <c>Dmg:</c> minimum and maximum, and the <c>Dmg_Flags:</c> bits of
/// <see cref="SpellDamageFlags"/>). The reader's <c>DamageData</c> maps to it one to one.
/// </summary>
public sealed record SpellDamageComponent(int DamageType, int Minimum, int Maximum, int Flags);

/// <summary>The <c>Dmg_Flags:</c> bits the components read (the original's damage-flags word). The data's <c>Reanimate</c> sets
/// <see cref="Reanimate"/> together with <see cref="Raise"/> (0x400002); <c>Crippling</c> sets the three cures and injuries
/// 0x200, 0x400 and 0x800.</summary>
public static class SpellDamageFlags
{
    public const int Full = 0x1, Raise = 0x2, Death = 0x4, Stunned = 0x40, Blind = 0x200, CrippleArm = 0x400, CrippleLeg = 0x800,
        Scars = 0x1000, DropWeapon = 0x2000, Reanimate = 0x400000, Scaled = 0x800000;
}

/// <summary>Where a Damage component goes: the damage step, the acid's steps (type 5), or cancelled (the death flag with a
/// resistance margin).</summary>
public enum SpellDamageRoute { Step, Acid, Cancelled }

/// <summary>
/// What a Damage component hands the damage step (0x4518D0): the route, the amount per type (one slot filled) and the flags
/// after the component's own (<see cref="SpellDamageFlags.Scaled"/> cleared, <see cref="SpellDamageFlags.Full"/> cleared for the
/// fatigue slot).
/// </summary>
public sealed record SpellDamagePacket(SpellDamageRoute Route, DamageByType Damage, int Flags)
{
    /// <summary>The full or death flag: the step's amount is the target's current hit points.</summary>
    public bool FullAmount => (Flags & (SpellDamageFlags.Full | SpellDamageFlags.Death)) != 0;
    /// <summary>The death flag: a death falls with the plain animation.</summary>
    public bool PlainDeath => (Flags & SpellDamageFlags.Death) != 0;
    /// <summary>The flags that are life effects (<see cref="SpellDamageRules.Effects"/>).</summary>
    public CriticalHitEffects Effects => SpellDamageRules.Effects(Flags);
}

/// <summary>One item worn by an acid's target, in slot order (1000..1008): its id, whether the damage step leaves it alone (a
/// destroyed or invulnerable object, object flags 0x400001) and its normal resistance (the object's, 0..95).</summary>
public sealed record SpellAcidItem(string Id, int Slot, bool Untouched, int NormalResistance);

/// <summary>An acid's steps (0x4B5810): each worn item with the damage it receives (the normal amount, lowered by the resistance of
/// every item before it), then the wearer's share (a third of what is left) unless spared. <see cref="SpellLifeComponents"/> takes
/// the items and the wearer's share from it and chains the amounts through the damage steps themselves (the step's own
/// resistance, which a weapon that ignores resistance skips).</summary>
public sealed record SpellAcidPlan(IReadOnlyList<(SpellAcidItem Item, DamageByType Damage)> Items, DamageByType? Wearer);

/// <summary>
/// ARC-MAGIC-012 (magic plan MT12): the original's Damage (0x4518D0) and Heal (0x4521A0) components and the acid's steps (0x4B5810),
/// as the 1.0.7.4 executable has them (contract docs/world/spell-damage-heal.md, ledger "Spell damage and heal", DH rows). Pure:
/// the damage goes through the combat damage step (<see cref="AttackResolver.DamageStep"/>), the heal through
/// <see cref="LifeRules.Heal"/>; <see cref="SpellLifeComponents"/> runs both over the world's port.
/// </summary>
public static class SpellDamageRules
{
    /// <summary>The Damage component (0x4518D0): with <c>Scaled</c> and a critter parent the amount is
    /// <c>min + (max − min) × aptitude / 100</c> of the parent's aptitude captured at the cast (the minimum at 0 or less; a fate
    /// "spell at maximum" made it 100), no roll; otherwise <c>Dmg:</c> is rolled (an empty range draws nothing). <c>Full</c> on the
    /// fatigue slot is the target's current fatigue + 10; on another slot it leaves the amount for the step, which takes the
    /// current hit points. Acid (type 5) fills the normal slot and goes to the acid's steps without the margin. A resistance
    /// margin cancels a death-flag component and takes <c>margin %</c> (at least 1) from the type's amount (MG37).</summary>
    [OriginalFunction(0x4518D0, OriginalFunctionScope.Partial)]
    public static SpellDamagePacket Damage(SpellDamageComponent component, SpellRun run, int targetFatigue, ISpellRollSource rolls,
        MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        if (component.DamageType is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(component), "DmgType is 0..5.");
        var flags = component.Flags;
        var (min, max) = Scale(component, run, ref flags);
        var type = component.DamageType;
        var damage = DamageByType.Zero;
        if ((flags & SpellDamageFlags.Full) == 0)
            damage = damage.With(type < 5 ? (DamageType)type : DamageType.Normal, Roll(rolls, SpellRollPurpose.Damage, min, max));
        else if (type == (int)DamageType.Fatigue)
        {
            damage = damage with { Fatigue = (int)Math.Clamp(targetFatigue + 10L, int.MinValue, int.MaxValue) };
            flags &= ~SpellDamageFlags.Full;
        }
        if (type == 5) return new(SpellDamageRoute.Acid, damage, flags);
        if (run.Margin != 0)
        {
            if ((flags & SpellDamageFlags.Death) != 0) return new(SpellDamageRoute.Cancelled, damage, flags);
            var slot = (DamageType)type;
            var amount = damage[slot];
            if (amount > 0 || profile.MarginTakesFromNoDamage)
            {
                var taken = (int)((long)amount * run.Margin / 100);
                damage = damage.With(slot, amount - (taken == 0 ? 1 : taken));
            }
        }
        return new(SpellDamageRoute.Step, damage, flags);
    }

    /// <summary>The Heal component (0x4521A0): without <c>Full</c>, the amount as <see cref="Damage"/> scales or rolls it, for the
    /// poison level (type 1), the fatigue (type 4) or the hit points (any other type); with it, no amount (the full heal takes the
    /// hit points, and the fatigue only with a fatigue amount). The flags give the full heal, the raise (<c>Resurrect</c>) and
    /// reanimation, five scars and the three cures. A negative amount heals nothing (the applier heals only positive amounts).</summary>
    [OriginalFunction(0x4521A0, OriginalFunctionScope.Full)]
    public static LifeHeal Heal(SpellDamageComponent component, SpellRun run, ISpellRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rolls);
        if (component.DamageType is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(component), "A heal's DmgType is 0..4.");
        var flags = component.Flags;
        int hitPoints = 0, poison = 0, fatigue = 0;
        if ((flags & SpellDamageFlags.Full) == 0)
        {
            var (min, max) = Scale(component, run, ref flags);
            var amount = Math.Clamp(Roll(rolls, SpellRollPurpose.Heal, min, max), 0, LifeLimits.MaxAmount);
            switch (component.DamageType)
            {
                case 1: poison = amount; break;
                case 4: fatigue = amount; break;
                default: hitPoints = amount; break;
            }
        }
        return new LifeHeal
        {
            HitPoints = hitPoints, Poison = poison, Fatigue = fatigue, Full = (flags & SpellDamageFlags.Full) != 0,
            Scars = (flags & SpellDamageFlags.Scars) != 0, Blindness = (flags & SpellDamageFlags.Blind) != 0,
            CrippledArms = (flags & SpellDamageFlags.CrippleArm) != 0, CrippledLegs = (flags & SpellDamageFlags.CrippleLeg) != 0,
            Resurrect = (flags & SpellDamageFlags.Raise) != 0, Reanimate = (flags & SpellDamageFlags.Reanimate) == SpellDamageFlags.Reanimate,
        };
    }

    /// <summary>The acid's steps (0x4B5810) for a critter target: each item worn in slots 1000..1008 takes the normal amount, which
    /// that item's resistance then lowers for the next one (the step changes the context's amounts in place); the wearer takes a
    /// third (toward zero) of what is left — always in the original (MG38: its body-armour test never holds), only when it wears no
    /// body armour (slot 1006) in Steamwoven. Any other target takes a third at once.</summary>
    [OriginalFunction(0x4B5810, OriginalFunctionScope.Partial)]
    public static SpellAcidPlan Acid(SpellDamagePacket packet, bool targetIsCritter, IReadOnlyList<SpellAcidItem> worn, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(profile);
        var damage = packet.Damage;
        var items = new List<(SpellAcidItem, DamageByType)>();
        var armour = false;
        if (targetIsCritter)
        {
            foreach (var item in worn.OrderBy(i => i.Slot))
            {
                ArgumentNullException.ThrowIfNull(item);
                if (item.Slot is < 1000 or > 1008) throw new ArgumentOutOfRangeException(nameof(worn), "Worn slots are 1000..1008.");
                items.Add((item, damage));
                if (!item.Untouched && item.NormalResistance > 0)
                    damage = damage with { Normal = damage.Normal - (int)((long)Math.Min(item.NormalResistance, 95) * damage.Normal / 100) };
                if (item.Slot == 1006) armour = true;
            }
        }
        DamageByType? wearer = armour && !profile.AcidIgnoresBodyArmour ? null : damage with { Normal = damage.Normal / 3 };
        return new(items, wearer);
    }

    /// <summary>The object the damage counts as coming from for experience and the AI (the context's +0x30): the run's other
    /// object, else its parent (0x4518D0).</summary>
    public static SpellRunObject? Source(SpellRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Other ?? run.Parent;
    }

    /// <summary>The damage flags that are life effects: stun (0x40), blinding (0x200), an arm (0x400), the legs (0x800), a scar
    /// (0x1000) and the dropped weapon (0x2000, the item owner's).</summary>
    public static CriticalHitEffects Effects(int flags) =>
        ((flags & SpellDamageFlags.Stunned) != 0 ? CriticalHitEffects.Stun : 0)
        | ((flags & SpellDamageFlags.Blind) != 0 ? CriticalHitEffects.Blind : 0)
        | ((flags & SpellDamageFlags.CrippleArm) != 0 ? CriticalHitEffects.CrippleArm : 0)
        | ((flags & SpellDamageFlags.CrippleLeg) != 0 ? CriticalHitEffects.CrippleLeg : 0)
        | ((flags & SpellDamageFlags.Scars) != 0 ? CriticalHitEffects.Scar : 0)
        | ((flags & SpellDamageFlags.DropWeapon) != 0 ? CriticalHitEffects.DropWeapon : 0);

    // Scaled (both components): a critter parent's captured aptitude fixes the amount, no roll; the flag is cleared.
    private static (int Min, int Max) Scale(SpellDamageComponent component, SpellRun run, ref int flags)
    {
        int min = component.Minimum, max = component.Maximum;
        if ((flags & SpellDamageFlags.Scaled) == 0) return (min, max);
        if (run.Parent is { IsCritter: true } parent)
        {
            var value = parent.Aptitude > 0 ? (int)(min + (long)(max - min) * parent.Aptitude / 100) : min;
            (min, max) = (value, value);
        }
        flags &= ~SpellDamageFlags.Scaled;
        return (min, max);
    }

    // The original's random range (0x4CEF00): an empty range draws nothing, a reversed one is swapped.
    private static int Roll(ISpellRollSource rolls, SpellRollPurpose purpose, int a, int b) =>
        a == b ? a : rolls.Roll(purpose, Math.Min(a, b), Math.Max(a, b));
}
