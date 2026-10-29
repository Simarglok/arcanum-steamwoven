using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-058: the menu's button windows in the original's look (docs/ui/original-windows.md; ledger MW1–MW6). The art is read once a
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
        if (_menuArtContent.Font is not { } font)
        {
            GD.Print($"ARC-UI-058 menu art not ready: {_menuArtContent.Detail}");
            return;
        }
        var backgrounds = new[] { AreaMenuArtLayout.MainArt, AreaMenuArtLayout.SinglePlayerArt, AreaMenuArtLayout.OptionsArt }
            .ToDictionary(number => number, number => (Texture2D)HotkeyTexture(_menuArtContent.Art[number].Frames[0]));
        var glyphs = _menuArtContent.Art[AreaMenuArtLayout.FontArt].Frames.Select((frame, index) => (frame, index))
            .Where(glyph => glyph.frame.Width > 0 && glyph.frame.Height > 0).ToDictionary(glyph => glyph.index, glyph => (Texture2D)HotkeyTexture(glyph.frame));
        panel.SetArt(backgrounds, font, glyphs);
        GD.Print($"ARC-UI-058 menu art ready {_menuArtContent.Detail}; glyphs={glyphs.Count}; ms={Time.GetTicksMsec() - started}");
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
        require(labels.SequenceEqual(_mainMenu!.Shown.Select(button => _mainMenu.Texts.Text(button.Message))) && places.Count == 3
            && places.Select((place, slot) => place.Y == AreaMenuArtLayout.FirstButtonY + AreaMenuArtLayout.ButtonStep * slot
                && place.X + place.Width / 2 == AreaMenuArtLayout.ButtonX && place.Width > 0 && place.Height == 33).All(ok => ok),
            "the labels stand centred at (410, 143 + 50 i): " + string.Join(" ", places));
        await Frames(2);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
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
