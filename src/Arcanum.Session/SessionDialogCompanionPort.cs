using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog companion seam over a <see cref="GameSessionOwner"/> (ARC-DEV-195, ledger CF1–CF7): the <c>FO</c> and <c>WT</c> conditions read the NPC's party record (<c>LedByPc</c>, <c>Jilted</c>), and
/// the <c>WA</c>, <c>LV</c>, <c>SO</c> and <c>SC</c> actions run the session's wait (<see cref="GameSessionOwner.WaitParty"/> with the host's wait timer facts), forced leave
/// (<see cref="GameSessionOwner.LeaveParty"/> with <c>Force</c>, the source's flag 1) and spread-out flag (<see cref="GameSessionOwner.SetSpreadOut"/>). Waiting while already waiting, leaving
/// while not in the party and a flag already in the asked state change nothing and are Applied, as in the source. Waiting while not in the party is Rejected: the original sets the
/// wait flag on an NPC with no leader, a state the session cannot hold. Every other refusal of the session (a version change, a full budget, no room for the wait timer) is Rejected and fails the conversation.
/// A participant the host cannot map or a PC other than <paramref name="playerCharacter"/> throws on a query and is Rejected for an action. The session is read at each call; the port must be
/// rebuilt when the host replaces the owner.
/// </summary>
/// <param name="session">The owner.</param>
/// <param name="playerCharacter">The one PC the port answers for.</param>
/// <param name="entityOf">The session entity of a dialog NPC, or null for one the host does not map.</param>
/// <param name="waitFacts">The wait timer facts of an NPC (its wait index and the leader's side), or null for one with none: then no timer is scheduled.</param>
public sealed class SessionDialogCompanionPort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    Func<DialogParticipantId, PartyWaitFacts?> waitFacts) : IDialogCompanionPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly Func<DialogParticipantId, PartyWaitFacts?> _waitFacts = waitFacts ?? throw new ArgumentNullException(nameof(waitFacts));

    /// <summary>The session's answer to the last action through this port; null before the first.</summary>
    public GameSessionMutationResult? LastResult { get; private set; }

    public bool LedByPc(DialogPartyQuery query) => Companion(query).LedByPc;

    public bool Jilted(DialogPartyQuery query) => Companion(query).Jilted;

    public DialogCommandResult Execute(CompanionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        LastResult = null;
        if (command.PlayerCharacter != playerCharacter || _entityOf(command.Npc) is not { } entity) return DialogCommandResult.Rejected;
        var result = command.Action switch
        {
            DialogCompanionAction.Wait => _session.WaitParty(_session.Version, entity, _waitFacts(command.Npc)),
            DialogCompanionAction.Leave => _session.LeaveParty(_session.Version, entity, new PartyLeave(Force: true)),
            DialogCompanionAction.SpreadOut => _session.SetSpreadOut(_session.Version, entity, true),
            DialogCompanionAction.SpreadClear => _session.SetSpreadOut(_session.Version, entity, false),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        LastResult = result;
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    // An unmapped NPC or another PC throws: the conversation records a query failure.
    private SessionCompanion Companion(DialogPartyQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return _session.QueryCompanion(_entityOf(query.Npc) ?? throw new ArgumentException("Unmapped NPC.", nameof(query)));
    }
}
