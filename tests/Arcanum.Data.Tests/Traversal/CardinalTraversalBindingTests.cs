using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Traversal.CardinalTraversalFixture;
using static Arcanum.Data.Tests.Traversal.CardinalTraversalInspectorTests;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class CardinalTraversalBindingTests
{
    [TestMethod]
    public void Independently_encoded_v1_evidence_and_context_digests_include_presence_and_direct_flags_not_epoch()
    {
        using var f = new CardinalTraversalFixture([Record(0), Record(null)]); f.PutPrototype(7001, 1024);
        var s = f.Inspect();
        Assert.AreEqual((uint)(4 + 16384 + 4 + 4 + 52 + 12 + 24 + 512 + 91 + 87 + 4), s.InputIdentity.Sector.DecodedSize);
        Assert.AreEqual((uint)Encoding.ASCII.GetByteCount("{0}{aaa}{300}{aaa}"), s.InputIdentity.SelectedNameEntryDecodedSize);
        Assert.AreEqual(ExpectedStructuralDigest(), s.InputIdentity.SelectedEvidenceSha256);
        var a = s.Assess(Request(s)); Assert.AreEqual(ExpectedContextDigest(), a.StepEvidence.ContextFingerprint);
        var newer = f.Inspect(epoch: 8); Assert.AreEqual(s.InputIdentity.SelectedEvidenceSha256, newer.InputIdentity.SelectedEvidenceSha256);
        Result(s.Assess(Request(s, identity: newer.InputIdentity)), "Supported", "Blocked", true);
        Result(s.Assess(Request(s, epoch: 8)), "Unsupported", null, false);
        using var different = new CardinalTraversalFixture([Record(null), Record(null)]); different.PutPrototype(7001, 0);
        Assert.AreNotEqual(s.InputIdentity.SelectedEvidenceSha256, different.Inspect().InputIdentity.SelectedEvidenceSha256);
    }
    private static string ExpectedStructuralDigest()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII);
        // Literal ratified V1 grammar, no production encoder or implementation enum values.
        Text(w, "CARDINAL_EVIDENCE_V1"); w.Write((byte)0); Text(w, "MODULE"); Text(w, "RESOURCE"); Text(w, "PROTOTYPES");
        w.Write(1452); w.Write(1290); w.Write(86570436012L); w.Write(92928); w.Write(82560); w.Write(64); w.Write(64); w.Write(0); w.Write(1u);
        // Literal byte grammar sizes (v119 header70, art4, location9, flags4/0, slot19=4).
        w.Write((uint)(4 + 16384 + 4 + 4 + 52 + 12 + 24 + 512 + 91 + 87 + 4));
        w.Write((byte)0); w.Write((byte)1); w.Write(0); w.Write(1u); w.Write((uint)Encoding.ASCII.GetByteCount("{0}{aaa}{300}{aaa}")); w.Write((byte)0);
        w.Write(4096u); for (var i = 0; i < 4096; i++) w.Write(0x180u);
        w.Write((byte)1); w.Write(128u); for (var i = 0; i < 128; i++) w.Write(0u);
        w.Write(4096u);
        for (var i = 0; i < 4096; i++)
        { Present(w, 3); Present(w, 0); Present(w, 0); Present(w, 0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)1); w.Write((byte)0); Text(w, "TILE_NAME_RULE_NO_BLOCK"); Text(w, "RESOURCE"); Present(w, 0); }
        w.Write(2); w.Write(2u);
        for (var i = 0; i < 2; i++)
        { w.Write(i); w.Write(3); Present(w, 7001); w.Write((byte)1); w.Write(World()); Present(w, 92933); Present(w, 82561); Present(w, 5); Present(w, 1); w.Write((byte)(i == 0 ? 1 : 0)); if (i == 0) w.Write(0u); w.Write((byte)1); w.Write(i == 0 ? 0u : 1024u); w.Write((byte)(i == 0 ? 1 : 2)); w.Write((byte)0); }
        w.Write(2); w.Write(12); return Hash(stream.ToArray());
    }
    private static string ExpectedContextDigest()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.ASCII);
        Text(w, "CARDINAL_CONTEXT_V1"); Text(w, "ISOLATED_PC_CRITTER_ART_NONPOLYMORPH"); w.Write(15); w.Write(2); w.Write(0u); w.Write(0u); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write(0);
        return Hash(stream.ToArray());
    }
    private static void Text(BinaryWriter w, string s) { var b = Encoding.ASCII.GetBytes(s); w.Write((uint)b.Length); w.Write(b); }
    private static void Present(BinaryWriter w, int n) { w.Write((byte)1); w.Write(n); }

    [TestMethod]
    public void Production_builder_rejects_incomplete_duplicate_subset_and_disagreeing_raw_census_projections()
    {
        using var f = new CardinalTraversalFixture([Record()]);
        var real = StaticObjectLoader.InspectForObstruction(f.Source, f.Prototypes, new StaticObjectLoaderOptions());
        var ordinary = StaticObstructionInspector.Create(real, 0, "MODULE", "PROTOTYPES", null);
        var terrain = TerrainObstructionInspector.InspectSector(f.Module, "MODULE", f.Selector, new(f.Resource, "RESOURCE", NamePath, TerrainPathProfile.NonWindowsMacOs));
        var row = real.TraversalRecords[0];
        foreach (var inspection in new[] {
            new StaticObjectInspection(StaticObjectCensusStatus.Complete, 1, real.Records, []),
            new StaticObjectInspection(StaticObjectCensusStatus.Complete, 2, real.Records, [row, row]),
            new StaticObjectInspection(StaticObjectCensusStatus.Complete, 0, real.Records, [row]),
            new StaticObjectInspection(StaticObjectCensusStatus.Complete, 1, real.Records, [new(0, 3, 7001, World(), 0, 0, false)]) })
        {
            var s = CardinalTraversalInspector.Build(CardinalTraversalInputKind.Structural, "MODULE", "RESOURCE", "PROTOTYPES", f.Selector, f.Module.Entries[0], terrain, inspection, ordinary, null, 7);
            Result(s.Assess(Request(s)), "Unknown", null, false);
        }
    }

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("module")]
    [DataRow("resource")]
    [DataRow("4026")]
    [DataRow("4028")]
    [DataRow("4030")]
    [DataRow("4031")]
    [DataRow("4038")]
    [DataRow("4048")]
    public void Production_final_fingerprints_follow_snapshot_reads_and_reject_each_late_mutation(string changedInput)
    {
        // Actual DAT/SEC/MES/v119/default readers + owned Build, not a toy projection.
        // Structural fixtures never authenticate originals; only the INTERNAL final guard is shared.
        var aids = new[] { 4026, 4028, 4030, 4031, 4038, 4048 };
        using var f = new CardinalTraversalFixture(aids.Select(aid => Record(null, aid: aid)).ToArray());
        var prototypes = new List<(string Path, StaticObjectInputFingerprint Fingerprint)>();
        foreach (var aid in aids)
        {
            var bytes = StaticObjects.StaticObjectLoaderTests.Prototype(aid, 123, 1024);
            f.PutPrototype(aid, 1024, bytes);
            prototypes.Add((Path.Combine(f.PrototypeRoot, $"{aid:000000} - fixture.pro"), new(bytes.LongLength, Hash(bytes))));
        }
        // Independent BCL oracle over test-authored bytes, not the production Fingerprint method.
        var moduleBefore = new StaticObjectInputFingerprint(f.ModuleBytes.LongLength, Hash(f.ModuleBytes));
        var resourceBefore = new StaticObjectInputFingerprint(f.ResourceBytes.LongLength, Hash(f.ResourceBytes));
        var events = new List<string>(); var projected = false;
        f.ModuleStream.ReadObserved = () => events.Add(projected ? "post-module-read" : "projection-module-read");
        f.ResourceStream.ReadObserved = () => events.Add(projected ? "post-resource-read" : "projection-resource-read");
        CardinalTraversalSnapshot? returned = null;
        CardinalTraversalSnapshot Construct() => CardinalTraversalInspector.Sanitize(() => StaticObjectLoader.CompleteGuardedProjection(() =>
        {
            var snapshot = f.Inspect();
            Assert.AreEqual("Structural", snapshot.InputIdentity.Kind.ToString());
            Assert.IsNull(snapshot.InputIdentity.OriginalIdentity);
            Assert.AreEqual(6, snapshot.Census.Count);
            CollectionAssert.AreEqual(aids, snapshot.Census.Select(row => row.PrototypeAid!.Value).ToArray());
            Assert.IsTrue(snapshot.Census.All(row => row.FlagsSource.ToString() == "Prototype"));
            Result(snapshot.Assess(Request(snapshot)), "Supported", "Open", true);
            Assert.IsTrue(events.Contains("projection-module-read"));
            Assert.IsTrue(events.Contains("projection-resource-read"));
            events.Add("last-projection-read-complete");
            // Mutate only synthetic inputs AFTER the final factory read/Build, using no OS sharing assumption.
            if (changedInput == "module") f.ModuleBytes[0] ^= 1;
            else if (changedInput == "resource") f.ResourceBytes[0] ^= 1;
            else if (int.TryParse(changedInput, out var aid))
            {
                var path = Path.Combine(f.PrototypeRoot, $"{aid:000000} - fixture.pro");
                var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes);
            }
            projected = true;
            return snapshot;
        }, f.ModuleStream, f.ResourceStream, moduleBefore, resourceBefore, prototypes, new StaticObjectLoaderOptions()));

        if (changedInput == "unchanged")
        {
            returned = Construct();
            Result(returned.Assess(Request(returned)), "Supported", "Open", true);
        }
        else
        {
            var exception = Throws<CardinalTraversalInvocationException>(() => returned = Construct());
            Assert.IsNull(returned, "A changed input must not return the already-built snapshot.");
            Assert.AreEqual("InputChanged", exception.Code.ToString());
            Assert.AreEqual("Fingerprint", exception.Stage.ToString());
            Assert.AreEqual("CARDINAL_INVOCATION_FAILED", exception.Message);
            Assert.IsNull(exception.InnerException); Assert.IsNull(exception.SourceIndex); Assert.IsNull(exception.Offset);
        }
        Assert.IsTrue(events.IndexOf("post-module-read") > events.IndexOf("last-projection-read-complete"));
        // The verifier intentionally short-circuits once module mismatch is found.
        if (changedInput != "module") Assert.IsTrue(events.IndexOf("post-resource-read") > events.LastIndexOf("post-module-read"));
        Assert.AreEqual(changedInput == "module", moduleBefore.Sha256 != Hash(f.ModuleBytes));
        Assert.AreEqual(changedInput == "resource", resourceBefore.Sha256 != Hash(f.ResourceBytes));
        for (var i = 0; i < prototypes.Count; i++)
            Assert.AreEqual(changedInput == aids[i].ToString(System.Globalization.CultureInfo.InvariantCulture),
                prototypes[i].Fingerprint.Sha256 != Hash(File.ReadAllBytes(prototypes[i].Path)));
    }
}
