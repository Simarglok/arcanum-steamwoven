using System.Buffers.Binary;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed class AreaHandleArrayTests
{
    [TestMethod]
    public void Container_inventory_and_critter_inventory_follower_are_24_byte_sparse_arrays()
    {
        byte[][] records = [ArrayRecord(2, 40, 41), ArrayRecord(16, 56, 42), ArrayRecord(16, 59, 43)];
        using var fixture = new Fixture([(67108865, [])], records);
        var report = fixture.Owner.Inspect();
        Assert.IsTrue(report.IsComplete, string.Join(',', report.Issues));
        var snapshot = report.Snapshot!;
        Assert.AreEqual(3, snapshot.Reconciliation.Aggregate);
        Assert.AreEqual(3, snapshot.Reconciliation.OutsideMobiles);
        Assert.AreEqual(3, snapshot.Reconciliation.EditorCorrespondence);
    }

    [TestMethod]
    public void Array_population_width_and_truncation_are_validated_before_next_record()
    {
        var valid = ArrayRecord(2, 40, 41);
        var wrongWidth = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(wrongWidth.AsSpan(80), 4);
        var badCount = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badCount.AsSpan(84), 2);
        var badId = valid.ToArray(); BinaryPrimitives.WriteInt16LittleEndian(badId.AsSpan(92), -1);
        foreach (var broken in new[] { wrongWidth, badCount, badId, valid[..^1] })
        {
            using var fixture = new Fixture([(67108865, [])], [broken, Record(Id(1, 42))]);
            var report = fixture.Owner.Inspect();
            Assert.IsFalse(report.IsComplete);
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.MalformedRecord && i.Stage == AreaStage.Aggregate && i.SourceIndex == 0), string.Join(',', report.Issues));
        }
    }

    [TestMethod]
    public void Owner_cold_and_repeated_large_id_arrays_cover_allocations_without_payload_cache()
    {
        // FILE fixture is prepared before measuring the actual public owner.
        // Run this test alone as well, so another test cannot supply schema warm-up.
        using var fixture = new Fixture([(67108865, [])], [ArrayRecord(2, 40, 41, 2048)]);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var prior = fixture.Owner.Usage.ManagedReserved;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var report = fixture.Owner.Inspect();
            var observed = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(report.IsComplete, string.Join(',', report.Issues));
            Assert.AreEqual(1, report.Snapshot!.Reconciliation.Aggregate);
            Assert.AreEqual(1, report.Snapshot.Reconciliation.OutsideMobiles);
            var admitted = fixture.Owner.Usage.ManagedReserved - prior;
            Console.WriteLine($"owner ID-array attempt={attempt} observed={observed} admitted={admitted}");
            Assert.IsTrue(observed <= admitted, $"Owner allocated {observed}, reserved {admitted}.");
        }
    }

    [TestMethod]
    public void Aggregate_record_byte_limit_rejects_before_sparse_graph_growth_and_editor_access()
    {
        // A 44,000-element 24-byte FILE array fits the aggregate budget but not
        // the existing one-MiB record boundary. This is not a count-cap failure.
        var record = ArrayRecord(2, 40, 41, 44000);
        Assert.IsTrue(record.Length > 1048576 && record.Length + 16 < 1823280);
        using var fixture = new Fixture([(67108865, [])], [record]);
        var report = fixture.Owner.Inspect();
        Assert.IsFalse(report.IsComplete); Assert.IsNull(fixture.Owner.PublishedSnapshot);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.BudgetExceeded && i.Stage == AreaStage.Aggregate && i.SourceIndex == 0),
            string.Join(',',report.Issues));
    }

    [TestMethod]
    public void Exact_record_byte_cap_is_admitted_but_extra_FILE_capacity_is_rejected()
    {
        var exact = ArrayRecord(2, 40, 41, 43000, 2776);
        Assert.AreEqual(1048576, exact.Length);
        using (var fixture = new Fixture([(67108865, [])], [exact])) Assert.IsTrue(fixture.Owner.Inspect().IsComplete);
        using var larger = new Fixture([(67108865, [])], [ArrayRecord(2, 40, 41, 43000, 2777)]);
        var report = larger.Owner.Inspect();
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.BudgetExceeded && i.Stage == AreaStage.Aggregate));
    }

    private static byte[] ArrayRecord(int type, int slot, int own, int elements = 1, int extraZeroWords = 0)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1, 7001)); s.Write(Id(1, own)); I32(s, type);
        s.Write(new byte[] { 2, 0 }); I32(s, 2); I32(s, 0);
        I32(s, 1 << (slot - 36));
        if (type == 16) { I32(s, 0); I32(s, 0); }
        s.WriteByte(1); I64(s, 27487790700800); s.WriteByte(1); I32(s, 24); I32(s, elements); I32(s, 19);
        for (var i = 0; i < elements; i++) s.Write(Id(1, 123 + i));
        var occupiedWords = (elements + 31) / 32;
        var words = occupiedWords + extraZeroWords; I32(s, words);
        for (var i = 0; i < words; i++)
            I32(s, i >= occupiedWords ? 0 : i == occupiedWords - 1 && elements % 32 != 0 ? (1 << (elements % 32)) - 1 : -1);
        return s.ToArray();
    }
}
