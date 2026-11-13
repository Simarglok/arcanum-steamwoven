using System.Collections.Immutable;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-087: the last change of the hero's encumbrance level (the recalculation 0x45F820, ledger WE5): its sequence (1 for the
/// first change of this owner; it restarts after a restore, which recalculates nothing), the new level and the effect entries it left
/// (<see cref="CarryRules.EffectEntries"/>, fewer when the effect list is full, none without the effect's rule). The host shows line
/// <c>11 + level</c> of <c>mes\critter.mes</c> for each new sequence, as the original shows it to the local PC.
/// </summary>
public sealed record SessionEncumbranceChange(long Sequence, int Level, int Entries);

public sealed partial class GameSessionOwner
{
    private SessionEffectDefinition? _encumbranceEffect;
    private EncumbranceMark? _encumbranceMark;
    private bool _encumbranceSettling;
    private SessionEncumbranceChange? _encumbranceChange;

    /// <summary>
    /// What a change is compared with: the hero's held objects (kit, taken and made; its coins are a counter and weigh nothing), the units
    /// of its held stacks, its base and effective strength (without its poison and crippled legs, which no recalculation follows) and its
    /// encumbrance level. Taken after every revision, so it is the state before the next change.
    /// </summary>
    private sealed record EncumbranceMark(ImmutableHashSet<string> Members, ImmutableDictionary<string, int> Units, int BaseStrength,
        int Strength, int Level);

    /// <summary>
    /// ARC-WORLD-087: attaches the rule of effect 158 (<see cref="CarryRules.EffectNumber"/>; the host reads it from <c>rules\effect.mes</c>,
    /// speed −2 in the installed game), which the level's recalculation adds. Not state: it is never saved and is attached again after a
    /// restore; null detaches it. While none is attached, the recalculation uses the rule the hero's saved entries of effect 158 carry, and
    /// without either it adds no entries (the timer and the change are still recorded). A rule of another number or one the effect list
    /// does not admit throws <see cref="ArgumentException"/>.
    /// </summary>
    public void AttachEncumbranceEffect(SessionEffectDefinition? definition)
    {
        ThrowIfDisposed();
        if (definition is not null && (definition.Number != CarryRules.EffectNumber || !EffectDomain.IsValid(definition)))
            throw new ArgumentException("Not an admitted rule of effect 158.", nameof(definition));
        _encumbranceEffect = definition;
    }

    /// <summary>ARC-WORLD-087: the last change of the hero's encumbrance level since this owner was built, or null when none happened.</summary>
    public SessionEncumbranceChange? QueryHeroEncumbranceChange()
    {
        ThrowIfDisposed();
        return _encumbranceChange;
    }

    // Every change of the session's state passes here: the revision, the hotkey entries whose items left (ARC-UI-022), the hero's grid
    // (ARC-UI-019), then the level's recalculation where the original has one.
    private void Revise()
    {
        _revision++;
        SettleHotkeys();
        SettleHeroGrid();
        MarkHotkeyPlaces();
        SettleEncumbrance();
    }

    /// <summary>
    /// The callers of the recalculation (0x45F820, ledger WE7), compared with the state before the change: an object entering or leaving the
    /// hero's list (the insertion 0x466640 and the removal 0x467860, which a wear move, a deletion and a placement also run), a change of
    /// its base strength (0x4B0980) or of the strength its effects give (the effect add 0x4E9F70 and removal 0x4EA520). Each compares the
    /// level before with the level after and recalculates only when they differ. Units merged into a stack the hero holds enter no list:
    /// the Original setting does not recalculate for them (WEA1), Steamwoven does. Any other change (poison, aptitude, a stack spent in
    /// part) recalculates nothing, as in the original, so the level the effects show may lag the weight until the next recalculation.
    /// </summary>
    [OriginalFunction(0x45F820, OriginalFunctionScope.Partial)]
    private void SettleEncumbrance()
    {
        var before = _encumbranceMark;
        var now = EncumbranceNow();
        _encumbranceMark = now;
        if (_encumbranceSettling || now is null) return;
        var listed = before is null || !before.Members.SetEquals(now.Members);
        var strength = before is not null && (before.BaseStrength != now.BaseStrength || before.Strength != now.Strength);
        var merged = before is not null && !_rules.Items.EncumbranceLagsOnStackMerge &&
            now.Units.Any(pair => before.Units.TryGetValue(pair.Key, out var units) && pair.Value > units);
        if (!listed && !strength && !merged || (before?.Level ?? 0) == now.Level) return;
        RecalculateEncumbrance(now.Level);
        _encumbranceMark = EncumbranceNow();
    }

    // The state the recalculation compares, or null without a hero (or stats the character owner cannot answer).
    private EncumbranceMark? EncumbranceNow()
    {
        if (_hero is null || HeroCarryFacts() is not { } carry || _hero.Stats.Query(new(0)).Snapshot is not { } stats) return null;
        var members = HeroHeld().Select(item => item.Entity).ToImmutableHashSet(StringComparer.Ordinal);
        var units = members.Where(_inventory.Ammo.ContainsKey).ToImmutableDictionary(entity => entity, entity => _inventory.Ammo[entity].Units, StringComparer.Ordinal);
        return new(members, units, stats.GetBase(CharacterStat.Strength), stats.GetEffective(CharacterStat.Strength),
            CarryRules.Level(carry.Weight, carry.CarryWeight));
    }

    // A new session's hero has just received its kit: the state before is an empty list at level 0, so the first change recalculates a
    // kit above level 0 (as the original's insertions of the kit would have).
    private void MarkEncumbranceOfNewHero() =>
        _encumbranceMark = EncumbranceNow() is { } now ? now with { Members = [], Units = ImmutableDictionary<string, int>.Empty, Level = 0 } : null;

