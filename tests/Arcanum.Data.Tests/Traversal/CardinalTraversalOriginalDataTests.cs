using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Traversal.CardinalTraversalInspectorTests;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class CardinalTraversalOriginalDataTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly (int Aid, long Length, string Sha)[] Expected =
    [
        (4026,414,"1378ddb05e981098eca7816b22a366e6733c9df7c580ccc3f19345f782319874"),
        (4028,414,"c2442deca958715bc49f5f0504066f951a7d549e5787be41dcded2257e23d6e3"),
        (4030,406,"f15e7b0ed999ac9eeabcafa1bcddbd55b96dcbba9b2d71a7d485024c06f9c310"),
        (4031,414,"3c5ad2f204a010b3908d0a02e693128e9f8ff421b4a7dafb7ee3e78b66420c53"),
        (4038,414,"2531e1a1c6ddca2f483a2d557bebcfd6c1b0b89bd2d996a3f559adc28924214b"),
        (4048,378,"ec807b77ea283d8d84387993a439953b7acf1a1ef7bdfb30b6a86a334dcfe8d1")
    ];
    [TestMethod]
    [TestCategory("RealData")]
    public void Authenticated_production_four_directions_full_census_and_independent_before_after_identities()
    {
        var module = Required("ARCANUM_STATIC_MODULE"); var resource = Required("ARCANUM_STATIC_RESOURCE"); var root = Required("ARCANUM_STATIC_PROTOTYPES");
        var paths = Expected.Select(e => Directory.EnumerateFiles(root).Single(p => Path.GetFileName(p).StartsWith($"{e.Aid:000000} -", StringComparison.Ordinal))).ToArray();
        var moduleBefore = Fingerprint(module); var resourceBefore = Fingerprint(resource); var prototypesBefore = paths.Select(Fingerprint).ToArray();
        Assert.AreEqual((355938305L, "8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f"), moduleBefore);
        Assert.AreEqual((147991122L, "4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d"), resourceBefore);
        for (var i = 0; i < Expected.Length; i++) Assert.AreEqual((Expected[i].Length, Expected[i].Sha), prototypesBefore[i]);
        var s = CardinalTraversalInspector.ValidateOriginalData(module, resource, root, 7);
        Assert.AreEqual(CardinalTraversalInputKind.AuthenticatedOriginal, s.InputIdentity.Kind); var original = s.InputIdentity.OriginalIdentity!; Assert.IsNotNull(original);
        Assert.AreEqual(moduleBefore, (original.Module.Length, original.Module.Sha256)); Assert.AreEqual(resourceBefore, (original.Resource.Length, original.Resource.Sha256));
        Assert.AreEqual(6, original.Prototypes.Count);
        for (var i = 0; i < Expected.Length; i++) { Assert.AreEqual(Expected[i].Aid, original.Prototypes[i].Aid); Assert.AreEqual((Expected[i].Length, Expected[i].Sha), (original.Prototypes[i].Fingerprint.Length, original.Prototypes[i].Fingerprint.Sha256)); }
        Assert.AreEqual("00d5610fc295b84a8dd355d843b0307d0dff5f73e6d353ed25c81667882d65d6", original.PrototypeAggregateSha256);
        var historical = ShaText(string.Join("|", Expected.Select(e => $"{e.Aid:000000}:{e.Length}:{e.Sha}")));
        Assert.AreEqual("684e04f305cef5e0c89caabee852728b33bfb85d4a7e6b2f509505815c080342", historical);
        Assert.AreEqual(463, s.InputIdentity.Sector.EntryOrdinal); Assert.AreEqual(2u, s.InputIdentity.Sector.Flags); Assert.AreEqual(47291u, s.InputIdentity.Sector.DecodedSize);
        Assert.AreEqual(5353, s.InputIdentity.SelectedNameEntryOrdinal); Assert.AreEqual(317, s.TerminalRecordCount); Assert.AreEqual(317, s.Census.Count);
        Assert.AreEqual(317, s.Census.Count(r => r.ObjectType == 3 && r.LocationStatus == CardinalTraversalLocationStatus.InRange));
        Assert.AreEqual(31, s.Census.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Direct)); Assert.AreEqual(286, s.Census.Count(r => r.FlagsSource == StaticObstructionFlagsSource.Prototype));
        var groups = s.Census.GroupBy(r => r.LocalCell).ToArray(); Assert.AreEqual(293, groups.Length); Assert.AreEqual(22, groups.Count(g => g.Count() > 1)); Assert.AreEqual(4, groups.Max(g => g.Count()));
        Assert.AreEqual(8, s.TileScriptCount); Assert.AreEqual(12, s.SectorScriptBytes);
        foreach (var (fromX, toX, dir, disposition, fromOrdinal, ordinal, raw, indices) in new[]
        {
            (6,5,1,"Blocked",70,69,70592L,new[]{1}), (5,6,5,"Open",69,70,79809L,new[]{2,3}),
            (6,7,5,"Open",70,71,111553L,Array.Empty<int>()), (7,6,1,"Open",71,70,79809L,new[]{2,3})
        })
        {
            var a = s.Assess(Request(s, new(fromX, 1), new(toX, 1), (CardinalTraversalDirection)dir)); Result(a, "Supported", disposition, true);
            Assert.AreEqual(0, a.Gaps.Count); Assert.AreEqual(317, a.RecordAssessments.Count);
            Assert.AreEqual(92928 + fromX, a.From.WorldX); Assert.AreEqual(92928 + toX, a.To.WorldX); Assert.AreEqual(82561, a.From.WorldY); Assert.AreEqual(82561, a.To.WorldY); Assert.AreEqual(fromOrdinal, a.From.Ordinal); Assert.AreEqual(ordinal, a.To.Ordinal);
            var mask = a.Components.Single(c => c.ComponentKind == CardinalTraversalComponentKind.DestinationMask).Inputs.ToDictionary(i => i.Code, i => i.Value);
            Assert.AreEqual(raw, mask["RAW_TERRAIN"]); Assert.AreEqual(0L, mask["PACKED_CLASS"]); Assert.AreEqual(2L, mask["MASK_WORD_INDEX"]); Assert.AreEqual((long)(toX), mask["MASK_BIT"]); Assert.AreEqual(0L, mask["MASK_VALUE"]);
            var name = a.Components.Single(c => c.ComponentKind == CardinalTraversalComponentKind.DestinationName);
            var n = name.Inputs.ToDictionary(i => i.Code, i => i.Value); Assert.AreEqual(3L, n["CATEGORY_BITS"]); Assert.AreEqual(0L, n["NAME_CATEGORY_INDEX"]); Assert.AreEqual(0L, n["MATERIAL_ORDINAL"]); Assert.AreEqual(0L, n["MES_KEY"]);
            Assert.AreEqual(5353, name.Provenance.Single().EntryOrdinal); Assert.AreEqual(CardinalTraversalProvenanceKind.SelectedTileName, name.Provenance.Single().Kind);
            var at = a.RecordAssessments.Where(r => r.LocalCell == new CardinalTraversalCell(toX, 1)).ToArray(); CollectionAssert.AreEqual(indices, at.Select(r => r.SourceIndex).ToArray());
            foreach (var r in at)
            {
                Assert.AreEqual(toX == 5 ? 4196352u : 4197424u, r.EffectiveFlags); Assert.AreEqual(toX == 5 ? 4027 : 4030, r.PrototypeAid);
                Assert.AreEqual(toX == 5 ? StaticObstructionFlagsSource.Direct : StaticObstructionFlagsSource.Prototype, r.FlagsSource);
                Assert.IsTrue(r.Result.Provenance.Any(p => p.SourceIndex == r.SourceIndex && p.EntryOrdinal == 463));
                if (toX == 6) Assert.IsTrue(r.Result.Provenance.Any(p => p.Kind == CardinalTraversalProvenanceKind.PrototypeDefault && p.PrototypeAid == 4030));
            }
            TestContext.WriteLine($"production row from={fromX},1 to={toX},1 direction={dir} coverage={a.Coverage} disposition={a.Disposition} gaps=0 all_records=317 indices={string.Join(',',indices)} digest={s.InputIdentity.SelectedEvidenceSha256} epoch={s.EvidenceEpoch}");
        }
        Result(s.Assess(Request(s, new(4, 0), new(4, -1), CardinalTraversalDirection.YMinus)), "Unsupported", null, false);
        Assert.AreEqual(moduleBefore, Fingerprint(module)); Assert.AreEqual(resourceBefore, Fingerprint(resource));
        for (var i = 0; i < paths.Length; i++) { Assert.AreEqual(prototypesBefore[i], Fingerprint(paths[i])); TestContext.WriteLine($"before_after aid={Expected[i].Aid} length={Expected[i].Length} sha256={Expected[i].Sha} equality=PASS"); }
        TestContext.WriteLine($"before_after module_length={moduleBefore.Length} module_sha256={moduleBefore.Sha} resource_length={resourceBefore.Length} resource_sha256={resourceBefore.Sha} equality=PASS loader_ID_SHA={original.PrototypeAggregateSha256} historical_ID_length_SHA={historical}");
    }
    private static string Required(string name) { var value = Environment.GetEnvironmentVariable(name); if (string.IsNullOrWhiteSpace(value)) Assert.Fail(name + " is required (missing inputs fail, not skip)."); return value!; }
    private static (long Length, string Sha) Fingerprint(string path) { using var f = File.OpenRead(path); return (f.Length, Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant()); }
    private static string ShaText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();
}
