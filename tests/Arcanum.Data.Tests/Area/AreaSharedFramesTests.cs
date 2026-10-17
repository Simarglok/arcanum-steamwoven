using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-024 on synthetic fixtures: a whole map decodes each distinct object frame
/// selection once per build and gives every object its own visual record; a townmap plan
/// keeps one admission and decode per visual.</summary>
[TestClass]
public sealed class AreaSharedFramesTests
{
    private const long Start = 67108865;

    [TestMethod]
    public void A_whole_map_decodes_one_frame_for_objects_with_the_same_selection()
    {
        var records = new[] { Record(Id(2, 11), location: 1), Record(Id(2, 12), location: 2), Record(Id(2, 13), location: 3) };
        int Requests(AreaSectorSelection selection, out AreaSnapshot snapshot)
        {
            using var f = new Fixture([(Start, records)], selection: selection, resourceEntries: selection == AreaSectorSelection.AllSectors
                ? [("tile.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()), ("facade.mes", "{0}{fixture}"u8.ToArray()),
                   ("scenery.mes", "{0}{fixture.art}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art())]
                : null);
            var report = f.Owner.Inspect();
            Assert.IsTrue(report.IsComplete, string.Join("; ", report.Issues));
            snapshot = report.Snapshot!;
            return checked((int)report.Usage.FrameRequests);
        }
        // One terrain selection plus one object selection, against one terrain plus three objects.
        Assert.AreEqual(2, Requests(AreaSectorSelection.AllSectors, out var whole));
        Assert.AreEqual(4, Requests(AreaSectorSelection.TownmapComponent, out var townmap));
        var bodies = whole.Visuals.Where(v => v.Key.Object is not null).ToArray();
        Assert.AreEqual(3, bodies.Length);
        Assert.AreEqual(3, bodies.Select(v => v.Key).Distinct().Count());
        Assert.AreEqual(1, bodies.Select(v => (v.RgbaSha256, v.Width, v.Height, v.HotX, v.HotY, v.PayloadSha256)).Distinct().Count());
        // The shared facts equal what a per-visual decode gives.
        var single = townmap.Visuals.First(v => v.Key.Object is not null);
        Assert.AreEqual((single.RgbaSha256, single.EncodedBytes, single.RgbaBytes, single.EffectivePalette), (bodies[0].RgbaSha256, bodies[0].EncodedBytes, bodies[0].RgbaBytes, bodies[0].EffectivePalette));
    }

    [TestMethod]
    public void Each_visual_keeps_its_own_object_facts_and_a_different_palette_is_decoded_apart()
    {
        // Prototype 7001: scenery 0x40000000. Prototype 7002: the same art with offsets and scale
        // of its own (one shared decode), then with another rotation or palette slot (decoded apart).
        foreach (var (aid, requests) in new[] { (0x40000000u, 2), (0x40000000u | (2u << 11), 3), (0x40000010u, 3) })
        {
            var records = new[] { Record(Id(2, 11), location: 1), Record(Id(2, 12), location: 2, parent: Id(1, 7002)) };
            using var f = new Fixture([(Start, records)], extraPrototype: Prototype7002(aid, ox: 7, oy: -3, scale: 50), selection: AreaSectorSelection.AllSectors, allowArtTrailingBytes: true,
                resourceEntries: WholeMapResources);
            var report = f.Owner.Inspect();
            Assert.IsTrue(report.IsComplete, string.Join("; ", report.Issues));
            Assert.AreEqual(requests, report.Usage.FrameRequests, aid.ToString("x8"));
            var plain = report.Snapshot!.Visuals.Single(v => v.Key.Object is not null && v.Scale == 100);
            var own = report.Snapshot.Visuals.Single(v => v.Key.Object is not null && v.Scale == 50);
            Assert.AreEqual((0, 0, 0x40000000u), (plain.ObjectOffsetX, plain.ObjectOffsetY, plain.RawArt));
            Assert.AreEqual((7, -3, 50, (int)((aid >> 11) & 7), aid), (own.ObjectOffsetX, own.ObjectOffsetY, own.Scale, own.NominalFacing, own.LookupArt));
            Assert.AreNotEqual(plain.Key, own.Key);
            // A second inspection is a new build: it decodes its frames again.
            var again = f.Owner.Inspect();
            Assert.AreEqual(2 * requests, again.Usage.FrameRequests, aid.ToString("x8"));
        }
    }

    private static readonly (string, byte[])[] WholeMapResources =
    [
        ("tile.mes", "{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}"u8.ToArray()), ("facade.mes", "{0}{fixture}"u8.ToArray()),
        ("scenery.mes", "{0}{fixture.art}"u8.ToArray()), ("art/tile/stonebse0a.art", Art()), ("art/scenery/fixture.art", Art()),
    ];

    /// <summary>A scenery prototype 7002 with its own art id, offsets (fields 2, 3) and scale (11).</summary>
    private static byte[] Prototype7002(uint aid, int ox, int oy, int scale)
    {
        using var s = new MemoryStream(); I32(s, 119); s.Write(Id(-1)); s.Write(Id(1, 7002)); I32(s, 3);
        I32(s, 0); I32(s, 0); I32(s, 0);
        int[] optional = [1, 5, 6, 7, 15, 16, 17, 30, 31, 34, 35, 37, 40, 41];
        for (var field = 0; field < 42; field++)
        {
            if (optional.Contains(field)) s.WriteByte(0);
            else I32(s, field switch { 0 => unchecked((int)aid), 2 => ox, 3 => oy, 11 => scale, _ => 0 });
        }
        return s.ToArray();
    }
}
