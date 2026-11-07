using Arcanum.Data.Area;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-014: the production AREA compositor under profile Hd with a synthetic nearest
    // 4× pack (Legacy hard edge). By ARC-RENDER-013 consequence 4 every HD raster must equal
    // the Legacy raster pixel for pixel, at any transform, also with wall fades; a tinted pack
    // proves the HD texture is really bound (colour changes at magnification 2, alpha never);
    // six fallback reasons draw Legacy and are counted (HdAxisLimit, SourceMismatch and Pending
    // are unit-tested). Synthetic pixels only.
    private static async Task AssertHdPresentationAsync(Node host)
    {
        var native = DisplayServer.GetName() != "headless";
        var root = System.IO.Directory.CreateTempSubdirectory("arc-hd-selftest-").FullName;
        using var cache = new TerrainTextureCache();
        var synthetic = SyntheticActorFixture.Create(human: true);
        cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var sources = new List<HdFrameSource>();
        var compared = 0L;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (native) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            var (bodyCount, bodies, frames, plan, placement, actor, bodyFrames, actorFrames) = HdAreaScene(synthetic);
            HdFrameSource Source(string id, IEnumerable<HdNearestPack.Frame> packFrames, HdReviewStatus review = HdReviewStatus.Unreviewed, int tint = -1)
            {
                var open = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, id), id, 4, packFrames, review, tint, 0xFF));
                if (open.Status != HdPackOpenStatus.Opened) throw new InvalidOperationException("HD_SELFTEST_PACK:" + open.Status + ":" + open.Detail);
                var source = new HdFrameSource(open.Pack); sources.Add(source); return source;
            }
            var transforms = new (float Zoom, float X, float Y)[] { (1f, 100f, 130f), (2f, 60f, 200f), (.5f, 200f, 220f), (1.5f, 120.5f, 150.25f), (4f, -40f, 330f) };
            // Counts of the first transform (every candidate is in view there), per reason.
            var first = (Hd: 0, Fallback: 0, Reasons: new int[Enum.GetValues<HdFallbackReason>().Length], Text: "");
            async Task<byte[][]> Capture()
            {
                var images = new byte[transforms.Length][];
                for (var t = 0; t < transforms.Length; t++)
                {
                    var (zoom, x, y) = transforms[t];
                    viewport.CanvasTransform = new Transform2D(0, new Vector2(zoom, zoom), 0, new Vector2(x, y));
                    await Settle();
                    // Frames that enter the view are decoded lazily; observe the view once they are bound.
                    await Drain("transform" + t);
                    if (t == 0)
                    {
                        var stats = canvas.AreaHdStats;
                        first = (stats.Hd, stats.Fallback, Enum.GetValues<HdFallbackReason>().Select(r => stats[r]).ToArray(), stats.Format());
                    }
                    if (!native) { images[t] = Array.Empty<byte>(); continue; }
                    using var image = viewport.GetTexture().GetImage();
                    images[t] = image.GetData();
                }
                return images;
            }
            async Task Drain(string label)
            {
                for (var wait = 0; canvas.HdSource is { } source && (source.PendingCount > 0 || canvas.AreaHdStats[HdFallbackReason.Pending] > 0); wait++)
                {
                    if (wait > 600) throw new InvalidOperationException("HD_SELFTEST_PENDING:" + label);
                    await Settle();
                }
                await Settle();
            }
            void Same(byte[][] expected, byte[][] actual, string label)
            {
                for (var t = 0; t < expected.Length; t++)
                {
                    if (!expected[t].AsSpan().SequenceEqual(actual[t])) throw new InvalidOperationException($"HD_SELFTEST_RASTER:{label}:transform{t}");
                    compared += expected[t].Length / 4;
                }
            }
            void Counts(string label, int hd, HdFallbackReason? reason = null, int fallback = 0)
            {
                if (first.Hd != hd || first.Fallback != fallback || (reason is { } r && first.Reasons[(int)r] != fallback))
                    throw new InvalidOperationException($"HD_SELFTEST_COUNTS:{label}:{first.Text}");
            }

            canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(true);
            canvas.PublishArea(plan, frames, actor, placement, cache);
            await Settle();
            var legacy = await Capture();
            var candidates = bodyCount + 1;

            // Identity: the nearest 4× pack draws the Legacy raster at every transform.
            var nearest = Source("nearest", bodyFrames.Concat(actorFrames));
            canvas.SetVisualProfile(VisualProfile.Hd, nearest, HdDeviceBudget.Ceiling(4));
            await Drain("nearest");
            var hd = await Capture();
            Counts("nearest", candidates);
            if (canvas.HdResidentCount != candidates) throw new InvalidOperationException("HD_SELFTEST_RESIDENT:" + canvas.HdResidentCount);
            var items = canvas.GetChildren().OfType<AreaDrawItem>().Where(item => item.Visible).ToArray();
            // Every visible character/object item of the last view binds an HD texture of exactly N·w × N·h.
            if (items.Length == 0 || items.Any(item => item.HdScale != 4 || item.FrameTexture!.GetWidth() % 4 != 0 || item.FrameTexture.GetHeight() % 4 != 0))
                throw new InvalidOperationException("HD_SELFTEST_ITEMS");
            Same(legacy, hd, "nearest");

            // Wall fade with an HD crop reproduces the Legacy fade exactly (Legacy column, Legacy width).
            var fades = new Dictionary<AreaObjectKey, AreaWallFade> { [bodies[0].Key] = AreaWallFade.Left, [bodies[1].Key] = AreaWallFade.Both, [bodies[2].Key] = AreaWallFade.Right };
            canvas.SetAreaWallFades(fades);
            var hdFaded = await Capture();
            canvas.SetVisualProfile(VisualProfile.Legacy, nearest, HdDeviceBudget.Ceiling(4));
            var legacyFaded = await Capture();
            Same(legacyFaded, hdFaded, "wall-fade");
            if (native && legacyFaded[0].AsSpan().SequenceEqual(legacy[0])) throw new InvalidOperationException("HD_SELFTEST_FADE_NOT_OBSERVED");
            canvas.SetAreaWallFades(null);
            if (canvas.HdResidentCount != candidates) throw new InvalidOperationException("HD_SELFTEST_LEGACY_RETAIN");

            // A tinted pack: HD is bound (visible from magnification 2), coverage stays Legacy.
            var tinted = Source("tinted", bodyFrames.Concat(actorFrames), tint: 1);
            canvas.SetVisualProfile(VisualProfile.Hd, tinted, HdDeviceBudget.Ceiling(4));
            await Drain("tinted");
            var tint = await Capture();
            Counts("tinted", candidates);
            if (native)
            {
                // Only colour may change, and it must at magnification 2 (zoom 2, transform 1).
                for (var t = 0; t < tint.Length; t++)
                for (var at = 3; at < tint[t].Length; at += 4)
                    if (tint[t][at] != legacy[t][at]) throw new InvalidOperationException("HD_SELFTEST_TINT_ALPHA:" + t);
                var changed = 0;
                for (var at = 0; at < tint[1].Length; at += 4) if (tint[1][at] != legacy[1][at]) changed++;
                if (changed == 0) throw new InvalidOperationException("HD_SELFTEST_TINT_NOT_BOUND");
            }

            // Fallbacks: each draws Legacy, is counted by reason, and never fails the view.
            async Task Fallback(string label, HdFrameSource? source, HdDeviceBudget? budget, int hdCount, HdFallbackReason reason, int fallback)
            {
                canvas.SetVisualProfile(VisualProfile.Hd, source, budget ?? HdDeviceBudget.Ceiling(4));
                var images = await Capture();
                Counts(label, hdCount, reason, fallback);
                Same(legacy, images, label);
            }
            await Fallback("frame-missing", Source("bodies-only", bodyFrames), null, bodyCount, HdFallbackReason.FrameMissing, 1);
            await Fallback("no-pack", null, null, 0, HdFallbackReason.NoPack, candidates);
            await Fallback("pack-rejected", Source("rejected", bodyFrames.Concat(actorFrames), HdReviewStatus.Rejected), null, 0, HdFallbackReason.PackRejected, candidates);
            await Fallback("frame-rejected", Source("frame-rejected", bodyFrames.Select((f, i) => i == 1 ? f with { Review = HdReviewStatus.Rejected } : f).Concat(actorFrames)),
                null, candidates - 1, HdFallbackReason.FrameRejected, 1);
            await Fallback("budget-off", nearest, HdDeviceBudget.Ceiling(4) with { AreaVisibleBytes = 0 }, 0, HdFallbackReason.HdBudget, candidates);
            var broken = Source("broken", bodyFrames.Concat(actorFrames));
            var entry = broken.Pack!.Entries[0];
            var file = System.IO.Path.Combine([broken.Pack.Directory, .. entry.File.Split('/')]);
            var bytes = System.IO.File.ReadAllBytes(file); bytes[^20] ^= 0x5A; System.IO.File.WriteAllBytes(file, bytes);
            await Fallback("invalid", broken, null, candidates - 1, HdFallbackReason.Invalid, 1);

            // Back to Legacy: Legacy raster, HD kept within the retain limit; clearing releases all.
            canvas.SetVisualProfile(VisualProfile.Legacy, broken, HdDeviceBudget.Ceiling(4));
            Same(legacy, await Capture(), "legacy-again");
            if (canvas.AreaHdStats.Hd + canvas.AreaHdStats.Fallback != 0) throw new InvalidOperationException("HD_SELFTEST_LEGACY_COUNTS");
            canvas.ClearScene();
            if (canvas.HdResidentCount != 0 || broken.PendingCount != 0)
                throw new InvalidOperationException("HD_SELFTEST_RELEASE");
        }
        finally
        {
            canvas.ClearScene(); viewport.QueueFree();
            foreach (var source in sources) source.Dispose();
            System.IO.Directory.Delete(root, true);
        }
        GD.Print($"ARC-RENDER-014 self-test AREA HD PASS nearest-4x identity (a wall included, ARC-RENDER-016), wall fade, tint bound at magnification 2 with Legacy alpha, fallbacks FrameMissing/NoPack/PackRejected/FrameRejected/HdBudget/Invalid"
            + (native ? $" pixels={compared}" : "; pixels NOT_VERIFIED headless"));
    }

    // ARC-RENDER-014 WORLD: statics and the actor bind HD with the source crop scaled from the
    // Legacy frame size and the same destination; with the nearest pack the GPU nearest raster
    // equals Legacy at integer transforms. A pack recoloured per parent texel must equal a
    // Legacy draw of the recoloured frames, which also catches a wrong crop on uniform statics.
    private static async Task AssertWorldHdPresentationAsync(Node host)
    {
        var native = DisplayServer.GetName() != "headless";
        var root = System.IO.Directory.CreateTempSubdirectory("arc-hd-world-").FullName;
        var fixture = WorldFixture();
        using var cache = new TerrainTextureCache();
        cache.Upload(fixture.Result.Textures); cache.UploadStatic(fixture.Result.StaticTextures); cache.UploadActor(fixture.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        HdFrameSource? source = null;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (native) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            canvas.Publish(fixture.Result.DiagnosticPlan!, cache);
            var actor = fixture.Result.ActorPlan!.Current.Destination;
            async Task<byte[][]> Capture()
            {
                var shots = new List<byte[]>();
                foreach (var zoom in new[] { 1, 2, 3 })
                {
                    viewport.CanvasTransform = new Transform2D(0, new Vector2(zoom, zoom), 0, new Vector2(240 - zoom * actor.X, 200 - zoom * actor.Y));
                    await Settle();
                    if (!native) { shots.Add(Array.Empty<byte>()); continue; }
                    using var image = viewport.GetTexture().GetImage();
                    shots.Add(image.GetData());
                }
                return shots.ToArray();
            }
            var legacy = await Capture();
            var frames = fixture.Result.StaticTextures.Values.Select(t => new HdNearestPack.Frame(t.HdQuery!, t.Rgba))
                .Concat(fixture.Result.ActorTextures.Values.Select(t => new HdNearestPack.Frame(t.HdQuery!, t.Rgba)));
            var open = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, "world"), "world", 4, frames));
            source = new HdFrameSource(open.Pack ?? throw new InvalidOperationException("HD_WORLD_PACK:" + open.Status));
            canvas.SetVisualProfile(VisualProfile.Hd, source, HdDeviceBudget.Ceiling(4));
            for (var wait = 0; source.PendingCount > 0 || canvas.WorldHdStats.Hd != 6; wait++)
            {
                if (wait > 600) throw new InvalidOperationException("HD_WORLD_PENDING:" + canvas.WorldHdStats.Format());
                await Settle();
            }
            var hd = await Capture();
            if (canvas.WorldHdStats.Hd != 6 || canvas.WorldHdStats.Fallback != 0) throw new InvalidOperationException("HD_WORLD_COUNTS:" + canvas.WorldHdStats.Format());
            for (var t = 0; t < legacy.Length; t++)
                if (!legacy[t].AsSpan().SequenceEqual(hd[t])) throw new InvalidOperationException("HD_WORLD_RASTER:" + t);
            // A WORLD actor step republishes the plan; HD textures stay resident.
            var resident = canvas.HdResidentCount;
            canvas.Publish(fixture.Result.DiagnosticPlan!, cache); await Settle();
            if (canvas.HdResidentCount != resident || canvas.WorldHdStats.Hd != 6) throw new InvalidOperationException("HD_WORLD_REPUBLISH");

            static byte Recolour(int x, int y) => (byte)(x * 37 + y * 11 + 64);
            var recoloured = HdPack.Open(HdNearestPack.Write(System.IO.Path.Combine(root, "recoloured"), "recoloured", 4, frames, parentTint: Recolour));
            using var tinted = new HdFrameSource(recoloured.Pack ?? throw new InvalidOperationException("HD_WORLD_PACK:" + recoloured.Status));
            canvas.SetVisualProfile(VisualProfile.Hd, tinted, HdDeviceBudget.Ceiling(4));
            for (var wait = 0; tinted.PendingCount > 0 || canvas.WorldHdStats.Hd != 6; wait++)
            {
                if (wait > 600) throw new InvalidOperationException("HD_WORLD_PENDING_TINT:" + canvas.WorldHdStats.Format());
                await Settle();
            }
            var hdTinted = await Capture();
            canvas.SetVisualProfile(VisualProfile.Legacy, tinted, HdDeviceBudget.Ceiling(4));
            static byte[] Recoloured(byte[] rgba, int width)
            {
                var copy = (byte[])rgba.Clone();
                for (var at = 0; at < copy.Length; at += 4) if (copy[at + 3] != 0) copy[at] ^= Recolour(at / 4 % width, at / 4 / width);
                return copy;
            }
            using var oracleCache = new TerrainTextureCache();
            oracleCache.Upload(fixture.Result.Textures);
            oracleCache.UploadStatic(fixture.Result.StaticTextures.ToDictionary(p => p.Key, p => new DecodedStaticTexture(p.Key, p.Value.Width, p.Value.Height, Recoloured(p.Value.Rgba, p.Value.Width))));
            oracleCache.UploadActor(fixture.Result.ActorTextures.ToDictionary(p => p.Key, p => new DecodedActorTexture(p.Key, p.Value.Width, p.Value.Height, Recoloured(p.Value.Rgba, p.Value.Width))));
            canvas.Publish(fixture.Result.DiagnosticPlan!, oracleCache);
            var legacyTinted = await Capture();
            for (var t = 0; t < legacy.Length; t++)
            {
                if (!legacyTinted[t].AsSpan().SequenceEqual(hdTinted[t])) throw new InvalidOperationException("HD_WORLD_RECOLOURED:" + t);
                if (native && legacyTinted[t].AsSpan().SequenceEqual(legacy[t])) throw new InvalidOperationException("HD_WORLD_RECOLOUR_NOT_OBSERVED:" + t);
            }
            canvas.ClearScene();
            if (canvas.HdResidentCount != 0) throw new InvalidOperationException("HD_WORLD_RELEASE");
        }
        finally
        {
            canvas.ClearScene(); viewport.QueueFree(); source?.Dispose();
            System.IO.Directory.Delete(root, true);
        }
        GD.Print("ARC-RENDER-014 self-test WORLD HD PASS statics+actor 6 HD, nearest-4x identity and per-parent recoloured oracle at zoom 1/2/3, republish keeps HD" + (native ? "" : "; pixels NOT_VERIFIED headless"));
    }

    // The synthetic AREA scene of the HD self-tests: five bodies over one cell (a wall, sizes, a
    // mirrored frame, scale 75, shrunk, additive) and the actor, with their nearest-pack frames.
    private static (int Length, List<AreaObjectRecord> Bodies, Dictionary<AreaFrameKey, DecodedAreaFrame> Frames, AreaScenePlan Plan,
        ActorDiagnosticPlacementInput Placement, ActorDiagnosticScenePlan Actor, HdNearestPack.Frame[] BodyFrames, HdNearestPack.Frame[] ActorFrames)
        HdAreaScene(SyntheticActorLoad synthetic)
    {
        // Five bodies over one cell: sizes, a mirrored frame, scale 75, shrunk, additive.
        var shapes = new (int W, int H, int HotX, int HotY, bool Mirrored, int Scale, uint Flags, uint Blit)[]
        { (160, 160, 40, 80, false, 100, 0, 0), (37, 23, 10, 20, true, 100, 0, 0), (50, 40, -12, 30, false, 75, 0, 0),
          (30, 30, 5, 5, false, 100, 0x80, 0), (24, 18, 20, 10, false, 100, 0, 0x10) };
        var bodies = new List<AreaObjectRecord>(); var visuals = new List<AreaVisualRecord>();
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>();
        for (var i = 0; i < shapes.Length; i++)
        {
            var s = shapes[i];
            var body = AreaCompositionFixture.Body(i, s.Flags, s.Scale, blit: s.Blit);
            var rgba = HdPattern(s.W, s.H, i);
            var visual = AreaCompositionFixture.Visual(body, s.W, s.H, s.HotX, s.HotY, mirrored: s.Mirrored) with
            {
                PayloadSha256 = HdFrameQueries.Sha256(System.Text.Encoding.ASCII.GetBytes($"synthetic-hd-art-{i}")), ViewLength = 4096,
                RgbaSha256 = HdFrameQueries.Sha256(rgba),
            };
            // ARC-RENDER-016: the first body is a wall (ART class 1), an HD candidate like any object.
            if (i == 0) visual = visual with { RawArt = 0x10000000u | (visual.RawArt & 0x0FFFFFFFu) };
            bodies.Add(body); visuals.Add(visual); frames[AreaFrameKey.From(visual)] = new(s.W, s.H, rgba);
        }
        var plan = AreaScenePlan.Create([], bodies, visuals);
        var placement = AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]);
        var actor = synthetic.Result.ActorPlan!.Select(placement);
        var bodyFrames = frames.Select(pair => new HdNearestPack.Frame(HdFrameQueries.FromArea(pair.Key, pair.Value), pair.Value.Rgba)).ToArray();
        var actorFrames = synthetic.Result.ActorTextures.Values.Select(texture => new HdNearestPack.Frame(texture.HdQuery!, texture.Rgba)).ToArray();
        return (shapes.Length, bodies, frames, plan, placement, actor, bodyFrames, actorFrames);
    }

    // Distinct colours per texel with holes, so a wrong subtexel or crop changes pixels.
    private static byte[] HdPattern(int width, int height, int seed)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            if ((x + 2 * y + seed) % 9 == 0) continue;
            rgba[at] = (byte)(x * 37 + seed * 11); rgba[at + 1] = (byte)(y * 23 + seed * 5); rgba[at + 2] = (byte)((x + y) * 7 + seed); rgba[at + 3] = 255;
        }
        return rgba;
    }
}
