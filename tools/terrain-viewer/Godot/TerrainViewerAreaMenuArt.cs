using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-058: the menu's button windows in the original's look (docs/ui/original-windows.md; ledger MW1–MW8). The art is read once a
// module; without a game folder (the synthetic self-tests) the menu keeps its diagnostic window.
public partial class TerrainViewerApp
{
    private AreaMenuArtContent? _menuArtContent;
    private string? _menuArtModule;

    /// <summary>Gives the menu panel the current module's art, read on first use; nothing when it did not read.</summary>
    private void AttachMenuArt(AreaMainMenuPanel panel)
    {
        var module = _arguments?.ModulePath;
        if (_menuArtContent is not null && module == _menuArtModule) return;
        _menuArtModule = module;
        var started = Time.GetTicksMsec();
        _menuArtContent = SelfTestBootstrap || module is null ? AreaMenuArtContent.Empty("self-test: no game folder") : AreaMenuArtContent.Load(module);
        if (_menuArtContent.Font is null)
        {
            GD.Print($"ARC-UI-058 menu art not ready: {_menuArtContent.Detail}");
            return;
        }
        var backgrounds = new[] { AreaMenuArtLayout.MainArt, AreaMenuArtLayout.SinglePlayerArt, AreaMenuArtLayout.OptionsArt }
            .ToDictionary(number => number, number => (Texture2D)PremultipliedTexture(_menuArtContent.Art[number].Frames[0]));
        var art = _menuArtContent.Art;
        IReadOnlyList<Texture2D> Frames(int number) => [.. art[number].Frames.Select(frame => (Texture2D)PremultipliedTexture(frame))];
        // ARC-UI-070: the fonts drawn by the shared art-text draw in the original's blend (MW31).
        var font = new AreaArtFontFace(art[AreaMenuArtLayout.FontArt].Frames);
        panel.SetArt(backgrounds, font);
        // ARC-UI-061: the options controls' art (MW9, MW10).
        panel.SetControlArt(new AreaMenuControlArt(
            new[] { AreaMenuArtLayout.SliderArt, AreaMenuArtLayout.ListArt }.ToDictionary(number => number, number => Frames(number)[0]),
            Frames(AreaMenuArtLayout.BarArt)[0], Frames(AreaMenuArtLayout.LessArt), Frames(AreaMenuArtLayout.MoreArt),
            new AreaArtFontFace(art[AreaMenuArtLayout.LabelFontArt].Frames), new AreaArtFontFace(art[AreaMenuArtLayout.ValueFontArt].Frames)));
        GD.Print($"ARC-UI-058 menu art ready {_menuArtContent.Detail}; glyphs={font.Frames.Count}; ms={Time.GetTicksMsec() - started}");
    }

    /// <summary>ARC-UI-070: the main window's labels as drawn match the original's text blend (MW31): the background art with each
    /// label's three passes rendered by <see cref="AreaArtTextRules.Render"/> is compared, pixel by pixel of the 800 × 600 screen round
    /// the labels, with an in-app readback (within two levels a channel), and the edges must hold partly covered pixels.</summary>
    private void MenuTextBlendForSmoke(Action<bool, string> require, AreaMenuArtView art)
    {
        var face = art.FontForSelfTest ?? throw new InvalidOperationException("ARC-UI-070 smoke: no menu font");
        var background = _menuArtContent!.Art[AreaMenuArtLayout.MainArt].Frames[0];
        var (width, height) = (AreaMenuArtLayout.ScreenWidth, AreaMenuArtLayout.ScreenHeight);
        require(AreaArtTextDraw.Prepared(art) && background.Width == width && background.Height == height,
            "the menu's canvas draws art text in premultiplied alpha over an 800 × 600 background");
        var screen = (byte[])background.Rgba.Clone();
        var (labels, places) = (art.LabelsForSelfTest, art.ButtonsForSelfTest);
        for (var i = 0; i < labels.Count; i++)
        {
            var passes = AreaMenuArtLayout.Passes;
            for (var pass = 0; pass < passes.Count; pass++)
            {
                var (dx, dy, colour, blend) = passes[pass];
                if (pass == passes.Count - 1 && i == art.HoveredForSelfTest) colour = AreaMenuArtLayout.HoverFace;
                AreaArtTextRules.Render(screen, width, face.Font, face.Frames, labels[i], places[i].X + dx, places[i].Y + dy, colour, blend);
            }
        }
        using var image = GetViewport().GetTexture().GetImage();
        var ratio = new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
        var transform = art.GetGlobalTransformWithCanvas();
        var (compared, worst, edges) = (0, 0, 0);
        foreach (var (x, y, w, h) in places)
            for (var sy = Math.Max(0, y - 2); sy < Math.Min(height, y + h + 2); sy++)
                for (var sx = Math.Max(0, x - 2); sx < Math.Min(width, x + w + 2); sx++)
                {
                    var at = transform * new Vector2(sx + 0.5f, sy + 0.5f) * ratio;
                    if (at.X < 0 || at.Y < 0 || at.X >= image.GetWidth() || at.Y >= image.GetHeight()) continue;
                    var drawn = image.GetPixel((int)at.X, (int)at.Y);
                    var s = (sy * width + sx) * 4;
                    var difference = new[] { drawn.R8 - screen[s], drawn.G8 - screen[s + 1], drawn.B8 - screen[s + 2] }.Max(Math.Abs);
                    (compared, worst) = (compared + 1, Math.Max(worst, difference));
                }
        foreach (var frame in face.Frames)
            for (var p = 0; p < frame.Rgba.Length; p += 4)
                if (frame.Rgba[p + 3] != 0 && frame.Rgba[p] is > 0 and < 255) edges++;
        require(compared > 0 && worst <= 2, $"ARC-UI-070: the labels match the original's blend: pixels={compared} worst={worst}");
        require(edges > 0, "ARC-UI-070: the font has partly covering edge pixels: " + edges);
        GD.Print($"ARC-UI-070 native-smoke menu text blend PASS pixels={compared} worst={worst} edgePixels={edges}");
    }

