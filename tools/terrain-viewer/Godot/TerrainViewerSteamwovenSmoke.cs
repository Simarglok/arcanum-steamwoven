using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
                    Require(panel.StripRectForSelfTest(AreaBarStrip.Top).Position == new Vector2(plan.StripLeft, plan.TopStripY)
                        && panel.StripRectForSelfTest(AreaBarStrip.Bottom).Position == new Vector2(plan.StripLeft, plan.BottomStripY), $"[{tag}] the strips stand where the plan says");
                    Require(AreaGameBarPanel.TopInset == layout.Middle.Band.Y && AreaGameBarPanel.BottomInset == size.Y - layout.Middle.Band.Bottom
                        && AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y)).Size.Y == layout.Middle.Band.Height, $"[{tag}] the free area is the middle band {layout.Middle.Band}");
                    // ARC-UI-2608 (SWI-LAY-11, variant B): each control of the HUD draws and hits in the rectangle of layout.Control(id): the node's rectangle after every scale is the layout's, and a
                    // full-width hit area stands over each band. The vials are the hero gauges' (placed when they show).
                    foreach (var control in layout.Controls)
                    {
                        var drawn = panel.ControlRectForSelfTest(control.Id);
                        if (control.Id is SteamwovenControlId.HealthVial or SteamwovenControlId.FatigueVial)
                        {
                            if (HeroGaugesForSelfTest is not { ShownForSelfTest: true } gauges) continue;
                            var placement = gauges.PlacementForSelfTest;
                            if (!placement.InBars) continue;
                            drawn = control.Id == SteamwovenControlId.HealthVial ? placement.Health : placement.Fatigue;
                        }
                        var want = control.Device;
                        Require(drawn is { } rect && Math.Abs(rect.Position.X - want.X) < 0.6f && Math.Abs(rect.Position.Y - want.Y) < 0.6f
                            && Math.Abs(rect.Size.X - want.Width) < 0.6f && Math.Abs(rect.Size.Y - want.Height) < 0.6f, $"[{tag}] {control.Id} is drawn in the layout's rectangle {want} (got {drawn})");
                    }
                    Require(panel.BandHitsForSelfTest.Select(hit => new Rect2I((int)hit.Position.X, (int)hit.Position.Y, (int)hit.Size.X, (int)hit.Size.Y)).SequenceEqual(
                        [new Rect2I(layout.Top.Band.X, layout.Top.Band.Y, layout.Top.Band.Width, layout.Top.Band.Height), new Rect2I(layout.Bottom.Band.X, layout.Bottom.Band.Y, layout.Bottom.Band.Width, layout.Bottom.Band.Height)])
                        && panel.BandHitsForSelfTest.All(hit => hit.Visible && hit.MouseFilter == Control.MouseFilterEnum.Stop), $"[{tag}] each band has a full-width hit area");
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
                    await CheckSteamwovenScreenPartsAsync(tag, plan, size, frames, Require);
                    await CaptureSteamwovenPartsAsync(tag, frames);
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
        _gameSettings = _gameSettings with { GeneratedInterface = true };
        SyncInterfaceHd();
        SteamwovenHud.Suspended = true; panel.Relayout();
        await frames(3);
        await WaitBarArtGeneratedAsync(panel, frames);
        GD.Print($"ARC-UI-2452 native steamwoven layout PASS sizes={checkedSizes}");
    }

    // ARC-UI-2510: the parts the layout model leaves to the screen's own units follow the strips: the free area is the frame's, the screen the follower portraits hang in and the floaters' scale are where the strips
    // put them, the sleep and fate views keep their own whole scale (placed on that screen), and the generated textures of the views paint at the view's own scale, not the strips'.
    private async Task CheckSteamwovenScreenPartsAsync(string tag, SteamwovenHudPlan plan, Vector2I size, Func<int, Task> frames, Action<bool, string> require)
    {
        var frame = plan.Layout.Frame;
        var (freeTop, free) = AreaGameBarPanel.FreeArea(new Vector2(size.X, size.Y));
        require(free.X == frame.Width && AreaGameBarPanel.FreeLeft == frame.X && freeTop == plan.TopInset, $"[{tag}] the free width is the frame's {frame.Width} at x {frame.X} (got {free.X} at {AreaGameBarPanel.FreeLeft})");
        var scale = (float)plan.StripScale;
        require(AreaGameBarPanel.TopScreen is { } screen && screen.Origin == new Vector2(plan.StripLeft, plan.Layout.Top.Band.Bottom - (int)Math.Round(AreaGameBarLayout.TopHeight * plan.StripScale, MidpointRounding.AwayFromZero)) && Math.Abs(screen.Scale - scale) < 1e-4f
            && Math.Abs(screen.BottomEdge - plan.Layout.Bottom.Band.Bottom) < 1e-2f, $"[{tag}] the screen hangs from the top frame at the HUD's unit scale and ends at the bottom band's lower edge ({AreaGameBarPanel.TopScreen})");
        var origin = AreaGameBarPanel.TopScreen!.Value.Origin;
        var floaters = new AreaFloaterLayer(); var portraits = new AreaFollowerPortraits(); var sleep = new AreaSleepView(); var fate = new AreaFateView();
        try
        {
            foreach (var node in new Node[] { floaters, portraits, sleep, fate }) AddChild(node);
            await frames(2);
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
                    var otherOrigin = new Vector2(other.StripLeft, other.Layout.Top.Band.Bottom - (int)Math.Round(AreaGameBarLayout.TopHeight * other.StripScale, MidpointRounding.AwayFromZero));
                    require(Math.Abs(floaters.DrawScale - otherScale) < 1e-4f && portraits.OriginForSelfTest == otherOrigin && Math.Abs(portraits.ScaleForSelfTest - otherScale) < 1e-4f,
                        $"[{tag}] the floaters and the portraits follow a new layout by themselves: scale {otherScale}, origin {otherOrigin} (got {floaters.DrawScale}, {portraits.OriginForSelfTest} at {portraits.ScaleForSelfTest})");
                    require((sleep.Position - (otherOrigin + new Vector2(AreaSleepLayout.ScreenX, AreaSleepLayout.ScreenY) * otherScale)).Length() <= 1e-2f
                        && (fate.Position - (otherOrigin + new Vector2(AreaFateLayout.ScreenX, AreaFateLayout.ScreenY) * otherScale)).Length() <= 1e-2f, $"[{tag}] the sleep and fate views follow a new layout by themselves");
                }
                finally { SteamwovenHud.Options = saved; _gameBarPanel!.Relayout(); await frames(2); }
            }
        }
        finally { foreach (var node in new Node[] { floaters, portraits, sleep, fate }) { RemoveChild(node); node.QueueFree(); } }
    }

    // ARC-UI-2575: every menu window the smoke can open (inventory, character sheet, logbook, world map, schematics, the save list) stands in the window zone of the layout at the layout's unit scale:
    // the drawn body (its top left and its scale) is what SteamwovenWindowZone says, and the window is captured as steamwoven-VARIANT-WxH-WINDOW.png (local). With a null plan (the "before" variant, the old
    // layout) only the captures are made. The loot, barter, reading, pre-made and new-character windows need an NPC, a container, a written item or the main menu: they take the same hook
    // (unit tests of the hook) and are captured with the window hooks of #2489.
    private async Task VerifySteamwovenWindowsAsync(string tag, SteamwovenHudPlan? plan, AreaGameBarPanel panel, Func<int, Task> frames, Func<Task> waitPainted, Action<bool, string> require)
    {
        var windows = new (string Name, Action Open, Func<bool> IsOpen, Action Close, Func<(Vector2 Origin, float Scale, int BodyTop)?> View)[]
        {
            ("inventory", () => OpenInventory(), () => InventoryOpen, EndInventory, () => _inventoryPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale, 0) : null),
            ("sheet", OpenSheet, () => SheetOpen, EndSheet, () => _sheetPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X, 0) : null),
            ("logbook", OpenLogbook, () => LogbookOpen, CloseLogbookByPlayer, () => _logbookPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X, 0) : null),
            ("map", OpenWorldMap, () => WorldMapOpen, CloseWorldMapByPlayer, () => _worldMapPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X, 0) : null),
            ("schematics", () => OpenSchematics(), () => SchematicsOpen, CloseSchematicsByPlayer, () => _schematicsPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X, 0) : null),
            ("saves", () => OpenSaveList(AreaSaveListMode.Load), () => SaveListOpen, () => EndSaveList(), () => _saveListPanel?.ArtForSelfTest is { } v ? (v.Position, v.Scale.X, AreaShopArtLayout.Window.Y) : null),
        };
        foreach (var (name, open, isOpen, close, view) in windows)
        {
            if (isOpen()) close();
            await frames(2);
            open();
            await frames(3);
            if (!isOpen()) { GD.Print($"ARC-UI-2575 steamwoven window [{tag}] {name} did not open (not captured)"); continue; }
            await waitPainted();
            await frames(2);
            if (plan is not null)
            {
                require(view() is not null, $"[{tag}] the {name} window shows the original's look (a view to measure)");
                var (origin, scale, bodyTop) = view()!.Value;
                // A window that docks between the strips stands with its body in the zone; a screen (the save list) has its body at 41 units from the screen's top.
                var expected = bodyTop > 0 ? SteamwovenWindowZone.Screen(plan, bodyTop) : SteamwovenWindowZone.Body(plan, 800, 400);
                var (ex, ey) = (expected.X, expected.Y);
                require(Math.Abs(scale - expected.Scale) < 0.002 && Math.Abs(origin.X - ex) <= 1.01 && Math.Abs(origin.Y - ey) <= 1.01,
                    $"[{tag}] the {name} window is at the zone place ({ex:F1}, {ey:F1}) scale {expected.Scale:F4}, got ({origin.X:F1}, {origin.Y:F1}) scale {scale:F4}");
                GD.Print($"ARC-UI-2575 steamwoven window [{tag}] {name} origin=({origin.X:F1},{origin.Y:F1}) scale={scale:F4} zone={plan.Layout.WindowZone}");
            }
            await CaptureHudAsync("steamwoven-" + tag + "-" + name, frames);
            close();
            await frames(3);
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
