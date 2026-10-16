namespace Arcanum.Simulation.Combat;

/// <summary>
/// The life rules of one critter as pure functions (ARC-COMBAT-002): hit points and fatigue, the dead, unconscious and
/// active predicates, applying damage, a kill, healing, fatigue recovery, resting heal, the two poison ticks and the end
/// of a stun. Numbers and order are the CE reading at <c>a7ff41b300ef712f0e7d088183a3d08957110cdd</c> (see
/// docs/world/life-state-runtime.md); the fatigue recovery step was also checked in the original 1.0.7.4 executable.
/// Every function validates its inputs (<see cref="ArgumentException"/> for a state, facts or amount out of range) and
/// returns a new state; the only randomness is the knockout and second-arm rolls of <see cref="ApplyDamage"/>.
/// </summary>
public static class LifeRules
{
    private const LifeConditions Crippled = LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth | LifeConditions.CrippledLegsBoth;
    private const LifeConditions AllConditions = LifeConditions.Stunned | LifeConditions.Blinded | Crippled;
    private const LifeTraits AllTraits = (LifeTraits)((1 << 9) - 1);
    private const CriticalHitEffects AllEffects = (CriticalHitEffects)((1 << 11) - 1);

    /// <summary>Strength lost to poison: <c>min(3, poison / 100)</c>.</summary>
    public static int PoisonPenalty(int poisonLevel) => Math.Clamp(poisonLevel / 100, 0, 3);

    /// <summary>Effective strength at a poison level (see <see cref="LifeFacts.PoisonedStrength"/>).</summary>
    public static int StrengthAt(LifeFacts facts, int poisonLevel)
    {
        var penalty = PoisonPenalty(poisonLevel);
        if (penalty == 0) return facts.Strength;
        return facts.PoisonedStrength.IsDefaultOrEmpty ? Math.Max(1, facts.Strength - penalty) : facts.PoisonedStrength[penalty - 1];
    }

    /// <summary>Maximum hit points: <c>4 × points + adjustment + WP + 2 × (ST + level) + 4</c> (effects on the maximum are not modelled).</summary>
    public static int MaxHitPoints(LifeFacts facts, int poisonLevel) =>
        4 * facts.HitPointPoints + facts.HitPointAdjustment + facts.Willpower + 2 * (StrengthAt(facts, poisonLevel) + facts.Level) + 4;

    /// <summary>Maximum fatigue: <c>4 × points + adjustment + 2 × (level + CN) + WP + 4</c>.</summary>
    public static int MaxFatigue(LifeFacts facts) =>
        4 * facts.FatiguePoints + facts.FatigueAdjustment + 2 * (facts.Level + facts.Constitution) + facts.Willpower + 4;

    public static bool IsDead(LifeState state, LifeFacts facts) => MaxHitPoints(facts, state.PoisonLevel) - state.HitPointDamage <= 0;

    public static bool IsUnconscious(LifeState state, LifeFacts facts) =>
        !facts.Traits.HasFlag(LifeTraits.Undead) && MaxFatigue(facts) - state.FatigueDamage <= 0;

    public static LifeStatus Evaluate(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        var maxHp = MaxHitPoints(facts, state.PoisonLevel);
        var maxFatigue = MaxFatigue(facts);
        var dead = IsDead(state, facts);
        var unconscious = IsUnconscious(state, facts);
        var active = !dead && !unconscious && !state.Conditions.HasFlag(LifeConditions.Stunned) &&
            (facts.Traits & (LifeTraits.Paralyzed | LifeTraits.Stoned)) == 0;
        return new(maxHp, maxHp - state.HitPointDamage, maxFatigue, maxFatigue - state.FatigueDamage, dead, unconscious, active);
    }

