using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog purse seam over a <see cref="GameSessionOwner"/> (ARC-SCRIPT-1304): the hero's own gold for the price of a paid <c>r:</c> rumor
/// (<see cref="GameSessionOwner.QueryGold"/>: the hero's stack only, as 0x464780 reads it; the followers' gold does not count here, unlike the <c>$$</c>
/// condition). A PC other than <paramref name="playerCharacter"/> or a session without a hero throws on a read (the conversation records a query failure).
/// The session is read at each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogPursePort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogPcGoldPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public int PcGold(DialogGoldQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        if (_session.QueryHero() is null) throw new InvalidOperationException("The session has no hero.");
        return _session.QueryGold();
    }
}