    /// <summary>The menu smoke's art part: the main window in the original's look with its labels at the exe's places (MW2); the pointer
    /// over Single Player lights it, a click opens the single-player window (its own background, MW1), and Back returns to the main
    /// window. ARC_AREA_MENU_CAPTURE saves an in-app readback of the main window to the caller's ignored path.</summary>
    private async Task MainMenuArtForSmoke(Action<bool, string> require)
    {
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        var panel = _mainMenuPanel!;
        var art = panel.ArtForSelfTest;
        require(panel.ArtMode && _menuArtContent is { ArtReady: true }, "the main window shows the game's menu art: " + _menuArtContent?.Detail);
        var labels = art.LabelsForSelfTest;
        var places = art.ButtonsForSelfTest;
        require(labels.SequenceEqual(_mainMenu!.Shown.Select(button => _mainMenu.Texts.Text(button.Message))) && places.Count == _mainMenu.Shown.Count
            && places.Select((place, slot) => place.Y == AreaMenuArtLayout.FirstButtonY + AreaMenuArtLayout.ButtonStep * slot
                && place.X + place.Width / 2 == AreaMenuArtLayout.ButtonX && place.Width > 0 && place.Height == 33).All(ok => ok),
            "the labels stand centred at (410, 143 + 50 i): " + string.Join(" ", places));
        await Frames(2);
        MenuTextBlendForSmoke(require, art);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
            // ARC-RENDER-1171 (graphics report): the same window as before the Credits button was built (hidden with its reason), saved beside it as NAME-before.png.
            var shownRefusal = _mainMenu.CreditsRefusal;
            _mainMenu.CreditsRefusal = AreaMainMenu.NoCredits;
            RenderMainMenu();
            await Frames(2);
            using var before = GetViewport().GetTexture().GetImage();
            require(before.SavePng(System.IO.Path.ChangeExtension(capture, null) + "-before.png") == Error.Ok, "the before capture saved");
            _mainMenu.CreditsRefusal = shownRefusal;
            RenderMainMenu();
            await Frames(2);
        }
        var single = panel.ButtonCentreForSelfTest(0) ?? throw new InvalidOperationException("ARC-UI-058 menu art smoke: no label");
        // The focused first label is lit at the opening; the pointer off every label darkens it, over Single Player lights it again.
        require(art.HoveredForSelfTest == _mainMenu.Focus, "the focused label is lit at the opening: " + art.HoveredForSelfTest);
        var away = art.GetGlobalTransform() * new Vector2(8, 8);
        GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
        await Frames(1);
        require(art.HoveredForSelfTest == -1, "the pointer off the labels darkens them: " + art.HoveredForSelfTest);
        GetViewport().PushInput(new InputEventMouseMotion { Position = single, GlobalPosition = single });
        await Frames(1);
        require(art.HoveredForSelfTest == 0, "the pointer over Single Player lights it: " + art.HoveredForSelfTest);
        foreach (var pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = single, GlobalPosition = single, ButtonMask = pressed ? MouseButtonMask.Left : 0,
            });
            await Frames(1);
        }
        require(_mainMenu!.Window == AreaMenuWindow.SinglePlayer && panel.ArtMode && art.LabelsForSelfTest.Count == _mainMenu.Shown.Count,
            "the click opened Single Player in the original's look: " + _mainMenu.Window);
        MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; });
        require(_mainMenu!.Window == AreaMenuWindow.Main && panel.ArtMode, "Back returned to the main window");
        GD.Print("ARC-UI-058 native-smoke menu art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            _menuArtContent!.Detail, Labels = labels, Places = places.Select(place => $"{place.X},{place.Y} {place.Width}x{place.Height}").ToArray() }));
    }
}
