using Arcanum.TerrainViewer.Core;
using Arcanum.Data.Terrain;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class CorePolicyTests
{
    [TestMethod]
    public void Area_arguments_take_an_optional_cave_object_file_only_in_crash_site_area()
    {
        string[] area = ["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--actor-resource", "a", "--prototypes", "p",
            "--area-mobile", "o", "--area-effects", "e", "--area-patch", "x", "--source-id", "fixture"];
        Assert.IsNull(TerrainViewerArguments.Parse(area).AreaCaveMobilePath);
        Assert.AreEqual("c", TerrainViewerArguments.Parse([.. area, "--area-cave-mobile", "c"]).AreaCaveMobilePath);
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse([.. area, "--area-cave-mobile", " "]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse([.. area, "--area-cave-mobile", "c", "--area-cave-mobile", "d"]));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--module", "m", "--resource", "r", "--source-id", "fixture", "--area-cave-mobile", "c"]));
    }

    [TestMethod]
    public void A_whole_map_takes_the_game_folder_and_one_folder_name_and_refuses_the_crash_site_inputs()
    {
        string[] map = ["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--actor-resource", "a", "--source-id", "fixture",
            "--game-root", "g", "--area-map", "elven ruins"];
        var parsed = TerrainViewerArguments.Parse(map);
        Assert.AreEqual(("g", "elven ruins", (string?)null), (parsed.GameRoot, parsed.AreaMapName, parsed.PrototypePath));
        Assert.IsNotNull(parsed.ActorInput);
        foreach (var extra in new[] { new[] { "--prototypes", "p" }, ["--area-mobile", "o"], ["--area-effects", "e"], ["--area-patch", "x"],
            ["--area-cave-mobile", "c"], ["--actor-map-x", "1"], ["--area-map", "other"], ["--game-root", "h"] })
            AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse([.. map, .. extra]));
        Assert.AreEqual(new string('x', 128), TerrainViewerArguments.Parse([.. map[..^1], new string('x', 128)]).AreaMapName);
        foreach (var name in new[] { "", " ", "maps/x", "a\\b", "line\nbreak", new string('x', 129) })
            AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse([.. map[..^1], name]));
        // Map mode needs the game folder and the actor archive, and only crash-site-area has it.
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(map.Where((_, i) => i is not (10 or 11)).ToArray()));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(map.Where((_, i) => i is not (6 or 7)).ToArray()));
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "world-diagnostic", .. map[2..]]));
        // The game folder alone is not a mode.
        AssertThrows<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "m", "--resource", "r",
            "--actor-resource", "a", "--prototypes", "p", "--area-mobile", "o", "--area-effects", "e", "--area-patch", "x", "--source-id", "fixture", "--game-root", "g"]));
    }

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
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4754, 0xc9001850, 0, 0, 1), 7, 5, 2, 3, 11, -4, 8, 15);
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
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Frame = 8 }, frame), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input, frame with { TextureKey = frame.TextureKey with { Palette = 0 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);

        // Select/CreateSynthetic are public production entry points. Their
        // closed input failures must precede an absent catalogue lookup.
        var planWithAbsentSelection = ActorDiagnosticScenePlan.CreateSynthetic(input, [frame]);
        AssertViewerFailure(() => planWithAbsentSelection.Select(input with { Action = (ActorDiagnosticAction)99 }), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(input with { Action = (ActorDiagnosticAction)99 }, [frame]), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        AssertViewerFailure(() => planWithAbsentSelection.Select(input with { RenderBranch = ActorDiagnosticRenderBranch.Unknown }), TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
    }

    [TestMethod]
    public void Actor_public_planning_seam_has_literal_signed_deltas_offsets_and_multiframe_guide_union()
    {
        var first = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4754, 0xc9001850, 0, 0, 1), 7, 5, 2, 3, 11, -4, 8, 15);
        var second = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4754, 0xc9001850, 7, 0, 1), 11, 9, -2, 7, -13, 6, 8, 15);
        var input = new ActorDiagnosticPlacementInput(2, 6, 100, 50, -21, 8, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Walk, 0, 0, 7, 5, 2, 3, 11, -4);
        Assert.AreEqual(new TerrainRect(237, 235, 7, 5), ActorDiagnosticScenePlan.Place(input, first).Destination);
        Assert.AreEqual(new TerrainRect(197, 255, 7, 5), ActorDiagnosticScenePlan.Place(input with { MapX = 3 }, first).Destination);
        Assert.AreEqual(new TerrainRect(277, 255, 7, 5), ActorDiagnosticScenePlan.Place(input with { MapY = 7 }, first).Destination);
        Assert.AreEqual(new TerrainRect(224, 235, 7, 5), ActorDiagnosticScenePlan.Place(input with { CameraOriginX = 87 }, first).Destination);
        Assert.AreEqual(new TerrainRect(237, 252, 7, 5), ActorDiagnosticScenePlan.Place(input with { CameraOriginY = 67 }, first).Destination);
        Assert.AreEqual(new TerrainRect(228, 235, 7, 5), ActorDiagnosticScenePlan.Place(input with { InstanceOffsetX = -30 }, first).Destination);
        Assert.AreEqual(new TerrainRect(237, 247, 7, 5), ActorDiagnosticScenePlan.Place(input with { InstanceOffsetY = 20 }, first).Destination);
        var mirroredFirst = ActorDiagnosticScenePlan.Place(input with { Facing = 1 }, first with { TextureKey = first.TextureKey with { PhysicalGroup = 7 } });
        Assert.AreEqual(new TerrainRect(235, 235, 7, 5), mirroredFirst.Destination);
        Assert.AreEqual(-11, mirroredFirst.EffectiveOffsetX);
        var changedOffset = ActorDiagnosticScenePlan.Place(input with { Facing = 1, ArtOffsetX = -9, ArtOffsetY = 6 },
            first with { TextureKey = first.TextureKey with { PhysicalGroup = 7 }, ArtOffsetX = -9, ArtOffsetY = 6 });
        Assert.AreEqual(new TerrainRect(235, 235, 7, 5), changedOffset.Destination, "Frame offsets are retained metadata, not another placement translation.");
        Assert.AreEqual(9, changedOffset.EffectiveOffsetX);
        Assert.AreEqual(6, changedOffset.EffectiveOffsetY);

        var secondUnmirrored = ActorDiagnosticScenePlan.Place(input with { Facing = 7, Width = 11, Height = 9, HotX = -2, HotY = 7, ArtOffsetX = -13, ArtOffsetY = 6 }, second);
        var secondMirrored = ActorDiagnosticScenePlan.Place(input with { Facing = 1, Width = 11, Height = 9, HotX = -2, HotY = 7, ArtOffsetX = -13, ArtOffsetY = 6 }, second);
        Assert.AreEqual(new TerrainRect(241, 231, 11, 9), secondUnmirrored.Destination);
        Assert.AreEqual(new TerrainRect(227, 231, 11, 9), secondMirrored.Destination);
        Assert.AreEqual(13, secondMirrored.EffectiveOffsetX);
        var actor = ActorDiagnosticScenePlan.CreateSynthetic(input, [first, second]);
        var terrainKey = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var terrain = TerrainScenePlan.CreateSynthetic(new TerrainRect(227, 231, 25, 12),
            [new TerrainPlanInput(0, terrainKey, TerrainKind.Tile, 3, 9, 0, 1, 1, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        Assert.AreEqual(new TerrainRect(227, 231, 25, 12), DiagnosticScenePlan.Create(terrain, null, actor).WorldBounds);
    }

    [TestMethod]
    public void Actor_public_planning_seam_rejects_each_closed_input_and_catalogue_metadata_mismatch()
    {
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4753, 0xc9001810, 0, 0, 1), 1, 1, 0, 0, 0, 0, 15, 8);
        var input = new ActorDiagnosticPlacementInput(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 0, 0, 1, 1, 0, 0, 0, 0);
        foreach (var absent in new ActorDiagnosticPlacementInput[]
        {
            input with { MapX = null }, input with { MapY = null }, input with { CameraOriginX = null }, input with { CameraOriginY = null },
            input with { InstanceOffsetX = null }, input with { InstanceOffsetY = null }, input with { Shrunk = null }
        }) AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(absent, frame), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        foreach (var render in new[]
        {
            input with { RenderBranch = ActorDiagnosticRenderBranch.CachedUnsupported }, input with { RenderBranch = ActorDiagnosticRenderBranch.Unknown },
            input with { ScalePercent = 99 }, input with { ScalePercent = 101 }, input with { Shrunk = true }
        }) AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(render, frame), TerrainViewerErrorCode.ActorRenderState, TerrainViewerFailureStage.ActorRenderState);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Action = (ActorDiagnosticAction)9 }, frame), TerrainViewerErrorCode.ActorInput, TerrainViewerFailureStage.ActorInput);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Facing = -1 }, frame), TerrainViewerErrorCode.ActorFacing, TerrainViewerFailureStage.ActorFacing);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Frame = -1 }, frame), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Width = 0 }, frame), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Height = -1 }, frame), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input, frame with { TextureKey = frame.TextureKey with { Palette = 0 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(input, [frame, frame]), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(input, [frame with { TextureKey = frame.TextureKey with { Selector = 0 } }]), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
    }

    [TestMethod]
    public void Actor_public_planning_seam_binds_literal_two_resource_selector_action_and_timing_profile()
    {
        var idle = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4753, 0xc9001810, 0, 0, 1), 3, 2, 0, 0, 0, 0, 15, 8);
        var idleInput = new ActorDiagnosticPlacementInput(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 0, 0, 3, 2, 0, 0, 0, 0);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(idleInput, []), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput with { Frame = 15 }, idle), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput, idle with { TextureKey = idle.TextureKey with { ResourceOrdinal = 4754 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput, idle with { TextureKey = idle.TextureKey with { Selector = 0xc9001850 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput, idle with { FramesPerAction = 14 }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput, idle with { RawFramesPerSecond = 7 }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput, idle with { TextureKey = idle.TextureKey with { PhysicalGroup = 1 } }), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput with { Action = ActorDiagnosticAction.Walk }, idle), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput with { Width = 2 }, idle), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(idleInput with { Height = 3 }, idle), TerrainViewerErrorCode.ActorFrame, TerrainViewerFailureStage.ActorFrame);
    }

    [TestMethod]
    public void Actor_archive_open_boundary_maps_only_authorized_input_failures_and_identity_is_path_free()
    {
        var input = new ActorDiagnosticPlacementInput(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 0, 0, 1, 1, 0, 0, 0, 0);
        var progress = new TerrainViewerProgress(1, 1, 0);
        foreach (var exception in new Exception[]
        {
            new DatException("private"), new DatFormatException("private"), new DatUnsupportedFormatException("private"),
            new DatLimitExceededException("private"), new DatUnsupportedEntryException("private"), new IOException("private"),
            new FileNotFoundException("private"), new DirectoryNotFoundException("private"), new UnauthorizedAccessException("private")
        })
        {
            using var operation = new ActorDiagnosticSourceOperation(() => throw exception);
            AssertViewerFailure(() => operation.Execute(null!, input, 0, 0, progress, CancellationToken.None), TerrainViewerErrorCode.ActorFingerprint, TerrainViewerFailureStage.ActorFingerprint);
        }
        using (var programmer = new ActorDiagnosticSourceOperation(() => throw new InvalidOperationException("sentinel")))
            AssertThrows<InvalidOperationException>(() => programmer.Execute(null!, input, 0, 0, progress, CancellationToken.None));
        using (var cancelled = new ActorDiagnosticSourceOperation(() => throw new OperationCanceledException()))
            AssertThrows<OperationCanceledException>(() => cancelled.Execute(null!, input, 0, 0, progress, CancellationToken.None));
        ActorDiagnosticSource.VerifyIdentity(new InputFingerprint(7, "abc"), 7, "abc", "abc");
        AssertViewerFailure(() => ActorDiagnosticSource.VerifyIdentity(new InputFingerprint(6, "abc"), 7, "abc", null), TerrainViewerErrorCode.ActorFingerprint, TerrainViewerFailureStage.ActorFingerprint);
        AssertViewerFailure(() => ActorDiagnosticSource.VerifyIdentity(new InputFingerprint(7, "abc"), 7, "def", null), TerrainViewerErrorCode.ActorFingerprint, TerrainViewerFailureStage.ActorFingerprint);
        AssertViewerFailure(() => ActorDiagnosticSource.VerifyIdentity(new InputFingerprint(7, "abc"), 7, "abc", "def"), TerrainViewerErrorCode.ActorFingerprint, TerrainViewerFailureStage.ActorFingerprint);
    }

    [TestMethod]
    public void Actor_source_operation_decodes_real_synthetic_dat_and_art_catalogue_with_checked_retention_and_disposal()
    {
        using var fixture = SyntheticActorSourceFixture.Create();
        using var operation = fixture.CreateOperation();
        var load = operation.Execute(fixture.Module, fixture.Input(ActorDiagnosticAction.Walk, facing: 1, frame: 7), 0, 0,
            TerrainViewerProgress.Empty, CancellationToken.None);
        Assert.AreEqual(184, load.Textures.Count);
        Assert.AreEqual(184, load.Plan.Catalog.Count);
        Assert.AreEqual(120, load.Textures.Keys.Count(key => key.ResourceOrdinal == 4753));
        Assert.AreEqual(64, load.Textures.Keys.Count(key => key.ResourceOrdinal == 4754));
        Assert.AreEqual(736L, load.CheckedRgbaBytes);
        Assert.AreEqual(new ActorDiagnosticTextureKey(4754, 0xc9001850, 7, 7, 1), load.Plan.Current.TextureKey);
        Assert.AreEqual(7, load.Plan.Current.PhysicalGroup);
        Assert.IsTrue(load.Plan.Current.FlipX);
        operation.Dispose();
        Assert.AreEqual(1, fixture.ResourceDisposeCount, "Actor archive must be disposed exactly once with its production operation.");

        using var byteCapFixture = SyntheticActorSourceFixture.Create();
        using var byteCapOperation = byteCapFixture.CreateOperation();
        var retainedBeforeByteCap = 0;
        AssertViewerFailure(() => byteCapOperation.Execute(byteCapFixture.Module, byteCapFixture.Input(), 0,
            TerrainCachePolicy.MaxRgbaBytes - 4, TerrainViewerProgress.Empty, CancellationToken.None,
            retained => retainedBeforeByteCap = retained), TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
        Assert.AreEqual(1, retainedBeforeByteCap, "The second 1x1 frame must fail before its RGBA copy is retained.");

        using var countCapFixture = SyntheticActorSourceFixture.Create();
        using var countCapOperation = countCapFixture.CreateOperation();
        var retainedBeforeCountCap = 0;
        AssertViewerFailure(() => countCapOperation.Execute(countCapFixture.Module, countCapFixture.Input(), TerrainCachePolicy.MaxTotalDistinctKeys - 1,
            0, TerrainViewerProgress.Empty, CancellationToken.None, retained => retainedBeforeCountCap = retained),
            TerrainViewerErrorCode.ResourceLimit, TerrainViewerFailureStage.ActorDecode);
        Assert.AreEqual(1, retainedBeforeCountCap, "The aggregate key limit must be checked before retaining the second actor frame.");
    }

    [TestMethod]
    public void Actor_source_operation_cancellation_and_substituted_real_fixture_failures_are_actor_specific()
    {
        using var cancellationFixture = SyntheticActorSourceFixture.Create();
        using var cancellationOperation = cancellationFixture.CreateOperation();
        using var cancellation = new CancellationTokenSource();
        var checkpoint = 0;
        AssertThrows<OperationCanceledException>(() => cancellationOperation.Execute(cancellationFixture.Module, cancellationFixture.Input(), 0, 0,
            TerrainViewerProgress.Empty, cancellation.Token, retained => { checkpoint = retained; if (retained == 17) cancellation.Cancel(); }));
        Assert.AreEqual(17, checkpoint, "Cancellation must occur after the seventeenth retained frame and before frame eighteen.");
        cancellationOperation.Dispose();
        Assert.AreEqual(1, cancellationFixture.ResourceDisposeCount);

        using var missingResource = SyntheticActorSourceFixture.Create(missingIdle: true);
        using var missingOperation = missingResource.CreateOperation();
        AssertViewerFailure(() => missingOperation.Execute(missingResource.Module, missingResource.Input(), 0, 0,
            TerrainViewerProgress.Empty, CancellationToken.None), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);

        using var malformedResource = SyntheticActorSourceFixture.Create(invalidIdleArt: true);
        using var malformedOperation = malformedResource.CreateOperation();
        AssertViewerFailure(() => malformedOperation.Execute(malformedResource.Module, malformedResource.Input(), 0, 0,
            TerrainViewerProgress.Empty, CancellationToken.None), TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode);

        using var metadataMismatch = SyntheticActorSourceFixture.Create(idleFrames: 14);
        using var metadataOperation = metadataMismatch.CreateOperation();
        AssertViewerFailure(() => metadataOperation.Execute(metadataMismatch.Module, metadataMismatch.Input(), 0, 0,
            TerrainViewerProgress.Empty, CancellationToken.None), TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
    }

    [TestMethod]
    public void Actor_source_operation_real_dat_art_matrix_rejects_each_fixed_profile_substitution_and_disposes_once()
    {
        // Every case writes a real 1TAD/ART byte fixture and invokes the
        // production ActorDiagnosticSourceOperation.  Expected classifications
        // are literal; this test does not replace decoding with a fake result.
        var metadataCases = new Func<SyntheticActorSourceFixture>[]
        {
            () => SyntheticActorSourceFixture.Create(moduleEntryCount: 643),
            () => SyntheticActorSourceFixture.Create(missingWalk: true),
            () => SyntheticActorSourceFixture.Create(swapActionPayloads: true),
            () => SyntheticActorSourceFixture.Create(physicalGroupCount: 1),
            () => SyntheticActorSourceFixture.Create(idleFrames: 14),
            () => SyntheticActorSourceFixture.Create(walkFrames: 7),
            () => SyntheticActorSourceFixture.Create(idleFps: 7),
            () => SyntheticActorSourceFixture.Create(walkFps: 14),
            () => SyntheticActorSourceFixture.Create(absentPalette: true),
            () => SyntheticActorSourceFixture.Create(substituteIdleResourceOrdinal: 4752)
        };
        foreach (var create in metadataCases)
        {
            using var fixture = create();
            using var operation = fixture.CreateOperation();
            AssertViewerFailure(() => operation.Execute(fixture.Module, fixture.Input(), 0, 0, TerrainViewerProgress.Empty, CancellationToken.None),
                TerrainViewerErrorCode.ActorMetadata, TerrainViewerFailureStage.ActorMetadata);
            operation.Dispose();
            Assert.AreEqual(1, fixture.ResourceDisposeCount);
        }

        foreach (var create in new Func<SyntheticActorSourceFixture>[]
        {
            () => SyntheticActorSourceFixture.Create(invalidPalette: true),
            () => SyntheticActorSourceFixture.Create(resourceOptions: new DatReaderOptions { MaxStoredEntryBytes = 1, MaxDecodedEntryBytes = 1 })
        })
        {
            using var fixture = create();
            using var operation = fixture.CreateOperation();
            AssertViewerFailure(() => operation.Execute(fixture.Module, fixture.Input(), 0, 0, TerrainViewerProgress.Empty, CancellationToken.None),
                TerrainViewerErrorCode.ActorDecode, TerrainViewerFailureStage.ActorDecode);
            operation.Dispose();
            Assert.AreEqual(1, fixture.ResourceDisposeCount);
        }
    }

    [TestMethod]
    public void Actor_public_planning_seam_rejects_projection_anchor_extent_and_draw_domain_overflow()
    {
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4753, 0xc9001810, 0, 0, 1), 2, 2, 0, 0, 0, 0, 15, 8);
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
    public void Actor_public_planning_seam_has_one_literal_overflow_vector_for_every_checked_geometry_and_union_path()
    {
        // Each call names one checked production path.  Inputs and expected
        // classification are literals; no expected coordinate is delegated to
        // MapFacing, Place, or a geometry helper under test.
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(4753, 0xc9001810, 0, 0, 1), 2, 2, 0, 0, 0, 0, 15, 8);
        var input = new ActorDiagnosticPlacementInput(0, 0, 0, 0, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 0, 0, 2, 2, 0, 0, 0, 0);

        // X: map subtraction, subtract-one, multiply, and origin add.
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 0, MapX = long.MinValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = long.MinValue, MapX = 0 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = long.MaxValue, MapX = 0 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 2, CameraOriginX = long.MaxValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // Y: map add, multiply, and origin add.
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = long.MaxValue, MapX = long.MaxValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 500_000_000_000_000_000L, MapX = 500_000_000_000_000_000L }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginY = long.MaxValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // Caller offsets and the explicit (+40,+20) diagnostic anchor adds.
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = long.MaxValue - 40, InstanceOffsetX = int.MaxValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = long.MaxValue - 20, InstanceOffsetY = int.MaxValue }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = long.MaxValue - 39 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = long.MaxValue - 19 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // Mirror-specific hotspot subtraction and source-offset negation.
        var mirroredHotspot = frame with { TextureKey = frame.TextureKey with { PhysicalGroup = 7 }, Width = int.MaxValue, HotX = -1 };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Facing = 1, Width = int.MaxValue, HotX = -1 }, mirroredHotspot), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        var mirroredOffset = frame with { TextureKey = frame.TextureKey with { PhysicalGroup = 7 }, ArtOffsetX = int.MinValue };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { Facing = 1, ArtOffsetX = int.MinValue }, mirroredOffset), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // Widened left/top/right/bottom edges, then their checked int narrowing.
        var wideHotX = frame with { HotX = int.MaxValue };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = long.MinValue, HotX = int.MaxValue }, wideHotX), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        var wideHotY = frame with { HotY = int.MaxValue };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = long.MinValue, HotY = int.MaxValue }, wideHotY), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        var wideExtent = frame with { Width = 2, Height = 2 };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = long.MaxValue - 40 }, wideExtent), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = long.MaxValue - 20 }, wideExtent), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = (long)int.MinValue - 41 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = (long)int.MinValue - 21 }, frame), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { MapY = 1, CameraOriginX = (long)int.MaxValue - 40, Width = 1 }, frame with { Width = 1 }), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        AssertViewerFailure(() => ActorDiagnosticScenePlan.Place(input with { CameraOriginY = (long)int.MaxValue - 20, Height = 1 }, frame with { Height = 1 }), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // The guide cross narrows anchor-4 and anchor+5 independently.
        var crossLeft = frame with { Width = 1, Height = 1 };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(input with { MapY = 1, CameraOriginX = (long)int.MinValue - 40, Width = 1, Height = 1 }, [crossLeft]), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        var crossRight = frame with { Width = 1, Height = 1, HotX = 1 };
        AssertViewerFailure(() => ActorDiagnosticScenePlan.CreateSynthetic(input with { MapY = 1, CameraOriginX = (long)int.MaxValue - 40, Width = 1, Height = 1, HotX = 1 }, [crossRight]), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);

        // WorldBounds unions include terrain and every admitted actor guide.
        // These two cases separately overflow union width and union height.
        var terrainKey = new TerrainTextureKey("fixture", 1, 0, 1, "fixture", 0, 0, 0);
        var terrain = TerrainScenePlan.CreateSynthetic(new TerrainRect(-40, 0, 100, 100),
            [new TerrainPlanInput(0, terrainKey, TerrainKind.Tile, 0, 0, 0, 1, 1, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        var unionWidthFirst = frame with { Width = 1, Height = 1, HotX = int.MaxValue - 5 };
        var unionWidthSecond = frame with { TextureKey = frame.TextureKey with { Frame = 1 }, Width = int.MaxValue - 4, Height = 1, HotX = 0 };
        var unionWidthInput = input with { Width = 1, Height = 1, HotX = int.MaxValue - 5 };
        var widthActor = ActorDiagnosticScenePlan.CreateSynthetic(unionWidthInput, [unionWidthFirst, unionWidthSecond]);
        AssertViewerFailure(() => DiagnosticScenePlan.Create(terrain, null, widthActor), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
        var unionHeightFirst = frame with { Width = 1, Height = 1, HotY = int.MaxValue - 5 };
        var unionHeightSecond = frame with { TextureKey = frame.TextureKey with { Frame = 1 }, Width = 1, Height = int.MaxValue - 4, HotY = 0 };
        var unionHeightInput = input with { CameraOriginY = -20, Width = 1, Height = 1, HotY = int.MaxValue - 5 };
        var heightActor = ActorDiagnosticScenePlan.CreateSynthetic(unionHeightInput, [unionHeightFirst, unionHeightSecond]);
        AssertViewerFailure(() => DiagnosticScenePlan.Create(terrain, null, heightActor), TerrainViewerErrorCode.ActorPlan, TerrainViewerFailureStage.ActorPlan);
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
    public async Task Coordinator_stale_success_and_actor_failure_use_the_real_actor_source_operation()
    {
        // The adapter supplies only the minimal synthetic terrain wrapper that
        // ITerrainLoadOperation requires. Actor success/failure is always
        // produced by ActorDiagnosticSourceOperation over real 1TAD/ART bytes.
        var delayedFixture = SyntheticActorSourceFixture.Create();
        var currentFixture = SyntheticActorSourceFixture.Create();
        var delayed = new ActorSourceOperationAdapter(delayedFixture, pauseAtRetainedFrame: 184);
        var current = new ActorSourceOperationAdapter(currentFixture);
        using (var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(delayed, current)))
        {
            var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
            var staleGeneration = coordinator.BeginGeneration();
            var staleLoad = coordinator.LoadAsync(staleGeneration, arguments);
            await delayed.Entered.Task;
            var currentGeneration = coordinator.BeginGeneration();
            var currentLoad = coordinator.LoadAsync(currentGeneration, arguments);
            delayed.Release.TrySetResult();
            var stale = await staleLoad;
            var published = "new-generation-loading";
            Assert.IsFalse(coordinator.TryPublish(stale.Generation, () => published = "stale-ready"));
            var replacement = await currentLoad;
            Assert.IsTrue(coordinator.TryPublish(replacement.Generation, () => published = "replacement-ready"));
            Assert.AreEqual("replacement-ready", published);
        }
        Assert.AreEqual(1, delayedFixture.ResourceDisposeCount);
        Assert.AreEqual(1, currentFixture.ResourceDisposeCount);

        var failingFixture = SyntheticActorSourceFixture.Create(missingWalk: true);
        var replacementFixture = SyntheticActorSourceFixture.Create();
        var failing = new ActorSourceOperationAdapter(failingFixture, pauseAtRetainedFrame: 120);
        var replacementOperation = new ActorSourceOperationAdapter(replacementFixture);
        using (var coordinator = new TerrainLoadCoordinator(new QueueArchiveOperationFactory(failing, replacementOperation)))
        {
            var arguments = TerrainViewerArguments.Parse(["--module", "synthetic-module", "--resource", "synthetic-resource", "--source-id", "fixture"]);
            var staleGeneration = coordinator.BeginGeneration();
            var failedLoad = coordinator.LoadAsync(staleGeneration, arguments);
            await failing.Entered.Task;
            var replacementGeneration = coordinator.BeginGeneration();
            var replacementLoad = coordinator.LoadAsync(replacementGeneration, arguments);
            failing.Release.TrySetResult();
            await AssertThrowsAsync<TerrainViewerException>(failedLoad);
            var errorHud = "new-generation-loading";
            Assert.IsFalse(coordinator.TryPublish(staleGeneration, () => errorHud = "stale-error"));
            await replacementLoad;
            Assert.AreEqual("new-generation-loading", errorHud);
        }
        Assert.AreEqual(1, failingFixture.ResourceDisposeCount);
        Assert.AreEqual(1, replacementFixture.ResourceDisposeCount);
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

    private sealed class ActorSourceOperationAdapter : ITerrainLoadOperation
    {
        private readonly SyntheticActorSourceFixture _fixture;
        private readonly ActorDiagnosticSourceOperation _operation;
        private readonly int? _pauseAtRetainedFrame;
        private int _disposed;

        public ActorSourceOperationAdapter(SyntheticActorSourceFixture fixture, int? pauseAtRetainedFrame = null)
        {
            _fixture = fixture;
            _operation = fixture.CreateOperation();
            _pauseAtRetainedFrame = pauseAtRetainedFrame;
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TerrainLoadResult Execute(long generation, TerrainViewerArguments arguments, CancellationToken cancellationToken)
        {
            var actor = _operation.Execute(_fixture.Module, _fixture.Input(), 0, 0, TerrainViewerProgress.Empty, cancellationToken,
                retained =>
                {
                    if (_pauseAtRetainedFrame != retained) return;
                    Entered.TrySetResult();
                    Release.Task.Wait(cancellationToken);
                });
            return Result(generation) with { ActorPlan = actor.Plan, ActorTextures = actor.Textures };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _operation.Dispose();
            _fixture.Dispose();
        }
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