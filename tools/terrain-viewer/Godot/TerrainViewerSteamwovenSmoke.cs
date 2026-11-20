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
    // Steamwoven layout model (the column, ARC-UI-2926: the full-width layout and its variants are gone) at each size of ARC_STEAMWOVEN_SIZES (default: the TRD's verification sizes, SWI-VER-6) for each variant of
    // ARC_STEAMWOVEN_VARIANTS (default "column": the fitted column of ARC-UI-2890; "column-wood" is the unfitted one), with and without an open menu. The window takes the size's width, not its height (a Mac screen may be smaller than 3840 x 2160; ARC-UI-2696, FitWindowToRenderTargetAsync): the root
    // viewport is given the size as its logical render target (content scale mode Viewport), so the captures have exactly the requested size.
    // ARC_AREA_BAR_CAPTURE=PREFIX saves PREFIX-hud-steamwoven-VARIANT-WxH[-menu].png (local, never in git).
    // ARC-DEV-2672: the variant "legacy" is the Original look (the HD option off: Legacy textures, no plan) at the same sizes and windows, for side-by-side comparison with the Steamwoven
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
        if (string.IsNullOrWhiteSpace(text)) text = "column";
        var list = new List<(string, SteamwovenLayoutOptions)>();
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // ARC-UI-2890: "column" is the fitted column mode and "column-wood" the unfitted one (the original's strip width, k = 1, the world at the sides); "legacy" is the Original look (its pass takes no layout checks).
            var options = name switch { "column" or "kit" or "legacy" => SteamwovenLayoutOptions.ColumnFitted, "column-wood" => SteamwovenLayoutOptions.ColumnOriginal, _ => null };
            if (options is null) throw new InvalidOperationException($"ARC-UI-2452 steamwoven smoke: ARC_STEAMWOVEN_VARIANTS entry '{name}' is not column, column-wood, kit or legacy");
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
                if (variant == "kit")
                {
                    // ARC-UI-2461: the kit runtime on a synthetic kit, at the same sizes.
                    await CaptureSteamwovenKitAsync(panel, window, frames, Require);
                    SteamwovenHud.Options = options;
                    continue;
                }
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
                    if (variant == "column") await VerifySteamwovenWorldZoomAsync(tag, size, plan.StripScale, frames, Require);
                    // The strips: one scale, placed by the plan; the insets are the middle band's, so every docked window lies in it.
                    Require(Math.Abs(panel.StripScale - (float)plan.StripScale) < 1e-4f, $"[{tag}] both strips are drawn at the plan's scale {plan.StripScale:F4} (got {panel.StripScale})");
                    // ARC-UI-2696: the strips' generated wood is painted at the scale it is drawn at, as the boards' is (one texel per device pixel): else the strips' wood is softer than the boards' beside it.
                    var woodAtScale = InterfaceHd.FractionalScale is { } paintedScale && Math.Abs(InterfaceHd.PaintScale(paintedScale) - InterfaceHd.PaintScale(plan.StripScale)) < 0.002; // a generated frame is painted at one pixel per unit at least (PaintScale), so a strip scale under 1 (1280 x 800 at k = 0.8: 0.96) is drawn down from 1
                    var woodNote = $"[{tag}] the strips' wood is painted at the scale it is drawn at, {plan.StripScale:F4} (painted at {InterfaceHd.FractionalScale}, viewport stretch {GetViewport().GetFinalTransform().Scale})";
                    if (System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_FIT_WINDOW") == "0") GD.Print("ARC-UI-2696 steamwoven " + woodNote + (woodAtScale ? " yes" : " NO (the window is not fitted)"));
                    else Require(woodAtScale, woodNote);
                    Require(panel.StripRectForSelfTest(AreaBarStrip.Top).Position == new Vector2(plan.StripLeft, plan.TopStripY)
                        && panel.StripRectForSelfTest(AreaBarStrip.Bottom).Position == new Vector2(plan.StripLeft, plan.BottomStripY), $"[{tag}] the strips stand where the plan says");
                    Require(AreaGameBarPanel.TopInset == layout.Middle.Y && AreaGameBarPanel.BottomInset == size.Y - layout.Middle.Bottom
                        && AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y)).Size.Y == layout.Middle.Height, $"[{tag}] the free area is the middle band {layout.Middle}");
                    // The top band's art ends on the board's edge and the bottom band's starts on it: each strip lies inside its own band (the bottom one may hang over the frame's bottom edge by under 3 px · f).
                    Require(plan.TopStripY >= layout.Top.Y && plan.TopStripY + (int)Math.Round(41 * plan.StripScale) == layout.Top.Bottom, $"[{tag}] the top strip stands on its band's lower edge");
                    Require(plan.BottomStripY == layout.Bottom.Y, $"[{tag}] the bottom strip hangs from its band's upper edge");
                    // ARC-UI-2767: the Fate Points and Sleep balls stand in the middle of the ring the strip draws round their hole (the ring's centre is read from the strip's Legacy hole, the ball's from its node).
                    foreach (var ball in new[] { AreaBarButton.FatePoints, AreaBarButton.Sleep })
                    {
                        var ring = panel.RingCentreForSelfTest(ball);
                        Require(ring is not null, $"[{tag}] the {ball} button has a ring round its hole");
                        var off = panel.BallCentreForSelfTest(ball) - ring!.Value;
                        GD.Print($"ARC-UI-2767 steamwoven [{tag}] {ball} ball centre minus ring centre: ({off.X:F3}, {off.Y:F3}) px");
                        Require(Math.Abs(off.X) <= 0.01f && Math.Abs(off.Y) <= 0.01f, $"[{tag}] the {ball} ball is centred in its ring (off by {off} px)");
                    }
                    await CheckSteamwovenColumnPlayAsync(tag, plan, size, panel, WaitPaintedAsync, frames, Require);
                    await VerifySteamwovenWindowsAsync(tag, plan, panel, frames, WaitPaintedAsync, Require);
                    checkedSizes++;
                    GD.Print($"ARC-UI-2890 steamwoven {tag} scale={plan.StripScale:F4} step={layout.Step} column={plan.Column.Column} window={plan.WindowZone} worldLeft={plan.Column.WorldLeft} worldRight={plan.Column.WorldRight} bars={layout.Bars}");
                }
            }
        }
        finally
        {
            // The layout the viewer runs with is back for what runs after this smoke in the process.
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

    // ARC-DEV-2672, ARC-UI-2936: the Original look (the HD option off: Legacy textures, no plan) as the centred 4:3 composition: for each size of ARC_STEAMWOVEN_LEGACY_SIZES (default: the sizes of issue #2936 AC5 and the whole
    // scales 1600x1200 and 3200x1800 for the pixel check) the window is a virtual screen of 800 n x 600 n pixels (n = ceil(s)), the strips stand at the whole factor n, the world is drawn at the composition's scale (the default zoom shows
    // the original's 800 x 400 view), and the out-of-sight window is the fixed 800 x 400. The captures are the virtual screen reduced to s and placed on black at the size (what the window shows, composed in software), saved as
    // steamwoven-legacy-WxH[-menu|-WINDOW]; ARC_STEAMWOVEN_LEGACY_BEFORE=1 also saves the old layout (a whole factor, windows fitted to the window) as steamwoven-legacy-before-WxH for the owner's comparison.
    private static IReadOnlyList<Vector2I> LegacySizes()
    {
        var text = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_LEGACY_SIZES");
        if (string.IsNullOrWhiteSpace(text)) text = "3840x2160,2560x1440,1920x1080,2560x1600,1280x800,3440x1440,1024x768,1280x1024,1600x1200,3200x1800";
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('x')).Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            .Select(parts => new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]))).ToList();
    }

    private async Task CaptureSteamwovenLegacyAsync(AreaGameBarPanel panel, Window window, Func<int, Task> frames, Action<bool, string> require)
    {
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        var (savedMode, savedAspect, savedContentSize, savedContentFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
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
            async Task EnterCompositionAsync(Vector2I size, bool composition)
            {
                _originalCompositionForced = composition; _originalCompositionTarget = composition ? size : null;
                var (width, height) = composition ? OriginalComposition.VirtualSize(size.X, size.Y) : (size.X, size.Y);
                if (!composition) { window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport; window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore; window.ContentScaleFactor = 1; window.ContentScaleSize = size; }
                else SyncOriginalComposition();
                for (var wait = 0; wait < 240 && (Vector2I)window.GetVisibleRect().Size != new Vector2I(width, height); wait++) { SyncOriginalComposition(); await frames(1); }
                require((Vector2I)window.GetVisibleRect().Size == new Vector2I(width, height), $"[legacy {size.X}x{size.Y}] the viewport is {width}x{height}, got {window.GetVisibleRect().Size}");
                panel.Relayout();
                await WaitSettledAsync();
            }
            if (System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_LEGACY_BEFORE") == "1")
                foreach (var size in LegacySizes())
                {
                    await EnterCompositionAsync(size, false);
                    await CaptureHudAsync($"steamwoven-legacy-before-{size.X}x{size.Y}", frames);
                    OpenInventory(); await frames(3); await WaitSettledAsync();
                    await CaptureHudAsync($"steamwoven-legacy-before-{size.X}x{size.Y}-menu", frames);
                    EndInventory(); await frames(3);
                    await VerifySteamwovenWindowsAsync($"legacy-before-{size.X}x{size.Y}", null, panel, frames, WaitSettledAsync, require);
                }
            foreach (var size in LegacySizes())
            {
                await EnterCompositionAsync(size, true);
                var tag = $"legacy-{size.X}x{size.Y}";
                var whole = OriginalComposition.WholeFactor(size.X, size.Y);
                var (virtualWidth, virtualHeight) = OriginalComposition.VirtualSize(size.X, size.Y);
                // AC1: the HD option is off (Legacy textures, no plan, no boards); one scale: the virtual screen is 800 n x 600 n and the strips stand at the whole factor n with the original's insets.
                require(!InterfaceHd.Enabled && !AreaGameBarPanel.SteamwovenOn && panel.PlanForSelfTest is null && !panel.BoardsShownForSelfTest && InterfaceHd.FractionalScale is null,
                    $"[{tag}] the HD option is off: no Steamwoven layout, no plan");
                require(OriginalCompositionFactor == whole && panel.Factor == whole, $"[{tag}] the composition's whole factor is {whole}, got {OriginalCompositionFactor} (strips {panel.Factor})");
                require(Math.Abs(panel.StripScale - whole) < 1e-4f && AreaGameBarPanel.TopInset == AreaGameBarLayout.TopHeight * whole && AreaGameBarPanel.BottomInset == AreaGameBarLayout.BottomHeight * whole,
                    $"[{tag}] the strips are at the whole factor {whole}, got {panel.StripScale}, insets {AreaGameBarPanel.TopInset}/{AreaGameBarPanel.BottomInset}");
                require(AreaGameBarPanel.TopInset + AreaGameBarPanel.BottomInset + 400 * whole == virtualHeight, $"[{tag}] the strips and the 400-unit play view fill the virtual screen's height {virtualHeight}");
                foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
                    require(panel.GetNode<TextureRect>($"{strip}Strip/Art").Texture is { } art && art is not HdInterfaceTexture { IsHd: true }, $"[{tag}] the {strip} strip is drawn with Legacy art");
                // AC3: the world is drawn at the composition's scale (the default zoom 2 shows the original's 800 x 400 view) and the out-of-sight window is the fixed 800 x 400.
                if (_camera is not null)
                {
                    var expectedZoom = _camera.State.Zoom * whole / WorldZoomPolicy.DefaultZoom;
                    require(Math.Abs(_camera.Camera.Zoom.X - expectedZoom) < 1e-4, $"[{tag}] the camera's zoom {_camera.Camera.Zoom.X} follows the player's setting {_camera.State.Zoom} at the factor {whole}");
                    require(Math.Abs(_camera.PlayHeight - OriginalComposition.ViewHeight * whole) < 1e-6, $"[{tag}] the camera's map-edge clamp uses the play view's height {OriginalComposition.ViewHeight * whole}, got {_camera.PlayHeight}");
                    if (_camera.State.Zoom == 2) require(Math.Abs(virtualWidth / _camera.Camera.Zoom.X - OriginalComposition.ViewWidth) < 1e-3, $"[{tag}] the view is {OriginalComposition.ViewWidth} world pixels wide at the default zoom");
                }
                if (AreaGameWindow() is { } outOfSight)
                    require(outOfSight.Width == OriginalComposition.ViewWidth && outOfSight.Height == OriginalComposition.ViewHeight, $"[{tag}] the out-of-sight window is the fixed 800 x 400, got {outOfSight.Width}x{outOfSight.Height}");
                // AC2: the strips are sampled with nearest pixels (the virtual screen is reduced to s by the window afterwards); their art nodes inherit it.
                foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
                    require(panel.GetNode<Control>($"{strip}Strip").TextureFilter is CanvasItem.TextureFilterEnum.Nearest, $"[{tag}] the {strip} strip is sampled with nearest pixels");
                // AC2: at a whole scale the picture is exactly the nearest-pixel one (every whole x whole block of the virtual screen's strips is uniform apart from the art's smooth parts).
                if (Math.Abs(OriginalComposition.Scale(size.X, size.Y) - whole) < 1e-9) await CheckNearestBlocksAsync(tag, whole, panel, frames, require);
                await CaptureHudAsync("steamwoven-" + tag, frames);
                OpenInventory(); await frames(3); await WaitSettledAsync();
                await CaptureHudAsync("steamwoven-" + tag + "-menu", frames);
                EndInventory(); await frames(3);
                await VerifySteamwovenWindowsAsync(tag, null, panel, frames, WaitSettledAsync, require);
                GD.Print($"ARC-UI-2936 steamwoven legacy {tag} s={OriginalComposition.Scale(size.X, size.Y):F4} factor={whole} virtual={virtualWidth}x{virtualHeight}");
            }
        }
        finally
        {
            _originalCompositionForced = null; _originalCompositionTarget = null;
            SyncOriginalComposition();
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleFactor = savedContentFactor; window.ContentScaleSize = savedContentSize;
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

    /// <summary>AC2 (ARC-UI-2936): at a whole composition scale the virtual screen is shown as it is, so the Legacy strips are drawn with nearest pixels: each whole x whole block of the two strips is one colour. The art's smooth
    /// overlays (text, gauges, portraits) blend at a block's edge only, so the share of uniform blocks is high; a bilinear or fractional draw would leave almost none.</summary>
    private async Task CheckNearestBlocksAsync(string tag, int whole, AreaGameBarPanel panel, Func<int, Task> frames, Action<bool, string> require)
    {
        await frames(2);
        using var image = GetViewport().GetTexture().GetImage();
        require(image.GetWidth() == OriginalComposition.ScreenWidth * whole && image.GetHeight() == OriginalComposition.ScreenHeight * whole, $"[{tag}] the readback is the virtual screen");
        var (uniform, total) = (0, 0);
        foreach (var (top, bottom) in new[] { (0, AreaGameBarLayout.TopHeight), (AreaGameBarLayout.ScreenHeight - AreaGameBarLayout.BottomHeight, AreaGameBarLayout.ScreenHeight) })
            for (var unitY = top; unitY < bottom; unitY++)
                for (var unitX = 0; unitX < AreaGameBarLayout.ScreenWidth; unitX += 3)
                {
                    var first = image.GetPixel(unitX * whole, unitY * whole);
                    var same = true;
                    for (var dy = 0; dy < whole && same; dy++) for (var dx = 0; dx < whole && same; dx++) same = image.GetPixel(unitX * whole + dx, unitY * whole + dy).IsEqualApprox(first);
                    total++; if (same) uniform++;
                }
        var share = total == 0 ? 0 : uniform / (double)total;
        GD.Print($"ARC-UI-2936 legacy {tag} nearest blocks uniform={uniform}/{total} share={share:F3}");
        require(share >= 0.85, $"[{tag}] the Legacy strips are drawn with nearest pixels at the whole scale {whole} ({share:P1} of the blocks are uniform)");
    }

    // ARC-UI-2890 (AC2, AC3, AC4): the play screen of the column mode. The strips stand in the column (the original's width at the strips' scale), no wood sides are drawn (the only board is the sliver above the top strip),
    // the generated end caps stand on the strips' outer ends, the world is drawn over the whole frame; the edge elements (spell pane, combat list, bubbles) are placed against the frame's edges and the follower group stays in the column (ARC-UI-2925); an open menu
    // takes the column's window zone, the wood of the gaps and the dimmed, desaturated world at the sides (read back: its mean brightness falls).
    private async Task CheckSteamwovenColumnPlayAsync(string tag, SteamwovenHudPlan plan, Vector2I size, AreaGameBarPanel panel, Func<Task> waitPainted, Func<int, Task> frames, Action<bool, string> require)
    {
        var column = plan.Column;
        var layout = plan.Layout;
        var frame = layout.Frame;
        var topStrip = panel.StripRectForSelfTest(AreaBarStrip.Top);
        var bottomStrip = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
        require(topStrip.Position == new Vector2(column.Column.X, plan.TopStripY) && bottomStrip.Position == new Vector2(column.Column.X, plan.BottomStripY), $"[{tag}] the strips stand in the column at x {column.Column.X}");
        require(Math.Abs(topStrip.Size.X - column.Column.Width) <= 1.5f && Math.Abs(bottomStrip.Size.X - column.Column.Width) <= 1.5f, $"[{tag}] the strips are as wide as the column {column.Column.Width} (got {topStrip.Size.X} and {bottomStrip.Size.X})");
        var (freeTop, free) = AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y));
        require(free.X == column.Column.Width && AreaGameBarPanel.FreeLeft == column.Column.X && freeTop == plan.TopInset && free.Y == layout.Middle.Height,
            $"[{tag}] the windows' free area is the column's {column.Column.Width} wide at {column.Column.X} between the strips (got {free} at {AreaGameBarPanel.FreeLeft})");
        // No wood sides: the only board is the sliver above the top strip's art.
        var (topBoard, backdropShown) = panel.BoardsForSelfTest;
        require(!backdropShown && panel.DimmedWorldForSelfTest.Count == 0, $"[{tag}] no backdrop and no dimmed world with no menu");
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
        var column = plan.Column;
        var frame = plan.Layout.Frame;
        var winSize = new Vector2(size.X, size.Y);
        var scale = (float)plan.StripScale;
        var (_, free) = AreaGameBarPanel.FreeArea(winSize);
        require(AreaGameBarPanel.FreeRect(winSize) == new Rect2(frame.X, plan.TopInset, frame.Width, free.Y) && FloaterLayer().BubbleView?.Invoke() == AreaGameBarPanel.FreeRect(GetViewport().GetVisibleRect().Size),
            $"[{tag}] the bubbles' view is the frame's width between the strips {AreaGameBarPanel.FreeRect(winSize)}, not the column's");
        require(AreaGameBarPanel.EdgeRect(winSize) == new Rect2(frame.X, frame.Y, frame.Width, frame.Height) && AreaGameBarPanel.EdgeTop == frame.Y && AreaGameBarPanel.EdgeBottom(winSize) == frame.Bottom,
            $"[{tag}] the edge elements' rectangle is the whole frame {frame} (got {AreaGameBarPanel.EdgeRect(winSize)})");
        require(AreaGameBarPanel.TopScreen is { } screen && screen.Origin == new Vector2(plan.StripLeft, plan.TopStripY) && Math.Abs(screen.Scale - scale) < 1e-4f
            && Math.Abs(screen.BottomEdge - (plan.BottomStripY + AreaGameBarLayout.BottomHeight * scale)) < 1e-3f,
            $"[{tag}] the strips' screen is the column's: the follower group hangs from it (got {AreaGameBarPanel.TopScreen})");
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
            // ARC-UI-2925 (AC3): seven followers show the whole group (portraits, both scroll arrows, the show/hide button); owner decision 2026-11-20: it stays in the column and moves as one: every part stands at its original place (units from the composition's top left, so the button is the bottom strip's top left corner) at the
            // composition's scale and the group's left edge is the window's left edge.
            if (_portraitContent is { } content) portraits.Bind(InterfaceTextures(content.Art));
            portraits.Show(Enumerable.Range(0, 7).Select(i => ($"smoke:{i}", $"F{i}", (AreaLife?)new AreaLife(100, 100, 50, 60, 0), (Texture2D?)null)).ToArray());
            await frames(2);
            var origin = new Vector2(plan.StripLeft, plan.TopStripY);
            require(portraits.OriginForSelfTest == origin && Math.Abs(portraits.ScaleForSelfTest - scale) < 1e-4f, $"[{tag}] the follower group hangs from the composition's left edge at {origin} (got {portraits.OriginForSelfTest} at {portraits.ScaleForSelfTest})");
            bool Near(Rect2 got, float x, float y, float w, float h) => Math.Abs(got.Position.X - (origin.X + x * scale)) <= 1.01f && Math.Abs(got.Position.Y - (origin.Y + y * scale)) <= 1.01f
                && Math.Abs(got.Size.X - w * scale) <= 1.01f && Math.Abs(got.Size.Y - h * scale) <= 1.01f;
            require(portraits.ButtonScreenRectForSelfTest("toggle") is { } toggleRect && Near(toggleRect, 0, 428, 67, 13), $"[{tag}] the show/hide button is 67 x 13 units at (0, 428) of the composition (got {portraits.ButtonScreenRectForSelfTest("toggle")})");
            require(portraits.ButtonScreenRectForSelfTest("up") is { } upRect && Near(upRect, 0, 415, 33, 13) && portraits.ButtonScreenRectForSelfTest("down") is { } downRect && Near(downRect, 33, 415, 34, 13),
                $"[{tag}] the scroll arrows stand at (0, 415) and (33, 415) of the composition (got {portraits.ButtonScreenRectForSelfTest("up")} and {portraits.ButtonScreenRectForSelfTest("down")})");
            for (var slot = 0; slot < 6; slot++)
                require(portraits.FrameForSelfTest("smoke:" + slot) is { } slotFrame && Near(slotFrame.Rect, 11, 50 + 62 * slot, 40, 49), $"[{tag}] follower slot {slot} stands at (11, {50 + 62 * slot}) of the composition (got {portraits.FrameForSelfTest("smoke:" + slot)?.Rect})");
            // The button stands on the bottom strip's top left corner: its left edge is the column's, its bottom edge the strip's top (unit 441, AreaGameBarLayout.BottomTop), at every window size.
            require(portraits.ButtonScreenRectForSelfTest("toggle") is { } corner && Math.Abs(corner.Position.X - column.Column.X) <= 1.01f && Math.Abs(corner.End.Y - plan.BottomStripY) <= 1.01f,
                $"[{tag}] the show/hide button stands on the bottom strip's top left corner (x {column.Column.X}, bottom {plan.BottomStripY}); got {portraits.ButtonScreenRectForSelfTest("toggle")}");
            for (var wait = 0; wait < 600 && InterfaceHd.GeneratedPending > 0; wait++) await frames(1);
            await CaptureHudAsync("steamwoven-" + tag + "-followers", frames);
            require(Math.Abs(portraits.OriginForSelfTest.X - column.Column.X) <= 1e-3f, $"[{tag}] the follower group's left edge is the column's at {column.Column.X} (got {portraits.OriginForSelfTest.X})");
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

    // ARC-UI-2575 / ARC-UI-2614: every menu window the smoke can open (inventory, character sheet, logbook, world map, schematics, the save list, and since ARC-UI-2614 the loot window of a container, the
    // barter window of an NPC, a book's reading window and the main menu's pre-made and new-character screens) stands in the window zone of the layout at the layout's unit scale: the drawn body (its top
    // left and its scale) is what SteamwovenWindowZone says, and the window is captured as steamwoven-VARIANT-WxH-WINDOW.png (local). With a null plan (the legacy-before pass) only the
    // captures are made. A window whose data the map lacks (no container, no NPC to trade with, no readable book) is reported as not captured; the main menu screens come from the in-play menu.
    private sealed record SteamwovenWindowProbe(string Name, Action Open, Func<bool> IsOpen, Action Close, Func<(Vector2 Origin, float Scale)?> View, Func<SteamwovenHudPlan, SteamwovenWindowPlace> Expected, SteamwovenBackdropFamily? Family = null, bool Optional = false);

    private static SteamwovenWindowPlace ZoneBody(SteamwovenHudPlan plan) => SteamwovenWindowZone.Body(plan, 800, 400);
    // ARC-UI-2891: the two-strip screens (save and load lists, pre-made, new character, shop) stand on the fitted plate of SteamwovenColumnMenu (AreaGameBarPanel.TwoStripScreen).
    private static SteamwovenWindowPlace ZoneScreen(SteamwovenHudPlan plan) =>
        SteamwovenColumnMenu.Plan(plan.Layout.WindowWidth, plan.Layout.WindowHeight, plan.Options) is { } menu
            ? SteamwovenColumnMenu.Place(menu) : SteamwovenWindowZone.Screen(plan, AreaShopArtLayout.Window.Y);
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

    // ARC-UI-2576, ARC-UI-2890: the open menu window has the wood of its family (the census's) in the gaps of the unfitted column: the pieces of SteamwovenBackdrops, the two parts of a two-part window meeting at
    // the split, each drawn at its own rectangle with its texels one to one (the fitted column has no gap and no piece); a window with no family (the save list, whose screen hides the bars) has none. On the viewport read back, the join at
    // the top and bottom edge of the window zone has the seam ratio of SWI-VER-5 (the mean neighbour difference in a ±8 px band at the join over the one beside it, R ≤ 1.5); a join with no room for the bands outside it is skipped.
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
        var band = plan.Layout.Middle;
        // ARC-UI-2890: the zone's left and right edges border the world (dimmed), not wood, so only its top and bottom edges join the wood of the gaps.
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
            ("top", Reference(zone.Y, band.Y, zone.Y) is { } top ? SteamwovenSeam.RowRatio(rgba, width, height, zone.Y, zone.X, zone.Right, top) : null),
            ("bottom", Reference(zone.Bottom, zone.Bottom, band.Bottom) is { } bottom ? SteamwovenSeam.RowRatio(rgba, width, height, zone.Bottom, zone.X, zone.Right, bottom) : null),
        };
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
}
