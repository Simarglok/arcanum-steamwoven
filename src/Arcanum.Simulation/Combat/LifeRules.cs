using System.Collections.Immutable;
using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// The life rules of one critter as pure functions (ARC-COMBAT-002): hit points and fatigue, the dead, unconscious and
/// active predicates, applying damage, a kill, healing, fatigue recovery, resting heal, the two poison ticks and the end
/// of a stun. Numbers and order are the claims of
/// docs/world/life-state-runtime.md (F0); the fatigue recovery step was also checked in the original 1.0.7.4 executable.
/// Every function validates its inputs (<see cref="ArgumentException"/> for a state, facts or amount out of range) and
/// returns a new state; the only randomness is the knockout and second-arm rolls of <see cref="ApplyDamage"/>.
/// </summary>
public static class LifeRules
{
    private const LifeConditions Crippled = LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth | LifeConditions.CrippledLegsBoth;
    private const LifeConditions AllConditions = LifeConditions.Stunned | LifeConditions.Blinded | Crippled;
    private const LifeTraits AllTraits = (LifeTraits)((1 << 10) - 1);
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

    /// <summary>Maximum hit points: <c>4 × points + adjustment + WP + 2 × (ST + level) + 4</c>, then the maximum's effects over
    /// that whole sum (ARC-WORLD-029; 0x43D5A0, checked in the original executable). Facts that fail
    /// <see cref="IsValid(LifeFacts)"/> may throw <see cref="ArgumentOutOfRangeException"/> (an undefined operator, a divide by 0).</summary>
    [OriginalFunction(0x43D5A0, OriginalFunctionScope.Full)]
    public static int MaxHitPoints(LifeFacts facts, int poisonLevel) => EffectModifiers.Apply(
        4L * facts.HitPointPoints + facts.HitPointAdjustment + facts.Willpower + 2 * (StrengthAt(facts, poisonLevel) + facts.Level) + 4,
        facts.MaxHitPointEffects.IsDefault ? [] : facts.MaxHitPointEffects);

    /// <summary>Maximum fatigue: <c>4 × points + adjustment + 2 × (level + CN) + WP + 4</c>, then the maximum's effects over that
    /// whole sum (0x45D670); the same exceptions.</summary>
    [OriginalFunction(0x45D670, OriginalFunctionScope.Full)]
    public static int MaxFatigue(LifeFacts facts) => EffectModifiers.Apply(
        4L * facts.FatiguePoints + facts.FatigueAdjustment + 2 * (facts.Level + facts.Constitution) + facts.Willpower + 4,
        facts.MaxFatigueEffects.IsDefault ? [] : facts.MaxFatigueEffects);

    [OriginalFunction(0x45D8D0, OriginalFunctionScope.Partial)]
    public static bool IsDead(LifeState state, LifeFacts facts) => MaxHitPoints(facts, state.PoisonLevel) - state.HitPointDamage <= 0;

    [OriginalFunction(0x45D800, OriginalFunctionScope.Partial)]
    public static bool IsUnconscious(LifeState state, LifeFacts facts) =>
        !facts.Traits.HasFlag(LifeTraits.Undead) && MaxFatigue(facts) - state.FatigueDamage <= 0;

    [OriginalFunction(0x45D700, OriginalFunctionScope.Full)]
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
    /// Then, in order: stun (unless a knockdown or knockout comes with it, or the critter is unconscious, paralysed or stoned), knockdown, knockout (a critter still conscious
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
    /// A kill (the source's 0x45D900): the damage step with the death flag, so a living critter takes its current
    /// hit points and dies with the plain fall, then hit-point damage is pinned at <see cref="LifeLimits.DeathHitPointDamage"/>.
    /// An invulnerable or nigh-invulnerable critter skips the death step but is pinned all the same, as in the source, so
    /// it reads as dead without <see cref="LifeEvents.Died"/>. The step's poison write applies too: when the
    /// extraordinary-constitution reset lifts strength, the critter is not dead at the death check and the death step
    /// does not run either, though the pin follows.
    /// </summary>
    [OriginalFunction(0x45D900, OriginalFunctionScope.Partial)]
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
    /// ARC-COMBAT-1141 (GT210–GT212, F1): a kill whose damage step the victim's taking-damage script vetoed (attachment point 23, skip default). The script runs
    /// at the start of the damage apply, before every gate, so the death flag's step does nothing: no hit points taken, no poison write, no
    /// death procedure (<see cref="LifeEvents.Died"/>, the plain fall). The kill's own pin follows all the same (the kill writes
    /// <see cref="LifeLimits.DeathHitPointDamage"/> after the step), so the critter reads as dead without a death: the "dead" state the source never
    /// processes (the "dead" procedure is not read). A dead, invulnerable or living critter alike: the script is asked first, so nothing else is reported.
    /// </summary>
    public static LifeOutcome KillVetoed(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        var after = state with { HitPointDamage = LifeLimits.DeathHitPointDamage };
        return new(state, after, Consciousness(state, after, facts), 0, DeathAnimation.None);
    }