    // A restored session: the saved effects and timer already hold the level's consequences; the next change compares with this state.
    private void MarkEncumbranceOfRestore() => _encumbranceMark = EncumbranceNow();

    /// <summary>
    /// The recalculation's writes (0x45F820, ledger WE5) for a new level: every entry of effect 158 leaves the hero's list (0x4EA200, whatever
    /// added it), then effect 158 with the item cause is added <see cref="CarryRules.EffectEntries"/> times; the change is recorded for the
    /// level message (<see cref="QueryHeroEncumbranceChange"/>); a level with a drain interval schedules the encumbrance fatigue timer unless
    /// one is pending (WEA2: a pending one keeps its older delay). The session's list holds at most
    /// <see cref="SessionEffectLimits.MaxHeroEffects"/> entries, so a full list takes fewer (the original's has no bound); entries the
    /// effect list's bounds refuse are not added. No revision: it is part of the change that caused it. The animation speed (0x436FA0) is
    /// the host's.
    /// </summary>
    private void RecalculateEncumbrance(int level)
    {
        _encumbranceSettling = true;
        try
        {
            var definition = _encumbranceEffect ?? _effects.Definitions.GetValueOrDefault(CarryRules.EffectNumber);
            var entries = 0;
            if (definition is not null)
            {
                var steps = new List<EffectStep>();
                var kept = 0;
                for (var i = 0; i < _effects.Entries.Count; i++)
                    if (_effects.Entries[i].Number == CarryRules.EffectNumber) steps.Add(new(null, kept));
                    else kept++;
                entries = Math.Min(CarryRules.EffectEntries(level), SessionEffectLimits.MaxHeroEffects - kept);
                steps.AddRange(Enumerable.Repeat(new EffectStep(new SessionEffect(CarryRules.EffectNumber, EffectCause.Item), -1), entries));
                var definitions = new Dictionary<int, SessionEffectDefinition>(_effects.Definitions) { [CarryRules.EffectNumber] = definition };
                var list = _effects.Entries.Where(e => e.Number != CarryRules.EffectNumber)
                    .Concat(Enumerable.Repeat(new SessionEffect(CarryRules.EffectNumber, EffectCause.Item), entries)).ToList();
                if (steps.Count > 0 && EffectDomain.Bounded(list, definitions, innate: HeroInnateRules()) &&
                    PrepareEffects([.. steps], definitions, 0, out var prepared) == GameSessionFailure.None) ApplyEffects(prepared!);
                else
                {
                    // Additions the list's bounds refuse are left out; the removals alone always pass (a removal is never refused).
                    entries = 0;
                    var removals = steps.Where(step => step.Add is null).ToArray();
                    if (removals.Length > 0 && PrepareEffects(removals, definitions, 0, out var removed) == GameSessionFailure.None) ApplyEffects(removed!);
                    entries = _effects.Entries.Count(e => e.Number == CarryRules.EffectNumber);
                }
            }
            if (CarryRules.DrainIntervalMs(level) is { } delay) ScheduleEncumbranceDrain(delay);
            _encumbranceChange = new((_encumbranceChange?.Sequence ?? 0) + 1, level, entries);
        }
        finally { _encumbranceSettling = false; }
    }

    // The schedule (0x45E820, mode 1): nothing while one is pending (WEA2); the session keeps room for it in the queue's budget.
    private void ScheduleEncumbranceDrain(long delay)
    {
        if (IsPending(SessionEventKinds.HeroEncumbrance, null) || _events.Count >= _events.Limits.MaxPending) return;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (now > long.MaxValue - delay) return;
        _events.ScheduleAfter(SessionEventKinds.HeroEncumbrance, GameEventPayload.Of([], now), delay, EventClocks(now));
    }

    /// <summary>
    /// The encumbrance fatigue timer (mode 1 of the fatigue timer 0x45E610, ledger WE8): nothing for a session without a hero or a dead hero;
    /// otherwise its steps are <c>max(1, elapsed s / 80)</c> since it was scheduled and <see cref="CarryRules.Drain"/> of the hero's current
    /// fatigue is added to its fatigue damage (nothing below 5 fatigue, never below 4), one revision when it drains. It does not check the
    /// level first; afterwards it is scheduled again with the drain interval of the level then (none below level 4).
    /// </summary>
    internal GameEventHandlerOutcome FireEncumbranceDrain(GameEventFiring firing)
    {
        if (_hero is null || Resolve(SessionLifeTarget.Hero, out var record, out var facts, out _) != GameSessionFailure.None) return GameEventHandlerOutcome.Completed;
        var status = LifeRules.Evaluate(record.State, facts!);
        if (status.IsDead) return GameEventHandlerOutcome.Completed;
        var scheduledAt = firing.Event.Payload.Values[0];
        var steps = scheduledAt <= firing.NowMs ? LifeRules.FatigueRecoverySteps(scheduledAt, firing.NowMs) : 1;
        var drain = CarryRules.Drain(status.Fatigue, steps);
        if (drain > 0)
        {
            var result = Life(Version, SessionLifeTarget.Hero, true, (state, _, _) => new LifeOutcome(state,
                state with { FatigueDamage = (int)Math.Min((long)state.FatigueDamage + drain, LifeLimits.MaxDamage) }, LifeEvents.None, 0, DeathAnimation.None));
            _passFirings?.Add(new(firing.Event, result));
        }
        if (QueryHeroCarry() is { } carry && CarryRules.DrainIntervalMs(carry.Level) is { } delay) ScheduleEncumbranceDrain(delay);
        return GameEventHandlerOutcome.Completed;
    }
}
