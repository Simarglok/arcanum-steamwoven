using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-066: the pre-made character browser (window 13) in the original's look (docs/ui/original-windows.md; ledger MW17–MW22). The art
// and tables are read once a module, each hero's large portrait on its first showing; without a game folder (the synthetic self-tests)
// or for a hero that did not read the browser keeps its diagnostic window.
public partial class TerrainViewerApp
{
    private AreaPremadeBrowserArtContent? _premadeArtContent;
    private string? _premadeArtModule;
    private readonly Dictionary<int, Texture2D?> _premadePortraits = [];

    /// <summary>Gives the menu panel the current module's browser art, read on first use; nothing when it did not read.</summary>
    private void AttachPremadeBrowserArt(AreaMainMenuPanel panel)
    {
        var module = _arguments?.ModulePath;
        if (_premadeArtContent is not null && module == _premadeArtModule) return;
        _premadeArtModule = module;
        _premadePortraits.Clear();
        var started = Time.GetTicksMsec();
        _premadeArtContent = SelfTestBootstrap || module is null ? AreaPremadeBrowserArtContent.Empty("self-test: no game folder") : AreaPremadeBrowserArtContent.Load(module);
        if (_premadeArtContent.TextFont is not { } text || _premadeArtContent.NameFont is not { } name)
        {
            GD.Print($"ARC-UI-066 browser art not ready: {_premadeArtContent.Detail}");
            return;
        }
        var art = _premadeArtContent.Art;
        IReadOnlyDictionary<int, Texture2D> Glyphs(int number) => art[number].Frames.Select((frame, index) => (frame, index))
            .Where(glyph => glyph.frame.Width > 0 && glyph.frame.Height > 0).ToDictionary(glyph => glyph.index, glyph => (Texture2D)HotkeyTexture(glyph.frame));
        var frames = AreaPremadeBrowserArtLayout.ArtNumbers.Where(number => number is not (AreaPremadeBrowserArtLayout.TextFontArt or AreaPremadeBrowserArtLayout.NameFontArt))
            .ToDictionary(number => number, number => art[number].Frames.Select(frame => (Texture2D)HotkeyTexture(frame)).ToArray());
        panel.SetPremadeArt(frames, new(text, Glyphs(AreaPremadeBrowserArtLayout.TextFontArt), name, Glyphs(AreaPremadeBrowserArtLayout.NameFontArt)));
        GD.Print($"ARC-UI-066 browser art ready {_premadeArtContent.Detail}; ms={Time.GetTicksMsec() - started}");
    }

    /// <summary>The browsed hero in the browser's art with its large portrait (null when it did not read), or null when the art or the
    /// hero is not ready.</summary>
    private AreaPremadeBrowserView? BrowsedHeroArt(int index, out Texture2D? portrait)
    {
        portrait = null;
        if (_premadeArtContent is not { ArtReady: true } content || content.View(index, MainMenuHero(index)) is not { } view) return null;
        if (!_premadePortraits.TryGetValue(view.Portrait, out portrait))
        {
            var file = content.Portrait(_arguments!.ModulePath, view.Portrait);
            var image = new Image();
            portrait = file is { Status: AreaPortraitStatus.Ready, Bytes: { } bytes } && image.LoadBmpFromBuffer(bytes) == Error.Ok && !image.IsEmpty()
                ? ImageTexture.CreateFromImage(image) : null;
            GD.Print($"ARC-UI-066 browser portrait {view.Portrait}: {file.Status} {file.Detail} decoded={portrait is not null}");
            _premadePortraits[view.Portrait] = portrait;
        }
        return view;
    }