    /// <summary>
    /// Applies one damage packet (the source's critter damage step). Undead and mechanical critters take no poison, a
    /// stoned one no hit-point, fatigue or poison damage; a dead critter takes nothing (<see cref="LifeEvents.AlreadyDead"/>).
    /// Then, in order: stun (unless a knockdown or knockout comes with it), knockdown, knockout (a critter still conscious
    /// gets fatigue damage <c>max fatigue + 10..20</c>, rolled as <see cref="CombatRollPurpose.KnockoutFatigue"/>), scar,
    /// blinding, an arm (the first, else the second on <see cref="CombatRollPurpose.CrippleBothArms"/> d100 ≤ 50), legs,
    /// hit-point damage (never below 0), fatigue damage (positive, not fatigue-immune), the poison level (0..1000, and 0
    /// whenever the effective constitution is extraordinary), and death: a critter now dead gets the pinned
    /// <see cref="LifeLimits.DeathHitPointDamage"/> and a dying animation. An invulnerable critter takes nothing at all;
    /// nigh-invulnerability depends on the weapon, so it is the packet builder's (<see cref="LifeDamage.FromAttack"/>,
    /// <see cref="PoisonDamage"/>). The dying script is not run (no SCR runtime).
    /// </summary>
    public static LifeOutcome ApplyDamage(LifeState state, LifeFacts facts, LifeDamage damage, ICombatRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(damage);
        ArgumentNullException.ThrowIfNull(rolls);
        Check(state, facts);
        if (!IsValid(damage)) throw new ArgumentException("Damage out of range.", nameof(damage));
        return Damage(state, facts, damage, rolls);
    }

    /// <summary>
    /// A kill (the source's <c>critter_kill</c>): the damage step with the death flag, so a living critter takes its current
    /// hit points and dies with the plain fall, then hit-point damage is pinned at <see cref="LifeLimits.DeathHitPointDamage"/>.
    /// An invulnerable or nigh-invulnerable critter skips the death step but is pinned all the same, as in the source, so
    /// it reads as dead without <see cref="LifeEvents.Died"/>. The step's poison write applies too: when the
    /// extraordinary-constitution reset lifts strength, the critter is not dead at the death check and the death step
    /// does not run either, though the pin follows.
    /// </summary>
    public static LifeOutcome Kill(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        var events = LifeEvents.None;
        var death = DeathAnimation.None;
        var current = 0;
        var s = state;
        if (IsDead(state, facts)) events |= LifeEvents.AlreadyDead;
        else if ((facts.Traits & (LifeTraits.Invulnerable | LifeTraits.NighInvulnerable)) == 0)
        {
            current = MaxHitPoints(facts, state.PoisonLevel) - state.HitPointDamage;
            s = s with { HitPointDamage = AddDamage(s.HitPointDamage, current), PoisonLevel = PoisonLevel(s.PoisonLevel, facts) };
            if (IsDead(s, facts))
            {
                events |= LifeEvents.Died;
                death = DeathAnimation.Plain;
            }
        }
        var after = s with { HitPointDamage = LifeLimits.DeathHitPointDamage };
        return new(state, after, events | Consciousness(state, after, facts), current, death);
    }

