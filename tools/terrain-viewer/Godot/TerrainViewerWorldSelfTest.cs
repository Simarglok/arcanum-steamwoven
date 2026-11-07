using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertSyntheticWorldRetirementAsync(Node host)
    {
        var fixture = WorldFixture();
        var viewport = new SubViewport { Size = new(800,600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true }; app.SetProcess(false);
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
        app.PublishSyntheticForSelfTest(fixture.Arguments,fixture.Result);
        await app.WorldRetirementMatrixForSelfTestAsync(fixture.Arguments,fixture.Result);
        viewport.QueueFree();
        await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
    }
    internal static SyntheticActorLoad WorldFixture()
    {
        using var grid = new Arcanum.TerrainViewer.Tests.GridWireFixture(policy: Arcanum.Data.Traversal.CardinalTraversalAdmissionPolicy.BoundedAdjacent);
        var fixture = SyntheticActorFixture.Create(human: true);
        var terrainKey = fixture.Result.Textures.Keys.Single();
        var terrain = TerrainScenePlan.CreateSynthetic(new(-2600, -100, 5200, 2800),
            [new(1, terrainKey, TerrainKind.Tile, 30, 32, 0, 200, 100, new(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic(Enumerable.Range(0, 5).Select(i =>
            // One remote public static establishes room above the grid spawn
            // in the actual command-derived camera envelope. The other four
            // retain the literal overlapping terrain/static/actor pixel oracle.
            new StaticSceneryPlanInput(i, 0, 0, -204, i == 0 ? -2600 : 157, identity, 0, i, 0, 9, 7, 0, 0, 0, 0, false)));
        var textures = statics.Commands.ToDictionary(command => command.TextureKey,
            command => SyntheticStatic(command.TextureKey, command.SourceIndex));
        var input = DiagnosticActorMotion.ToPlacement(new(new(6,1),5,ActorDiagnosticAction.Idle), 0);
        var actor = fixture.Result.ActorPlan!.Select(input);
        var bounds = DiagnosticWorldBounds.Create(terrain, statics, actor);
        var result = fixture.Result with { Plan = terrain, StaticPlan = statics, StaticTextures = textures, ActorPlan = actor,
            DiagnosticPlan = DiagnosticScenePlan.CreateWorldDiagnostic(terrain, statics, actor, bounds), StaticCensusCount = 317, StaticOutsideScopeCount = 312,
            TraversalSnapshot = grid.Snapshot };
        var arguments = TerrainViewerArguments.Parse(["--mode", "world-diagnostic", "--module", "synthetic-module", "--resource", "synthetic-terrain",
            "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes", "--source-id", "synthetic"]);
        return new(arguments, result);
    }
    // ARC-RENDER-014: a synthetic ART identity per static so the WORLD HD path has questions to ask.
    private static DecodedStaticTexture SyntheticStatic(StaticSceneryTextureKey key, int index)
    {
        var rgba = SolidRgba(9, 7, (byte)(100 + index), 111, 222);
        return new DecodedStaticTexture(key, 9, 7, rgba) { HdQuery = HdFrameQueries.FromArt(HdFrameQueries.Sha256(System.Text.Encoding.ASCII.GetBytes($"synthetic-static-art-{index}")),
            4096, HdFrameQueries.Sha256(System.Text.Encoding.ASCII.GetBytes($"synthetic-static-{index}")), key.Group, key.Frame, key.Palette, 9, 7, rgba) };
    }
    private static async Task AssertSyntheticWorldAppAsync(Node host)
    {
        var fixture = WorldFixture();
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true }; app.SetProcess(false);
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.ShowActorGuidesForSelfTest(); // the sector/guide raster oracle below (ARC-DEV-064)
        app.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        var initial = app.GetSelfTestSnapshot();
        if (initial.CameraZoom != 2 || app.WorldFollowForSelfTest.Mode != GamepadLab.Core.CameraMode.Follow)
            throw new InvalidOperationException($"WORLD default-2 ordinary ready framing failed zoom={initial.CameraZoom} follow={app.WorldFollowForSelfTest}");
        var hudOrigin = initial.HudPosition;
        foreach (var ratio in new[] { 1d, 2d, 3d, 1.7 })
        {
            app.WorldZoomForSelfTest(ratio);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var framed = app.GetSelfTestSnapshot();
            var anchor = framed.CanvasTransform * new Vector2(-200,160);
            if (framed.CameraZoom != ratio || framed.HudPosition != hudOrigin || anchor.DistanceTo(new(400,300)) > 1
                || framed.Actor != initial.Actor || framed.Input != initial.Input)
                throw new InvalidOperationException($"WORLD ratio/anchor/UI/geometry separation failed ratio={ratio} actual={framed.CameraZoom} anchor={anchor} hud={framed.HudPosition}/{hudOrigin} actor={framed.Actor}/{initial.Actor} input={framed.Input}/{initial.Input} state={app.WorldStateForSelfTest}");
            if (DisplayServer.GetName() != "headless")
            {
                using var image = viewport.GetTexture().GetImage();
                AssertTransformedPixel(image, framed.CanvasTransform, -203,158,(231,37,19));
            }
        }
        app.WorldZoomForSelfTest(2);
        GD.Print("ARC-GPAD-004 default2/Follow actor framing exact1/2/3/fractional1.7 UI/geometry separation PASS; nearest public actor pixel=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS") + "; physical1:1/QHD NOT_VERIFIED");
        app.WorldGridObservationMatrixForSelfTest();
        app.WorldPaletteChordMatrixForSelfTest();
        await app.WorldPaletteGraphicalMatrixForSelfTestAsync();
        // ARC-RENDER-008: synthetic WORLD fixture blocker (5,1), Open (7,1), (20,20) outside the bounded zone.
        await app.WorldPassabilityOverlayMatrixForSelfTestAsync(new(5, 1), new(7, 1), new(20, 20), "world-diagnostic");
        app.WorldControlMatrixForSelfTest();
        await app.WorldZoomMatrixForSelfTestAsync();
        app.WorldLifecycleMatrixForSelfTest();
        await app.WorldModalRoutingMatrixForSelfTestAsync();
        await app.WorldMouseSettingsMatrixForSelfTestAsync();
        var snapshot = app.GetSelfTestSnapshot();
        if (app.WorldStateForSelfTest.Position != new DiagnosticSectorPosition(6,1) || snapshot.CacheCount != 61
            || snapshot.Actor?.AnchorX != -200 || snapshot.Actor.AnchorY != 160
            || !snapshot.Hud.Contains("Grid committed", StringComparison.Ordinal))
            throw new InvalidOperationException("WORLD literal combined publication failed.");
        // Test camera boundary uses the actual double-camera host; zoom then
        // resize must not derive actor position from any Godot transform.
        app.GetNode<Camera2D>("TerrainCamera").PositionSmoothingEnabled = false;
        var camera = app.GetNode<Camera2D>("TerrainCamera");
        // Preserve the historical sector/guide raster oracle from explicit Fit;
        // ordinary default framing is verified independently by the zoom matrix.
        app.WorldPaletteFitForSelfTest(); // the palette Fit action (ARC-INPUT-001: no F key)
        for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(new(-1, default), Array.Empty<GamepadLab.Core.RawInputFrame>(), .1);
        // Fit is intentionally Manual and keeps its center under wheel zoom.
        // Explicitly frame the new diagnostic spawn before the sprite oracle;
        // the old free spawn happened to lie near the full-sector Fit center.
        app.WorldFitFollowForSelfTest();
        // Zoom through the production preferred-zoom route (the palette's exact
        // zoom; the world wheel no longer zooms), to the ratio 14 former wheel
        // steps reached. At least two screen pixels/source pixel keep the last
        // sparse source column observable next to the 1px diagnostic outline.
        app.WorldZoomForSelfTest(FormerWheelZoom(app));
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        snapshot = app.GetSelfTestSnapshot();
        if (DisplayServer.GetName() != "headless")
        {
            using var image = viewport.GetTexture().GetImage();
            // Actor source (1,1)/(7,1) over statics. Transparent center keeps
            // the final equal-anchor static sourceIndex4 color, not terrain.
            AssertTransformedPixel(image, snapshot.CanvasTransform, -203,158,(231,37,19));
            AssertTransformedPixel(image, snapshot.CanvasTransform, -197,158,(24,218,236));
            AssertTransformedPixel(image, snapshot.CanvasTransform, -202,158,(104,111,222));
            AssertGuideAt(image, snapshot.CanvasTransform,-204,157,false);
            AssertGuideAt(image, snapshot.CanvasTransform,-200,160,true);
        }
        var actorBefore = app.WorldStateForSelfTest;
        foreach (var size in new[] { new Vector2I(480,320), new(1280,800), new(800,600) })
        {
            viewport.Size = size; await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (app.WorldStateForSelfTest != actorBefore || app.GetSelfTestSnapshot().CacheCount != 61)
                throw new InvalidOperationException("WORLD resize mutated actor/cache.");
        }
        // The bounded zone, not the old free sector rectangle, is authoritative.
        // All directions are tested through actual aggregation/Data/runtime.
        var none = new GamepadLab.Core.RawInputFrame(-1, default);
        foreach (var (direction, x, y) in new[] {
            (new GamepadLab.Core.Vec2(-1,-1),6d,0d), (new GamepadLab.Core.Vec2(-1,1),8d,1d),
            (new GamepadLab.Core.Vec2(1,-1),6d,1d), (new GamepadLab.Core.Vec2(1,1),6d,3d) })
        {
            var pad = new[] { TerrainViewerApp.StickLevel(default) };
            app.SelectSimulatedPadForSelfTest(); app.WorldPaletteResetSceneForSelfTest();
            app.WorldSettleForSelfTest(pad);
            for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(none, pad, .1);
            var time = app.WorldTimeForSelfTest.Value;
            foreach (var offset in new[] { Arcanum.Simulation.Movement.ExactScalar.Create(1,10), Arcanum.Simulation.Movement.ExactScalar.Integer(2), Arcanum.Simulation.Movement.ExactScalar.Create(21,10) })
                app.WorldFrameForSelfTest(none, new[] { TerrainViewerApp.StickLevel(direction) }, .1,
                    observationTime: new Arcanum.Simulation.Movement.SimulationTime(time + offset));
            var corner = app.GetSelfTestSnapshot();
            var bounds = fixture.Result.DiagnosticPlan!.WorldBounds;
            if (app.WorldStateForSelfTest.Position != new DiagnosticSectorPosition(x,y)
                || app.WorldStateForSelfTest.Action != ActorDiagnosticAction.Idle || corner.CacheCount != 61
                || corner.Actor!.Destination.X < bounds.X || corner.Actor.Destination.Y < bounds.Y
                || corner.Actor.Destination.X + corner.Actor.Destination.Width > bounds.X + bounds.Width
                || corner.Actor.Destination.Y + corner.Actor.Destination.Height > bounds.Y + bounds.Height)
                throw new InvalidOperationException("WORLD actual corner/envelope containment failed.");
            // Resolve the guide in the real combined viewport at each corner.
            // Fit can downsample a one-pixel guide; use the production zoom
            // route for a resolved guide, as for the asymmetric sprite oracle.
            app.WorldPaletteFitForSelfTest();
            for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(none, pad, .1);
            app.WorldFitFollowForSelfTest();
            app.WorldZoomForSelfTest(FormerWheelZoom(app));
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless")
            {
                corner = app.GetSelfTestSnapshot();
                using var image = viewport.GetTexture().GetImage();
                AssertGuideAt(image, corner.CanvasTransform, checked((int)corner.Actor!.AnchorX), checked((int)corner.Actor.AnchorY), true);
            }
        }
        app.ReleaseSimulatedPadForSelfTest();
        GD.Print("ARC-WORLD-003 bounded adjacent zone/Blocked/Unsupported/envelope actual app PASS; pixels=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS"));
        // ARC-RENDER-008 invalidation: a republish binds a fresh authority; a failure leaves no overlay cells.
        app.SetPassabilityOverlayForSelfTest(true); app.PassabilityOverlayFillForSelfTest();
        var previousOverlay = app.PassabilityOverlayLayerForSelfTest?.Overlay ?? throw new InvalidOperationException("ARC-RENDER-008 overlay not bound before republish");
        app.PublishSyntheticForSelfTest(fixture.Arguments,fixture.Result);
        app.PassabilityOverlayFillForSelfTest();
        if (app.PassabilityOverlayLayerForSelfTest?.Overlay is not { } republished || republished.Failure is not null || republished.CachedCells == 0
            || ReferenceEquals(republished, previousOverlay) || !ReferenceEquals(republished.Authority.Owner, app.WorldSessionForSelfTest))
            throw new InvalidOperationException("ARC-RENDER-008 overlay did not rebind after republish");
        var reloaded = app.GetSelfTestSnapshot();
        if (reloaded.CreatedTextures != 122 || reloaded.DisposedTextures != 61 || reloaded.CacheCount != 61)
            throw new InvalidOperationException("WORLD reload ownership failed.");
        app.PublishSyntheticFailureForSelfTest(TerrainViewerMode.WorldDiagnostic,new(TerrainViewerErrorCode.RenderFailure,Stage:TerrainViewerFailureStage.Upload));
        if (app.GetSelfTestSnapshot().DisposedTextures != 122 || app.GetSelfTestSnapshot().CacheCount != 0)
            throw new InvalidOperationException("WORLD failure ownership failed.");
        if (app.PassabilityOverlayLayerForSelfTest is { Overlay: not null } || !app.GetSelfTestSnapshot().Hud.Contains("failure stage=", StringComparison.Ordinal))
            throw new InvalidOperationException("ARC-RENDER-008 failure left overlay cells");
        app.SetPassabilityOverlayForSelfTest(false);
        GD.Print("ARC-RENDER-008 overlay invalidation PASS republish rebinds the authority; failure drops every overlay cell");
        app.ClearSyntheticForSelfTest(); viewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        // Inject failures independently at each uploaded domain in the SAME app.
        foreach (var failureAt in new[] { 1, 3, 8, 33, 61 })
        {
            var childViewport = new SubViewport { Size = new(800,600) };
            var failing = new TerrainViewerApp { SelfTestBootstrap = true }; failing.SetProcess(false);
            var attempt = 0;
            failing.ConfigureSelfTestTextureCache(image => ++attempt == failureAt ? throw new InvalidOperationException("owned test failure") : ImageTexture.CreateFromImage(image), texture => texture.Dispose());
            childViewport.AddChild(failing); host.AddChild(childViewport);
            try { failing.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result); } catch (InvalidOperationException) { }
            var failed = failing.GetSelfTestSnapshot();
            if (failed.CacheCount != 0 || failed.CreatedTextures != failureAt - 1 || failed.DisposedTextures != failureAt - 1)
                throw new InvalidOperationException("WORLD domain upload rollback failed.");
            childViewport.QueueFree(); await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("ARC-WORLD-001 integrated synthetic app PASS five statics/55 human actor/control matrix/resize/domain rollback; pixels=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS"));
    }
    // The ratio 14 former WORLD wheel-up events reached from the current view.
    private static double FormerWheelZoom(TerrainViewerApp app)
    {
        var zoom = app.WorldCameraZoomForSelfTest;
        for (var i = 0; i < 14; i++) zoom = WorldZoomPolicy.Step(zoom, 1.25);
        return zoom;
    }
}
