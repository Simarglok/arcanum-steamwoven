using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog fate seam over a <see cref="GameSessionOwner"/> (ARC-WORLD-054): the FP action gives the session hero one fate point
/// (<see cref="GameSessionOwner.GrantHeroFatePoint"/>); at 100 it is Applied with nothing written, as the source's stat setter
/// clamps. A PC other than <paramref name="playerCharacter"/>, or a session without a hero, is Rejected. The session is read at
/// each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogFatePort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogFatePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public DialogCommandResult Execute(FatePointCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter) return DialogCommandResult.Rejected;
        var result = _session.GrantHeroFatePoint(_session.Version);
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }
}
