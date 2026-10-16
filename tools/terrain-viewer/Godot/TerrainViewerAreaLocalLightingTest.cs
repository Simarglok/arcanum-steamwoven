using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-012: the production compositor draws terrain under a stationary light lattice as
    // source × bilinear((ambient + lighter) − darker) / 255, a body with a two-colour gradient across its
    // width, and the actor with its own light colour. Expected pixels are literal hand-computed values.
    private static async Task AssertAreaLocalLightingAsync(Node host)
    {
        using var cache = new TerrainTextureCache();
        var synthetic = SyntheticActorFixture.Create(human: true);
        cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var native = DisplayServer.GetName() != "headless";
        var samples = 0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (native) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        void Pixel(string label, int x, int y, (double R, double G, double B) expected)
        {
            if (!native) return;
            using var image = viewport.GetTexture().GetImage();
            var pixel = image.GetPixel(x, y);
            var actual = (pixel.R * 255, pixel.G * 255, pixel.B * 255);
            if (Math.Abs(actual.Item1 - expected.R) > 2 || Math.Abs(actual.Item2 - expected.G) > 2 || Math.Abs(actual.Item3 - expected.B) > 2)
                throw new InvalidOperationException($"AREA_LOCAL_LIGHT_PIXEL:{label} expected {expected} actual {actual}");
            samples++;
        }
        try
        {
            // One outdoor tile (class bit 8 set) at the chart origin: destination (1,0,78,40), source colour 200.
            const string digest = AreaCompositionFixture.Digest;
            var key = new AreaVisualKey(AreaSourceKind.Terrain, new(digest, 5051, 100), 0, "");
            var tile = new AreaTerrainRecord(key, 0, 0, 92928, 82560, 0x100, 0, AreaScope.Core, key);
            var tileVisual = new AreaVisualRecord(key, 0x100, 0, new(new(AreaSourceRole.Resource2), 1, 0, 100, 100, 0, digest), digest, 100, 0, 0, 0, 0, 0, 0,
                false, 78, 40, 0, 0, 777, -999, 0, 0, 100, 1, 78 * 40 * 4, digest, "synthetic");
            var tilePixels = new byte[78 * 40 * 4];
            for (var at = 0; at < tilePixels.Length; at += 4) { tilePixels[at] = 200; tilePixels[at + 1] = 200; tilePixels[at + 2] = 200; tilePixels[at + 3] = 255; }
            var plan = AreaScenePlan.Create([tile], [], [tileVisual]);
            var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame> { [AreaFrameKey.From(tileVisual)] = new(78, 40, tilePixels) };
            var placement = AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]);
            var actor = synthetic.Result.ActorPlan!.Select(placement);
            viewport.CanvasTransform = new Transform2D(0, Vector2.One, 0, new Vector2(100, 130));
            canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(false);
            canvas.PublishArea(plan, frames, actor, placement, cache); await Settle();

            // A 160x80 light, hotspot (80,40), at the tile's cell: rectangle (-40,-20,160,80). Its left half is
            // (100,0,0), its right half (0,0,0). Nodes at x = 0 take R 100, at x = 40 take R 0.
            var mask = new byte[160 * 80 * 4];
            for (var y = 0; y < 80; y++) for (var x = 0; x < 160; x++) { var at = (y * 160 + x) * 4; mask[at] = x < 80 ? (byte)100 : (byte)0; mask[at + 3] = 255; }
            var light = new StationaryLight(100, 0, 92928, 82560, 0, 0, StationaryLightFlags.None, 5u << 19 | 9u << 28, AmbientColor.Neutral, digest);
            var source = AreaStationaryLightSource.Create(5000, [light], [(5, 160, 80, 80, 40, mask)]);
            var local = AreaLocalLighting.Create([tile], [tileVisual], AreaChart.Exterior, source);
            var pair = new AmbientPair(new(128, 64, 255), new(255, 0, 0));
            var channels = AreaAmbientPolicy.Channels(plan, [tile], [tileVisual]);
            canvas.SetAreaAmbient(new(pair, channels) { Local = local.View(plan, channels, pair) }); await Settle();
            // World (20.5,10.5): nodes x 0 (R 128+100 = 228) and x 40 (R 128), t = 0.5125 → R 176.75;
            // pixel = 200 × (176.75, 64, 255) / 255.
            Pixel("lattice-mid", 120, 140, (138.63, 50.20, 200));
            // Pixel 101 is world (1.5,10.5): t = 0.0375 → R 224.25 → 175.88.
            Pixel("lattice-node", 100 + 1, 140, (200 * (228 - 100 * 1.5 / 40) / 255, 50.20, 200));
            // World (60.5,10.5): nodes x 40 and 80 both carry no light → the plain ambient.
            Pixel("lattice-dark", 160, 140, (100.39, 50.20, 200));

            // A body with a hand-specified gradient and the actor with its own colour.
            var body = AreaCompositionFixture.Body(0);
            var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
            var bodyPlan = AreaScenePlan.Create([], [body], [visual]);
            var bodyFrames = new Dictionary<AreaFrameKey, DecodedAreaFrame> { [AreaFrameKey.From(visual)] = new(160, 160, AreaCompositionFixture.Pixels(160, 160, true)) };
            canvas.SetAreaAmbient(null); canvas.SetActorVisibilityForReadback(true);
            canvas.PublishArea(bodyPlan, bodyFrames, actor, placement, cache);
            var bodies = new Dictionary<AreaVisualKey, AreaLitColors> { [visual.Key] = new(new(255, 0, 0), new(0, 0, 255)) };
            canvas.SetAreaAmbient(new(pair, new Dictionary<AreaVisualKey, AreaAmbientChannel> { [visual.Key] = AreaAmbientChannel.Outdoor })
                { Local = new(null, bodies, _ => new(10, 20, 30)) });
            await Settle();
            // Marker (250,150,50) at world (11.5,-48.5) of the body at destination (0,-60,160,160):
            // t = 11.5 / 159 → tint (236.56, 0, 18.44) → (231.92, 0, 3.62).
            Pixel("gradient", 111, 81, (231.92, 0, 3.62));
            var actorItem = canvas.GetChildren().OfType<AreaDrawItem>().Single(node => node.Visible && node.Actor is not null);
            if (actorItem.Ambient != new Color(10 / 255f, 20 / 255f, 30 / 255f) || actorItem.Lattice is not null)
                throw new InvalidOperationException("AREA_LOCAL_LIGHT_ACTOR_COLOR");
            canvas.SetAreaAmbient(null);
        }
        finally { canvas.ClearScene(); viewport.QueueFree(); }
        GD.Print($"ARC-RENDER-012 self-test PASS lattice/gradient/actor samples={samples}" + (native ? "" : "; pixels NOT_VERIFIED headless"));
    }
}
