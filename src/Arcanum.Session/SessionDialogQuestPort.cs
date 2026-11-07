using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog quest log seam over a <see cref="GameSessionOwner"/> (ARC-DEV-180, ledger DCC4): the <c>qu</c>, <c>qa</c> and <c>qb</c> conditions read
/// the state of a quest in the PC's log, the one the <c>QU</c> action writes (<see cref="GameSessionOwner.SetQuestState"/>). As the original's
/// quest state getter (0x4C4CB0) answers for a PC: the stored state of its record, 6 (Botched) when the record's botched mark is set, 0
/// (Unknown) for a quest never changed. A PC other than <paramref name="playerCharacter"/> throws (the original answers the quest's shared
/// state for an object that is not a PC; the session has the one PC). The session is read at each call; the port must be rebuilt when the
/// host replaces the owner.
/// </summary>
public sealed class SessionDialogQuestPort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogQuestPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    [Simulation.OriginalFunction(0x4C4CB0, Simulation.OriginalFunctionScope.Partial)]
    public int State(DialogQuestQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return (int)(_session.QueryQuest(query.Quest)?.Effective ?? SessionQuestState.Unknown);
    }
}
