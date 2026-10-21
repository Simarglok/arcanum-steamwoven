using Arcanum.Data.Combat;
using Arcanum.Data.Magic;

namespace Arcanum.Data.Technology;

/// <summary>What a technology record's run pays at each tick of MAINTAIN (ledger CC7, TD rows): nothing (not maintained), charge
/// cells (<c>ChargesFromCells:</c>, the charge-cell source of ARC-TECHNOLOGY-007), or the source's own payment (a critter's
/// mana stores and fatigue, an item's spell charge).</summary>
public enum TechMaintainPayment
{
    None,
    Cells,
    SourcePayment,
}

/// <summary>A ChargeNBranch component (0x451850): the action whose component list holds it and its <c>Cost:</c>, paid through the
/// spell payment (cells for a record with <c>ChargesFromCells:</c>) each time that action's components run.</summary>
public readonly record struct TechCellBranch(SpellAction Action, int Cost);

/// <summary>
/// One technology record (140..222) as the magic engine runs it: its number, <c>Cost:</c>, the maintenance (cost and period;
/// a period above 0 makes it maintained), whether it takes charge cells, its ChargeNBranch components and what MAINTAIN pays.
/// A technology record never pays its <c>Cost:</c> at BEGIN (the stage skips the payment for every record with the technology
/// flag, which every record from 140 carries; ledger CC7); its BEGIN components still run, a ChargeNBranch among them.
/// </summary>
public sealed record TechRecordEntry(int Spell, int Cost, int MaintainCost, int MaintainPeriod, bool ChargesFromCells, IReadOnlyList<TechCellBranch> CellBranches)
{
    public bool Maintained => MaintainPeriod > 0;
    /// <summary>The stage's BEGIN payment of <c>Cost:</c>: never for a technology record.</summary>
    public bool PaysAtBegin => false;
    /// <summary>What the stage charges at each MAINTAIN tick: the maintenance cost through the payment (cells for a cell record);
    /// a cost of 0 or less pays nothing there (the payment returns before its branches).</summary>
    public TechMaintainPayment MaintainPayment => !Maintained || MaintainCost <= 0 ? TechMaintainPayment.None
        : ChargesFromCells ? TechMaintainPayment.Cells : TechMaintainPayment.SourcePayment;
    /// <summary>The cost the record's ChargeNBranch components ask on an action's run (summed).</summary>
    public int BranchCost(SpellAction action) => CellBranches.Where(b => b.Action == action).Sum(b => b.Cost);
}

/// <summary>One item spell slot that names a technology record: the item's prototype, object type, magick/tech complexity
/// (negative: technological), the slot (0..4), the record, and the item's spell charges (negative: unlimited; 0: the payment
/// refuses).</summary>
public sealed record TechDeviceSlot(int Prototype, int ObjectType, int Complexity, int Slot, int Spell, int Charges)
{
    public bool Technological => Complexity < 0;
}

/// <summary>An item spell slot left out of the catalogue: a spell number that is neither a record 0..222 nor the "no spell" 10000.
/// The original's trigger executors run the slot's low byte (0x4CB790), so such a value runs record <c>spell &amp; 0xFF</c>, and
/// 223..255 after the mask read past the record table (ledger DV1).</summary>
public sealed record TechDeviceAnomaly(int Prototype, int Slot, int Spell);

/// <summary>
/// ARC-TECHNOLOGY-008: which technology items and records reach the magic engine (contract docs/world/tech-devices-runtime.md):
/// every item spell slot naming a record 140..222, and every technology record with what it pays. Built from the item
/// prototypes' combat fields (instances over them are the host's) and the spell records.
/// </summary>
public sealed class TechDeviceCatalogue
{
    public const int NoSpell = 10000, ItemSpellSlots = 5;

    private TechDeviceCatalogue(IReadOnlyList<TechDeviceSlot> slots, IReadOnlyList<TechRecordEntry> records, IReadOnlyList<TechDeviceAnomaly> anomalies)
    {
        Slots = slots;
        Records = records;
        Anomalies = anomalies;
    }

    /// <summary>The device slots in prototype and slot order.</summary>
    public IReadOnlyList<TechDeviceSlot> Slots { get; }
    /// <summary>The technology records 140..222 in number order.</summary>
    public IReadOnlyList<TechRecordEntry> Records { get; }
    public IReadOnlyList<TechDeviceAnomaly> Anomalies { get; }

    /// <summary>The record entry of a technology record, or null for a magic record or a number out of range.</summary>
    public TechRecordEntry? Record(int spell) => spell is >= SpellRecord.FirstTechnology and < SpellRecord.Count ? Records[spell - SpellRecord.FirstTechnology] : null;

    /// <summary>The maintained technology records.</summary>
    public IEnumerable<TechRecordEntry> Maintained => Records.Where(r => r.Maintained);

    /// <summary>The catalogue of the items' spell slots (the five item spell fields, 10000 none, as the slot reader 0x4CB7A0 reads
    /// them; ledger DV1) and the technology records.</summary>
    [OriginalFunction(0x4CB7A0, OriginalFunctionScope.Partial)]
    public static TechDeviceCatalogue Build(IEnumerable<(int Prototype, CombatFields Fields)> items, SpellRecordTable records)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(records);
        var entries = new List<TechRecordEntry>();
        for (var spell = SpellRecord.FirstTechnology; spell < SpellRecord.Count; spell++)
        {
            var r = records[spell];
            entries.Add(new(spell, r.Cost, r.MaintainCost, r.MaintainPeriod, r.ChargesFromCells, r.Components
                .Where(c => c.Type == SpellComponentType.ChargeNBranch && c.Data is ChargeBranchData)
                .Select(c => new TechCellBranch(c.Action, ((ChargeBranchData)c.Data!).Cost)).ToArray()));
        }
        var slots = new List<TechDeviceSlot>();
        var anomalies = new List<TechDeviceAnomaly>();
        foreach (var (prototype, fields) in items.OrderBy(i => i.Prototype))
        {
            if (fields?.Item is not { } item) continue;
            for (var slot = 0; slot < Math.Min(ItemSpellSlots, item.Spells.Count); slot++)
            {
                var spell = item.Spells[slot];
                if (spell == NoSpell) continue;
                if (spell is < 0 or >= SpellRecord.Count) { anomalies.Add(new(prototype, slot, spell)); continue; }
                if (spell >= SpellRecord.FirstTechnology)
                    slots.Add(new(prototype, fields.Object.Type, item.MagickTechComplexity, slot, spell, item.SpellManaStore));
            }
        }
        return new(slots.AsReadOnly(), entries.AsReadOnly(), anomalies.AsReadOnly());
    }
}
