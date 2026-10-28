namespace Arcanum.Simulation.Items;

/// <summary>Bounds of the stored durability values the rules accept.</summary>
public static class ItemDurabilityLimits
{
    /// <summary>hp_pts and hp_adj each lie within ±this; indestructible items store 10 000.</summary>
    public const int MaxMagnitude = 100_000;
    /// <summary>hp_damage lies within 0..this.</summary>
    public const int MaxDamage = 2 * MaxMagnitude;
    /// <summary>One damage step adds at most this.</summary>
    public const int MaxDamageStep = MaxMagnitude;
    /// <summary>A price lies within 0..this (the original computes it in 32 bits; an item worth stays far below).</summary>
    public const int MaxPrice = 1_000_000_000;
}

/// <summary>
/// The durability of one item: its saved fields hp_pts (27, <see cref="Points"/>), hp_adj (28, <see cref="Adjustment"/>) and
/// hp_damage (29, <see cref="Damage"/>), the "destroyed" bit (0x400) of its current art (<see cref="Broken"/>, which every
/// broken test reads) and of its inventory art (<see cref="InventoryArtBroken"/>, which only the inventory icon shows: a master
/// repair clears the current art's bit only).
/// </summary>
public readonly record struct ItemCondition(int Points, int Adjustment, int Damage, bool Broken, bool InventoryArtBroken = false)
{
    /// <summary>The maximum: <c>hp_pts + hp_adj</c> (an item has no effects).</summary>
    public int MaximumHitPoints => ItemDurabilityRules.MaximumHitPoints(this);

    /// <summary>The current hit points: maximum − damage, which may be 0 or less.</summary>
    public int HitPoints => ItemDurabilityRules.HitPoints(this);
}

/// <summary>What a damage step or a break did.</summary>
public enum ItemBreakStep
{
    /// <summary>The item still has hit points (or the break did nothing).</summary>
    None,
    /// <summary>The first break: the destroyed bit is set on the current and the inventory art and the damage reset to 0; the
    /// item leaves its wear slot (dropped when it cannot go back to the holder's inventory), the bust sound plays and the
    /// item's BUST script (point 30) runs with the breaker as triggerer.</summary>
    Broken,
    /// <summary>The second break of an already broken item: the object is deleted (with the destroy sound, no script).</summary>
    Deleted,
}

/// <summary>The item after a damage step or a break, and what the break asks of the item's owner.</summary>
public readonly record struct ItemDamageOutcome(ItemCondition Condition, ItemBreakStep Break)
{
    /// <summary>The holder takes the item off its wear slot (or drops it); the bust sound and the BUST script follow.</summary>
    public bool TakeOffAndRunBust => Break == ItemBreakStep.Broken;
}

public enum ItemQuoteStatus
{
    Quoted,
    /// <summary>Damage 0: the original shows its "nothing to repair" line.</summary>
    NotNeeded,
    /// <summary>A broken item and a repairer who is not a master of Repair: the original shows its refusal line.</summary>
    BrokenNeedsMaster,
}

public readonly record struct ItemRepairQuote(ItemQuoteStatus Status, int Cost);

/// <summary>The training a repairer has in Repair, as the original numbers it (0 none .. 3 master).</summary>
public enum RepairTraining { None = 0, Apprentice = 1, Expert = 2, Master = 3 }

public enum ItemRepairStatus
{
    Repaired,
    /// <summary>No damage and nothing a master would clear (a broken item and a repairer who is not a master): the original's
    /// service changes nothing.</summary>
    NothingToRepair,
}

public readonly record struct ItemRepairOutcome(ItemRepairStatus Status, ItemCondition Condition);

