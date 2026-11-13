namespace Arcanum.Session;

/// <summary>
/// ARC-UI-1364: what the original's light callback hears about the local PC (ledger GB15): three stamps that change when the PC's logbook
/// content, its held list or its known world-map areas change. A stamp is a hash that lives in this process only (never saved, never
/// compared across runs); the host compares a stamp with the one it last saw and lights the matching button when they differ.
/// </summary>
/// <param name="Logbook">Over the PC's quests (number, botched mark, time of the last change; a record whose stored state is Unknown, one that holds
/// nothing but the botched mark, counts as no record: the original lights only when the stored state less the botched bit is not 0), blessings, curses and reputations (the
/// events 0x4C4E60, 0x4C5070, 0x4C42F0, 0x4C3E40, 0x4C4450, 0x4C3FA0, 0x4C1D20, 0x4C1E10 light the Logbook button for the local PC).</param>
/// <param name="Inventory">Over the held list's objects, in no order (0x466640 and 0x467860 light the Inventory button when the local PC's
/// list takes or loses an object; a move inside the list changes nothing).</param>
/// <param name="Areas">Over the known world-map areas (0x4CAFD0 lights the world map face of the Map button for the local PC).</param>
public readonly record struct SessionInterfaceStamp(long Logbook, long Inventory, long Areas);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-UI-1364: the stamps of the hero's logbook, held list and known areas (null for a session without a hero). The original lights the
    /// top strip's buttons at the writes themselves; the session's writes are many and spread, so the host reads these after each revision
    /// instead and lights on a difference. A quest request that is not above the quest's state changes nothing here and lights nothing, as in the
    /// original: its public setter passes a request on to the setter that lights only while the effective state is not completed, other-completed or botched and is
    /// below the request, so a repeated or lower request never reaches a write (ARC-WORLD-1554, ledger SQ16).
    /// </summary>
    public SessionInterfaceStamp? QueryHeroInterfaceStamp()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        long logbook = 17;
        // A record that holds only the botched mark (stored state 0) does not light the Logbook in the original (the internal setter and the unbotch test the state less the botched bit).
        foreach (var quest in _story.Quests.Values.Where(quest => quest.State != SessionQuestState.Unknown).OrderBy(quest => quest.Quest))
            logbook = unchecked(logbook * 31 + HashCode.Combine(quest.Quest, (int)quest.State, quest.Botched, quest.TimestampMs));
        foreach (var kind in new[] { SessionBlessingKind.Blessing, SessionBlessingKind.Curse })
        {
            logbook = unchecked(logbook * 31 + (int)kind);
            foreach (var blessing in _effects.List(kind)) logbook = unchecked(logbook * 31 + HashCode.Combine(blessing.Number, blessing.Effect, blessing.TimeMs));
        }
        logbook = unchecked(logbook * 31 + 7);
        foreach (var reputation in _story.Reputations) logbook = unchecked(logbook * 31 + HashCode.Combine(reputation.Reputation, reputation.TimestampMs));
        long inventory = 0;
        var count = 0;
        // A set: the sum of the entities' hashes does not depend on their order.
        foreach (var item in HeroHeld()) { inventory = unchecked(inventory + StringComparer.Ordinal.GetHashCode(item.Entity)); count++; }
        inventory = unchecked(inventory * 31 + count);
        long areas = 0;
        foreach (var area in _story.KnownAreas) areas = unchecked(areas + area * 2654435761L);
        areas = unchecked(areas * 31 + _story.KnownAreas.Count);
        return new(logbook, inventory, areas);
    }
}
