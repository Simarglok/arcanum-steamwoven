using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog story state seam over a <see cref="GameSessionOwner"/> (ARC-DEV-191, ledger DCC7, DAC7, SS1): the <c>ss</c> condition reads the game's story state
/// (0x445090, <see cref="GameSessionOwner.QueryStoryState"/>) and the <c>ss</c> action is the setter 0x4450A0 (<see cref="GameSessionOwner.RaiseStoryState"/>:
/// the state only rises, a value at or below it is a successful no-change). A command for a PC other than <paramref name="playerCharacter"/> throws (the
/// story state is the game's, but the dialog's PC is the session's one). The session is read at each call.
/// </summary>
public sealed class SessionDialogStoryPort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogStoryPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    // The functions' marks (0x445090, 0x4450A0) stay on GameSessionOwner, the one method per address.
    public int State() => _session.QueryStoryState();

    public DialogCommandResult Execute(StoryStateCommand command)
    {
        if (command.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(command));
        return _session.RaiseStoryState(_session.Version, command.Value).Disposition == GameSessionDisposition.Rejected
            ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }
}

/// <summary>
/// Dialog known areas seam over a <see cref="GameSessionOwner"/> (ARC-DEV-191, ledger DCC7, WT2): the <c>ar</c> condition asks whether the session PC knows a
/// world-map area, the set the <c>mm</c> action writes (<see cref="GameSessionOwner.DiscoverArea"/>). A PC other than <paramref name="playerCharacter"/> throws.
/// </summary>
public sealed class SessionDialogAreaPort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogAreaPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    [Simulation.OriginalFunction(0x4CAF50, Simulation.OriginalFunctionScope.Partial)]
    public bool Known(DialogAreaQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return _session.IsAreaKnown(query.Area);
    }
}
