using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>Bounds of an NPC's durable effect list (ARC-DEV-348; project policy, the source has none, ledger EF1).</summary>
public static class SessionNpcEffectLimits
{
    /// <summary>Entries one NPC holds, loaded ones included (64: the hero's capacity before ARC-DEV-366 raised it for the tech books, which only the
    /// hero carries through an item event).</summary>
    public const int MaxEntries = 64;
    /// <summary>NPCs the session keeps a list for at once.</summary>
    public const int MaxCritters = 4096;
}

/// <summary>One NPC's saved effect list (the <c>effects</c> section's <c>critters</c>, schema 87): its entries in list order and the rule of each number they use, ascending by number.</summary>
public sealed record SessionCritterEffectRecord(string Entity, IReadOnlyList<SessionEffect> Entries, IReadOnlyList<SessionEffectDefinition> Definitions);

/// <summary>An NPC's effect list: <see cref="Entries"/> in list order, the rule of each number they use (<see cref="Recorded"/>: the session holds the list; else it is the list the NPC's map data stores).</summary>
public sealed record SessionNpcEffectsView(string Entity, IReadOnlyList<SessionEffect> Entries, IReadOnlyList<SessionEffectDefinition> Definitions, bool Recorded)
{
    /// <summary>Entries of <paramref name="number"/> in the list (0x4EA4A0, EF4; the hero's view marks the function).</summary>
    public int Count(int number) => Entries.Count(e => e.Number == number);
}

/// <summary>One NPC's list in the effect domain: the entries and the rule of each number in use.</summary>
internal sealed class CritterEffectList
{
    internal List<SessionEffect> Entries { get; } = [];
    internal Dictionary<int, SessionEffectDefinition> Definitions { get; } = [];
}

