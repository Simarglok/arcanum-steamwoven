using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-066: the pre-made character browser (window 13) in the original's look (docs/ui/original-windows.md; ledger MW17–MW21). The art
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
    /// story and fifteen stats, its large portrait read); the pointer over Next lights it, a click on Next shows the second hero and a
    /// click on Previous the first again. ARC_AREA_PREMADE_CAPTURE saves an in-app readback of the window to the caller's ignored path.</summary>
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
        async Task Click(Vector2 at)
        {
            foreach (var pressed in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
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
        await Click(next);
        var second = art.ViewForSelfTest;
        require(_mainMenu!.Hero == 1 && second is { Index: 1 } && second.Name != first.Name, $"a click on Next shows the second hero: {_mainMenu.Hero} {second?.Name}");
        var (_, px, py) = AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Previous);
        await Click(art.ScreenPointForSelfTest(px + 15, py + 15));
        require(_mainMenu!.Hero == 0 && art.ViewForSelfTest?.Name == first.Name, "a click on Previous shows the first hero again: " + _mainMenu.Hero);
        var away = art.ScreenPointForSelfTest(400, 20);
        GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
        await Frames(1);
        GD.Print("ARC-UI-066 native-smoke browser art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            _premadeArtContent!.Detail, first.Name, first.Gender, first.Race, first.BackgroundName, Stats = first.Stats, first.Portrait, Second = second!.Name }));
    }
}
