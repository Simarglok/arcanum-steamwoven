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
        Assert.AreEqual(TerrainViewerMode.TerrainOnly, result.Mode);
        Assert.IsTrue(result.SourceId == "fixture");
        Assert.IsTrue(result.ExpectedModuleSha256 == new string('a', 64));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "a/b"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--unknown"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--prototypes", "p"]));
        var combined = TerrainViewerArguments.Parse(["--mode", "combined", "--module", "m", "--resource", "r", "--source-id", "fixture", "--prototypes", "p"]);
        Assert.AreEqual(TerrainViewerMode.CombinedStaticDiagnostic, combined.Mode);
        Assert.AreEqual("p", combined.PrototypePath);
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "combined", "--module", "m", "--resource", "r", "--source-id", "fixture"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "terrain-only", "--mode", "combined", "--module", "m", "--resource", "r", "--source-id", "fixture", "--prototypes", "p"]));
        var actor = TerrainViewerArguments.Parse(["--mode", "actor-diagnostic", "--module", "m", "--resource", "r", "--actor-resource", "actor-r", "--prototypes", "p", "--source-id", "fixture",
            "--actor-map-x", "-2", "--actor-map-y", "3", "--actor-camera-origin-x", "100", "--actor-camera-origin-y", "-50", "--actor-offset-x", "7", "--actor-offset-y", "-9",
            "--actor-render-branch", "normal-uncached", "--actor-scale-percent", "100", "--actor-shrunk", "false", "--actor-action", "walk", "--actor-facing", "2", "--actor-frame", "1", "--expect-actor-resource-sha256", new string('B', 64)]);
        Assert.AreEqual(TerrainViewerMode.ActorDiagnostic, actor.Mode);
        Assert.AreEqual(ActorDiagnosticAction.Walk, actor.ActorInput!.Action);
        Assert.IsTrue(actor.ActorResourcePath == "actor-r", "Actor resource must retain the caller-selected role.");
        Assert.IsTrue(actor.ExpectedActorResourceSha256 == new string('b', 64), "Actor hash must normalize to lowercase.");
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "actor-diagnostic", "--module", "m", "--resource", "r", "--prototypes", "p", "--source-id", "fixture", "--actor-map-x", "0"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "actor-diagnostic", "--module", "m", "--resource", "r", "--actor-resource", "a", "--actor-resource", "b", "--prototypes", "p", "--source-id", "fixture", "--actor-map-x", "0"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--actor-resource", "a"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "combined", "--module", "m", "--resource", "r", "--prototypes", "p", "--source-id", "fixture", "--expect-actor-resource-sha256", new string('a', 64)]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "combined", "--module", "m", "--resource", "r", "--prototypes", "p", "--source-id", "fixture", "--actor-frame", "0"]));
    }

    [TestMethod]
    public void Actor_geometry_maps_every_facing_mirrors_hotspot_and_does_not_translate_by_frame_offset()
    {
        var expected = new[] { (0, false), (7, true), (6, true), (5, true), (4, false), (5, false), (6, false), (7, false) };
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, 8).Select(facing => (ActorDiagnosticScenePlan.MapFacing(facing).Group, ActorDiagnosticScenePlan.MapFacing(facing).FlipX)).ToArray());
        AssertViewerFailure(() => ActorDiagnosticScenePlan.MapFacing(8), TerrainViewerErrorCode.ActorFacing, TerrainViewerFailureStage.ActorFacing);
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4754, 0xc9001850, 7, 1, 1), 7, 5, 2, 3, 11, -4, 8, 15);
        var baseInput = new ActorDiagnosticPlacementInput(2, 6, 100, 50, -21, 8, ActorDiagnosticRenderBranch.NormalUncached, 100, false, ActorDiagnosticAction.Walk, 1, 1, 7, 5, 2, 3, 11, -4);
        var command = ActorDiagnosticScenePlan.Place(baseInput, frame);
        Assert.AreEqual(239L, command.AnchorX);
        Assert.AreEqual(238L, command.AnchorY);
        Assert.AreEqual(new TerrainRect(235, 235, 7, 5), command.Destination, "Mirrored effective hotspot is 7 - 2 - 1 = 4.");
        Assert.AreEqual(-11, command.EffectiveOffsetX);
        Assert.AreEqual(-4, command.EffectiveOffsetY);
        Assert.AreEqual(11, command.SourceOffsetX);
        Assert.AreEqual(new TerrainRect(235, 235, 7, 5), ActorDiagnosticScenePlan.Place(baseInput with { ArtOffsetX = -999, ArtOffsetY = 999 }, frame).Destination,
            "ART offsets remain metadata and are never a second placement translation.");
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(baseInput with { ScalePercent = 99 }, frame), TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(baseInput with { RenderBranch = ActorDiagnosticRenderBranch.CachedUnsupported }, frame), TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(baseInput with { MapX = long.MinValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(baseInput with { ArtOffsetX = int.MinValue }, frame with { ArtOffsetX = int.MinValue }), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
    }

    [TestMethod]
    public void Actor_public_planning_seam_has_literal_facing_geometry_guides_and_safe_failures()
    {
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4754, 0xc9001850, 0, 0, 1), 7, 5, 2, 3, 11, -4, 1, 15);
        var input = new ActorDiagnosticPlacementInput(2, 6, 100, 50, -21, 8, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Walk, 0, 0, 7, 5, 2, 3, 11, -4);
        // Literal independent outcomes: anchor=(239,238); mirrored hotspot=4,
        // ordinary hotspot=2. No expectation is calculated by production code.
        var expected = new[] { (0, false, 237), (7, true, 235), (6, true, 235), (5, true, 235),
            (4, false, 237), (5, false, 237), (6, false, 237), (7, false, 237) };
        foreach (var (facing, group, mirrored, left) in expected.Select((item, facing) => (facing, item.Item1, item.Item2, item.Item3)))
        {
            var command = ActorDiagnosticScenePlan.Place(input with { Facing = facing }, frame with { TextureKey = frame.TextureKey with { PhysicalGroup = group } });
            Assert.AreEqual(group, command.PhysicalGroup);
            Assert.AreEqual(mirrored, command.FlipX);
            Assert.AreEqual(new TerrainRect(left, 235, 7, 5), command.Destination);
            Assert.AreEqual(239L, command.AnchorX);
            Assert.AreEqual(238L, command.AnchorY);
        }

        var plan = ActorDiagnosticScenePlan.CreateSynthetic(input, [frame]);
        var bounds = plan.AdmittedBounds.Single();
        Assert.AreEqual(235, bounds.X, "Mirrored sprite is leftmost.");
        Assert.AreEqual(234, bounds.Y, "Anchor cross extends one pixel above this sprite.");
        Assert.AreEqual(9, bounds.Width, "Cross expands right edge through anchor + 4.");
        Assert.AreEqual(9, bounds.Height, "Cross expands bottom edge through anchor + 4.");
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapX = null }, frame), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Action = (ActorDiagnosticAction)99 }, frame), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Facing = 8 }, frame), TerrainViewerErrorCode.ActorFacing, TerrainViewerFailureStage.ActorFacing);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Frame = 1 }, frame), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input, frame with { TextureKey = frame.TextureKey with { Palette = 0 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
    }

    [TestMethod]
    public void Actor_public_planning_seam_rejects_projection_anchor_extent_and_draw_domain_overflow()
    {
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4753, 0xc9001810, 0, 0, 1), 2, 2, 0, 0, 0, 0, 1, 8);
        var valid = new ActorDiagnosticPlacementInput(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 0, 0, 2, 2, 0, 0, 0, 0);
        var overflowInputs = new[]
        {
            valid with { MapX = long.MinValue }, valid with { MapY = long.MaxValue },
            valid with { CameraOriginX = long.MaxValue }, valid with { CameraOriginY = long.MaxValue },
            valid with { InstanceOffsetX = int.MaxValue, CameraOriginX = long.MaxValue - 39 },
            valid with { InstanceOffsetY = int.MaxValue, CameraOriginY = long.MaxValue - 19 }
        };
        foreach (var input in overflowInputs)
            AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(valid with { CameraOriginX = (long)int.MaxValue + 1 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(valid with { Width = int.MaxValue, CameraOriginX = 1 }, frame with { Width = int.MaxValue, Height = 2 }), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
    }

    [TestMethod]
    public void Static_scenery_geometry_uses_checked_projection_hotspot_and_not_art_offset()
    {
        var identity = new Arcanum.Data.StaticObjects.StaticObjectResourceIdentity(7, 3, 99, new string('a', 64));
        var command = StaticSceneryGeometry.Place(new StaticSceneryPlanInput(2, 6, 1, -21, 8, identity, 0, 0, 0, 62, 57, 34, 39, 0, 0, false));
        Assert.AreEqual(-221, command.AnchorX);
        Assert.AreEqual(148, command.AnchorY);
        Assert.AreEqual(new TerrainRect(-255, 109, 62, 57), command.Destination);

        var noOffsetTranslation = StaticSceneryGeometry.Place(new StaticSceneryPlanInput(9, 0, 0, 100, 50, identity, 0, 0, 0, 4, 3, 1, 2, 9, -4, true));
        Assert.AreEqual(new TerrainRect(99, 48, 4, 3), noOffsetTranslation.Destination);
        Assert.IsTrue(noOffsetTranslation.FlipX);
        AssertThrows<TerrainViewerException>(() => StaticSceneryGeometry.Place(new StaticSceneryPlanInput(0, int.MaxValue, -1, 0, 0, identity, 0, 0, 0, 1, 1, 0, 0, 0, 0, false)));
        AssertThrows<TerrainViewerException>(() => StaticSceneryGeometry.Place(new StaticSceneryPlanInput(0, 0, 0, null, 0, identity, 0, 0, 0, 1, 1, 0, 0, 0, 0, false)));
    }

    [TestMethod]
    public void Static_scenery_plan_orders_equal_anchors_by_source_index()
    {
        var identity = new Arcanum.Data.StaticObjects.StaticObjectResourceIdentity(7, 3, 99, new string('b', 64));
        var plan = StaticSceneryScenePlan.CreateSynthetic(
        [
            new StaticSceneryPlanInput(9, 0, 0, 20, 20, identity, 0, 0, 0, 3, 3, 0, 0, 0, 0, false),
            new StaticSceneryPlanInput(7, 0, 0, 20, 20, identity, 0, 0, 0, 3, 3, 0, 0, 0, 0, false)
        ]);
        CollectionAssert.AreEqual(new[] { 7, 9 }, plan.Commands.Select(command => command.SourceIndex).ToArray());
    }

    [TestMethod]
    public void Static_scenery_geometry_rejects_each_public_input_and_checked_arithmetic_boundary()
    {
        var identity = new Arcanum.Data.StaticObjects.StaticObjectResourceIdentity(7, 3, 99, new string('c', 64));
        var valid = new StaticSceneryPlanInput(4, 0, 0, 0, 0, identity, 0, 0, 0, 2, 3, 0, 0, 0, 0, false);
        var invalidInputs = new[]
        {
            valid with { SourceIndex = -1 }, valid with { LocalX = null }, valid with { LocalY = null },
            valid with { DirectOffsetX = null }, valid with { DirectOffsetY = null }, valid with { Width = 0 },
            valid with { Width = -1 }, valid with { Height = 0 }, valid with { Height = -1 }, valid with { Group = -1 },
            valid with { Frame = -1 }, valid with { Palette = -1 }, valid with { ResourceIdentity = identity with { DatOrdinal = -1 } },
            valid with { ResourceIdentity = identity with { NormalizedLogicalNameSha256 = " " } }
        };
        foreach (var input in invalidInputs)
            AssertViewerFailure(() => StaticSceneryGeometry.Place(input), TerrainViewerErrorCode.StaticResource, TerrainViewerFailureStage.Plan);

        var overflows = new[]
        {
            valid with { LocalY = int.MinValue, LocalX = 1 }, // X subtraction
            valid with { LocalY = int.MaxValue, LocalX = 0 }, // X multiplication
            valid with { LocalY = int.MaxValue, LocalX = 1 }, // Y addition
            valid with { LocalY = 100_000_000, LocalX = 100_000_000 }, // Y multiplication
            valid with { LocalY = 1, DirectOffsetX = int.MaxValue }, // X anchor add
            valid with { LocalY = 1, DirectOffsetY = int.MaxValue }, // Y anchor add
            valid with { HotX = int.MinValue }, // left subtraction
            valid with { HotY = int.MinValue }, // top subtraction
            valid with { DirectOffsetX = int.MaxValue }, // right bound add
            valid with { DirectOffsetY = int.MaxValue } // bottom bound add
        };
        foreach (var input in overflows)
            AssertViewerFailure(() => StaticSceneryGeometry.Place(input), TerrainViewerErrorCode.StaticPlan, TerrainViewerFailureStage.Plan);
    }

    [TestMethod]
    public void Failure_diagnostics_preserve_explicit_stage_and_safe_reached_aggregates()
    {
        var terrainComplete = new TerrainViewerProgress(4096, 3033, 1063);
        var staticComplete = terrainComplete.WithStaticCensus(317, 5, 312);
        var terrainDecode = new TerrainViewerError(TerrainViewerErrorCode.DecodeFailure, Stage: TerrainViewerFailureStage.TerrainDecode, Progress: terrainComplete);
        var staticFingerprint = new TerrainViewerError(TerrainViewerErrorCode.StaticFingerprint, Stage: TerrainViewerFailureStage.StaticFingerprint, Progress: terrainComplete);
        var staticDecode = new TerrainViewerError(TerrainViewerErrorCode.StaticDecode, Stage: TerrainViewerFailureStage.StaticDecode, Progress: staticComplete);
        Assert.AreEqual(TerrainViewerFailureStage.TerrainDecode, terrainDecode.Stage);
        Assert.AreEqual(4096, terrainDecode.SafeProgress.TerrainResolved);
        Assert.AreEqual(0, terrainDecode.SafeProgress.StaticCensus);
        Assert.AreEqual(TerrainViewerFailureStage.StaticFingerprint, staticFingerprint.Stage);
        Assert.AreEqual(3033, staticFingerprint.SafeProgress.TerrainTiles);
        Assert.AreEqual(TerrainViewerFailureStage.StaticDecode, staticDecode.Stage);
        Assert.AreEqual(317, staticDecode.SafeProgress.StaticCensus);
        Assert.AreEqual(5, staticDecode.SafeProgress.StaticAdmitted);
        Assert.AreEqual(312, staticDecode.SafeProgress.StaticOutsideScope);
        CollectionAssert.AreEqual(new[] { "Arguments", "Fingerprint", "TerrainCensus", "TerrainDecode", "StaticFingerprint", "StaticCensus", "StaticResource", "StaticDecode", "ActorFingerprint", "ActorMetadata", "ActorInput", "ActorRenderState", "ActorFacing", "ActorFrame", "ActorDecode", "ActorPlan", "Plan", "Upload", "Readback" },
            Enum.GetNames<TerrainViewerFailureStage>());
        AssertThrows<ArgumentOutOfRangeException>(() => new TerrainViewerProgress(-1));
        AssertThrows<ArgumentOutOfRangeException>(() => new TerrainViewerProgress(4097));
        AssertThrows<ArgumentOutOfRangeException>(() => new TerrainViewerProgress(1, 1, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => new TerrainViewerProgress(4096, 3033, 1063, 317, 6, 311));
        Assert.AreNotSame(terrainComplete, staticComplete);
        foreach (var property in typeof(TerrainViewerProgress).GetProperties())
            Assert.IsNull(property.SetMethod, $"{property.Name} must not expose a mutation path.");
    }

    [TestMethod]
    public void Combined_diagnostic_plan_overflow_preserves_plan_stage_and_completed_progress()
    {
        var terrainKey = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var terrain = TerrainScenePlan.CreateSynthetic(new TerrainRect(0, 0, 100, 60),
            [new TerrainPlanInput(0, terrainKey, TerrainKind.Tile, 0, 0, 0, 78, 40,
                new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        var identity = new Arcanum.Data.StaticObjects.StaticObjectResourceIdentity(7, 3, 99, new string('d', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic(
        [
            new StaticSceneryPlanInput(0, 0, 0, int.MinValue, 0, identity, 0, 0, 0, 1, 1, 0, 0, 0, 0, false),
            new StaticSceneryPlanInput(1, 0, 0, int.MaxValue - 1, 0, identity, 0, 0, 0, 1, 1, 0, 0, 0, 0, false)
        ]);
        var progress = new TerrainViewerProgress(4096, 3033, 1063, 317, 5, 312);

        try
        {
            _ = DiagnosticScenePlan.Create(terrain, statics, progress);
            Assert.Fail("Expected a checked combined-bounds failure.");
        }
        catch (TerrainViewerException exception)
        {
            Assert.AreEqual(TerrainViewerErrorCode.StaticPlan, exception.Error.Code);
            Assert.AreEqual(TerrainViewerFailureStage.Plan, exception.Error.Stage);
            Assert.AreEqual(4096, exception.Error.SafeProgress.TerrainResolved);
            Assert.AreEqual(3033, exception.Error.SafeProgress.TerrainTiles);
            Assert.AreEqual(1063, exception.Error.SafeProgress.TerrainFacades);
            Assert.AreEqual(317, exception.Error.SafeProgress.StaticCensus);
            Assert.AreEqual(5, exception.Error.SafeProgress.StaticAdmitted);
            Assert.AreEqual(312, exception.Error.SafeProgress.StaticOutsideScope);
        }
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
        AssertThrows<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(0, TerrainCachePolicy.MaxTotalDistinctKeys, 1, 1));
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
    public async Task Coordinator_serializes_per_load_archive_decode_work_and_disposes_each_load_before_gate_release()
    {
        var firstOperation = new BarrierArchiveDecodeOperation();
        var secondOperation = new ImmediateArchiveOperation(() => Assert.AreEqual(1, firstOperation.DisposeCount,
            "The completed per-load archive must be disposed before the next serialized load starts."));
        var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(firstOperation, secondOperation));
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        var first = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await firstOperation.Entered.Task;
        var second = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await Task.Delay(20);
        Assert.AreEqual(1, firstOperation.ExecuteCount, "Second request must remain outside the archive/decode operation.");
        firstOperation.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency);
        Assert.AreEqual(1, firstOperation.MaximumActive);
        Assert.AreEqual(1, firstOperation.DisposeCount);
        Assert.AreEqual(1, secondOperation.DisposeCount);
        coordinator.Dispose();
        Assert.AreEqual(1, firstOperation.DisposeCount, "Coordinator disposal must not redispose a completed per-load archive.");
        Assert.AreEqual(1, secondOperation.DisposeCount);
        coordinator.Dispose();
        Assert.AreEqual(1, firstOperation.DisposeCount);
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
    public async Task Delayed_stale_success_and_failure_cannot_mutate_scene_or_hud_after_a_newer_generation()
    {
        var success = new BarrierArchiveDecodeOperation();
        var current = new ImmediateArchiveOperation();
        using (var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(success, current)))
        {
            var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
            var staleGeneration = coordinator.BeginGeneration();
            var staleLoad = coordinator.LoadAsync(staleGeneration, arguments);
            await success.Entered.Task;
            var currentGeneration = coordinator.BeginGeneration();
            var currentLoad = coordinator.LoadAsync(currentGeneration, arguments);
            success.Release.TrySetResult();
            var staleResult = await staleLoad;
            var scene = "new-generation-clear";
            var hud = "new-generation-loading";
            Assert.IsFalse(coordinator.TryPublish(staleResult.Generation, () => scene = "stale-scene"));
            Assert.IsFalse(coordinator.TryPublish(staleResult.Generation, () => hud = "stale-ready"));
            Assert.AreEqual("new-generation-clear", scene);
            Assert.AreEqual("new-generation-loading", hud);
            var currentResult = await currentLoad;
            Assert.IsTrue(coordinator.TryPublish(currentResult.Generation, () => { scene = "current-scene"; hud = "current-ready"; }));
            Assert.AreEqual("current-scene", scene);
            Assert.AreEqual("current-ready", hud);
            Assert.AreEqual(1, success.DisposeCount);
            Assert.AreEqual(1, current.DisposeCount);
        }

        var failure = new ThrowingBarrierArchiveOperation();
        var replacement = new ImmediateArchiveOperation();
        using var failureCoordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(failure, replacement));
        var failureArguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        var failedGeneration = failureCoordinator.BeginGeneration();
        var failedLoad = failureCoordinator.LoadAsync(failedGeneration, failureArguments);
        await failure.Entered.Task;
        var replacementGeneration = failureCoordinator.BeginGeneration();
        var replacementLoad = failureCoordinator.LoadAsync(replacementGeneration, failureArguments);
        failure.Release.TrySetResult();
        await AssertThrowsAsync<TerrainViewerException>(failedLoad);
        var errorHud = "new-generation-loading";
        var sceneAfterFailure = "new-generation-clear";
        Assert.IsFalse(failureCoordinator.TryPublish(failedGeneration, () => errorHud = "stale-error"));
        Assert.IsFalse(failureCoordinator.TryPublish(failedGeneration, () => sceneAfterFailure = "stale-clear"));
        Assert.AreEqual("new-generation-loading", errorHud);
        Assert.AreEqual("new-generation-clear", sceneAfterFailure);
        await replacementLoad;
        Assert.AreEqual(1, failure.DisposeCount);
        Assert.AreEqual(1, replacement.DisposeCount);
    }

    [TestMethod]
    public async Task Coordinator_propagates_safe_operation_failure_and_disposes_failure_path()
    {
        var operation = new ThrowingArchiveOperation();
        using (var coordinator = new TerrainLoadCoordinator(operation))
        {
            var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
            await AssertThrowsAsync<TerrainViewerException>(coordinator.LoadAsync(coordinator.BeginGeneration(), arguments));
            Assert.AreEqual(1, operation.DisposeCount, "The failing per-load archive must be disposed before coordinator disposal.");
        }
        Assert.AreEqual(1, operation.DisposeCount);
    }

    [TestMethod]
    public async Task Coordinator_cancellation_prevents_publish_disposes_once_and_remains_reusable()
    {
        var cancelled = new CancellableBarrierOperation();
        var replacement = new ImmediateArchiveOperation();
        using var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(cancelled, replacement));
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        using var source = new CancellationTokenSource();
        var cancelledGeneration = coordinator.BeginGeneration();
        var load = coordinator.LoadAsync(cancelledGeneration, arguments, source.Token);
        await cancelled.Entered.Task;
        source.Cancel();
        await AssertThrowsAsync<OperationCanceledException>(load);
        Assert.AreEqual(1, cancelled.DisposeCount);
        var replacementGeneration = coordinator.BeginGeneration();
        var scene = "new-generation-clear";
        Assert.IsFalse(coordinator.TryPublish(cancelledGeneration, () => scene = "cancelled-scene"));
        Assert.AreEqual("new-generation-clear", scene);

        var result = await coordinator.LoadAsync(replacementGeneration, arguments);
        Assert.IsTrue(coordinator.TryPublish(result.Generation, () => scene = "replacement-scene"));
        Assert.AreEqual("replacement-scene", scene);
        Assert.AreEqual(1, replacement.DisposeCount);
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency);
    }

    [TestMethod]
    public async Task Coordinator_waiting_cancellation_creates_no_operation_and_can_load_again()
    {
        var first = new BarrierArchiveDecodeOperation();
        var waiting = new ImmediateArchiveOperation();
        using var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(first, waiting));
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        var firstLoad = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        await first.Entered.Task;
        using var source = new CancellationTokenSource();
        var waitingLoad = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments, source.Token);
        source.Cancel();
        await AssertThrowsAsync<OperationCanceledException>(waitingLoad);
        Assert.AreEqual(0, waiting.DisposeCount, "Waiting cancellation must not create an archive/decode operation.");
        first.Release.TrySetResult();
        await firstLoad;
        var replacement = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        Assert.AreEqual(1, waiting.DisposeCount, "The queued operation must remain available after a waiting cancellation.");
        Assert.IsTrue(coordinator.TryPublish(replacement.Generation, () => { }));
    }

    [TestMethod]
    public async Task Coordinator_acquired_permit_cancellation_releases_without_creating_an_operation()
    {
        var replacement = new ImmediateArchiveOperation();
        using var cancellation = new CancellationTokenSource();
        using var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(replacement), cancellation.Cancel);
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        await AssertThrowsAsync<OperationCanceledException>(coordinator.LoadAsync(coordinator.BeginGeneration(), arguments, cancellation.Token));
        Assert.AreEqual(0, replacement.DisposeCount, "Cancellation after permit acquisition must not create an operation.");
        Assert.AreEqual(1, coordinator.MaximumObservedConcurrency, "The acquired permit remains correctly accounted for.");

        var result = await coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);
        Assert.AreEqual(1, replacement.DisposeCount, "The released permit must make the same queued operation reusable.");
        Assert.IsTrue(coordinator.TryPublish(result.Generation, () => { }));
    }

    [TestMethod]
    public async Task Coordinator_exit_cancels_active_and_waiting_loads_drains_before_gate_disposal_and_rejects_later_loads()
    {
        var active = new CancellableBarrierOperation();
        var waiting = new ImmediateArchiveOperation();
        var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(active, waiting));
        var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
        var activeGeneration = coordinator.BeginGeneration();
        var activeLoad = coordinator.LoadAsync(activeGeneration, arguments);
        await active.Entered.Task;
        var waitingLoad = coordinator.LoadAsync(coordinator.BeginGeneration(), arguments);

        await Task.Run(coordinator.Dispose);
        await AssertThrowsAsync<OperationCanceledException>(activeLoad);
        await AssertThrowsAsync<OperationCanceledException>(waitingLoad);
        Assert.AreEqual(1, active.DisposeCount, "Exit must dispose the created operation once after cancellation.");
        Assert.AreEqual(0, waiting.DisposeCount, "A queued load must not create an operation while exit drains.");
        Assert.IsFalse(coordinator.TryPublish(activeGeneration, () => Assert.Fail("Exit must suppress publication.")));
        await AssertThrowsAsync<ObjectDisposedException>(coordinator.LoadAsync(coordinator.BeginGeneration(), arguments));
        coordinator.Dispose();
    }

    private sealed class QueueArchiveOperationFactory : ITerrainLoadOperationFactory
    {
        private readonly Queue<ITerrainLoadOperation> _operations;

        public QueueArchiveOperationFactory(params ITerrainLoadOperation[] operations) => _operations = new Queue<ITerrainLoadOperation>(operations);
        public ITerrainLoadOperation Create() => _operations.Dequeue();
    }

    private sealed class BarrierArchiveDecodeOperation : ITerrainLoadOperation
    {
        private int _active;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecuteCount { get; private set; }
        public int MaximumActive { get; private set; }
        public int DisposeCount { get; private set; }

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
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

    private sealed class ImmediateArchiveOperation : ITerrainLoadOperation
    {
        private readonly Action? _onExecute;
        public int DisposeCount { get; private set; }
        public ImmediateArchiveOperation(Action? onExecute = null) => _onExecute = onExecute;
        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken) { _onExecute?.Invoke(); return Result(generation); }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingBarrierArchiveOperation : ITerrainLoadOperation
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable));
        }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ThrowingArchiveOperation : ITerrainLoadOperationFactory, ITerrainLoadOperation
    {
        public int DisposeCount { get; private set; }
        public ITerrainLoadOperation Create() => this;
        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
            => throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.InputUnavailable));
        public void Dispose() => DisposeCount++;
    }

    private sealed class CancellableBarrierOperation : ITerrainLoadOperation
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            return Result(generation);
        }

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

    private static void AssertViewerFailure(Action action, TerrainViewerErrorCode code, TerrainViewerFailureStage stage)
    {
        try { action(); Assert.Fail($"Expected {nameof(TerrainViewerException)}."); }
        catch (TerrainViewerException exception)
        {
            Assert.AreEqual(code, exception.Error.Code);
            Assert.AreEqual(stage, exception.Error.Stage);
        }
    }
}