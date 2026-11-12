using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog inventory seam over a <see cref="GameSessionOwner"/> (ARC-WORLD-014): negative IN runs the session's
/// <see cref="GameSessionOwner.GiveItemToPc"/> with the host's insert check. The host maps each NPC participant to its
/// session entity id; an unmapped NPC or a PC other than <paramref name="playerCharacter"/> is Rejected with no effect.
/// A transfer (Changed, ARC-WORLD-1131: also the item the insert check refused, which lies on the PC's tile as in the original, 0x415BA0) and the source's
/// no-effect case (NoChange, no item with that name) are Applied; every other refusal is Rejected, and the conversation then fails on that command. The last result is kept in <see cref="LastResult"/> for the
/// host. The session is read at each call, so the port never caches its owner's state; it must be rebuilt when the host
/// replaces the owner. An item's insert-item script runs through <paramref name="scripts"/> (ARC-SCRIPT-013); its printed
/// lines are in <see cref="LastResult"/>. ARC-DEV-087 (MIG6e): <paramref name="prepare"/> is called with the NPC's entity before the move, for a host
/// that attaches the seed of the map listing it first (a companion carried here from its home map): the session then takes the item from that seed.
/// </summary>
public sealed class SessionDialogInventoryPort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    SessionInsertCheck insertCheck,
    ISessionItemScriptHost? scripts = null,
    Action<string>? prepare = null) : IDialogInventoryPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly SessionInsertCheck _insertCheck = insertCheck ?? throw new ArgumentNullException(nameof(insertCheck));

    /// <summary>The session's answer to the last IN through this port; null before the first and after an unmapped one.</summary>
    public InventoryMutationResult? LastResult { get; private set; }

    public DialogCommandResult Execute(GiveItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        LastResult = null;
        if (command.PlayerCharacter != playerCharacter || _entityOf(command.Npc) is not { } entity) return DialogCommandResult.Rejected;
        prepare?.Invoke(entity);
        var result = _session.GiveItemToPc(_session.Version, entity, command.ItemName, _insertCheck, scripts);
        LastResult = result;
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }
}
