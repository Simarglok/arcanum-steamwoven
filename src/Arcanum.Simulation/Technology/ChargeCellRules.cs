using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;

namespace Arcanum.Simulation.Technology;

/// <summary>
/// The object whose charge cells pay (ledger CC2–CC3): a critter (its one power-cell stack, critter field 0xEB), a container (the
/// original looks for the first item of the cell prototype in its inventory but passes the raw prototype number where a handle is
/// compared, so it never finds one: no cells, ledger AM6, AMA5), none (an item without a parent), or any other object (no cells).
/// </summary>
public enum CellOwnerKind
{
    None,
    Critter,
    Container,
    Other,
}

/// <summary>A stack of charge cells (ammunition of type 2): its entity and its unit count (the ammunition quantity, field 0x8E).</summary>
public readonly record struct CellStack(string Entity, int Units);

/// <summary>Why a cell payment was refused, or <see cref="None"/>.</summary>
public enum CellRefusal
{
    None,
    /// <summary>The owner has no cell stack, or fewer units than the need (nothing is taken).</summary>
    TooFewCells,
    /// <summary>The need or the product <c>power × cost</c> does not fit 32 bits, or the item's complexity or the holder's aptitude
    /// is out of range: the original wraps (and can add cells, or fault on a division); both settings refuse (ledger CCA2).</summary>
    OutOfRange,
    /// <summary>The payment's payer kind differs from the payer the source was built for (a wiring error of the host).</summary>
    PayerMismatch,
    /// <summary>The cells were enough but writing them was refused (a session's budget or revision); nothing was taken.</summary>
    WriteRefused,
}

/// <summary>
/// The facts of a cell payment the host gives: who pays (a critter pays from itself; an item from its parent), the item's
/// magick/tech complexity (an item payer only), the parent's kind and, for a critter parent, its magick/tech aptitude, and the
/// owner's one cell stack (null: none).
/// </summary>
public sealed record CellPayer(SpellPayerKind Payer, CellOwnerKind Owner, CellStack? Cells, int ItemComplexity = 0, int HolderAptitude = 0);

/// <summary>
/// A cell payment plan: whether it is paid, the cells needed, the stack it takes from, that stack's units after and whether the
/// stack is removed (its last units taken, as the original destroys it). <see cref="Applied"/> says whether the host writes it:
/// a request without "apply" (an affordability check) reports the same answer and writes nothing (ledger CCA1).
/// </summary>
public sealed record CellPlan(bool Paid, CellRefusal Refusal, int Need, CellStack? Stack, int UnitsAfter, bool StackRemoved, bool Applied)
{
    /// <summary>The plan the host writes: paid and applied.</summary>
    public bool Writes => Paid && Applied;
}

/// <summary>
/// ARC-TECHNOLOGY-007: the charge-cell branch of the spell payment (0x450420, record flag 0x20 <c>ChargesFromCells:</c>; contract
/// docs/world/charge-cell-runtime.md, ledger "Charge cells (ARC-TECHNOLOGY-007)"). Pure rules: the cells a payer needs, the one
/// stack they come from and the units it keeps. No state is written here (the session's <c>HeroCellSource</c> spends the cells,
/// ARC-WORLD-066).
/// </summary>
public static class ChargeCellRules
{
    /// <summary>The ammunition type of charge cells.</summary>
    public const int CellAmmoType = 2;

