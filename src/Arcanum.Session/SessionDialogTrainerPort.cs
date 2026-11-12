using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog trainer seam over a <see cref="GameSessionOwner"/> (ARC-SCRIPT-1150, ledger DTR1–DTR9): the skill names the <c>t:</c> menu shows (the host's
/// <c>skill.mes</c> texts) and the hero's own gold (<see cref="GameSessionOwner.QueryGold"/>: the hero's stack only, as 0x464780 reads it; the
/// followers' gold does not count here, unlike the <c>$$</c> condition). A PC other than <paramref name="playerCharacter"/> or a session without a hero
/// throws on a read (the conversation records a query failure). The session is read at each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogTrainerPort(GameSessionOwner session, DialogParticipantId playerCharacter, Func<int, string?> skillName) : IDialogTrainerPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<int, string?> _skillName = skillName ?? throw new ArgumentNullException(nameof(skillName));

    public string? SkillName(int skill) => _skillName(skill);

    public int PcGold(DialogGoldQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        if (_session.QueryHero() is null) throw new InvalidOperationException("The session has no hero.");
        return _session.QueryGold();
    }
}
