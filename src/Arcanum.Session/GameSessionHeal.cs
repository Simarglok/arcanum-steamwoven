using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// A healing item the hero uses (ARC-SKILLS-005), as its host's data holds it: the entity, the generic item's flags (field 0xD3;
/// bit 0x08 marks a healing item), its usage bonus (field 0xD4, lowering the heal difficulty), its remaining uses (field 0xD5, the
/// baseline until the session spends one), its condition (a damaged item adds to the critical failure chance) and its script
/// points (a use or destroy script is refused, as the session runs none).
/// </summary>
public sealed record SessionHealingItem(string Entity, uint Flags, int UsageBonus, int Uses, ItemCondition Condition, IReadOnlyList<int> ScriptPoints)
{
    /// <summary>The generic item flag of a healing item (the source's field 0xD3 bit 0x08).</summary>
    public const uint HealingFlag = 0x08;
    /// <summary>The script points this use runs: use (1) and destroy (2).</summary>
    public const int UseScript = 1, DestroyScript = 2;

    public bool Healing => (Flags & HealingFlag) != 0;
}

/// <summary>
/// One heal (ARC-SKILLS-005): the disposition, the failure, the action-point charge of a turn-based round (null outside one, or
/// when no world was given), the check, the heal's outcome, the target's life outcome (null after a failed check), the item's
/// uses left (null when it was destroyed), whether the hero's heal fate was used, and the version after the call.
/// </summary>
public sealed record SessionHealResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCombatResult? Charge,
    SkillCheckResult? Check, HealOutcome? Heal, LifeOutcome? Life, int? UsesLeft, bool ItemDestroyed, bool FateUsed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The bound of a healing item's usage bonus (project policy).</summary>
    public const int MaxHealingItemBonus = 1_000;

    /// <summary>
    /// The hero uses a healing item on a living or dead critter, the hero itself included (the source's "use item" with a healing item,
    /// 0x462CC0 → the use-item-with-skill goal 0x4352C0/0x428E10 → the heal check, ledger HL1–HL5). In order:
    /// <list type="number">
    /// <item>With a world and a turn-based round whose subturn is the hero's, 4 action points are charged
    /// (<see cref="ConsumeActionPoints"/>, its own revision; the original ignores the charge's answer, so the heal goes on).</item>
    /// <item>The heal check (<see cref="SkillCheckRules.Run"/>): the hero's heal level and training, the item's bonus, the hero's
    /// blindness and crippled arms, the item's condition for a critical failure, the armed heal fate.</item>
    /// <item>The outcome (<see cref="HealRules.Resolve"/>): a success heals 1..the maximum (a master or a critical success the
    /// maximum and cures one scar and one injury, <see cref="LifeRules.SkillHeal"/>); a failure heals nothing.</item>
    /// <item>The item loses one use, or five on a critical failure below expert, and is destroyed at 0 or less.</item>
    /// </list>
    /// The rolls (<c>skill</c> check, critical and amount) come from the session generator; the check, the life record, the fate
    /// and the item are one revision. Before a charge the heal's worst-case record budgets and revisions are checked, so only an
    /// exhausted generator can refuse a heal after its charge (the charge stays, as any spent action point). Without a world no
    /// action points are charged even in the hero's subturn (the caller's choice; the AREA viewer has no turn-based round). Nothing refuses a target at full hit points, an undead or mechanical target or the hero
    /// itself (HL5). Refused before anything is charged or rolled: no hero or a dead one (<see cref="GameSessionFailure.HealUnavailable"/>, as every hero action;
    /// an unconscious hero is not refused), an
    /// item that is not a healing item or holds values outside the bounds (<see cref="GameSessionFailure.InvalidHealingItem"/>), an
    /// item with a use or destroy script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), an item the hero does not hold
    /// or the session deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>), a target whose values do not hold, the version.
    /// </summary>
    public SessionHealResult HealWithItem(GameSessionVersion expected, SessionLifeTarget target, SessionHealingItem item,
        ISessionCombatWorld? world = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(item);
        if (_hero is null || HeroDead()) return RejectHeal(GameSessionFailure.HealUnavailable);
        if (!item.Healing || item.UsageBonus is < -MaxHealingItemBonus or > MaxHealingItemBonus || item.Uses is < 0 or > SessionItemUseLimits.MaxUses ||
            !SessionValidation.Entity(item.Entity) || !ItemDurabilityRules.IsValid(item.Condition) || item.ScriptPoints is null)
            return RejectHeal(GameSessionFailure.InvalidHealingItem);
        if (item.ScriptPoints.Contains(SessionHealingItem.UseScript) || item.ScriptPoints.Contains(SessionHealingItem.DestroyScript))
            return RejectHeal(GameSessionFailure.ItemScriptUnsupported);
        if (_durability.Destroyed.Contains(item.Entity) || !_inventory.Kit.Any(kit => kit.Entity == item.Entity) && !_inventory.Pc.Any(pc => pc.Entity == item.Entity) &&
            !_inventory.Made.Any(made => made.Entity == item.Entity))
            return RejectHeal(GameSessionFailure.ItemNotAvailable);
        var resolved = Resolve(target, out _, out _, out _);
        if (resolved != GameSessionFailure.None) return RejectHeal(resolved);
        if (!IsCurrent(expected)) return RejectHeal(GameSessionFailure.StaleVersion);

        SessionCombatResult? charge = null;
        if (world is not null && _combat.Round is { } round && round.Actor == SessionCombatant.Hero)
        {
            // The heal's worst-case budgets and revisions before the charge, so a charged heal is not then refused for them.
            var recordedCritter = !target.IsHero && _life.Critters.ContainsKey(target.Entity!);
            if (!target.IsHero && !recordedCritter && _life.Critters.Count >= _limits.MaxLifeRecords || NewDurabilityRecord(item.Entity, destroyed: false) &&
                _durability.Count >= _limits.MaxDurabilityRecords)
                return RejectHeal(GameSessionFailure.BudgetExceeded);
            // An NPC's heal can add its resting timer and move its pending timers to this map; an overexerting charge can add the
            // hero's fatigue timer first, so two slots.
            if (!target.IsHero && (_events.Count > _events.Limits.MaxPending - 2 || _events.NextSequence > long.MaxValue - 2 ||
                _clock.QueryClockSnapshot().GameTimestampMs > long.MaxValue - SessionEventKinds.RestingDelayMs))
                return RejectHeal(GameSessionFailure.EventBudgetExceeded);
            if (!target.IsHero && _passWorld is not null && StrayMaps(target.Entity!, _actor.Map.RuntimeMap).Length > 0)
                return RejectHeal(GameSessionFailure.InvalidEventPass);
            if (_revision >= long.MaxValue - 1) return RejectHeal(GameSessionFailure.RevisionExhausted);
            charge = ConsumeActionPoints(Version, SessionCombatant.Hero, TurnBasedRules.UseItemCost, world);
            if (charge.Disposition == GameSessionDisposition.Rejected) return RejectHeal(charge.Failure) with { Charge = charge };
        }
        // The charge can change the hero's life (overexertion) or a critter's record (a new subturn); the target is read now.
        Resolve(target, out var record, out var facts, out var recorded);
        var result = HealStep(target, item, record, facts!, recorded);
        return result with { Charge = charge };
    }

    private SessionHealResult HealStep(SessionLifeTarget target, SessionHealingItem item, SessionLifeRecord record, LifeFacts facts, bool recorded)
    {
        var hero = _hero!;
        var heal = HeroSkills()[Skill.Heal];
        var condition = QueryItemCondition(item.Entity, item.Condition)!.Value;
        var request = HeroSkillRequest(Skill.Heal, condition);
        request = request with
        {
            Difficulty = request.Difficulty with { Target = true, TargetCritter = true, HealingItem = true, HealingItemBonus = item.UsageBonus },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        HealOutcome outcome;
        try
        {
            check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice);
            outcome = HealRules.Resolve(check.Success, check.Critical, heal.Level, heal.Training, rolls);
        }
        catch (SkillRolls.ExhaustedException) { return RejectHeal(GameSessionFailure.RandomExhausted); }

        LifeOutcome? life = check.Success ? LifeRules.SkillHeal(record.State, facts, outcome.HitPoints, outcome.Cure) : null;
        var lifeChanged = life is not null && life.After != record.State;
        var current = _durability.Uses.TryGetValue(item.Entity, out var spent) ? spent : item.Uses;
        var left = (long)current - outcome.UsesSpent;
        var destroyed = left <= 0;
        // An NPC's life write follows the session's life rule (ARC-WORLD-024): hit-point damage left positive and changed schedules
        // its resting timer on the map it is on (the actor's), and its pending timers on another map move here.
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var map = _actor.Map.RuntimeMap;
        var timers = life is null || target.IsHero ? [] : Timers(false, record.State, life.After, null)
            .Select(timer => TimerEvent(timer, target, map, now, 1)).Where(pending => !IsPending(pending.Kind, target.Entity)).ToArray();
        var stray = life is null || target.IsHero ? [] : StrayMaps(target.Entity!, map);
        if (stray.Length > 0 && _passWorld is not null) return RejectHeal(GameSessionFailure.InvalidEventPass);
        if (timers.Length > 0 && (_events.Count > _events.Limits.MaxPending - timers.Length ||
            _events.NextSequence > long.MaxValue - timers.Length || now > long.MaxValue - SessionEventKinds.RestingDelayMs))
            return RejectHeal(GameSessionFailure.EventBudgetExceeded);
        // Budgets before anything is written: a new NPC life record, a new use record.
        if (lifeChanged && !target.IsHero && !recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return RejectHeal(GameSessionFailure.BudgetExceeded);
        if (NewDurabilityRecord(item.Entity, destroyed) && _durability.Count >= _limits.MaxDurabilityRecords)
            return RejectHeal(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectHeal(GameSessionFailure.RevisionExhausted);

        _random = rolls.Trial;
        foreach (var from in stray)
            if (_events.Relocate(from, map, entity => entity.Code == target.Entity).Failure != GameEventFailure.None)
                throw new InvalidOperationException("Timer relocation failed after admission.");
        foreach (var (kind, payload) in timers)
            if (_events.ScheduleAfter(kind, payload, SessionEventKinds.Delay(TimerOf(kind)), new(0, now, 0)).Disposition != GameEventScheduleDisposition.Added)
                throw new InvalidOperationException("Timer scheduling failed after admission.");
        if (lifeChanged)
        {
            var next = new SessionLifeRecord(life!.After, record.DeathTimeMs);
            if (target.IsHero) _life.Hero = next;
            else _life.Critters[target.Entity!] = next;
        }
        if (check.Fated) hero.Apply(hero.State.WithFate(FateRules.Use(hero.State.Fate, FateKind.Heal).After));
        if (destroyed) Delete(item.Entity);
        else _durability.Uses[item.Entity] = (int)left;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, check, outcome, life, destroyed ? null : (int)left, destroyed,
            check.Fated, Version);
    }

    // Whether the item's change adds a durability record: a destroyed entry for an item with no condition or uses record, or a
    // first uses record.
    private bool NewDurabilityRecord(string entity, bool destroyed) => destroyed
        ? !_durability.Records.ContainsKey(entity) && !_durability.Uses.ContainsKey(entity)
        : !_durability.Uses.ContainsKey(entity);

    private SessionHealResult RejectHeal(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, null, null, null, false, false, Version);

    /// <summary>
    /// The shared check of a hero's skill use (ARC-SKILLS-005, ARC-SKILLS-006): the hero's level and training of
    /// <paramref name="skill"/>, the effectiveness facts (extraordinary intelligence, the local PC, the game difficulty), the hero's
    /// blindness and crippled arms and legs, the used item's condition for a critical failure (none without an item) and the armed
    /// fate of the skill. The caller adds the skill's own difficulty terms.
    /// </summary>
    private SkillCheckRequest HeroSkillRequest(Skill skill, ItemCondition? item)
    {
        var hero = _hero!;
        var level = HeroSkills()[skill];
        var snapshot = hero.Stats.Query(new(0, _life.Hero!.Value.State.PoisonLevel)).Snapshot!;
        var conditions = _life.Hero!.Value.State.Conditions;
        return new()
        {
            Skill = skill,
            Level = level.Level,
            Training = level.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                LocalPc = true,
                Difficulty = _difficulty,
            },
            Difficulty = new()
            {
                SourceInjuries = (conditions.HasFlag(LifeConditions.Blinded) ? SkillSourceInjuries.Blinded : 0)
                    | (conditions.HasFlag(LifeConditions.CrippledArmOne) ? SkillSourceInjuries.CrippledArmOne : 0)
                    | (conditions.HasFlag(LifeConditions.CrippledArmsBoth) ? SkillSourceInjuries.CrippledArmsBoth : 0)
                    | (conditions.HasFlag(LifeConditions.CrippledLegsBoth) ? SkillSourceInjuries.CrippledLegsBoth : 0),
            },
            Critical = item is { } condition ? new()
            {
                Item = true,
                ItemHitPoints = Math.Clamp(condition.HitPoints, 0, Math.Max(0, condition.MaximumHitPoints)),
                ItemMaxHitPoints = Math.Max(0, condition.MaximumHitPoints),
            } : new(),
            FateArmed = HeroSkillFate(skill) is { } fate && FateRules.IsArmed(hero.State.Fate, fate),
        };
    }

    // A skill use's draws from a trial copy of the session generator: the check's two d100 and, for a heal, the amount.
    private sealed class SkillRolls(SeededRandom trial) : ISkillCheckRolls, IHealRolls
    {
        internal SeededRandom Trial { get; } = trial;

        public int D100(SkillCheckRoll roll) => Trial.TryNext(1, 100, out var value) ? value : throw new ExhaustedException();

        public int Amount(int maximum) => Trial.TryNext(1, maximum, out var value) ? value : throw new ExhaustedException();

        internal sealed class ExhaustedException : Exception;
    }
}