    /// <summary>
    /// Heals (the source's heal step). In order: scar removal without <see cref="LifeHeal.Full"/>; a dead critter stops
    /// here unless <see cref="LifeHeal.Resurrect"/>, and a resurrection is refused for an unrevivifiable critter that is
    /// reanimated or an unresurrectable one that is not (the resurrect script is not run); a resurrection clears the stun
    /// and sets fatigue damage to 10. Then the full heal or the single cures (crippled legs heal crippled legs, as in the
    /// original game; CE clears blindness there, anomaly A5), and the hit-point, fatigue and poison amounts, each only
    /// while there is damage to remove.
    /// </summary>
    public static LifeOutcome Heal(LifeState state, LifeFacts facts, LifeHeal heal)
    {
        ArgumentNullException.ThrowIfNull(heal);
        Check(state, facts);
        if (heal.HitPoints is < 0 or > LifeLimits.MaxAmount || heal.Fatigue is < 0 or > LifeLimits.MaxAmount ||
            heal.Poison is < 0 or > LifeLimits.MaxAmount) throw new ArgumentException("Heal out of range.", nameof(heal));
        var s = state;
        var events = LifeEvents.None;
        if (heal.Scars && !heal.Full) s = s with { Scars = Math.Max(0, s.Scars - 5) };
        if (IsDead(s, facts))
        {
            if (!heal.Resurrect) return new(state, s, events | LifeEvents.AlreadyDead, 0, DeathAnimation.None);
            var refused = facts.Traits.HasFlag(LifeTraits.Unrevivifiable) && heal.Reanimate ||
                facts.Traits.HasFlag(LifeTraits.Unresurrectable) && !heal.Reanimate;
            if (refused) return new(state, s, events | LifeEvents.ResurrectionRefused, 0, DeathAnimation.None);
            s = s with { Conditions = s.Conditions & ~LifeConditions.Stunned, FatigueDamage = 10 };
            events |= LifeEvents.Resurrected;
        }

        var hitPoints = heal.HitPoints;
        var fatigue = heal.Fatigue;
        if (heal.Full)
        {
            hitPoints = s.HitPointDamage;
            if (fatigue > 0) fatigue = s.FatigueDamage;
            s = s with { Scars = Math.Max(0, s.Scars - 5), Conditions = s.Conditions & ~(LifeConditions.Blinded | Crippled) };
        }
        else
        {
            var conditions = s.Conditions;
            if (heal.Blindness) conditions &= ~LifeConditions.Blinded;
            if (heal.CrippledArms) conditions &= ~(LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth);
            if (heal.CrippledLegs) conditions &= ~LifeConditions.CrippledLegsBoth;
            s = s with { Conditions = conditions };
        }
        if (hitPoints > 0 && s.HitPointDamage != 0) s = s with { HitPointDamage = Math.Max(0, s.HitPointDamage - hitPoints) };
        if (fatigue > 0 && s.FatigueDamage != 0) s = s with { FatigueDamage = Math.Max(0, s.FatigueDamage - fatigue) };
        if (heal.Poison > 0 && s.PoisonLevel != 0) s = s with { PoisonLevel = PoisonLevel(s.PoisonLevel - (long)heal.Poison, facts) };
        return new(state, s, events | Consciousness(state, s, facts), 0, DeathAnimation.None);
    }

    /// <summary>
    /// Recovery steps of a fatigue recovery event: <c>max(1, (now s − scheduled s) / 80)</c> in whole game seconds (CE
    /// <c>critter_fatigue_timeevent_process</c>; the same arithmetic in the original 1.0.7.4 executable at 0x45E610).
    /// </summary>
    public static int FatigueRecoverySteps(long scheduledAtGameMs, long nowGameMs) => Steps(scheduledAtGameMs, nowGameMs, 80);

    /// <summary>Hours of a resting event: <c>max(1, (now s − scheduled s) / 3600)</c> (CE <c>critter_resting_timeevent_process</c>).</summary>
    public static int RestingHours(long scheduledAtGameMs, long nowGameMs) =>
        Math.Min(Steps(scheduledAtGameMs, nowGameMs, 3600), LifeLimits.MaxRestHours);

    /// <summary>
    /// Fatigue recovery: a living critter with fatigue damage loses <c>steps × HealRate</c> of it (never below 0). The 80-second
    /// event that calls it belongs to the game event queue.
    /// </summary>
    public static LifeOutcome RecoverFatigue(LifeState state, LifeFacts facts, int steps)
    {
        Check(state, facts);
        if (steps is < 1 or > LifeLimits.MaxAmount) throw new ArgumentOutOfRangeException(nameof(steps));
        if (IsDead(state, facts)) return new(state, state, LifeEvents.AlreadyDead, 0, DeathAnimation.None);
        var after = state.FatigueDamage > 0
            ? state with { FatigueDamage = (int)Math.Max(0, state.FatigueDamage - (long)steps * facts.HealRate) }
            : state;
        return new(state, after, Consciousness(state, after, facts), 0, DeathAnimation.None);
    }