    /// <summary>
    /// The cells a payer needs (CC1, CC4): a critter or another object pays the cost (never halved for a fatigue-limiting critter);
    /// an item pays <c>2 × cost − adjust(cost)</c> with the item magic adjustment for its parent (0x461590): a neutral item the
    /// cost; a magickal one (complexity &gt; 0) <c>2 × cost − trunc(cost × p / c)</c>; a technological one (complexity &lt; 0)
    /// <c>cost + trunc(cost × p / c)</c> in Original, <c>2 × cost − trunc(cost × p / c)</c> in Steamwoven (TC1: an experienced
    /// technologist pays fewer cells). The power p is the parent's when it is a critter (0x4614A0), the complexity itself for any
    /// other parent. Null: out of range (CCA2). A cost of 0 or less needs nothing (the payment returns before the cells).
    /// </summary>
    public static int? Need(CellPayer payer, int cost, TechnologyRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(profile);
        if (cost <= 0) return 0;
        if (payer.Payer != SpellPayerKind.Item) return cost;
        var c = payer.ItemComplexity;
        if (c is < -EquipmentLimits.MaxMagnitude or > EquipmentLimits.MaxMagnitude) return null;
        if (c == 0) return cost;
        int power;
        if (payer.Owner == CellOwnerKind.Critter)
        {
            if (payer.HolderAptitude is < EquipmentLimits.MinAptitude or > EquipmentLimits.MaxAptitude) return null;
            power = EquipmentRules.EffectivePower(c, payer.HolderAptitude);
        }
        else power = c;
        var product = (long)cost * power;
        if (product is < int.MinValue or > int.MaxValue) return null;
        var share = product / c;
        long need = c > 0 || !profile.ItemCellsFavourMagick ? 2L * cost - share : cost + share;
        return need is > 0 and <= int.MaxValue ? (int)need : null;
    }

    /// <summary>
    /// The cell payment (CC2–CC5): the owner's one stack must hold at least the need, else nothing is taken; the stack keeps the
    /// rest and is removed when its last units go (0x4658E0). The original takes the cells whether or not the payment is applied
    /// (CCA1); here an unapplied request answers the same and the plan is not <see cref="CellPlan.Applied"/>. An item without a
    /// parent or another owner counts no cells (CCA3, CCA4). The mark is the count's (0x465820): the plan reads the one stack the host
    /// found; the need's arithmetic is the payment's (0x450420, marked on <see cref="SpellPaymentRules.Pay"/>).
    /// </summary>
    [OriginalFunction(0x465820, OriginalFunctionScope.Partial)]
    public static CellPlan Plan(CellPayer payer, int cost, bool apply, TechnologyRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(payer);
        if (Need(payer, cost, profile) is not { } need) return new(false, CellRefusal.OutOfRange, 0, null, 0, false, apply);
        if (need == 0) return new(true, CellRefusal.None, 0, null, 0, false, apply);
        // A critter gives its one stack; a container's lookup never matches in the original (AM6), and an item without a parent or
        // another object counts 0.
        var stack = payer.Owner == CellOwnerKind.Critter ? payer.Cells : null;
        if (stack is not { } cells || cells.Units < need || SchematicAssemblyRules.UseUnits(cells.Units, need) is not { } left)
            return new(false, CellRefusal.TooFewCells, need, stack, stack?.Units ?? 0, false, apply);
        return new(true, CellRefusal.None, need, cells, left.Units, left.Destroyed, apply);
    }
}

/// <summary>
/// The engine's charge-cell port (<see cref="ISpellCellSource"/>) over one payer's facts: each request is answered by
/// <see cref="ChargeCellRules.Plan"/> and the last plan is kept for the host to write (<see cref="LastPlan"/>).
/// </summary>
public sealed class ChargeCellSource : ISpellCellSource
{
    private readonly CellPayer _payer;
    private readonly TechnologyRuleProfile _profile;

    public ChargeCellSource(CellPayer payer, TechnologyRuleProfile profile)
    {
        _payer = payer ?? throw new ArgumentNullException(nameof(payer));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>The plan of the last request, or null before any.</summary>
    public CellPlan? LastPlan { get; private set; }

    public SpellCellPayment Pay(SpellCellRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = request.Payer != _payer.Payer
            ? new CellPlan(false, CellRefusal.PayerMismatch, 0, null, 0, false, request.Apply)
            : ChargeCellRules.Plan(_payer, request.Cost, request.Apply, _profile);
        LastPlan = plan;
        return new(plan.Paid, plan.Need);
    }
}
