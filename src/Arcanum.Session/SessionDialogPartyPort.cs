using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// Dialog party seam over a <see cref="GameSessionOwner"/> (ARC-WORLD-013): WA reads the NPC's WAIT bit, JO and
/// UW run the session's checked join and unwait (first operand 0: charisma limit applies, not forced). The host maps
/// each NPC participant to its session entity id and supplies the follow-check facts (with a session hero the session
/// supplies the PC's persuasion, charisma, level and max followers itself, ARC-SKILLS-002); a participant it cannot map, a
/// PC other than <paramref name="playerCharacter"/> or missing facts is Rejected with no effect. A refusal is Refused and
/// its reason is kept in <see cref="LastRefusal"/> for the host's refusal line. The session is read at each call, so the
/// port never caches its owner's state; it must be rebuilt when the host replaces the owner.
/// </summary>
public sealed class SessionDialogPartyPort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    Func<DialogParticipantId, PartyConsentFacts?> factsOf) : IDialogPartyPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly Func<DialogParticipantId, PartyConsentFacts?> _factsOf = factsOf ?? throw new ArgumentNullException(nameof(factsOf));

    /// <summary>The reason of the last refused JO/UW through this port; cleared by every accepted or rejected one.</summary>
    public FollowConsent? LastRefusal { get; private set; }

    /// <summary>An unmapped NPC or another PC throws: the conversation records a query failure.</summary>
    public bool IsWaiting(DialogPartyQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        var entity = _entityOf(query.Npc) ?? throw new ArgumentException("Unmapped NPC.", nameof(query));
        // The condition reads the WAIT bit alone (ARC-SCRIPT-1136, ledger CF9): the waiting standing, or the flag of an NPC that has no leader or joined with it.
        return _session.QueryCompanion(entity).WaitBit;
    }

    public DialogJoinResult Execute(JoinCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Run(command.Npc, command.PlayerCharacter, (entity, facts) => _session.JoinParty(_session.Version, entity, facts, false));
    }

    public DialogJoinResult Execute(UnwaitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Run(command.Npc, command.PlayerCharacter, (entity, facts) => _session.UnwaitParty(_session.Version, entity, facts, false));
    }

    private DialogJoinResult Run(DialogParticipantId npc, DialogParticipantId pc,
        Func<string, PartyConsentFacts, PartyMutationResult> change)
    {
        LastRefusal = null;
        if (pc != playerCharacter || _entityOf(npc) is not { } entity || _factsOf(npc) is not { } facts) return DialogJoinResult.Rejected;
        var result = change(entity, facts);
        if (result.Disposition != GameSessionDisposition.Rejected) return DialogJoinResult.Accepted;
        if (result.Failure != GameSessionFailure.ConsentRefused) return DialogJoinResult.Rejected;
        LastRefusal = result.Consent;
        return DialogJoinResult.Refused;
    }
}
