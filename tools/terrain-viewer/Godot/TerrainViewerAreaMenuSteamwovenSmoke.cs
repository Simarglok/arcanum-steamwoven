using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2577: opt-in part of the menu smoke (ARC_AREA_MENU_STEAMWOVEN=1): the full-screen menus (the main window and the options window) are laid out by the Steamwoven layout model
    // (SWI-BDR-6) at each size of ARC_STEAMWOVEN_SIZES (default: the TRD's verification sizes, SWI-VER-6): the 800 x 600 plate centred in the frame at `f · 3.6`, a generated backdrop over the
    // whole frame, black outside it, and the pointer hitting the labels where they are drawn. The window is not resized (a Mac screen may be smaller than 3840 x 2160): the root viewport is given
    // the size as its logical render target. ARC_AREA_MENU_CAPTURE=PATH.png also saves PATH-steamwoven-WINDOW-WxH.png and, as the comparison, PATH-steamwoven-WINDOW-WxH-before.png (local, never in git).
    // ARC-DEV-2672: with ARC_AREA_MENU_CAPTURE the same windows are also saved in the Original look (the HD option off, the whole-factor plate, no plan)
    // at each size as PATH-legacy-WINDOW-WxH.png, with no layout checks beyond the HD option being off; the settings are put back after each capture.
    private async Task MenuSteamwovenForSmoke(Action<bool, string> require, Func<int, Task> frames)
    {
        var panel = _mainMenuPanel!;
        var art = panel.ArtForSelfTest;
        var window = GetWindow();
        var (savedMode, savedAspect, savedSize, savedFactor) = (window.ContentScaleMode, window.ContentScaleAspect, window.ContentScaleSize, window.ContentScaleFactor);
        var (savedHd, savedGenerated, savedSuspended) = (_gameSettings.HdInterface, _gameSettings.GeneratedInterface, SteamwovenHud.Suspended);
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } path ? System.IO.Path.ChangeExtension(path, null) : null;
        async Task WaitBackdropAsync()
        {
            for (var wait = 0; wait < 1800 && (panel.FrameBackdropForSelfTest.Backdrop.Pending || !panel.FrameBackdropForSelfTest.Shown); wait++) await frames(1);
            await frames(2);
        }
        async Task SaveAsync(string name)
        {
            if (capture is null) return;
            await frames(2);
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "the capture saved: " + name);
        }
        var checkedSizes = 0;
        try
        {
            _gameSettings = _gameSettings with { HdInterface = true, GeneratedInterface = true };
            SteamwovenHud.Suspended = false;
            SyncInterfaceHd();
            require(AreaGameBarPanel.SteamwovenOn, "the HD option with the generated interface turned the Steamwoven layout on");
            window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
            window.ContentScaleFactor = 1;
            foreach (var windowName in new[] { "main", "options" })
            {
                if (windowName == "options")
                {
                    var options = _mainMenu!.Shown.ToList().FindIndex(button => button.Message == 12);
                    require(options >= 0, "the main window offers Options");
                    MainMenuCommand(() => _mainMenu.Activate(options));
                    require(_mainMenu.Window == AreaMenuWindow.Options && panel.ArtMode, "Options opened in the original's look: " + _mainMenu.Window);
                }
                foreach (var size in SteamwovenSizes())
                {
                    window.ContentScaleSize = size;
                    for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                    require((Vector2I)window.GetVisibleRect().Size == size, $"the viewport is {size}, got {window.GetVisibleRect().Size}");
                    await frames(2);
                    var tag = $"{windowName}-{size.X}x{size.Y}";
                    var plan = SteamwovenFullScreen.Plan(size.X, size.Y);
                    require(plan is not null && art.FullScreenPlan == plan, $"[{tag}] the menu is laid out by the full-screen plan");
                    await WaitBackdropAsync();
                    var layout = plan!;
                    // The plate: scale f · 3.6 at the zone's top left (SWI-BDR-6).
                    require(Math.Abs(art.Scale.X - (float)layout.Factor) < 1e-4f && art.Scale.Y == art.Scale.X, $"[{tag}] the plate is drawn at {layout.Factor:F4}, got {art.Scale}");
                    require(art.Position == new Vector2(layout.Zone.X, layout.Zone.Y), $"[{tag}] the plate stands at the zone {layout.Zone}, got {art.Position}");
                    // The backdrop covers the frame, one texel per device pixel; the black bars are outside it.
                    var (backdrop, shown) = panel.FrameBackdropForSelfTest;
                    require(shown && backdrop.Position == new Vector2(layout.Frame.X, layout.Frame.Y) && backdrop.PixelSizeForSelfTest == new Vector2I(layout.Frame.Width, layout.Frame.Height),
                        $"[{tag}] the backdrop covers the frame {layout.Frame} (pixels {backdrop.PixelSizeForSelfTest})");
                    // Hit testing: the pointer over a drawn label lights it, over the frame outside the zone or a bar it lights nothing.
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
                    var outside = new Vector2(layout.Frame.X + 1, layout.Frame.Y + 1);
                    GetViewport().PushInput(new InputEventMouseMotion { Position = outside, GlobalPosition = outside }, true);
                    await frames(1);
                    require(art.HoveredForSelfTest == -1, $"[{tag}] the pointer in the frame's corner lights no label");
                    // Pixels: the frame's corner is the backdrop (not black) and a bar is black.
                    using (var image = GetViewport().GetTexture().GetImage())
                    {
                        var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
                        Color Pixel(int x, int y) => image.GetPixel(Math.Clamp((int)(x * ratio.X), 0, image.GetWidth() - 1), Math.Clamp((int)(y * ratio.Y), 0, image.GetHeight() - 1));
                        var corner = Pixel(layout.Frame.X + 1, layout.Frame.Y + 1);
                        require(corner.R8 + corner.G8 + corner.B8 > 20, $"[{tag}] the frame's corner shows the backdrop ({corner})");
                        if (layout.Bars.Left > 4) { var bar = Pixel(2, size.Y / 2); require(bar.R8 + bar.G8 + bar.B8 == 0, $"[{tag}] the left bar is black ({bar})"); }
                        if (layout.Bars.Top > 4) { var bar = Pixel(size.X / 2, 2); require(bar.R8 + bar.G8 + bar.B8 == 0, $"[{tag}] the top bar is black ({bar})"); }
                    }
                    await SaveAsync($"steamwoven-{tag}");
                    checkedSizes++;
                }
                // ARC-DEV-2672: the Original look at each size (the HD option off: the whole-factor plate, no plan, no backdrop), saved as PATH-legacy-WINDOW-WxH.png; no layout checks.
                if (capture is not null)
                {
                    _gameSettings = _gameSettings with { HdInterface = false, GeneratedInterface = false };
                    SyncInterfaceHd();
                    await frames(3);
                    foreach (var size in SteamwovenSizes())
                    {
                        window.ContentScaleSize = size;
                        for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != size; wait++) await frames(1);
                        require((Vector2I)window.GetVisibleRect().Size == size, $"[legacy-{windowName}] the viewport is {size}, got {window.GetVisibleRect().Size}");
                        await frames(3);
                        require(!InterfaceHd.Enabled && art.FullScreenPlan is null && !panel.FrameBackdropForSelfTest.Shown, $"[legacy-{windowName}-{size.X}x{size.Y}] the HD option is off: no full-screen plan, no backdrop");
                        await SaveAsync($"legacy-{windowName}-{size.X}x{size.Y}");
                    }
                    _gameSettings = _gameSettings with { HdInterface = true, GeneratedInterface = true };
                    SyncInterfaceHd();
                    await frames(3);
                    await WaitBackdropAsync();
                    require(AreaGameBarPanel.SteamwovenOn && art.FullScreenPlan is not null, $"[legacy-{windowName}] the HD option is back on after the Original-look pass");
                }
                // The comparison: the same window as before (the layout suspended), at the first size.
                if (capture is not null && SteamwovenSizes() is [var first, ..])
                {
                    window.ContentScaleSize = first;
                    for (var wait = 0; wait < 120 && (Vector2I)window.GetVisibleRect().Size != first; wait++) await frames(1);
                    SteamwovenHud.Suspended = true;
                    await frames(3);
                    await SaveAsync($"steamwoven-{windowName}-{first.X}x{first.Y}-before");
                    SteamwovenHud.Suspended = false;
                    await frames(3);
                }
            }
            // With the HD option off the whole-factor plate stands unchanged: no plan, no backdrop.
            _gameSettings = _gameSettings with { HdInterface = false };
            SyncInterfaceHd();
            await frames(3);
            require(art.FullScreenPlan is null && !panel.FrameBackdropForSelfTest.Shown, "with the HD option off the full-screen layout is off and no backdrop shows");
            GD.Print($"ARC-UI-2577 native-smoke menu steamwoven PASS sizes={checkedSizes}");
        }
        finally
        {
            window.ContentScaleMode = savedMode; window.ContentScaleAspect = savedAspect; window.ContentScaleSize = savedSize; window.ContentScaleFactor = savedFactor;
            _gameSettings = _gameSettings with { HdInterface = savedHd, GeneratedInterface = savedGenerated };
            SyncInterfaceHd();
            SteamwovenHud.Suspended = savedSuspended;
            if (_mainMenu!.Window == AreaMenuWindow.Options) MainMenuCommand(() => { _mainMenu.Back(); return AreaMenuCommand.Nothing; });
        }
    }
}
