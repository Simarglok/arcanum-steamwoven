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
        Require(InterfaceHd.DirectScale(1, 4) is null && InterfaceHd.DirectScale(2, 4) is null && InterfaceHd.DirectScale(3.004, 4) is null && InterfaceHd.DirectScale(4, 4) is null
            && InterfaceHd.DirectScale(4.8, 4) is null && InterfaceHd.DirectScale(double.NaN, 4) is null && InterfaceHd.DirectScale(0, 4) is null && InterfaceHd.DirectScale(2.5, 1) is null,
            "a whole scale, the pack's scale and above, a bad number and a pack of scale 1 draw as before");
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
            texture.ScaleSource = () => device;
            plain.ScaleSource = () => device;
            Require(await Settled(texture), "at 4/3 the frame is resampled by a worker and drawn from the copy");
            var (width, height) = InterfaceChromeRenderer.PixelSize(W, H, 1.333);
            Require(texture.AreaPixelSize == new Vector2I(width, height) && texture.GetWidth() == W && texture.GetHeight() == H, $"the copy is {width}x{height} device pixels and the texture keeps the Legacy size");
            Require(Math.Abs(texture.HdScale - 1) <= 1, "the region draw scales by the copy's ratio");
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
