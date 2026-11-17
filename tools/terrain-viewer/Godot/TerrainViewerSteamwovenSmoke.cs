using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2452: opt-in part of the native bar smoke (ARC_AREA_BAR_STEAMWOVEN=1, with ARC_HD_PACK: the generated interface runs the HD path). The play screen is laid out by the
    // Steamwoven layout model at each size of ARC_STEAMWOVEN_SIZES (default: the TRD's verification sizes, SWI-VER-6) for each panel-height variant of ARC_STEAMWOVEN_VARIANTS
    // (default "thin,original": SWI-LAY-10's 120 and 456 master px, and the original strips' 150 and 570), with and without an open menu. The window is not resized (a Mac screen may be smaller than 3840 x 2160): the root
    // viewport is given the size as its logical render target (content scale mode Viewport), so the captures have exactly the requested size.
    // ARC_AREA_BAR_CAPTURE=PREFIX saves PREFIX-hud-steamwoven-VARIANT-WxH[-menu].png (local, never in git).
    private static IReadOnlyList<Vector2I> SteamwovenSizes()
    {
        var text = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_SIZES");
        if (string.IsNullOrWhiteSpace(text)) text = "3840x2160,2560x1440,1920x1080,2560x1600,1920x1200,1280x800,1366x768,3440x1440,1024x768";
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('x')).Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]))).ToList();
    }

    private static IReadOnlyList<(string Name, SteamwovenLayoutOptions Options)> SteamwovenVariants()
    {
        var text = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_VARIANTS");
        if (string.IsNullOrWhiteSpace(text)) text = "thin,original";
        var list = new List<(string, SteamwovenLayoutOptions)>();
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "before" is the whole-factor layout of the generated interface as it was (captured for the owner's comparison; no layout checks).
            var options = name switch { "thin" or "before" => SteamwovenLayoutOptions.Default, "original" => SteamwovenHud.Original, _ => SteamwovenHud.ParseOptions(name.Replace('-', ',')) };
            if (options is null) throw new InvalidOperationException($"ARC-UI-2452 steamwoven smoke: ARC_STEAMWOVEN_VARIANTS entry '{name}' is not thin, original, before or TOP-BOTTOM master px");
            list.Add((name, options));
        }
        return list;
    }

    private async Task VerifySteamwovenLayoutAsync(AreaGameBarPanel panel, Func<int, Task> frames)
    {
        static void Require(bool ok, string why)
        {
            if (ok) return;
            GD.Print("ARC-UI-2452 steamwoven smoke FAIL " + why);
            throw new InvalidOperationException("ARC-UI-2452 steamwoven smoke: " + why);
        }
        Require(InterfaceHd.Generated && InterfaceHd.Enabled, "the HD option with the generated interface is on");
        var window = GetWindow();
        var (savedMode, savedAspect, savedSize, savedFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
        var checkedSizes = 0;
        SteamwovenHud.Suspended = false;
        panel.Relayout();
        try
        {
            window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            window.ContentScaleFactor = 1;
            foreach (var (variant, options) in SteamwovenVariants())
            {
                SteamwovenHud.Options = options;
                panel.Relayout();
                foreach (var size in SteamwovenSizes())
                {
                    window.ContentScaleSize = size;
                    for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                    Require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                    await frames(2);
                    var tag = $"{variant}-{size.X}x{size.Y}";
                    if (variant == "before")
                    {
                        SteamwovenHud.Suspended = true; panel.Relayout();
                        await WaitBarArtGeneratedAsync(panel, frames);
                        await CaptureHudAsync("steamwoven-" + tag, frames);
                        OpenInventory(); await frames(3); await WaitBarArtGeneratedAsync(panel, frames);
                        await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
                        EndInventory(); await frames(3);
                        SteamwovenHud.Suspended = false; panel.Relayout();
                        continue;
                    }
                    var expected = SteamwovenHud.Plan(size.X, size.Y, options);
                    async Task WaitPaintedAsync()
                    {
                        for (var wait = 0; wait < 1800 && (InterfaceHd.GeneratedPending > 0 || panel.BoardsPendingForSelfTest || !AllStripArtGenerated(panel)); wait++) await frames(1);
                        await frames(2);
                    }
                    await WaitPaintedAsync();
                    var plan = panel.PlanForSelfTest;
                    Require(expected is not null && plan is not null && plan.Layout == expected.Layout && plan.StripLeft == expected.StripLeft, $"[{tag}] the panel is laid out by the plan");
                    var layout = plan!.Layout;
                    // The strips: one scale, placed by the plan; the insets are the middle band's, so every docked window lies in it.
                    Require(Math.Abs(panel.StripScale - (float)plan.StripScale) < 1e-4f, $"[{tag}] both strips are drawn at the plan's scale {plan.StripScale:F4} (got {panel.StripScale})");
                    Require(panel.StripRectForSelfTest(AreaBarStrip.Top).Position == new Vector2(plan.StripLeft, plan.TopStripY)
                        && panel.StripRectForSelfTest(AreaBarStrip.Bottom).Position == new Vector2(plan.StripLeft, plan.BottomStripY), $"[{tag}] the strips stand where the plan says");
                    Require(AreaGameBarPanel.TopInset == layout.Middle.Band.Y && AreaGameBarPanel.BottomInset == size.Y - layout.Middle.Band.Bottom
                        && AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y)).Size.Y == layout.Middle.Band.Height, $"[{tag}] the free area is the middle band {layout.Middle.Band}");
                    // The top band's art ends on the board's edge and the bottom band's starts on it: each strip lies inside its own band (the bottom one may hang over the frame's bottom edge by under 3 px · f).
                    Require(plan.TopStripY >= layout.Top.Band.Y && plan.TopStripY + (int)Math.Round(41 * plan.StripScale) == layout.Top.Band.Bottom, $"[{tag}] the top strip stands on its band's lower edge");
                    Require(plan.BottomStripY == layout.Bottom.Band.Y, $"[{tag}] the bottom strip hangs from its band's upper edge");
                    // The boards fill each band over the whole frame width in the strips' own device pixels, one texel per pixel.
                    var (topBoard, bottomBoard, backdrop, backdropShown) = panel.BoardsForSelfTest;
                    Require(panel.BoardsShownForSelfTest && !backdropShown, $"[{tag}] the boards show and no backdrop shows with no menu");
                    Require(topBoard.PixelSizeForSelfTest == new Vector2I(layout.Top.Band.Width, layout.Top.Band.Height) && topBoard.Position == new Vector2(layout.Top.Band.X, layout.Top.Band.Y),
                        $"[{tag}] the top board is the top band {layout.Top.Band} (pixels {topBoard.PixelSizeForSelfTest})");
                    Require(bottomBoard.PixelSizeForSelfTest == new Vector2I(layout.Bottom.Band.Width, layout.Bottom.Band.Height) && bottomBoard.Position == new Vector2(layout.Bottom.Band.X, layout.Bottom.Band.Y),
                        $"[{tag}] the bottom board is the bottom band {layout.Bottom.Band} (pixels {bottomBoard.PixelSizeForSelfTest})");
                    // The bars and the frame cover the window exactly once.
                    long barArea = panel.BarsForSelfTest.Where(bar => bar.Visible).Sum(bar => (long)bar.Size.X * (long)bar.Size.Y);
                    Require(barArea + (long)layout.Frame.Width * layout.Frame.Height == (long)size.X * size.Y, $"[{tag}] the bars ({barArea}) and the frame {layout.Frame} fill the window");
                    await CaptureHudAsync("steamwoven-" + tag, frames);
                    await CheckSteamwovenPixelsAsync(tag, layout, Require);
                    // An open menu: the backdrop fills the whole middle band, the window docks in it.
                    OpenInventory();
                    await frames(3);
                    Require(InventoryOpen, $"[{tag}] the inventory opened");
                    await WaitPaintedAsync();
                    (_, _, backdrop, backdropShown) = panel.BoardsForSelfTest;
                    Require(backdropShown && backdrop.Position == new Vector2(layout.Middle.Band.X, layout.Middle.Band.Y) && backdrop.PixelSizeForSelfTest == new Vector2I(layout.Middle.Band.Width, layout.Middle.Band.Height),
                        $"[{tag}] the open menu has its backdrop over the middle band {layout.Middle.Band} (pixels {backdrop.PixelSizeForSelfTest})");
                    await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
                    EndInventory();
                    await frames(3);
                    Require(!panel.BoardsForSelfTest.BackdropShown, $"[{tag}] the backdrop goes with the menu");
                    checkedSizes++;
                    GD.Print($"ARC-UI-2452 steamwoven {tag} scale={plan.StripScale:F4} step={layout.Step} top={layout.Top.Band} middle={layout.Middle.Band} bottom={layout.Bottom.Band} bars={layout.Bars}");
                }
            }
        }
        finally
        {
            SteamwovenHud.Options = SteamwovenLayoutOptions.Default;
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            SteamwovenHud.Suspended = true;
            panel.Relayout();
            await frames(3);
        }
        // With the generated interface off (the HD option still on) the whole-factor layout is back: no plan, the strips at the whole factor, the old insets.
        _gameSettings = _gameSettings with { GeneratedInterface = false };
        SyncInterfaceHd();
        SteamwovenHud.Suspended = false; panel.Relayout();
        await frames(3);
        Require(panel.PlanForSelfTest is null && !panel.BoardsShownForSelfTest && Math.Abs(panel.StripScale - panel.Factor) < 1e-4f && InterfaceHd.FractionalScale is null
            && AreaGameBarPanel.TopInset == AreaGameBarLayout.TopHeight * panel.Factor && AreaGameBarPanel.BottomInset == AreaGameBarLayout.BottomHeight * panel.Factor,
            "without the generated interface the whole-factor layout stands");
        _gameSettings = _gameSettings with { GeneratedInterface = true };
        SyncInterfaceHd();
        SteamwovenHud.Suspended = true; panel.Relayout();
        await frames(3);
        await WaitBarArtGeneratedAsync(panel, frames);
        GD.Print($"ARC-UI-2452 native steamwoven layout PASS sizes={checkedSizes}");
    }

    // Both strips and every frame of every bar button draw generated chrome (asking for it paints it on a worker).
    private static bool AllStripArtGenerated(AreaGameBarPanel panel) =>
        new[] { AreaBarStrip.Top, AreaBarStrip.Bottom }.All(strip => panel.GetNode<TextureRect>($"{strip}Strip/Art").Texture is HdInterfaceTexture { IsGenerated: true })
        && AreaGameBarLayout.Buttons.All(spec => (panel.ArtFrames(spec.Art) ?? []).OfType<HdInterfaceTexture>().All(texture => texture.IsGenerated));

    private static async Task WaitBarArtGeneratedAsync(AreaGameBarPanel panel, Func<int, Task> frames)
    {
        for (var wait = 0; wait < 1800 && (InterfaceHd.GeneratedPending > 0 || !AllStripArtGenerated(panel)); wait++) await frames(1);
        await frames(2);
    }

    // Reads the viewport back: a bar is black, and the wood beside a strip is wood (not the world, not black) in both bands.
    private async Task CheckSteamwovenPixelsAsync(string tag, SteamwovenLayout layout, Action<bool, string> require)
    {
        await Task.CompletedTask;
        using var image = GetViewport().GetTexture().GetImage();
        require(image is not null && image.GetSize() == new Vector2I(layout.WindowWidth, layout.WindowHeight), $"[{tag}] the viewport read back at the window's size");
        var bars = layout.Bars;
        if (bars.Left >= 2) require(image!.GetPixel(bars.Left / 2, layout.WindowHeight / 2).IsEqualApprox(Colors.Black), $"[{tag}] the left bar is black");
        if (bars.Top >= 2) require(image!.GetPixel(layout.WindowWidth / 2, bars.Top / 2).IsEqualApprox(Colors.Black), $"[{tag}] the top bar is black");
        if (layout.Top.LeftSide.Width >= 8)
        {
            var side = layout.Top.LeftSide;
            var colour = image!.GetPixel(side.X + side.Width / 2, side.Y + side.Height / 2);
            require(colour.R > colour.B && colour.R > 0.05f, $"[{tag}] the top band's left side is wood ({colour})");
            var bottomSide = layout.Bottom.RightSide;
            var bottomColour = image.GetPixel(bottomSide.X + bottomSide.Width / 2, bottomSide.Y + bottomSide.Height / 2);
            require(bottomColour.R > bottomColour.B && bottomColour.R > 0.05f, $"[{tag}] the bottom band's right side is wood ({bottomColour})");
        }
    }
}
