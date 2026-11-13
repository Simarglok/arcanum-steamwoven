using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation;

namespace Arcanum.Session;

/// <summary>
/// One repair by the hero's Repair skill (ARC-SKILLS-007): the disposition, the failure, the check (its roll changes nothing,
/// ledger RP1), the hit points the run restores, the item rule's status (null when refused), the item's condition after it, whether
/// the hero's repair fate was used, and the version after the call.
/// </summary>
public sealed record SessionRepairResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, int Amount,
    ItemRepairStatus? Repair, ItemCondition? Condition, bool FateUsed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero repairs one of its items with the Repair skill (the skill bar's repair on an item in the inventory or a wear slot,
    /// 0x57A320 → the skill run's repair case, ledger RP1–RP5). In order: the check (<see cref="SkillCheckRules.Run"/>, its two draws
    /// always taken, its armed repair fate used); the case then treats every roll as a success: it restores
    /// <see cref="ItemDurabilityRules.SkillRepairAmount"/> (the whole maximum with the fate, else maximum × effectiveness / 100, at
    /// least 1 above an effectiveness of 0) and lowers the adjustment by the training's share
    /// (<see cref="ItemDurabilityRules.SkillRepairAdjustmentPercent"/>); an item without damage, unless broken and the hero a master, is not
    /// touched (<see cref="ItemRepairStatus.NothingToRepair"/>). No action points, no experience. The check, the fate and the
    /// item are one revision. Refused before anything is rolled: no hero, or a dead or unconscious one
    /// (<see cref="GameSessionFailure.RepairUnavailable"/>), an invalid item or baseline (<see cref="GameSessionFailure.InvalidItem"/>),
    /// an item the hero does not hold or the session deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version.
    /// </summary>
    [OriginalFunction(0x57A1F0, OriginalFunctionScope.Partial)]
    public SessionRepairResult RepairWithSkill(GameSessionVersion expected, string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead() || _life.Hero is { } life && LifeRules.IsUnconscious(life.State, HeroLifeFacts(life.State)))
            return RejectRepair(GameSessionFailure.RepairUnavailable);
        if (!SessionValidation.Entity(entity) || !ItemDurabilityRules.IsValid(baseline)) return RejectRepair(GameSessionFailure.InvalidItem);
        if (_durability.Destroyed.Contains(entity) || !_inventory.Hero.InPack(entity))
            return RejectRepair(GameSessionFailure.ItemNotAvailable);
        if (!IsCurrent(expected)) return RejectRepair(GameSessionFailure.StaleVersion);

        var hero = _hero!;
        var request = HeroSkillRequest(Skill.Repair, null);
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try { check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice); }
        catch (SkillRolls.ExhaustedException) { return RejectRepair(GameSessionFailure.RandomExhausted); }

        var training = (RepairTraining)(int)request.Training;
        var current = _durability.Records.TryGetValue(entity, out var recorded) ? recorded : baseline;
        var amount = ItemDurabilityRules.SkillRepairAmount(current, check.Check.Effectiveness, check.Fated);
        var outcome = ItemDurabilityRules.Repair(current, amount,
            ItemDurabilityRules.SkillRepairAdjustmentPercent(training, current.Broken, amount, check.Critical), training);
        var changed = outcome.Condition != current;
        if (changed && !ItemDurabilityRules.IsValid(outcome.Condition)) return RejectRepair(GameSessionFailure.InvalidItem);
        if (changed && !_durability.Records.ContainsKey(entity) && _durability.Count >= _limits.MaxDurabilityRecords)
            return RejectRepair(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectRepair(GameSessionFailure.RevisionExhausted);

        // All refusals precede the commit. The fate is used whatever the case finds (RP3).
        _random = rolls.Trial;
        if (check.Fated && FateRules.Of(SkillCheckRules.Fate(Skill.Repair)) is { } fate) hero.Apply(hero.State.WithFate(FateRules.Use(hero.State.Fate, fate).After));
        if (changed)
        {
            _durability.Records[entity] = outcome.Condition;
            // A worn item's values follow its record (a repair never breaks it).
            if (_equipment.SlotOf(entity) is { } slot)
                _equipment.Worn[slot] = _equipment.Worn[slot] with { Item = Conditioned(_equipment.Worn[slot].Item, outcome.Condition) };
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, outcome.Status == ItemRepairStatus.Repaired ? amount : 0, outcome.Status,
            outcome.Condition, check.Fated, Version);
    }

    /// <summary>
    /// ARC-UI-1731 (ledger IS3, RP2, RP3, DU8): the item part of the paid repair service of an NPC (the skill run of the NPC as
    /// source on one of the hero's items with the forced flag 0x1000, then the payment, which the caller makes through
    /// <see cref="GiveGoldToNpc"/>). The check is the NPC's: its Repair level and training from <see cref="QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)"/>
    /// (the sheet the session levelled it to, else <paramref name="loaded"/>), the two draws always taken, no fate (the source is not a PC,
    /// RP3). The run is forced, so the amount is <see cref="ItemDurabilityRules.RepairAmount"/> (the maximum × effectiveness / 100, at least 1)
    /// and the item side is <see cref="ItemDurabilityRules.Repair"/> with the NPC's training. The result is a <see cref="SessionRepairResult"/>
    /// with no hero check and no fate. Refused before anything is rolled: no hero or a dead one (<see cref="GameSessionFailure.RepairUnavailable"/>),
    /// the hero as the repairer, a sheet that is not the NPC's or not admissible (<see cref="GameSessionFailure.RepairUnavailable"/>), an invalid
    /// item or baseline (<see cref="GameSessionFailure.InvalidItem"/>), an item the hero does not hold or the session deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version, an exhausted random stream, a full durability record store.
    /// One revision.
    /// </summary>
    /// <remarks>Executable: the repair service 0x4C8E60, whose item part is registered as <c>ItemDurabilityRules.Repair</c>.</remarks>
    public SessionRepairResult RepairForService(GameSessionVersion expected, string npc, SessionNpcAdvancement loaded, AdvancementModifiers modifiers,
        string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(modifiers);
        if (_hero is null || HeroDead()) return RejectRepair(GameSessionFailure.RepairUnavailable);
        if (!SessionValidation.Entity(npc) || npc == SessionSpellCaster.Hero || loaded.Entity != npc || !AutoLevelRules.IsValid(loaded.Sheet) ||
            !AutoLevelRules.IsValid(modifiers))
            return RejectRepair(GameSessionFailure.RepairUnavailable);
        if (!SessionValidation.Entity(entity) || !ItemDurabilityRules.IsValid(baseline)) return RejectRepair(GameSessionFailure.InvalidItem);
        if (_durability.Destroyed.Contains(entity) || !_inventory.Hero.InPack(entity)) return RejectRepair(GameSessionFailure.ItemNotAvailable);
        if (!IsCurrent(expected)) return RejectRepair(GameSessionFailure.StaleVersion);

        var sheet = _advancement.Npcs.TryGetValue(loaded.Entity, out var levelled) ? levelled.Sheet : loaded.Sheet;
        var skill = QueryNpcSkills(loaded, modifiers)[Skill.Repair];
        var request = new SkillCheckRequest
        {
            Skill = Skill.Repair,
            Level = skill.Level,
            Training = skill.Training,
            Effectiveness = new()
            {
                ExtraordinaryIntelligence = AutoLevelRules.EffectiveStat(sheet, modifiers, CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
                Difficulty = _difficulty,
            },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try { check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice); }
        catch (SkillRolls.ExhaustedException) { return RejectRepair(GameSessionFailure.RandomExhausted); }

        var training = (RepairTraining)(int)skill.Training;
        var current = _durability.Records.TryGetValue(entity, out var recorded) ? recorded : baseline;
        var amount = ItemDurabilityRules.RepairAmount(current, check.Check.Effectiveness, fullRepair: false);
        var outcome = ItemDurabilityRules.Repair(current, amount, ItemDurabilityRules.RepairAdjustmentPercent(training, current.Broken), training);
        var changed = outcome.Condition != current;
        if (changed && !ItemDurabilityRules.IsValid(outcome.Condition)) return RejectRepair(GameSessionFailure.InvalidItem);
        if (changed && !_durability.Records.ContainsKey(entity) && _durability.Count >= _limits.MaxDurabilityRecords)
            return RejectRepair(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectRepair(GameSessionFailure.RevisionExhausted);

        _random = rolls.Trial;
        if (changed)
        {
            _durability.Records[entity] = outcome.Condition;
            if (_equipment.SlotOf(entity) is { } slot)
                _equipment.Worn[slot] = _equipment.Worn[slot] with { Item = Conditioned(_equipment.Worn[slot].Item, outcome.Condition) };
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, outcome.Status == ItemRepairStatus.Repaired ? amount : 0, outcome.Status,
            outcome.Condition, false, Version);
    }

    private SessionRepairResult RejectRepair(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, 0, null, null, false, Version);
}
