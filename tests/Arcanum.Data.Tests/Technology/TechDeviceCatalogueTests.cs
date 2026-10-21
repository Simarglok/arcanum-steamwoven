using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Data.Technology;
using Arcanum.Data.Tests.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-008: the catalogue of technology items and records over invented records and items: the slots naming
/// a technology record, the records' maintenance and ChargeNBranch costs, what MAINTAIN pays, and an out-of-range spell.</summary>
[TestClass]
public sealed class TechDeviceCatalogueTests
{
    private static int L(int spell, int offset) => SpellRecordTable.FirstMessage(spell) + offset;

    private static CombatFields Item(int type, int complexity, int charges, params int[] spells) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields(type, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 1, 1, 0, complexity, 0, 0, spells, charges, 0, 0, 0), null, null, null, null, null, null);

    [TestMethod]
    public void Technology_slots_and_records_say_what_reaches_the_engine_and_what_it_pays()
    {
        var read = SpellRecordTable.Read(SpellRecordTableTests.List(
            (L(150, 1), "Cost: 4, Maintain: (2 @ 10)"),
            (L(151, 1), "Maintain: (0 @ 5), ChargesFromCells: 1"),
            (L(151, 5), "[Maintain], Type: ChargeNBranch, Cost: 1"),
            (L(152, 1), "ChargesFromCells: 1"),
            (L(152, 5), "[Begin], Type: ChargeNBranch, Cost: 2"),
            (L(153, 1), "Maintain: (3 @ 5), ChargesFromCells: 1")), SpellRecordTableTests.Types, SpellRecordMode.Strict);
        Assert.AreEqual(SpellDataStatus.Read, read.Status, $"{read.Refusal}");
        var catalogue = TechDeviceCatalogue.Build([
            (6020, Item(7, -80, 1, 151, 10000, 10000, 10000, 10000)),
            (6010, Item(5, -50, -1, 10000, 152, 30, 10000, 10000)),
            (6030, Item(9, 50, 0, 150, 99999, 10000, 10000, 10000)),
            (6040, new CombatFields(CombatFieldOrigin.Prototype, new CombatObjectFields(16, 2, 0, 0, 0, 0, 0, 1, 0, 0, 0, [0, 0, 0, 0, 0], 0), null, null, null, null, null, null, null)),
        ], read.Table!);
        CollectionAssert.AreEqual(new[]
        {
            new TechDeviceSlot(6010, 5, -50, 1, 152, -1), new TechDeviceSlot(6020, 7, -80, 0, 151, 1), new TechDeviceSlot(6030, 9, 50, 0, 150, 0),
        }, catalogue.Slots.ToArray(), "a magic record (30), no spell (10000) and a non-item are not devices; a magickal food carrying a technology record is");
        Assert.AreEqual(new TechDeviceAnomaly(6030, 1, 99999), catalogue.Anomalies.Single(), "a spell outside 0..222");
        Assert.AreEqual(SpellRecord.Count - SpellRecord.FirstTechnology, catalogue.Records.Count);
        Assert.AreEqual(TechMaintainPayment.SourcePayment, catalogue.Record(150)!.MaintainPayment, "a maintained record without cells: the source's payment");
        Assert.AreEqual((TechMaintainPayment.None, 1, 0), (catalogue.Record(151)!.MaintainPayment, catalogue.Record(151)!.BranchCost(SpellAction.Maintain),
            catalogue.Record(151)!.BranchCost(SpellAction.Begin)), "maintenance 0: the stage pays nothing; the Maintain branch takes a cell a tick");
        Assert.AreEqual((TechMaintainPayment.None, 2), (catalogue.Record(152)!.MaintainPayment, catalogue.Record(152)!.BranchCost(SpellAction.Begin)));
        Assert.AreEqual(TechMaintainPayment.Cells, catalogue.Record(153)!.MaintainPayment);
        CollectionAssert.AreEqual(new[] { 150, 151, 153 }, catalogue.Maintained.Select(r => r.Spell).ToArray());
        Assert.IsTrue(catalogue.Records.All(r => !r.PaysAtBegin));
        Assert.IsNull(catalogue.Record(30));
    }
}