    /// <summary>
    /// ARC-COMBAT-1141 (GT210, GT211): a poison tick whose damage step the victim's taking-damage script vetoed: nothing is written and nothing is
    /// reported; the tick's event still reschedules itself as any tick does (the poison goes on, GT211).
    /// </summary>
    public static LifeOutcome PoisonDamageVetoed(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        return new(state, state, LifeEvents.None, 0, DeathAnimation.None);
    }

    /// <summary>
    /// Heals (the source's heal step). In order: scar removal without <see cref="LifeHeal.Full"/>; a dead critter stops
    /// here unless <see cref="LifeHeal.Resurrect"/>, and a resurrection is refused for an unrevivifiable critter that is
    /// reanimated or an unresurrectable one that is not, or by the target's raise script (<paramref name="raiseScript"/>, the
    /// caller's port, ARC-MAGIC-012: the original runs it, point 0x12, before it tests the refusal, so it runs either way;
    /// none: allowed); a resurrection clears the stun
    /// and sets fatigue damage to 10. Then the full heal or the single cures (crippled legs heal crippled legs, as in the
    /// original game, anomaly A5), and the hit-point, fatigue and poison amounts, each only
    /// while there is damage to remove. Re-read in the retail executable by two readers (ARC-SKILLS-005, ledger HL4): the record
    /// applier 0x4B58C0, without its network, script, art, AI and reaction side effects.
    /// </summary>
    [OriginalFunction(0x4B58C0, OriginalFunctionScope.Partial)]
    public static LifeOutcome Heal(LifeState state, LifeFacts facts, LifeHeal heal, Func<bool>? raiseScript = null)
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
            if (raiseScript is not null && !raiseScript()) refused = true;
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
    /// A heal skill's outcome on its target (ARC-SKILLS-005, the heal case of 0x4C7160 then 0x4B58C0, ledger HL1, HL4): a cure first
    /// removes one scar and the first of blinded, both arms, one arm and both legs crippled (also on a dead critter, as the
    /// original's cure does not look at life), then the hit points heal as <see cref="Heal"/> heals them (nothing on a dead critter).
    /// </summary>
    public static LifeOutcome SkillHeal(LifeState state, LifeFacts facts, int hitPoints, bool cure)
    {
        Check(state, facts);
        if (hitPoints is < 0 or > LifeLimits.MaxAmount) throw new ArgumentOutOfRangeException(nameof(hitPoints));
        var cured = state;
        if (cure)
        {
            var conditions = state.Conditions;
            var first = new[] { LifeConditions.Blinded, LifeConditions.CrippledArmsBoth, LifeConditions.CrippledArmOne, LifeConditions.CrippledLegsBoth }
                .FirstOrDefault(condition => conditions.HasFlag(condition));
            cured = state with { Scars = Math.Max(0, state.Scars - 1), Conditions = conditions & ~first };
        }
        var healed = Heal(cured, facts, new LifeHeal { HitPoints = hitPoints });
        return healed with { Before = state, Events = healed.Events | Consciousness(state, healed.After, facts) };
    }

    /// <summary>
    /// The PC's "full heal" fate (ARC-WORLD-054, 0x4F52D0 kind 0, ledger FT5): hit-point damage, poison level and fatigue damage
    /// are set to 0, nothing else (scars, blindness and crippled limbs stay). The project refuses it for a dead critter
    /// (<see cref="LifeEvents.AlreadyDead"/>, nothing changed); the original writes the values whatever the state, but a dead PC
    /// cannot reach the fate window.
    /// </summary>
    [OriginalFunction(0x4F52D0, OriginalFunctionScope.Partial)]
    public static LifeOutcome FateFullHeal(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        if (IsDead(state, facts)) return new(state, state, LifeEvents.AlreadyDead, 0, DeathAnimation.None);
        var after = state with { HitPointDamage = 0, PoisonLevel = 0, FatigueDamage = 0 };
        return new(state, after, Consciousness(state, after, facts), 0, DeathAnimation.None);
    }

    /// <summary>
    /// Recovery steps of a fatigue recovery event: <c>max(1, (now s − scheduled s) / 80)</c> in whole game seconds
    /// (checked in the original 1.0.7.4 executable at 0x45E610).
    /// </summary>
    [OriginalFunction(0x45E610, OriginalFunctionScope.Partial)]
    public static int FatigueRecoverySteps(long scheduledAtGameMs, long nowGameMs) => Steps(scheduledAtGameMs, nowGameMs, 80);