/// <summary>
/// ARC-WORLD-053: item durability as the retail 1.0.7.4 executable applies it (contract docs/world/item-durability-runtime.md,
/// ledger "Item durability"): the maximum, the current hit points, the damage setter, a damage step with the two-step break,
/// what a broken item blocks, the damage scaling of the PC's sale price, the repair quote and the item side of a repair. Pure
/// functions over an <see cref="ItemCondition"/>; who wears the item, sounds, scripts, the inventory and the price's other
/// parts are the caller's. 32-bit signed arithmetic, every division truncating toward zero, as the original.
/// </summary>
public static class ItemDurabilityRules
{
    /// <summary>The script attachment point the first break runs (BUST).</summary>
    public const int BustScriptPoint = 30;

    /// <summary>The maximum hit points of an item: hp_pts + hp_adj, no effects and no clamp (the critter branch, with level,
    /// strength, willpower and effects, is not an item's).</summary>
    public static int MaximumHitPoints(ItemCondition item) => unchecked(item.Points + item.Adjustment);

    /// <summary>The current hit points: maximum − damage, no clamp.</summary>
    [OriginalFunction(0x43D600, OriginalFunctionScope.Full)]
    public static int HitPoints(ItemCondition item) => unchecked(MaximumHitPoints(item) - item.Damage);

    /// <summary>The damage setter: a negative value is stored as 0. It never breaks the item itself (the callers do); a
    /// network client's no-write branch and the critter rest timer are not an item's.</summary>
    [OriginalFunction(0x43D530, OriginalFunctionScope.Partial)]
    public static ItemCondition SetDamage(ItemCondition item, int damage) => item with { Damage = Math.Max(0, damage) };

