using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog experience seam over a <see cref="GameSessionOwner"/> (ARC-DEV-178, ledger DAC9, XP11): the XP action gives the session hero the
/// experience of a quest experience level (<see cref="GameSessionOwner.AwardQuestExperience"/>). A level without a table entry gives 0 and is
/// Applied with nothing written. A PC other than <paramref name="playerCharacter"/>, a session without a hero or without the experience tables,
/// or a refused write is Rejected. The award a write gave (<see cref="SessionExperienceResult.Award"/>) goes to <paramref name="awarded"/>, so the
/// host can pass the hero's new levels on to the followers, as for a completed quest. The session is read at each call; the port must be rebuilt
/// when the host replaces the owner.
/// </summary>
public sealed class SessionDialogExperiencePort(GameSessionOwner session, DialogParticipantId playerCharacter, Action<SessionExperienceAward>? awarded = null) : IDialogExperiencePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public DialogCommandResult Execute(QuestExperienceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter) return DialogCommandResult.Rejected;
        var result = _session.AwardQuestExperience(_session.Version, command.Level);
        if (result.Disposition == GameSessionDisposition.Rejected) return DialogCommandResult.Rejected;
        if (result.Award is { } award) awarded?.Invoke(award);
        return DialogCommandResult.Applied;
    }
}
