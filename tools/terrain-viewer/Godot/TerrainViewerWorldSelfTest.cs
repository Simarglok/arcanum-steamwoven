using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    internal static SyntheticActorLoad WorldFixture()
    {
        var fixture = SyntheticActorFixture.Create();
        var terrainKey = fixture.Result.Textures.Keys.Single();
        var terrain = TerrainScenePlan.CreateSynthetic(new(-2600, -100, 5200, 2800),
            [new(1, terrainKey, TerrainKind.Tile, 30, 32, 0, 200, 100, new(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0))]);
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic(Enumerable.Range(0, 5).Select(i =>
            new StaticSceneryPlanInput(i, 0, 0, 76, 1257, identity, 0, i, 0, 9, 7, 0, 0, 0, 0, false)));
        var textures = statics.Commands.ToDictionary(command => command.TextureKey,
            command => new DecodedStaticTexture(command.TextureKey, 9, 7, SolidRgba(9, 7, (byte)(100 + command.SourceIndex), 111, 222)));
        var input = DiagnosticActorMotion.ToPlacement(new DiagnosticActorMotion().Reset(), 0);
        var actor = fixture.Result.ActorPlan!.Select(input);
        var bounds = DiagnosticWorldBounds.Create(terrain, statics, actor);
        var result = fixture.Result with { Plan = terrain, StaticPlan = statics, StaticTextures = textures, ActorPlan = actor,
            DiagnosticPlan = DiagnosticScenePlan.CreateWorldDiagnostic(terrain, statics, actor, bounds), StaticCensusCount = 317, StaticOutsideScopeCount = 312 };
        var arguments = TerrainViewerArguments.Parse(["--mode", "world-diagnostic", "--module", "synthetic-module", "--resource", "synthetic-terrain",
            "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes", "--source-id", "synthetic"]);
        return new(arguments, result);
    }
    private static async Task AssertSyntheticWorldAppAsync(Node host)
    {
        var fixture = WorldFixture();
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true }; app.SetProcess(false);
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        var initial = app.GetSelfTestSnapshot();
        if (initial.CameraZoom != 2 || app.WorldFollowForSelfTest.Mode != GamepadLab.Core.CameraMode.Follow)
            throw new InvalidOperationException("WORLD default-2 ordinary ready framing failed.");
        var hudOrigin = initial.HudPosition;
        foreach (var ratio in new[] { 1d, 2d, 3d, 1.7 })
        {
            app.WorldZoomForSelfTest(ratio);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var framed = app.GetSelfTestSnapshot();
            var anchor = framed.CanvasTransform * new Vector2(80,1260);
            if (framed.CameraZoom != ratio || framed.HudPosition != hudOrigin || anchor.DistanceTo(new(400,300)) > 1
                || framed.Actor != initial.Actor || framed.Input != initial.Input)
                throw new InvalidOperationException("WORLD ratio/anchor/UI/geometry separation failed.");
            if (DisplayServer.GetName() != "headless")
            {
                using var image = viewport.GetTexture().GetImage();
                AssertTransformedPixel(image, framed.CanvasTransform, 77,1258,(231,37,19));
            }
        }
        app.WorldZoomForSelfTest(2);
        GD.Print("ARC-GPAD-004 default2/Follow actor framing exact1/2/3/fractional1.7 UI/geometry separation PASS; nearest public actor pixel=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS") + "; physical1:1/QHD NOT_VERIFIED");
        app.WorldControlMatrixForSelfTest();
        await app.WorldZoomMatrixForSelfTestAsync();
        app.WorldLifecycleMatrixForSelfTest();
        await app.WorldModalRoutingMatrixForSelfTestAsync();
        await app.WorldMouseSettingsMatrixForSelfTestAsync();
        var snapshot = app.GetSelfTestSnapshot();
        if (app.WorldStateForSelfTest.Position != new DiagnosticSectorPosition(30,32) || snapshot.CacheCount != 190
            || snapshot.Actor?.AnchorX != 80 || snapshot.Actor.AnchorY != 1260
            || !snapshot.Hud.Contains("collision not evaluated", StringComparison.Ordinal))
            throw new InvalidOperationException("WORLD literal combined publication failed.");
        // Test camera boundary uses the actual double-camera host; zoom then
        // resize must not derive actor position from any Godot transform.
        app.GetNode<Camera2D>("TerrainCamera").PositionSmoothingEnabled = false;
        var camera = app.GetNode<Camera2D>("TerrainCamera");
        // Preserve the historical sector/guide raster oracle from explicit Fit;
        // ordinary default framing is verified independently by the zoom matrix.
        app._Input(new InputEventKey { Keycode = Key.F, Pressed = true });
        for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(new(-1, default), Array.Empty<GamepadLab.Core.RawInputFrame>(), .1);
        // Zoom through the real world input route, away from UI.
        // At least two screen pixels/source pixel keep the last sparse source
        // column independently observable next to the 1px diagnostic outline.
        for (var i = 0; i < 14; i++) app._Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new(799,599) });
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        snapshot = app.GetSelfTestSnapshot();
        if (DisplayServer.GetName() != "headless")
        {
            using var image = viewport.GetTexture().GetImage();
            // Actor source (1,1)/(7,1) over statics. Transparent center keeps
            // the final equal-anchor static sourceIndex4 color, not terrain.
            AssertTransformedPixel(image, snapshot.CanvasTransform, 77,1258,(231,37,19));
            AssertTransformedPixel(image, snapshot.CanvasTransform, 83,1258,(24,218,236));
            AssertTransformedPixel(image, snapshot.CanvasTransform, 78,1258,(104,111,222));
            AssertGuideAt(image, snapshot.CanvasTransform,76,1257,false);
            AssertGuideAt(image, snapshot.CanvasTransform,80,1260,true);
        }
        var actorBefore = app.WorldStateForSelfTest;
        foreach (var size in new[] { new Vector2I(480,320), new(1280,800), new(800,600) })
        {
            viewport.Size = size; await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (app.WorldStateForSelfTest != actorBefore || app.GetSelfTestSnapshot().CacheCount != 190)
                throw new InvalidOperationException("WORLD resize mutated actor/cache.");
        }
        // Reach every declared sector corner through the actual motion/input
        // seam. The immutable envelope must retain complete sprite + guides,
        // and actual Camera2D rendering may not become authoritative state.
        var none = new GamepadLab.Core.RawInputFrame(-1, default);
        foreach (var (direction, x, y) in new[] {
            (new GamepadLab.Core.Vec2(0,-1),0d,0d), (new GamepadLab.Core.Vec2(1,0),0d,63d),
            (new GamepadLab.Core.Vec2(-1,0),63d,0d), (new GamepadLab.Core.Vec2(0,1),63d,63d) })
        {
            app._Input(new InputEventKey { Keycode = Key.R, Pressed = true });
            for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(none, Array.Empty<GamepadLab.Core.RawInputFrame>(), .1);
            app.WorldFrameForSelfTest(none with { KeyboardActor = direction, KeyboardActorHeld = true }, Array.Empty<GamepadLab.Core.RawInputFrame>(), 1000);
            var corner = app.GetSelfTestSnapshot();
            var bounds = fixture.Result.DiagnosticPlan!.WorldBounds;
            if (app.WorldStateForSelfTest.Position != new DiagnosticSectorPosition(x,y)
                || app.WorldStateForSelfTest.Action != ActorDiagnosticAction.Idle || corner.CacheCount != 190
                || corner.Actor!.Destination.X < bounds.X || corner.Actor.Destination.Y < bounds.Y
                || corner.Actor.Destination.X + corner.Actor.Destination.Width > bounds.X + bounds.Width
                || corner.Actor.Destination.Y + corner.Actor.Destination.Height > bounds.Y + bounds.Height)
                throw new InvalidOperationException("WORLD actual corner/envelope containment failed.");
            // Resolve the guide in the real combined viewport at each corner.
            // Fit can downsample a one-pixel guide; use the production zoom
            // route for a resolved guide, as for the asymmetric sprite oracle.
            app._Input(new InputEventKey { Keycode = Key.F, Pressed = true });
            for (var i = 0; i < 3; i++) app.WorldFrameForSelfTest(none, Array.Empty<GamepadLab.Core.RawInputFrame>(), .1);
            app.WorldFitFollowForSelfTest();
            for (var i = 0; i < 14; i++) app._Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new(799,599) });
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless")
            {
                corner = app.GetSelfTestSnapshot();
                using var image = viewport.GetTexture().GetImage();
                AssertGuideAt(image, corner.CanvasTransform, checked((int)corner.Actor!.AnchorX), checked((int)corner.Actor.AnchorY), true);
            }
        }
        GD.Print("ARC-WORLD-001 sector corners/envelope actual app PASS; pixels=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS"));
        app.PublishSyntheticForSelfTest(fixture.Arguments,fixture.Result);
        var reloaded = app.GetSelfTestSnapshot();
        if (reloaded.CreatedTextures != 380 || reloaded.DisposedTextures != 190 || reloaded.CacheCount != 190)
            throw new InvalidOperationException("WORLD reload ownership failed.");
        app.PublishSyntheticFailureForSelfTest(TerrainViewerMode.WorldDiagnostic,new(TerrainViewerErrorCode.RenderFailure,Stage:TerrainViewerFailureStage.Upload));
        if (app.GetSelfTestSnapshot().DisposedTextures != 380 || app.GetSelfTestSnapshot().CacheCount != 0)
            throw new InvalidOperationException("WORLD failure ownership failed.");
        app.ClearSyntheticForSelfTest(); viewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        // Inject failures independently at each uploaded domain in the SAME app.
        foreach (var failureAt in new[] { 1, 3, 8 })
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
        GD.Print("ARC-WORLD-001 integrated synthetic app PASS five statics/184 actor/control matrix/resize/domain rollback; pixels=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS"));
    }
}
