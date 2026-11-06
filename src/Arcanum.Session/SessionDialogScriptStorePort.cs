using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog script store seam over a <see cref="GameSessionOwner"/> (ARC-DEV-152, ledger DG1–DG4): gv reads and writes the session's
/// global variables, pv and pf the session hero's variables and flags (the stores of ARC-DEV-098). A write of the value already held is
/// Applied with nothing written. A PC other than <paramref name="playerCharacter"/> throws on a query and is rejected on an action; a
/// session without a hero (the viewer's diagnostic PC) reads 0 from the PC stores and ignores a PC write as Applied, as the original's
/// accessors ignore an object that is not a PC (SA14) and as <see cref="GameSessionOwner.ApplyScriptStores"/> does.
/// The session is read at each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogScriptStorePort(GameSessionOwner session, DialogParticipantId playerCharacter) : IDialogScriptStorePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public int Read(DialogScriptStoreQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return query.Store switch
        {
            DialogScriptStore.GlobalVariable => _session.QueryGlobalVariable(query.Index),
            DialogScriptStore.PcVariable => _session.QueryPcVariable(query.Index),
            DialogScriptStore.PcFlag => _session.QueryPcFlag(query.Index) ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };
    }

    public DialogCommandResult Execute(ScriptStoreCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter) return DialogCommandResult.Rejected;
        GameSessionMutationResult? result = command.Store switch
        {
            DialogScriptStore.GlobalVariable => _session.SetGlobalVariable(_session.Version, command.Index, command.Value),
            DialogScriptStore.PcVariable => _session.SetPcVariable(_session.Version, command.Index, command.Value),
            DialogScriptStore.PcFlag when command.Value is 0 or 1 => _session.SetPcFlag(_session.Version, command.Index, command.Value == 1),
            _ => null,
        };
        if (result is { Disposition: GameSessionDisposition.Rejected, Failure: GameSessionFailure.PcScriptsUnavailable }) return DialogCommandResult.Applied;
        return result is not { } done || done.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }
}
