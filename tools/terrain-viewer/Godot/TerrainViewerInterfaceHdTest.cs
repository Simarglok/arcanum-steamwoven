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
        // A fractional edge tests RGB multiplication as well as zero-alpha corners.
        for (var y = 0; y < 4; y++) for (var x = 4; x < 8; x++) hdRgba[(y * W * 4 + x) * 4 + 3] = 128;
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

            async Task<Color> Probe(int x = 2, int y = 2)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return default;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewport.GetTexture().GetImage();
                return image.GetPixel(x, y);
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

            // ARC-DEV-376: the optimized cache converts the PNG once and then binds the same pixels with their whole mip chain.
            var cacheRoot = System.IO.Path.Combine(root, "hd-cache");
            HdOptimizedStore.Attach(null, false);
            HdOptimizedStore.Attach(pack, true, cacheRoot);
            Require(HdOptimizedStore.Cache is not null && HdOptimizedStore.TryBind(pack, keyed.Hd!, legacy) is null, "ARC-DEV-376: an empty cache binds nothing");
            HdOptimizedStore.Convert(pack, HdOptimizedStore.Cache!, CancellationToken.None, 0);
            var resolved = pack.ResolveSprite(keyed.Hd!, legacy);
            using (var cachedImage = HdOptimizedStore.TryBind(pack, keyed.Hd!, legacy))
            {
                Require(cachedImage is not null && cachedImage.HasMipmaps() && cachedImage.GetWidth() == W * 4 && cachedImage.GetHeight() == H * 4, "ARC-DEV-376: the converted frame binds with its mip chain");
                Require(resolved.Rgba is { } resolvedRgba && cachedImage!.GetData().AsSpan(0, resolvedRgba.Length).SequenceEqual(resolvedRgba), "ARC-DEV-376: the cached level 0 is the PNG's pixels");
            }
            Require(texture.IsHd, "ARC-DEV-376: the texture still draws HD");
            HdOptimizedStore.Attach(null, false);
            GD.Print("ARC-DEV-376 self-test optimized cache PASS");

            // ARC-UI-114: the production sheet's art-font material must not add RGB from transparent HD texels.
            var straightEdge = await Probe(1, 0);
            var premultiplied = (HdInterfaceTexture)InterfaceHd.Texture(keyed, premultipliedAlpha: true);
            rect.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
            rect.Texture = premultiplied;
            var transparentCorner = await Probe(0, 0);
            var premultipliedEdge = await Probe(1, 0);
            if (native)
            {
                Require(transparentCorner.A8 == 0 && transparentCorner.R8 == 0 && transparentCorner.G8 == 0, "transparent HD RGB must not add a rectangle on a PremultAlpha canvas");
                Require(Math.Abs(straightEdge.R8 - premultipliedEdge.R8) <= 1 && Math.Abs(straightEdge.G8 - premultipliedEdge.G8) <= 1
                    && Math.Abs(straightEdge.A8 - premultipliedEdge.A8) <= 1, "fractional HD edge matches ordinary alpha blending");
            }
            rect.Material = null; rect.Texture = texture;
            GD.Print("ARC-UI-114 self-test HD premultiplied canvas PASS" + (native ? "" : " (pixel checks skipped: headless)"));

            sides.Set(texture, 0, 0);
            Require(!sides.DrawsHd, "no margin, no side tiles (a 4:3 screen)");
            sides.Set(texture, 40, 40);

            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd && !sides.DrawsHd && InterfaceHd.ActiveCount() == 0, "the switch off again: Legacy");
            pixel = await Probe();
            if (native) Require(IsLegacy(pixel), "Legacy pixel after switching back");

            // ARC-UI-114: grey copies retain HD resolution, Legacy dimensions and the source query across switches/packs.
            var grey = (HdInterfaceTexture)InterfaceHd.Greyed(texture);
            Require(grey.GetSize() == new Vector2(W, H) && !grey.IsHd, "grey Legacy dimensions and off state");
            rect.Texture = grey;
            pixel = await Probe();
            if (native) Require(Math.Abs(pixel.R8 - 124) <= 1 && Math.Abs(pixel.G8 - pixel.R8) <= 1, "grey Legacy luminance");
            InterfaceHd.Configure(pack, true);
            Require(grey.IsHd, "grey uses HD, not a Legacy raster");
            pixel = await Probe();
            if (native) Require(Math.Abs(pixel.R8 - 81) <= 1 && Math.Abs(pixel.G8 - pixel.R8) <= 1, "grey HD luminance");
            var alternate = (byte[])hdRgba.Clone();
            for (var i = 0; i < alternate.Length; i += 4) { alternate[i] = 10; alternate[i + 1] = 20; alternate[i + 2] = 30; }
            var second = System.IO.Path.Combine(root, "second");
            Require(HdInterfacePackWriter.Write(second, "alternate", 4,
                [new HdBuiltInterfaceFrame(art, 0, W, H, alternate, new(1, 1, 0, 0, 1))], DateTimeOffset.UnixEpoch).Status == HdInterfaceWriteStatus.Written, "second pack written");
            InterfaceHd.Configure(HdPack.Open(second).Pack!, true);
            Require(grey.IsHd, "grey resolves replacement pack");
            pixel = await Probe();
            if (native) Require(Math.Abs(pixel.R8 - 18) <= 1, "grey invalidates decoded image on pack replacement");
            using var greyLegacy = InterfaceHd.LegacyImage(grey)!;
            Require(greyLegacy.GetPixel(0, 0).A8 == 0, "grey preserves transparent holes");
            GD.Print("ARC-UI-114 self-test grey HD source and pack switch PASS" + (native ? "" : " (pixel checks skipped: headless)"));

            InterfaceHd.Configure(null, true);
            Require(!texture.IsHd, "the profile on without a pack: Legacy");

            await AssertInterfaceHdFilterAsync(host, root, native, Require);
            await AssertInterfaceHdFontAsync(host, root, native, Require);
            await AssertMainMenuBackdropAsync(host, pack, native, Require);
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            viewport.QueueFree();
            try { System.IO.Directory.Delete(root, recursive: true); } catch (System.IO.IOException) { }
        }
        GD.Print("ARC-UI-089 self-test interface HD switch PASS");
    }

    // ARC-UI-122: the same synthetic wide art follows viewport changes without moving input off the label.
    private static async Task AssertMainMenuBackdropAsync(Node host, HdPack pack, bool native, Action<bool, string> require)
    {
        var path = System.IO.Path.Combine(pack.Directory, "main-menu-16x9.png");
        require(pack.ReadMainMenuBackdrop() is null && pack.ReadMainMenuBackdropReport() is { Status: "NoFile", Sha12: null }, "missing main-menu backdrop falls back");
        System.IO.File.WriteAllBytes(path, HdPng.EncodeRgba(16, 10, new byte[16 * 10 * 4]));
        require(pack.ReadMainMenuBackdrop() is null && pack.ReadMainMenuBackdropReport() is { Status: "WrongSize", Width: 16, Height: 10, Sha12.Length: 12 }, "main-menu backdrop rejects the wrong aspect");
        var rgba = new byte[16 * 9 * 4];
        rgba[3] = 255;
        System.IO.File.WriteAllBytes(path, HdPng.EncodeRgba(16, 9, rgba));
        require(pack.ReadMainMenuBackdrop() is null && pack.ReadMainMenuBackdropReport() is { Status: "NotOpaque" }, "main-menu backdrop rejects transparency");
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i + 1] = 160; rgba[i + 3] = 255; }
        System.IO.File.WriteAllBytes(path, HdPng.EncodeRgba(16, 9, rgba));

        require(pack.ReadMainMenuBackdrop() is { Width: 16, Height: 9 } && pack.ReadMainMenuBackdropReport() is { Status: "Used", Sha12.Length: 12 }, "valid main-menu backdrop opens");
        if (!native)
        {
            GD.Print("ARC-UI-122 self-test main-menu backdrop file PASS (window checks skipped: headless)");
            return;
        }

        var viewport = host.GetWindow();
        var originalScaleSize = viewport.ContentScaleSize;
        var originalScaleMode = viewport.ContentScaleMode;
        var originalScaleAspect = viewport.ContentScaleAspect;
        var originalScaleFactor = viewport.ContentScaleFactor;
        var overlay = new CanvasLayer { Layer = 100 };
        var menu = new AreaMenuArtView();
        overlay.AddChild(menu);
        host.AddChild(overlay);
        try
        {
            var cell = new AreaInterfaceFrame(5, 8, Enumerable.Repeat((byte)255, 5 * 8 * 4).ToArray(), 6);
            using var legacyImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            legacyImage.Fill(Colors.Blue);
            using var legacy = ImageTexture.CreateFromImage(legacyImage);
            menu.SetArt(new Dictionary<int, Texture2D> { [AreaMenuArtLayout.MainArt] = legacy, [AreaMenuArtLayout.SinglePlayerArt] = legacy, [AreaMenuArtLayout.OptionsArt] = legacy },
                new AreaArtFontFace(Enumerable.Repeat(cell, 225).ToArray()));
            InterfaceHd.Configure(pack, true);
            // The main window can be smaller than 2560×1440 on a Mac. Use its logical render target for exact test sizes.
            viewport.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            viewport.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            viewport.ContentScaleFactor = 1;
            var clicked = -1;
            menu.ButtonPressed += index =>
            {
                clicked = index;
                menu.Render(AreaMenuWindow.SinglePlayer, ["Back"], 0);
            };
            foreach (var size in new Vector2I[] { new(1920, 1080), new(2560, 1440), new(1600, 1000) })
            {
                viewport.ContentScaleSize = size;
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var actualSize = viewport.GetVisibleRect().Size;
                require(actualSize.IsEqualApprox(size), $"main viewport requested {size} and got {actualSize}");
                menu.Render(AreaMenuWindow.Main, ["One", "Two"], 1);
                var factor = size.Y / 600f;
                var expectedPosition = new Vector2((size.X - 800 * factor) / 2, 0);
                require(menu.Position.IsEqualApprox(expectedPosition) && menu.Scale.IsEqualApprox(new(factor, factor)),
                    $"main-menu plate is centred with a uniform scale at {size}");
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var capture = viewport.GetTexture().GetImage();
                require(capture.GetSize() == size, $"main-menu readback requested {size} and got {capture.GetSize()}");
                require(capture.GetPixel(0, 0).G8 == 160 && capture.GetPixel(size.X - 1, size.Y - 1).G8 == 160,
                    $"main-menu art covers opposite corners at {size}");
                var centre = menu.ButtonCentreForSelfTest(0)!.Value;
                viewport.PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre }, inLocalCoords: true);
                require(menu.HoveredForSelfTest == 0, $"main-menu hover at {size}");
                clicked = -1;
                foreach (var down in new[] { true, false })
                    viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down,
                        Position = centre, GlobalPosition = centre, ButtonMask = down ? MouseButtonMask.Left : 0 }, inLocalCoords: true);
                require(clicked == 0 && menu.LabelsForSelfTest.SequenceEqual(["Back"]), $"main-menu click opens the sub-menu at {size}");
                // ARC-DEV-344: the single-player window, drawn after the click, takes the same wide plate on the same transform.
                require(menu.Scale.IsEqualApprox(new(factor, factor)) && menu.Position.IsEqualApprox(expectedPosition),
                    $"single-player menu keeps the wide plate transform at {size}");
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var subCapture = viewport.GetTexture().GetImage();
                require(subCapture.GetPixel(0, 0).G8 == 160 && subCapture.GetPixel(size.X - 1, size.Y - 1).G8 == 160,
                    $"single-player menu art covers opposite corners at {size}");
                foreach (var window in new[] { AreaMenuWindow.NewGame, AreaMenuWindow.InPlay, AreaMenuWindow.InPlayLimited })
                {
                    menu.Render(window, ["One"], 0);
                    require(menu.Scale.IsEqualApprox(new(factor, factor)) && menu.Position.IsEqualApprox(expectedPosition),
                        $"{window} menu keeps the wide plate transform at {size}");
                }
                // The options window has its own wooden board (556): it stays on the whole-factor layout.
                menu.Render(AreaMenuWindow.Options, ["One"], 0);
                require(menu.Scale.X == AreaGameBarPanel.ScreenFactor(size), $"options menu keeps whole scale at {size}");
            }
            InterfaceHd.Configure(pack, false);
            menu.Render(AreaMenuWindow.Main, ["One"], 0);
            require(menu.Scale == Vector2.One, "main menu keeps whole scale with HD interface off");
            menu.Render(AreaMenuWindow.SinglePlayer, ["One"], 0);
            require(menu.Scale == Vector2.One, "single-player menu keeps whole scale with HD interface off");
            InterfaceHd.Configure(null, true);
            menu.Place();
            require(menu.Scale == Vector2.One, "main menu keeps whole scale without a pack");
            menu.Render(AreaMenuWindow.SinglePlayer, ["One"], 0);
            require(menu.Scale == Vector2.One, "single-player menu keeps whole scale without a pack");
        }
        finally
        {
            overlay.Free();
            viewport.ContentScaleSize = originalScaleSize;
            viewport.ContentScaleMode = originalScaleMode;
            viewport.ContentScaleAspect = originalScaleAspect;
            viewport.ContentScaleFactor = originalScaleFactor;
        }
        GD.Print("ARC-UI-122 self-test main-menu backdrop PASS (1920x1080, 2560x1440, 1600x1000)");
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
            require(halves.IsHd, "the half-and-half frame is HD with the pack and the profile on");
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
        GD.Print(native ? "ARC-UI-112 self-test interface HD filter PASS" : "ARC-UI-112 self-test interface HD filter PASS (pixel checks skipped: headless)");
    }

    // ARC-DEV-361: a font glyph with a coverage entry in the pack draws that frame while the switch and the pack are on, and only then; a glyph with
    // no entry, a frame without a key and every blend other than Coverage keep the Legacy frame. Synthetic 6 × 5 glyph: Legacy cover 200 in two columns,
    // HD cover 255 over the same area (4×). On a black canvas drawn white the Legacy glyph is 199, the HD one 254 (the same layer maths on 255).
    // Pixel checks need a drawing window.
    private static async Task AssertInterfaceHdFontAsync(Node host, string root, bool native, Action<bool, string> require)
    {
        const int W = 6, H = 5, Scale = 4, Glyph = 34, NoEntry = 35;
        var legacy = new byte[W * H * 4];
        for (var y = 1; y < 4; y++)
        foreach (var x in new[] { 2, 3 })
        { var at = (y * W + x) * 4; legacy[at] = 200; legacy[at + 3] = 255; }
        var identity = HdInterfaceKey.Identity("synthetic-font.art", new byte[153]);
        var empty = new HdLegacyRaster(1, 1, new byte[4]);
        var rasters = Enumerable.Range(0, 225).Select(i => i == Glyph || i == NoEntry ? new HdLegacyRaster(W, H, legacy) : empty).ToArray();
        var art = new HdInterfaceArt("synthetic-font.art", identity, rasters);
        var hd = new byte[W * Scale * H * Scale * 4];
        for (var y = 4; y < 16; y++)
        for (var x = 8; x < 16; x++)
        { var at = (y * W * Scale + x) * 4; hd[at] = hd[at + 1] = hd[at + 2] = hd[at + 3] = 255; }
        var directory = System.IO.Path.Combine(root, "font-pack");
        var written = HdInterfacePackWriter.Write(directory, "font-selftest", Scale, [new HdBuiltInterfaceFrame(art, Glyph, W, H, hd, new(1, 1, 0, 0, 1), Masked: true)], DateTimeOffset.UnixEpoch);
        require(written.Status == HdInterfaceWriteStatus.Written, "the font pack is written: " + written.Status + " " + written.Detail);
        var pack = HdPack.Open(directory).Pack!;
        var frames = Enumerable.Range(0, 225).Select(i => i == Glyph || i == NoEntry
            ? new AreaInterfaceFrame(W, H, legacy, W, 0, HdInterfaceKey.Query(identity, i, W, H, legacy)) : new AreaInterfaceFrame(1, 1, new byte[4])).ToArray();
        var face = new AreaArtFontFace(frames);

        InterfaceHd.Configure(null, false);
        require(!face.IsHd(Glyph) && face.Texture(Glyph, AreaArtTextBlend.Coverage) is not CanvasTexture, "no pack, switch off: the Legacy glyph");
        InterfaceHd.Configure(pack, false);
        require(!face.IsHd(Glyph), "a pack alone does not switch the glyph");
        InterfaceHd.Configure(pack, true);
        require(face.IsHd(Glyph) && face.Texture(Glyph, AreaArtTextBlend.Coverage) is CanvasTexture, "pack and switch on: the glyph with an entry is HD");
        require(!face.IsHd(NoEntry) && !face.IsHd(0), "a glyph without an entry and a frame without a key stay Legacy");
        require(face.Texture(Glyph, AreaArtTextBlend.Additive) is not CanvasTexture && face.Texture(Glyph, AreaArtTextBlend.Multiply) is not CanvasTexture, "only the Coverage blend takes the HD frame");
        require(face.Texture(Glyph, AreaArtTextBlend.Coverage)!.GetWidth() == W * Scale, "the HD glyph texture is 4× the Legacy frame");

        var viewport = new SubViewport { Size = new(W, H), TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var probe = new FontProbe(face) { TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        viewport.AddChild(probe);
        host.AddChild(viewport);
        try
        {
            async Task<Image?> Shot()
            {
                probe.QueueRedraw();
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!native) return null;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                return viewport.GetTexture().GetImage();
            }
            using (var hdShot = await Shot())
                if (hdShot is not null)
                {
                    require(Math.Abs(hdShot.GetPixel(2, 2).R8 - 254) <= 3 && Math.Abs(hdShot.GetPixel(3, 1).R8 - 254) <= 3, $"the HD glyph is drawn at 1:1 ({hdShot.GetPixel(2, 2).R8})");
                    require(hdShot.GetPixel(1, 2).R8 == 0 && hdShot.GetPixel(4, 2).R8 == 0 && hdShot.GetPixel(2, 0).R8 == 0, "outside the glyph the canvas stays black");
                }
            InterfaceHd.Configure(pack, false);
            using (var legacyShot = await Shot())
                if (legacyShot is not null)
                    require(Math.Abs(legacyShot.GetPixel(2, 2).R8 - 199) <= 2, $"the Legacy glyph is drawn with the switch off ({legacyShot.GetPixel(2, 2).R8})");
            InterfaceHd.Configure(pack, true);
            require(face.IsHd(Glyph), "the switch on again: HD");
            InterfaceHd.Configure(HdPack.Open(directory).Pack!, true);
            require(face.IsHd(Glyph), "a reopened pack binds the glyph again");
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            viewport.QueueFree();
        }
        GD.Print(native ? "ARC-DEV-361 self-test HD font glyphs PASS" : "ARC-DEV-361 self-test HD font glyphs PASS (pixel checks skipped: headless)");
    }

    private sealed partial class FontProbe : Control
    {
        private readonly AreaArtFontFace _face;

        public FontProbe(AreaArtFontFace face) => _face = face;

        public override void _Ready() => AreaArtTextDraw.Prepare(this);

        public override void _Draw()
        {
            DrawRect(new Rect2(0, 0, 6, 5), Colors.Black);
            AreaArtTextDraw.Text(this, _face, "A", 0, 0, (255, 255, 255));
        }
    }

    private sealed partial class RegionProbe(Texture2D texture, Rect2 destination, Rect2 source) : Control
    {
        public override void _Draw() => DrawTextureRectRegion(texture, destination, source);
    }
}
