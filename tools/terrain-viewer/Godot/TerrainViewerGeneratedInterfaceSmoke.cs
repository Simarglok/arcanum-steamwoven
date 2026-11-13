using Arcanum.World;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-140: opt-in part of the native bar smoke (ARC_AREA_BAR_GENERATED_VERIFY=1). Both strips and every bar button have a
    // recipe, so each draws generated chrome at the same size and place as its Legacy frame; the layout and hit tests are unchanged.
    private async Task VerifyGeneratedInterfaceAsync(AreaGameBarPanel panel, Func<int, Task> frames)
    {
        static void Require(bool ok, string why)
        {
            if (ok) return;
            GD.Print("ARC-UI-140 generated interface smoke FAIL " + why);
            throw new InvalidOperationException("ARC-UI-140 generated interface smoke: " + why);
        }
        await frames(2);
        Require(InterfaceHd.Generated, "the generated interface is on");
        // ARC-UI-141: the frames are painted on workers at the window's exact device scale; ask for every one, then wait for the swap (at most ~20 s).
        var wanted = new List<HdInterfaceTexture>();
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
            if (panel.GetNode<TextureRect>($"{strip}Strip/Art").Texture is HdInterfaceTexture stripTexture) wanted.Add(stripTexture);
        foreach (var spec in AreaGameBarLayout.Buttons)
            wanted.AddRange((panel.ArtFrames(spec.Art) ?? []).OfType<HdInterfaceTexture>());
        long slowestFrame = 0;
        async Task WaitPaintedAsync()
        {
            for (var wait = 0; wait < 1200 && (wanted.Any(texture => !texture.IsGenerated) || InterfaceHd.GeneratedPending > 0); wait++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await frames(1);
                slowestFrame = Math.Max(slowestFrame, clock.ElapsedMilliseconds);
            }
            await frames(2);
        }
        await WaitPaintedAsync();
        var scale = panel.Factor * GetViewport().GetFinalTransform().Scale.X;
        Require(InterfaceHd.GeneratedScale == Math.Clamp((int)Math.Round(scale), 1, InterfaceChromeRenderer.MaxScale) && InterfaceHd.GeneratedPending == 0,
            $"the chrome is painted at the device scale {scale} (painted at {InterfaceHd.GeneratedScale}, pending {InterfaceHd.GeneratedPending})");
        var checkedCount = 0;
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
        {
            var art = panel.GetNode<TextureRect>($"{strip}Strip/Art");
            Require(art.Texture is HdInterfaceTexture { IsGenerated: true } texture
                && texture.GetSize() == new Vector2(AreaGameBarLayout.ScreenWidth, strip == AreaBarStrip.Top ? AreaGameBarLayout.TopHeight : AreaGameBarLayout.BottomHeight),
                $"the {strip} strip draws generated chrome at its Legacy size");
            var pixels = ((HdInterfaceTexture)art.Texture!).GeneratedPixelSize;
            Require(pixels == new Vector2I(AreaGameBarLayout.ScreenWidth * InterfaceHd.GeneratedScale, (strip == AreaBarStrip.Top ? AreaGameBarLayout.TopHeight : AreaGameBarLayout.BottomHeight) * InterfaceHd.GeneratedScale),
                $"the {strip} strip's pixels are its Legacy size times the device scale ({pixels})");
            checkedCount++;
        }
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var frames0 = panel.ArtFrames(spec.Art);
            Require(frames0 is { Length: > 0 }, $"{spec.Button} has art");
            foreach (var frame in frames0!)
            {
                Require(frame is HdInterfaceTexture { IsGenerated: true }, $"{spec.Button} frame draws generated chrome");
                checkedCount++;
            }
            var control = panel.GetNode<TextureRect>($"{spec.Strip}Strip/{spec.Button}");
            var (left, top, _, _) = AreaGameBarLayout.StripRect(spec.Strip);
            Require(control.Position == new Vector2(spec.X - left, spec.Y - top) && control.Size == control.Texture?.GetSize(),
                $"{spec.Button} stays at its Legacy place and size");
        }
        Require(InterfaceHd.GeneratedCount() >= wanted.Distinct().Count(), $"the generated frames are counted ({InterfaceHd.GeneratedCount()} of {wanted.Distinct().Count()})");
        // ARC-UI-141: a bigger window (1600 x 1200 = factor 2) paints the chrome again at the new exact scale, every pixel 1:1; then the window goes back.
        // A screen too small to hold it is reported and skipped.
        var originalSize = DisplayServer.WindowGetSize();
        var originalFactor = panel.Factor;
        var doubleSize = new Vector2I(2 * AreaGameBarLayout.ScreenWidth, 2 * AreaGameBarLayout.ScreenHeight);
        Vector2I StripPixels(AreaBarStrip strip) => ((HdInterfaceTexture)panel.GetNode<TextureRect>($"{strip}Strip/Art").Texture!).GeneratedPixelSize;
        Vector2I StripUnits(AreaBarStrip strip) => new(AreaGameBarLayout.ScreenWidth, strip == AreaBarStrip.Top ? AreaGameBarLayout.TopHeight : AreaGameBarLayout.BottomHeight);
        try
        {
            DisplayServer.WindowSetSize(doubleSize);
            for (var wait = 0; wait < 120 && panel.Factor < 2; wait++) await frames(1);
            if (panel.Factor >= 2)
            {
                await WaitPaintedAsync();
                var scale2 = panel.Factor * GetViewport().GetFinalTransform().Scale.X;
                Require(InterfaceHd.GeneratedScale == (int)Math.Round(scale2) && InterfaceHd.GeneratedPending == 0, $"the chrome is painted again at the device scale {scale2}");
                foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
                    Require(StripPixels(strip) == StripUnits(strip) * InterfaceHd.GeneratedScale, $"the {strip} strip is repainted at the bigger scale ({StripPixels(strip)})");
                await CaptureHudAsync("generated-x" + panel.Factor, frames);
                GD.Print($"ARC-UI-141 generated interface repainted at factor={panel.Factor} device-scale={InterfaceHd.GeneratedScale}");
            }
            else GD.Print($"ARC-UI-141 generated interface bigger window skipped: factor stayed {panel.Factor} at {DisplayServer.WindowGetSize()}");
        }
        finally
        {
            DisplayServer.WindowSetSize(originalSize);
            for (var wait = 0; wait < 120 && panel.Factor != originalFactor; wait++) await frames(1);
        }
        await WaitPaintedAsync();
        Require(panel.Factor == originalFactor && InterfaceHd.GeneratedScale == (int)Math.Round(originalFactor * GetViewport().GetFinalTransform().Scale.X), "the window and the scale went back");
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
            Require(StripPixels(strip) == StripUnits(strip) * InterfaceHd.GeneratedScale, $"the {strip} strip is back at the first scale ({StripPixels(strip)})");
        GD.Print($"ARC-UI-141 slowest frame while the chrome was painted: {slowestFrame} ms");
        GD.Print($"ARC-UI-140 native generated interface PASS textures={checkedCount} factor={panel.Factor} device-scale={InterfaceHd.GeneratedScale}");
    }

    // ARC-UI-148: the open inventory window in the generated interface. Its frames (the pack 221, the paper doll 223 and the lens 233) draw generated chrome
    // painted at the window's own device scale (the frame scale times the canvas transform's, fractional in the inventory), at the Legacy size
    // in layout; a bigger window paints them again at the new scale. The window is open when this runs.
    private Task VerifyGeneratedInventoryAsync(Func<int, Task> frames, Func<Vector2I, Task> resize)
    {
        if (!(InterfaceHd.Generated && _inventoryPanel is { ArtMode: true } panel && panel.GridAreaForSelfTest.Look is not null))
        {
            GD.Print("ARC-UI-148 generated inventory smoke FAIL the generated interface is on and the inventory shows the original's look");
            throw new InvalidOperationException("ARC-UI-148 generated inventory smoke: the generated interface is on and the inventory shows the original's look");
        }
        return VerifyGeneratedWindowAsync("ARC-UI-148", "inventory", panel.GridAreaForSelfTest,
            [AreaInventoryArtLayout.PackArt, AreaInventoryArtLayout.DollArt, AreaInventoryHole.InventoryArt], frames, resize,
            at => CaptureHudAsync(at == "first" ? "inventory-generated" : at == "big" ? "inventory-generated-big" : "inventory-generated-back", frames));
    }

    // ARC-UI-148 / ARC-UI-1194: an open window's frames in the generated interface. Each art of the window's look (its textures claim the window's own device scale
    // at every draw) draws generated chrome painted at round(size x paint scale) pixels; a bigger window paints them again at the new scale; then the window goes back.
    private async Task VerifyGeneratedWindowAsync(string task, string window, AreaInventoryGridArea area, IReadOnlyList<int> arts, Func<int, Task> frames, Func<Vector2I, Task> resize, Func<string, Task> capture)
    {
        void Require(bool ok, string why)
        {
            if (ok) return;
            GD.Print($"{task} generated {window} smoke FAIL {why}");
            throw new InvalidOperationException($"{task} generated {window} smoke: {why}");
        }
        Require(InterfaceHd.Generated && area.Look is not null, "the generated interface is on and the window shows the original's look");
        var textures = arts
            .Select(number => area.Look!.Art.GetValueOrDefault(number) as HdInterfaceTexture ?? throw new InvalidOperationException($"{task} generated {window} smoke: art {number} is no interface texture"))
            .ToArray();
        double Device() => area.DeviceScale();
        async Task Painted(string at)
        {
            // Each draw asks for the frame at the window's current scale; wait for the worker and the swap (at most ~20 s).
            for (var wait = 0; wait < 1200 && (textures.Any(texture => !texture.IsGenerated || texture.GeneratedPixelSize != Wanted(texture)) || InterfaceHd.GeneratedPending > 0); wait++) await frames(1);
            await frames(2);
            foreach (var (texture, number) in textures.Zip(arts))
                Require(texture.IsGenerated && texture.GeneratedPixelSize == Wanted(texture), $"{at}: frame {number} is generated at {Wanted(texture)} (got {texture.GeneratedPixelSize}, device scale {Device():F3})");
        }
        Vector2I Wanted(HdInterfaceTexture texture)
        {
            var (w, h) = InterfaceChromeRenderer.PixelSize((int)texture.GetSize().X, (int)texture.GetSize().Y, InterfaceHd.PaintScale(Device()));
            return new Vector2I(w, h);
        }
        await Painted("at the first size");
        await capture("first");
        var firstScale = Device();
        var size = DisplayServer.WindowGetSize();
        try
        {
            var bigger = new Vector2I(Math.Max(size.X, 1920), Math.Max(size.Y, 1080));
            await resize(bigger);
            await frames(3);
            await Painted("at the bigger size");
            await capture("big");
            GD.Print($"{task} generated {window} scales {firstScale:F3} -> {Device():F3}");
            Require(Math.Abs(Device() - firstScale) > 0.01 || bigger == size, "the window scale changed with the window");
        }
        finally { await resize(size); }
        await frames(3);
        await Painted("back at the first size");
        await capture("back");
        GD.Print($"{task} native generated {window} PASS frames={textures.Length} device-scale={Device():F3}");
    }

    // ARC_AREA_BAR_CAPTURE=PREFIX also saves the whole viewport with the bars as PREFIX-hud-MODE.png (a local, ignored path).
    private async Task CaptureHudAsync(string mode, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        await frames(2);
        var path = System.IO.Path.GetFullPath(capture) + $"-hud-{mode}.png";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-140 HUD readback not saved: " + path);
        GD.Print($"ARC-UI-140 HUD capture {mode} {image.GetWidth()}x{image.GetHeight()}");
    }
}
