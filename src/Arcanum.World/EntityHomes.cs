using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-072 (MIG1, TM3, FM3): where an NPC's stored record lies, by identity. A travelling NPC keeps its identity on every map
/// (FM4); the session records it as one map-local change of the NPC on its home map, <see cref="AwayProperty"/>, naming the
/// runtime map it stands on while it is elsewhere (ARC-WORLD-017). So the map that carries that change is the NPC's home map,
/// whether or not this run ever loaded it. An NPC that stands at home has no such change; its record is found among the held maps.
/// </summary>
public static class EntityHomes
{
    /// <summary>The change on the home map that names the map the NPC stands on (absent: at home).</summary>
    public const string AwayProperty = "npc.map";

    /// <summary>The runtime map whose stored objects hold the NPC while it stands elsewhere, or null (it stands at home, or the
    /// session records no such change). With several (a corrupt record) the lowest map number.</summary>
    public static int? HomeOf(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        var away = session.QueryEntityChanges(entity, AwayProperty);
        return away.Count == 0 ? null : away[0].Map.RuntimeMap;
    }

    /// <summary>
    /// ARC-DEV-337 (B52): <see cref="HomeOf"/>, else the map that records the NPC's stored cell. A companion that stands on its home map number but
    /// in another window of it (the main world map) has no away record, yet its stored cell names the map that lists it, so its record is still found
    /// by identity. Null for an NPC with neither record.
    /// </summary>
    public static int? RecordedHomeOf(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (HomeOf(session, entity) is { } away) return away;
        var stored = session.QueryEntityChanges(entity, AreaCompanionPlacement.HomeProperty);
        return stored.Count == 0 ? null : stored[0].Map.RuntimeMap;
    }
}
