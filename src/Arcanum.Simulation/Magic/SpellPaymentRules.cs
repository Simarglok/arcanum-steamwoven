namespace Arcanum.Simulation.Magic;

/// <summary>Who pays: nothing (no object), a critter (PC or NPC), an item (object types 5..14), or another object.</summary>
public enum SpellPayerKind
{
    None,
    Critter,
    Item,
    Other,
}

/// <summary>Why a payment failed, or <see cref="Paid"/>.</summary>
public enum SpellPaymentStatus
{
    /// <summary>Paid, or nothing to pay (a cost of 0 or less, no payer, an object that is neither critter nor item).</summary>
    Paid,
    /// <summary>The spell number is neither 0..222 nor 10000: the original would read past its spell table.</summary>
    SpellOutOfRange,
    /// <summary>The charge-cell source refused (the technology lane's rule).</summary>
    CellsRefused,
    /// <summary>Fatigue would fall to −15 or lower; nothing but the worn mana stores was paid (MG20).</summary>
    FatigueRefused,
    /// <summary>The new fatigue damage is 0 or less (reachable only from a negative stored damage): the original's setter writes
    /// it clamped to 0 and reports a failure; the plan brings the damage to 0.</summary>
    FatigueNotSet,
    /// <summary>Paid, but the record is maintained or has no duration and fatigue is now below 0: the cast fails after paying.</summary>
    FailedAfterPay,
    /// <summary>An item source without spell charges (0).</summary>
    NoCharges,
}

/// <summary>A request to the charge-cell source: the payer and the cost before any other step. The source decides the
/// amount (a critter pays the cost, an item source its own surcharge, TC1) and whether the holder has enough; the original
/// spends cells whether or not the payment is applied (<paramref name="Apply"/> is passed for the source to see).</summary>
public sealed record SpellCellRequest(SpellPayerKind Payer, int Cost, bool Apply);

/// <summary>The charge-cell source's answer: paid (with the cells it takes) or refused.</summary>
public sealed record SpellCellPayment(bool Paid, int Cells);

/// <summary>The port the technology lane fills: the charge cells of a record with <c>ChargesFromCells:</c>.</summary>
public interface ISpellCellSource
{
    SpellCellPayment Pay(SpellCellRequest request);
}

/// <summary>One worn item's mana store drained by a payment: its slot (0..8), the points drained and its store after.</summary>
public sealed record ManaStoreDrain(int Slot, int Drained, int StoreAfter)
{
    /// <summary>The store is now empty: the original notifies the AI of the holder.</summary>
    public bool Emptied => StoreAfter == 0;
}

/// <summary>
/// What a payer brings to a payment. <paramref name="WornManaStores"/> holds the mana store of the item worn in each of the nine
/// slots (0 for an empty slot or an item without a store). <paramref name="CurrentFatigue"/> is the fatigue left now,
/// <paramref name="FatigueDamage"/> the stored fatigue damage, <paramref name="ItemCharges"/> an item source's spell charges
/// (negative: unlimited).
/// </summary>
public sealed record SpellPayer(SpellPayerKind Kind, bool FatigueLimiting = false, IReadOnlyList<int>? WornManaStores = null,
    int CurrentFatigue = 0, int FatigueDamage = 0, int ItemCharges = 0);

/// <summary>The record facts a payment reads: whether it takes charge cells, and its maintenance period and duration (for
/// the fail-after-pay test).</summary>
public readonly record struct SpellPaymentRecord(bool ChargesFromCells, int MaintainPeriod, int DurationSeconds);

/// <summary>
/// The result of a payment: the status; the "last cost" the original keeps for reflection (MG15) when the cost is positive;
/// the worn stores drained and the fatigue damage added (only when applied); the charge spent; whether an item source is
/// marked magical (the original marks it before testing whether to apply); the cell payment when cells were asked. The host
/// applies the plan whatever the status: a refused cast still has its stores drained (<see cref="SpellPaymentStatus.FatigueRefused"/>,
/// MG20), and one that fails after paying still has its fatigue damage (<see cref="SpellPaymentStatus.FailedAfterPay"/>).
/// </summary>
public sealed record SpellPayment(SpellPaymentStatus Status, int? LastCost, IReadOnlyList<ManaStoreDrain> Drains, int FatigueDamageAdded,
    bool ChargeSpent, bool MarksItemMagical, SpellCellPayment? Cells)
{
    public bool Succeeded => Status == SpellPaymentStatus.Paid;
}