    /// <summary>Hours of a resting event: <c>max(1, (now s − scheduled s) / 3600)</c> (0x45EA00, ledger IF9).</summary>
    [OriginalFunction(0x45EA00, OriginalFunctionScope.Partial)]
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
    [OriginalFunction(0x45E910, OriginalFunctionScope.Partial)]
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

    /// <summary>
    /// ARC-MAGIC-1508 (the stat setter's poison case, ledger RM3b): the poison level is written as <paramref name="level"/>: clamped to 0..1000 (the range tables), then 0 for a critter whose effective
    /// constitution is 20 or more (the setter's constitution test, RM3b), as every poison write does (<see cref="Damage"/> too). Nothing else changes: no damage step, so no death check (the original's setter
    /// kills nobody; the next poison tick or hit does) and no event but the timers the session sets for a positive level.
    /// </summary>
    public static LifeOutcome SetPoison(LifeState state, LifeFacts facts, long level)
    {
        Check(state, facts);
        return new(state, state with { PoisonLevel = PoisonLevel(level, facts) }, LifeEvents.None, 0, DeathAnimation.None);
    }

    /// <summary>The 120-second poison recovery tick: the poison level loses the PoisonRecovery stat (never below 0); dead or not.</summary>
    public static LifeOutcome PoisonRecoveryTick(LifeState state, LifeFacts facts)
    {
        Check(state, facts);
        var after = state.PoisonLevel > 0 ? state with { PoisonLevel = PoisonLevel(state.PoisonLevel - (long)facts.PoisonRecovery, facts) } : state;
        return new(state, after, LifeEvents.None, 0, DeathAnimation.None);
    }

    /// <summary>
    /// A plain fatigue charge (the source's fatigue-damage write, as a run step in combat mode makes it, ARC-COMBAT-004):
    /// fatigue damage plus <paramref name="amount"/> whatever the critter's immunities or state, losing consciousness
    /// reported; no damage step runs.
    /// </summary>
    public static LifeOutcome ChargeFatigue(LifeState state, LifeFacts facts, int amount)
    {
        Check(state, facts);
        if (amount is < 1 or > LifeLimits.MaxAmount) throw new ArgumentOutOfRangeException(nameof(amount));
        var after = state with { FatigueDamage = AddDamage(state.FatigueDamage, amount) };
        return new(state, after, Consciousness(state, after, facts), 0, DeathAnimation.None);
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
        (facts.PoisonedStrength.IsDefaultOrEmpty || facts.PoisonedStrength.Length == 3 && Stats(facts.PoisonedStrength)) &&
        !facts.MaxHitPointEffects.IsDefault && !facts.MaxFatigueEffects.IsDefault &&
        EffectModifiers.IsValid(facts.MaxHitPointEffects, LifeLimits.MaxMaximumEffects, LifeLimits.MaxMaximumEffectMagnitude) &&
        EffectModifiers.IsValid(facts.MaxFatigueEffects, LifeLimits.MaxMaximumEffects, LifeLimits.MaxMaximumEffectMagnitude) &&
        Math.Abs((long)MaxFatigue(facts)) < LifeLimits.DeathHitPointDamage && PoisonedPoolsBounded(facts);

    private static bool Stat(int value) => value is >= 0 and <= LifeLimits.MaxStat;

    // ARC-RENDER-3418: loops, not queries: every read of a critter's life checks its facts.
    private static bool Stats(ImmutableArray<int> values)
    {
        foreach (var value in values)
            if (!Stat(value)) return false;
        return true;
    }

    private static bool PoisonedPoolsBounded(LifeFacts facts)
    {
        for (var penalty = 0; penalty < 4; penalty++)
            if (Math.Abs((long)MaxHitPoints(facts, penalty * 100)) >= LifeLimits.DeathHitPointDamage) return false;
        return true;
    }

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
        // ARC-COMBAT-009: the stun starts only on a critter that can act (0x4357B0 through 0x45D790: alive, conscious, not
        // stunned, paralysed or stoned); the stun animation's own start is the host's.
        if (effects.HasFlag(CriticalHitEffects.Stun) && !conditions.HasFlag(LifeConditions.Stunned) && (effects & Down) == 0
            && !IsUnconscious(s, facts) && (facts.Traits & (LifeTraits.Paralyzed | LifeTraits.Stoned)) == 0)
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
            death = hitPoints <= 20 || damage.PlainDeath ? DeathAnimation.Plain : damage.Location switch
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
