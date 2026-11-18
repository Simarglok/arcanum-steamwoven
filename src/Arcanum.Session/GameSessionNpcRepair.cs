using Arcanum.Simulation.Items;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-AI-2622: the answer of an NPC's repair attempt (<see cref="GameSessionOwner.RepairByNpc"/>): the check (whose roll changes nothing), the hit points restored, the item rule's
/// status (null when refused), the item's condition after it, the success the run's flag 0x40000 takes (the case forces it; see the method), and the version.
/// </summary>
public sealed record SessionNpcRepairResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SkillCheckResult? Check, int Amount, ItemRepairStatus? Repair,
    ItemCondition? Condition, bool Success, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-AI-2622 (exe 0x4C7160, repair case 12 for an NPC actor, reached by the goal-33 attempt 0x42A2A0; read 2026-11-18): the NPC repairs the item <paramref name="entity"/> with its own Repair skill. The check is
    /// the shared one of the NPC (<see cref="SkillCheckRules.Run"/>: its level and training from <see cref="QueryNpcSkills(SessionNpcAdvancement, AdvancementModifiers)"/>, its extraordinary intelligence, the game difficulty, its
    /// injuries; no fate, the PC's only, and no target term). The case then forces success whatever the roll was, so the roll decides nothing but the critical flag: the hit points restored are
    /// <see cref="ItemDurabilityRules.SkillRepairAmount"/> (the item's maximum × the check's effectiveness / 100, at least 1 above an effectiveness of 0, else 0) and hp_adj falls by the training's share of the maximum
    /// (<see cref="ItemDurabilityRules.SkillRepairAdjustmentPercent"/>: none 10, apprentice 5, expert 1, master 0; a master takes 5 and clears the destroyed bit of the current art of a broken item, and 1 on an item not
    /// broken when it restores nothing on a critical), written even when nothing is restored. An item without damage that is not broken to a master is not touched, and its success stays 1. The attempt's success is 1, except
    /// that an amount of 0 (an effectiveness of 0 on a damaged or broken-but-not-master-repaired item) clears it. The request is not forced (the paid repair service's flag 0x1000, which the generic AI goal never sets;
    /// the service is <see cref="RepairForService"/>). The caller supplies the item's <paramref name="baseline"/> (the item's own fields), vouches that the NPC reaches it, and answers its UI callbacks, which the original
    /// runs only for the local player. No action points here (the goal charged its own), no experience, no event. Refused before anything is rolled: a user whose sheet does not hold, that is not the sheet's NPC, is the
    /// hero or is dead (<see cref="GameSessionFailure.SkillsUnavailable"/>), an invalid item or baseline (<see cref="GameSessionFailure.InvalidItem"/>), an item the session deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version, an exhausted random stream, a full durability record store. The check and the item are one revision.
    /// </summary>
    public SessionNpcRepairResult RepairByNpc(GameSessionVersion expected, SessionNpcSkillUser user, string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        if (NpcSkillUserFailure(user, out var record, out _) != GameSessionFailure.None) return RejectNpcRepair(GameSessionFailure.SkillsUnavailable);
        if (!SessionValidation.Entity(entity) || !ItemDurabilityRules.IsValid(baseline)) return RejectNpcRepair(GameSessionFailure.InvalidItem);
        if (_durability.Destroyed.Contains(entity)) return RejectNpcRepair(GameSessionFailure.ItemNotAvailable);
        if (!IsCurrent(expected)) return RejectNpcRepair(GameSessionFailure.StaleVersion);

        var request = NpcSkillRequest(user, Skill.Repair, record, null);
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try { check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice); }
        catch (SkillRolls.ExhaustedException) { return RejectNpcRepair(GameSessionFailure.RandomExhausted); }

        var training = (RepairTraining)(int)request.Training;
        var current = _durability.Records.TryGetValue(entity, out var recorded) ? recorded : baseline;
        var amount = ItemDurabilityRules.SkillRepairAmount(current, check.Check.Effectiveness, fullRepair: false);
        var outcome = ItemDurabilityRules.Repair(current, amount, ItemDurabilityRules.SkillRepairAdjustmentPercent(training, current.Broken, amount, check.Critical), training);
        var changed = outcome.Condition != current;
        if (changed && !ItemDurabilityRules.IsValid(outcome.Condition)) return RejectNpcRepair(GameSessionFailure.InvalidItem);
        if (changed && !_durability.Records.ContainsKey(entity) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectNpcRepair(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectNpcRepair(GameSessionFailure.RevisionExhausted);

        _random = rolls.Trial;
        if (changed)
        {
            _durability.Records[entity] = outcome.Condition;
            if (_equipment.SlotOf(entity) is { } slot)
                _equipment.Worn[slot] = _equipment.Worn[slot] with { Item = Conditioned(_equipment.Worn[slot].Item, outcome.Condition) };
        }
        Revise();
        // The case's success: forced to 1 at its entry, cleared only when a damaged item gets nothing (amount 0), except a master's broken-art clear, which forces it back to 1.
        var repaired = outcome.Status == ItemRepairStatus.Repaired;
        var success = !repaired || amount > 0 || current.Broken && training == RepairTraining.Master;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, check, repaired ? amount : 0, outcome.Status, outcome.Condition, success, Version);
    }

    private SessionNpcRepairResult RejectNpcRepair(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, 0, null, null, false, Version);
}
