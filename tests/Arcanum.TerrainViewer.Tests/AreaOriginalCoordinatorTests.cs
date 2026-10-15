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
            "--area-effects", Required("ARC_AREA_EFFECTS"), "--area-patch", Required("ARC_AREA_PATCH"),
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
        Assert.AreNotSame(stale.AreaStepSnapshot, result.AreaStepSnapshot);
        Assert.AreNotSame(stale.ActorPlan, result.ActorPlan);
        var firstKey = result.Area.Frames.Keys.First();
        Assert.AreNotSame(stale.Area.Frames[firstKey].Rgba, result.Area.Frames[firstKey].Rgba);
        Assert.IsFalse(coordinator.TryPublish(old, () => Assert.Fail("Stale original AREA publication")));
        TerrainLoadResult? published = null;
        Assert.IsTrue(coordinator.TryPublish(current, () => published = result));
        Assert.AreSame(result, published);

        var steps = result.AreaStepSnapshot!;
        AreaLoadOperation.VerifyAreaSteps(current, result.Area.Snapshot, steps);
        // Genuine authenticated evidence, but a wrong generation or another area must
        // fail at the same production binding boundary, not produce permission.
        Refused(() => AreaLoadOperation.VerifyAreaSteps(old, result.Area.Snapshot, steps));
        Refused(() => AreaLoadOperation.VerifyAreaSteps(current, result.Area.Snapshot, stale.AreaStepSnapshot!));
        Assert.AreEqual("COMPLETE_NONBLOCKING_EVIDENCE", Assess(steps, 6, 1, 7, 1).ReasonCode);
        var blocked = Assess(steps, 6, 1, 5, 1);
        Assert.AreEqual(CardinalTraversalCoverage.Supported, blocked.Coverage);
        Assert.AreEqual(CardinalTraversalDisposition.Blocked, blocked.Disposition);
        Assert.AreEqual("DESTINATION_OBJECT_BLOCKER", blocked.ReasonCode);
        // Former bounded-zone edge (8,1)->(9,1) is now an ordinary whole-core step.
        Assert.AreEqual(CardinalTraversalCoverage.Supported, Assess(steps, 8, 1, 9, 1).Coverage);
        var outside = steps.Assess(steps.Request(new(92928 - 65, 82561), new(92928 - 66, 82561), CardinalTraversalDirection.XMinus));
        Assert.AreEqual(CardinalTraversalCoverage.Unsupported, outside.Coverage);
        Assert.AreEqual("SOURCE_OUTSIDE_ADMITTED_AREA", outside.ReasonCode);
        Console.WriteLine("AREA original coordinator PASS: two complete serialized loads; fresh owner payloads; current/stale generation; authenticated chart/traversal/HumanFixed; two binding refusals; Open/Blocked/outside-core denial on whole-core T0 steps. Not an AREA input/UI or resource matrix.");
    }

    private static AreaStepAssessment Assess(AreaStepSnapshot snapshot, int x, int y, int tx, int ty)
        => snapshot.Assess(snapshot.Request(AreaGridSession.Cell(new(x, y)), AreaGridSession.Cell(new(tx, ty)),
            tx > x ? CardinalTraversalDirection.XPlus : CardinalTraversalDirection.XMinus));

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
        Assert.IsNull(result.TraversalSnapshot, "The bounded cardinal snapshot is WORLD-only.");
        Assert.IsNotNull(result.AreaStepSnapshot);
        var traversal = result.AreaStepSnapshot;
        Assert.AreEqual(generation, traversal.EvidenceEpoch);
        Assert.AreEqual(AreaStepInputKind.AuthenticatedOriginal, traversal.InputIdentity.Kind);
        Assert.AreEqual("CRASH_SITE_CORE_T0_V1", traversal.ProfileId);
        Assert.AreEqual((36864, 0, 0), (traversal.AdmittedCells, traversal.TerrainUnknownCells, traversal.UnknownObjects));
        Assert.IsTrue(traversal.InputIdentity.Sources.Any(s => s.Length == result.ModuleFingerprint.Size && s.Sha256 == result.ModuleFingerprint.Sha256));
        Assert.IsTrue(traversal.InputIdentity.Sources.Any(s => s.Length == result.ResourceFingerprint.Size && s.Sha256 == result.ResourceFingerprint.Sha256));
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
