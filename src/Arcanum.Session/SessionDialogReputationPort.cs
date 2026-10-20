using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>What the dialog's reputation port does with an RP action on a number outside 1000..1999 (anomaly A5: the original
/// stores any number, which then reads outside its table; the session admits none).</summary>
public enum ReputationRangePolicy
{
    /// <summary>The action is skipped with a <see cref="DialogReputationResult.Skipped"/> receipt and the dialog goes on (default:
    /// the coordinator's recommendation while the owner decides; the original shows no effect of such a number).</summary>
    Skip,
    /// <summary>The action is refused and the dialog fails, like a quest or rumor out of range.</summary>
    Refuse,
}

/// <summary>
/// Dialog reputation seam over a <see cref="GameSessionOwner"/> (ARC-WORLD-044): RP conditions read
/// <see cref="GameSessionOwner.HeroHasReputation"/>, RP actions call <see cref="GameSessionOwner.AddReputation"/> and
/// <see cref="GameSessionOwner.RemoveReputation"/>. A reputation already held (add) or not held (remove) is applied with no
/// change; a number outside 1000..1999 follows <paramref name="outOfRange"/>. A PC other than
/// <paramref name="playerCharacter"/> throws on a query and is rejected on an action. The session is read at each call.
/// </summary>
public sealed class SessionDialogReputationPort(GameSessionOwner session, DialogParticipantId playerCharacter,
    ReputationRangePolicy outOfRange = ReputationRangePolicy.Skip) : IDialogReputationPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));

    public bool HasReputation(DialogReputationQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) throw new ArgumentException("Not the session PC.", nameof(query));
        return _session.HeroHasReputation(query.Reputation);
    }

    public DialogReputationResult Execute(ReputationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter) return DialogReputationResult.Rejected;
        if (!SessionStoryLimits.IsReputation(command.Reputation))
            return outOfRange == ReputationRangePolicy.Skip ? DialogReputationResult.Skipped : DialogReputationResult.Rejected;
        var result = command.Add
            ? _session.AddReputation(_session.Version, command.Reputation)
            : _session.RemoveReputation(_session.Version, command.Reputation);
        return result.Disposition == GameSessionDisposition.Rejected ? DialogReputationResult.Rejected : DialogReputationResult.Applied;
    }
}
