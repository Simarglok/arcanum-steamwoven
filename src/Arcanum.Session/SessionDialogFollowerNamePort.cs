using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog follower name seam over a <see cref="GameSessionOwner"/> (ARC-DEV-227, ledger PN1–PN3): the <c>PA</c> condition asks whether some direct follower of the PC that is not mind-controlled
/// carries a name number (the object name field 0x16). The session's party gives the followers (<see cref="GameSessionOwner.QueryParty"/>, read at each call) and the host gives each follower's
/// stored name number and mind-controlled flag (<paramref name="factsOf"/>: the object's own spell flags, field 0x14, bit 0x8000000). A follower the host cannot read throws: the answer would be a
/// guess, and the conversation fails with a query failure. A PC other than <paramref name="playerCharacter"/> throws as well. The port must be rebuilt when the host replaces the owner.
/// </summary>
/// <param name="session">The owner.</param>
/// <param name="playerCharacter">The one PC the port answers for.</param>
/// <param name="factsOf">A follower's name number and whether it is mind-controlled, or null when the host cannot read its record.</param>
public sealed class SessionDialogFollowerNamePort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<string, (int Name, bool MindControlled)?> factsOf) : IDialogFollowerNamePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<string, (int Name, bool MindControlled)?> _factsOf = factsOf ?? throw new ArgumentNullException(nameof(factsOf));

    public bool HasFollowerNamed(DialogFollowerNameQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        foreach (var follower in _session.QueryParty().Followers)
        {
            var facts = _factsOf(follower.Entity) ?? throw new InvalidOperationException("A follower's record cannot be read.");
            if (!facts.MindControlled && facts.Name == query.Name) return true;
        }
        return false;
    }
}
