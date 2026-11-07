using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog item-holding seam over a <see cref="GameSessionOwner"/> (ARC-DEV-194, ledger DCC6, IH1–IH4): the <c>in</c> and <c>ni</c> conditions. An object holds an item when its own list has one whose
/// name field equals the asked name (the item search 0x462480). <see cref="PartyHolds"/> asks the hero's lists (the starting kit, the items taken from the world, the made items) and then each
/// direct follower's list in the follower array's order, as the original's follower list goes. <see cref="NpcHolds"/> asks the NPC's list and, when the NPC has a substitute container
/// (<paramref name="substituteOf"/>, 0x45F650), the container's. A list the session does not know counts as empty. A PC other than <paramref name="playerCharacter"/> holds nothing. The session is read at
/// each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
/// <param name="session">The owner.</param>
/// <param name="playerCharacter">The one PC the port answers for.</param>
/// <param name="entityOf">The session entity of a dialog NPC, or null for one the host does not map.</param>
/// <param name="substituteOf">The substitute container of an NPC entity (no leader, within 20 tiles of it, BT11), or null.</param>
/// <param name="prepare">Called with an entity before its list is read, for a host that attaches the seed of the map listing it first (a companion carried here from its home map).</param>
public sealed class SessionDialogItemHoldPort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    Func<string, string?> substituteOf,
    Action<string>? prepare = null) : IDialogItemHoldPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly Func<string, string?> _substituteOf = substituteOf ?? throw new ArgumentNullException(nameof(substituteOf));

    public bool PartyHolds(DialogItemQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) return false;
        if (_session.QueryHeroKit().Any(item => item.Kind.Name == query.ItemName) || _session.QueryPcInventory().Any(item => item.Kind.Name == query.ItemName)
            || _session.QueryMadeItems().Any(item => item.Kind.Name == query.ItemName)) return true;
        foreach (var follower in _session.QueryParty().Followers)
            if (Holds(follower.Entity, query.ItemName)) return true;
        return false;
    }

    public bool NpcHolds(DialogItemQuery query)
    {
        if (query.PlayerCharacter != playerCharacter || _entityOf(query.Npc) is not { } npc) return false;
        return Holds(npc, query.ItemName) || _substituteOf(npc) is { } container && Holds(container, query.ItemName);
    }

    private bool Holds(string holder, int name)
    {
        prepare?.Invoke(holder);
        return _session.QueryHolderItems(holder)?.Any(item => item.Facts.Kind.Name == name) == true;
    }
}
