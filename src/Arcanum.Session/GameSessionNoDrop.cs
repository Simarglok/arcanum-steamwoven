namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-1283 (ledger NT2, VP5): the item message the original shows to a PC that is the source or the recipient of a transfer the remove check
    /// (0x466DA0, code 11: the no-drop item flag 0x20) refuses: message number 100 + code in the item message file (0x4673F0, 0x4673B0), 111.
    /// </summary>
    public const int NoDropMessage = 111;

    private Func<int, bool>? _prototypeNoDrop;

    /// <summary>
    /// ARC-WORLD-1283: attaches the host's no-drop item flag (item flag 0x20, field 0x57) by prototype, which the session reads for an item that has
    /// no seed row to carry its own flag: one of the hero's kit, a made item the hero or an NPC holds. A seed item's flag is its own row's (the instance
    /// holds the flag in the original). Not state: it is never saved and is the host's to attach again wherever it builds, carries or restores a session
    /// (with <see cref="AttachItemScripts"/>); null detaches it. Without one such an item has no flag, which is the older behaviour.
    /// </summary>
    public void AttachItemNoDrop(Func<int, bool>? noDrop)
    {
        ThrowIfDisposed();
        _prototypeNoDrop = noDrop;
    }

    // The prototype's flag from the attached source; a host call that throws is the host's own failure, a disposed owner is the caller's.
    private bool PrototypeNoDrop(SessionItemKind kind)
    {
        var answer = _prototypeNoDrop?.Invoke(kind.Prototype) == true;
        ThrowIfDisposed();
        return answer;
    }

    // ARC-WORLD-1283: whether the remove check (0x466DA0) refuses an item an NPC, a container or a junk pile holds, as the session's lists show it: its own flag
    // (a seed row's, or the flag the facts carry), else, when no seed row holds it (a made item), its prototype's. A worn item is refused too (the flag is tested
    // first; the worn-slot check 0x465010 answers 0 for the items the session lets leave).
    private bool HolderItemNoDrop(InventoryDomain.Seed seed, SessionInventoryItem item) =>
        item.Facts.NoDrop || (seed.Items.TryGetValue(item.Entity, out var row) ? row.Facts.NoDrop : PrototypeNoDrop(item.Facts.Kind));

    // ARC-WORLD-1283: the same for one of the hero's held items (not placed): the worn record's flag, the attached origin seed row's, else its prototype's.
    private bool HeroItemNoDrop(string item, SessionItemKind kind, SessionMapId? origin)
    {
        if (_equipment.SlotOf(item) is { } slot && _equipment.Worn[slot].Item.NoDrop) return true;
        if (origin is { } map && _inventory.Seeds.TryGetValue(map, out var seed) && seed.Items.TryGetValue(item, out var row)) return row.Facts.NoDrop;
        return PrototypeNoDrop(kind);
    }

    // The refusal of a transfer at the remove check: the failure and the PC's message number.
    private InventoryMutationResult NoDropRefusal(IReadOnlyList<SessionItemScriptRun>? scripts = null) =>
        RejectItem(GameSessionFailure.ItemNotDroppable) with { PcMessage = NoDropMessage, Scripts = scripts ?? [] };
}
