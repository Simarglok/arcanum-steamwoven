using System.Buffers.Binary;
using Arcanum.Data.StaticObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.StaticObjects;

[TestClass]
public sealed class StaticObjectLoaderTests
{
    [TestMethod]
    public void Inspect_preserves_direct_fields_uses_slot_18_for_static_predicate_and_separates_resource_coverage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"static-object-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\object.sec", Sector(Record(0x00000004, 0x00002000), 1)))), leaveOpen: true);
            using var resource = DatArchive.Open(new MemoryStream(Dat(
                ("art\\scenery\\scenery.mes", System.Text.Encoding.ASCII.GetBytes("{0}{fixture.art}")),
                ("art\\scenery\\fixture.art", Art()))), leaveOpen: true);

            var report = StaticObjectLoader.Inspect(new StaticObjectSource(module, "maps\\fixture\\object.sec", resource, "fixture-resource"),
                new StaticObjectPrototypeSource(root, "fixture-prototypes"));

            Assert.AreEqual(StaticObjectCensusStatus.Complete, report.CensusStatus);
            Assert.AreEqual(1, report.TerminalRecordCount);
            Assert.AreEqual(1, report.Records.Count);
            Assert.AreEqual(1, report.FieldResolvedCount);
            Assert.AreEqual(1, report.ResourceResolvedCount);
            Assert.AreEqual(0, report.ResourceNotEvaluatedCount);
            var record = report.Records[0];
            Assert.AreEqual(0, record.SourceIndex);
            Assert.AreEqual(3, record.ObjectType);
            Assert.AreEqual(0x40000000, record.DirectCurrentArt);
            Assert.AreEqual(0x00000004u, record.DirectFlags);
            Assert.AreEqual(7L, record.Location);
            Assert.AreEqual(-3, record.DirectOffsetX);
            Assert.AreEqual(11, record.DirectOffsetY);
            Assert.IsTrue(record.IsStatic);
            Assert.IsFalse(record.CurrentArtInherited);
            Assert.IsFalse(record.FlagsInherited);
            Assert.AreEqual(StaticObjectResourceStatus.Resolved, record.ResourceStatus);
            Assert.IsNotNull(record.ArtMetadata);
            Assert.AreEqual(1, record.ArtMetadata.Width);
            Assert.AreEqual(2, resource.Entries.Count, "The caller still owns and can use the resource archive.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Inspect_rejects_terminal_count_that_cannot_be_delimited_without_fabricating_records()
    {
        var root = Path.Combine(Path.GetTempPath(), $"static-object-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var module = DatArchive.Open(new MemoryStream(Dat(("maps\\fixture\\truncated.sec", Sector(Record(0, 0), 2)))), leaveOpen: true);
            using var resource = DatArchive.Open(new MemoryStream(Dat()), leaveOpen: true);
            StaticObjectException? exception = null;
            try
            {
                _ = StaticObjectLoader.Inspect(new StaticObjectSource(module, "maps\\fixture\\truncated.sec", resource, "fixture-resource"),
                    new StaticObjectPrototypeSource(root, "fixture-prototypes"));
            }
            catch (StaticObjectException caught)
            {
                exception = caught;
            }
            Assert.IsNotNull(exception);
            Assert.AreEqual(StaticObjectErrorCode.InvalidRecord, exception.Code);
            StringAssert.Contains(exception.Message, "Object record 1");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("RealData")]
    public void RealData_opt_in_inspection_reconciles_the_bounded_census_and_selected_resource_scope()
    {
        var modulePath = Environment.GetEnvironmentVariable("ARCANUM_STATIC_MODULE");
        var resourcePath = Environment.GetEnvironmentVariable("ARCANUM_STATIC_RESOURCE");
        var prototypeRoot = Environment.GetEnvironmentVariable("ARCANUM_STATIC_PROTOTYPES");
        if (string.IsNullOrWhiteSpace(modulePath) || string.IsNullOrWhiteSpace(resourcePath) || string.IsNullOrWhiteSpace(prototypeRoot))
            Assert.Fail("ARCANUM_STATIC_MODULE, ARCANUM_STATIC_RESOURCE, and ARCANUM_STATIC_PROTOTYPES are required for this opt-in test.");
        var validation = StaticObjectLoader.ValidateOriginalData(modulePath!, "maps\\Arcanum1-024-fixed\\86570436012.sec", prototypeRoot!, resourcePath!,
            "selected-resource", "selected-prototypes");
        Assert.AreEqual(355938305L, validation.Module.Length);
        Assert.AreEqual("8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f", validation.Module.Sha256);
        Assert.AreEqual(147991122L, validation.Resource.Length);
        Assert.AreEqual("4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d", validation.Resource.Sha256);
        Assert.AreEqual("00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6", validation.PrototypeAggregateSha256);
        var report = validation.Inspection;

        Assert.AreEqual(317, report.TerminalRecordCount);
        Assert.AreEqual(317, report.Records.Count);
        Assert.AreEqual(317, report.FieldResolvedCount);
        Assert.AreEqual(288, report.Records.Count(record => !record.CurrentArtInherited));
        Assert.AreEqual(29, report.Records.Count(record => record.CurrentArtInherited));
        Assert.AreEqual(31, report.Records.Count(record => !record.FlagsInherited));
        Assert.AreEqual(286, report.Records.Count(record => record.FlagsInherited));
        Assert.AreEqual(317, report.Records.Count(record => record.IsStatic == true));
        Assert.AreEqual(5, report.ResourceResolvedCount);
        Assert.AreEqual(312, report.ResourceNotEvaluatedCount);
    }

    private static byte[] Record(uint flags, uint spellFlags)
    {
        var bytes = new List<byte>();
        I32(bytes, 119); ObjectId(bytes, 1, 7001); ObjectId(bytes, 1, 1); I32(bytes, 3); I16(bytes, 6);
        U32(bytes, (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 18) | (1u << 19)); U32(bytes, 0); U32(bytes, 0);
        I32(bytes, unchecked((int)0x40000000u)); bytes.Add(1); I64(bytes, 7); I32(bytes, -3); I32(bytes, 11); U32(bytes, flags); U32(bytes, spellFlags);
        return bytes.ToArray();
    }

    private static byte[] Sector(byte[] record, int terminalCount)
    {
        var bytes = new byte[4 + (4096 * 4) + 4 + 4 + record.Length + 4];
        var offset = 4 + (4096 * 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), 0xAA0000);
        record.CopyTo(bytes, offset + 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), terminalCount);
        return bytes;
    }

    private static byte[] Art()
    {
        var bytes = new byte[132 + 1024 + 28 + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 1);
        var frame = 132 + 1024; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 8), 1); bytes[^1] = 1;
        return bytes;
    }

    private static byte[] Dat(params (string Name, byte[] Payload)[] entries)
    {
        using var output = new MemoryStream(); var offsets = new List<uint>();
        foreach (var entry in entries) { offsets.Add(checked((uint)output.Position)); output.Write(entry.Payload); }
        using var index = new MemoryStream(); U32(index, checked((uint)entries.Length));
        for (var i = 0; i < entries.Length; i++)
        {
            var name = System.Text.Encoding.ASCII.GetBytes(entries[i].Name + "\0"); U32(index, checked((uint)name.Length)); index.Write(name); index.Write(new byte[4]); U32(index, 1); U32(index, checked((uint)entries[i].Payload.Length)); U32(index, checked((uint)entries[i].Payload.Length)); U32(index, offsets[i]);
        }
        index.Position = 0; index.CopyTo(output); output.Write(new byte[16]); output.Write("1TAD"u8); U32(output, 0); U32(output, checked((uint)(index.Length + 28))); return output.ToArray();
    }

    private static void ObjectId(List<byte> bytes, short kind, int aid) { I16(bytes, kind); bytes.AddRange(new byte[6]); var payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, aid); bytes.AddRange(payload); }
    private static void I16(List<byte> bytes, short value) { var raw = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void I32(List<byte> bytes, int value) { var raw = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void I64(List<byte> bytes, long value) { var raw = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void U32(List<byte> bytes, uint value) { var raw = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(raw, value); bytes.AddRange(raw); }
    private static void U32(Stream stream, uint value) { Span<byte> raw = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(raw, value); stream.Write(raw); }
}