    /// <summary>
    /// Resting heal (sleep, and the hourly event of an NPC without a PC leader): a living critter loses
    /// <c>HealRate × hours</c> hit-point damage and three times that fatigue damage (never below 0).
    /// </summary>
    public static LifeOutcome RestHeal(LifeState state, LifeFacts facts, int hours)
    {
        Check(state, facts);
        if (hours is < 0 or > LifeLimits.MaxRestHours) throw new ArgumentOutOfRangeException(nameof(hours));
        if (IsDead(state, facts)) return new(state, state, LifeEvents.AlreadyDead, 0, DeathAnimation.None);
        var heal = (long)facts.HealRate * hours;
        var after = state;
        if (after.HitPointDamage > 0) after = after with { HitPointDamage = (int)Math.Max(0, after.HitPointDamage - heal) };
        if (after.FatigueDamage > 0) after = after with { FatigueDamage = (int)Math.Max(0, after.FatigueDamage - 3 * heal) };
        return new(state, after, Consciousness(state, after, facts), 0, DeathAnimation.None);
    }

    /// <summary>
    /// The 15-second poison damage tick: while the poison level is positive, normal damage 1 (level below 200), 2 (below
    /// 550) or 3, less the normal resistance, through the damage step without a weapon, so an invulnerable or
    /// nigh-invulnerable critter takes nothing and poison can kill (plain fall). The event belongs to the game event queue.
    /// </summary>
    public static LifeOutcome PoisonDamage(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        if (state.PoisonLevel <= 0 || (facts.Traits & (LifeTraits.Invulnerable | LifeTraits.NighInvulnerable)) != 0)
            return new(state, state, LifeEvents.None, 0, DeathAnimation.None);
        var damage = state.PoisonLevel >= 550 ? 3 : state.PoisonLevel >= 200 ? 2 : 1;
        if (facts.NormalResistance > 0) damage -= facts.NormalResistance * damage / 100;
        return Damage(state, facts, new LifeDamage { HitPoints = damage }, NoRolls.Instance);
    }

    /// <summary>The 120-second poison recovery tick: the poison level loses the PoisonRecovery stat (never below 0); dead or not.</summary>
    public static LifeOutcome PoisonRecoveryTick(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        var after = state.PoisonLevel > 0 ? state with { PoisonLevel = PoisonLevel(state.PoisonLevel - (long)facts.PoisonRecovery, facts) } : state;
        return new(state, after, LifeEvents.None, 0, DeathAnimation.None);
    }

    /// <summary>The stun animation ended (or could not start): the stun is cleared.</summary>
    public static LifeOutcome ClearStun(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        if (!state.Conditions.HasFlag(LifeConditions.Stunned)) return new(state, state, LifeEvents.None, 0, DeathAnimation.None);
        return new(state, state with { Conditions = state.Conditions & ~LifeConditions.Stunned }, LifeEvents.StunEnded, 0, DeathAnimation.None);
    }

    /// <summary>True when every amount is within <see cref="LifeLimits.MaxAmount"/> and the effects and location are defined.</summary>
    public static bool IsValid(LifeDamage? damage) =>
        damage is not null && Math.Abs((long)damage.HitPoints) <= LifeLimits.MaxAmount &&
        Math.Abs((long)damage.Fatigue) <= LifeLimits.MaxAmount && Math.Abs((long)damage.Poison) <= LifeLimits.MaxAmount &&
        (damage.Effects & ~AllEffects) == 0 && Enum.IsDefined(damage.Location);