/// <summary>
/// ARC-MAGIC-003: the original's payment of a spell cost (0x450420; contract docs/world/spell-cost-runtime.md, ledger "Spell
/// cost and payment"). A pure plan: it says what to take and write; the host applies it (fatigue through the life state,
/// stores and charges as item fields, the recharge event of each drain). The steps, in the original's order:
/// <list type="number">
/// <item>a cost of 0 or less, or no payer, pays nothing;</item>
/// <item>a record with <c>ChargesFromCells:</c> pays charge cells and nothing else (the cell source, before every other step);</item>
/// <item>a critter: a fatigue-limiting one pays half (at least 1); the worn mana stores pay first, slot by slot; the rest is
/// fatigue, refused when fatigue would fall to −15 or lower (the stores are drained all the same when applying, MG20); when
/// applying, the fatigue damage is added, and a maintained or untimed record fails after paying when fatigue is now below 0;</item>
/// <item>an item source spends one spell charge (0 refuses, negative is unlimited) and is marked magical;</item>
/// <item>any other object pays nothing.</item>
/// </list>
/// Without <paramref name="apply"/> (the affordability checks) no store, damage or charge is written, but the cell source is
/// still asked and an item source with charges is still marked magical.
/// </summary>
public static class SpellPaymentRules
{
    public const int NoSpell = 10000, Spells = 223, WornSlots = 9, FatigueFloor = -15;

    [OriginalFunction(0x450420, OriginalFunctionScope.Partial)]
    public static SpellPayment Pay(SpellPayer payer, int cost, bool apply, int spell, SpellPaymentRecord record, ISpellCellSource cells)
    {
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(cells);
        if (spell != NoSpell && spell is < 0 or >= Spells) return Result(SpellPaymentStatus.SpellOutOfRange, null);
        var stores = payer.WornManaStores ?? [];
        if (stores.Count > WornSlots) throw new ArgumentException("At most nine worn slots.", nameof(payer));
        var usesCells = spell != NoSpell && record.ChargesFromCells;
        if (cost <= 0) return Result(SpellPaymentStatus.Paid, null);
        if (payer.Kind == SpellPayerKind.None) return Result(SpellPaymentStatus.Paid, cost);
        if (usesCells)
        {
            var paid = cells.Pay(new(payer.Kind, cost, apply));
            return Result(paid.Paid ? SpellPaymentStatus.Paid : SpellPaymentStatus.CellsRefused, cost, cellPayment: paid);
        }
        if (payer.Kind == SpellPayerKind.Item)
        {
            if (payer.ItemCharges == 0) return Result(SpellPaymentStatus.NoCharges, cost, marks: false);
            if (payer.ItemCharges < 0) return Result(SpellPaymentStatus.Paid, cost);
            return Result(SpellPaymentStatus.Paid, cost, spent: apply, marks: true);
        }
        if (payer.Kind != SpellPayerKind.Critter) return Result(SpellPaymentStatus.Paid, cost);

        var rest = cost;
        if (payer.FatigueLimiting)
        {
            rest /= 2;
            if (rest == 0) rest = 1;
        }
        var drains = new List<ManaStoreDrain>();
        for (var slot = 0; slot < stores.Count; slot++)
        {
            var store = stores[slot];
            if (store == 0) continue;
            int drained;
            if (store >= rest) (drained, store, rest) = (rest, store - rest, 0);
            else (drained, rest, store) = (store, rest - store, 0);
            if (apply) drains.Add(new(slot, drained, store));
        }
        if (rest == 0) return Result(SpellPaymentStatus.Paid, cost, drains);
        if (payer.CurrentFatigue - rest <= FatigueFloor) return Result(SpellPaymentStatus.FatigueRefused, cost, drains);
        if (!apply) return Result(SpellPaymentStatus.Paid, cost, drains);
        if (unchecked(payer.FatigueDamage + rest) <= 0) return Result(SpellPaymentStatus.FatigueNotSet, cost, drains, unchecked(-payer.FatigueDamage));
        var maintainedOrUntimed = spell != NoSpell && (record.MaintainPeriod != 0 || record.DurationSeconds == 0);
        var failed = maintainedOrUntimed && payer.CurrentFatigue - rest < 0;
        return Result(failed ? SpellPaymentStatus.FailedAfterPay : SpellPaymentStatus.Paid, cost, drains, rest);
    }

    private static SpellPayment Result(SpellPaymentStatus status, int? lastCost, IReadOnlyList<ManaStoreDrain>? drains = null,
        int fatigue = 0, bool spent = false, bool marks = false, SpellCellPayment? cellPayment = null) =>
        new(status, lastCost, (drains ?? []).ToArray(), fatigue, spent, marks, cellPayment);
}
