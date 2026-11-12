using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// Dialog item hand-over seam over a <see cref="GameSessionOwner"/> (ARC-DEV-199, ledger DAC5, TK1–TK3): the non-negative <c>IN</c> action. The hero's first item whose name field equals the
/// command's (its starting kit, then the items it took, then the made ones) goes to the NPC through <see cref="GameSessionOwner.GiveItemToNpc"/> with the host's insert check for the NPC.
/// Nobody holding such an item is the source's no-effect case (Applied). The source takes the item from the first follower holding it when the PC has none; the session has no move from
/// one NPC's list to another's, so that case (unless the NPC is itself the first follower holding it: the item is with it already, Applied), and every refusal of the move (the version, an NPC the session does not know, a script or item effect it cannot run, a full list, the host's
/// verdict), is Rejected and the conversation fails on the command. A move the host's insert check refused (<see cref="GameSessionFailure.InsertRefused"/>) or the item's remove check refused (the no-drop flag, <see cref="GameSessionFailure.ItemNotDroppable"/>, ARC-WORLD-1283, which the original tests first) is the source's transfer that did not
/// end with the NPC (ledger TK2, ARC-DEV-238): an NPC the PC leads has the item laid on its cell (<paramref name="locationOf"/>; no cell, or one the ground cannot hold, is Rejected), any other NPC
/// destroys it (<see cref="GameSessionOwner.SettleRefusedHandover"/>); both are Applied and the string goes on. The leader is the PC's party (an NPC following another NPC is not modelled). A PC other than <paramref name="playerCharacter"/> or an NPC without a session entity is Rejected. The last move's result is kept in <see cref="LastResult"/>. The session is read
/// at each call; the port must be rebuilt when the host replaces the owner.
/// </summary>
/// <param name="session">The owner.</param>
/// <param name="playerCharacter">The one PC the port answers for.</param>
/// <param name="entityOf">The session entity of a dialog NPC, or null for one the host does not map.</param>
/// <param name="insertCheck">The host's insert check for the NPC.</param>
/// <param name="locationOf">The cell of a dialog NPC on the shown map, or null for one the host cannot place; needed only for a refused hand-over to an NPC the PC leads.</param>
/// <param name="prepare">Called with an entity before its list is read or moved into, for a host that attaches the seed of the map listing it first (a companion carried here from its home map).</param>
public sealed class SessionDialogItemTakePort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    SessionInsertCheck insertCheck,
    Action<string>? prepare = null,
    Func<string, CellCoord?>? locationOf = null) : IDialogItemTakePort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly SessionInsertCheck _insertCheck = insertCheck ?? throw new ArgumentNullException(nameof(insertCheck));

    /// <summary>The session's answer to the last move through this port; null before the first and when nothing was moved.</summary>
    public InventoryMutationResult? LastResult { get; private set; }

    // TK2: the transfer did not end with the NPC. A leader (the PC) keeps the item on the ground at the NPC's cell, no leader destroys it.
    private DialogCommandResult Settle(string npc, string item)
    {
        CellCoord? cell = null;
        if (_session.QueryCompanion(npc).LedByPc)
        {
            if (locationOf?.Invoke(npc) is not { } at) return DialogCommandResult.Rejected;
            cell = at;
        }
        var settled = _session.SettleRefusedHandover(_session.Version, item, cell);
        LastResult = settled;
        return settled.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(TakeItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        LastResult = null;
        if (command.PlayerCharacter != playerCharacter || _entityOf(command.Npc) is not { } npc) return DialogCommandResult.Rejected;
        prepare?.Invoke(npc);
        var held = _session.QueryHeroKit().Select(item => (item.Entity, item.Kind))
            .Concat(_session.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .Concat(_session.QueryMadeItems().Select(item => (item.Entity, item.Kind)))
            .FirstOrDefault(item => item.Kind.Name == command.ItemName);
        if (held.Entity is not null)
        {
            var result = _session.GiveItemToNpc(_session.Version, npc, held.Entity, _insertCheck);
            LastResult = result;
            // ARC-WORLD-1283: the item's remove check (the no-drop flag) fails the transfer too, before the insert check; the original's fallback is the same (0x416091).
            if (result.Disposition == GameSessionDisposition.Rejected && result.Failure is GameSessionFailure.InsertRefused or GameSessionFailure.ItemNotDroppable) return Settle(npc, held.Entity);
            return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
        }
        foreach (var follower in _session.QueryParty().Followers)
        {
            prepare?.Invoke(follower.Entity);
            if (_session.QueryHolderItems(follower.Entity)?.Any(item => item.Facts.Kind.Name == command.ItemName) == true)
                return follower.Entity == npc ? DialogCommandResult.Applied : DialogCommandResult.Rejected; // the NPC itself is the first holder: the item is already with it
        }
        return DialogCommandResult.Applied;
    }
}
