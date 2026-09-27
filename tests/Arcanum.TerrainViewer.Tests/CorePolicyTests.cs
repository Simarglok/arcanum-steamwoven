using Arcanum.TerrainViewer.Core;
using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class CorePolicyTests
{
    [TestMethod]
    public void Arguments_require_explicit_non_path_source_id_and_normalize_hashes()
    {
        var result = TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--expect-module-sha256", new string('A', 64)]);
        Assert.IsTrue(result.SourceId == "fixture");
        Assert.IsTrue(result.ExpectedModuleSha256 == new string('a', 64));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "a/b"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--unknown"]));
    }

    [TestMethod]
    public void Camera_fit_manual_pan_zoom_and_resize_preserve_declared_contract_at_both_sizes()
    {
        foreach (var viewport in new[] { (1280d, 800d), (1920d, 1080d) })
        {
            var state = CameraPolicy.Fit(-100, 0, 200, 100, viewport.Item1, viewport.Item2);
            Assert.IsTrue(state.FitMode);
            Assert.IsTrue(state.Zoom >= CameraPolicy.MinimumZoom && state.Zoom <= CameraPolicy.MaximumZoom);
            for (var cycle = 0; cycle < 100; cycle++)
            {
                state = CameraPolicy.Pan(state, 600, -300);
                Assert.IsFalse(state.FitMode);
                state = CameraPolicy.Zoom(state, 1.25);
                state = CameraPolicy.Zoom(state, 0.8);
                state = CameraPolicy.Fit(-100, 0, 200, 100, viewport.Item1, viewport.Item2);
            }
            Assert.IsTrue(state.FitMode);
            Assert.AreEqual(Math.Floor(state.Zoom * 1024d) / 1024d, state.Zoom);
        }
    }

    [TestMethod]
    public void Cache_policy_rejects_over_count_and_over_byte_before_retaining_frame()
    {
        var bytes = TerrainCachePolicy.CheckedRgbaBytes(1024, 1024);
        Assert.AreEqual(4L * 1024 * 1024, bytes);
        var entries = Enumerable.Range(0, TerrainCachePolicy.MaxDistinctKeys + 1).ToDictionary(i => new TerrainTextureKey("s", i, 0, 1, "a", 0, 0, 0), _ => (1, 1));
        AssertThrows<TerrainViewerException>(() => TerrainCachePolicy.Validate(entries));
        AssertThrows<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(TerrainCachePolicy.MaxRgbaBytes - 4, 0, 2, 1));
        AssertThrows<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(0, TerrainCachePolicy.MaxDistinctKeys, 1, 1));
        AssertThrows<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(0, 0, 0, 1));
    }

    [TestMethod]
    public void Literal_plan_preserves_precomputed_crop_flip_and_order_without_geometry_recalculation()
    {
        var key = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var command = new TerrainDrawCommand(7, key, new TerrainRect(2, 3, 4, 5), new TerrainRect(81, 1240, 4, 5), true,
            new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 1, 30, 32));
        var plan = TerrainScenePlan.CreateLiteral(new TerrainRect(-10, 0, 100, 50), [command]);
        Assert.AreEqual(1, plan.Commands.Count);
        Assert.AreEqual(command, plan.Commands[0]);
        Assert.AreEqual(81, plan.Commands[0].Destination.X);
        Assert.IsTrue(plan.Commands[0].FlipX);
    }

    [TestMethod]
    public async Task Coordinator_serializes_concurrent_invalid_loads_and_disposes_safely()
    {
        using var coordinator = new TerrainLoadCoordinator();
        var arguments = TerrainViewerArguments.Parse(["--module", "missing-module", "--resource", "missing-resource", "--source-id", "fixture"]);
        var first = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        var second = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await AssertThrowsAsync<TerrainViewerException>(first);
        await AssertThrowsAsync<TerrainViewerException>(second);
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency);
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); Assert.Fail($"Expected {typeof(T).Name}."); }
        catch (T) { }
    }

    private static async Task AssertThrowsAsync<T>(Task task) where T : Exception
    {
        try { await task; Assert.Fail($"Expected {typeof(T).Name}."); }
        catch (T) { }
    }
}