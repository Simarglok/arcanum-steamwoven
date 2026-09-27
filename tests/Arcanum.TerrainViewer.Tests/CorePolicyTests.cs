using Arcanum.TerrainViewer.Core;
using Arcanum.Data.Terrain;
using Arcanum.Data;
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
    public void Synthetic_plan_uses_production_placement_crop_flip_and_stable_order()
    {
        var key = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var plan = TerrainScenePlan.CreateSynthetic(new TerrainRect(-100, 0, 300, 100),
        [
            new TerrainPlanInput(9, key, TerrainKind.Facade, 1, 1, 0xB00000C1, 80, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, -1, -1)),
            new TerrainPlanInput(7, key, TerrainKind.Tile, 0, 1, 0xC1, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 1, 99, 99)),
            new TerrainPlanInput(8, key, TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 1, 99, 99)),
        ]);
        CollectionAssert.AreEqual(new[] { 8, 7, 9 }, plan.Commands.Select(command => command.Ordinal).ToArray());
        var flip = plan.Commands.Single(command => command.Ordinal == 7);
        var facade = plan.Commands.Single(command => command.Ordinal == 9);
        Assert.AreEqual(new TerrainRect(41, 20, 78, 40), flip.Destination);
        Assert.IsTrue(flip.FlipX);
        Assert.AreEqual(new TerrainRect(1, 40, 80, 40), facade.Destination);
        Assert.IsFalse(facade.FlipX);
        Assert.AreEqual(1, facade.Order.Layer, "Facade layer is derived from kind, not caller input.");

        var cropped = TerrainScenePlan.CreateSynthetic(new TerrainRect(2, 1, 10, 10),
        [new TerrainPlanInput(10, key, TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        Assert.AreEqual(new TerrainRect(1, 1, 10, 10), cropped.Commands[0].Source);
        Assert.AreEqual(new TerrainRect(2, 1, 10, 10), cropped.Commands[0].Destination);
        AssertThrows<TerrainViewerException>(() => TerrainScenePlan.CreateSynthetic(new TerrainRect(200, 200, 1, 1),
        [new TerrainPlanInput(11, key, TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]));
    }

    [TestMethod]
    public void Malformed_art_and_invalid_frame_are_rejected_before_publication()
    {
        AssertThrows<ArtFormatException>(() => ArtDecoder.Decode(new byte[1], new ArtFrameSelection(0, 0, 0)));
        AssertThrows<ArgumentOutOfRangeException>(() => ArtDecoder.Decode(new byte[132 + (256 * 4)], new ArtFrameSelection(-1, 0, 0)));
    }

    [TestMethod]
    public async Task Coordinator_serializes_barrier_backed_archive_decode_work_and_disposes_operation()
    {
        var operation = new BarrierArchiveDecodeOperation();
        var coordinator = new TerrainLoadCoordinator(operation);
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        var first = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await operation.Entered.Task;
        var second = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await Task.Delay(20);
        Assert.AreEqual(1, operation.ExecuteCount, "Second request must remain outside the archive/decode operation.");
        operation.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency);
        Assert.AreEqual(1, operation.MaximumActive);
        coordinator.Dispose();
        Assert.AreEqual(1, operation.DisposeCount, "Coordinator owns operation/archive cleanup exactly once.");
        coordinator.Dispose();
        Assert.AreEqual(1, operation.DisposeCount);
    }

    [TestMethod]
    public void Scene_plan_sorts_y_x_then_ordinal_after_derived_layer()
    {
        var key = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var plan = TerrainScenePlan.CreateSynthetic(new TerrainRect(-100, 0, 300, 100),
        [
            new TerrainPlanInput(4, key, TerrainKind.Tile, 0, 1, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 99, 99, 99)),
            new TerrainPlanInput(3, key, TerrainKind.Tile, 1, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 99, 99, 99)),
            new TerrainPlanInput(2, key, TerrainKind.Tile, 0, 1, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 99, 99, 99)),
            new TerrainPlanInput(1, key, TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 99, 99, 99)),
        ]);
        CollectionAssert.AreEqual(new[] { 1, 3, 2, 4 }, plan.Commands.Select(command => command.Ordinal).ToArray());
    }

    [TestMethod]
    public void Generation_gate_prevents_stale_publication_and_allows_current_generation_once()
    {
        using var coordinator = new TerrainLoadCoordinator();
        var stale = coordinator.BeginGeneration();
        var current = coordinator.BeginGeneration();
        var publications = 0;
        Assert.IsFalse(coordinator.TryPublish(stale, () => publications++));
        Assert.IsTrue(coordinator.TryPublish(current, () => publications++));
        Assert.AreEqual(1, publications);
    }

    [TestMethod]
    public async Task Coordinator_propagates_safe_operation_failure_and_disposes_failure_path()
    {
        var operation = new ThrowingArchiveOperation();
        using (var coordinator = new TerrainLoadCoordinator(operation))
        {
            var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
            await AssertThrowsAsync<TerrainViewerException>(coordinator.LoadAsync(coordinator.BeginGeneration(), arguments));
        }
        Assert.AreEqual(1, operation.DisposeCount);
    }

    private sealed class BarrierArchiveDecodeOperation : ITerrainLoadOperation, IDisposable
    {
        private int _active;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecuteCount { get; private set; }
        public int MaximumActive { get; private set; }
        public int DisposeCount { get; private set; }

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments)
        {
            ExecuteCount++;
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref _active));
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            Interlocked.Decrement(ref _active);
            return Result(generation);
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingArchiveOperation : ITerrainLoadOperation, IDisposable
    {
        public int DisposeCount { get; private set; }
        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments)
            => throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable));
        public void Dispose() => DisposeCount++;
    }

    private static TerrainLoadResult Result(long generation)
    {
        var key = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var plan = TerrainScenePlan.CreateSynthetic(new TerrainRect(0, 0, 100, 60),
            [new TerrainPlanInput(0, key, TerrainKind.Tile, 0, 0, 0, 78, 40, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        var fingerprint = new InputFingerprint(1, "fixture");
        return new TerrainLoadResult(generation, plan, new Dictionary<TerrainTextureKey, DecodedTerrainTexture>(), 1, 1, 0, 0, 0,
            fingerprint, fingerprint, fingerprint, fingerprint, 0, 0, 1);
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