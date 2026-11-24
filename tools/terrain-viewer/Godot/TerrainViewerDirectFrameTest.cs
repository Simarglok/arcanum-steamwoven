using Arcanum.World;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-3002: a direct pack frame drawn at a fractional device scale below the pack's scale is area-resampled once per scale on a worker and drawn from that copy
    // (the mip chain is freed); whole scales, the pack's scale and above, art-font canvases and textures that claim no scale keep the trilinear draw. Synthetic 8 × 4 frame, 4× pack.
    private static async Task AssertDirectFrameAreaAsync(Node host)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-RENDER-3002 direct frame self-test: " + why); }
        const int W = 8, H = 4;
        Require(InterfaceHd.DirectScale(4.0 / 3, 4) == 1.333 && InterfaceHd.DirectScale(1.8, 4) == 1.8 && InterfaceHd.DirectScale(2.4, 4) == 2.4 && InterfaceHd.DirectScale(0.96, 4) == 0.96,
            "the fractional scales below the pack's are resampled (1280x800, 1920x1080, 2560x1440, a scale under 1)");
        Require(InterfaceHd.DirectScale(1, 4) is null && InterfaceHd.DirectScale(2, 4) is null && InterfaceHd.DirectScale(2.004, 4) is null && InterfaceHd.DirectScale(4, 4) is null
            && InterfaceHd.DirectScale(4.8, 4) is null && InterfaceHd.DirectScale(double.NaN, 4) is null && InterfaceHd.DirectScale(0, 4) is null && InterfaceHd.DirectScale(2.5, 1) is null,
            "a whole scale that divides the pack's (1, 2), the pack's scale and above, a bad number and a pack of scale 1 draw as before");
        Require(InterfaceHd.DirectScale(3, 4) == 3 && InterfaceHd.DirectScale(3.004, 4) == 3 && InterfaceHd.DirectScale(2.996, 4) == 3 && InterfaceHd.DirectScale(3, 6) is null && InterfaceHd.DirectScale(4, 6) == 4 && InterfaceHd.DirectScale(5, 6) == 5,
            "ARC-RENDER-3045: a whole scale that does not divide the pack's is resampled to exactly that scale (3 of a 4x pack, 4 and 5 of a 6x pack), 3 of a 6x pack is a mip level");
        var root = System.IO.Directory.CreateTempSubdirectory("arc-render-3002-selftest-").FullName;
        try
        {
            var legacy = new byte[W * H * 4];
            for (var i = 0; i < W * H; i++) { legacy[i * 4] = 200; legacy[i * 4 + 1] = 100; legacy[i * 4 + 2] = 50; legacy[i * 4 + 3] = i == 0 ? (byte)0 : (byte)255; }
            var identity = HdInterfaceKey.Identity("synthetic3002.art", new byte[160]);
            var art = new HdInterfaceArt("synthetic3002.art", identity, [new(W, H, legacy)]);
            var hdRgba = HdNearestPack.Replicate(legacy, W, H, 4, parentTint: (_, _) => 0xFF);
            var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "pack"), "direct-selftest", 4,
                [new HdBuiltInterfaceFrame(art, 0, W, H, hdRgba, new(1, 1, 0, 0, 1))], DateTimeOffset.UnixEpoch);
            Require(written.Status == HdInterfaceWriteStatus.Written, "the synthetic pack is written: " + written.Status + " " + written.Detail);
            var pack = HdPack.Open(System.IO.Path.Combine(root, "pack")).Pack ?? throw new InvalidOperationException("ARC-RENDER-3002 direct frame self-test: the pack does not open");

            var frame = new AreaInterfaceFrame(W, H, legacy, Hd: HdInterfaceKey.Query(identity, 0, W, H, legacy));
            float device = 4f / 3f;
            var texture = (HdInterfaceTexture)InterfaceHd.Texture(frame);
            var plain = (HdInterfaceTexture)InterfaceHd.Texture(frame, premultipliedAlpha: true);
            InterfaceHd.Configure(pack, true);
            async Task<bool> Settled(HdInterfaceTexture t)
            {
                for (var wait = 0; wait < 900; wait++)
                {
                    if (t.IsAreaResampled && InterfaceHd.DirectPending == 0) return true;
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                return false;
            }
            Require(texture.IsHd && !texture.IsAreaResampled, "no scale claimed: the mip chain draws as before (" + pack.ResolveSprite(frame.Hd!, legacy).Status + ")");
            // ARC-RENDER-3045: a texture that claims the scale before anything is built draws the mip chain while its first copy pends (never the Legacy pixels), then the copy.
            var fresh = (HdInterfaceTexture)InterfaceHd.Texture(frame);
            fresh.ScaleSource = () => device;
            Require(fresh.IsHd, "the first copy pending or not, the HD frame draws, never the Legacy pixels");
            Require(await Settled(fresh) && fresh.IsHd, "the first copy lands and replaces the mip chain");
            texture.ScaleSource = () => device;
            plain.ScaleSource = () => device;
            Require(await Settled(texture) && texture.IsHd, "at 4/3 the frame is resampled by a worker and drawn from the copy");
            var (width, height) = InterfaceChromeRenderer.PixelSize(W, H, 1.333);
            Require(texture.AreaPixelSize == new Vector2I(width, height) && texture.GetWidth() == W && texture.GetHeight() == H, $"the copy is {width}x{height} device pixels and the texture keeps the Legacy size");
            Require(texture.HdScale == 1 && texture.AreaPixelSize.X > 0, "the copy is not a whole multiple of the Legacy size (11 of 8 texels)");
            var expected = InterfaceChromeRenderer.ResampleStraight(hdRgba, W * 4, H * 4, width, height);
            Require(texture.AreaImageBytes() is { } drawn && drawn.AsSpan().SequenceEqual(expected), "the uploaded pixels are the area resample with straight alpha");
            Require(expected.Where((v, i) => i % 4 == 3 && v > 0 && v < 255).Any(), "the copy keeps partial alpha at the edge of the transparent corner texel");
            Require(plain.IsHd && !plain.IsAreaResampled, "an art-font (premultiplied) canvas keeps the mip chain");
            var (frames, bytes, _) = InterfaceHd.DirectTotals;
            Require(frames == 1 && bytes == expected.Length, $"the totals count the one frame ({frames}, {bytes})");

            device = 2f;
            Require(texture.IsHd && !texture.IsAreaResampled, "a whole device scale draws the mip chain again");
            device = 4f;
            Require(texture.IsHd && !texture.IsAreaResampled, "the pack's scale draws the frame as it is");
            device = 1.8f;
            Require(await Settled(texture) && texture.AreaPixelSize == new Vector2I(14, 7), "a new fractional scale is resampled once more (1.8: 14x7)");
            device = 4f / 3f;
            var again = InterfaceHd.DirectTotals.Frames;
            Require(await Settled(texture) && InterfaceHd.DirectTotals.Frames == again, "a scale already resampled is kept, not resampled again");

            // ARC-RENDER-3045: a whole scale the pack's does not divide (3 of 4) is resampled to exactly that scale (24x12: one texel per device pixel).
            device = 3f;
            Require(await Settled(texture) && texture.AreaPixelSize == new Vector2I(W * 3, H * 3) && texture.HdScale == 3, "a whole scale 3 of a 4x pack is resampled to 24x12");
            // Least recently used: five scales of one frame, the oldest asked for goes (1.333 was asked for after 1.8, so 1.8 goes, not the first one inserted).
            device = 2.4f;
            Require(await Settled(texture), "2.4 is resampled");
            device = 0.96f;
            Require(await Settled(texture) && InterfaceHd.DirectTotals.Frames == 4, "a fifth scale evicts one: four kept");
            device = 4f / 3f;
            Require(texture.IsAreaResampled && InterfaceHd.DirectPending == 0 && texture.AreaPixelSize == new Vector2I(width, height), "1.333 was asked for last among the old ones: it is kept (LRU), not resampled again");
            device = 1.8f;
            Require(await Settled(texture) && texture.AreaPixelSize == new Vector2I(14, 7), "1.8 went (the least recently used) and is resampled again");

            // ARC-RENDER-3045: a frame the pack has no redraw for fails once; another scale does not start another worker.
            var strayIdentity = HdInterfaceKey.Identity("synthetic3045.art", new byte[160].Select((_, i) => (byte)(i + 1)).ToArray());
            var stray = new AreaInterfaceFrame(W, H, legacy, Hd: HdInterfaceKey.Query(strayIdentity, 0, W, H, legacy));
            var strayTexture = (HdInterfaceTexture)InterfaceHd.Texture(stray);
            device = 4f / 3f;
            strayTexture.ScaleSource = () => device;
            Require(!strayTexture.IsHd, "a frame the pack has no redraw for draws Legacy");
            for (var wait = 0; wait < 900 && InterfaceHd.DirectPending > 0; wait++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InterfaceHd.DirectPending == 0 && !strayTexture.IsHd, "the failed resample settles");
            device = 1.8f;
            Require(!strayTexture.IsHd && InterfaceHd.DirectPending == 0, "another scale of the same frame starts no worker (negative cache)");
            device = 2.4f;
            Require(!strayTexture.IsHd && InterfaceHd.DirectPending == 0, "and neither does a third");

            var grey = texture.Greyed();
            Require(grey.IsHd, "a greyed copy shares the resampled frame");
            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd && texture.AreaPixelSize == default && InterfaceHd.DirectTotals.Frames == 0, "HD off: Legacy, the copy and the totals are gone");
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            System.IO.Directory.Delete(root, recursive: true);
        }
        GD.Print("ARC-RENDER-3002 self-test direct frame area resample PASS");
    }
}
