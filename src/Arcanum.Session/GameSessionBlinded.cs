using Arcanum.Simulation;
using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-035 (magic audit MA-4; the executable's blinded critter flag 0x80, set by 0x4B81B0 and cleared by the plain write of 0x454A10, ledger SO6): sets or clears the blinded condition
    /// of a critter's life state, the one condition the combat resolver (<see cref="CombatantRules.Conditions"/>), the skill checks and the AI gate's blindness recovery (a 1 in <c>N</c> chance each pass)
    /// read. The state is the session's life record, else the target's loaded state (the data's flags); a state already as asked is <see cref="GameSessionDisposition.NoChange"/>. The hero's record
    /// is the hero's; an NPC with no record gets one (counted in the life-record budget). A hero or a record that does not exist is <see cref="GameSessionFailure.InvalidLifeTarget"/>.
    /// </summary>
    [OriginalFunction(0x4B81B0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult SetBlinded(GameSessionVersion expected, SessionLifeTarget target, bool blinded)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        static LifeState With(LifeState state, bool on) =>
            state with { Conditions = on ? state.Conditions | LifeConditions.Blinded : state.Conditions & ~LifeConditions.Blinded };
        if (target.IsHero)
        {
            if (_hero is null || _life.Hero is not { } hero) return Reject(GameSessionFailure.InvalidLifeTarget);
            var next = With(hero.State, blinded);
            if (next == hero.State) return NoChange();
            if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
            _life.Hero = hero with { State = next };
            return Changed();
        }
        if (target.Entity is not { } entity || !SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidLifeTarget);
        var recorded = _life.Critters.TryGetValue(entity, out var record);
        var state = recorded ? record.State : target.Loaded;
        var changed = With(state, blinded);
        if (changed == state) return NoChange();
        if (!recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _life.Critters[entity] = new(changed, recorded ? record.DeathTimeMs : null);
        return Changed();
    }

    /// <summary>ARC-MAGIC-035: clears the blinded condition of an NPC that has a life record, with no loaded state (the END of a spell after its critter left the map). No record is <see cref="GameSessionFailure.InvalidLifeTarget"/>.</summary>
    public GameSessionMutationResult ClearRecordedBlinded(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidLifeTarget);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!_life.Critters.TryGetValue(entity, out var record)) return Reject(GameSessionFailure.InvalidLifeTarget);
        var next = record.State with { Conditions = record.State.Conditions & ~LifeConditions.Blinded };
        if (next == record.State) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _life.Critters[entity] = record with { State = next };
        return Changed();
    }
}
