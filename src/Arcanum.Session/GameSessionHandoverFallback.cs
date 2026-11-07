using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-238 (ledger TK2): what the original does with the hero's item when a hand-over to an NPC was refused (the dialog action <c>in</c> n ≥ 0, 0x416091–0x4160F7, after
    /// <see cref="GiveItemToNpc"/> answered <see cref="GameSessionFailure.InsertRefused"/>): an NPC with no leader destroys it (<paramref name="placeAt"/> null; 0x43CCA0), one with a leader
    /// has it taken out of the hero's list and laid on the NPC's cell (0x466E00, 0x466E50: a junk pile or a new one, then the decay timer; the item joins the hero's placed items).
    /// Refused before any effect: an invalid input (<see cref="GameSessionFailure.InvalidItem"/>), the version, an item the hero does not hold (<see cref="GameSessionFailure.ItemNotAvailable"/>),
    /// a worn item with item event scripts, an item with a remove-item, insert-item or (worn) wield-off script on its origin seed (<see cref="GameSessionFailure.ItemScriptUnsupported"/>),
    /// a worn item with the item spells mark or an item that is the source of a spell run (<see cref="GameSessionFailure.ItemEffectUnsupported"/>; the removal's events are not run here), a cell
    /// the ground cannot hold (<see cref="GameSessionFailure.DropTileUnfit"/>), the revision. One revision. The placement is on the actor's map (the dialog's NPC stands on it). The caller owns the proof that the insert was refused (<see cref="SessionDialogItemTakePort"/> asks only after <see cref="GameSessionFailure.InsertRefused"/>).
    /// </summary>
    internal InventoryMutationResult SettleRefusedHandover(GameSessionVersion expected, string item, CellCoord? placeAt)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (HeroListed(item) is not { } held) return RejectItem(GameSessionFailure.ItemNotAvailable);
        var worn = _equipment.SlotOf(item) is not null;
        if (_equipment.SlotOf(item) is { } wornIn && _equipment.Worn[wornIn].ScriptPoints.Count > 0) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (_equipment.SlotOf(item) is { } markedIn && _equipment.Worn[markedIn].ItemSpells) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        if (HasItemRuns(item)) return RejectItem(GameSessionFailure.ItemEffectUnsupported);
        if (held.Origin is { } origin && _inventory.Seeds.TryGetValue(origin, out var seed) && seed.Items.TryGetValue(item, out var listed) &&
            (listed.Facts.HasScript(SessionScriptPoint.RemoveItem) || listed.Facts.HasScript(SessionScriptPoint.InsertItem) ||
                worn && listed.Facts.HasScript(SessionScriptPoint.WieldOff)))
            return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (placeAt is { } cell && !DropCellFits(cell)) return RejectItem(GameSessionFailure.DropTileUnfit);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        if (placeAt is { } at) PlaceHeroItem(item, at);
        else
        {
            DestroyObject(item);
            Revise();
        }
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item, null, Version);
    }
}