    /// <summary>
    /// One damage step of the callers that wear items (combat damage on an item, armour and weapon wear): <paramref name="amount"/>
    /// (0 or more) is added to the damage through the setter, and an item whose current hit points are then 0 or less breaks
    /// (<see cref="Break"/>). An item already broken that loses hit points again is deleted.
    /// </summary>
    public static ItemDamageOutcome AddDamage(ItemCondition item, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, ItemDurabilityLimits.MaxDamageStep);
        var damaged = SetDamage(item, (int)Math.Min((long)item.Damage + amount, ItemDurabilityLimits.MaxDamage));
        return HitPoints(damaged) <= 0 ? Break(damaged) : new(damaged, ItemBreakStep.None);
    }

    /// <summary>
    /// The break of an item (types 5..14): when its current art carries no destroyed bit, the first break sets the bit on the
    /// current and the inventory art and resets the damage to 0 (so a broken item reads as healthy); otherwise the second break
    /// deletes it. What the first break asks of the holder is <see cref="ItemDamageOutcome.TakeOffAndRunBust"/>.
    /// </summary>
    [OriginalFunction(0x43F1C0, OriginalFunctionScope.Partial)]
    public static ItemDamageOutcome Break(ItemCondition item) => item.Broken
        ? new(item, ItemBreakStep.Deleted)
        : new(SetDamage(item with { Broken = true, InventoryArtBroken = true }, 0), ItemBreakStep.Broken);

    /// <summary>A broken item cannot be worn (the wear check, ledger EQ1, which reads <c>EquipmentItem.Broken</c>).</summary>
    public static bool BlocksWear(ItemCondition item) => item.Broken;

    /// <summary>A broken item is not a schematic component (the component search skips it).</summary>
    public static bool BlocksSchematicUse(ItemCondition item) => item.Broken;

    /// <summary>Use and item spells never test the broken bit (TC9, the original's behaviour in both modes).</summary>
    public static bool BlocksUse(ItemCondition item) => false;

    /// <summary>The part of the sale check that reads the item alone: a worth of 0 or a broken item is refused (the price is
    /// then 0). The whole check, both parties included, is <see cref="TradeRules.SaleCheck"/> (ARC-UI-050).</summary>
    public static bool SaleRefused(ItemCondition item, int worth) => worth == 0 || item.Broken;

    /// <summary>
    /// The damage part of the item price: when the PC sells and the item has lost hit points (current below maximum), the
    /// price the caller computed (worth, the buyer's multiplier and the haggle share) becomes <c>current × price / maximum</c>;
    /// then a negative price is 0 and a price of exactly 1 is 2 (the PC-seller branch's bounds). Buying from an NPC ignores
    /// damage: the price comes back unchanged (that branch's own bounds are the trade owner's). The whole price is
    /// <see cref="TradeRules.Price"/> (ARC-UI-050); the repair quote keeps this step.
    /// </summary>
    public static int ScalePrice(int price, ItemCondition item, bool pcSells)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(price, ItemDurabilityLimits.MaxPrice);
        var maximum = MaximumHitPoints(item);
        var current = HitPoints(item);
        if (!pcSells) return price;
        if (current < maximum && maximum != 0) price = (int)((long)current * price / maximum);
        return price < 0 ? 0 : price == 1 ? 2 : price;
    }

    /// <summary>
    /// The repair quote of an NPC repairer for the PC. <paramref name="undamagedPrice"/> is the PC's sale price of the item to
    /// that NPC before the damage scaling (<see cref="ScalePrice"/>), from the trade owner. A broken item is quoted only by a
    /// master of Repair, with the damage taken as the maximum; damage 0 is "not needed". The quote is <c>trunc(P / 2) × damage /
    /// maximum</c>, at least 2. Original (TC7): P is the damage-scaled sale price, so damage counts twice; Steamwoven: P is the
    /// undamaged price (owner decision 2026-10-20).
    /// </summary>
    [OriginalFunction(0x578760, OriginalFunctionScope.Partial)]
    public static ItemRepairQuote RepairQuote(ItemCondition item, int undamagedPrice, RepairTraining repairer, ItemRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(repairer)) throw new ArgumentOutOfRangeException(nameof(repairer));
        ArgumentOutOfRangeException.ThrowIfNegative(undamagedPrice);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(undamagedPrice, ItemDurabilityLimits.MaxPrice);
        var maximum = MaximumHitPoints(item);
        int damage;
        if (item.Broken)
        {
            if (repairer != RepairTraining.Master) return new(ItemQuoteStatus.BrokenNeedsMaster, 0);
            damage = maximum;
        }
        else
        {
            damage = item.Damage;
            if (damage == 0) return new(ItemQuoteStatus.NotNeeded, 0);
        }
        var price = ScalePrice(undamagedPrice, item, pcSells: profile.RepairQuoteCountsDamageTwice);
        // A maximum of 0 cannot come from the installed data (every item prototype has at least 1); the quote is then the minimum.
        var cost = maximum == 0 ? 0 : (long)(price / 2) * damage / maximum;
        return new(ItemQuoteStatus.Quoted, (int)Math.Clamp(cost, 2, ItemDurabilityLimits.MaxPrice));
    }

    /// <summary>
    /// How many hit points a repair restores: the whole maximum with the PC's fate full repair, else <c>maximum × effectiveness /
    /// 100</c> (the repairer's Repair effectiveness from the skills owner); a result of 0 or less is 1 (the service forces
    /// success).
    /// </summary>
    public static int RepairAmount(ItemCondition item, int effectiveness, bool fullRepair)
    {
        var maximum = MaximumHitPoints(item);
        var amount = fullRepair ? maximum : (int)((long)maximum * effectiveness / 100);
        return amount <= 0 ? 1 : amount;
    }

    /// <summary>
    /// ARC-SKILLS-007: how many hit points the Repair skill's own run restores (the repair case of the skill run, ledger RP2): the
    /// whole maximum with the PC's fate full repair, else <c>maximum × effectiveness / 100</c>, at least 1 only while the
    /// effectiveness is above 0; with an effectiveness of 0 or less nothing is restored (the run is not forced, unlike the
    /// service's, <see cref="RepairAmount"/>).
    /// </summary>
    public static int SkillRepairAmount(ItemCondition item, int effectiveness, bool fullRepair)
    {
        var maximum = MaximumHitPoints(item);
        // The floor follows both branches, as in the case: a fated repair of an item whose maximum is 0 or less is 1 or 0 too.
        var amount = fullRepair ? maximum : (int)((long)maximum * effectiveness / 100);
        return amount > 0 ? amount : effectiveness > 0 ? 1 : 0;
    }

    /// <summary>
    /// ARC-SKILLS-007: the hp_adj share of the Repair skill's own run (ledger RP2): <see cref="RepairAdjustmentPercent"/>, except that
    /// a master who restores nothing (an effectiveness of 0, which clears the case's success) on an item that is not broken takes 1 %
    /// when the check's critical flag is set.
    /// </summary>
    public static int SkillRepairAdjustmentPercent(RepairTraining repairer, bool broken, int amount, bool critical) =>
        repairer == RepairTraining.Master && !broken && amount == 0 && critical ? 1 : RepairAdjustmentPercent(repairer, broken);

    /// <summary>The share of the maximum a repair takes from hp_adj, by the repairer's training: none 10, apprentice 5, expert 1,
    /// master 0, and 5 when a master repairs a broken item (ledger RP2: a repairer who is not a master keeps the table for a broken
    /// item that took damage again).</summary>
    public static int RepairAdjustmentPercent(RepairTraining repairer, bool broken)
    {
        if (!Enum.IsDefined(repairer)) throw new ArgumentOutOfRangeException(nameof(repairer));
        return broken && repairer == RepairTraining.Master ? 5
            : repairer switch { RepairTraining.None => 10, RepairTraining.Apprentice => 5, RepairTraining.Expert => 1, _ => 0 };
    }

    /// <summary>
    /// The item side of a repair (the repair service's skill case): nothing for an item without damage unless it is broken and
    /// the repairer a master, who clears the destroyed bit of the current art only. A broken item that took damage again is
    /// repaired by any repairer and stays broken unless the repairer is a master (only the quote turns a broken item away from
    /// others). Otherwise the damage falls by
    /// <paramref name="amount"/> (to 0 at least) and hp_adj by <c>trunc(maximum × percent / 100)</c> of the maximum before the
    /// repair, without a clamp. An amount of 0 (the skill's own run at effectiveness 0) restores nothing and still takes the share.
    /// <paramref name="amount"/> is <see cref="RepairAmount"/> or <see cref="SkillRepairAmount"/> and <paramref name="adjustmentPercent"/>
    /// <see cref="RepairAdjustmentPercent"/> as the original passes them; the payment, made whatever the skill gives, is the
    /// caller's.
    /// </summary>
    [OriginalFunction(0x4C8E60, OriginalFunctionScope.Partial)]
    public static ItemRepairOutcome Repair(ItemCondition item, int amount, int adjustmentPercent, RepairTraining repairer)
    {
        if (!Enum.IsDefined(repairer)) throw new ArgumentOutOfRangeException(nameof(repairer));
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfNegative(adjustmentPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(adjustmentPercent, 100);
        var master = repairer == RepairTraining.Master;
        if (item.Damage <= 0 && !(item.Broken && master)) return new(ItemRepairStatus.NothingToRepair, item);
        var maximum = MaximumHitPoints(item);
        var repaired = item with
        {
            Damage = Math.Max(0, item.Damage - amount),
            Adjustment = (int)Math.Clamp(item.Adjustment - (long)maximum * adjustmentPercent / 100, -ItemDurabilityLimits.MaxMagnitude, ItemDurabilityLimits.MaxMagnitude),
            Broken = item.Broken && !master,
        };
        return new(ItemRepairStatus.Repaired, repaired);
    }

    /// <summary>Stored values within the bounds: hp_pts and hp_adj within ±<see cref="ItemDurabilityLimits.MaxMagnitude"/>,
    /// damage 0..<see cref="ItemDurabilityLimits.MaxDamage"/>.</summary>
    public static bool IsValid(ItemCondition item) =>
        Math.Abs((long)item.Points) <= ItemDurabilityLimits.MaxMagnitude && Math.Abs((long)item.Adjustment) <= ItemDurabilityLimits.MaxMagnitude &&
        item.Damage is >= 0 and <= ItemDurabilityLimits.MaxDamage;
}
