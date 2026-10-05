using System.Buffers.Binary;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed class AreaSectorBoundaryTests
{
    [TestMethod]
    public void All_five_literal_placeholder_branches_keep_numeric_lights_and_context_townmap_absence()
    {
        for (var branch = 0; branch <= 4; branch++)
        {
            var bytes = OptionalSector(branch);
            using var f = new Fixture([(67108865, [])], moduleBytes: Dat(("maps/test/67108865.sec", Sector()), ("maps/test/67108866.sec", bytes)));
            var snapshot = f.Owner.LoadStrict(); var context = snapshot.Sectors.Single(s => s.Key.Packed == 67108866);
            Assert.AreEqual(AreaScope.Context, context.Scope);
            Assert.AreEqual(0xAA0000 + branch, context.Placeholder);
            Assert.AreEqual(branch < 3 ? null : (int?)0, context.Townmap);
            Assert.AreEqual(1, context.LightCount);
            CollectionAssert.AreEqual(new[] { -1,0,1,2,3,4,5,6,7,8,9,10 }, context.Lights.Single().ToArray());
            Assert.AreEqual(1, context.RoofDiscriminator);
            Assert.AreEqual(4096, snapshot.Terrain.Count(t => t.Key.Sector.Packed == 67108866));
            Assert.AreEqual(bytes.Length, context.Sections.Max(s => s.Start + s.Length));
            Assert.IsFalse(snapshot.GameplayVerified);
        }
    }

    [TestMethod]
    public void Required_roof_presence_retains_diagnostic_sector_but_never_publishes_strict_readiness()
    {
        using var f = new Fixture([(67108865, [])], moduleBytes: Dat(("maps/test/67108865.sec", Sector()), ("maps/test/67108866.sec", OptionalSector(4, roof: true))));
        var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Assert.IsNull(f.Owner.PublishedSnapshot);
        Assert.AreEqual(0, report.Snapshot!.Sectors.Single(s => s.Key.Packed == 67108866).RoofDiscriminator);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.UnsupportedArtClassOrState && i.Stage == AreaStage.Sector));
        Throws<AreaIncompleteException>(() => f.Owner.LoadStrict());
    }

    [TestMethod]
    public void Malformed_sector_sections_have_sanitized_area_failures_not_escaping_terrain_exceptions()
    {
        var valid = OptionalSector(4);
        var badLight = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badLight, -1);
        var badRoof = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badRoof.AsSpan(16436), 2);
        var badPlaceholder = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badPlaceholder.AsSpan(16440), 0xAA0005);
        var badScripts = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badScripts.AsSpan(16444), int.MaxValue);
        var badTerminal = valid.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(badTerminal.AsSpan(valid.Length-4), 1);
        foreach (var malformed in new[] { badLight,badRoof,badPlaceholder,badScripts,badTerminal,valid[..100],valid[..^1] })
        {
            using var f = new Fixture([(67108865, [])], moduleBytes: Dat(("maps/test/67108865.sec", Sector()), ("maps/test/67108866.sec", malformed)));
            var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
            Assert.IsNull(f.Owner.PublishedSnapshot);
            Assert.IsTrue(report.Issues.Any(i => i.Stage == AreaStage.Sector), string.Join(',', report.Issues));
            Throws<AreaIncompleteException>(() => f.Owner.LoadStrict());
        }
    }

    [TestMethod]
    public void Diagonal_and_disconnected_same_townmap_sectors_do_not_join_cardinal_core()
    {
        using var f = new Fixture([(67108865, []), (134217730, []), (335544325, [])]);
        var s = f.Owner.LoadStrict();
        Assert.AreEqual(1, s.Reconciliation.CoreSectors);
        Assert.AreEqual(1, s.Reconciliation.ContextPresent);
        Assert.AreEqual(7, s.Reconciliation.ContextMissing);
        Assert.AreEqual(AreaScope.Context, s.Sectors.Single(row => row.Key.Packed == 134217730).Scope);
        Assert.IsFalse(s.Sectors.Any(row => row.Key.Packed == 335544325));
        using var absent = new Fixture([(67108866, [])]);
        var report = absent.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InvalidSelection));
    }

    private static byte[] OptionalSector(int branch, bool roof = false)
    {
        using var s = new MemoryStream(); I32(s,1);
        for (var i = -1; i < 11; i++) I32(s,i);
        s.Write(new byte[16384]); I32(s,roof ? 0 : 1); if (roof) s.Write(new byte[1024]); I32(s,0xAA0000+branch);
        if (branch > 0) { I32(s,1); for (var i=0; i<6; i++) I32(s,100+i); }
        if (branch >= 2) { I32(s,201); I32(s,202); I32(s,203); }
        if (branch >= 3) { I32(s,0); for (var i=0; i<5; i++) I32(s,300+i); }
        if (branch == 4) s.Write(new byte[512]); I32(s,0); return s.ToArray();
    }
}
