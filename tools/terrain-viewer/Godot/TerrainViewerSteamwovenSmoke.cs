using Arcanum.Data.Area;
using Arcanum.Data.Written;
using Arcanum.Presentation.Hud;
using Arcanum.Presentation.Steamwoven;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2452: opt-in part of the native bar smoke (ARC_AREA_BAR_STEAMWOVEN=1, with ARC_HD_PACK: the generated interface runs the HD path). The play screen is laid out by the
    // Steamwoven layout model at each size of ARC_STEAMWOVEN_SIZES (default: the TRD's verification sizes, SWI-VER-6) for each panel-height variant of ARC_STEAMWOVEN_VARIANTS
    // (default "thin,original,column,column-thin": SWI-LAY-10's 120 and 456 master px, the original strips' 150 and 570, and the column mode of ARC-UI-2890 with k = 1 and k = 0.8), with and without an open menu. The window takes the size's width, not its height (a Mac screen may be smaller than 3840 x 2160; ARC-UI-2696, FitWindowToRenderTargetAsync): the root
    // viewport is given the size as its logical render target (content scale mode Viewport), so the captures have exactly the requested size.
    // ARC_AREA_BAR_CAPTURE=PREFIX saves PREFIX-hud-steamwoven-VARIANT-WxH[-menu].png (local, never in git).
    // ARC-DEV-2672: the variant "legacy" is the Original look (the HD option off: Legacy textures, no plan, the whole-factor layout) at the same sizes and windows, for side-by-side comparison with the Steamwoven
    // layout; its pass makes no layout checks (only that the HD option is off) and restores the settings after it. The names are PREFIX-hud-steamwoven-legacy-WxH[-menu|-WINDOW].png.
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
        if (string.IsNullOrWhiteSpace(text)) text = "thin,original,column,column-thin";
        var list = new List<(string, SteamwovenLayoutOptions)>();
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "before" is the whole-factor layout of the generated interface as it was (captured for the owner's comparison; no layout checks).
            // ARC-UI-2890: "column" and "column-thin" are the column mode (the original's strip width, the world at the sides; k = 1 and k = 0.8).
            var options = name switch { "thin" or "before" or "legacy" => SteamwovenLayoutOptions.Default, "original" => SteamwovenHud.Original, "column" => SteamwovenLayoutOptions.ColumnOriginal, "column-thin" => SteamwovenLayoutOptions.ColumnThin, _ => SteamwovenHud.ParseOptions(name.Replace('-', ',')) };
            if (options is null) throw new InvalidOperationException($"ARC-UI-2452 steamwoven smoke: ARC_STEAMWOVEN_VARIANTS entry '{name}' is not thin, original, column, column-thin, before, legacy or TOP-BOTTOM master px");
            list.Add((name, options));
        }
        return list;
    }

    /// <summary>
    /// ARC-UI-2696: gives the window the render target's width, so the viewport's stretch is 1 across (the content scale mode Viewport draws the logical target at the window's size: a window narrower than the target, the
    /// default 1280 wide for a 2560 target, has the stretch 0.5, and every generated texture (the strips', the windows') is painted at the real window's scale, clamped to 1 unit per Legacy pixel, and drawn up
    /// again, softer than the boards beside them, which are painted at the logical scale; the captures then showed two woods). The height is the operating system's to limit (a Mac's screen is lower than
    /// 2160 or 1440), the stretch along it does not matter to the paint (it reads the stretch across).
    /// </summary>
    private async Task FitWindowToRenderTargetAsync(AreaGameBarPanel panel, Window window, Vector2I size, Func<int, Task> frames, Action<bool, string> require)
    {
        // ARC_STEAMWOVEN_FIT_WINDOW=0 keeps the window as it is (the old captures, for a comparison of the two woods).
        if (System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_FIT_WINDOW") == "0") return;
        window.Size = size;
        for (var wait = 0; wait < 120 && window.Size.X != size.X; wait++) await frames(1);
        require(window.Size.X == size.X, $"the window took the render target's width {size.X}, got {window.Size}");
        await frames(3);
        // A new stretch moves no viewport size, so nothing else tells the bars to paint at it.
        panel.Relayout();
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
        var savedWindowSize = window.Size;
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
                if (variant == "legacy")
                {
                    await CaptureSteamwovenLegacyAsync(panel, window, frames, Require);
                    SteamwovenHud.Options = options;
                    continue;
                }
                foreach (var size in SteamwovenSizes())
                {
                    window.ContentScaleSize = size;
                    await FitWindowToRenderTargetAsync(panel, window, size, frames, Require);
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
                        await VerifySteamwovenWindowsAsync(tag, null, panel, frames, () => WaitBarArtGeneratedAsync(panel, frames), Require);
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
                    // ARC-UI-2696: the strips' generated wood is painted at the scale it is drawn at, as the boards' is (one texel per device pixel): else the strips' wood is softer than the boards' beside it.
                    var woodAtScale = InterfaceHd.FractionalScale is { } paintedScale && Math.Abs(InterfaceHd.PaintScale(paintedScale) - InterfaceHd.PaintScale(plan.StripScale)) < 0.002; // a generated frame is painted at one pixel per unit at least (PaintScale), so a strip scale under 1 (1280 x 800 at k = 0.8: 0.96) is drawn down from 1
                    var woodNote = $"[{tag}] the strips' wood is painted at the scale it is drawn at, {plan.StripScale:F4} (painted at {InterfaceHd.FractionalScale}, viewport stretch {GetViewport().GetFinalTransform().Scale})";
                    if (System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_FIT_WINDOW") == "0") GD.Print("ARC-UI-2696 steamwoven " + woodNote + (woodAtScale ? " yes" : " NO (the window is not fitted)"));
                    else Require(woodAtScale, woodNote);
                    Require(panel.StripRectForSelfTest(AreaBarStrip.Top).Position == new Vector2(plan.StripLeft, plan.TopStripY)
                        && panel.StripRectForSelfTest(AreaBarStrip.Bottom).Position == new Vector2(plan.StripLeft, plan.BottomStripY), $"[{tag}] the strips stand where the plan says");
                    Require(AreaGameBarPanel.TopInset == layout.Middle.Band.Y && AreaGameBarPanel.BottomInset == size.Y - layout.Middle.Band.Bottom
                        && AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y)).Size.Y == layout.Middle.Band.Height, $"[{tag}] the free area is the middle band {layout.Middle.Band}");
                    // The top band's art ends on the board's edge and the bottom band's starts on it: each strip lies inside its own band (the bottom one may hang over the frame's bottom edge by under 3 px · f).
                    Require(plan.TopStripY >= layout.Top.Band.Y && plan.TopStripY + (int)Math.Round(41 * plan.StripScale) == layout.Top.Band.Bottom, $"[{tag}] the top strip stands on its band's lower edge");
                    Require(plan.BottomStripY == layout.Bottom.Band.Y, $"[{tag}] the bottom strip hangs from its band's upper edge");
                    // ARC-UI-2767: the Fate Points and Sleep balls stand in the middle of the ring the strip draws round their hole (the ring's centre is read from the strip's Legacy hole, the ball's from its node).
                    foreach (var ball in new[] { AreaBarButton.FatePoints, AreaBarButton.Sleep })
                    {
                        var ring = panel.RingCentreForSelfTest(ball);
                        Require(ring is not null, $"[{tag}] the {ball} button has a ring round its hole");
                        var off = panel.BallCentreForSelfTest(ball) - ring!.Value;
                        GD.Print($"ARC-UI-2767 steamwoven [{tag}] {ball} ball centre minus ring centre: ({off.X:F3}, {off.Y:F3}) px");
                        Require(Math.Abs(off.X) <= 0.01f && Math.Abs(off.Y) <= 0.01f, $"[{tag}] the {ball} ball is centred in its ring (off by {off} px)");
                    }
                    if (options.Column is not null)
                    {
                        await CheckSteamwovenColumnPlayAsync(tag, plan, size, panel, WaitPaintedAsync, frames, Require);
                        await VerifySteamwovenWindowsAsync(tag, plan, panel, frames, WaitPaintedAsync, Require);
                        checkedSizes++;
                        GD.Print($"ARC-UI-2890 steamwoven {tag} scale={plan.StripScale:F4} step={layout.Step} column={plan.Column!.Column} window={plan.WindowZone} worldLeft={plan.Column.WorldLeft} worldRight={plan.Column.WorldRight} bars={layout.Bars}");
                        continue;
                    }
                    // The boards fill each band over the whole frame width in the strips' own device pixels, one texel per pixel.
                    var (topBoard, bottomBoard, backdropShown) = panel.BoardsForSelfTest;
                    Require(panel.BoardsShownForSelfTest && !backdropShown, $"[{tag}] the boards show and no backdrop shows with no menu");
                    Require(topBoard.PixelSizeForSelfTest == new Vector2I(layout.Top.Band.Width, layout.Top.Band.Height) && topBoard.Position == new Vector2(layout.Top.Band.X, layout.Top.Band.Y),
                        $"[{tag}] the top board is the top band {layout.Top.Band} (pixels {topBoard.PixelSizeForSelfTest})");
                    Require(bottomBoard.PixelSizeForSelfTest == new Vector2I(layout.Bottom.Band.Width, layout.Bottom.Band.Height) && bottomBoard.Position == new Vector2(layout.Bottom.Band.X, layout.Bottom.Band.Y),
                        $"[{tag}] the bottom board is the bottom band {layout.Bottom.Band} (pixels {bottomBoard.PixelSizeForSelfTest})");
                    // The bars and the frame cover the window exactly once.
                    long barArea = panel.BarsForSelfTest.Where(bar => bar.Visible).Sum(bar => (long)bar.Size.X * (long)bar.Size.Y);
                    Require(barArea + (long)layout.Frame.Width * layout.Frame.Height == (long)size.X * size.Y, $"[{tag}] the bars ({barArea}) and the frame {layout.Frame} fill the window");
                    await CheckSteamwovenScreenPartsAsync(tag, plan, size, frames, Require);
                    await CaptureSteamwovenPartsAsync(tag, frames);
                    await CaptureHudAsync("steamwoven-" + tag, frames);
                    await CheckSteamwovenPixelsAsync(tag, layout, Require);
                    // An open menu: the backdrop fills the whole middle band, the window docks in it.
                    OpenInventory();
                    await frames(3);
                    Require(InventoryOpen, $"[{tag}] the inventory opened");
                    await WaitPaintedAsync();
                    CheckBackdrop(tag, "inventory", SteamwovenBackdropFamily.Inventory, plan, panel, Require);
                    await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
                    EndInventory();
                    await frames(3);
                    Require(!panel.BoardsForSelfTest.BackdropShown, $"[{tag}] the backdrop goes with the menu");
                    await VerifySteamwovenWindowsAsync(tag, plan, panel, frames, WaitPaintedAsync, Require);
                    checkedSizes++;
                    GD.Print($"ARC-UI-2452 steamwoven {tag} scale={plan.StripScale:F4} step={layout.Step} top={layout.Top.Band} middle={layout.Middle.Band} bottom={layout.Bottom.Band} bars={layout.Bars}");
                }
            }
        }
        finally
        {
            SteamwovenHud.Options = SteamwovenLayoutOptions.Default;
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            window.Size = savedWindowSize;
            await frames(3);
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
        Require(AreaGameBarPanel.TopScreen is null && AreaGameBarPanel.FreeLeft == 0 && AreaGameBarPanel.FreeArea(GetViewport().GetVisibleRect().Size).Size.X == GetViewport().GetVisibleRect().Size.X,
            "without the layout the screen is the whole-factor one and the free width is the whole window's (ARC-UI-2510)");
        Require(AreaGameBarPanel.FreeRect(GetViewport().GetVisibleRect().Size) == new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size), "without the layout the bubbles' view is the whole window (ARC-UI-2603)");
        _gameSettings = _gameSettings with { GeneratedInterface = true };
        SyncInterfaceHd();
        SteamwovenHud.Suspended = true; panel.Relayout();
        await frames(3);
        await WaitBarArtGeneratedAsync(panel, frames);
        GD.Print($"ARC-UI-2452 native steamwoven layout PASS sizes={checkedSizes}");
    }

    // ARC-DEV-2672: the Original look for comparison: the HD option off (Legacy textures, no plan, the whole factor), every size of ARC_STEAMWOVEN_SIZES through the same logical render target, the inventory open and
    // every window probe of VerifySteamwovenWindowsAsync, saved as steamwoven-legacy-WxH[-menu|-WINDOW]. No layout checks; the pass asserts the HD option is off and puts the settings back after it.
    private async Task CaptureSteamwovenLegacyAsync(AreaGameBarPanel panel, Window window, Func<int, Task> frames, Action<bool, string> require)
    {
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        try
        {
            // As the settings stand with the HD option off: the generated interface goes with it (GeneratedInterface && HdInterface).
            _gameSettings = _gameSettings with { HdInterface = false, GeneratedInterface = false };
            SteamwovenHud.Suspended = false;
            SyncInterfaceHd();
            panel.Relayout();
            await frames(3);
            async Task WaitSettledAsync()
            {
                for (var wait = 0; wait < 1800 && InterfaceHd.GeneratedPending > 0; wait++) await frames(1);
                await frames(2);
            }
            foreach (var size in SteamwovenSizes())
            {
                window.ContentScaleSize = size;
                await FitWindowToRenderTargetAsync(panel, window, size, frames, require);
                for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                require((Vector2I)window.GetVisibleRect().Size == size, $"[legacy] the viewport is {size}, got {window.GetVisibleRect().Size}");
                await WaitSettledAsync();
                var tag = $"legacy-{size.X}x{size.Y}";
                // The HD option is off: Legacy textures on the strips, no plan, no boards, the strips at the whole factor, the old insets.
                require(!InterfaceHd.Enabled && !AreaGameBarPanel.SteamwovenOn && panel.PlanForSelfTest is null && !panel.BoardsShownForSelfTest && InterfaceHd.FractionalScale is null,
                    $"[{tag}] the HD option is off: no Steamwoven layout, no plan");
                require(Math.Abs(panel.StripScale - panel.Factor) < 1e-4f && AreaGameBarPanel.TopInset == AreaGameBarLayout.TopHeight * panel.Factor && AreaGameBarPanel.BottomInset == AreaGameBarLayout.BottomHeight * panel.Factor,
                    $"[{tag}] the strips are at the whole factor {panel.Factor}, got {panel.StripScale}");
                foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
                    require(panel.GetNode<TextureRect>($"{strip}Strip/Art").Texture is { } art && art is not HdInterfaceTexture { IsHd: true }, $"[{tag}] the {strip} strip is drawn with Legacy art");
                await CaptureHudAsync("steamwoven-" + tag, frames);
                OpenInventory(); await frames(3); await WaitSettledAsync();
                await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
                EndInventory(); await frames(3);
                await VerifySteamwovenWindowsAsync(tag, null, panel, frames, WaitSettledAsync, require);
                GD.Print($"ARC-DEV-2672 steamwoven legacy {tag} factor={panel.Factor}");
            }
        }
        finally
        {
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
            panel.Relayout();
            await frames(3);
            for (var wait = 0; wait < 1800 && (InterfaceHd.GeneratedPending > 0 || panel.BoardsPendingForSelfTest || !AllStripArtGenerated(panel)); wait++) await frames(1);
            await frames(2);
        }
        require(InterfaceHd.Enabled && InterfaceHd.Generated, "[legacy] the HD option is back on after the Original-look pass");
    }

    // ARC-UI-2890 (AC2, AC3, AC4): the play screen of the column mode. The strips stand in the column (the original's width at the strips' scale), no wood sides are drawn (the only board is the sliver above the top strip),
    // the generated end caps stand on the strips' outer ends, the world is drawn over the whole frame; the edge elements (follower portraits, spell pane, combat list, bubbles) are placed against the frame's edges; an open menu
    // takes the column's window zone, the wood of the gaps and the dimmed, desaturated world at the sides (read back: its mean brightness falls).
    private async Task CheckSteamwovenColumnPlayAsync(string tag, SteamwovenHudPlan plan, Vector2I size, AreaGameBarPanel panel, Func<Task> waitPainted, Func<int, Task> frames, Action<bool, string> require)
    {
        var column = plan.Column!;
        var layout = plan.Layout;
        var frame = layout.Frame;
        var topStrip = panel.StripRectForSelfTest(AreaBarStrip.Top);
        var bottomStrip = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
        require(topStrip.Position == new Vector2(column.Column.X, plan.TopStripY) && bottomStrip.Position == new Vector2(column.Column.X, plan.BottomStripY), $"[{tag}] the strips stand in the column at x {column.Column.X}");
        require(Math.Abs(topStrip.Size.X - column.Column.Width) <= 1.5f && Math.Abs(bottomStrip.Size.X - column.Column.Width) <= 1.5f, $"[{tag}] the strips are as wide as the column {column.Column.Width} (got {topStrip.Size.X} and {bottomStrip.Size.X})");
        var (freeTop, free) = AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y));
        require(free.X == column.Column.Width && AreaGameBarPanel.FreeLeft == column.Column.X && freeTop == plan.TopInset && free.Y == layout.Middle.Band.Height,
            $"[{tag}] the windows' free area is the column's {column.Column.Width} wide at {column.Column.X} between the strips (got {free} at {AreaGameBarPanel.FreeLeft})");
        // No wood sides: no bottom board, and the top board is only the sliver above the top strip's art.
        var (topBoard, bottomBoard, backdropShown) = panel.BoardsForSelfTest;
        require(!bottomBoard.Visible && !backdropShown && panel.DimmedWorldForSelfTest.Count == 0, $"[{tag}] no bottom board, no backdrop and no dimmed world with no menu");
        var sliver = plan.TopStripY - column.TopStrip.Y;
        require(topBoard.Visible == sliver > 0, $"[{tag}] the top board shows only for the sliver above the strip ({sliver} px)");
        if (topBoard.Visible)
            require(topBoard.Position == new Vector2(column.Column.X, column.TopStrip.Y) && topBoard.PixelSizeForSelfTest.X == column.Column.Width && topBoard.PixelSizeForSelfTest.Y == Math.Min(sliver + 1, column.TopStrip.Height),
                $"[{tag}] the top board is the sliver {topBoard.PixelSizeForSelfTest} at {topBoard.Position}, as wide as the column");
        long barArea = panel.BarsForSelfTest.Where(bar => bar.Visible).Sum(bar => (long)bar.Size.X * (long)bar.Size.Y);
        require(barArea + (long)frame.Width * frame.Height == (long)size.X * size.Y, $"[{tag}] the bars ({barArea}) and the frame {frame} fill the window");
        require(panel.CapsForSelfTest.SequenceEqual(SteamwovenColumnEdges.Parts(plan, false)) && panel.CapsForSelfTest.Count == 2, $"[{tag}] the end caps stand on the two strips ({panel.CapsForSelfTest.Count} parts)");
        await CheckSteamwovenColumnPartsAsync(tag, plan, size, frames, require);
        await CaptureHudAsync("steamwoven-" + tag, frames);
        // Read back: the brass edge of the top strip's end, and the world's brightness at the sides for the dimming comparison below.
        double worldBefore;
        using (var image = GetViewport().GetTexture().GetImage())
        {
            require(image is not null && image.GetSize() == new Vector2I(layout.WindowWidth, layout.WindowHeight), $"[{tag}] the viewport read back at the window's size");
            var third = Math.Max(1, SteamwovenColumnEdges.BrassWidth(plan) / 3);
            var brass = image!.GetPixel(column.Column.X + third + third / 2, column.TopStrip.Y + column.TopStrip.Height / 2);
            require(brass.R > 0.5f && brass.R > brass.B + 0.15f, $"[{tag}] the brass edge of the top strip's end is brass ({brass})");
            var brassRight = image.GetPixel(column.Column.Right - 1 - third - third / 2, column.TopStrip.Y + column.TopStrip.Height / 2);
            require(brassRight.R > 0.5f && brassRight.R > brassRight.B + 0.15f, $"[{tag}] the brass edge of the top strip's right end is brass ({brassRight})");
            worldBefore = MeanLuma(image, column.WorldLeft, column.WorldRight);
        }
        // An open menu: the window in the column's zone, the wood of the gaps, the middle band's end caps and the dimmed world at both sides.
        OpenInventory();
        await frames(3);
        require(InventoryOpen, $"[{tag}] the inventory opened");
        await waitPainted();
        CheckBackdrop(tag, "inventory", SteamwovenBackdropFamily.Inventory, plan, panel, require);
        var dimmed = panel.DimmedWorldForSelfTest;
        var worlds = SteamwovenColumnEdges.DimmedWorld(plan);
        require(dimmed.Count == worlds.Count && dimmed.Zip(worlds).All(pair => pair.First == new Rect2(pair.Second.X, pair.Second.Y, pair.Second.Width, pair.Second.Height)), $"[{tag}] the world left and right of the column is dimmed ({dimmed.Count} rectangles, expected {worlds.Count})");
        require(panel.CapsForSelfTest.SequenceEqual(SteamwovenColumnEdges.Parts(plan, true)) && panel.CapsForSelfTest.Count == 3, $"[{tag}] the end caps stand on the middle band too while a menu is open");
        await frames(2);
        await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
        using (var image = GetViewport().GetTexture().GetImage())
        {
            var worldAfter = MeanLuma(image, column.WorldLeft, column.WorldRight);
            GD.Print($"ARC-UI-2890 steamwoven [{tag}] mean brightness of the world at the sides: {worldBefore:F3} -> {worldAfter:F3}");
            // The darken value is the share of the brightness taken away (0.45 by default): the dimmed world is clearly darker; a black world cannot show it.
            if (worldBefore > 0.05) require(worldAfter < worldBefore * 0.8, $"[{tag}] the world at the sides is darker while the menu is open ({worldBefore:F3} -> {worldAfter:F3})");
        }
        EndInventory();
        await frames(3);
        require(!panel.BoardsForSelfTest.BackdropShown && panel.DimmedWorldForSelfTest.Count == 0 && panel.CapsForSelfTest.Count == 2, $"[{tag}] the backdrop, the dimming and the middle caps go with the menu");
    }

    // The mean brightness (0..1) of the pixels of the given rectangles, every eighth pixel each way.
    private static double MeanLuma(Image image, params PixelRect[] rects)
    {
        double sum = 0; long count = 0;
        foreach (var rect in rects)
            for (var y = rect.Y; y < rect.Bottom && y < image.GetHeight(); y += 8)
            for (var x = rect.X; x < rect.Right && x < image.GetWidth(); x += 8)
            {
                var c = image.GetPixel(x, y);
                sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; count++;
            }
        return count == 0 ? 0 : sum / count;
    }

    // ARC-UI-2890 (AC4): the edge elements stand against the frame's edges over the world, not against the column; what is placed by the strips' screen keeps its place.
    private async Task CheckSteamwovenColumnPartsAsync(string tag, SteamwovenHudPlan plan, Vector2I size, Func<int, Task> frames, Action<bool, string> require)
    {
        var column = plan.Column!;
        var frame = plan.Layout.Frame;
        var winSize = new Vector2(size.X, size.Y);
        var scale = (float)plan.StripScale;
        var (_, free) = AreaGameBarPanel.FreeArea(winSize);
        require(AreaGameBarPanel.FreeRect(winSize) == new Rect2(frame.X, plan.TopInset, frame.Width, free.Y) && FloaterLayer().BubbleView?.Invoke() == AreaGameBarPanel.FreeRect(GetViewport().GetVisibleRect().Size),
            $"[{tag}] the bubbles' view is the frame's width between the strips {AreaGameBarPanel.FreeRect(winSize)}, not the column's");
        require(AreaGameBarPanel.EdgeRect(winSize) == new Rect2(frame.X, frame.Y, frame.Width, frame.Height) && AreaGameBarPanel.EdgeTop == frame.Y && AreaGameBarPanel.EdgeBottom(winSize) == frame.Bottom,
            $"[{tag}] the edge elements' rectangle is the whole frame {frame} (got {AreaGameBarPanel.EdgeRect(winSize)})");
        require(AreaGameBarPanel.TopScreen is { } screen && screen.Origin == new Vector2(plan.StripLeft, plan.TopStripY) && Math.Abs(screen.Scale - scale) < 1e-4f
            && AreaGameBarPanel.EdgeScreen is { } edge && edge.Origin == new Vector2(frame.X, plan.TopStripY) && Math.Abs(edge.Scale - scale) < 1e-4f && Math.Abs(edge.BottomEdge - screen.BottomEdge) < 1e-3f,
            $"[{tag}] the strips' screen is the column's and the followers' screen hangs from the frame's left edge (got {AreaGameBarPanel.TopScreen} and {AreaGameBarPanel.EdgeScreen})");
        var floaters = new AreaFloaterLayer(); var portraits = new AreaFollowerPortraits(); var sleep = new AreaSleepView(); var fate = new AreaFateView();
        var spellPane = new AreaSpellPane(); var combatHud = new AreaCombatHud();
        try
        {
            foreach (var node in new Node[] { floaters, portraits, sleep, fate, spellPane, combatHud }) AddChild(node);
            await frames(2);
            spellPane.Show(Array.Empty<AreaSpellChoice>());
            combatHud.Show(new AreaCombatHudView(ActionPointRules.Lamps(5, 0), true, 1, new[] { ("Smoke", true), ("Foe", false) }, 0), _gameBarPanel);
            await frames(2);
            var paneRect = spellPane.PanelRectForSelfTest; var listRect = combatHud.PlacementForSelfTest.List;
            require(Math.Abs(paneRect.Position.X - (frame.X + 12)) <= 1e-2f && Math.Abs(paneRect.End.Y - (frame.Bottom - 12)) <= 1.01f,
                $"[{tag}] the spell pane stands at the frame's left edge {frame.X} + 12 and 12 above its bottom {frame.Bottom} (got {paneRect})");
            require(Math.Abs(listRect.End.X - (frame.Right - 8)) <= 1e-2f && Math.Abs(listRect.Position.Y - (frame.Y + 8)) <= 1e-2f, $"[{tag}] the combat list stands at the frame's right edge {frame.Right} - 8, 8 under its top (got {listRect})");
            require(Math.Abs(floaters.DrawScale - scale) < 1e-4f, $"[{tag}] the floaters are laid out at the strips' scale {scale} (got {floaters.DrawScale})");
            portraits.Show(Enumerable.Range(0, 2).Select(i => ($"smoke:{i}", $"F{i}", (AreaLife?)new AreaLife(100, 100, 50, 60, 0), (Texture2D?)null)).ToArray());
            await frames(2);
            var origin = new Vector2(frame.X, plan.TopStripY);
            require(portraits.OriginForSelfTest == origin && Math.Abs(portraits.ScaleForSelfTest - scale) < 1e-4f, $"[{tag}] the follower portraits hang from the frame's left edge at {origin} (got {portraits.OriginForSelfTest} at {portraits.ScaleForSelfTest})");
            require(portraits.OriginForSelfTest.X + 2 * scale <= column.Column.X || column.Column.X - frame.X < 2 * scale, $"[{tag}] the portraits' left edge lies left of the column at {column.Column.X}");
            sleep.Place(); fate.Place();
            // The sleep and fate views are screens of the original: they stay on the strips' screen, centred in the column.
            var whole = AreaGameBarPanel.ScreenFactor(winSize);
            foreach (var (name, view, x, y) in new[] { ("sleep", (Control)sleep, AreaSleepLayout.ScreenX, AreaSleepLayout.ScreenY), ("fate", fate, AreaFateLayout.ScreenX, AreaFateLayout.ScreenY) })
            {
                var expected = new Vector2(plan.StripLeft, plan.TopStripY) + new Vector2(x, y) * scale;
                require(view.Scale.X == whole && (view.Position - expected).Length() <= 1e-2f, $"[{tag}] the {name} view keeps its own whole scale {whole} on the column's screen at {expected} (got {view.Scale.X} at {view.Position})");
            }
        }
        finally { foreach (var node in new Node[] { floaters, portraits, sleep, fate, spellPane, combatHud }) { RemoveChild(node); node.QueueFree(); } }
    }

    // ARC-UI-2510: the parts the layout model leaves to the screen's own units follow the strips: the free area is the frame's, the screen the follower portraits hang in and the floaters' scale are where the strips
    // put them, the sleep and fate views keep their own whole scale (placed on that screen), and the generated textures of the views paint at the view's own scale, not the strips'.
    private async Task CheckSteamwovenScreenPartsAsync(string tag, SteamwovenHudPlan plan, Vector2I size, Func<int, Task> frames, Action<bool, string> require)
    {
        var frame = plan.Layout.Frame;
        var (freeTop, free) = AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y));
        require(free.X == frame.Width && AreaGameBarPanel.FreeLeft == frame.X && freeTop == plan.TopInset, $"[{tag}] the free width is the frame's {frame.Width} at x {frame.X} (got {free.X} at {AreaGameBarPanel.FreeLeft})");
        // ARC-UI-2603: a text bubble is placed inside what the strips and pillarbox bars leave free, not the whole window.
        var winSize = new Vector2(size.X, size.Y);
        require(AreaGameBarPanel.FreeRect(winSize) == new Rect2(frame.X, plan.TopInset, frame.Width, free.Y) && FloaterLayer().BubbleView?.Invoke() == AreaGameBarPanel.FreeRect(GetViewport().GetVisibleRect().Size),
            $"[{tag}] the bubbles' view is the free rect {AreaGameBarPanel.FreeRect(winSize)}, not the whole window");
        var scale = (float)plan.StripScale;
        require(AreaGameBarPanel.TopScreen is { } screen && screen.Origin == new Vector2(plan.StripLeft, plan.TopStripY) && Math.Abs(screen.Scale - scale) < 1e-4f
            && Math.Abs(screen.BottomEdge - (plan.BottomStripY + AreaGameBarLayout.BottomHeight * scale)) < 1e-2f, $"[{tag}] the screen hangs from the top strip at the strips' scale ({AreaGameBarPanel.TopScreen})");
        var origin = AreaGameBarPanel.TopScreen!.Value.Origin;
        var floaters = new AreaFloaterLayer(); var portraits = new AreaFollowerPortraits(); var sleep = new AreaSleepView(); var fate = new AreaFateView();
        var spellPane = new AreaSpellPane(); var combatHud = new AreaCombatHud();
        try
        {
            foreach (var node in new Node[] { floaters, portraits, sleep, fate, spellPane, combatHud }) AddChild(node);
            await frames(2);
            // ARC-UI-2604: the spell pane starts at the frame's left edge and the combat list ends at its right edge, neither under a pillarbox bar.
            spellPane.Show(Array.Empty<AreaSpellChoice>());
            combatHud.Show(new AreaCombatHudView(ActionPointRules.Lamps(5, 0), true, 1, new[] { ("Smoke", true), ("Foe", false) }, 0), _gameBarPanel);
            await frames(2);
            var freeRect = AreaGameBarPanel.FreeRect(winSize);
            var paneRect = spellPane.PanelRectForSelfTest; var listRect = combatHud.PlacementForSelfTest.List;
            require(Math.Abs(paneRect.Position.X - (freeRect.Position.X + 12)) <= 1e-2f && paneRect.Position.X >= frame.X, $"[{tag}] the spell pane starts at the frame's left edge {frame.X} + 12 (got {paneRect.Position.X})");
            require(Math.Abs(listRect.End.X - (freeRect.End.X - 8)) <= 1e-2f && listRect.End.X <= frame.X + frame.Width, $"[{tag}] the combat list ends at the frame's right edge {frame.X + frame.Width} - 8 (got {listRect.End.X})");
            require(Math.Abs(floaters.DrawScale - scale) < 1e-4f, $"[{tag}] the floaters are laid out at the strips' scale {scale} (got {floaters.DrawScale})");
            portraits.Show(Enumerable.Range(0, 2).Select(i => ($"smoke:{i}", $"F{i}", (AreaLife?)new AreaLife(100, 100, 50, 60, 0), (Texture2D?)null)).ToArray());
            await frames(2);
            require(portraits.OriginForSelfTest == origin && Math.Abs(portraits.ScaleForSelfTest - scale) < 1e-4f, $"[{tag}] the follower portraits are drawn on the screen from {origin} at {scale} (got {portraits.OriginForSelfTest} at {portraits.ScaleForSelfTest})");
            var firstSlot = origin.Y + AreaFollowerFrameLayout.FirstY * scale;
            require(Math.Abs(firstSlot - (plan.TopInset + (AreaFollowerFrameLayout.FirstY - AreaGameBarLayout.TopHeight) * scale)) <= 1f, $"[{tag}] the first portrait stands under the top strip ({firstSlot})");
            var toggle = AreaFollowerPanelRules.ToggleRectFor(true);
            var expectedY = AreaGameBarPanel.TopScreen!.Value.BottomEdge - (AreaGameBarLayout.ScreenHeight - (toggle.Y + toggle.Height / 2f)) * scale;
            require(portraits.Visible && Math.Abs(portraits.ButtonCentreForSelfTest("toggle")!.Value.Y - expectedY) <= 1f, $"[{tag}] the toggle button hangs from the bottom strip's edge: y {portraits.ButtonCentreForSelfTest("toggle")!.Value.Y} expected {expectedY:F1}");
            sleep.Place(); fate.Place();
            var whole = AreaGameBarPanel.ScreenFactor(new Vector2(size.X, size.Y));
            foreach (var (name, view, x, y) in new[] { ("sleep", (Control)sleep, AreaSleepLayout.ScreenX, AreaSleepLayout.ScreenY), ("fate", fate, AreaFateLayout.ScreenX, AreaFateLayout.ScreenY) })
            {
                var expected = origin + new Vector2(x, y) * scale;
                require(view.Scale.X == whole && view.Scale.Y == whole && (view.Position - expected).Length() <= 1e-2f, $"[{tag}] the {name} view keeps its own whole scale {whole} at {expected} (got {view.Scale.X} at {view.Position})");
            }
            // The parts follow the layout while they stand in the tree (the event wiring, not a placement by hand): the original strips' heights give another strip scale.
            if (SteamwovenHud.Plan(size.X, size.Y, SteamwovenHud.Original) is { } other && Math.Abs(other.StripScale - plan.StripScale) > 1e-6)
            {
                var saved = SteamwovenHud.Options;
                try
                {
                    SteamwovenHud.Options = SteamwovenHud.Original;
                    _gameBarPanel!.Relayout();
                    await frames(2);
                    var otherScale = (float)other.StripScale;
                    var otherOrigin = new Vector2(other.StripLeft, other.TopStripY);
                    require(Math.Abs(floaters.DrawScale - otherScale) < 1e-4f && portraits.OriginForSelfTest == otherOrigin && Math.Abs(portraits.ScaleForSelfTest - otherScale) < 1e-4f,
                        $"[{tag}] the floaters and the portraits follow a new layout by themselves: scale {otherScale}, origin {otherOrigin} (got {floaters.DrawScale}, {portraits.OriginForSelfTest} at {portraits.ScaleForSelfTest})");
                    var otherFree = AreaGameBarPanel.FreeRect(winSize);
                    require(Math.Abs(spellPane.PanelRectForSelfTest.Position.X - (otherFree.Position.X + 12)) <= 1e-2f && Math.Abs(combatHud.PlacementForSelfTest.List.End.X - (otherFree.End.X - 8)) <= 1e-2f,
                        $"[{tag}] the spell pane and the combat list follow a new layout by themselves (ARC-UI-2604)");
                    require((sleep.Position - (otherOrigin + new Vector2(AreaSleepLayout.ScreenX, AreaSleepLayout.ScreenY) * otherScale)).Length() <= 1e-2f
                        && (fate.Position - (otherOrigin + new Vector2(AreaFateLayout.ScreenX, AreaFateLayout.ScreenY) * otherScale)).Length() <= 1e-2f, $"[{tag}] the sleep and fate views follow a new layout by themselves");
                }
                finally { SteamwovenHud.Options = saved; _gameBarPanel!.Relayout(); await frames(2); }
            }
        }
        finally { foreach (var node in new Node[] { floaters, portraits, sleep, fate, spellPane, combatHud }) { RemoveChild(node); node.QueueFree(); } }
    }

    // ARC-UI-2575 / ARC-UI-2614: every menu window the smoke can open (inventory, character sheet, logbook, world map, schematics, the save list, and since ARC-UI-2614 the loot window of a container, the
    // barter window of an NPC, a book's reading window and the main menu's pre-made and new-character screens) stands in the window zone of the layout at the layout's unit scale: the drawn body (its top
    // left and its scale) is what SteamwovenWindowZone says, and the window is captured as steamwoven-VARIANT-WxH-WINDOW.png (local). With a null plan (the "before" variant, the old layout) only the
    // captures are made. A window whose data the map lacks (no container, no NPC to trade with, no readable book) is reported as not captured; the main menu screens come from the in-play menu.
    private sealed record SteamwovenWindowProbe(string Name, Action Open, Func<bool> IsOpen, Action Close, Func<(Vector2 Origin, float Scale)?> View, Func<SteamwovenHudPlan, SteamwovenWindowPlace> Expected, SteamwovenBackdropFamily? Family = null, bool Optional = false);

    private static SteamwovenWindowPlace ZoneBody(SteamwovenHudPlan plan) => SteamwovenWindowZone.Body(plan, 800, 400);
    private static SteamwovenWindowPlace ZoneScreen(SteamwovenHudPlan plan) => SteamwovenWindowZone.Screen(plan, AreaShopArtLayout.Window.Y);
    // ARC-UI-2714: the reading window stands in the zone like the others: its top left is the zone body's (its 800 x 399 under the body's top).
    private static SteamwovenWindowPlace ZoneReading(SteamwovenHudPlan plan) => SteamwovenWindowZone.Body(plan, AreaReadingArtLayout.Window.Width, AreaReadingArtLayout.Window.Height + 1);

    // ARC-UI-2614: a container of the map for the loot window (See Contents: no open attempt, a locked one shows its items too), null when the map has none.
    private AreaInventoryTarget? SteamwovenLootTarget()
    {
        if (_areaSession is not { } session || _readyArea is not { } area || _inventoryHost?.Map(CurrentAreaMap) is not { } map) return null;
        return area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == ContainerType && map.TryGetEntity(row.Key, out _))
            .Select(row => _inventoryHost.Target(session, CurrentAreaMap, row.Key)).OfType<AreaInventoryTarget>()
            .Where(target => target.Kind == AreaInventoryTargetKind.Container).OrderBy(target => target.Entity, StringComparer.Ordinal).FirstOrDefault();
    }

    // ARC-UI-2614: the barter model of the first speaker of the map's talk host that can trade, null when none can.
    private AreaBarter? SteamwovenBarter()
    {
        if (_conversationHost is not { } host) return null;
        for (var index = 0; index < host.Speakers.Count; index++)
            if (BarterModel(host, AreaConversationState.EntityId(host.Speakers[index].Row), index, substitute: true, "ARC-UI-2614 steamwoven barter").Barter is { } barter) return barter;
        return null;
    }

    // ARC-UI-2614: a readable written document without a use script (a book first), null when the map's data has none.
    private WrittenDocument? SteamwovenReading(WrittenSubtype? only = null)
    {
        if (_areaSession is not { } session || _readingData?.Data is not { } data) return null;
        return data.Prototypes.OrderBy(p => p.Key).Where(p => !p.Value.UseScript && !p.Value.ItemSpell && ReadingKinds.Contains((WrittenSubtype)p.Value.Subtype) && (only is null || p.Value.Subtype == (int)only))
            .Select(p => (p.Value.Subtype, Document: session.QueryWrittenDocument(p.Value, data.Texts))).Where(p => p.Document is { Readable: true, Body.Count: > 0 })
            .OrderBy(p => p.Subtype != (int)WrittenSubtype.Book).Select(p => p.Document).FirstOrDefault();
    }

    // The main menu opened over the game (root Main: Single Player, New Game), where the pre-made browser (420) and the new character window (421) open. The menu's music replaces the scene's sounds
    // for the rest of the run (as it does when the owner opens the menu from play); the smoke's later checks do not depend on them.
    private void SteamwovenOpenMenuWindow(int message, Action<bool, string> require)
    {
        if (MainMenuOpen) EndMainMenu("steamwoven smoke");
        OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!));
        foreach (var press in new[] { 10, 50, message })
        {
            var index = _mainMenu is null ? -1 : _mainMenu.Shown.ToList().FindIndex(shown => shown.Message == press);
            require(index >= 0, $"the main menu offers button {press} (window {_mainMenu?.Window})");
            MainMenuCommand(() => _mainMenu!.Activate(index));
        }
    }
    private async Task VerifySteamwovenWindowsAsync(string tag, SteamwovenHudPlan? plan, AreaGameBarPanel panel, Func<int, Task> frames, Func<Task> waitPainted, Action<bool, string> require)
    {
        for (var wait = 0; wait < 1800 && (_barterData is null || _readingData is null); wait++) { AdoptBarterData(); AdoptReadingData(); await frames(1); }
        SteamwovenWindowProbe[] windows =
        [
            new("inventory", () => OpenInventory(), () => InventoryOpen, EndInventory, () => _inventoryPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody, SteamwovenBackdropFamily.Inventory),
            new("sheet", OpenSheet, () => SheetOpen, EndSheet, () => _sheetPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody, SteamwovenBackdropFamily.CharacterSheet),
            new("logbook", OpenLogbook, () => LogbookOpen, CloseLogbookByPlayer, () => _logbookPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody, SteamwovenBackdropFamily.Logbook),
            new("map", OpenWorldMap, () => WorldMapOpen, CloseWorldMapByPlayer, () => _worldMapPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody, SteamwovenBackdropFamily.WorldMap),
            new("schematics", () => OpenSchematics(), () => SchematicsOpen, CloseSchematicsByPlayer, () => _schematicsPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody, SteamwovenBackdropFamily.Schematic),
            new("saves", () => OpenSaveList(AreaSaveListMode.Load), () => SaveListOpen, () => EndSaveList(), () => _saveListPanel?.ArtForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneScreen),
            new("loot", () => { if (SteamwovenLootTarget() is { } target) OpenContainerContents(target); }, () => ContainerLootOpen, EndInventory,
                () => _inventoryPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody, SteamwovenBackdropFamily.Inventory),
            new("barter", () => { if (SteamwovenBarter() is { } barter) OpenBarterWindow(barter); }, () => BarterOpen, CloseBarterByPlayer,
                () => _barterPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody, SteamwovenBackdropFamily.Inventory),
            new("reading", () => { if (SteamwovenReading() is { } document) OpenReading(document, "steamwoven", replaceWindows: true); }, () => ReadingOpen, CloseReadingByPlayer,
                () => _readingPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneReading, SteamwovenBackdropFamily.Reading),
            // ARC-UI-2890: the reading window of each other kind the map's data has (a letter, a newspaper, a telegram, a plaque), captured when there is one.
            .. new[] { (WrittenSubtype.Note, "letter"), (WrittenSubtype.Newspaper, "newspaper"), (WrittenSubtype.Telegram, "telegram"), (WrittenSubtype.Plaque, "plaque") }.Select(kind =>
                new SteamwovenWindowProbe("reading-" + kind.Item2, () => { if (SteamwovenReading(kind.Item1) is { } document) OpenReading(document, "steamwoven", replaceWindows: true); }, () => ReadingOpen, CloseReadingByPlayer,
                    () => _readingPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneReading, SteamwovenBackdropFamily.Reading, Optional: true)),
            new("premade", () => SteamwovenOpenMenuWindow(420, require), () => _mainMenu is { Window: AreaMenuWindow.PickCharacter }, () => EndMainMenu("steamwoven smoke"),
                () => _mainMenuPanel is { PremadeArtMode: true } m ? (m.PremadeArtForSelfTest.Position, m.PremadeArtForSelfTest.Scale.X) : null, ZoneScreen),
            new("newcharacter", () => SteamwovenOpenMenuWindow(421, require), () => _mainMenu is { Window: AreaMenuWindow.NewCharacter }, () => EndMainMenu("steamwoven smoke"),
                () => _mainMenuPanel is { NewCharacterArtMode: true } m ? (m.NewCharacterArtForSelfTest.Position, m.NewCharacterArtForSelfTest.Scale.X) : null, ZoneScreen),
        ];
        foreach (var window in windows)
        {
            var name = window.Name;
            if (window.IsOpen()) window.Close();
            await frames(2);
            window.Open();
            await frames(3);
            // With a plan (the layout checks) every window must open: the loot, barter and reading windows need a container, a trading NPC and a readable book of the map, which the smoke's map has.
            if (!window.IsOpen())
            {
                require(plan is null || window.Optional, $"[{tag}] the {name} window opened (the map has no data for it?)");
                GD.Print($"ARC-UI-2614 steamwoven window [{tag}] {name} did not open (not captured)");
                continue;
            }
            await waitPainted();
            for (var wait = 0; wait < 600 && window.View() is null; wait++) await frames(1);
            await frames(2);
            if (plan is not null)
            {
                require(window.View() is not null, $"[{tag}] the {name} window shows the original's look (a view to measure)");
                var (origin, scale) = window.View()!.Value;
                var expected = window.Expected(plan);
                require(Math.Abs(scale - expected.Scale) < 0.002 && Math.Abs(origin.X - expected.X) <= 1.01 && Math.Abs(origin.Y - expected.Y) <= 1.01,
                    $"[{tag}] the {name} window is at the zone place ({expected.X:F1}, {expected.Y:F1}) scale {expected.Scale:F4}, got ({origin.X:F1}, {origin.Y:F1}) scale {scale:F4}");
                GD.Print($"ARC-UI-2575 steamwoven window [{tag}] {name} origin=({origin.X:F1},{origin.Y:F1}) scale={scale:F4} zone={plan.WindowZone}");
            }
            await CaptureHudAsync("steamwoven-" + tag + "-" + name, frames);
            if (plan is not null) CheckBackdrop(tag, name, window.Family, plan, panel, require);
            // ARC-UI-2694: the lens of the sheet and the map window is a close spot (a mouse-up in its square closes the window): a click on its centre, at the zone place, closes it.
            if (plan is not null && name is "sheet" or "map")
            {
                var at = name == "sheet" ? _sheetPanel!.ArtViewForSelfTest!.PointForSelfTest(56.5, 54.5) : _worldMapPanel!.ArtViewForSelfTest!.WindowPointForSelfTest(69.5, 68.5);
                foreach (var pressed in new[] { true, false })
                {
                    GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, inLocalCoords: true);
                    await frames(2);
                }
                require(!window.IsOpen(), $"[{tag}] a click on the lens of the {name} window at {at} closes it");
            }
            if (window.IsOpen()) window.Close();
            await frames(3);
        }
    }

    // ARC-UI-2576: the open menu window has the backdrop of its family (the census's, SWI-BDR-4): the pieces of SteamwovenBackdrops over the middle band, the two parts of a two-part window meeting at the split
    // of the layout model (SWI-BDR-5), each drawn at its own rectangle with its texels one to one; a window with no family (the save list, whose screen hides the bars) has none. On the viewport read back, the join at
    // each edge of the window zone has the seam ratio of SWI-VER-5 (the mean neighbour difference in a ±8 px band at the join over the one beside it, R ≤ 1.5); a join with no room for the bands outside it is skipped.
    // The ratios are printed (ARC-UI-2576 seam) so a capture's numbers can be read; the gate is R ≤ SteamwovenSeam.Limit.
    private void CheckBackdrop(string tag, string name, SteamwovenBackdropFamily? family, SteamwovenHudPlan plan, AreaGameBarPanel panel, Action<bool, string> require)
    {
        var (shownFamily, shown) = panel.BackdropForSelfTest;
        var shownLayer = panel.BoardsForSelfTest.BackdropShown;
        if (family is not { } expectedFamily)
        {
            require(shownFamily is null && !shownLayer, $"[{tag}] the {name} window has no backdrop (family {shownFamily}, layer shown {shownLayer})");
            return;
        }
        var pieces = SteamwovenBackdrops.Pieces(expectedFamily, plan);
        require(shownFamily == expectedFamily && shownLayer && shown.Count == pieces.Count, $"[{tag}] the {name} window has the backdrop of {expectedFamily} in {pieces.Count} piece(s) (family {shownFamily}, shown {shown.Count}, layer {shownLayer})");
        for (var i = 0; i < pieces.Count; i++)
            require(shown[i].Position == new Vector2(pieces[i].Rect.X, pieces[i].Rect.Y) && shown[i].Pixels == new Vector2I(pieces[i].Rect.Width, pieces[i].Rect.Height),
                $"[{tag}] the {name} backdrop piece {i} fills {pieces[i].Rect} one texel per pixel (at {shown[i].Position}, pixels {shown[i].Pixels})");
        if (pieces.Count == 2 && plan.Column is null) require(pieces[0].Rect.Right == pieces[1].Rect.X, $"[{tag}] the {name} backdrop parts meet at the split {pieces[0].Rect.Right} / {pieces[1].Rect.X}");
        // A window with a lens (the round view of the hero, the logbook's, the schematics', the reading window's, the inventory's) shows the game view in its square: the backdrop leaves the square out (it lies above the
        // round view's copy of the world), so the lens does not show wood.
        var lenses = AreaRoundView.VisibleRects();
        require(panel.BackdropHolesForSelfTest == Math.Min(2, lenses.Count), $"[{tag}] the {name} backdrop leaves out the {lenses.Count} round-view square(s) (got {panel.BackdropHolesForSelfTest})");
        var unit = SteamwovenWindowZone.UnitScale(plan);
        var places = string.Join(" ", lenses.Select(rect => $"({(rect.Position.X - plan.WindowZone.X) / unit:F2},{(rect.Position.Y - plan.WindowZone.Y) / unit:F2},{rect.Size.X / unit:F2})"));
        // ARC-UI-2694: every lens is the same 89 units square (the cover art 198's) within a device pixel, at the same scale as the window.
        foreach (var rect in lenses) require(Math.Abs(rect.Size.X - 89 * unit) <= 1 && Math.Abs(rect.Size.Y - 89 * unit) <= 1, $"[{tag}] the {name} lens is 89 units square ({rect.Size.X / unit:F2} x {rect.Size.Y / unit:F2})");
        // ARC-UI-2714: every lens of the generated interface stands at the one common place (within a unit: the reading window's origin is a fraction of a unit off the zone's).
        foreach (var rect in lenses)
        {
            var (lensX, lensY) = ((rect.Position.X - plan.WindowZone.X) / unit, (rect.Position.Y - plan.WindowZone.Y) / unit);
            require(Math.Abs(lensX - AreaLensPlace.X) <= 1 / unit && Math.Abs(lensY - AreaLensPlace.Y) <= 1 / unit, $"[{tag}] the {name} lens stands at the common place ({AreaLensPlace.X}, {AreaLensPlace.Y}) (at {lensX:F2}, {lensY:F2})");
        }
        GD.Print($"ARC-UI-2576 lens [{tag}] {name} round views={lenses.Count} places(units from the zone corner)={places}");
        using var captured = GetViewport().GetTexture().GetImage();
        if (captured is null) return;
        captured.Convert(Image.Format.Rgba8);
        var rgba = captured.GetData();
        var (width, height) = (captured.GetWidth(), captured.GetHeight());
        var zone = plan.WindowZone;
        var band = plan.Layout.Middle.Band;
        // ARC-UI-2890: in the column the zone's left and right edges border the world (dimmed), not wood, so only its top and bottom edges join the wood of the gaps.
        var columnMode = plan.Column is not null;
        // The interior band beside a join lies on the backdrop inside the middle band (never on a strip or the world), clear of the join's own band; where there is no room it is skipped.
        int? Reference(int join, int low, int high)
        {
            var wanted = join + Math.Sign(low - join + high - join) * (3 * SteamwovenSeam.Band + 16);
            var (lo, hi) = (low + SteamwovenSeam.Band + 1, high - SteamwovenSeam.Band - 1);
            if (wanted < join) { hi = Math.Min(hi, join - 2 * SteamwovenSeam.Band); return lo <= hi ? Math.Clamp(wanted, lo, hi) : null; }
            lo = Math.Max(lo, join + 2 * SteamwovenSeam.Band);
            return lo <= hi ? Math.Clamp(wanted, lo, hi) : null;
        }
        var seams = new (string Edge, double? Ratio)[]
        {
            ("left", !columnMode && Reference(zone.X, band.X, zone.X) is { } left ? SteamwovenSeam.ColumnRatio(rgba, width, height, zone.X, zone.Y, zone.Bottom, left) : null),
            ("right", !columnMode && Reference(zone.Right, zone.Right, band.Right) is { } right ? SteamwovenSeam.ColumnRatio(rgba, width, height, zone.Right, zone.Y, zone.Bottom, right) : null),
            ("top", Reference(zone.Y, band.Y, zone.Y) is { } top ? SteamwovenSeam.RowRatio(rgba, width, height, zone.Y, zone.X, zone.Right, top) : null),
            ("bottom", Reference(zone.Bottom, zone.Bottom, band.Bottom) is { } bottom ? SteamwovenSeam.RowRatio(rgba, width, height, zone.Bottom, zone.X, zone.Right, bottom) : null),
        };
        // The split of a two-part window is a designed join of two arts (each half's grain starts at its own left edge, as the window's own halves do): printed above and below the zone, not gated.
        if (pieces.Count == 2 && !columnMode && pieces[1].Rect.Width > 4 * SteamwovenSeam.Band + 2)
        {
            var splitReference = pieces[1].Rect.X + 3 * SteamwovenSeam.Band + 16;
            var above = zone.Y - band.Y >= 2 * SteamwovenSeam.Band + 1 ? SteamwovenSeam.ColumnRatio(rgba, width, height, pieces[0].Rect.Right, band.Y, zone.Y, splitReference) : null;
            GD.Print($"ARC-UI-2576 seam [{tag}] {name} split above the zone R={(above is { } a ? a.ToString("F2") : "n/a")} (a designed join, not gated)");
        }
        foreach (var (edge, ratio) in seams)
        {
            GD.Print($"ARC-UI-2576 seam [{tag}] {name} {edge} R={(ratio is { } r ? r.ToString("F2") : "n/a")} zone={zone} band={band}");
            // The windows' own look switch ("Diagnostic view", a wide button with a border; debug-only since ARC-UI-2764) lay just below the zone's bottom edge, so that edge is printed and not gated.
            if (ratio is { } measured && edge != "bottom") require(measured <= SteamwovenSeam.Limit, $"[{tag}] the {name} window's {edge} join of the zone has seam ratio {measured:F2} > {SteamwovenSeam.Limit}");
        }
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
