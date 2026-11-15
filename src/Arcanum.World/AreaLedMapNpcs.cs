using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-2132 (GR14 tail of ARC-WORLD-2118): which critters the shown map's companion group has to hold bodies for. The move list of the hero's change of place (0x4D3760) walks the whole
/// follower tree, so a map NPC that a companion of the hero leads (an NPC the AI host would otherwise hold as its own) travels with the hero; the viewer can only move what the group holds
/// (a placement edit on the destination, <see cref="AreaCompanionGroup.Arrive"/>), so such an NPC gets a body template of its map row as a map NPC the hero leads does
/// (<see cref="AreaCompanionTemplate.ForLedMapNpcs"/>) and is a companion of the group while a leader's chain reaches the hero.
/// </summary>
public static class AreaLedMapNpcs
{
    /// <summary>The critters the hero's follower tree holds now, its direct followers first and each follower's own followers after them (the original's follower-list walk, the move list's): the hero's followers and
    /// the followers of followers. A follower waiting under an NPC leader has left that leader's array and is not in it.</summary>
    public static IReadOnlyList<string> HeroTree(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.QueryFollowerTree(SessionEventKinds.PlayerEntity);
    }

    /// <summary>
    /// ARC-WORLD-2161 (tail of #2132: the original's move list carries the whole follower tree as objects, and its join puts the joiner's own followers in the leader's list): the followers of the
    /// hero's change of place the shown group holds no body for, with what the session needs to stand each on the destination. <paramref name="travelling"/> is the session's move list; a follower
    /// the group or the created critters hold is left out (their own edits carry them), as is one whose home the session cannot name. <paramref name="rowCell"/> answers the stored cell of an NPC row of
    /// the shown map (null when the map lists none). An NPC at home on the shown map stands on its stored cell; one visiting from another map (its away change) has the stored cell recorded
    /// for it there, and one without a record is left behind and named in <paramref name="unresolved"/>.
    /// </summary>
    public static IReadOnlyList<AreaBodylessFollower> Bodyless(GameSessionOwner session, SessionMapId shown, IEnumerable<string> travelling, Func<string, bool> hasBody,
        Func<string, CellCoord?> rowCell, ICollection<string>? unresolved = null)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(travelling); ArgumentNullException.ThrowIfNull(hasBody); ArgumentNullException.ThrowIfNull(rowCell);
        var found = new List<AreaBodylessFollower>();
        foreach (var entity in travelling.Distinct(StringComparer.Ordinal))
        {
            if (hasBody(entity) || SessionCreatedObject.KindOf(entity) == SessionCreatedKind.Critter) continue;
            var follower = Resolve(session, shown, entity, rowCell);
            if (follower is not null) found.Add(follower);
            else unresolved?.Add(entity);
        }
        return found.AsReadOnly();
    }

    private static AreaBodylessFollower? Resolve(GameSessionOwner session, SessionMapId shown, string entity, Func<string, CellCoord?> rowCell)
    {
        if (EntityHomes.HomeOf(session, entity) is { } away)
        {
            // Its away change lives on its home map: another map than the shown one, with the stored cell recorded there (a visitor of the shown map).
            if (away == shown.RuntimeMap) return null;
            // It must stand on the shown map now (a visitor of a third map is not carried from here).
            if (AreaCompanionPlacement.Where(session, new(away), entity) is not { Error: null } where || where.Map != shown) return null;
            return AreaCompanionPlacement.HomeCell(session, new(away), entity) is { } stored ? new(entity, new(away), stored) : null;
        }
        return rowCell(entity) is { } cell ? new(entity, shown, cell) : null;
    }
}

/// <summary>ARC-WORLD-2161: a follower of the hero's change of place with no body in the shown group: its entity, the map whose data lists it and the stored cell it has there.</summary>
public sealed record AreaBodylessFollower(string Entity, SessionMapId Home, CellCoord HomeWorld);
