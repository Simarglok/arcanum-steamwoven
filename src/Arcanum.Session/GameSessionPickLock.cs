using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// Lockpicks the hero uses (ARC-SKILLS-006), as its host's data holds them: the entity, the generic item's flags (field 0xD3; bit
/// 0x02 marks lockpicks), the usage bonus (field 0xD4, lowering the difficulty as stored), the condition (a damaged item adds to
/// the critical failure chance) and the script points (a use script is refused, as the session runs none). Lockpicks never lose
/// uses and are never destroyed (ledger PL4).
/// </summary>
public sealed record SessionLockpick(string Entity, uint Flags, int UsageBonus, ItemCondition Condition, IReadOnlyList<int> ScriptPoints)
{
    /// <summary>The generic item flag of lockpicks (the source's field 0xD3 bit 0x02).</summary>
    public const uint LockpickFlag = 0x02;
    /// <summary>The use script point the inventory use runs.</summary>
    public const int UseScript = 1;

    public bool Lockpick => (Flags & LockpickFlag) != 0;
}

/// <summary>The portal or container of a pick-locks attempt: the object, its T0 lock facts, the lock's difficulty (portal field
/// 47, container field 57) and the light 0..255 at the object (from expert the darkness term does not apply, SC9).</summary>
public sealed record SessionPickLockTarget(SessionOpenable Openable, SessionOpenableBaseline Baseline, int LockDifficulty, int Light = SessionPickLockTarget.FullLight)
{
    public const int FullLight = 255;
}

