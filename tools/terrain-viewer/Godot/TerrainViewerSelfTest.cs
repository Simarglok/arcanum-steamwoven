using Arcanum.Data;
using Arcanum.Data.Terrain;
using Arcanum.Data.StaticObjects;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;
using System.Reflection;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    public static async void Run(Node host)
    {
        StageClock.Restart();
        try
        {
            // ARC-DEV-032: the self-test reads and writes no setting of the player.
            if (!ViewerAutomatedStorage.Automated || DiagnosticZoomSettingsStore.StoragePath.StartsWith("user://", StringComparison.Ordinal)
                || DiagnosticInputSettingsStore.StoragePath.StartsWith("user://", StringComparison.Ordinal))
                throw new InvalidOperationException("SELF_TEST_SETTINGS_NOT_ISOLATED");
            GD.Print("ARC-DEV-032 self-test PASS settings isolated (the zoom and input stores use no user:// file)");
            // ARC-RENDER-1355: a failed window publication logs the exception's type, message and first stack frames, not only its type name.
            try { new Dictionary<string, int>()["missing-key"] = new Dictionary<string, int>()["missing-key"]; }
            catch (KeyNotFoundException lookup)
            {
                var detail = TerrainViewerApp.FailureDetail(lookup, 3);
                if (!detail.StartsWith("KeyNotFoundException | KeyNotFoundException: ", StringComparison.Ordinal) || !detail.Contains("missing-key", StringComparison.Ordinal)
                    || !detail.Contains(" | at ", StringComparison.Ordinal) || detail.Split(" <- ").Length > 3)
                    throw new InvalidOperationException("STREAM_FAILURE_DETAIL " + detail);
                GD.Print("ARC-RENDER-1355 self-test PASS failure detail names the exception and its stack");
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_GRID_PROBE") == "1")
            {
                if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA_GRID_NATIVE_REQUIRED");
                Stage("AreaGrid");
                await AssertAreaGridAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OWNER_SEAM_PROBE") == "1")
            {
                if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA_SEAM_NATIVE_REQUIRED");
                Stage("AreaOwnerSeams");
                await AssertAreaOwnerSeamsAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OWNER_PROBE") == "1")
            {
                if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA_OWNER_NATIVE_REQUIRED");
                Stage("AreaOwner");
                await AssertAreaOwnerAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_COMPOSITION_PROBE") == "1")
            {
                if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA_COMPOSITION_NATIVE_REQUIRED");
                Stage("AreaCompositionMatrix");
                await AssertAreaCompositionMatrixAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_NEAREST_PROBE") == "1")
            {
                Stage("AreaNearestHoldouts");
                await AssertAreaNearestHoldoutsAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_WORLD_MAIN_SETTINGS_PROBE") == "1")
            {
                if (host is not TerrainViewerApp app) throw new InvalidOperationException("MAIN_SETTINGS_APP_REQUIRED");
                app.SelfTestBootstrap = true; app.SetProcess(false);
                var fixture = WorldFixture();
                app.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
                Stage("WorldMainSettings");
                var verified = await app.WorldMainSettingsForSelfTestAsync();
                app.ClearSyntheticForSelfTest();
                GD.Print("ARC-WORLD-003 main settings focused control " + (verified ? "PASS" : "NOT_VERIFIED") + "; historical cause UNKNOWN; physical usability NOT_VERIFIED");
                host.GetTree().Quit(verified ? 0 : 2);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_STABILITY_PROBE") == "1")
            {
                Stage("AreaIndexStability");
                await AssertAreaIndexStabilityAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_INDEX_PROBE") == "1")
            {
                Stage("AreaIndexMapping");
                await AssertAreaIndexMappingAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_PROBE") == "1")
            {
                Stage("ConversationPanel");
                await AssertConversationPanelAsync(host);
                await AssertConversationPanelArtFontsAsync(host);
                Stage("ConversationRouting");
                await AssertConversationRoutingAsync(host);
                if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT")))
                {
                    Stage("OriginalConversation");
                    await AssertOriginalConversationAsync(host);
                }
                host.GetTree().Quit(0);
                return;
            }
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_ADMISSION_PROBE") == "1")
            {
                Stage("AreaAdmission");
                await AssertAreaAdmissionAsync(host);
                host.GetTree().Quit(0);
                return;
            }
            AssertIndependentGeometryConstants();
            Stage("AreaRaster");
            await AssertAreaRasterAsync(host);
            var frame = ArtDecoder.Decode(SyntheticTerrainFixture.CreateArt(), new ArtFrameSelection(0, 0, 0));
            AssertFrameBytes(frame);
            var keys = CreateKeys();
            using var cache = new TerrainTextureCache();
            cache.Upload(CreateTextures(keys, frame));
            cache.UploadStatic(CreateStaticTextures(frame));
            cache.UploadActor(CreateActorTextures(frame));
            AssertUploadOwnership(frame, keys.Tile);
            AssertStaticUploadOwnership(frame);
            AssertActorUploadOwnership(frame);
            AssertAreaRetentionOrder();
            AssertReloadAndExitTextureOwnership(frame);
            AssertReadyStateRetainsOnlyHudMetrics();
            Stage("SyntheticProductionApp");
            await AssertSyntheticProductionAppAsync(host);
            Stage("SyntheticWorldApp");
            await AssertSyntheticWorldAppAsync(host);
            Stage("SyntheticWorldRetirement");
            await AssertSyntheticWorldRetirementAsync(host);
            Stage("AreaCanvas");
            await AssertAreaCanvasAsync(host);
            if (DisplayServer.GetName() != "headless") { Stage("AreaOwner"); await AssertAreaOwnerAsync(host); }
            Stage("AreaCompositionMatrix");
            await AssertAreaCompositionMatrixAsync(host);
            Stage("AreaLighting");
            await AssertAreaLightingAsync(host);
            Stage("AreaRingSwap");
            await AssertAreaRingSwapAsync(host);
            Stage("AreaLocalLighting");
            await AssertAreaLocalLightingAsync(host);
            Stage("HdPresentation");
            await AssertHdPresentationAsync(host);
            Stage("WorldHdPresentation");
            await AssertWorldHdPresentationAsync(host);
            AssertVisualProfileStore();
            Stage("HdToggle");
            await AssertHdToggleAsync(host);
            Stage("WorldHdToggle");
            await AssertWorldHdToggleAsync(host);
            Stage("InterfaceHd");
            await AssertInterfaceHdAsync(host);
            Stage("ConversationPanel");
            await AssertConversationPanelAsync(host);
            await AssertConversationPanelArtFontsAsync(host);
            Stage("ConversationRouting");
            await AssertConversationRoutingAsync(host);
            Stage("AreaHover");
            await AssertAreaHoverAsync(host);
            Stage("AreaInventory");
            await AssertAreaInventoryAsync(host);
            Stage("AreaSheet");
            await AssertAreaSheetAsync(host);
            Stage("AreaLogbook");
            await AssertAreaLogbookAsync(host);
            Stage("AreaSaveList");
            await AssertAreaSaveListAsync(host);
            Stage("AreaMainMenu");
            await AssertAreaMainMenuAsync(host);
            Stage("AreaSceneFlow");
            await AssertAreaSceneFlowAsync(host);
            Stage("AreaHeroDeath");
            await AssertAreaHeroDeathAsync(host);
            Stage("AreaOptions");
            await AssertAreaOptionsAsync(host);
            Stage("Launcher");
            await AssertLauncherAsync(host);
            Stage("AreaSchematics");
            await AssertAreaSchematicsAsync(host);
            Stage("AreaBarter");
            await AssertAreaBarterAsync(host);
            Stage("AreaReading");
            await AssertAreaReadingAsync(host);
            Stage("ViewerAudio");
            await AssertViewerAudioAsync(host);
            Stage("VisualToggleApp");
            await AssertVisualToggleAppAsync(host);

            if (DisplayServer.GetName() != "headless")
            {
                Stage("CanvasOracles");
                await AssertCanvasOraclesAsync(host, cache, keys);
                Stage("CombinedCanvasOracle");
                await AssertCombinedCanvasOracleAsync(host, cache, keys.Red);
                Stage("StaticRasterPolicies");
                await AssertStaticRasterPoliciesAsync(host, cache, keys.Red);
                GD.Print("ARC-RENDER-005 self-test PASS terrain/static/actor production-app pixel oracle");
            }
            else
            {
                // The dummy renderer cannot read a ViewportTexture. Plans and
                // source constants are still constructed, but no pixels are claimed.
                AssertPlanConstants(keys.Tile, keys.Facade);
                AssertCombinedPlan();
                AssertActorPlan();
                GD.Print("ARC-RENDER-005 self-test PASS terrain/static/actor structural plan; graphical readback NOT_VERIFIED headless");
            }
            host.GetTree().Quit(0);
            GD.Print("ARC-WORLD-001 self-test PASS combined scene/control/lifecycle; physical usability NOT_READY");
        }
        catch (Exception exception)
        {
            GD.PrintErr($"ARC-RENDER-003 self-test FAIL RenderFailure synthetic={exception.GetType().Name}:{exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    // ARC-DEV-043: each frame-bound stage names itself as it begins, with the time since the self-test began and whether
    // the window can draw and has focus, and the line is flushed at once (a release export does not flush each print), so a
    // run stopped by its timeout shows the stage it waited in.
    private static readonly System.Diagnostics.Stopwatch StageClock = new();

    internal static void Stage(string name)
    {
        GD.Print($"ARC-DEV-043 self-test stage {name} begin t={StageClock.ElapsedMilliseconds}ms"
                 + $" can-draw={DisplayServer.WindowCanDraw()} focused={DisplayServer.WindowIsFocused()}"
                 + $" forced-draws={HiddenWindowDrawKeeper.ForcedDraws}");
        if (OperatingSystem.IsWindows()) return;
        // A diagnostic line never changes the outcome: a missing libc only loses the flush.
        try { _ = FlushAllStreams(IntPtr.Zero); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fflush")]
    private static extern int FlushAllStreams(IntPtr stream);

    private static (TerrainTextureKey Tile, TerrainTextureKey Facade, TerrainTextureKey Red, TerrainTextureKey Green, TerrainTextureKey Blue) CreateKeys()
        => (new("synthetic", 1, 0, 1, "tile", 0, 0, 0), new("synthetic", 2, 0, 1, "facade", 0, 0, 0),
            new("synthetic", 3, 0, 1, "red", 0, 0, 0), new("synthetic", 4, 0, 1, "green", 0, 0, 0),
            new("synthetic", 5, 0, 1, "blue", 0, 0, 0));

    private static Dictionary<TerrainTextureKey, DecodedTerrainTexture> CreateTextures(
        (TerrainTextureKey Tile, TerrainTextureKey Facade, TerrainTextureKey Red, TerrainTextureKey Green, TerrainTextureKey Blue) keys, ArtFrame frame)
        => new()
        {
            [keys.Tile] = new(keys.Tile, frame.Width, frame.Height, frame.Rgba.ToArray()),
            [keys.Facade] = new(keys.Facade, frame.Width, frame.Height, frame.Rgba.ToArray()),
            [keys.Red] = Solid(keys.Red, 255, 0, 0),
            [keys.Green] = Solid(keys.Green, 0, 255, 0),
            [keys.Blue] = Solid(keys.Blue, 0, 0, 255)
        };

    private static DecodedTerrainTexture Solid(TerrainTextureKey key, byte r, byte g, byte b)
    {
        var rgba = new byte[SyntheticTerrainFixture.Width * SyntheticTerrainFixture.Height * 4];
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = r; rgba[offset + 1] = g; rgba[offset + 2] = b; rgba[offset + 3] = 255;
        }
        return new DecodedTerrainTexture(key, SyntheticTerrainFixture.Width, SyntheticTerrainFixture.Height, rgba);
    }

    private static Dictionary<StaticSceneryTextureKey, DecodedStaticTexture> CreateStaticTextures(ArtFrame frame)
    {
        var key = new StaticSceneryTextureKey(new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64)), 0, 0, 0);
        return new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture> { [key] = new(key, frame.Width, frame.Height, frame.Rgba.ToArray()) };
    }

    private static Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture> CreateActorTextures(ArtFrame frame)
    {
        var key = new ActorDiagnosticTextureKey(ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector, 7, 0, 1);
        return new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture> { [key] = new(key, frame.Width, frame.Height, frame.Rgba.ToArray()) };
    }

    private static async Task AssertCanvasOraclesAsync(Node host, TerrainTextureCache cache,
        (TerrainTextureKey Tile, TerrainTextureKey Facade, TerrainTextureKey Red, TerrainTextureKey Green, TerrainTextureKey Blue) keys)
    {
        // Each case is an isolated production canvas; expectations below are
        // literals and do not use TerrainGeometry or a command under test.
        using (var image = await RenderAsync(host, cache, FullTile(keys.Tile)))
        {
            AssertPixel(image, 1, 0, 0, 0, 0, 0);
            AssertPixel(image, 2, 0, 0, 0, 255, 255);
            AssertPixel(image, 78, 0, 255, 255, 255, 255);
            AssertPixel(image, 79, 0, 0, 0, 0, 0);
        }
        using (var image = await RenderAsync(host, cache, FullFacade(keys.Facade)))
        {
            AssertPixel(image, 1, 0, 0, 0, 0, 0);
            AssertPixel(image, 80, 0, 0, 255, 0, 255);
            AssertPixel(image, 81, 0, 0, 0, 0, 0);
        }
        using (var image = await RenderAsync(host, cache, CroppedTile(keys.Tile)))
        {
            AssertPixel(image, 3, 2, 255, 0, 0, 255);
            AssertPixel(image, 4, 2, 0, 255, 0, 255);
            AssertPixel(image, 2, 2, 0, 0, 0, 0);
            AssertPixel(image, 3, 37, 0, 0, 0, 0);
        }
        using (var image = await RenderAsync(host, cache, FlippedTile(keys.Tile)))
        {
            AssertPixel(image, 117, 20, 0, 0, 255, 255);
            AssertPixel(image, 118, 20, 0, 0, 0, 0);
        }
        using (var image = await RenderAsync(host, cache, LayerOverlap(keys.Red, keys.Green))) AssertPixel(image, 5, 5, 0, 255, 0, 255);
        using (var image = await RenderAsync(host, cache, YOverlap(keys.Red, keys.Green))) AssertPixel(image, 45, 25, 0, 255, 0, 255);
        using (var image = await RenderAsync(host, cache, XOverlap(keys.Red, keys.Blue))) AssertPixel(image, 5, 25, 0, 0, 255, 255);
        using (var image = await RenderAsync(host, cache, OrdinalOverlap(keys.Red, keys.Green))) AssertPixel(image, 5, 5, 0, 255, 0, 255);
    }

    private static async Task<Image> RenderAsync(Node host, TerrainTextureCache cache, TerrainScenePlan plan)
    {
        var viewport = new SubViewport { Size = new Vector2I(200, 100), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas();
        viewport.AddChild(canvas);
        host.AddChild(viewport);
        canvas.Publish(plan, cache);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        return image;
    }

    private static async Task AssertCombinedCanvasOracleAsync(Node host, TerrainTextureCache cache, TerrainTextureKey terrainKey)
    {
        var terrain = Plan(new TerrainRect(0, 0, 100, 60), [Input(30, terrainKey, TerrainKind.Tile, 0, 0, 0)]);
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic([new StaticSceneryPlanInput(4, 0, 0, 20, 20, identity, 0, 0, 0,
            SyntheticTerrainFixture.Width, SyntheticTerrainFixture.Height, 0, 0, 0, 0, false)]);
        var viewport = new SubViewport { Size = new Vector2I(100, 60), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas();
        viewport.AddChild(canvas); host.AddChild(viewport); canvas.Publish(DiagnosticScenePlan.Create(terrain, statics), cache);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        using var image = viewport.GetTexture().GetImage();
        AssertPixel(image, 20, 20, 255, 0, 0, 255);
        AssertPixel(image, 21, 20, 0, 0, 255, 255);
        viewport.QueueFree();
    }

    /// <summary>
    /// Drives a bootstrapped real TerrainViewerApp through its production ready
    /// publication, input and timeline paths. Expected state/pixel tables are
    /// literals, independent of the placement/facing implementation.
    /// </summary>
    private static async Task AssertSyntheticProductionAppAsync(Node host)
    {
        var viewport = new SubViewport { Size = new Vector2I(512, 300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app);
        host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.ShowActorGuidesForSelfTest(); // the actor pixel oracle checks the guides (ARC-DEV-064: debug mode only in play)
        var fixture = SyntheticActorFixture.Create();
        app.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await AssertResponsiveHudAsync(host, viewport, app);
        await AssertHiddenHudDefaultAsync(host);
        // The HUD matrix above validates its actual production overlay. Hide
        // that overlay only for later actor-pixel probes so a deliberately
        // top-anchored diagnostic panel cannot cover their world landmarks.
        app.GetNode<TerrainDiagnosticHud>("Hud").Visible = false;
        var expectedFacings = new[] { (0, false), (7, true), (6, true), (5, true), (4, false), (5, false), (6, false), (7, false) };
        var callerInput = fixture.Arguments.ActorInput!;
        foreach (var actionCase in new[]
        {
            (Key.Key1, ActorDiagnosticAction.Idle, 15, 8, SyntheticActorFixture.IdleLeftPixel, SyntheticActorFixture.IdleRightPixel),
            (Key.Key2, ActorDiagnosticAction.Walk, 8, 15, SyntheticActorFixture.WalkLeftPixel, SyntheticActorFixture.WalkRightPixel)
        })
        {
            app._Input(new InputEventKey { Keycode = actionCase.Item1, Pressed = true });
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var turns = 0; turns < 8 && app.GetSelfTestSnapshot().Actor?.RequestedFacing != 0; turns++)
                app._Input(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
            for (var requested = 0; requested < 8; requested++)
            {
                var snapshot = app.GetSelfTestSnapshot();
                AssertActorControlTransitionSnapshot(snapshot, callerInput, actionCase.Item2, requested, expectedFacings[requested].Item1,
                    expectedFacings[requested].Item2, actionCase.Item3, actionCase.Item4, frame: 0, playing: false);
                if (DisplayServer.GetName() != "headless")
                {
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                    using var image = viewport.GetTexture().GetImage();
                    AssertSyntheticActorPixels(image, snapshot, actionCase.Item5, actionCase.Item6);
                }
                if (requested < 7) app._Input(new InputEventKey { Keycode = Key.Bracketright, Pressed = true });
            }
        }

        // Walk's real input/timeline policy: step, play/advance, pause, then
        // switch reset to Idle. The immutable caller input stays unchanged.
        app._Input(new InputEventKey { Keycode = Key.Period, Pressed = true });
        var forwardStep = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(forwardStep, callerInput, ActorDiagnosticAction.Walk, 7, 7, false, 8, 15, frame: 1, playing: false);
        app._Input(new InputEventKey { Keycode = Key.Comma, Pressed = true });
        var reverseStep = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(reverseStep, callerInput, ActorDiagnosticAction.Walk, 7, 7, false, 8, 15, frame: 0, playing: false);
        app._Input(new InputEventKey { Keycode = Key.Space, Pressed = true });
        app._Process(0.1);
        var playbackAdvance = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(playbackAdvance, callerInput, ActorDiagnosticAction.Walk, 7, 7, false, 8, 15, frame: 1, playing: true);
        app._Input(new InputEventKey { Keycode = Key.Space, Pressed = true });
        app._Process(1.0);
        var pausedNonAdvance = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(pausedNonAdvance, callerInput, ActorDiagnosticAction.Walk, 7, 7, false, 8, 15, frame: 1, playing: false);
        app._Input(new InputEventKey { Keycode = Key.Key1, Pressed = true });
        var idleReset = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(idleReset, callerInput, ActorDiagnosticAction.Idle, 7, 7, false, 15, 8, frame: 0, playing: false);

        var beforeCamera = app.GetSelfTestSnapshot();
        app._Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        app._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        app._Input(new InputEventMouseMotion { Relative = new Vector2(20, -10) });
        app._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        var afterCamera = app.GetSelfTestSnapshot();
        AssertActorControlTransitionSnapshot(afterCamera, callerInput, ActorDiagnosticAction.Idle, 7, 7, false, 15, 8, frame: 0, playing: false);
        if (afterCamera.Actor?.AnchorX != beforeCamera.Actor?.AnchorX || afterCamera.Actor?.AnchorY != beforeCamera.Actor?.AnchorY
            || (afterCamera.CameraX, afterCamera.CameraY, afterCamera.CameraZoom) == (beforeCamera.CameraX, beforeCamera.CameraY, beforeCamera.CameraZoom)
            || afterCamera.CanvasTransform == beforeCamera.CanvasTransform)
            throw new InvalidOperationException("Inspection camera mutated stationary caller input or failed to update the canvas transform.");
        // The app-level lifecycle must use normal publication/clearing rather
        // than the cache in isolation: 1 terrain + 184 decoded actor textures.
        if (afterCamera.CacheCount != 185 || afterCamera.CreatedTextures != 185 || afterCamera.DisposedTextures != 0)
            throw new InvalidOperationException("Initial production-app texture ownership differs from the 185-texture fixture.");
        app.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        var republished = app.GetSelfTestSnapshot();
        if (republished.CacheCount != 185 || republished.CreatedTextures != 370 || republished.DisposedTextures != 185)
            throw new InvalidOperationException("Production-app republish did not replace exactly 185 textures.");
        app.PublishSyntheticFailureForSelfTest(TerrainViewerMode.ActorDiagnostic,
            new TerrainViewerError(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Upload));
        var cleared = app.GetSelfTestSnapshot();
        if (cleared.CacheCount != 0 || cleared.DisposedTextures != 370 || !cleared.Hud.Contains("failure stage=Upload code=RenderFailure", StringComparison.Ordinal))
            throw new InvalidOperationException("Production-app load failure did not clear its live 185 textures once.");
        app.ClearSyntheticForSelfTest();
        if (app.GetSelfTestSnapshot().DisposedTextures != 370)
            throw new InvalidOperationException("Repeated production-app clear double-disposed textures.");
        viewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        // Force an actor upload after terrain and one actor texture exist. The
        // normal PublishReadyResult failure path must roll both back through
        // PublishFailure without retaining an alternate test implementation.
        var failingViewport = new SubViewport { Size = new Vector2I(512, 300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var failingApp = new TerrainViewerApp { SelfTestBootstrap = true };
        var attempts = 0;
        failingApp.ConfigureSelfTestTextureCache(image =>
        {
            attempts++;
            if (attempts == 3) throw new InvalidOperationException("injected actor upload failure");
            return ImageTexture.CreateFromImage(image);
        }, texture => texture.Dispose());
        failingViewport.AddChild(failingApp); host.AddChild(failingViewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        try { failingApp.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result); }
        catch (InvalidOperationException) { }
        var failedUpload = failingApp.GetSelfTestSnapshot();
        if (failedUpload.CacheCount != 0 || failedUpload.CreatedTextures != 2 || failedUpload.DisposedTextures != 2)
            throw new InvalidOperationException("Production-app partial actor upload did not roll back exactly once.");
        failingApp.ClearSyntheticForSelfTest();
        if (failingApp.GetSelfTestSnapshot().DisposedTextures != 2)
            throw new InvalidOperationException("Repeated production failure clear double-disposed textures.");
        failingViewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        // Exercise live _ExitTree ownership before any clear; a second call is
        // idempotent and must not double-dispose the 185 textures.
        var exitViewport = new SubViewport { Size = new Vector2I(512, 300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var exitApp = new TerrainViewerApp { SelfTestBootstrap = true };
        exitViewport.AddChild(exitApp); host.AddChild(exitViewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        exitApp.PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        exitApp._ExitTree();
        var exited = exitApp.GetSelfTestSnapshot();
        if (exited.CacheCount != 0 || exited.CreatedTextures != 185 || exited.DisposedTextures != 185)
            throw new InvalidOperationException("Live production _ExitTree did not dispose exactly 185 textures.");
        exitApp._ExitTree();
        if (exitApp.GetSelfTestSnapshot().DisposedTextures != 185)
            throw new InvalidOperationException("Repeated production _ExitTree double-disposed textures.");
        exitViewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>ARC-DEV-052: outside the debug mode and automated runs the panel starts hidden; a failure stays on screen as a
    /// line of its own while it is hidden, and Ctrl+Shift+F10 shows and hides the panel.</summary>
    private static async Task AssertHiddenHudDefaultAsync(Node host)
    {
        var viewport = new SubViewport { Size = new Vector2I(512, 300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var hud = new TerrainDiagnosticHud { Name = "Hud" };
        viewport.AddChild(hud);
        host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        void Require(bool condition, string what) { if (!condition) throw new InvalidOperationException($"ARC-DEV-052 hidden HUD: {what}."); }
        void Chord(bool pressed) => hud._Input(new InputEventKey { Keycode = Key.F10, Pressed = pressed, CtrlPressed = true, ShiftPressed = true });
        Require(!hud.PanelShown && !hud.FailureLineShown, "the panel starts hidden with no failure line");
        hud.SetDiagnosticText("Diagnostic mode crash-site-area | ready", automatedTestMode: false);
        Require(!hud.PanelShown && !hud.FailureLineShown, "a status shows neither the panel nor a failure line");
        const string failure = "Diagnostic mode unresolved | failure stage=GameFolder code=StartFailed";
        hud.SetDiagnosticText(failure, automatedTestMode: false, failure: true);
        Require(hud.FailureLineShown && hud.FailureLineText == failure && !hud.PanelShown, "a failure shows its line, not the panel");
        Chord(true); Chord(false);
        Require(hud.PanelShown && !hud.FailureLineShown && hud.Text.Contains(failure, StringComparison.Ordinal),
            "Ctrl+Shift+F10 shows the panel with the failure in it, the separate line gone");
        Chord(true); Chord(false);
        Require(!hud.PanelShown && hud.FailureLineShown, "Ctrl+Shift+F10 hides the panel again and the failure line returns");
        hud.SetDiagnosticText("Diagnostic mode crash-site-area | ready", automatedTestMode: false);
        Require(!hud.FailureLineShown, "a later status clears the failure line");
        hud.ShowPanel("self-test");
        Require(hud.PanelShown, "ShowPanel shows the panel");
        viewport.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async Task AssertResponsiveHudAsync(Node host, SubViewport viewport, TerrainViewerApp app)
    {
        var hud = app.GetNode<TerrainDiagnosticHud>("Hud");
        hud.AddOverflowProbeForSelfTest();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var invariant = app.GetSelfTestSnapshot();
        foreach (var size in new[] { new Vector2I(480, 320), new Vector2I(640, 360), new Vector2I(800, 500), new Vector2I(1280, 800) })
        {
            viewport.Size = size;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var rect = hud.Rect;
            if (hud.GlobalPosition.X < 16 || hud.GlobalPosition.Y < 16 || hud.Height > (size.Y * 0.35f) + 1
                || rect.Position.X < 0 || rect.Position.Y < 0 || rect.End.X > size.X || rect.End.Y > size.Y)
                throw new InvalidOperationException($"Diagnostic HUD escaped its 16px/35% viewport bounds at {size}.");
            var scrollBefore = hud.VerticalScrollValue;
            hud.MoveVerticalScroll();
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var scrollMoved = !Mathf.IsEqualApprox(scrollBefore, hud.VerticalScrollValue);
            if (!hud.IsScrollable || !hud.HasVerticalOverflow || !scrollMoved || !hud.DetailWraps
                || !hud.Text.Contains("AUTOMATED TEST MODE", StringComparison.Ordinal)
                || !hud.Text.Contains("Mode: actor-diagnostic", StringComparison.Ordinal)
                || !hud.Text.Contains("Original placement parity: NOT VERIFIED", StringComparison.Ordinal)
                || !hud.StickyVisible || hud.HasHorizontalClipping)
                throw new InvalidOperationException($"Diagnostic HUD lost its sticky limits or scrollable detail contract at {size}: configured={hud.IsScrollable} overflow={hud.HasVerticalOverflow} moved={scrollMoved} scroll={scrollBefore}/{hud.VerticalScrollValue}/{hud.VerticalScrollMaximum}/{hud.VerticalScrollPage} wraps={hud.DetailWraps} sticky={hud.StickyVisible} clip={hud.HasHorizontalClipping}.");
            var after = app.GetSelfTestSnapshot();
            var expectedCanvas = new Transform2D(invariant.CanvasTransform.X, invariant.CanvasTransform.Y,
                invariant.CanvasTransform.Origin + new Vector2((size.X - 512) / 2f, (size.Y - 300) / 2f));
            if (after.Actor != invariant.Actor || !HasStationaryCallerInput(after.Input, invariant.Input!)
                || after.CacheCount != invariant.CacheCount || after.CameraX != invariant.CameraX
                || after.CameraY != invariant.CameraY || after.CameraZoom != invariant.CameraZoom
                || !SameTransform(after.CanvasTransform, expectedCanvas))
                throw new InvalidOperationException("Viewport resize mutated renderer, actor input, or published scene state.");
        }
    }

    private static bool SameTransform(Transform2D actual, Transform2D expected)
        => Mathf.IsEqualApprox(actual.X.X, expected.X.X) && Mathf.IsEqualApprox(actual.X.Y, expected.X.Y)
            && Mathf.IsEqualApprox(actual.Y.X, expected.Y.X) && Mathf.IsEqualApprox(actual.Y.Y, expected.Y.Y)
            && Mathf.IsEqualApprox(actual.Origin.X, expected.Origin.X) && Mathf.IsEqualApprox(actual.Origin.Y, expected.Origin.Y);

    private static bool HasCompleteActorHud(TerrainViewerSelfTestSnapshot snapshot, ActorDiagnosticAction action, int facing, int group,
        bool mirrored, int frameCount, int fps, int frame, bool playing)
    {
        var actionName = action == ActorDiagnosticAction.Idle ? "Idle" : "Walk";
        var actionNumber = action == ActorDiagnosticAction.Idle ? 0 : 1;
        var playback = playing ? "playing" : "paused";
        var mirror = mirrored ? "yes" : "no";
        var required = new[]
        {
            "Diagnostic mode actor-diagnostic", "terrain 4096/3033/1063", "static not requested", "source-id synthetic", "zoom ",
            "terrain decoded 1", "static decoded 0", "cache 185", "Actor anchor policy: caller-selected diagnostic",
            "Actor render state: caller-supplied normal-uncached | scale 100 | shrunk false", $"Actor: {actionName} (source action {actionNumber})",
            $"Facing: requested {facing} -> physical group {group} | mirror {mirror}", $"Frame: {frame}/{frameCount}",
            $"Playback: {playback} | raw ART FPS preview {fps} (not source scheduler/gameplay)", "World position: stationary",
            "Layering: diagnostic terrain -> actor -> guides", "Original placement parity: NOT VERIFIED",
            "Controls: 1 Idle | 2 Walk | [ ] facing | Space play/pause | , . step"
        };
        return required.All(item => snapshot.Hud.Contains(item, StringComparison.Ordinal));
    }

    /// <summary>
    /// One post-transition production snapshot must independently prove the
    /// complete HUD policy and fixed caller/world placement. These literals are
    /// deliberately not derived from the production planner or HUD formatter.
    /// </summary>
    private static void AssertActorControlTransitionSnapshot(TerrainViewerSelfTestSnapshot snapshot, ActorDiagnosticPlacementInput callerInput,
        ActorDiagnosticAction action, int facing, int group, bool mirrored, int frameCount, int fps, int frame, bool playing)
    {
        var actor = snapshot.Actor ?? throw new InvalidOperationException("Synthetic app did not publish an actor command.");
        if (actor.Action != action || actor.RequestedFacing != facing || actor.PhysicalGroup != group || actor.FlipX != mirrored
            || actor.Frame != frame || actor.FrameCount != frameCount || actor.RawFramesPerSecond != fps || snapshot.Playing != playing
            || snapshot.CacheCount != 185 || snapshot.HudPosition != new Vector2(16, 16)
            || !HasStationaryCallerInput(snapshot.Input, callerInput)
            || actor.Destination != new TerrainRect(96, 57, 9, 7) || actor.AnchorX != 100 || actor.AnchorY != 60
            || !HasCompleteActorHud(snapshot, action, facing, group, mirrored, frameCount, fps, frame, playing))
            throw new InvalidOperationException("Synthetic app control transition diverges from the literal HUD or stationary-placement policy.");
    }

    private static bool HasStationaryCallerInput(ActorDiagnosticPlacementInput? actual, ActorDiagnosticPlacementInput expected)
        => actual is not null && actual.MapX == expected.MapX && actual.MapY == expected.MapY
            && actual.CameraOriginX == expected.CameraOriginX && actual.CameraOriginY == expected.CameraOriginY
            && actual.InstanceOffsetX == expected.InstanceOffsetX && actual.InstanceOffsetY == expected.InstanceOffsetY
            && actual.RenderBranch == expected.RenderBranch && actual.ScalePercent == expected.ScalePercent && actual.Shrunk == expected.Shrunk
            && actual.Width == expected.Width && actual.Height == expected.Height && actual.HotX == expected.HotX && actual.HotY == expected.HotY
            && actual.ArtOffsetX == expected.ArtOffsetX && actual.ArtOffsetY == expected.ArtOffsetY;

    private static void AssertSyntheticActorPixels(Image image, TerrainViewerSelfTestSnapshot snapshot,
        (byte R, byte G, byte B) leftExpected, (byte R, byte G, byte B) rightExpected)
    {
        // Fixture literals do not read the produced command to establish actor
        // placement: caller input fixes destination (96,57), anchor (100,60),
        // and source pixels (1,1)/(7,1) for the decoded 9x7 ART.
        var actor = snapshot.Actor ?? throw new InvalidOperationException("Synthetic app did not publish an actor command.");
        if (actor.Destination != new TerrainRect(96, 57, 9, 7) || actor.AnchorX != 100 || actor.AnchorY != 60)
            throw new InvalidOperationException("Synthetic actor placement differs from the literal fixture oracle.");
        AssertTransformedPixel(image, snapshot.CanvasTransform, actor.FlipX ? 103 : 97, 58, leftExpected);
        AssertTransformedPixel(image, snapshot.CanvasTransform, actor.FlipX ? 97 : 103, 58, rightExpected);
        AssertTransformedPixel(image, snapshot.CanvasTransform, 10, 10, (41, 82, 143));
        AssertGuideAt(image, snapshot.CanvasTransform, 96, 57, false);
        AssertGuideAt(image, snapshot.CanvasTransform, 100, 60, true);
    }

    private static void AssertTransformedPixel(Image image, Transform2D transform, int worldX, int worldY, (byte R, byte G, byte B) expected)
    {
        var point = transform * new Vector2(worldX, worldY);
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        for (var sampleY = y - 1; sampleY <= y + 1; sampleY++)
        for (var sampleX = x - 1; sampleX <= x + 1; sampleX++)
        {
            if (sampleX < 0 || sampleY < 0 || sampleX >= image.GetWidth() || sampleY >= image.GetHeight()) continue;
            var pixel = image.GetPixel(sampleX, sampleY);
            if (Math.Abs(Math.Round(pixel.R * 255) - expected.R) <= 1 && Math.Abs(Math.Round(pixel.G * 255) - expected.G) <= 1
                && Math.Abs(Math.Round(pixel.B * 255) - expected.B) <= 1 && Math.Round(pixel.A * 255) == 255) return;
        }
        throw new InvalidOperationException($"Synthetic literal pixel oracle failed at world=({worldX},{worldY}) screen=({x},{y}) expected={expected} actual={image.GetPixel(Math.Clamp(x,0,image.GetWidth()-1),Math.Clamp(y,0,image.GetHeight()-1))}.");
    }

    private static void AssertGuideAt(Image image, Transform2D transform, int worldX, int worldY, bool yellow)
    {
        var point = transform * new Vector2(worldX, worldY);
        if (!HasSyntheticGuideColor(image, new Vector2I((int)Math.Round(point.X), (int)Math.Round(point.Y)), yellow))
            throw new InvalidOperationException(yellow ? "Synthetic yellow anchor oracle failed." : "Synthetic magenta rectangle oracle failed.");
    }

    private static bool HasSyntheticGuideColor(Image image, Vector2I center, bool yellow)
    {
        for (var y = center.Y - 5; y <= center.Y + 5; y++)
        for (var x = center.X - 5; x <= center.X + 5; x++)
        {
            if (x < 0 || y < 0 || x >= image.GetWidth() || y >= image.GetHeight()) continue;
            var pixel = image.GetPixel(x, y);
            if (yellow ? pixel.R > .9f && pixel.G > .9f && pixel.B < .2f : pixel.R > .9f && pixel.G < .2f && pixel.B > .9f)
                return true;
        }
        return false;
    }

    private static async Task AssertActorCanvasOracleAsync(Node host, TerrainTextureCache cache, TerrainTextureKey terrainKey)
    {
        var terrain = Plan(new TerrainRect(0, 0, 200, 100), [Input(51, terrainKey, TerrainKind.Tile, 0, 0, 0)]);
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector, 7, 0, 1),
            SyntheticTerrainFixture.Width, SyntheticTerrainFixture.Height, 0, 0, 3, -2, ActorDiagnosticSource.IdleFrames, ActorDiagnosticSource.IdleFps);
        var input = new ActorDiagnosticPlacementInput(0, 0, 100, 20, 0, 0, ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            ActorDiagnosticAction.Idle, 1, 0, SyntheticTerrainFixture.Width, SyntheticTerrainFixture.Height, 0, 0, 3, -2);
        var actor = ActorDiagnosticScenePlan.CreateSynthetic(input, [frame]);
        var viewport = new SubViewport { Size = new Vector2I(200, 100), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas { ShowActorGuides = true };
        viewport.AddChild(canvas); host.AddChild(viewport); canvas.Publish(DiagnosticScenePlan.Create(terrain, null, actor), cache);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        using var image = viewport.GetTexture().GetImage();
        AssertPixel(image, 98, 42, 255, 0, 0, 255); // asymmetric source x=2 reflects to width-1-2.
        AssertPixel(image, 100, 39, 255, 255, 0, 255); // anchor guide paints last.
        viewport.QueueFree();
    }

    private static async Task AssertStaticRasterPoliciesAsync(Node host, TerrainTextureCache cache, TerrainTextureKey terrainKey)
    {
        // Literal asymmetric RGBA keeps this oracle independent of the ART
        // fixture's layout.  It exercises the production static canvas pass:
        // transparent source pixels retain terrain, X flip swaps only X, and
        // equal anchors paint the larger source-index command last.
        var identity = new StaticObjectResourceIdentity(31, 0, 6, new string('e', 64));
        var asymmetric = new StaticSceneryTextureKey(identity, 0, 0, 0);
        var overlapFirst = new StaticSceneryTextureKey(identity, 0, 1, 0);
        var overlapLast = new StaticSceneryTextureKey(identity, 0, 2, 0);
        cache.UploadStatic(new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>
        {
            [asymmetric] = new(asymmetric, 3, 2, Rgba(
                (0, 0, 0, 0), (10, 20, 30, 255), (40, 50, 60, 255),
                (70, 80, 90, 255), (100, 110, 120, 255), (130, 140, 150, 255))),
            [overlapFirst] = new(overlapFirst, 3, 2, SolidRgba(3, 2, 1, 2, 3)),
            [overlapLast] = new(overlapLast, 3, 2, SolidRgba(3, 2, 200, 201, 202))
        });
        var terrain = Plan(new TerrainRect(0, 0, 100, 60), [Input(40, terrainKey, TerrainKind.Tile, 0, 0, 0)]);
        var unflipped = StaticSceneryScenePlan.CreateSynthetic([StaticInput(4, 10, 10, identity, 0, false)]);
        using (var image = await RenderDiagnosticAsync(host, cache, terrain, unflipped))
        {
            AssertPixel(image, 10, 10, 255, 0, 0, 255); // transparent static leaves terrain.
            AssertPixel(image, 11, 10, 10, 20, 30, 255);
            AssertPixel(image, 12, 10, 40, 50, 60, 255);
            AssertPixel(image, 10, 11, 70, 80, 90, 255);
        }
        var flipped = StaticSceneryScenePlan.CreateSynthetic([StaticInput(4, 10, 10, identity, 0, true)]);
        using (var image = await RenderDiagnosticAsync(host, cache, terrain, flipped))
        {
            AssertPixel(image, 10, 10, 40, 50, 60, 255);
            AssertPixel(image, 11, 10, 10, 20, 30, 255);
            AssertPixel(image, 12, 10, 255, 0, 0, 255); // same rectangle, reversed X only.
            AssertPixel(image, 10, 11, 130, 140, 150, 255); // no Y flip.
        }
        var overlap = StaticSceneryScenePlan.CreateSynthetic([
            StaticInput(7, 30, 10, identity, 1, false), StaticInput(9, 30, 10, identity, 2, false)]);
        using var overlapImage = await RenderDiagnosticAsync(host, cache, terrain, overlap);
        AssertPixel(overlapImage, 31, 10, 200, 201, 202, 255);
    }

    private static StaticSceneryPlanInput StaticInput(int sourceIndex, int offsetX, int offsetY, StaticObjectResourceIdentity identity, int frame, bool flipX)
        => new(sourceIndex, 0, 0, offsetX, offsetY, identity, 0, frame, 0, 3, 2, 0, 0, 0, 0, flipX);

    private static byte[] Rgba(params (byte R, byte G, byte B, byte A)[] pixels)
        => pixels.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A }).ToArray();

    private static byte[] SolidRgba(int width, int height, byte r, byte g, byte b)
        => Rgba(Enumerable.Repeat((r, g, b, (byte)255), checked(width * height)).ToArray());

    private static async Task<Image> RenderDiagnosticAsync(Node host, TerrainTextureCache cache, TerrainScenePlan terrain, StaticSceneryScenePlan statics)
    {
        var viewport = new SubViewport { Size = new Vector2I(100, 60), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas();
        viewport.AddChild(canvas); host.AddChild(viewport);
        canvas.Publish(DiagnosticScenePlan.Create(terrain, statics), cache);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        return image;
    }

    private static void AssertCombinedPlan()
    {
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic([new StaticSceneryPlanInput(4, 0, 0, 20, 20, identity, 0, 0, 0, 3, 3, 0, 0, 0, 0, false)]);
        if (statics.Commands.Single().Destination != new TerrainRect(20, 20, 3, 3)) throw new InvalidOperationException("Combined static geometry differs from literal oracle.");
    }

    private static void AssertActorPlan()
    {
        var frame = new ActorDiagnosticFrame(new ActorDiagnosticTextureKey(ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector, 7, 0, 1),
            3, 2, 0, 0, 8, -1, ActorDiagnosticSource.IdleFrames, ActorDiagnosticSource.IdleFps);
        var plan = ActorDiagnosticScenePlan.CreateSynthetic(new ActorDiagnosticPlacementInput(0, 0, 10, 20, 0, 0,
            ActorDiagnosticRenderBranch.NormalUncached, 100, false, ActorDiagnosticAction.Idle, 1, 0, 3, 2, 0, 0, 8, -1), [frame]);
        if (!plan.Current.FlipX || plan.Current.Destination != new TerrainRect(8, 40, 3, 2) || plan.Current.EffectiveOffsetX != -8)
            throw new InvalidOperationException("Actor mirror geometry differs from literal structural oracle.");
    }

    private static TerrainScenePlan FullTile(TerrainTextureKey key) => Plan(new TerrainRect(0, 0, 100, 60), [Input(10, key, TerrainKind.Tile, 0, 0, 0)]);
    private static TerrainScenePlan FullFacade(TerrainTextureKey key) => Plan(new TerrainRect(0, 0, 100, 60), [Input(11, key, TerrainKind.Facade, 0, 0, 0xB00000C1)]);
    private static TerrainScenePlan CroppedTile(TerrainTextureKey key) => Plan(new TerrainRect(3, 2, 74, 35), [Input(12, key, TerrainKind.Tile, 0, 0, 0)]);
    private static TerrainScenePlan FlippedTile(TerrainTextureKey key) => Plan(new TerrainRect(0, 0, 150, 80), [Input(13, key, TerrainKind.Tile, 0, 1, 0xC1)]);
    private static TerrainScenePlan LayerOverlap(TerrainTextureKey red, TerrainTextureKey green) => Plan(new TerrainRect(0, 0, 100, 60), [Input(14, red, TerrainKind.Tile, 0, 0, 0), Input(15, green, TerrainKind.Facade, 0, 0, 0)]);
    private static TerrainScenePlan YOverlap(TerrainTextureKey red, TerrainTextureKey green) => Plan(new TerrainRect(0, 0, 150, 80), [Input(16, red, TerrainKind.Tile, 0, 0, 0), Input(17, green, TerrainKind.Tile, 0, 1, 0)]);
    private static TerrainScenePlan XOverlap(TerrainTextureKey red, TerrainTextureKey blue) => Plan(new TerrainRect(-50, 0, 200, 80), [Input(18, red, TerrainKind.Tile, 0, 0, 0), Input(19, blue, TerrainKind.Tile, 1, 0, 0)]);
    private static TerrainScenePlan OrdinalOverlap(TerrainTextureKey red, TerrainTextureKey green) => Plan(new TerrainRect(0, 0, 100, 60), [Input(20, red, TerrainKind.Tile, 0, 0, 0), Input(21, green, TerrainKind.Tile, 0, 0, 0)]);

    private static TerrainPlanInput Input(int ordinal, TerrainTextureKey key, TerrainKind kind, int x, int y, uint rawId)
        => new(ordinal, key, kind, x, y, rawId, SyntheticTerrainFixture.Width, SyntheticTerrainFixture.Height,
            new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 99, -99, -99));
    private static TerrainScenePlan Plan(TerrainRect bounds, IEnumerable<TerrainPlanInput> inputs) => TerrainScenePlan.CreateSynthetic(bounds, inputs);

    private static void AssertPlanConstants(TerrainTextureKey tile, TerrainTextureKey facade)
    {
        var full = FullTile(tile).Commands.Single();
        var facadeCommand = FullFacade(facade).Commands.Single();
        var crop = CroppedTile(tile).Commands.Single();
        var flip = FlippedTile(tile).Commands.Single();
        if (full.Source != new TerrainRect(0, 0, 78, 40) || full.Destination != new TerrainRect(1, 0, 78, 40)
            || facadeCommand.Source != new TerrainRect(0, 0, 80, 40) || facadeCommand.Destination != new TerrainRect(1, 0, 80, 40)
            || crop.Source != new TerrainRect(2, 2, 74, 35) || crop.Destination != new TerrainRect(3, 2, 74, 35)
            || !flip.FlipX) throw new InvalidOperationException("Production placement differs from literal oracle constants.");
    }

    private static void AssertIndependentGeometryConstants()
    {
        var key = new TerrainTextureKey("constants", 1, 0, 1, "constants", 0, 0, 0);
        // The pre-anchor values are literal independent expectations. The
        // production projection and production placement are checked in two
        // separate assertions so an accidental anchor change cannot mask a
        // projection error (especially the documented 30,32 -> 80,1240).
        var points = new[] { (0, 0, 0, 0), (1, 0, -40, 20), (0, 1, 40, 20), (63, 0, -2520, 1260), (0, 63, 2520, 1260), (63, 63, 0, 2520), (30, 32, 80, 1240) };
        var plan = Plan(new TerrainRect(-2600, -1, 5200, 2600), points.Select((point, ordinal) => Input(ordinal, key, TerrainKind.Tile, point.Item1, point.Item2, 0)));
        foreach (var point in points)
        {
            var command = plan.Commands.Single(candidate => candidate.Order.LocalX == point.Item1 && candidate.Order.LocalY == point.Item2);
            var projected = TerrainGeometry.Project(point.Item1, point.Item2);
            if (projected.X != point.Item3 || projected.Y != point.Item4
                || command.Destination.X != point.Item3 + 1 || command.Destination.Y != point.Item4
                || command.Source.Width != 78 || command.Source.Height != 40)
                throw new InvalidOperationException("Projection, +1 anchor, or tile footprint mismatch.");
        }
    }

    private static void AssertFrameBytes(ArtFrame frame)
    {
        if (frame.Width != SyntheticTerrainFixture.Width || frame.Height != SyntheticTerrainFixture.Height || frame.Rgba.Count != SyntheticTerrainFixture.Width * SyntheticTerrainFixture.Height * 4)
            throw new InvalidOperationException("Fixture dimensions changed.");
        AssertRgba(frame.Rgba, 0, 0, 0, 0, 0, 0);
        AssertRgba(frame.Rgba, 1, 0, 0, 0, 255, 255);
        AssertRgba(frame.Rgba, 79, 0, 0, 255, 0, 255);
        AssertRgba(frame.Rgba, 2, 2, 255, 0, 0, 255);
    }

    private static void AssertUploadOwnership(ArtFrame frame, TerrainTextureKey key)
    {
        var attempts = 0;
        var disposals = 0;
        using var cache = new TerrainTextureCache(image =>
        {
            attempts++;
            if (attempts == 2) throw new InvalidOperationException("injected upload failure");
            return ImageTexture.CreateFromImage(image);
        }, _ => disposals++);
        var second = new TerrainTextureKey("synthetic", 99, 0, 1, "second", 0, 0, 0);
        try { cache.Upload(new Dictionary<TerrainTextureKey, DecodedTerrainTexture> { [key] = new(key, frame.Width, frame.Height, frame.Rgba.ToArray()), [second] = new(second, frame.Width, frame.Height, frame.Rgba.ToArray()) }); }
        catch (InvalidOperationException) { }
        if (cache.Count != 0 || disposals != 1) throw new InvalidOperationException("Partial texture upload was not rolled back exactly once.");
        cache.Clear();
        if (disposals != 1) throw new InvalidOperationException("Repeated cache clear disposed an already rolled-back texture.");
    }

    // Area frame LRU: frames that leave the view are evicted least recently used first
    // once the frame or byte allowance is exceeded, and a new repository releases all.
    private static void AssertAreaRetentionOrder()
    {
        var disposed = new List<ImageTexture>();
        AreaFrameKey Key(int frame) => new(null!, 0, 0, frame, "synthetic-retention", "", 0, "");
        var repository = Enumerable.Range(0, 4).ToDictionary(Key, _ => new DecodedAreaFrame(2, 2, new byte[16]));
        foreach (var (frames, bytes) in new[] { (1, long.MaxValue), (int.MaxValue, 16L) })
        {
            disposed.Clear();
            using var cache = new TerrainTextureCache(ImageTexture.CreateFromImage, texture => { disposed.Add(texture); texture.Dispose(); }) { AreaRetainFrames = frames, AreaRetainBytes = bytes };
            void Show(params int[] keys) => cache.UploadAreaVisible(repository, keys.Select(Key).ToHashSet());
            bool Has(int frame) => cache.TryGet(Key(frame), out _);
            Show(0); Show(1); Show(2);
            if (Has(0) || !Has(1) || !Has(2) || disposed.Count != 1 || cache.AreaCount != 2
                || cache.AreaVisibleFrames != 1 || cache.AreaVisibleBytes != 16 || cache.AreaRgbaBytes != 32)
                throw new InvalidOperationException("AREA retention did not evict the least recently used frame.");
            Show(1, 3);
            if (Has(0) || !Has(1) || !Has(2) || !Has(3) || disposed.Count != 1 || cache.AreaRgbaBytes != 48)
                throw new InvalidOperationException("AREA retention lost a frame within its allowance.");
            Show(3);
            if (Has(2) || !Has(1) || !Has(3) || disposed.Count != 2)
                throw new InvalidOperationException("AREA retention evicted out of order.");
            Show(0);
            if (!Has(0) || Has(1) || !Has(3) || disposed.Count != 3 || cache.AreaCount != 2)
                throw new InvalidOperationException("AREA retention kept frames beyond its allowance.");
            var replacement = repository.ToDictionary(pair => pair.Key, pair => pair.Value);
            cache.UploadAreaVisible(replacement, new HashSet<AreaFrameKey> { Key(0) });
            if (disposed.Count != 5 || cache.AreaCount != 1 || cache.AreaRgbaBytes != 16)
                throw new InvalidOperationException("AREA repository change kept another repository's textures.");
        }
        foreach (var invalid in new Func<TerrainTextureCache>[] { () => new() { AreaRetainFrames = -1 }, () => new() { AreaRetainBytes = -1 } })
        {
            try { using var rejected = invalid(); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("AREA retention accepted a negative allowance.");
        }
    }

    private static void AssertStaticUploadOwnership(ArtFrame frame)
    {
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('d', 64));
        var first = new StaticSceneryTextureKey(identity, 0, 0, 0);
        var second = new StaticSceneryTextureKey(identity, 0, 1, 0);
        var attempts = 0;
        var disposals = 0;
        using var cache = new TerrainTextureCache(image =>
        {
            attempts++;
            if (attempts == 2) throw new InvalidOperationException("injected static upload failure");
            return ImageTexture.CreateFromImage(image);
        }, _ => disposals++);
        try
        {
            cache.UploadStatic(new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>
            {
                [first] = new(first, frame.Width, frame.Height, frame.Rgba.ToArray()),
                [second] = new(second, frame.Width, frame.Height, frame.Rgba.ToArray())
            });
        }
        catch (InvalidOperationException) { }
        if (cache.Count != 0 || disposals != 1) throw new InvalidOperationException("Partial static texture upload was not rolled back exactly once.");
        cache.Clear();
        if (disposals != 1) throw new InvalidOperationException("Repeated static cache clear disposed an already rolled-back texture.");
    }

    private static void AssertActorUploadOwnership(ArtFrame frame)
    {
        var first = new ActorDiagnosticTextureKey(ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector, 0, 0, 1);
        var second = new ActorDiagnosticTextureKey(ActorDiagnosticSource.WalkResourceOrdinal, ActorDiagnosticSource.WalkSelector, 0, 0, 1);
        var attempts = 0;
        var disposals = 0;
        using var cache = new TerrainTextureCache(image =>
        {
            attempts++;
            if (attempts == 2) throw new InvalidOperationException("injected actor upload failure");
            return ImageTexture.CreateFromImage(image);
        }, _ => disposals++);
        try
        {
            cache.UploadActor(new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>
            {
                [first] = new(first, frame.Width, frame.Height, frame.Rgba.ToArray()),
                [second] = new(second, frame.Width, frame.Height, frame.Rgba.ToArray())
            });
        }
        catch (InvalidOperationException) { }
        if (cache.Count != 0 || disposals != 1) throw new InvalidOperationException("Partial actor texture upload was not rolled back exactly once.");
        cache.Clear();
        if (disposals != 1) throw new InvalidOperationException("Repeated actor cache clear disposed an already rolled-back texture.");
    }

    private static void AssertReloadAndExitTextureOwnership(ArtFrame frame)
    {
        var first = new TerrainTextureKey("synthetic", 100, 0, 1, "reload-first", 0, 0, 0);
        var second = new TerrainTextureKey("synthetic", 101, 0, 1, "reload-second", 0, 0, 0);
        var created = new List<ImageTexture>();
        var disposals = new Dictionary<ImageTexture, int>(ReferenceEqualityComparer.Instance);
        Func<Image, ImageTexture> create = image =>
        {
            var texture = ImageTexture.CreateFromImage(image);
            created.Add(texture);
            return texture;
        };
        Action<ImageTexture> dispose = texture =>
        {
            disposals[texture] = disposals.GetValueOrDefault(texture) + 1;
            texture.Dispose();
        };
        var textures = new Dictionary<TerrainTextureKey, DecodedTerrainTexture>
        {
            [first] = new(first, frame.Width, frame.Height, frame.Rgba.ToArray()),
            [second] = new(second, frame.Width, frame.Height, frame.Rgba.ToArray())
        };
        var staticIdentity = new StaticObjectResourceIdentity(10, 0, 1, new string('f', 64));
        var staticKey = new StaticSceneryTextureKey(staticIdentity, 0, 0, 0);
        var staticTextures = new Dictionary<StaticSceneryTextureKey, DecodedStaticTexture>
        {
            [staticKey] = new(staticKey, frame.Width, frame.Height, frame.Rgba.ToArray())
        };
        var actorKey = new ActorDiagnosticTextureKey(ActorDiagnosticSource.IdleResourceOrdinal, ActorDiagnosticSource.IdleSelector, 0, 0, 1);
        var actorTextures = new Dictionary<ActorDiagnosticTextureKey, DecodedActorTexture>
        {
            [actorKey] = new(actorKey, frame.Width, frame.Height, frame.Rgba.ToArray())
        };
        using (var reloadCache = new TerrainTextureCache(create, dispose))
        {
            reloadCache.Upload(textures);
            reloadCache.UploadStatic(staticTextures);
            reloadCache.UploadActor(actorTextures);
            var firstGeneration = created.ToArray();
            reloadCache.Clear();
            if (firstGeneration.Any(texture => disposals[texture] != 1) || reloadCache.Count != 0)
                throw new InvalidOperationException("Reload did not dispose each replaced texture exactly once.");
            reloadCache.Upload(textures);
            reloadCache.UploadStatic(staticTextures);
            reloadCache.UploadActor(actorTextures);
            var secondGeneration = created.Skip(firstGeneration.Length).ToArray();
            reloadCache.Clear();
            if (secondGeneration.Length != 4 || secondGeneration.Any(texture => disposals[texture] != 1))
                throw new InvalidOperationException("Reload replacement textures were not disposed exactly once.");
        }

        using (var exitCache = new TerrainTextureCache(create, dispose))
        {
            var beforeExit = created.Count;
            exitCache.Upload(textures);
            exitCache.UploadStatic(staticTextures);
            exitCache.UploadActor(actorTextures);
            var exitGeneration = created.Skip(beforeExit).ToArray();
            exitCache.Dispose();
            if (exitCache.Count != 0 || exitGeneration.Length != 4 || exitGeneration.Any(texture => disposals[texture] != 1))
                throw new InvalidOperationException("Exit did not dispose each live texture exactly once.");
        }
    }

    private static void AssertReadyStateRetainsOnlyHudMetrics()
    {
        // This is a deterministic structural ownership assertion, not a GC
        // timing claim. The app may use decoded textures while a load's local
        // task is active, but no scene-lifetime app field may retain that
        // result or its decoded texture dictionary after cache upload.
        var retainsDecodedData = typeof(TerrainViewerApp).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Any(field => field.FieldType == typeof(TerrainLoadResult) || ReferencesDecodedTexture(field.FieldType));
        if (retainsDecodedData) throw new InvalidOperationException("Ready app state retains decoded terrain textures instead of HUD metrics.");
    }

    private static bool ReferencesDecodedTexture(Type type)
    {
        if (type == typeof(DecodedTerrainTexture) || type == typeof(DecodedStaticTexture) || type == typeof(DecodedActorTexture) || type == typeof(byte[])) return true;
        if (type.IsArray) return ReferencesDecodedTexture(type.GetElementType()!);
        return type.IsGenericType && type.GetGenericArguments().Any(ReferencesDecodedTexture);
    }

    private static void AssertRgba(IReadOnlyList<byte> rgba, int x, int y, byte r, byte g, byte b, byte a)
    {
        var offset = ((y * SyntheticTerrainFixture.Width) + x) * 4;
        if (rgba[offset] != r || rgba[offset + 1] != g || rgba[offset + 2] != b || rgba[offset + 3] != a) throw new InvalidOperationException("Decoded RGBA mismatch.");
    }

    private static void AssertPixel(Image image, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = image.GetPixel(x, y);
        var actual = ((byte)Math.Round(pixel.R * 255), (byte)Math.Round(pixel.G * 255), (byte)Math.Round(pixel.B * 255), (byte)Math.Round(pixel.A * 255));
        if (actual != (r, g, b, a)) throw new InvalidOperationException($"pixel ({x},{y}) expected ({r},{g},{b},{a}) actual {actual}");
    }
}