/// <summary>
/// ARC-DEV-348 (E1-d of the missing-systems plan, magic plan P1): the effect list of an NPC. The original keeps one list per PC or NPC
/// (<c>effect_add</c> 0x4E9F70 takes either, ledger EF1); until now the session kept the hero's alone. An NPC's list starts as the one its map or prototype
/// stores (the two effect arrays, <see cref="SessionLifeTarget.LoadedEffects"/>) and is the session's once a call changes it: the session's list then wins,
/// whole, as an NPC's life record does. The maxima the list gives (hit points, fatigue) are laid over the host's life facts wherever the session reads
/// them (<c>WithWrittenHolds</c>), and an add or a removal moves the NPC's damage by the change of each maximum (EF1, EF5), clamped at no damage.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The rule of an effect number the lists loaded from map data name (the effect tables read as <see cref="SessionEffectDefinition.FromRule"/>), set by the host that reads them.
    /// Without it, or for a number it does not know, a loaded entry has no changes (the source reads a number with no table entry as none), and a later add of that number
    /// with a rule replaces it.
    /// </summary>
    public Func<int, SessionEffectDefinition?>? NpcEffectRules { get; set; }

    /// <summary>An NPC's effect list (the session's, else the loaded one), or null for the hero or an entity that is no mobile NPC.</summary>
    public SessionNpcEffectsView? QueryNpcEffects(SessionLifeTarget target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsHero || !NpcEffectEntity(target.Entity)) return null;
        if (_effects.Critters.TryGetValue(target.Entity!, out var list))
            return new(target.Entity!, Array.AsReadOnly(list.Entries.ToArray()), Array.AsReadOnly(list.Definitions.OrderBy(d => d.Key).Select(d => d.Value).ToArray()), true);
        var entries = target.LoadedEffects.ToArray();
        var definitions = entries.Select(e => e.Number).Distinct().Order().Select(number => LoadedRule(number)).ToArray();
        return new(target.Entity!, Array.AsReadOnly(entries), Array.AsReadOnly(definitions), false);
    }

    /// <summary>
    /// Appends <paramref name="count"/> entries of an effect with <paramref name="cause"/> to an NPC's list (0x4E9F70, EF1; a spell's Effect component, a script's add-effect action), each
    /// moving the NPC's damage by the change of its maxima. Refused, in order: a target that is no mobile NPC, facts or a loaded state the life rules refuse, a loaded list the session does not
    /// admit (<see cref="GameSessionFailure.InvalidLifeTarget"/>, <see cref="GameSessionFailure.InvalidLifeFacts"/>, <see cref="GameSessionFailure.InvalidLifeState"/>,
    /// <see cref="GameSessionFailure.InvalidEffect"/>); a rule the session does not admit, an undefined cause or a count below 1 (<see cref="GameSessionFailure.InvalidEffect"/>); a number in use with
    /// other changes (<see cref="GameSessionFailure.EffectMismatch"/>); the version; a list beyond <see cref="SessionNpcEffectLimits.MaxEntries"/> or more than
    /// <see cref="SessionNpcEffectLimits.MaxCritters"/> lists (<see cref="GameSessionFailure.EffectCapacityExceeded"/>); entries that leave the maxima beyond the life rules' bounds
    /// (<see cref="GameSessionFailure.InvalidEffect"/>); the life record, event and revision budgets. One revision. NPC causes are not limited as the hero's are: the race and gender setters own none.
    /// </summary>
    public SessionEffectResult AddNpcEffect(GameSessionVersion expected, SessionLifeTarget target, SessionEffectDefinition definition, EffectCause cause, int count = 1)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (!EffectDomain.IsValid(definition) || !Enum.IsDefined(cause) || count < 1) return RejectEffect(GameSessionFailure.InvalidEffect);
        return ChangeNpc(expected, target, definition, cause, count, null, 0, null);
    }

    /// <summary>Removes the first entry of <paramref name="number"/>, <paramref name="count"/> times (0x4EA100, EF2), whatever added it; NoChange when there is none.</summary>
    public SessionEffectResult RemoveNpcEffect(GameSessionVersion expected, SessionLifeTarget target, int number, int count = 1)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (count < 1 || number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber) return RejectEffect(GameSessionFailure.InvalidEffect);
        return ChangeNpc(expected, target, null, default, 0, number, count, null);
    }

    /// <summary>Removes every entry of <paramref name="number"/> (0x4EA200, EF3).</summary>
    public SessionEffectResult RemoveAllNpcEffects(GameSessionVersion expected, SessionLifeTarget target, int number)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber) return RejectEffect(GameSessionFailure.InvalidEffect);
        return ChangeNpc(expected, target, null, default, 0, number, int.MaxValue, null);
    }

    /// <summary>Removes the first entry with <paramref name="cause"/>, or every one when <paramref name="all"/> (0x4EA2E0, 0x4EA3C0, EF3).</summary>
    public SessionEffectResult RemoveNpcEffectsCausedBy(GameSessionVersion expected, SessionLifeTarget target, EffectCause cause, bool all = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(cause)) return RejectEffect(GameSessionFailure.InvalidEffect);
        return ChangeNpc(expected, target, null, default, 0, null, all ? int.MaxValue : 1, cause);
    }

    // A mobile NPC: the hero has its own list, a static object no critter state.
    private static bool NpcEffectEntity(string? entity) => entity is not null && SessionValidation.Entity(entity)
        && entity != SessionEventKinds.PlayerEntity && entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal);

    // A loaded number's rule: the host's, else none (no changes).
    private SessionEffectDefinition LoadedRule(int number) =>
        number is >= SessionEffectLimits.FirstNumber and <= SessionEffectLimits.LastNumber && NpcEffectRules?.Invoke(number) is { } rule && rule.Number == number && EffectDomain.IsValid(rule) ? rule : new(number, []);

    // The facts with the maxima an effect list gives (the host's facts carry none: the list replaces them).
    private static LifeFacts WithCritterEffects(LifeFacts facts, IEnumerable<SessionEffect> entries, IReadOnlyDictionary<int, SessionEffectDefinition> definitions) =>
        facts with
        {
            MaxHitPointEffects = EffectDomain.Modifiers(entries, definitions, EffectSpecial.MaxHitPoints),
            MaxFatigueEffects = EffectDomain.Modifiers(entries, definitions, EffectSpecial.MaxFatigue),
        };

    // The session's list laid over the host's facts, when it holds one for the NPC (the hold flags are WithWrittenHolds').
    // Facts the life rules refuse with the list laid over them (a level-up or a stronger stat after the add) keep the host's maxima: the life API never throws for a list.
    private LifeFacts WithWrittenEffects(string entity, LifeFacts facts)
    {
        if (!_effects.Critters.TryGetValue(entity, out var list)) return facts;
        var combined = WithCritterEffects(facts, list.Entries, list.Definitions);
        return LifeRules.IsValid(combined) ? combined : facts;
    }

    // One add or one removal run (the steps the hero's Commit takes), on a trial list: each step moves the damage by the change of the maxima, the life rules must admit
    // the facts after each, then the budgets; nothing is written before every check passed.
    private SessionEffectResult ChangeNpc(GameSessionVersion expected, SessionLifeTarget target, SessionEffectDefinition? add, EffectCause addCause, int addCount,
        int? removeNumber, int removeCount, EffectCause? removeCause)
    {
        if (target.IsHero || !NpcEffectEntity(target.Entity)) return RejectEffect(GameSessionFailure.InvalidLifeTarget);
        var entity = target.Entity!;
        if (!LifeRules.IsValid(target.Facts)) return RejectEffect(GameSessionFailure.InvalidLifeFacts);
        if (!LifeRules.IsValid(target.Loaded)) return RejectEffect(GameSessionFailure.InvalidLifeState);
        var held = _effects.Critters.TryGetValue(entity, out var list);
        var entries = new List<SessionEffect>();
        var definitions = new Dictionary<int, SessionEffectDefinition>();
        if (held)
        {
            entries.AddRange(list!.Entries);
            foreach (var pair in list.Definitions) definitions[pair.Key] = pair.Value;
        }
        else
        {
            // The loaded list becomes the session's: every number in the tables' range, every cause one of the source's.
            if (target.LoadedEffects.Count > SessionNpcEffectLimits.MaxEntries) return RejectEffect(GameSessionFailure.EffectCapacityExceeded);
            foreach (var entry in target.LoadedEffects)
            {
                if (entry.Number is < SessionEffectLimits.FirstNumber or > SessionEffectLimits.LastNumber || !Enum.IsDefined(entry.Cause)) return RejectEffect(GameSessionFailure.InvalidEffect);
                entries.Add(entry);
                if (!definitions.ContainsKey(entry.Number)) definitions[entry.Number] = LoadedRule(entry.Number);
            }
            if (definitions.Values.Any(d => !EffectDomain.IsValid(d))) return RejectEffect(GameSessionFailure.InvalidEffect);
        }
        if (add is not null && definitions.TryGetValue(add.Number, out var known) && !known.Changes.IsEmpty && !known.SameAs(add))
            return RejectEffect(GameSessionFailure.EffectMismatch);
        if (!IsCurrent(expected)) return RejectEffect(GameSessionFailure.StaleVersion);
        if (add is not null && addCount > SessionNpcEffectLimits.MaxEntries - entries.Count) return RejectEffect(GameSessionFailure.EffectCapacityExceeded);

        var steps = new List<EffectStep>();
        if (add is not null)
            steps.AddRange(Enumerable.Repeat(new EffectStep(new SessionEffect(add.Number, addCause), -1), addCount));
        else
        {
            var shifted = 0;
            for (var i = 0; i < entries.Count && steps.Count < removeCount; i++)
            {
                if (removeNumber is { } k ? entries[i].Number != k : entries[i].Cause != removeCause) continue;
                steps.Add(new(null, i - shifted));
                shifted++;
            }
        }
        if (steps.Count == 0) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, 0, null, null, Version);
        // A change adopts the loaded list: an add or a removal alike takes a place in the session's lists.
        if (!held && _effects.Critters.Count >= SessionNpcEffectLimits.MaxCritters) return RejectEffect(GameSessionFailure.EffectCapacityExceeded);
        if (add is not null) definitions[add.Number] = add;

        var recorded = _life.Critters.TryGetValue(entity, out var life);
        if (!recorded) life = new(target.Loaded, null);
        var host = WithWrittenHolds(entity, target.Facts!);
        var state = life.State;
        var facts = WithCritterEffects(host, entries, definitions);
        if (!LifeRules.IsValid(facts)) return RejectEffect(GameSessionFailure.InvalidEffect);
        foreach (var step in steps)
        {
            if (step.Add is { } entry) entries.Add(entry); else entries.RemoveAt(step.RemoveAt);
            var next = WithCritterEffects(host, entries, definitions);
            if (!LifeRules.IsValid(next)) return RejectEffect(GameSessionFailure.InvalidEffect);
            state = state with
            {
                HitPointDamage = Shift(state.HitPointDamage, LifeRules.MaxHitPoints(next, state.PoisonLevel) - (long)LifeRules.MaxHitPoints(facts, state.PoisonLevel)),
                FatigueDamage = Shift(state.FatigueDamage, LifeRules.MaxFatigue(next) - (long)LifeRules.MaxFatigue(facts)),
            };
            facts = next;
        }
        var used = entries.Select(e => e.Number).ToHashSet();
        foreach (var number in definitions.Keys.Where(n => !used.Contains(n)).ToArray()) definitions.Remove(number);
        // Bounded for any subset of the entries, over the largest base maximum this NPC's facts give (not the hero's ceiling): no later removal can be refused for the maxima.
        var bare = WithCritterEffects(host, [], definitions);
        var ceiling = Math.Max(0L, Math.Max(LifeRules.MaxHitPoints(bare, 0), LifeRules.MaxFatigue(bare)));
        if (!EffectDomain.Bounded(entries, definitions, ceiling)) return RejectEffect(GameSessionFailure.InvalidEffect);

        var lifeChanged = state != life.State;
        if (lifeChanged && !recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return RejectEffect(GameSessionFailure.BudgetExceeded);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var map = _actor.Map.RuntimeMap;
        var timers = lifeChanged
            ? Timers(false, life.State, state, null).Select(timer => TimerEvent(timer, target, map, now, 1)).Where(pending => !IsPending(pending.Kind, entity)).ToArray()
            : [];
        // One map per NPC's timers (as every life step keeps it): a change on another map than its pending timers' takes them along; that needs the queue outside a pass.
        var stray = lifeChanged ? StrayMaps(entity, map) : [];
        if (stray.Length > 0 && _passWorld is not null) return RejectEffect(GameSessionFailure.InvalidEventPass);
        if (timers.Length > 0 && (_events.Count > _events.Limits.MaxPending - timers.Length || _events.NextSequence > long.MaxValue - timers.Length ||
            now > long.MaxValue - SessionEventKinds.RestingDelayMs)) return RejectEffect(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return RejectEffect(GameSessionFailure.RevisionExhausted);

        var record = held ? list! : new CritterEffectList();
        record.Entries.Clear();
        record.Entries.AddRange(entries);
        record.Definitions.Clear();
        foreach (var pair in definitions) record.Definitions[pair.Key] = pair.Value;
        _effects.Critters[entity] = record;
        var next2 = new SessionLifeRecord(state, life.DeathTimeMs);
        if (lifeChanged) _life.Critters[entity] = next2;
        foreach (var from in stray)
            if (_events.Relocate(from, map, e => e.Code == entity).Failure != GameEventFailure.None)
                throw new InvalidOperationException("Timer relocation failed after admission.");
        foreach (var (kind, payload) in timers)
            if (_events.ScheduleAfter(kind, payload, SessionEventKinds.Delay(TimerOf(kind)), EventClocks(now)).Disposition != GameEventScheduleDisposition.Added)
                throw new InvalidOperationException("Timer scheduling failed after admission.");
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, steps.Count, lifeChanged ? next2 : null, null, new(_identity, _revision));

        static int Shift(int damage, long change) => (int)Math.Clamp(damage + change, 0, LifeLimits.MaxDamage);
    }

    // A save's NPC lists: ascending distinct mobile NPC entities, each list within the bound with a rule admitted and in use for every number, causes the source's. The maxima's bound depends on the NPC's
    // facts, which a restore does not have: the overlay checks the combined facts on use (WithWrittenEffects).
    private static GameSessionFailure ImportCritterEffects(IReadOnlyList<SessionCritterEffectRecord> saved, EffectDomain effects)
    {
        if (saved.Count > SessionNpcEffectLimits.MaxCritters) return GameSessionFailure.EffectCapacityExceeded;
        string? previous = null;
        foreach (var record in saved)
        {
            if (record is null || !NpcEffectEntity(record.Entity) || (previous is not null && string.CompareOrdinal(previous, record.Entity) >= 0)) return GameSessionFailure.InvalidSnapshot;
            previous = record.Entity;
            if (record.Entries.Count > SessionNpcEffectLimits.MaxEntries) return GameSessionFailure.EffectCapacityExceeded;
            var list = new CritterEffectList();
            var last = int.MinValue;
            foreach (var definition in record.Definitions)
            {
                if (!EffectDomain.IsValid(definition) || definition.Number <= last) return GameSessionFailure.InvalidEffect;
                last = definition.Number;
                list.Definitions.Add(definition.Number, definition);
            }
            foreach (var entry in record.Entries)
            {
                if (!Enum.IsDefined(entry.Cause) || !list.Definitions.ContainsKey(entry.Number)) return GameSessionFailure.InvalidEffect;
                list.Entries.Add(entry);
            }
            var used = list.Entries.Select(e => e.Number).ToHashSet();
            if (list.Definitions.Keys.Any(number => !used.Contains(number))) return GameSessionFailure.InvalidEffect;
            effects.Critters.Add(record.Entity, list);
        }
        return GameSessionFailure.None;
    }
}