    /// <summary>True when every field is within <see cref="LifeLimits"/> and the conditions are defined.</summary>
    public static bool IsValid(LifeState state) =>
        state.HitPointDamage is >= 0 and <= LifeLimits.MaxDamage && state.FatigueDamage is >= 0 and <= LifeLimits.MaxDamage &&
        state.PoisonLevel is >= 0 and <= LifeLimits.MaxPoisonLevel && (state.Conditions & ~AllConditions) == 0 &&
        state.Scars is >= 0 and <= LifeLimits.MaxScars;

    /// <summary>True when every fact is within <see cref="LifeLimits"/>, the traits are defined and the poisoned strengths, when given, are three.</summary>
    public static bool IsValid(LifeFacts? facts) =>
        facts is not null && facts.HitPointPoints is >= 0 and <= LifeLimits.MaxPoolPoints &&
        facts.FatiguePoints is >= 0 and <= LifeLimits.MaxPoolPoints &&
        Math.Abs((long)facts.HitPointAdjustment) <= LifeLimits.MaxPoolAdjustment &&
        Math.Abs((long)facts.FatigueAdjustment) <= LifeLimits.MaxPoolAdjustment &&
        facts.Level is >= 0 and <= LifeLimits.MaxLevel && Stat(facts.Strength) && Stat(facts.Constitution) && Stat(facts.Willpower) &&
        Stat(facts.HealRate) && Stat(facts.PoisonRecovery) && Math.Abs((long)facts.NormalResistance) <= LifeLimits.MaxResistance &&
        (facts.Traits & ~AllTraits) == 0 &&
        (facts.PoisonedStrength.IsDefaultOrEmpty || facts.PoisonedStrength.Length == 3 && facts.PoisonedStrength.All(Stat));

    private static bool Stat(int value) => value is >= 0 and <= LifeLimits.MaxStat;

    private static void Check(LifeState state, LifeFacts facts)
    {
        if (!IsValid(facts)) throw new ArgumentException("Life facts out of range.", nameof(facts));
        if (!IsValid(state)) throw new ArgumentException("Life state out of range.", nameof(state));
    }

    private static int Steps(long scheduledAtGameMs, long nowGameMs, int period)
    {
        if (scheduledAtGameMs < 0 || nowGameMs < scheduledAtGameMs) throw new ArgumentOutOfRangeException(nameof(scheduledAtGameMs));
        var steps = (nowGameMs / 1000 - scheduledAtGameMs / 1000) / period;
        return (int)Math.Clamp(steps, 1, LifeLimits.MaxAmount);
    }

    // The source's poison setter: 0..1000, and 0 for a critter with extraordinary constitution.
    private static int PoisonLevel(long value, LifeFacts facts) =>
        facts.Constitution >= 20 ? 0 : (int)Math.Clamp(value, 0, LifeLimits.MaxPoisonLevel);

    private static int AddDamage(int current, long amount) => (int)Math.Clamp(current + amount, 0, LifeLimits.MaxDamage);

