namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-095 (MIG7d, TM10, GR36): one link field of an object's directory entry, found by the holder's identity: the holder's entity, the
/// field, the map the holder stood on when the link was last written and the targets. A target is an object's identity, the entity, or the
/// hero (<see cref="SessionCombatant.IsHero"/>); it is never a position or a map-local name, so it names the same object on any map. The
/// fields are <see cref="ObjectLinkVocabulary"/>'s: a combat focus, who hit the holder last and who it flees from hold one target, the
/// enemy list holds its entries in the original's order (appended at the end, a removal fills the hole with the last entry). It is a row of the
/// snapshot section <see cref="GameSessionSnapshot.Objects"/> (<see cref="SessionObjectRecords.Links"/>, schema 65); schema 64 saved the same
/// handles inside the danger records and the 64 to 65 upgrade step moves them.
/// </summary>
public sealed record SessionObjectLink(string Entity, string Property, SessionMapId Map, IReadOnlyList<SessionCombatant> Targets);

/// <summary>
/// ARC-DEV-095 (MIG7d, TM10): the link fields an object's entry holds. They are the handles the original keeps in fields 0x11B (combat focus), 0xE6
/// (fleeing from), 0x11C (who hit me last) and 0x130 (the enemy list) of an NPC; the owner's danger domain keeps the danger bits beside them and
/// composes both as a <see cref="SessionDanger"/>. The leader link of a follower is the object field <c>follow.pc</c> (MIG7a); an NPC leader,
/// the summoner and the other links of TM10 are not modelled yet (GR31).
/// </summary>
public static class ObjectLinkVocabulary
{
    /// <summary>The combat focus (field 0x11B): one target.</summary>
    public const string Focus = "link.focus";
    /// <summary>Who the holder is fleeing from (field 0xE6): one target.</summary>
    public const string FleeingFrom = "link.fleeing";
    /// <summary>Who hit the holder last (field 0x11C): one target.</summary>
    public const string HitBy = "link.hitby";
    /// <summary>The enemy list (field 0x130): at least one target, no repeat, in the list's order.</summary>
    public const string Enemies = "link.enemies";

    /// <summary>Whether the text names a link field.</summary>
    public static bool IsLink(string? property) => property is Focus or FleeingFrom or HitBy or Enemies;

    /// <summary>Whether the link field holds a list (the enemy list) and not one target.</summary>
    public static bool IsList(string property) => property == Enemies;
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-095 (MIG7d, TM10): the link fields the entry of <paramref name="entity"/> holds, each with the map it was written on, in field
    /// order (ordinal). Empty for an object with no link; a read of the current state, in no revision. An invalid entity id throws.
    /// </summary>
    public IReadOnlyList<SessionObjectLink> QueryObjectLinks(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return Array.AsReadOnly(_danger.LinksOf(entity).ToArray());
    }

    /// <summary>
    /// ARC-DEV-095 (MIG7d, TM10): every link field held by an object whose links were last written on <paramref name="map"/>, by holder then
    /// field (ordinal). A read of the current state, in no revision.
    /// </summary>
    public IReadOnlyList<SessionObjectLink> QueryObjectLinks(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_danger.CaptureLinks().Where(link => link.Map == map).ToArray());
    }

    // GQ7 B / GR36 (OM30, OM31, FM7): the hero's map has just opened (a change of place to it, a load). Under the Original setting each link of
    // an object that stood on that map to an object that is not there is erased, as the original's handle rebinding finds nothing for it: a target
    // is there when it is the hero, travels with the hero (it arrives together), is not recorded as standing away on another map, or stands away
    // on this map. An object with no away record stands at home, which this model takes to be the map the link was written on (the home map of an
    // object is not known when the session has not recorded it away). Returns the holders changed.
    private int ApplyOpenMapLinkRule(SessionMapId map, IReadOnlyCollection<string> travelling)
    {
        if (!_rules.Lifecycle.OpeningMapNullsLinksToLeftObjects) return 0;
        var gone = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Gone(SessionCombatant target)
        {
            if (target.IsHero) return false;
            var entity = target.Entity!;
            if (gone.TryGetValue(entity, out var known)) return known;
            var away = QueryEntityChanges(entity, EntityAwayProperty);
            var left = !travelling.Contains(entity) && away.Count > 0 && away[0].Value != map.RuntimeMap;
            gone[entity] = left;
            return left;
        }

        var changed = 0;
        foreach (var holder in _danger.Records.Values.Where(record => _danger.LinkMaps.TryGetValue(record.Entity, out var at) && at == map).ToArray())
        {
            var next = holder with
            {
                Focus = holder.Focus is { } focus && Gone(focus) ? null : holder.Focus,
                FleeingFrom = holder.FleeingFrom is { } fleeing && Gone(fleeing) ? null : holder.FleeingFrom,
                HitBy = holder.HitBy is { } hitBy && Gone(hitBy) ? null : holder.HitBy,
                Enemies = holder.Enemies.Where(enemy => !Gone(enemy)).ToArray(),
            };
            if (next.SameAs(holder)) continue;
            _danger.Put(next);
            changed++;
        }
        return changed;
    }

    // The property of the away record that names the map an NPC stands on while it is elsewhere (World.EntityHomes.AwayProperty).
    private const string EntityAwayProperty = "npc.map";
}
