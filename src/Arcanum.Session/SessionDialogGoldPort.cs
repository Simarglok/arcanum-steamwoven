using Arcanum.Scripting.Dialog;

namespace Arcanum.Session;

/// <summary>
/// Dialog gold seam over a <see cref="GameSessionOwner"/> (ARC-DEV-179, ledger DCC5, DAC2, GM1–GM4): the <c>$$</c> condition and action.
/// <see cref="Total"/> adds the hero's gold (<see cref="GameSessionOwner.QueryGold"/>) and each direct follower's, in the follower array's order
/// (the first gold pile the session tracks for it, <see cref="GameSessionOwner.QueryNpcGold"/>; a follower the session holds no list for counts 0),
/// as a signed 32-bit sum that wraps like the source's. <see cref="Execute"/> runs the action: an amount above 0 gives the hero that many coins from
/// nothing (<see cref="GameSessionOwner.GiveGoldToHero"/>); an amount below 0 makes the hero pay its absolute value to the NPC
/// (<see cref="GameSessionOwner.GiveGoldToNpc"/>), then each follower in order pay what is still owed
/// (<see cref="GameSessionOwner.MoveGoldBetweenNpcs"/>), the owed amount being lowered by each payer's whole gold whether or not it paid, as the
/// source does. A payer holding less than the whole amount owed at its turn pays nothing under <paramref name="shortPayerPaysNothing"/> (the
/// source's transfer is refused then, GM3; the Original setting, <see cref="DialogRuleProfile.ShortPayerPaysNothing"/>); otherwise it pays all it
/// has. A payer without a gold stack at all makes the Original transfer credit the NPC the whole amount owed from nothing (GM2). A payer that pays nothing is not an error. Each payment is its own revision, so a refusal after the first leaves the earlier ones done, as in
/// the source. A PC other than <paramref name="playerCharacter"/>, an NPC without a session entity, or a session refusal that is not a short payer
/// (no hero, an unknown NPC, a budget or version failure) is Rejected. The session is read at each call; the port must be rebuilt when the host
/// replaces the owner.
/// </summary>
/// <param name="session">The owner.</param>
/// <param name="playerCharacter">The one PC the port answers for.</param>
/// <param name="entityOf">The session entity of a dialog NPC, or null for one the host does not map.</param>
/// <param name="pileKind">The kind of a new gold pile of an NPC that has none (the gold prototype).</param>
/// <param name="insertCheck">The host's insert check for such a new pile.</param>
/// <param name="shortPayerPaysNothing">The Original setting of GM3.</param>
/// <param name="itemScripts">ARC-SCRIPT-2910: the item script host the hero's new gold pile (a hero with no pile) runs its prototype's get and insert-item scripts through; null: such a prototype refuses the gift.</param>
/// <param name="prepare">Called with an NPC's entity before its gold is read or moved, for a host that attaches the seed of the map listing it first (a companion carried here from its home map).</param>
public sealed class SessionDialogGoldPort(
    GameSessionOwner session,
    DialogParticipantId playerCharacter,
    Func<DialogParticipantId, string?> entityOf,
    SessionItemKind pileKind,
    SessionInsertCheck insertCheck,
    bool shortPayerPaysNothing,
    Action<string>? prepare = null,
    ISessionItemScriptHost? itemScripts = null) : IDialogGoldPort
{
    private readonly GameSessionOwner _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<DialogParticipantId, string?> _entityOf = entityOf ?? throw new ArgumentNullException(nameof(entityOf));
    private readonly SessionInsertCheck _insertCheck = insertCheck ?? throw new ArgumentNullException(nameof(insertCheck));

    public int Total(DialogGoldQuery query)
    {
        if (query.PlayerCharacter != playerCharacter) return 0;
        unchecked
        {
            var total = _session.QueryGold();
            foreach (var follower in _session.QueryParty().Followers)
            {
                prepare?.Invoke(follower.Entity);
                total += _session.QueryNpcGold(follower.Entity) ?? 0;
            }
            return total;
        }
    }

    public DialogCommandResult Execute(GoldCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PlayerCharacter != playerCharacter || _session.QueryHero() is null) return DialogCommandResult.Rejected;
        if (command.Amount == 0) return DialogCommandResult.Applied;
        if (command.Amount > 0) return Applied(_session.GiveGoldToHero(_session.Version, command.Amount, itemScripts));
        if (_entityOf(command.Npc) is not { } npc) return DialogCommandResult.Rejected;
        prepare?.Invoke(npc);
        var owed = -(long)command.Amount;
        var own = _session.QueryGold();
        // The PC first: the source asks its transfer for the whole amount owed. A PC with a stack but less gets nothing moved (GM3); one without a
        // stack (no gold at all) makes the transfer credit the NPC from nothing (GM2).
        if (!Pay(owed, own, own > 0, shortPayerPaysNothing,
                payment => _session.GiveGoldToNpc(_session.Version, npc, (int)payment, pileKind, _insertCheck, itemScripts),
                payment => _session.GiveGoldToNpcFromNothing(_session.Version, npc, (int)payment, pileKind, _insertCheck))) return DialogCommandResult.Rejected;
        owed -= own;
        // Then the follower array as it stands after that payment (0x4411C0 is read after the PC's call).
        foreach (var follower in _session.QueryParty().Followers)
        {
            if (owed <= 0) break;
            prepare?.Invoke(follower.Entity);
            var held = _session.QueryNpcGold(follower.Entity) ?? 0;
            // The talking NPC is itself a follower: the source's call from it to itself moves its coins out and back, so nothing changes
            // while the amount owed still falls by its gold.
            if (follower.Entity == npc) { owed -= held; continue; }
            if (!Pay(owed, held, _session.QueryNpcHasGoldPile(follower.Entity), shortPayerPaysNothing,
                    payment => _session.MoveGoldBetweenNpcs(_session.Version, follower.Entity, npc, (int)payment, pileKind, _insertCheck, itemScripts),
                    payment => _session.GiveGoldToNpcFromNothing(_session.Version, npc, (int)payment, pileKind, _insertCheck))) return DialogCommandResult.Rejected;
            owed -= held;
        }
        return DialogCommandResult.Applied;
    }

    // One payer's turn. Original (GM2, GM3): a payer without a gold stack credits the NPC the whole amount owed from nothing; one whose stack holds
    // at least that much pays it; one with less pays nothing. Steamwoven: the payer pays the lesser of its gold and the amount owed, a payer without
    // gold nothing. False when the session refused a payment that was due.
    private static bool Pay(long owed, int held, bool hasStack, bool original, Func<long, InventoryMutationResult> move, Func<long, InventoryMutationResult> fromNothing)
    {
        if (original && !hasStack) return fromNothing(owed).Disposition != GameSessionDisposition.Rejected;
        var payment = held >= owed ? owed : original ? 0 : held;
        if (payment <= 0) return true;
        return move(payment).Disposition != GameSessionDisposition.Rejected;
    }

    private static DialogCommandResult Applied(InventoryMutationResult result) =>
        result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
}