    /// <summary>The menu smoke's browser part: the first hero in the original's look (its texts from the game: the name, the background's
    /// story and fifteen stats, its large portrait read) and every hero's story broken into lines; the pointer over Next lights it, a
    /// click on Next shows the second hero and a click on Previous the first again; a click on Back goes back to the new-game window and
    /// Pick Character opens the browser again at the first hero. ARC_AREA_PREMADE_CAPTURE saves an in-app readback of the window to the
    /// caller's ignored path.</summary>
    private async Task PremadeBrowserArtForSmoke(Action<bool, string> require)
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
        var art = panel.PremadeArtForSelfTest;
        require(panel.PremadeArtMode && _premadeArtContent is { ArtReady: true }, "the browser shows the game's art: " + _premadeArtContent?.Detail);
        var first = art.ViewForSelfTest!;
        require(first.Index == 0 && first.Name.Length > 0 && first.BackgroundName.Length > 0 && first.DescriptionTitle is null && first.Description.Length > 100
            && first.Gender.Length > 0 && first.Race.Length > 0 && first.StatsTitle.Length > 0,
            $"the first hero's texts come from the game: {first.Name} | {first.Gender} | {first.Race} | {first.BackgroundName}");
        require(first.Stats.Count == AreaPremadeBrowserArtLayout.StatOrder.Count && first.Stats[0].StartsWith("ST: ", StringComparison.Ordinal),
            "fifteen stats with their short labels: " + string.Join(", ", first.Stats));
        require(art.PortraitForSelfTest, "the hero's large portrait read: " + first.Portrait);
        // The story's lines as the original breaks them (MW22); the field shows as many as fit, the original's draw stops there: the
        // smoke reports each hero's line count (the installed stories are longer than the field).
        var content = _premadeArtContent!;
        var font = content.TextFont!;
        var height = font.Measure("A").Height;
        var fits = Enumerable.Range(0, AreaStartingHero.Premade.Count).Select(index =>
        {
            var view = content.View(index, MainMenuHero(index));
            var field = view?.DescriptionTitle is null ? AreaPremadeBrowserArtLayout.Description : AreaPremadeBrowserArtLayout.RaceText;
            var lines = view is null ? [] : AreaPremadeBrowserArtLayout.Wrap(font, view.Description, field.Width);
            // The measurement the docs state: each story is longer than its field and starts with an empty line.
            return lines.Count > field.Height / height && lines[0].Count == 0 ? lines.Count : -lines.Count;
        }).ToArray();
        require(height > 0 && fits.All(lines => lines > 0), "every hero's story is longer than its field and starts empty (lines; negative: not): "
            + string.Join(",", fits));
        await Frames(2);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_PREMADE_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
        }
        var (_, nx, ny) = AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Next);
        var next = art.ScreenPointForSelfTest(nx + 15, ny + 15);
        GetViewport().PushInput(new InputEventMouseMotion { Position = next, GlobalPosition = next });
        await Frames(1);
        require(art.FrameForSelfTest(AreaPremadeBrowserButton.Next) == 2 && art.FrameForSelfTest(AreaPremadeBrowserButton.Previous) == 0,
            "the pointer over Next lights it: " + art.FrameForSelfTest(AreaPremadeBrowserButton.Next));
        await ClickPremadeForSmoke(AreaPremadeBrowserButton.Next);
        var second = art.ViewForSelfTest;
        require(_mainMenu!.Hero == 1 && second is { Index: 1 } && second.Name != first.Name, $"a click on Next shows the second hero: {_mainMenu.Hero} {second?.Name}");
        await ClickPremadeForSmoke(AreaPremadeBrowserButton.Previous);
        require(_mainMenu!.Hero == 0 && art.ViewForSelfTest?.Name == first.Name, "a click on Previous shows the first hero again: " + _mainMenu.Hero);
        await ClickPremadeForSmoke(AreaPremadeBrowserButton.Back);
        require(_mainMenu!.Window == AreaMenuWindow.NewGame && !panel.PremadeArtMode, "a click on Back went back to the new-game window: " + _mainMenu.Window);
        var pick = _mainMenu.Shown.ToList().FindIndex(shown => shown.Message == 420);
        require(pick >= 0, "the new-game window shows Pick Character");
        MainMenuCommand(() => _mainMenu!.Activate(pick));
        require(_mainMenu!.Window == AreaMenuWindow.PickCharacter && _mainMenu.Hero == 0 && panel.PremadeArtMode, "Pick Character opened the browser again");
        var away = art.ScreenPointForSelfTest(400, 20);
        GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
        await Frames(1);
        GD.Print("ARC-UI-066 native-smoke browser art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            _premadeArtContent!.Detail, first.Name, first.Gender, first.Race, first.BackgroundName, Stats = first.Stats, first.Portrait, Second = second!.Name,
            StoryLines = fits, ShownLines = AreaPremadeBrowserArtLayout.Description.Height / height }));
    }

    /// <summary>A click (press and release) on a browser button of the art, as the pointer gives it.</summary>
    private async Task ClickPremadeForSmoke(AreaPremadeBrowserButton button)
    {
        var art = _mainMenuPanel!.PremadeArtForSelfTest;
        var (_, x, y) = AreaPremadeBrowserArtLayout.Button(button);
        var at = art.ScreenPointForSelfTest(x + 15, y + 15);
        foreach (var pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
}