/// <summary>
/// One pick-locks attempt (ARC-SKILLS-006): the disposition, the failure, the action-point charge of a turn-based round (null
/// outside one, without a world or without lockpicks), the check, the outcome (whether the witnesses are called is the host's to
/// act on), the object's state after it, whether the hero's pick-locks fate was used, and the version after the call.
/// </summary>
public sealed record SessionPickLockResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCombatResult? Charge,
    SkillCheckResult? Check, PickLockOutcome? Outcome, SessionOpenableState? State, bool FateUsed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The bound of a lock's difficulty and of a lockpick's usage bonus (project policy).</summary>
    public const int MaxLockDifficulty = 1_000, MaxLockpickBonus = 1_000;

    /// <summary>
    /// The hero picks the lock of a portal or a container (the source's inventory use of lockpicks on an object, 0x571CB0 →
    /// 0x462CC0 → the use-item-with-skill goal → the shared check and the pick-locks case, ledger PL1–PL6). In order:
    /// <list type="number">
    /// <item>With lockpicks, a world and a turn-based round whose subturn is the hero's, 4 action points are charged
    /// (<see cref="ConsumeActionPoints"/>, its own revision; the pick goes on whatever the charge answers, as for any item use).
    /// The worst-case budgets and revisions are checked before it. Without lockpicks (the path of a script or a service) nothing
    /// is charged.</item>
    /// <item>The check (<see cref="SkillCheckRules.Run"/>): the hero's pick-locks level and training, the lockpick's bonus, the
    /// lock's difficulty (halved for a master when positive), the light (none from expert), the hero's injuries, the lockpick's
    /// condition for a critical failure, the armed fate.</item>
    /// <item>The outcome (<see cref="PickLockRules.Resolve"/>) on the lock state at the session hour: a locked object unlocks
    /// (its relock an hour later) or, on a critical failure, jams (its relock a day later); an unlocked one locks on a success.
    /// A junk pile has no lock: nothing is written.</item>
    /// </list>
    /// The rolls come from the session generator; the check, the lock write, its Lock event and the fate are one revision.
    /// Refused before anything is charged or rolled: no hero or a dead one (<see cref="GameSessionFailure.PickLockUnavailable"/>),
    /// an invalid object, difficulty or light (<see cref="GameSessionFailure.InvalidOpenable"/>), an item that is not lockpicks or
    /// holds values outside the bounds (<see cref="GameSessionFailure.InvalidLockpick"/>), lockpicks with a use script
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), lockpicks the hero does not hold or the session deleted
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version.
    /// </summary>
    public SessionPickLockResult PickLock(GameSessionVersion expected, SessionPickLockTarget target, SessionLockpick? lockpick,
        ISessionCombatWorld? world = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (_hero is null || HeroDead()) return RejectPickLock(GameSessionFailure.PickLockUnavailable);
        if (!ValidOpenable(target.Openable, target.Baseline) || target.LockDifficulty is < -MaxLockDifficulty or > MaxLockDifficulty ||
            target.Light is < 0 or > SessionPickLockTarget.FullLight)
            return RejectPickLock(GameSessionFailure.InvalidOpenable);
        if (lockpick is not null)
        {
            if (!lockpick.Lockpick || lockpick.UsageBonus is < -MaxLockpickBonus or > MaxLockpickBonus || !SessionValidation.Entity(lockpick.Entity) ||
                !ItemDurabilityRules.IsValid(lockpick.Condition) || lockpick.ScriptPoints is null)
                return RejectPickLock(GameSessionFailure.InvalidLockpick);
            if (lockpick.ScriptPoints.Contains(SessionLockpick.UseScript)) return RejectPickLock(GameSessionFailure.ItemScriptUnsupported);
            if (_durability.Destroyed.Contains(lockpick.Entity) || !_inventory.Kit.Any(kit => kit.Entity == lockpick.Entity) &&
                !_inventory.Pc.Any(pc => pc.Entity == lockpick.Entity) && !_inventory.Made.Any(made => made.Entity == lockpick.Entity))
                return RejectPickLock(GameSessionFailure.ItemNotAvailable);
        }
        if (!IsCurrent(expected)) return RejectPickLock(GameSessionFailure.StaleVersion);

        SessionCombatResult? charge = null;
        if (lockpick is not null && world is not null && _combat.Round is { } round && round.Actor == SessionCombatant.Hero)
        {
            // The attempt's worst case before the charge: a first record of the object's three lock values, a Lock event and the
            // hero's fatigue timer an overexerting charge can add, two revisions.
            var (flags, frame) = Current(target.Openable, target.Baseline);
            if (!ChangesFit(EffectiveChanges(Edits(target.Openable, target.Baseline.Kind, flags ^ LockRules.LockedBit, frame))))
                return RejectPickLock(GameSessionFailure.BudgetExceeded);
            if (LockEventsFull(2) || _clock.QueryClockSnapshot().GameTimestampMs > long.MaxValue - LockRules.JamResetDelayMs)
                return RejectPickLock(GameSessionFailure.EventBudgetExceeded);
            if (_revision >= long.MaxValue - 1) return RejectPickLock(GameSessionFailure.RevisionExhausted);
            charge = ConsumeActionPoints(Version, SessionCombatant.Hero, TurnBasedRules.UseItemCost, world);
            if (charge.Disposition == GameSessionDisposition.Rejected) return RejectPickLock(charge.Failure) with { Charge = charge };
        }
        return PickLockStep(target, lockpick) with { Charge = charge };
    }

    private SessionPickLockResult PickLockStep(SessionPickLockTarget target, SessionLockpick? lockpick)
    {
        var hero = _hero!;
        var condition = lockpick is null ? (ItemCondition?)null : QueryItemCondition(lockpick.Entity, lockpick.Condition)!.Value;
        var request = HeroSkillRequest(Skill.PickLocks, condition);
        request = request with
        {
            Difficulty = request.Difficulty with
            {
                TargetLockable = true,
                LockpickBonus = lockpick?.UsageBonus ?? 0,
                LockDifficulty = target.LockDifficulty,
                Light = target.Light,
            },
        };
        var rolls = new SkillRolls(new SeededRandom(_random.Seed, _random.Draws));
        SkillCheckResult check;
        try { check = SkillCheckRules.Run(request, rolls, _rules.Combat.InvocationModifierCountedTwice); }
        catch (SkillRolls.ExhaustedException) { return RejectPickLock(GameSessionFailure.RandomExhausted); }

        var openable = target.Openable;
        var hour = _clock.QueryClockSnapshot().Hour;
        var locked = QueryOpenable(openable, target.Baseline).Locked;
        var outcome = PickLockRules.Resolve(locked, check.Success, check.Critical);
        var (flags, frame) = Current(openable, target.Baseline);
        // The lock setters of the pick-locks case (LK3, LK4); a junk pile has no lock, so nothing is written for it.
        LockWrite? write = target.Baseline.JunkPile ? null : outcome.Result switch
        {
            PickLockResult.Unlocked => LockRules.SetLocked(flags, false, hour),
            PickLockResult.Locked => LockRules.SetLocked(flags, true, hour),
            PickLockResult.Jammed => LockRules.SetJammed(flags, true, hour),
            _ => null,
        };
        var edits = write is { } written && written.Flags != flags ? EffectiveChanges(Edits(openable, target.Baseline.Kind, written.Flags, frame)) : [];
        var schedules = edits.Length > 0 && write!.Value.SchedulesReset;
        var delay = outcome.Result == PickLockResult.Jammed ? LockRules.JamResetDelayMs : LockRules.UnlockResetDelayMs;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (schedules && (LockEventsFull(1) || now > long.MaxValue - delay)) return RejectPickLock(GameSessionFailure.EventBudgetExceeded);
        if (!ChangesFit(edits)) return RejectPickLock(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectPickLock(GameSessionFailure.RevisionExhausted);

        // All refusals precede the commit.
        _random = rolls.Trial;
        if (check.Fated && FateRules.Of(SkillCheckRules.Fate(Skill.PickLocks)) is { } fate) hero.Apply(hero.State.WithFate(FateRules.Use(hero.State.Fate, fate).After));
        if (edits.Length > 0)
        {
            if (ApplyChanges(Version, null, edits).Disposition != GameSessionDisposition.Changed)
                throw new InvalidOperationException("An admitted lock write was refused.");
        }
        else Revise();
        if (schedules && _events.ScheduleAfter(SessionEventKinds.LockReset, GameEventPayload.Of([new(openable.Map.RuntimeMap, openable.Entity)], now), delay,
            new(0, now, 0)).Disposition != GameEventScheduleDisposition.Added)
            throw new InvalidOperationException("An admitted Lock event could not be scheduled.");
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, check, outcome, QueryOpenable(openable, target.Baseline), check.Fated, Version);
    }

    // Whether the queue lacks room for this many more events, one of them a Lock event.
    private bool LockEventsFull(int events) =>
        _events.QueryPending().Count(e => e.Kind == SessionEventKinds.LockReset) >= SessionEventKinds.MaxLockResets ||
        _events.Count > _events.Limits.MaxPending - events || _events.NextSequence > long.MaxValue - events;

    private SessionPickLockResult RejectPickLock(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, null, null, false, Version);
}
