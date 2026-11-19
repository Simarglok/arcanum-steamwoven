using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2891: opt-in part of the menu smoke (ARC_AREA_MENU_COLUMN=1, or a list of `column` and `column-thin`): the full-screen menus (main, options) and the two-strip screens (pre-made,
    // new character; the save list has its own pass inside the save window's smoke, ARC_AREA_MENU_COLUMN with `--stages scene`) in the column mode of the Steamwoven layout (TRD "Column mode"), at each size of ARC_STEAMWOVEN_SIZES (default: the AC5 sizes of the play-screen slice, 2560x1440, 1920x1080,
    // 2560x1600, 1920x1200, 3440x1440, 1280x800) for each sides option of ARC_AREA_MENU_COLUMN_SIDES (default `black,vignette`): the 800 x 600 plate stands in the column plan's full-screen zone at the strip scale,
    // no full-width backdrop, black or vignette sides, generated wood only in a vertical gap, the pointer hitting the labels where they are drawn (the full-screen windows, through the plan's mapping).
    // ARC_AREA_MENU_CAPTURE=PATH.png also saves PATH-column-VARIANT-SIDES-WINDOW-WxH.png (local, never in git). The window is not resized: the root viewport takes the size as its logical render target.
    private static IReadOnlyList<Vector2I> ColumnMenuSizes()
    {
        var text = System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_SIZES");
        if (string.IsNullOrWhiteSpace(text)) text = "2560x1440,1920x1080,2560x1600,1920x1200,3440x1440,1280x800";
        return [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part => part.Split('x'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _)).Select(parts => new Vector2I(int.Parse(parts[0]), int.Parse(parts[1])))];
    }

    private async Task MenuColumnForSmoke(Action<bool, string> require, Func<int, Task> frames)
    {
        var panel = _mainMenuPanel!;
        var art = panel.ArtForSelfTest;
        var window = GetWindow();
        var (savedMode, savedAspect, savedSize, savedFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        var (savedOptions, savedSides) = (SteamwovenHud.Options, SteamwovenColumnMenu.Sides);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } path ? System.IO.Path.ChangeExtension(path, null) : null;
        var variants = ColumnSmokeVariants();
        var sidesList = ColumnSmokeSides();
        async Task SaveAsync(string name)
        {
            if (capture is null) return;
            await frames(2);
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "the capture saved: " + name);
        }
        async Task WaitGapsAsync(Func<SteamwovenColumnSidesView> sides)
        {
            for (var wait = 0; wait < 1800 && sides().PaintPendingForSelfTest; wait++) await frames(1);
            await frames(2);
        }
        var checkedCases = 0;
        try
        {
            _gameSettings = _gameSettings with { HdInterface = true, GeneratedInterface = true };
            SteamwovenHud.Suspended = false;
            SyncInterfaceHd();
            window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            window.ContentScaleFactor = 1;
            // The screens: name, open (from the main window), is open, the drawn plate (position, scale) of its view, the sides view that surrounds it, close (back to the main window).
            var screens = new (string Name, Action Open, Func<bool> IsOpen, Func<(Vector2, float)?> Plate, Func<SteamwovenColumnSidesView> Sides, Action Close)[]
            {
                ("main", () => { }, () => art.Visible && _mainMenu?.Window == AreaMenuWindow.Main, () => (art.Position, art.Scale.X), () => panel.ColumnSidesForSelfTest.Sides, () => { }),
                ("options", () =>
                {
                    var options = _mainMenu!.Shown.ToList().FindIndex(button => button.Message == 12);
                    require(options >= 0, "the main window offers Options");
                    MainMenuCommand(() => _mainMenu.Activate(options));
                }, () => art.Visible && _mainMenu?.Window == AreaMenuWindow.Options, () => (art.Position, art.Scale.X), () => panel.ColumnSidesForSelfTest.Sides,
                    () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; })),
                ("premade", () => SteamwovenOpenMenuWindow(420, require), () => panel.PremadeArtMode,
                    () => (panel.PremadeArtForSelfTest.Position, panel.PremadeArtForSelfTest.Scale.X), () => panel.ColumnSidesForSelfTest.Sides, ReopenMainForColumnSmoke),
                ("newcharacter", () => SteamwovenOpenMenuWindow(421, require), () => panel.NewCharacterArtMode,
                    () => (panel.NewCharacterArtForSelfTest.Position, panel.NewCharacterArtForSelfTest.Scale.X), () => panel.ColumnSidesForSelfTest.Sides, ReopenMainForColumnSmoke),
            };
            foreach (var (variantName, options) in variants)
            foreach (var sides in sidesList)
            {
                SteamwovenHud.Options = options;
                SteamwovenColumnMenu.Sides = sides;
                foreach (var screen in screens)
                {
                    screen.Open();
                    await frames(3);
                    require(screen.IsOpen(), $"[{variantName}-{sides}] the {screen.Name} window opened (window {_mainMenu?.Window})");
                    foreach (var size in ColumnMenuSizes())
                    {
                        window.ContentScaleSize = size;
                        for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                        require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                        await frames(3);
                        var tag = $"{variantName}-{sides}-{screen.Name}-{size.X}x{size.Y}";
                        var plan = SteamwovenColumnMenu.Plan(size.X, size.Y, options);
                        require(plan is not null && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) == plan, $"[{tag}] the menu is laid out by the column plan");
                        var layout = plan!;
                        var surround = screen.Sides();
                        await WaitGapsAsync(screen.Sides);
                        await CheckColumnCaseAsync(tag, layout, sides, surround, screen.Plate() ?? throw new InvalidOperationException($"[{tag}] the {screen.Name} view is not drawn"),
                            panel.FrameBackdropForSelfTest.Shown, screen.Name is "main" or "options" ? art : null,
                            screen.Name is "main" or "options" ? art.EdgeTone : AreaMenuArtView.DefaultEdgeTone, frames, require);
                        await SaveAsync($"column-{tag}");
                        checkedCases++;
                    }
                    screen.Close();
                    await frames(3);
                }
            }
            // The column option off again: the full-width plan (HD on) or none (HD off) stands as before, and the surroundings are gone.
            SteamwovenHud.Options = SteamwovenLayoutOptions.Default;
            await frames(3);
            require(art.FullScreenPlan is { IsColumn: false } && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) is null, "with the column option off the full-width plan stands");
            require(panel.ColumnSidesForSelfTest is { Shown: false } && panel.ColumnSidesForSelfTest.Sides.BlackForSelfTest.Count == 0, "with the column option off the column's surroundings are gone");
            // The column option set with the HD option off: no column plan, the whole-factor plate stands (AC2).
            SteamwovenHud.Options = SteamwovenLayoutOptions.ColumnOriginal;
            _gameSettings = _gameSettings with { HdInterface = false };
            SyncInterfaceHd();
            await frames(3);
            require(!InterfaceHd.Enabled && art.FullScreenPlan is null && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) is null && !panel.ColumnSidesForSelfTest.Shown,
                "with the HD option off the column option draws nothing: no plan, no surroundings");
            GD.Print($"ARC-UI-2891 native-smoke menu column PASS cases={checkedCases}");
        }
        finally
        {
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            (SteamwovenHud.Options, SteamwovenColumnMenu.Sides) = (savedOptions, savedSides);
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
        }
    }

    private static IReadOnlyList<(string Name, SteamwovenLayoutOptions Options)> ColumnSmokeVariants()
    {
        var text = System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_COLUMN");
        return [.. (string.IsNullOrWhiteSpace(text) || text == "1" ? "column,column-thin" : text)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => (Name: name, Options: name switch
            {
                "column" => SteamwovenLayoutOptions.ColumnOriginal,
                "column-thin" => SteamwovenLayoutOptions.ColumnThin,
                _ => throw new InvalidOperationException($"ARC-UI-2891 column menu smoke: ARC_AREA_MENU_COLUMN entry '{name}' is not column or column-thin"),
            }))];
    }

    private static IReadOnlyList<SteamwovenColumnSides> ColumnSmokeSides() =>
        [.. (System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_COLUMN_SIDES") is { Length: > 0 } text ? text : "black,vignette")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => SteamwovenColumnMenu.ParseSides(name) ?? throw new InvalidOperationException($"ARC-UI-2891 column menu smoke: sides '{name}' is not black or vignette"))];

    /// <summary>
    /// ARC-UI-2891: the save list (an open save or load window of the original's look) in the column mode, at each size and sides option of the menu pass: the 800 × 600 screen stands in the column's full-screen
    /// zone, the sides are black or a vignette, no wood sides; saved as PATH-column-VARIANT-SIDES-saves-WxH.png with ARC_AREA_SAVE_CAPTURE.
    /// </summary>
    private async Task SaveListColumnForSmoke(Action<bool, string> require, Func<int, Task> frames)
    {
        var panel = _saveListPanel!;
        var window = GetWindow();
        var (savedMode, savedAspect, savedSize, savedFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        var (savedOptions, savedSides) = (SteamwovenHud.Options, SteamwovenColumnMenu.Sides);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SAVE_CAPTURE") is { Length: > 0 } path ? System.IO.Path.ChangeExtension(path, null) : null;
        var cases = 0;
        try
        {
            _gameSettings = _gameSettings with { HdInterface = true, GeneratedInterface = true };
            SteamwovenHud.Suspended = false;
            SyncInterfaceHd();
            window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            window.ContentScaleFactor = 1;
            foreach (var (variantName, options) in ColumnSmokeVariants())
            foreach (var sides in ColumnSmokeSides())
            {
                (SteamwovenHud.Options, SteamwovenColumnMenu.Sides) = (options, sides);
                foreach (var size in ColumnMenuSizes())
                {
                    window.ContentScaleSize = size;
                    for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                    require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                    await frames(3);
                    var tag = $"{variantName}-{sides}-saves-{size.X}x{size.Y}";
                    var plan = SteamwovenColumnMenu.Plan(size.X, size.Y, options);
                    require(plan is not null && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) == plan, $"[{tag}] the save list is laid out by the column plan");
                    var sidesView = panel.ColumnSidesForSelfTest;
                    for (var wait = 0; wait < 1800 && sidesView.PaintPendingForSelfTest; wait++) await frames(1);
                    await frames(2);
                    var art = panel.ArtForSelfTest;
                    await CheckColumnCaseAsync(tag, plan!, sides, sidesView, (art.Position, art.Scale.X), false, null, AreaMenuArtView.DefaultEdgeTone, frames, require);
                    if (capture is not null)
                    {
                        using var image = GetViewport().GetTexture().GetImage();
                        require(image.SavePng($"{capture}-column-{tag}.png") == Error.Ok, "the capture saved: " + tag);
                    }
                    cases++;
                }
            }
            GD.Print($"ARC-UI-2891 native-smoke save list column PASS cases={cases}");
        }
        finally
        {
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            (SteamwovenHud.Options, SteamwovenColumnMenu.Sides) = (savedOptions, savedSides);
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
        }
    }

    /// <summary>The checks of one column case (the plate, the surroundings, the hit tests of a full-screen window, the pixels); shared by the main menu's pass and the save list's.</summary>
    private async Task CheckColumnCaseAsync(string tag, SteamwovenFullScreenPlan layout, SteamwovenColumnSides sides, SteamwovenColumnSidesView surround, (Vector2 Position, float Scale) plate,
        bool frameBackdropShown, AreaMenuArtView? art, Color tone, Func<int, Task> frames, Action<bool, string> require)
    {
        var (position, scale) = plate;
        // The plate: the column's full-screen zone, drawn at the strip scale.
        require(Math.Abs(scale - (float)layout.Factor) < 1e-4f, $"[{tag}] the plate is drawn at {layout.Factor:F4}, got {scale}");
        require(Math.Abs(position.X - layout.Zone.X) <= 1.01f && Math.Abs(position.Y - layout.Zone.Y) <= 1.01f, $"[{tag}] the plate stands at {layout.Zone}, got {position}");
        // The plate: the column's full-screen zone, drawn at the strip scale.
        // No full-width backdrop and no wood sides; the black and vignette rectangles are the plan's.
        require(!frameBackdropShown, $"[{tag}] no full-width backdrop shows");
        require(surround.Visible && surround.BlackForSelfTest.SequenceEqual(SteamwovenColumnMenu.BlackRects(layout)), $"[{tag}] the sides are the plan's black rectangles");
        var (wantLeft, wantRight) = SteamwovenColumnMenu.VignetteRects(layout);
        var vignette = surround.VignetteForSelfTest;
        if (sides == SteamwovenColumnSides.Vignette)
            require(vignette.Count == (wantLeft.Width > 0 ? 1 : 0) + (wantRight.Width > 0 ? 1 : 0)
                && vignette.All(rect => rect.Position.Y == layout.Zone.Y && rect.Size.Y == layout.Zone.Height), $"[{tag}] the vignette hangs beside the plate: {vignette.Count} rectangles");
        else require(vignette.Count == 0, $"[{tag}] no vignette with black sides");
        // A vertical gap takes the generated wood; none where the plate fills the column.
        var gaps = surround.GapsForSelfTest;
        var wantGaps = new[] { layout.Column!.FullScreen.GapAbove, layout.Column.FullScreen.GapBelow };
        for (var g = 0; g < 2; g++)
            require(gaps[g].Shown == (wantGaps[g].Height > 0) && (!gaps[g].Shown || gaps[g].Rect == new Rect2(wantGaps[g].X, wantGaps[g].Y, wantGaps[g].Width, wantGaps[g].Height)),
                $"[{tag}] the gap {g} ({wantGaps[g]}) shows wood only where the plate leaves one: shown={gaps[g].Shown} at {gaps[g].Rect}");
        // Hit testing (the full-screen windows): the pointer over each drawn label lights it, over the black side or a gap it lights none.
        if (art is not null)
        {
            var buttons = art.ButtonsForSelfTest;
            require(buttons.Count > 0, $"[{tag}] the window shows labels");
            for (var slot = 0; slot < buttons.Count; slot++)
            {
                var (cx, cy) = layout.ToDevice(buttons[slot].X + buttons[slot].Width / 2.0, buttons[slot].Y + buttons[slot].Height / 2.0);
                require(layout.HitTest(buttons, cx, cy) == slot, $"[{tag}] label {slot}'s drawn centre hits it in the plan");
                var centre = art.ButtonCentreForSelfTest(slot) ?? throw new InvalidOperationException($"[{tag}] no centre for label {slot}");
                require(Math.Abs(centre.X - cx) < 0.5 && Math.Abs(centre.Y - cy) < 0.5, $"[{tag}] label {slot} stands at ({cx:F1}, {cy:F1}), got {centre}");
                GetViewport().PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre }, true);
                await frames(1);
                require(art.HoveredForSelfTest == slot, $"[{tag}] the pointer over label {slot} lights it, got {art.HoveredForSelfTest}");
            }
            var side = new Vector2(Math.Max(1, layout.Column!.Column.X / 2), layout.Zone.Y + layout.Zone.Height / 2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = side, GlobalPosition = side }, true);
            await frames(1);
            require(art.HoveredForSelfTest == -1, $"[{tag}] the pointer on the black side lights no label");
        }
        // Pixels: the side is black (or the vignette's tone beside the plate and black at the far edge); a gap is wood, not black.
        using (var image = GetViewport().GetTexture().GetImage())
        {
            var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
            Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
            static int Sum(Color c) => c.R8 + c.G8 + c.B8;
            var rowY = layout.Zone.Y + layout.Zone.Height / 2;
            var far = Pixel(1, rowY);
            var near = Pixel(layout.Zone.X - 2, rowY);
            if (sides == SteamwovenColumnSides.Black) require(Sum(far) == 0 && Sum(near) == 0, $"[{tag}] the sides are black (far {far}, near {near})");
            else
        {
            require(Sum(far) <= 12 && Sum(near) >= Sum(far), $"[{tag}] the vignette fades from the plate's edge tone ({near}) to black ({far})");
            // A drawn vignette is lit beside the plate (a tone that is itself near black proves nothing).
            if (tone.R8 + tone.G8 + tone.B8 >= 24) require(Sum(near) > 0, $"[{tag}] the vignette is drawn beside the plate: {near} for the tone {tone}");
        }
            for (var g = 0; g < 2; g++)
                if (wantGaps[g].Height >= 4)
                {
                    var inside = Pixel(wantGaps[g].X + wantGaps[g].Width / 2, wantGaps[g].Y + wantGaps[g].Height / 2);
                    require(Sum(inside) > 20, $"[{tag}] the gap {g} shows wood ({inside})");
                }
        }

    }

    // Back to the main window after a screen the smoke opened through the main menu's buttons.
    private void ReopenMainForColumnSmoke()
    {
        if (MainMenuOpen) EndMainMenu("column smoke");
        OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!));
    }
}
