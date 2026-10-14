using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaOriginalCoordinatorTests
{
    private static TerrainViewerArguments OriginalArguments()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1")
            Assert.Inconclusive("Opt-in authenticated AREA coordinator gate; not a synthetic PASS.");
        string Required(string role)
        {
            var value = Environment.GetEnvironmentVariable(role);
            if (string.IsNullOrWhiteSpace(value)) Assert.Fail("Requested original AREA gate is missing an input role: " + role);
            return value!;
        }
        return TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Required("ARC_AREA_MODULE"),
            "--resource", Required("ARC_AREA_RESOURCE2"), "--actor-resource", Required("ARC_AREA_RESOURCE1"),
            "--area-mobile", Required("ARC_AREA_MOBILE"), "--prototypes", Required("ARC_AREA_PROTOTYPES"),
            "--source-id", "ORIGINAL_AREA_COORDINATOR"]);
    }

    [TestMethod, TestCategory("RealData")]
    public async Task CompleteOriginalLoadsBindFreshAreaHumanAndTraversalToSerializedCurrentGeneration()
    {
        var arguments = OriginalArguments();
        // Default factory and complete production Execute: no synthetic envelope,
        // structural admission, substitute owner, actor or traversal operation.
        // Do not block the caller's synchronous permit-acquisition prefix with a
        // test barrier. The actual original source work runs on the worker pool.
        using var coordinator = new TerrainLoadCoordinator();
        var old = coordinator.BeginGeneration();
        var staleLoad = coordinator.LoadAsync(old, arguments);
        var current = coordinator.BeginGeneration();
        var currentLoad = coordinator.LoadAsync(current, arguments);
        var stale = await staleLoad.WaitAsync(TimeSpan.FromMinutes(8));
        var result = await currentLoad.WaitAsync(TimeSpan.FromMinutes(8));
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency);
        AssertComplete(stale, old); AssertComplete(result, current);
        Assert.AreNotSame(stale.Area!.Snapshot, result.Area!.Snapshot);
        Assert.AreNotSame(stale.TraversalSnapshot, result.TraversalSnapshot);
        Assert.AreNotSame(stale.ActorPlan, result.ActorPlan);
        var firstKey = result.Area.Frames.Keys.First();
        Assert.AreNotSame(stale.Area.Frames[firstKey].Rgba, result.Area.Frames[firstKey].Rgba);
        Assert.IsFalse(coordinator.TryPublish(old, () => Assert.Fail("Stale original AREA publication")));
        TerrainLoadResult? published = null;
        Assert.IsTrue(coordinator.TryPublish(current, () => published = result));
        Assert.AreSame(result, published);

        var snapshot = result.TraversalSnapshot!;
        var module = result.Area.Snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Module);
        var resource = result.Area.Snapshot.Inputs.Single(i => i.Key.Role == AreaSourceRole.Resource2);
        AreaLoadOperation.VerifyTraversal(current, module, resource, snapshot);
        // Genuine authenticated snapshot, but wrong generation/source/role must
        // fail at the same production binding boundary, not produce permission.
        Refused(() => AreaLoadOperation.VerifyTraversal(old, module, resource, snapshot));
        Refused(() => AreaLoadOperation.VerifyTraversal(current, module with { Length = module.Length - 1 }, resource, snapshot));
        Refused(() => AreaLoadOperation.VerifyTraversal(current, module with { Sha256 = new string('0', 64) }, resource, snapshot));
        Refused(() => AreaLoadOperation.VerifyTraversal(current, module, resource with { Sha256 = new string('0', 64) }, snapshot));
        Refused(() => AreaLoadOperation.VerifyTraversal(current, module, resource with { Key = new(AreaSourceRole.Resource1) }, snapshot));
        Assert.AreEqual("COMPLETE_NONBLOCKING_EVIDENCE", Assess(snapshot, 6, 1, 7, 1).ReasonCode);
        var blocked = Assess(snapshot, 6, 1, 5, 1);
        Assert.AreEqual(CardinalTraversalCoverage.Supported, blocked.Coverage);
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, blocked.Disposition);
        Assert.AreEqual("ADMITTED_DESTINATION_SCENERY_BLOCKER", blocked.ReasonCode);
        var outside = Assess(snapshot, 8, 1, 9, 1);
        Assert.AreEqual(CardinalTraversalCoverage.Unsupported, outside.Coverage);
        Assert.IsNull(outside.Disposition); Assert.AreEqual("ZONE_UNSUPPORTED", outside.ReasonCode);
        Console.WriteLine("AREA original coordinator PASS: two complete serialized loads; fresh owner payloads; current/stale generation; authenticated chart/traversal/HumanFixed; five binding refusals; Open/Blocked/outside-zone denial. Not an AREA input/UI or resource matrix.");
    }

    private static CardinalTraversalAssessment Assess(CardinalTraversalSnapshot snapshot, int x, int y, int tx, int ty)
        => snapshot.Assess(new(snapshot.ProfileId, snapshot.InputIdentity, snapshot.EvidenceEpoch, 1452, 1290,
            new(x, y), new(tx, ty), tx > x ? CardinalTraversalDirection.XPlus : CardinalTraversalDirection.XMinus, snapshot.DeclaredContext));

    private static void Refused(Action action)
    {
        var error = Assert.ThrowsExactly<AreaException>(action);
        Assert.AreEqual(AreaErrorCode.InputIdentityMismatch, error.Issue.Code);
        Assert.AreEqual(AreaStage.Publication, error.Issue.Stage);
    }

    private static void AssertComplete(TerrainLoadResult result, long generation)
    {
        Assert.AreEqual(generation, result.Generation);
        Assert.IsNotNull(result.Area); Assert.AreEqual(generation, result.Area.Generation);
        var area = result.Area; var snapshot = area.Snapshot;
        Assert.AreEqual("arc-data-009-complete-file-effective-source-v1", snapshot.Profile);
        Assert.AreEqual(9, snapshot.Reconciliation.CoreSectors);
        Assert.AreEqual(36864, snapshot.Reconciliation.CoreTerrain);
        Assert.AreEqual(426, snapshot.Reconciliation.Statics); Assert.AreEqual(88, snapshot.Reconciliation.CoreMobiles);
        Assert.AreEqual(2444, snapshot.Visuals.Count); Assert.AreEqual(524, snapshot.Objects.Count);
        Assert.AreEqual(37363, area.Plan.Commands.Count); Assert.AreEqual(499, area.Plan.DrawableBodies);
        Assert.AreEqual(37363, area.Plan.Commands.Select(c => c.Visual).Distinct().Count());
        Assert.AreEqual(1975, area.Frames.Count); Assert.AreEqual(25914936L, area.CpuRgbaBytes);
        CollectionAssert.AreEquivalent(area.Plan.Commands.Select(c => c.Frame).Distinct().ToArray(), area.Frames.Keys.ToArray());
        Assert.AreEqual(15, area.Plan.Coverage.Count(row => row.Disposition == Arcanum.TerrainViewer.Core.AreaDisposition.Suppressed));
        CollectionAssert.AreEquivalent(new long[] { 86503327147, 86503327148, 86503327149,
            86570436011, 86570436012, 86570436013, 86637544875, 86637544876, 86637544877 },
            snapshot.Sectors.Where(s => s.Scope == AreaScope.Core).Select(s => s.Key.Packed).ToArray());
        Assert.AreEqual(result.ModuleFingerprint, result.ModuleFingerprintAfter);
        Assert.AreEqual(result.ResourceFingerprint, result.ResourceFingerprintAfter);
        Assert.AreEqual(result.ActorResourceFingerprint, result.ActorResourceFingerprintAfter);
        Assert.IsNotNull(result.TraversalSnapshot);
        var traversal = result.TraversalSnapshot;
        Assert.AreEqual(generation, traversal.EvidenceEpoch);
        Assert.AreEqual(CardinalTraversalInputKind.AuthenticatedOriginal, traversal.InputIdentity.Kind);
        Assert.AreEqual(CardinalTraversalAdmissionPolicy.BoundedAdjacent, traversal.AdmissionPolicy);
        Assert.AreEqual("BASE_SECTOR_1452_1290_ADJACENT_V1", traversal.ProfileId);
        Assert.AreEqual(317, traversal.TerminalRecordCount);
        var identity = traversal.InputIdentity.OriginalIdentity!;
        Assert.AreEqual((result.ModuleFingerprint.Size, result.ModuleFingerprint.Sha256), (identity.Module.Length, identity.Module.Sha256));
        Assert.AreEqual((result.ResourceFingerprint.Size, result.ResourceFingerprint.Sha256), (identity.Resource.Length, identity.Resource.Sha256));
        Assert.AreEqual((1452, 1290, 92928, 82560), (traversal.InputIdentity.Sector.SectorX,
            traversal.InputIdentity.Sector.SectorY, traversal.InputIdentity.Sector.OriginX, traversal.InputIdentity.Sector.OriginY));
        Assert.IsNotNull(result.ActorPlan);
        Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed, result.ActorPlan.Profile.Id);
        Assert.AreEqual(55, result.ActorTextures.Count);
        Assert.AreEqual(457676L, result.ActorTextures.Values.Sum(t => (long)t.Rgba.Length));
        Assert.AreEqual(26372612L, result.CheckedRgbaBytes);
        Assert.AreEqual((-200L, 160L), (result.ActorPlan.Current.AnchorX, result.ActorPlan.Current.AnchorY));
        Assert.AreEqual((ActorDiagnosticAction.Idle, 5, 0), (result.ActorPlan.Current.Action, result.ActorPlan.Current.RequestedFacing, result.ActorPlan.Current.Frame));
        Assert.AreEqual(55, result.ActorPlan.Catalog.Count);
        Console.WriteLine($"AREA original full-load generation={generation} terrain=36864 bodies=499 frames=1975 bytes=25914936 actor=55/457676 traversalEpoch={traversal.EvidenceEpoch} maximumConcurrency={result.MaximumObservedConcurrency}");
    }
}
