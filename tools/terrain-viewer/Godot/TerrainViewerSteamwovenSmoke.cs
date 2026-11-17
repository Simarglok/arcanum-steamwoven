using Arcanum.Data.Area;
using Arcanum.Data.Written;
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
        _gameSettings = _gameSettings with { GeneratedInterface = true };
        SyncInterfaceHd();
        SteamwovenHud.Suspended = true; panel.Relayout();
        await frames(3);
        await WaitBarArtGeneratedAsync(panel, frames);
        GD.Print($"ARC-UI-2452 native steamwoven layout PASS sizes={checkedSizes}");
    }

    // ARC-UI-2575 / ARC-UI-2614: every menu window the smoke can open (inventory, character sheet, logbook, world map, schematics, the save list, and since ARC-UI-2614 the loot window of a container, the
    // barter window of an NPC, a book's reading window and the main menu's pre-made and new-character screens) stands in the window zone of the layout at the layout's unit scale: the drawn body (its top
    // left and its scale) is what SteamwovenWindowZone says, and the window is captured as steamwoven-VARIANT-WxH-WINDOW.png (local). With a null plan (the "before" variant, the old layout) only the
    // captures are made. A window whose data the map lacks (no container, no NPC to trade with, no readable book) is reported as not captured; the main menu screens come from the in-play menu.
    private sealed record SteamwovenWindowProbe(string Name, Action Open, Func<bool> IsOpen, Action Close, Func<(Vector2 Origin, float Scale)?> View, Func<SteamwovenHudPlan, SteamwovenWindowPlace> Expected);

    private static SteamwovenWindowPlace ZoneBody(SteamwovenHudPlan plan) => SteamwovenWindowZone.Body(plan, 800, 400);
    private static SteamwovenWindowPlace ZoneScreen(SteamwovenHudPlan plan) => SteamwovenWindowZone.Screen(plan, AreaShopArtLayout.Window.Y);
    // The reading window is the screen's body: its top left is the body's, 41 units below the screen's.
    private static SteamwovenWindowPlace ZoneReading(SteamwovenHudPlan plan)
    {
        var screen = ZoneScreen(plan);
        return screen with { Y = Math.Round(screen.Y + AreaReadingArtLayout.Window.Y * screen.Scale, MidpointRounding.AwayFromZero) };
    }

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
    private WrittenDocument? SteamwovenReading()
    {
        if (_areaSession is not { } session || _readingData?.Data is not { } data) return null;
        return data.Prototypes.OrderBy(p => p.Key).Where(p => !p.Value.UseScript && !p.Value.ItemSpell && ReadingKinds.Contains((WrittenSubtype)p.Value.Subtype))
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
        var windows = new SteamwovenWindowProbe[]
        {
            new("inventory", () => OpenInventory(), () => InventoryOpen, EndInventory, () => _inventoryPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody),
            new("sheet", OpenSheet, () => SheetOpen, EndSheet, () => _sheetPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody),
            new("logbook", OpenLogbook, () => LogbookOpen, CloseLogbookByPlayer, () => _logbookPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody),
            new("map", OpenWorldMap, () => WorldMapOpen, CloseWorldMapByPlayer, () => _worldMapPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody),
            new("schematics", () => OpenSchematics(), () => SchematicsOpen, CloseSchematicsByPlayer, () => _schematicsPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneBody),
            new("saves", () => OpenSaveList(AreaSaveListMode.Load), () => SaveListOpen, () => EndSaveList(), () => _saveListPanel?.ArtForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneScreen),
            new("loot", () => { if (SteamwovenLootTarget() is { } target) OpenContainerContents(target); }, () => ContainerLootOpen, EndInventory,
                () => _inventoryPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody),
            new("barter", () => { if (SteamwovenBarter() is { } barter) OpenBarterWindow(barter); }, () => BarterOpen, CloseBarterByPlayer,
                () => _barterPanel is { } p ? (p.GridAreaForSelfTest.GetGlobalRect().Position, p.GridAreaForSelfTest.FrameScale) : null, ZoneBody),
            new("reading", () => { if (SteamwovenReading() is { } document) OpenReading(document, "steamwoven", replaceWindows: true); }, () => ReadingOpen, CloseReadingByPlayer,
                () => _readingPanel?.ArtViewForSelfTest is { } v ? (v.Position, v.Scale.X) : null, ZoneReading),
            new("premade", () => SteamwovenOpenMenuWindow(420, require), () => _mainMenu is { Window: AreaMenuWindow.PickCharacter }, () => EndMainMenu("steamwoven smoke"),
                () => _mainMenuPanel is { PremadeArtMode: true } m ? (m.PremadeArtForSelfTest.Position, m.PremadeArtForSelfTest.Scale.X) : null, ZoneScreen),
            new("newcharacter", () => SteamwovenOpenMenuWindow(421, require), () => _mainMenu is { Window: AreaMenuWindow.NewCharacter }, () => EndMainMenu("steamwoven smoke"),
                () => _mainMenuPanel is { NewCharacterArtMode: true } m ? (m.NewCharacterArtForSelfTest.Position, m.NewCharacterArtForSelfTest.Scale.X) : null, ZoneScreen),
        };
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
                require(plan is null, $"[{tag}] the {name} window opened (the map has no data for it?)");
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
                GD.Print($"ARC-UI-2575 steamwoven window [{tag}] {name} origin=({origin.X:F1},{origin.Y:F1}) scale={scale:F4} zone={plan.Layout.WindowZone}");
            }
            await CaptureHudAsync("steamwoven-" + tag + "-" + name, frames);
            window.Close();
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
