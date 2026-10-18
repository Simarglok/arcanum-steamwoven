using System.Buffers.Binary;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

// Literal scenery FILE fixtures. Field indices/widths below are test-owned wire
// facts, never queried from the production schema or normalized by its reader.
[TestClass]
public sealed class AreaWireBoundaryTests
{
    [TestMethod]
    public void Direct_zero_is_not_absence_and_absent_offsets_inherit_signed_prototype_values()
    {
        var proto = PrototypeWithDefaults();
        using (var f = new Fixture([(67108865, [Fields((1, Location()), (0, I32Bytes(0)))])], prototype: proto))
        {
            // The direct art id 0 replaces the prototype's: a tile id, so the object is drawn from tile 0's file (N7).
            var snapshot = f.Owner.LoadStrict();
            var obj = snapshot.Objects.Single();
            Assert.AreEqual(0L, obj.DirectFields.Single(v => v.Slot == 0).Number);
            Assert.AreEqual(0L, obj.EffectiveFields.Single(v => v.Slot == 0).Number);
            Assert.AreEqual(AreaValueOrigin.Instance, obj.EffectiveFields.Single(v => v.Slot == 0).Origin);
            Assert.AreEqual(0U, snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static).RawArt);
        }
        using (var f = new Fixture([(67108865, [Fields((1, Location()))])], prototype: proto))
        {
            var snapshot = f.Owner.LoadStrict(); var obj = snapshot.Objects.Single();
            Assert.IsFalse(obj.DirectFields.Any(v => v.Slot is 2 or 3));
            Assert.AreEqual(-7L, obj.EffectiveFields.Single(v => v.Slot == 2).Number);
            Assert.AreEqual(11L, obj.EffectiveFields.Single(v => v.Slot == 3).Number);
            var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(-7, visual.ObjectOffsetX); Assert.AreEqual(11, visual.ObjectOffsetY);
        }
    }

    [TestMethod]
    public void Absent_null_empty_and_nonempty_arrays_preserve_distinct_provenance_and_sparse_keys()
    {
        byte[][] arrays = [new byte[] { 0 }, Array(4, [], [0]), Array(4, [11,22], [1,1])];
        foreach (var direct in new byte[]?[] { null, arrays[0], arrays[1], arrays[2] })
        {
            var record = direct is null ? Fields((1, Location())) : Fields((1, Location()), (5, direct));
            using var f = new Fixture([(67108865, [record])], prototype: PrototypeWithDefaults());
            var obj = f.Owner.LoadStrict().Objects.Single(); var field = obj.EffectiveFields.Single(v => v.Slot == 5);
            if (direct is null)
            {
                Assert.AreEqual(AreaValueOrigin.Prototype, field.Origin);
                Assert.AreEqual(AreaPresence.Array, field.Presence);
                CollectionAssert.AreEqual(new[] { 0,32 }, field.Elements.Select(v => v.Key).ToArray());
                CollectionAssert.AreEqual(new long[] { 11,22 }, field.Elements.Select(v => v.Words.Single()).ToArray());
            }
            else
            {
                Assert.AreEqual(AreaValueOrigin.Instance, field.Origin);
                Assert.AreEqual(direct == arrays[0] ? AreaPresence.Null : direct == arrays[1] ? AreaPresence.Empty : AreaPresence.Array, field.Presence);
                Assert.AreEqual(direct == arrays[2] ? 2 : 0, field.Elements.Count);
            }
        }
    }

    [TestMethod]
    public void One_word_and_two_word_sparse_storage_reject_zero_population_width_truncation_and_cap_plus_one()
    {
        var one = Array(4, [17], [1]); var two = Array(4, [11,22], [1,1]);
        foreach (var valid in new[] { one, two })
        {
            using var f = new Fixture([(67108865, [Fields((1, Location()), (5, valid)), Record(Id(0))])]);
            var snapshot = f.Owner.LoadStrict(); Assert.AreEqual(2, snapshot.Objects.Count);
            var copied = snapshot.Objects[0].DirectFields.Single(v => v.Slot == 5).Elements;
            CollectionAssert.AreEqual(valid == one ? new[] { 0 } : new[] { 0,32 }, copied.Select(v => v.Key).ToArray());
        }
        var badWidth = one.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badWidth.AsSpan(1), 8);
        var badPopulation = one.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badPopulation.AsSpan(21), 3);
        var zeroWords = Array(4, [], []);
        var excessive = one.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(excessive.AsSpan(5), 2049);
        foreach (var broken in new[] { badWidth, badPopulation, zeroWords, one[..^1], excessive })
        {
            using var f = new Fixture([(67108865, [Fields((1, Location()), (5, broken)), Record(Id(0))])]);
            var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
            Assert.IsNull(f.Owner.PublishedSnapshot);
            Assert.IsTrue(report.Issues.Any(i => i.SourceIndex == 0 && i.Stage == AreaStage.Sector), string.Join(',', report.Issues));
        }
    }

    [TestMethod]
    public void Every_record_truncation_bad_presence_count_mask_and_version_fails_before_publication()
    {
        var record = Fields((1, Location()), (5, Array(4, [17], [1])));
        for (var length = 0; length < record.Length; length++)
        {
            using var f = new Fixture([(67108865, [record[..length], Record(Id(0))])]);
            Assert.IsFalse(f.Owner.Inspect().IsComplete, $"Accepted truncation {length}.");
            Assert.IsNull(f.Owner.PublishedSnapshot);
        }
        var version = record.ToArray(); version[0] = 118;
        var population = record.ToArray(); population[56] = 3;
        var mask = record.ToArray(); mask[65] |= 128; // forbidden common mask suffix
        var presence = record.ToArray(); presence[70] = 2;
        foreach (var invalid in new[] { version, population, mask, presence, record.Concat(new byte[] { 0 }).ToArray() })
        {
            using var f = new Fixture([(67108865, [invalid])]);
            Assert.IsFalse(f.Owner.Inspect().IsComplete); Assert.IsNull(f.Owner.PublishedSnapshot);
        }
    }

    [TestMethod]
    public void Static_high_bits_rebase_low_six_bits_mobile_world_is_unchanged_and_edges_have_no_collisions()
    {
        var raw = unchecked((long)0x765432207654321eUL);
        using var f = new Fixture([(67108865, [Fields((1, Location(raw)))]), (67108866, [])], [Record(Id(1, 42), 274877907008)]);
        var s = f.Owner.LoadStrict(); var st = s.Objects.Single(o => o.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(94L, st.WorldX); Assert.AreEqual(96L, st.WorldY);
        var mobile = s.Objects.Single(o => o.Key.Kind == AreaSourceKind.Mobile);
        Assert.AreEqual(64L, mobile.WorldX); Assert.AreEqual(64L, mobile.WorldY);
        foreach (var (index, x, y) in new[] { (0,64L,64L), (63,127L,64L), (4032,64L,127L), (4095,127L,127L), (4096,128L,64L) })
        {
            Assert.AreEqual(x, s.Terrain[index].WorldX); Assert.AreEqual(y, s.Terrain[index].WorldY);
        }
        Assert.AreEqual(8192, s.Terrain.Select(v => v.Key).Distinct().Count());
        foreach (var n in new[] { -1L, 67108864L, long.MaxValue }) Throws<ArgumentOutOfRangeException>(() => new AreaSectorCoordinate(n, 0));
        foreach (var n in new[] { -1L, 4503599627370496L, long.MaxValue }) Throws<ArgumentOutOfRangeException>(() => AreaSectorCoordinate.Unpack(n));
    }

    [TestMethod]
    public void Both_axis_shared_edges_and_corner_have_literal_world_coordinates_and_distinct_keys()
    {
        using var f = new Fixture([(67108865, []), (67108866, []), (134217729, []), (134217730, [])]);
        var snapshot = f.Owner.LoadStrict();
        foreach (var (sector, ordinal, x, y) in new[] {
            (67108865L,4095,127L,127L), (67108866L,4032,128L,127L),
            (134217729L,63,127L,128L), (134217730L,0,128L,128L) })
        {
            var tile = snapshot.Terrain.Single(t => t.Key.Sector.Packed == sector && t.Key.SourceIndex == ordinal);
            Assert.AreEqual(x,tile.WorldX); Assert.AreEqual(y,tile.WorldY);
        }
        Assert.AreEqual(16384, snapshot.Terrain.Select(t => t.Key).Distinct().Count());
        Assert.AreEqual(16384, snapshot.Terrain.Select(t => (t.WorldX,t.WorldY)).Distinct().Count());
    }

    private static byte[] Fields(params (int Slot, byte[] Bytes)[] values)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(1,7001)); s.Write(Id(0)); I32(s, 3);
        s.WriteByte((byte)values.Length); s.WriteByte(0);
        I32(s, values.Where(v => v.Slot < 32).Aggregate(0, (mask,v) => mask | 1 << v.Slot));
        I32(s, values.Where(v => v.Slot is >= 32 and < 36).Aggregate(0, (mask,v) => mask | 1 << (v.Slot-32)));
        I32(s, values.Where(v => v.Slot >= 36).Aggregate(0, (mask,v) => mask | 1 << (v.Slot-36)));
        foreach (var v in values.OrderBy(v => v.Slot)) s.Write(v.Bytes); return s.ToArray();
    }
    private static byte[] Location(long value = 0) { using var s = new MemoryStream(); s.WriteByte(1); I64(s,value); return s.ToArray(); }
    private static byte[] I32Bytes(int value) { using var s = new MemoryStream(); I32(s,value); return s.ToArray(); }
    private static byte[] Array(int width, int[] values, int[] words)
    {
        using var s = new MemoryStream(); s.WriteByte(1); I32(s,width); I32(s,values.Length); I32(s,19);
        foreach (var v in values) I32(s,v); I32(s,words.Length); foreach (var w in words) I32(s,w); return s.ToArray();
    }
    private static byte[] PrototypeWithDefaults()
    {
        using var s = new MemoryStream(); I32(s,119); s.Write(Id(-1)); s.Write(Id(1,7001)); I32(s,3); I32(s,0); I32(s,0); I32(s,0);
        int[] optional = [1,5,6,7,15,16,17,30,31,34,35,37,40,41];
        for (var slot = 0; slot < 42; slot++)
        {
            if (slot == 5) s.Write(Array(4,[11,22],[1,1]));
            else if (optional.Contains(slot)) s.WriteByte(0);
            else I32(s, slot == 0 ? 0x40000000 : slot == 2 ? -7 : slot == 3 ? 11 : slot == 11 ? 100 : 0);
        }
        return s.ToArray();
    }
}
