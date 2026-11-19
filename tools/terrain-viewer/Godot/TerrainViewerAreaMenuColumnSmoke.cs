using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2891: opt-in part of the menu smoke (ARC_AREA_MENU_COLUMN=1): the full-screen menus (main, options) and the two-strip screens (pre-made, new character; the save list has its own pass inside
    // the save window's smoke, ARC_AREA_MENU_COLUMN with `--stages scene` or `menu`) in column mode of the Steamwoven layout (TRD "Column mode", owner decision 2026-11-19: the plate fills the window's height from edge
    // to edge, black beside it), at each size of ARC_STEAMWOVEN_SIZES (default: the AC5 sizes, 2560x1440, 1920x1080, 2560x1600, 1920x1200, 3440x1440, 1280x800): the plate's scale and place, the black rectangles, no
    // full-width backdrop, the pointer hitting the labels where they are drawn (the full-screen windows) and not on the black, the pixels of the black side. ARC_AREA_MENU_CAPTURE=PATH.png also saves
    // PATH-column-WINDOW-WxH.png (local, never in git). The window is not resized: the root viewport takes the size as its logical render target.
    private static bool ColumnMenuSmokeOn() => System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_COLUMN") is { Length: > 0 } value && value != "0";

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
        var savedOptions = SteamwovenHud.Options;
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } path ? System.IO.Path.ChangeExtension(path, null) : null;
        async Task SaveAsync(string name)
        {
            if (capture is null) return;
            await frames(2);
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "the capture saved: " + name);
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
            {
                var options = SteamwovenLayoutOptions.ColumnOriginal;
                SteamwovenHud.Options = options;
                foreach (var screen in screens)
                {
                    screen.Open();
                    await frames(3);
                    require(screen.IsOpen(), $"[column] the {screen.Name} window opened (window {_mainMenu?.Window})");
                    foreach (var size in ColumnMenuSizes())
                    {
                        window.ContentScaleSize = size;
                        for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                        require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                        await frames(3);
                        var tag = $"{screen.Name}-{size.X}x{size.Y}";
                        var plan = SteamwovenColumnMenu.Plan(size.X, size.Y, options);
                        require(plan is not null && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) == plan, $"[{tag}] the menu is laid out by the column plan");
                        var layout = plan!;
                        await frames(2);
                        await CheckColumnCaseAsync(tag, layout, screen.Sides(), screen.Plate() ?? throw new InvalidOperationException($"[{tag}] the {screen.Name} view is not drawn"),
                            panel.FrameBackdropForSelfTest.Shown, screen.Name is "main" or "options" ? art : null, frames, require);
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
            SteamwovenHud.Options = savedOptions;
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
        }
    }

    /// <summary>
    /// ARC-UI-2891: the save list (an open save or load window of the original's look) in column mode at each size of the menu pass: the 800 × 600 screen fills the window's height from edge to edge and the rest is
    /// black; saved as PATH-column-saves-WxH.png with ARC_AREA_SAVE_CAPTURE.
    /// </summary>
    private async Task SaveListColumnForSmoke(Action<bool, string> require, Func<int, Task> frames)
    {
        var panel = _saveListPanel!;
        var window = GetWindow();
        var (savedMode, savedAspect, savedSize, savedFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        var savedOptions = SteamwovenHud.Options;
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
            SteamwovenHud.Options = SteamwovenLayoutOptions.ColumnOriginal;
            foreach (var size in ColumnMenuSizes())
            {
                window.ContentScaleSize = size;
                for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                await frames(4);
                var tag = $"saves-{size.X}x{size.Y}";
                var plan = SteamwovenColumnMenu.Plan(size.X, size.Y, SteamwovenLayoutOptions.ColumnOriginal);
                require(plan is not null && AreaGameBarPanel.ColumnMenuPlan(window.GetVisibleRect().Size) == plan, $"[{tag}] the save list is laid out by the column plan");
                var art = panel.ArtForSelfTest;
                await CheckColumnCaseAsync(tag, plan!, panel.ColumnSidesForSelfTest, (art.Position, art.Scale.X), false, null, frames, require);
                if (capture is not null)
                {
                    using var image = GetViewport().GetTexture().GetImage();
                    require(image.SavePng($"{capture}-column-{tag}.png") == Error.Ok, "the capture saved: " + tag);
                }
                cases++;
            }
            GD.Print($"ARC-UI-2891 native-smoke save list column PASS cases={cases}");
        }
        finally
        {
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            SteamwovenHud.Options = savedOptions;
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
            // The window and its views take the restored size and layout before the caller measures them again.
            await frames(4);
        }
    }

    /// <summary>The checks of one column case (the plate, the black around it, the hit tests of a full-screen window, the pixels); shared by the main menu's pass and the save list's.</summary>
    private async Task CheckColumnCaseAsync(string tag, SteamwovenFullScreenPlan layout, SteamwovenColumnSidesView surround, (Vector2 Position, float Scale) plate,
        bool frameBackdropShown, AreaMenuArtView? art, Func<int, Task> frames, Action<bool, string> require)
    {
        var (position, scale) = plate;
        // The plate fills the window's height from edge to edge (the width limits a window narrower than 4:3), drawn at the fitted scale.
        var (fitFactor, fitZone) = SteamwovenColumnMenu.Fit(layout.Layout.WindowWidth, layout.Layout.WindowHeight);
        require(layout.Zone == fitZone && Math.Abs(layout.Factor - fitFactor) < 1e-9, $"[{tag}] the plan is the fitted plate {fitZone}");
        require(layout.Layout.WindowWidth * 3 < layout.Layout.WindowHeight * 4 || (layout.Zone.Y == 0 && layout.Zone.Bottom == layout.Layout.WindowHeight), $"[{tag}] the plate spans the window's height: {layout.Zone}");
        require(Math.Abs(scale - (float)layout.Factor) < 1e-4f, $"[{tag}] the plate is drawn at {layout.Factor:F4}, got {scale}");
        require(Math.Abs(position.X - layout.Zone.X) <= 1.01f && Math.Abs(position.Y - layout.Zone.Y) <= 1.01f, $"[{tag}] the plate stands at {layout.Zone}, got {position}");
        // No full-width backdrop; the black is the plan's.
        require(!frameBackdropShown, $"[{tag}] no full-width backdrop shows");
        require(surround.Visible && surround.BlackForSelfTest.SequenceEqual(SteamwovenColumnMenu.BlackRects(layout)), $"[{tag}] the black is the plan's rectangles");
        // Hit testing (the full-screen windows): the pointer over each drawn label lights it, over the black beside the plate it lights none.
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
            var side = new Vector2(Math.Max(1, layout.Zone.X / 2), layout.Zone.Y + layout.Zone.Height / 2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = side, GlobalPosition = side }, true);
            await frames(1);
            require(art.HoveredForSelfTest == -1, $"[{tag}] the pointer on the black lights no label");
        }
        // Pixels: the black beside the plate is black, and the plate's own pixels are not.
        using var image = GetViewport().GetTexture().GetImage();
        var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
        static int Sum(Color c) => c.R8 + c.G8 + c.B8;
        var rowY = layout.Zone.Y + layout.Zone.Height / 2;
        if (layout.Zone.X > 4)
        {
            var far = Pixel(1, rowY);
            var near = Pixel(layout.Zone.X - 2, rowY);
            require(Sum(far) == 0 && Sum(near) == 0, $"[{tag}] the black is black (far {far}, near {near})");
        }
    }

    // Back to the main window after a screen the smoke opened through the main menu's buttons.
    private void ReopenMainForColumnSmoke()
    {
        if (MainMenuOpen) EndMainMenu("column smoke");
        OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!));
    }
}
