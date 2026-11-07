using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog spell college seam over a <see cref="GameSessionOwner"/> (ARC-DEV-197, ledger CK1–CK3): the <c>SC</c> condition reads the session hero's rank in a college from its spell book
/// (<see cref="GameSessionOwner.QueryHeroSpellBook"/>, the college ranks the original's getter 0x4B1AB0 reads). Null without a hero or for a college outside 0..15; a PC other than
/// <paramref name="playerCharacter"/> throws. The session is read at each call.
/// </summary>
public sealed class SessionDialogCollegePort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogCollegePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public int? Rank(DialogCollegeQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return _session.QueryHeroSpellBook() is { Ranks.IsDefault: false } book && query.College >= 0 && query.College < book.Ranks.Length ? book.Ranks[query.College] : null;
    }
}
