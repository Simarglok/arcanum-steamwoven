using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Scripting.NpcDialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.NpcDialog;

[TestClass]
public sealed class NpcDialogEntryOriginalTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("NpcDialogEntryRealData")]
    public void Actual_loaded_original_attachment_computes_project_diagnostic_entries_and_unknown_frontier()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARC_NPC_ORACLE"))) Assert.Inconclusive("Explicit original inputs and private independent oracle required.");
        using var npcOracle = JsonDocument.Parse(File.ReadAllBytes(Required("ARC_NPC_ORACLE")));
        var n = npcOracle.RootElement;
        Assert.IsTrue(n.GetProperty("projectDiagnostic").GetBoolean());
        var areaBytes = File.ReadAllBytes(Required("ARC_AREA_ORACLE"));
        Assert.AreEqual(n.GetProperty("areaOracleHash").GetString(), Hash(areaBytes));
        using var area = JsonDocument.Parse(areaBytes);
        var files = new List<(AreaInputKey Key, string Path)>
        {
            (new(AreaSourceRole.Module), Required("ARC_AREA_MODULE")),
            (new(AreaSourceRole.Resource1), Required("ARC_AREA_RESOURCE1")),
            (new(AreaSourceRole.Resource2), Required("ARC_AREA_RESOURCE2")),
            (new(AreaSourceRole.NormalGameMobile), Required("ARC_AREA_MOBILE")),
        };
        foreach (var prototype in area.RootElement.GetProperty("prototypes").EnumerateArray())
        {
            var number = prototype.GetProperty("own_number").GetInt32();
            var candidates = Directory.EnumerateFiles(Required("ARC_AREA_PROTOTYPES"), $"{number:D6} - *.pro").Take(2).ToArray();
            Assert.AreEqual(1, candidates.Length, "Exactly one prototype input per oracle role.");
            files.Add((new(AreaSourceRole.Prototype, number), candidates[0]));
        }
        var scriptPath = Required("ARC_NPC_SCRIPTS"); var dialogPath = Required("ARC_NPC_DIALOGS");
        var paths = files.Select(f => f.Path).Concat([scriptPath, dialogPath]).Distinct().ToArray();
        var before = paths.ToDictionary(p => p, Fingerprint);
        try
        {
            using var owner = AreaOriginalValidator.Open(new(files.Select(f => AreaInput.FromFile(f.Key, f.Path))));
            var snapshot = owner.LoadStrict();
            var rows = snapshot.Objects.Where(r => r.Key.ObjectIdSha256 == n.GetProperty("objectHash").GetString()).ToArray();
            Assert.AreEqual(1, rows.Length); var row = rows[0];
            Assert.AreEqual(n.GetProperty("prototype").GetInt32(), row.PrototypeNumber);
            Assert.AreEqual(n.GetProperty("offset").GetInt64(), row.Key.Offset);
            Assert.AreEqual(n.GetProperty("worldX").GetInt64(), row.WorldX);
            Assert.AreEqual(n.GetProperty("worldY").GetInt64(), row.WorldY);
            Span<byte> location = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(location, row.SerializedLocation!.Value);
            Assert.AreEqual(n.GetProperty("locationHash").GetString(), Hash(location));
            var world = new NpcDialogWorldId(new Guid("10000000-0000-0000-0000-000000000001"));
            var map = new NpcDialogMapId(snapshot.RuntimeMap, snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Module).Sha256);
            var npc = new NpcDialogNpcId(world, map, row.Key, row.RawSha256, row.SemanticSha256);
            var prototypeId = new NpcDialogPrototypeId(row.PrototypeNumber, row.PrototypeOwnSha256, snapshot.Inputs.Single(i => i.Key == new AreaInputKey(AreaSourceRole.Prototype, row.PrototypeNumber)).Sha256);
            var scriptId = new Guid("10000000-0000-0000-0000-000000000002"); var dialogId = new Guid("10000000-0000-0000-0000-000000000003");
            var bindings = snapshot.Inputs.Select(i => new NpcDialogSourceBinding(NpcDialogEntryResolver.AreaRole(i.Key), Guid.NewGuid(), i.Length, i.Sha256)).ToList();
            bindings.Add(new("Script", scriptId, before[scriptPath].Length, before[scriptPath].Hash));
            bindings.Add(new("Dialog", dialogId, before[dialogPath].Length, before[dialogPath].Hash));
            var version = new NpcDialogSourceVersion(Guid.NewGuid(), "explicit-selected-archives-v1", bindings);
            // All epochs/revisions and boolean answers below are PROJECT diagnostics,
            // not reconstructed original PC/companion/new-game runtime state.
            var invocation = new NpcDialogInvocation(world, map, npc, prototypeId, new(world, map, Guid.NewGuid()), NpcDialogEvent.Dialog, new(0), version, new(Guid.NewGuid(), 1), new(Guid.NewGuid(), 1));
            using var scripts = File.OpenRead(scriptPath); using var dialogs = File.OpenRead(dialogPath);
            var policy = new NpcDialogResourcePolicy("explicit-selected-archives-v1", version, scriptId, dialogId,
                [new(scriptId, scripts, before[scriptPath].Length, before[scriptPath].Hash), new(dialogId, dialogs, before[dialogPath].Length, before[dialogPath].Hash)]);
            var preparation = NpcDialogEntryResolver.Prepare(snapshot, invocation, policy, new());
            Assert.AreEqual(NpcDialogAdmission.Admitted, preparation.Admission, (preparation.Error?.Code ?? preparation.Frontier?.Code)?.ToString());
            var plan = preparation.Plan!;
            Assert.AreEqual(n.GetProperty("script").GetInt32(), plan.Attachment.ScriptId.Value);
            Assert.AreEqual(n.GetProperty("flags").GetUInt32(), plan.Attachment.Flags);
            Assert.AreEqual(n.GetProperty("counters").GetUInt32(), plan.Attachment.Counters);
            Assert.AreEqual(n.GetProperty("scriptHash").GetString(), plan.ScriptResource.PayloadSha256);
            Assert.AreEqual(n.GetProperty("dialogHash").GetString(), plan.DialogResource.PayloadSha256);
            Assert.AreEqual(n.GetProperty("scriptNameHash").GetString(), plan.ScriptResource.NameSha256);
            Assert.AreEqual(n.GetProperty("dialogNameHash").GetString(), plan.DialogResource.NameSha256);
            var q = new EntryQueries(); var falseResult = NpcDialogEntryResolver.Resolve(plan, q, new());
            Assert.AreEqual(NpcDialogResolutionKind.Resolved, falseResult.Kind);
            Assert.AreEqual(n.GetProperty("falseNode").GetInt32(), falseResult.Entry!.DlgNode.Value);
            CollectionAssert.AreEqual(Numbers(n, "falseLines"), falseResult.Trace.Select(t => t.Line.Value).ToArray());
            var localFlags = Numbers(n, "localFlags");
            Assert.IsTrue(localFlags.All(bit => (plan.Attachment.Flags & (1U << bit)) == 0));
            CollectionAssert.AreEqual(new[] { "V0", "F0", "V0", $"L{localFlags[0]}:1", "V1", "J2", "V2", $"L{localFlags[1]}:3", "V3", "V4" }, q.Calls);
            var unknown = new EntryQueries(); unknown.Answers.Enqueue(NpcDialogQueryStatus.Unknown);
            var negative = NpcDialogEntryResolver.Resolve(plan, unknown, new());
            Assert.AreEqual(NpcDialogCode.QueryUnknown, negative.Frontier!.Code);
            Assert.AreEqual(0, negative.Frontier.Line!.Value.Value); Assert.IsNull(negative.Entry);
            CollectionAssert.AreEqual(new[] { "V0", "F0", "V0" }, unknown.Calls);
            for (var predicate = 0; predicate < 4; predicate++)
            {
                var yes = new EntryQueries(); for (var i = 0; i < predicate; i++) yes.Answers.Enqueue(NpcDialogQueryStatus.KnownFalse); yes.Answers.Enqueue(NpcDialogQueryStatus.KnownTrue);
                var alternative = NpcDialogEntryResolver.Resolve(plan, yes, new()); Assert.AreEqual(NpcDialogResolutionKind.Resolved, alternative.Kind);
                Assert.AreEqual(Numbers(n, "trueLines")[predicate], alternative.Entry!.ScrLine.Value);
                Assert.AreEqual(Numbers(n, "trueNodes")[predicate], alternative.Entry.DlgNode.Value);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { ProjectDiagnostic = true, FalseLines = falseResult.Trace.Select(t => t.Line.Value), Node = falseResult.Entry.DlgNode.Value, Unknown = negative.Frontier.Code.ToString(), TrueLines = Numbers(n, "trueLines"), TrueNodes = Numbers(n, "trueNodes"), OriginalGameplayVerified = false }));
        }
        finally
        {
            foreach (var path in paths) Assert.AreEqual(before[path], Fingerprint(path), "Full original input fingerprint changed.");
            Console.WriteLine(JsonSerializer.Serialize(new { IntegrityUnchanged = true, OriginalInputCount = paths.Length, Fingerprints = before.Values.Select((f, index) => new { InputIndex = index, f.Length, f.Hash }) }));
        }
    }
    private static int[] Numbers(JsonElement e, string property) => e.GetProperty(property).EnumerateArray().Select(x => x.GetInt32()).ToArray();
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Explicit opt-in input missing.");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static (long Length, string Hash) Fingerprint(string path)
    {
        using var file = File.OpenRead(path); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[262144]; int count;
        while ((count = file.Read(buffer)) != 0) hash.AppendData(buffer, 0, count);
        return (file.Length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}
