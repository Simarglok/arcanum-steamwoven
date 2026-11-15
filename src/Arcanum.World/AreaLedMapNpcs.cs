using Arcanum.Session;

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
}
