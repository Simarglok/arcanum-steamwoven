namespace Arcanum.Simulation.Items;

/// <summary>
/// The two item rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is
/// a behaviour of the original game confirmed in its executable (verification ledger) that the owner decided to change. The
/// profile holds no state, so it may change between any two item decisions.
/// </summary>
public sealed record ItemRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger TC7: the repair quote starts from the PC's sale price, which already scales with <c>current / maximum</c>, and
    /// then multiplies by <c>damage / maximum</c>, so damage counts twice: a badly damaged item is nearly free to repair and the
    /// dearest is half damaged, as the retail executable does (0x578760). Steamwoven quotes from the undamaged sale price, so
    /// the cost grows with the damage (owner decision 2026-10-20).
    /// </summary>
    public bool RepairQuoteCountsDamageTwice { get; init; }

    /// <summary>
    /// Ledger AM6 (AMA5): a container's stack of an ammunition type (the cell, gold and ammunition lookups 0x465870 and 0x4647D0) is
    /// looked up by passing the raw prototype number where a prototype handle is compared, so it is never found: a device in a
    /// container has no cells, as the retail executable does. Steamwoven finds the container's first stack of the type in its list
    /// (owner decision 2026-10-21, ARC-TECHNOLOGY-009; the ammunition lookups, cells included; gold is not modelled).
    /// </summary>
    public bool ContainerStackLookupFails { get; init; }

    /// <summary>
    /// Ledger WEA1 (WE7): units of gold or ammunition that merge into a stack the critter holds enter no list, so the encumbrance level is
    /// not recalculated (the insertion 0x466640 returns before 0x45F820, and the unit move 0x4658E0 grows a held stack without it): the
    /// level's effects and fatigue timer lag the weight until the next recalculation, as the retail executable does. Steamwoven recalculates
    /// when a held stack grows in place (owner decision 2026-10-28, ARC-WORLD-087).
    /// </summary>
    public bool EncumbranceLagsOnStackMerge { get; init; }

    /// <summary>
    /// Ledger IWA1 (IW13): a worn item moved to another wear slot (a ring between the two ring slots) passes the insert check of the
    /// same-holder move (0x5788C0 → 0x466510), which asks for a free slot in the grid although the item never enters it, so with a full
    /// grid the ring silently stays where it was, as the retail executable does. Steamwoven moves it without the room test (a gameplay
    /// bug fixed in Steamwoven, owner rule 2026-10-28, ARC-UI-019).
    /// </summary>
    public bool WornMoveNeedsRoom { get; init; }

    /// <summary>
    /// Ledger RK2 (VP68, VPG5): the restock delay is 12 hours plus the original generator's one 15-bit draw (0..32,767 ms), because the
    /// range draw 0x4CEF00 takes one 15-bit value modulo the width (RK11) where 0x4640C0 asks for 43,200,000..86,400,000 ms: a restock
    /// always comes 12 hours plus at most 32.8 seconds after it was scheduled, as the retail executable does. Steamwoven keeps the
    /// intended 12 to 24 hours (a gameplay bug fixed in Steamwoven, owner decision 2026-11-12, ARC-WORLD-1300, issue #1300).
    /// </summary>
    public bool RestockDelayIsNarrow { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly ItemRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly ItemRuleProfile Original = new() { Name = "original", RepairQuoteCountsDamageTwice = true, ContainerStackLookupFails = true,
        EncumbranceLagsOnStackMerge = true, WornMoveNeedsRoom = true, RestockDelayIsNarrow = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<ItemRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static ItemRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
