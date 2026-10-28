namespace Arcanum.Simulation.Items;

/// <summary>Where an item is when its decay is tested (0x4612A0): loose on the ground, inside a junk pile (description 3023), or
/// inside any other holder.</summary>
public enum ItemDecayPlace { Loose, JunkPile, Held }

/// <summary>What an Item Decay event does when it fires (0x467FC0).</summary>
public enum ItemDecayStep
{
    /// <summary>The item is destroyed (0x43CCA0).</summary>
    Destroy,
    /// <summary>A container is open in the loot window: the event is scheduled again <see cref="ItemDecayRules.RetryDelayMs"/> on.</summary>
    Retry,
    /// <summary>Nothing happens: the item no longer decays (picked up, put in a holder that is not a junk pile, or flagged).</summary>
    Keep,
}

/// <summary>
/// ARC-WORLD-062: the original's Item Decay (event kind 25; 0x468090, 0x468010, 0x467FC0, 0x468110, read in the retail 1.0.7.4
/// executable by two readers, ledger "Item decay" ID1–ID9). An item dropped on the ground (loose or into a junk pile) decays two
/// game days later unless it is picked up first; while the loot window shows a container the event waits a minute more each time.
/// </summary>
public static class ItemDecayRules
{
    /// <summary>The decay delay of a dropped item: 2 game days.</summary>
    public const long DelayMs = 172_800_000;
    /// <summary>The wait of an event that fires while the loot window shows a container: 1 game minute.</summary>
    public const long RetryDelayMs = 60_000;
    /// <summary>The item flag (field 0x57) that keeps an item from decaying.</summary>
    public const uint NoDecayFlag = 0x20000;
    /// <summary>The description number of a junk pile (container prototype 3023).</summary>
    public const int JunkPileDescription = 3023;

    /// <summary>
    /// Whether the item decays (0x468010): not while decay is switched off (<paramref name="decayOff"/>, the global the item module's
    /// start sets from the start-up options' first field, which only the world editor sets), not with <see cref="NoDecayFlag"/>, and
    /// only loose or in a junk pile.
    /// </summary>
    [OriginalFunction(0x468010, OriginalFunctionScope.Full)]
    public static bool Decays(bool decayOff, uint itemFlags, ItemDecayPlace place)
    {
        if (!Enum.IsDefined(place)) throw new ArgumentOutOfRangeException(nameof(place));
        return !decayOff && (itemFlags & NoDecayFlag) == 0 && place != ItemDecayPlace.Held;
    }

    /// <summary>
    /// The firing (0x467FC0): while the loot window shows a container (the counter 0x5E8800 at 0 or below) the event is scheduled
    /// again a minute on when the item still decays (0x468090 tests it again); otherwise an item that still decays is destroyed.
    /// </summary>
    [OriginalFunction(0x467FC0, OriginalFunctionScope.Full)]
    public static ItemDecayStep Fire(bool lootWindowShowsContainer, bool decays) =>
        !decays ? ItemDecayStep.Keep : lootWindowShowsContainer ? ItemDecayStep.Retry : ItemDecayStep.Destroy;
}
