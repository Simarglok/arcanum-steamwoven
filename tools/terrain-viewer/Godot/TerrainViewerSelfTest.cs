using Arcanum.Data;
using Arcanum.Data.Terrain;
using Arcanum.Data.StaticObjects;
using Arcanum.TerrainViewer.Core;
using Godot;
using System.Reflection;

namespace Arcanum.TerrainViewer;

public static class TerrainViewerSelfTest
{
    public static async void Run(Node host)
    {
        try
        {
            AssertIndependentGeometryConstants();
            var frame = ArtDecoder.Decode(SyntheticTerrainFixture.CreateArt(), new ArtFrameSelection(0, 0, 0));
            AssertFrameBytes(frame);
            var keys = CreateKeys();
            using var cache = new TerrainTextureCache();
            cache.Upload(CreateTextures(keys, frame));
            cache.UploadStatic(CreateStaticTextures(frame));
            AssertUploadOwnership(frame, keys.Tile);
            AssertReloadAndExitTextureOwnership(frame);
            AssertReadyStateRetainsOnlyHudMetrics();

            if (DisplayServer.GetName() != "headless")
            {
                await AssertCanvasOraclesAsync(host, cache, keys);
                await AssertCombinedCanvasOracleAsync(host, cache, keys.Red);
                GD.Print("ARC-RENDER-004 self-test PASS combined production-canvas pixel oracle");
            }
            else
            {
                // The dummy renderer cannot read a ViewportTexture. Plans and
                // source constants are still constructed, but no pixels are claimed.
                AssertPlanConstants(keys.Tile, keys.Facade);
                AssertCombinedPlan();
                GD.Print("ARC-RENDER-004 self-test PASS combined structural plan; graphical readback NOT_VERIFIED headless");
            }
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"ARC-RENDER-003 self-test FAIL RenderFailure synthetic={exception.GetType().Name}:{exception.Message}");
            host.GetTree().Quit(1);
        }
    }

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

    private static void AssertCombinedPlan()
    {
        var identity = new StaticObjectResourceIdentity(9, 0, 1, new string('c', 64));
        var statics = StaticSceneryScenePlan.CreateSynthetic([new StaticSceneryPlanInput(4, 0, 0, 20, 20, identity, 0, 0, 0, 3, 3, 0, 0, 0, 0, false)]);
        if (statics.Commands.Single().Destination != new TerrainRect(20, 20, 3, 3)) throw new InvalidOperationException("Combined static geometry differs from literal oracle.");
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
        using (var reloadCache = new TerrainTextureCache(create, dispose))
        {
            reloadCache.Upload(textures);
            var firstGeneration = created.ToArray();
            reloadCache.Clear();
            if (firstGeneration.Any(texture => disposals[texture] != 1) || reloadCache.Count != 0)
                throw new InvalidOperationException("Reload did not dispose each replaced texture exactly once.");
            reloadCache.Upload(textures);
            var secondGeneration = created.Skip(firstGeneration.Length).ToArray();
            reloadCache.Clear();
            if (secondGeneration.Length != 2 || secondGeneration.Any(texture => disposals[texture] != 1))
                throw new InvalidOperationException("Reload replacement textures were not disposed exactly once.");
        }

        using (var exitCache = new TerrainTextureCache(create, dispose))
        {
            var beforeExit = created.Count;
            exitCache.Upload(textures);
            var exitGeneration = created.Skip(beforeExit).ToArray();
            exitCache.Dispose();
            if (exitCache.Count != 0 || exitGeneration.Length != 2 || exitGeneration.Any(texture => disposals[texture] != 1))
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
        if (type == typeof(DecodedTerrainTexture) || type == typeof(byte[])) return true;
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
