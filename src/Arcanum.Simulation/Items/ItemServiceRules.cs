namespace Arcanum.Simulation.Items;

/// <summary>ARC-UI-1731: the two services of an NPC the dialog replies <c>ri</c> and <c>ii</c> open (ledger IS1–IS7).</summary>
public enum ItemServiceKind { Identify, Repair }

/// <summary>What a quote of a service said (the generated NPC line the original floats and the price it keeps).</summary>
public enum ItemServiceStatus
{
    /// <summary>The service can be done for <see cref="ItemServiceQuote.Price"/>.</summary>
    Quoted,
    /// <summary>The item is already identified: the "nothing to identify" line.</summary>
    AlreadyIdentified,
    /// <summary>The item has no damage and is not broken: the "nothing to repair" line.</summary>
    NothingToRepair,
    /// <summary>The item is broken and the NPC is not a master of Repair: the refusal line.</summary>
    NeedsMaster,
}

/// <summary>
/// A quote: its status, the price (0 unless <see cref="ItemServiceStatus.Quoted"/>) and the range of the generated NPC line the window
/// shows (message numbers of the NPC line files <c>gd_npc_*</c>, picked at random in the range by the original's NPC line generator).
/// </summary>
public readonly record struct ItemServiceQuote(ItemServiceStatus Status, int Price, int LineFirst, int LineLast)
{
    public bool Quoted => Status == ItemServiceStatus.Quoted;
}

/// <summary>
/// ARC-UI-1731 (ledger IS1–IS7; contract docs/world/item-service-runtime.md): the rules of the item identify and item repair windows of
/// the original (the inventory window in its modes 5 and 6, opened by the dialog replies). Pure functions.
/// </summary>
public static class ItemServiceRules
{
    /// <summary>The price of identifying one item: the original's price getter returns the constant 100 whatever the argument.</summary>
    public const int IdentifyPrice = 100;

    /// <summary>The identify quote line range, "I will identify it for N" (5800..5899).</summary>
    public const int IdentifyQuoteFirst = 5800, IdentifyQuoteLast = 5899;
    /// <summary>The "already identified" line range (5700..5799).</summary>
    public const int IdentifiedFirst = 5700, IdentifiedLast = 5799;
    /// <summary>The repair refusal line range for a broken item and a repairer who is not a master (5900..5999).</summary>
    public const int RepairRefusedFirst = 5900, RepairRefusedLast = 5999;
    /// <summary>The "nothing to repair" line range (6000..6099).</summary>
    public const int NothingToRepairFirst = 6000, NothingToRepairLast = 6099;
    /// <summary>The repair quote line range (6100..6199).</summary>
    public const int RepairQuoteFirst = 6100, RepairQuoteLast = 6199;
    /// <summary>The "no money" line is the class line of group 2000 (the class generator).</summary>
    public const int NoMoneyGroup = 2000;

    /// <summary>The identify quote of an item: an identified item (<see cref="IsIdentified"/>) is refused with the "already identified"
    /// line, any other costs <see cref="IdentifyPrice"/>.</summary>
    [OriginalFunction(0x5786C0, OriginalFunctionScope.Partial)]
    public static ItemServiceQuote Identify(bool identified) => identified
        ? new(ItemServiceStatus.AlreadyIdentified, 0, IdentifiedFirst, IdentifiedLast)
        : new(ItemServiceStatus.Quoted, IdentifyPrice, IdentifyQuoteFirst, IdentifyQuoteLast);

    /// <summary>The identified test for an item of types 5..14: no complexity or the identified bit (item flag 0x1, the stored one or
    /// a session mark). Other objects are never identified.</summary>
    /// <remarks>Executable: the identified test 0x464370 (field 0x60 below 1, flag bit 0x01 of field 0x57); <c>EquipmentChoice.Identified</c> is its registered port.</remarks>
    public static bool IsIdentified(int type, int complexity, bool flagged) => type is >= 5 and <= 14 && (complexity < 1 || flagged);

    /// <summary>The repair quote of an NPC as the window shows it: <see cref="ItemDurabilityRules.RepairQuote"/> with the
    /// generated line of its status.</summary>
    /// <remarks>Executable: the repair quote 0x578760 (registered as <c>ItemDurabilityRules.RepairQuote</c>).</remarks>
    public static ItemServiceQuote Repair(ItemRepairQuote quote) => quote.Status switch
    {
        ItemQuoteStatus.Quoted => new(ItemServiceStatus.Quoted, quote.Cost, RepairQuoteFirst, RepairQuoteLast),
        ItemQuoteStatus.NotNeeded => new(ItemServiceStatus.NothingToRepair, 0, NothingToRepairFirst, NothingToRepairLast),
        _ => new(ItemServiceStatus.NeedsMaster, 0, RepairRefusedFirst, RepairRefusedLast),
    };

    /// <summary>The price the window keeps after a quote (a global of the original's window): a quoted item sets it; a refused one leaves it
    /// as it was in the Original setting (<see cref="ItemRuleProfile.ServiceKeepsStalePrice"/>, ledger IS5) and clears it (-1) in Steamwoven.</summary>
    public static int KeptPrice(int current, ItemServiceQuote quote, ItemRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return quote.Quoted ? quote.Price : profile.ServiceKeepsStalePrice ? current : NoPrice;
    }

    /// <summary>The kept price of a window that holds none: after the open and after each payment or refusal for money it is -1.</summary>
    public const int NoPrice = -1;
}