    private static LifeOutcome Damage(LifeState state, LifeFacts facts, LifeDamage damage, ICombatRollSource rolls)
    {
        long hitPoints = damage.HitPoints, fatigue = damage.Fatigue, poison = damage.Poison;
        if ((facts.Traits & (LifeTraits.Undead | LifeTraits.Mechanical)) != 0) poison = 0;
        else if (facts.Traits.HasFlag(LifeTraits.Stoned)) hitPoints = fatigue = poison = 0;
        // The source checks the invulnerable flag before the dead check.
        if (facts.Traits.HasFlag(LifeTraits.Invulnerable)) return new(state, state, LifeEvents.None, 0, DeathAnimation.None);
        if (IsDead(state, facts)) return new(state, state, LifeEvents.AlreadyDead, 0, DeathAnimation.None);

        var s = state;
        var events = LifeEvents.None;
        var effects = damage.Effects;
        var conditions = s.Conditions;
        const CriticalHitEffects Down = CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout;
        if (effects.HasFlag(CriticalHitEffects.Stun) && !conditions.HasFlag(LifeConditions.Stunned) && (effects & Down) == 0)
        {
            conditions |= LifeConditions.Stunned;
            events |= LifeEvents.Stunned;
        }
        if ((effects & Down) != 0)
        {
            events |= LifeEvents.KnockedDown;
            if (effects.HasFlag(CriticalHitEffects.Knockout) && !IsUnconscious(s, facts))
            {
                var extra = rolls.Roll(CombatRollPurpose.KnockoutFatigue, 10, 20);
                s = s with { FatigueDamage = AddDamage(0, (long)MaxFatigue(facts) + extra) };
                events |= LifeEvents.KnockedOut;
            }
        }
        if (effects.HasFlag(CriticalHitEffects.Scar))
        {
            if (s.Scars < LifeLimits.MaxScars)
            {
                s = s with { Scars = s.Scars + 1 };
                events |= LifeEvents.Scarred;
            }
            else events |= LifeEvents.ScarLimitReached;
        }
        if (effects.HasFlag(CriticalHitEffects.Blind) && !conditions.HasFlag(LifeConditions.Blinded))
        {
            conditions |= LifeConditions.Blinded;
            events |= LifeEvents.Blinded;
        }
        if (effects.HasFlag(CriticalHitEffects.CrippleArm))
        {
            if (!conditions.HasFlag(LifeConditions.CrippledArmOne))
            {
                conditions |= LifeConditions.CrippledArmOne;
                events |= LifeEvents.CrippledArm;
            }
            else if (!conditions.HasFlag(LifeConditions.CrippledArmsBoth) && rolls.Roll(CombatRollPurpose.CrippleBothArms, 1, 100) <= 50)
            {
                conditions |= LifeConditions.CrippledArmsBoth;
                events |= LifeEvents.CrippledBothArms;
            }
            events |= LifeEvents.ArmHitDropsItems;
        }
        if (effects.HasFlag(CriticalHitEffects.CrippleLeg) && !conditions.HasFlag(LifeConditions.CrippledLegsBoth))
        {
            conditions |= LifeConditions.CrippledLegsBoth;
            events |= LifeEvents.CrippledLegs;
        }
        s = s with { Conditions = conditions, HitPointDamage = AddDamage(s.HitPointDamage, hitPoints) };
        if (!facts.Traits.HasFlag(LifeTraits.FatigueImmune) && fatigue > 0) s = s with { FatigueDamage = AddDamage(s.FatigueDamage, fatigue) };
        // The source writes the poison level on every damage step, so the extraordinary-constitution reset happens here too.
        s = s with { PoisonLevel = PoisonLevel(s.PoisonLevel + poison, facts) };

        var death = DeathAnimation.None;
        if (IsDead(s, facts))
        {
            death = hitPoints <= 20 ? DeathAnimation.Plain : damage.Location switch
            {
                HitLocation.Head => DeathAnimation.Head,
                HitLocation.Leg => DeathAnimation.Leg,
                _ => DeathAnimation.Body,
            };
            s = s with { HitPointDamage = LifeLimits.DeathHitPointDamage };
            events |= LifeEvents.Died;
        }
        return new(state, s, events | Consciousness(state, s, facts), (int)hitPoints, death);
    }

    // A corpse neither falls unconscious nor gets up.
    private static LifeEvents Consciousness(LifeState before, LifeState after, LifeFacts facts)
    {
        if (IsDead(after, facts)) return LifeEvents.None;
        var was = IsUnconscious(before, facts);
        var now = IsUnconscious(after, facts);
        return was == now ? LifeEvents.None : now ? LifeEvents.LostConsciousness : LifeEvents.RegainedConsciousness;
    }

    private sealed class NoRolls : ICombatRollSource
    {
        internal static readonly NoRolls Instance = new();

        public int Roll(CombatRollPurpose purpose, int min, int max) =>
            throw new InvalidOperationException("A damage step without critical effects makes no roll.");
    }
}
