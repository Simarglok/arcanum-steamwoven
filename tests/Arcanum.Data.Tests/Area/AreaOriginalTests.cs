using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed partial class AreaOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void Complete_original_owner_matches_independent_full_area_oracle_and_repeats()
    {
        var oraclePath = Environment.GetEnvironmentVariable("ARC_AREA_ORACLE");
        if (string.IsNullOrEmpty(oraclePath)) Assert.Inconclusive("Opt-in original inputs and independent numeric oracle are required.");
        var oracleBytes = File.ReadAllBytes(oraclePath);
        Assert.AreEqual("bfe9770d75a1c1922ca8817fd0c239e589c3de48020022f7de6bd450ef2d2b60", Digest(oracleBytes));
        using var oracle = JsonDocument.Parse(oracleBytes);
        var inputs = new List<AreaInput>();
        inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Module), Required("ARC_AREA_MODULE")));
        inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Resource1), Required("ARC_AREA_RESOURCE1")));
        inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Resource2), Required("ARC_AREA_RESOURCE2")));
        inputs.Add(AreaInput.FromFile(new(AreaSourceRole.NormalGameMobile), Required("ARC_AREA_MOBILE")));
        var directory = Required("ARC_AREA_PROTOTYPES");
        foreach (var prototype in oracle.RootElement.GetProperty("prototypes").EnumerateArray())
        {
            var number = prototype.GetProperty("own_number").GetInt32();
            var candidates = Directory.EnumerateFiles(directory, $"{number:D6} - *.pro").Take(2).ToArray();
            Assert.AreEqual(1, candidates.Length, "Exactly one original prototype path is required.");
            inputs.Add(AreaInput.FromFile(new(AreaSourceRole.Prototype, number), candidates[0]));
        }
        var measurements = new List<object>();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        using var owner = AreaOriginalValidator.Open(new(inputs));
        Observe("cold-open");
        var first = owner.Inspect();
        Observe("cold-complete-load");
        Console.WriteLine(JsonSerializer.Serialize(new { first.Usage, first.Issues }));
        Assert.IsTrue(first.IsComplete, JsonSerializer.Serialize(first.Issues));
        Check(first.Snapshot!, oracle.RootElement);
        var firstJson = JsonSerializer.Serialize(first.Snapshot);
        BeginObservation();
        var second = owner.LoadStrict();
        Observe("warm-repeat-load");
        Check(second, oracle.RootElement);
        Assert.AreEqual(firstJson, JsonSerializer.Serialize(second), "Repeated complete numeric snapshots must be identical.");
        BeginObservation();
        foreach (var index in new[] { 1201, 5699 })
        {
            var visual = second.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Mobile && v.Key.SourceIndex == index);
            using var lease = owner.AcquireSelectedFrame(second, visual.Key);
            var pixels = lease.CopyRgba();
            Assert.AreEqual(visual.RgbaSha256, Digest(pixels));
        }
        Console.WriteLine(JsonSerializer.Serialize(owner.Usage));
        Observe("selected-frame-copies");
        var retained = owner.Usage.RetainedReserved;
        BeginObservation();
        owner.Dispose();
        Assert.IsTrue(owner.Usage.RetainedReserved < retained);
        foreach (var input in inputs)
        {
            var stream = (Stream)typeof(AreaInput).GetProperty("Stream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(input)!;
            Assert.IsFalse(stream.CanRead, "Every transferred original-data stream is closed after owner disposal.");
        }
        Observe("owner-disposed-metadata-retained");
        Console.WriteLine(JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(), Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), Measurements = measurements, owner.PhaseHistory }));
        Assert.AreEqual(firstJson, JsonSerializer.Serialize(first.Snapshot));

        void Observe(string phase)
        {
            var now = GC.GetTotalAllocatedBytes(precise: true);
            measurements.Add(new { Phase = phase, AllocatedBytes = now - allocated, ElapsedMilliseconds = watch.ElapsedMilliseconds,
                LiveManagedBytes = GC.GetTotalMemory(forceFullCollection: true), owner.Usage });
            // Oracle checks/serialization happen outside each observed interval.
            // This is sampled process allocation/GC evidence, not a worst-case bound.
            BeginObservation();
        }
        void BeginObservation() { allocated = GC.GetTotalAllocatedBytes(precise: true); watch.Restart(); }
    }
    private static void Check(AreaSnapshot snapshot, JsonElement oracle)
    {
        Assert.AreEqual(new AreaReconciliation(621, 9, 11, 5, 36864, 45056, 426, 7399, 88, 10, 7301, 0, 7399, 56, 2444), snapshot.Reconciliation);
        CheckOccurrences(snapshot, oracle);
        var requests = oracle.GetProperty("requests").EnumerateArray().ToArray();
        Assert.AreEqual(2444, requests.Length);
        foreach (var actual in snapshot.Visuals)
        {
            var key = actual.Key.Kind switch
            {
                AreaSourceKind.Mobile => $"mobile:{actual.Key.SourceIndex}",
                AreaSourceKind.Static => $"static:{actual.Key.Sector.Packed}:{actual.Key.SourceIndex}",
                _ => $"terrain:({actual.Entry.Ordinal}, {actual.PhysicalGroup}, {actual.Frame}, {actual.RequestedPalette})"
            };
            var expected = requests.Single(r => r.GetProperty("key").GetString() == key);
            var entry = expected.GetProperty("entry"); var art = expected.GetProperty("oracle");
            Assert.AreEqual(expected.GetProperty("source").GetString(), actual.Entry.Source.Role == AreaSourceRole.Resource1 ? "resource1" : "resource2", key);
            Assert.AreEqual(entry.GetProperty("ordinal").GetInt32(), actual.Entry.Ordinal, key);
            Assert.AreEqual(entry.GetProperty("offset").GetUInt32(), actual.Entry.Offset, key);
            Assert.AreEqual(entry.GetProperty("stored").GetUInt32(), actual.Entry.StoredBytes, key);
            Assert.AreEqual(entry.GetProperty("decoded").GetUInt32(), actual.Entry.DecodedBytes, key);
            Assert.AreEqual(entry.GetProperty("name_sha256").GetString(), actual.Entry.NameSha256, key);
            Assert.AreEqual(expected.GetProperty("full_sha256").GetString(), actual.PayloadSha256, key);
            Assert.AreEqual(art.GetProperty("width").GetInt32(), actual.Width, key);
            Assert.AreEqual(art.GetProperty("height").GetInt32(), actual.Height, key);
            Assert.AreEqual(art.GetProperty("hot_x").GetInt32(), actual.HotX, key);
            Assert.AreEqual(art.GetProperty("hot_y").GetInt32(), actual.HotY, key);
            Assert.AreEqual(art.GetProperty("offset_x").GetInt32(), actual.ArtOffsetX, key);
            Assert.AreEqual(art.GetProperty("offset_y").GetInt32(), actual.ArtOffsetY, key);
            Assert.AreEqual(art.GetProperty("group").GetInt32(), actual.PhysicalGroup, key);
            Assert.AreEqual(art.GetProperty("frame").GetInt32(), actual.Frame, key);
            Assert.AreEqual(art.GetProperty("requested_palette").GetInt32(), actual.RequestedPalette, key);
            Assert.AreEqual(art.GetProperty("effective_palette").GetInt32(), actual.EffectivePalette, key);
            Assert.AreEqual(art.GetProperty("encoded_bytes").GetInt32(), actual.EncodedBytes, key);
            Assert.AreEqual(art.GetProperty("rgba_sha256").GetString(), actual.RgbaSha256, key);
        }
        Assert.AreEqual(15, snapshot.Objects.Count(o => o.Scope == AreaScope.Core && o.Key.Kind == AreaSourceKind.Mobile && o.Disposition == AreaDisposition.ExteriorSectorInsertionRefusedByInventory));
        Assert.AreEqual(10, snapshot.Objects.Count(o => o.Disposition == AreaDisposition.ContextOnly));
        Assert.IsFalse(snapshot.RendererExecutionVerified); Assert.IsFalse(snapshot.GameplayVerified);
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Explicit opt-in input is missing.");
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
