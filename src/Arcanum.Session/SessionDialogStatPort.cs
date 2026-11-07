using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog PC stat seam over a <see cref="GameSessionOwner"/> (ARC-DEV-192, ledger DCC2, DCC3): the stat conditions <c>ch</c>, <c>pe</c>, <c>le</c>, <c>al</c>,
/// <c>ma</c>, <c>ta</c> and <c>na</c> read the session hero's stat by the original's number as <see cref="GameSessionOwner.QueryHeroScriptStat"/> gives it (the stat
/// getter 0x4B0490: strength to charisma and the aptitude with the life's poison and crippled legs, the stored level and alignment); null without a hero or for a stat
/// the session does not hold. A PC other than <paramref name="playerCharacter"/> throws. The session is read at each call.
/// </summary>
public sealed class SessionDialogStatPort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogStatPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public int? Stat(DialogStatQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return _session.QueryHeroScriptStat(query.Stat);
    }
}
