namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1146 (the Trait64 component): an NPC's teleport tile (object field 242), as the object state of its directory entry: the location the component stored, as the original stores it (x in the low word, y in the high word, like
/// <see cref="SessionNpcStandpoint"/>). The component writes the teleport map (field 243, <see cref="SessionTeleportMap"/>) with it. No record is the original's 0, never set.
/// </summary>
public static class SessionTeleportTile
{
    /// <summary>The object field that holds an NPC's teleport tile.</summary>
    public const string Property = "npc.teleport.tile";

    /// <summary>The map change key of the NPC's teleport tile.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-1146 (the Trait64 component, the <c>Target_Tile</c> source): the NPC's teleport tile becomes (<paramref name="x"/>, <paramref name="y"/>) and its teleport map <paramref name="map"/>, in one revision (the two map changes of the
    /// original's two writes). The same pair is <see cref="GameSessionDisposition.NoChange"/>. A map below 1, an entity that is not a mobile object, a stale version and the change budget refuse as the map change does.
    /// </summary>
    public GameSessionMutationResult SetTeleportTile(GameSessionVersion expected, string entity, SessionMapId map, uint x, uint y)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        return ApplyChanges(expected, null, [new(map, SessionTeleportTile.Key(entity), SessionNpcStandpoint.Pack(x, y)), new(map, SessionTeleportMap.Key(entity), map.RuntimeMap)]);
    }

    /// <summary>The teleport tile recorded for the NPC (field 242), or null while it is 0 (never set).</summary>
    public (uint X, uint Y)? QueryTeleportTile(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (_, value) in QueryEntityChanges(entity, SessionTeleportTile.Property))
            return value == 0 ? null : (unchecked((uint)value), unchecked((uint)(value >> 32)));
        return null;
    }

    /// <summary>
    /// ARC-MAGIC-1146 (the Trait64 component, the <c>Caster_Obj</c> and <c>Caster_Owner_Obj</c> sources): the object field 230 of the NPC (the one it flees from, the danger record's fleeing-from) is written outright with <paramref name="target"/>
    /// (the hero by <see cref="SessionSpellCaster.Hero"/>, a critter by its entity, none clears it), whatever the NPC's danger state: no setter step, no stop-flee. The same value is <see cref="GameSessionDisposition.NoChange"/>; an NPC with no
    /// record at the danger bound, an invalid entity or a stale version are refused. One revision.
    /// </summary>
    public GameSessionMutationResult WriteFleeingFrom(GameSessionVersion expected, string entity, string? target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal) || (target is not null && !SessionValidation.Entity(target) && target != SessionSpellCaster.Hero))
            return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        SessionCombatant? flee = target is null ? null : target == SessionSpellCaster.Hero ? SessionCombatant.Hero : SessionCombatant.Critter(target);
        var held = _danger.Get(entity);
        if (held.FleeingFrom == flee) return NoChange();
        if (_danger.Records.Count >= _limits.MaxDangerRecords && !_danger.Records.ContainsKey(entity)) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _danger.Put(held with { FleeingFrom = flee });
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
    }
}
