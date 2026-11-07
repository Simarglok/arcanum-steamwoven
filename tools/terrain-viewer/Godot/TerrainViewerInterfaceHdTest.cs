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
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            viewport.QueueFree();
            try { System.IO.Directory.Delete(root, recursive: true); } catch (System.IO.IOException) { }
        }
        GD.Print("ARC-UI-089 self-test interface HD switch PASS");
    }
}
