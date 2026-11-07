using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-066: the pre-made character browser (window 13) in the original's look (docs/ui/original-windows.md; ledger MW17–MW21, MW26). The art
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
        if (_premadeArtContent.TextFont is null || _premadeArtContent.NameFont is null)
        {
            GD.Print($"ARC-UI-066 browser art not ready: {_premadeArtContent.Detail}");
            return;
        }
        var art = _premadeArtContent.Art;
        // ARC-UI-068: the new-character window's art comes with the same read.
        var frames = AreaPremadeBrowserArtContent.AllArtNumbers.Where(number => number is not (AreaPremadeBrowserArtLayout.TextFontArt or AreaPremadeBrowserArtLayout.NameFontArt)
                && art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready)
            .ToDictionary(number => number, number => art[number].Frames.Select(frame => (Texture2D)HotkeyTexture(frame)).ToArray());
        // ARC-UI-070: the fonts drawn by the shared art-text draw (MW31).
        panel.SetPremadeArt(frames, new(new AreaArtFontFace(art[AreaPremadeBrowserArtLayout.TextFontArt].Frames),
                new AreaArtFontFace(art[AreaPremadeBrowserArtLayout.NameFontArt].Frames)),
            _premadeArtContent.ArtReady, _premadeArtContent.NewCharacterArtReady, _premadeArtContent.ShopArtReady);
        GD.Print($"ARC-UI-066 browser art ready {_premadeArtContent.Detail}; browser={_premadeArtContent.ArtReady} new-character={_premadeArtContent.NewCharacterArtReady} "
            + $"shop={_premadeArtContent.ShopArtReady}; "
            + $"ms={Time.GetTicksMsec() - started}");
    }

    /// <summary>The browsed hero in the browser's art with its large portrait (null when it did not read), or null when the art or the
    /// hero is not ready.</summary>
    private AreaPremadeBrowserView? BrowsedHeroArt(int index, out Texture2D? portrait)
    {
        portrait = null;
        if (_premadeArtContent is not { ArtReady: true } content || content.View(index, MainMenuHero(index)) is not { } view) return null;
        portrait = CharacterPortrait(content, view.Portrait);
        return view;
    }

    /// <summary>ARC-UI-068: the new character in its window's art with its large portrait, or null when the art or the character is not
    /// ready (the window then keeps its diagnostic look).</summary>
    private AreaNewCharacterView? NewCharacterArt(AreaNewCharacter creation, out Texture2D? portrait)
    {
        portrait = null;
        if (_premadeArtContent is not { NewCharacterArtReady: true } content) return null;
        if (content.NewCharacterView(creation) is not { } view)
        {
            GD.Print($"ARC-UI-068 new-character art not drawn: the character or its stats did not read ({creation.Race} {creation.Gender} background {creation.Background})");
            return null;
        }
        portrait = CharacterPortrait(content, view.Character.Portrait);
        return view;
    }

    /// <summary>A large portrait, read and decoded on its first showing (null when it does not read).</summary>
    private Texture2D? CharacterPortrait(AreaPremadeBrowserArtContent content, int id)
    {
        if (_premadePortraits.TryGetValue(id, out var portrait)) return portrait;
        var file = content.Portrait(_arguments!.ModulePath, id);
        var image = new Image();
        portrait = file is { Status: AreaPortraitStatus.Ready, Bytes: { } bytes } && image.LoadBmpFromBuffer(bytes) == Error.Ok && !image.IsEmpty()
            ? ImageTexture.CreateFromImage(image) : null;
        GD.Print($"ARC-UI-066 character portrait {id}: {file.Status} {file.Detail} decoded={portrait is not null}");
        return _premadePortraits[id] = portrait;
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
        // The story's lines as the original breaks them (MW26); the field shows as many as fit, the original's draw stops there: the
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

    /// <summary>ARC-UI-068: the menu smoke's new-character part: the generic human man in the window's art (its texts from the game:
    /// the default name, "Male", "Human", the default background's name, the race's text and fifteen stats; its large portrait read); the
    /// pointer over the gender's band shows the gender's text and over the race's band the race's; a click on the race's right arrow
    /// makes a dwarf and one on its left arrow the human again; a click on the name field starts its typing (the caret drawn) and a
    /// press beside it ends it with the old text. ARC_AREA_NEWCHAR_CAPTURE saves an in-app readback of the window to the caller's
    /// ignored path.</summary>
    private async Task NewCharacterArtForSmoke(Action<bool, string> require)
    {
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        async Task Move(double x, double y)
        {
            var at = _mainMenuPanel!.NewCharacterArtForSelfTest.ScreenPointForSelfTest(x, y);
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frames(1);
        }
        async Task Click(double x, double y)
        {
            var at = _mainMenuPanel!.NewCharacterArtForSelfTest.ScreenPointForSelfTest(x, y);
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
        var art = panel.NewCharacterArtForSelfTest;
        var creation = _mainMenu!.Creation!;
        require(panel.NewCharacterArtMode && _premadeArtContent is { NewCharacterArtReady: true } && art.ViewForSelfTest is not null,
            "the new-character window shows the game's art: " + _premadeArtContent?.Detail);
        var first = art.ViewForSelfTest!;
        var character = first.Character;
        var defaultName = System.Text.Encoding.Latin1.GetString(creation.DefaultName.AsSpan());
        require(character.Name == defaultName && !first.Typing && character.Gender.Length > 0 && character.Race.Length > 0 && character.BackgroundName.Length > 0
            && character.DescriptionTitle is { Length: > 0 } && character.Description.Length > 0 && first.GenderText.Length > 0
            && character.Stats.Count == AreaPremadeBrowserArtLayout.StatOrder.Count && character.Stats[0].EndsWith(": 8", StringComparison.Ordinal),
            $"the generic character's texts come from the game: {character.Name} | {character.Gender} | {character.Race} | {character.BackgroundName} | {string.Join(", ", character.Stats)}");
        require(art.PortraitForSelfTest, "the character's large portrait read: " + character.Portrait);
        await Frames(2);
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_NEWCHAR_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
        }
        var gender = AreaNewCharacterArtLayout.GenderBand;
        await Move(gender.X + gender.Width / 2, gender.Y + gender.Height / 2);
        require(art.HoverForSelfTest == AreaNewCharacterHover.Gender && art.DescriptionForSelfTest is { Title: null } shownGender && shownGender.Text == first.GenderText,
            "the pointer over the gender's band shows the gender's text: " + art.HoverForSelfTest);
        var race = AreaNewCharacterArtLayout.RaceBand;
        await Move(race.X + race.Width / 2, race.Y + race.Height / 2);
        require(art.HoverForSelfTest == AreaNewCharacterHover.Race && art.DescriptionForSelfTest?.Text == first.RaceText, "over the race's band the race's text");
        var (_, rx, ry) = AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.RaceNext);
        await Move(rx + 10, ry + 10);
        require(art.FrameForSelfTest(AreaNewCharacterArtButton.RaceNext) == 2, "the pointer over the race's right arrow lights it: " + art.FrameForSelfTest(AreaNewCharacterArtButton.RaceNext));
        await Click(rx + 10, ry + 10);
        require(creation.Race == Arcanum.Simulation.Character.CharacterRace.Dwarf && art.ViewForSelfTest?.Character.Race != character.Race,
            $"a click on the race's right arrow made a dwarf: {creation.Race} {art.ViewForSelfTest?.Character.Race}");
        var (_, lx, ly) = AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.RacePrevious);
        await Click(lx + 10, ly + 10);
        require(creation.Race == Arcanum.Simulation.Character.CharacterRace.Human && art.ViewForSelfTest?.Character.Race == character.Race, "the left arrow the human again");
        var name = AreaNewCharacterArtLayout.NameButton;
        await Click(name.X + name.Width / 2, name.Y + name.Height / 2);
        require(creation.TypingName && art.ViewForSelfTest is { Typing: true, Character.Name: "" }, "a click on the name field started its typing");
        await Click(400, 20);
        require(!creation.TypingName && creation.NameText() == defaultName && art.ViewForSelfTest is { Typing: false } after && after.Character.Name == defaultName,
            "a press beside the field ended the typing with its old text: " + creation.NameText());
        await Move(400, 20);
        GD.Print("ARC-UI-068 native-smoke new character art PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            _premadeArtContent!.Detail, character.Name, character.Gender, character.Race, character.BackgroundName, character.DescriptionTitle,
            Stats = character.Stats, character.Portrait, first.GenderText, GenericDescription = creation.Generic.Definition?.BackgroundDescription }));
    }
}

