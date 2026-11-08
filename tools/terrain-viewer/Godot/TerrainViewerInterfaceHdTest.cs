using Arcanum.World;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-UI-089: the interface HD switch with a synthetic 4× pack of one 8×4 frame whose HD texels differ from Legacy in the red channel.
    // The texture keeps the Legacy size and the Legacy pixels for everything that reads them; it draws Legacy while the HD profile is off,
    // while the pack has no entry for the frame, and while a frame has no key; it draws the HD redraw once both the pack and the profile are
    // on; the strip sides draw only then. Synthetic pixels only. The pixel checks need a drawing window (they are skipped when headless).
    private static async Task AssertInterfaceHdAsync(Node host)
    {
        var native = DisplayServer.GetName() != "headless";
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-089-selftest-").FullName;
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-089 interface HD self-test: " + why); }
        const int W = 8, H = 4;
        var legacy = new byte[W * H * 4];
        for (var i = 0; i < W * H; i++) { legacy[i * 4] = 200; legacy[i * 4 + 1] = 100; legacy[i * 4 + 2] = 50; legacy[i * 4 + 3] = i == 0 ? (byte)0 : (byte)255; }
        var identity = HdInterfaceKey.Identity("synthetic.art", new byte[160]);
        var art = new HdInterfaceArt("synthetic.art", identity, [new(W, H, legacy)]);
        var hdRgba = HdNearestPack.Replicate(legacy, W, H, 4, parentTint: (_, _) => 0xFF);
        var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "pack"), "interface-selftest", 4,
            [new HdBuiltInterfaceFrame(art, 0, W, H, hdRgba, new(1, 1, 0, 0, 1))], DateTimeOffset.UnixEpoch);
        Require(written.Status == HdInterfaceWriteStatus.Written, "the synthetic pack is written: " + written.Status + " " + written.Detail);
        var open = HdPack.Open(System.IO.Path.Combine(root, "pack"));
        Require(open.Status == HdPackOpenStatus.Opened, "the synthetic pack opens: " + open.Status);
        var pack = open.Pack!;

        var keyed = new AreaInterfaceFrame(W, H, legacy, Hd: HdInterfaceKey.Query(identity, 0, W, H, legacy));
        var other = HdInterfaceKey.Identity("missing.art", new byte[140]);
        var unlisted = new AreaInterfaceFrame(W, H, legacy, Hd: HdInterfaceKey.Query(other, 0, W, H, legacy));
        var keyless = new AreaInterfaceFrame(W, H, legacy);
        var viewport = new SubViewport { Size = new(W, H), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        host.AddChild(viewport);
        try
        {
            InterfaceHd.Configure(null, false);
            var texture = (HdInterfaceTexture)InterfaceHd.Texture(keyed);
            var missing = (HdInterfaceTexture)InterfaceHd.Texture(unlisted);
            Require(InterfaceHd.Texture(keyless) is not HdInterfaceTexture, "a frame without a key is a plain Legacy texture");
            Require(texture.GetWidth() == W && texture.GetHeight() == H && texture.GetSize() == new Vector2(W, H), "the texture reports the Legacy size");
            Require(!texture.IsHd && InterfaceHd.ActiveCount() == 0, "no pack, switch off: Legacy");

            var rect = new TextureRect
            {
                Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Position = Vector2.Zero, Size = new(W, H),
            };
            viewport.AddChild(rect);
            var sides = new AreaStripSidesView(AreaBarStrip.Top);
            Require(!AreaStripSides.SideArtReady && !sides.Enabled, "ARC-UI-096: the side fill is off by default (no proper side art)");
            sides.Enabled = true; // the draw path itself is still checked below
            viewport.AddChild(sides);
            sides.Set(texture, 40, 40);

            async Task<Color> Probe()
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return default;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                return image.GetPixel(2, 2);
            }
            // Legacy red 200, HD red 200 ^ 255 = 55.
            bool IsLegacy(Color c) => Math.Abs(c.R8 - 200) <= 1 && Math.Abs(c.G8 - 100) <= 1;
            bool IsHdColour(Color c) => Math.Abs(c.R8 - 55) <= 1 && Math.Abs(c.G8 - 100) <= 1;

            var pixel = await Probe();
            if (native) Require(IsLegacy(pixel), $"Legacy pixel with the switch off ({pixel.R8},{pixel.G8},{pixel.B8})");
            Require(!sides.DrawsHd, "the sides draw nothing while the switch is off");

            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd && !sides.DrawsHd, "a pack alone does not switch: the profile is off");
            pixel = await Probe();
            if (native) Require(IsLegacy(pixel), "Legacy pixel with a pack and the switch off");

            InterfaceHd.Configure(pack, true);
            Require(texture.IsHd && InterfaceHd.ActiveCount() == 1, "pack and profile on: the keyed frame is HD");
            Require(!missing.IsHd, "a frame the pack has no entry for stays Legacy");
            Require(texture.GetSize() == new Vector2(W, H) && texture.GetWidth() == W && texture.GetHeight() == H, "the HD texture keeps the Legacy size");
            Require(sides.DrawsHd, "the sides draw HD tiles with the switch on");
            sides.Enabled = false;
            Require(!sides.DrawsHd, "ARC-UI-096: with the side fill off the sides draw nothing even with the switch on");
            sides.Enabled = true;
            Require(InterfaceHd.LegacyImage(texture) is { } legacyImage && legacyImage.GetWidth() == W && legacyImage.GetHeight() == H && legacyImage.GetPixel(2, 2).R8 == 200, "the Legacy pixels stay readable as an image with HD on");
            pixel = await Probe();
            if (native) Require(IsHdColour(pixel), $"HD pixel with the switch on ({pixel.R8},{pixel.G8},{pixel.B8})");

            sides.Set(texture, 0, 0);
            Require(!sides.DrawsHd, "no margin, no side tiles (a 4:3 screen)");
            sides.Set(texture, 40, 40);

            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd && !sides.DrawsHd && InterfaceHd.ActiveCount() == 0, "the switch off again: Legacy");
            pixel = await Probe();
            if (native) Require(IsLegacy(pixel), "Legacy pixel after switching back");

            InterfaceHd.Configure(null, true);
            Require(!texture.IsHd, "the profile on without a pack: Legacy");

            await AssertInterfaceHdFilterAsync(host, root, native, Require);
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            viewport.QueueFree();
            try { System.IO.Directory.Delete(root, recursive: true); } catch (System.IO.IOException) { }
        }
        GD.Print("ARC-UI-089 self-test interface HD switch PASS");
    }

    // ARC-UI-112: an HD interface frame drawn below its 4× scale is filtered (trilinear mips through its canvas texture) even under a parent
    // that draws Nearest; the Legacy frame under the same parent stays hard. Synthetic frames: a Legacy frame of one-texel stripes (red
    // 0/255) and an HD frame of one-HD-texel stripes, shown at 1.5× (every HD texel is 0.375 of a device pixel), and a half-black/half-white
    // frame drawn by region to see that the mips do not drag the neighbouring half into the white part. Pixel checks need a drawing window.
    private static async Task AssertInterfaceHdFilterAsync(Node host, string root, bool native, Action<bool, string> require)
    {
        const int W = 8, H = 4, Scale = 4;
        var legacy = new byte[W * H * 4];
        var hdStripes = new byte[W * Scale * H * Scale * 4];
        var legacyHalves = new byte[W * H * 4];
        var hdHalves = new byte[W * Scale * H * Scale * 4];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var at = (y * W + x) * 4;
            legacy[at] = x % 2 == 0 ? (byte)0 : (byte)255; legacy[at + 1] = 100; legacy[at + 2] = 50; legacy[at + 3] = 255;
            legacyHalves[at] = x < 3 ? (byte)0 : (byte)255; legacyHalves[at + 1] = 100; legacyHalves[at + 2] = 50; legacyHalves[at + 3] = 255;
        }
        for (var y = 0; y < H * Scale; y++)
        for (var x = 0; x < W * Scale; x++)
        {
            var at = (y * W * Scale + x) * 4;
            hdStripes[at] = x % 2 == 0 ? (byte)0 : (byte)255; hdStripes[at + 1] = 100; hdStripes[at + 2] = 50; hdStripes[at + 3] = 255;
            hdHalves[at] = x < 3 * Scale ? (byte)0 : (byte)255; hdHalves[at + 1] = 100; hdHalves[at + 2] = 50; hdHalves[at + 3] = 255;
        }
        var stripeArt = new HdInterfaceArt("synthetic.stripes", HdInterfaceKey.Identity("synthetic.stripes", new byte[150]), [new(W, H, legacy)]);
        var halvesArt = new HdInterfaceArt("synthetic.halves", HdInterfaceKey.Identity("synthetic.halves", new byte[151]), [new(W, H, legacyHalves)]);
        var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "filter-pack"), "interface-filter-selftest", Scale,
            [new HdBuiltInterfaceFrame(stripeArt, 0, W, H, hdStripes, new(1, 1, 0, 0, 1)), new HdBuiltInterfaceFrame(halvesArt, 0, W, H, hdHalves, new(1, 1, 0, 0, 1))],
            DateTimeOffset.UnixEpoch);
        require(written.Status == HdInterfaceWriteStatus.Written, "the filter pack is written: " + written.Status + " " + written.Detail);
        var open = HdPack.Open(System.IO.Path.Combine(root, "filter-pack"));
        require(open.Status == HdPackOpenStatus.Opened, "the filter pack opens: " + open.Status);

        // 12 × 6 device pixels for an 8 × 4 frame: 1.5×.
        const int DeviceW = 12, DeviceH = 6;
        var viewport = new SubViewport { Size = new(DeviceW, DeviceH), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        host.AddChild(viewport);
        try
        {
            InterfaceHd.Configure(null, false);
            var stripes = (HdInterfaceTexture)InterfaceHd.Texture(new AreaInterfaceFrame(W, H, legacy, Hd: HdInterfaceKey.Query(stripeArt.Identity, 0, W, H, legacy)));
            var halves = (HdInterfaceTexture)InterfaceHd.Texture(new AreaInterfaceFrame(W, H, legacyHalves, Hd: HdInterfaceKey.Query(halvesArt.Identity, 0, W, H, legacyHalves)));
            var stripeRect = new TextureRect
            {
                Texture = stripes, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, Position = Vector2.Zero, Size = new(DeviceW, DeviceH),
            };
            viewport.AddChild(stripeRect);

            async Task<Image?> Snapshot()
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return null;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                return viewport.GetTexture().GetImage();
            }
            static int Soft(Image image) // pixels whose red is neither 0 nor 255: filtered
            {
                var soft = 0;
                for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                    if (image.GetPixel(x, y).R8 is > 20 and < 235) soft++;
                return soft;
            }

            using (var legacyShot = await Snapshot())
                if (legacyShot is not null) require(Soft(legacyShot) == 0, $"the Legacy frame stays Nearest under a Nearest parent ({Soft(legacyShot)} filtered pixels)");

            InterfaceHd.Configure(open.Pack, true);
            require(stripes.IsHd, "the stripe frame is HD with the pack and the profile on");
            using (var hdShot = await Snapshot())
                if (hdShot is not null)
                    require(Soft(hdShot) >= DeviceW * DeviceH * 3 / 4, $"the HD frame under a Nearest parent is filtered ({Soft(hdShot)} of {DeviceW * DeviceH} pixels are neither 0 nor 255)");

            // The same HD texture through a region draw (gauges and bars): the white part of a half-black frame drawn alone must stay white.
            stripeRect.QueueFree();
            var regionNode = new RegionProbe(halves, new Rect2(0, 0, DeviceW * 5f / W, DeviceH), new Rect2(3, 0, 5, H)) { TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            viewport.AddChild(regionNode);
            using (var regionShot = await Snapshot())
                if (regionShot is not null)
                {
                    var darkest = 255;
                    for (var y = 0; y < regionShot.GetHeight(); y++)
                    for (var x = 0; x < (int)(DeviceW * 5f / W); x++)
                        darkest = Math.Min(darkest, regionShot.GetPixel(x, y).R8);
                    GD.Print($"ARC-UI-112 region probe darkest red in the white region={darkest}");
                    require(darkest >= 220, $"a region that starts at an unaligned HD texel does not take the neighbouring black part in (darkest red {darkest})");
                }
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            viewport.QueueFree();
        }
        GD.Print("ARC-UI-112 self-test interface HD filter PASS");
    }

    private sealed partial class RegionProbe(Texture2D texture, Rect2 destination, Rect2 source) : Control
    {
        public override void _Draw() => DrawTextureRectRegion(texture, destination, source);
    }
}
