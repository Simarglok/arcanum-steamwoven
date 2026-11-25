using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-034 (N10b slice 3): the character editor in creation mode, the original's window 14 that the new-character window's Accept
// opens (ledger CM1–CM8). It is the character sheet in its creation mode (AreaCharacterSheetPanel with Accept and Cancel) above the
// main menu's backdrop, over a draft session of the new character (AreaCreationEditor): the stats, skills, spells and tech pages' + and −
// spend the five starting points and take them back down to the sheet the editor opened on. Accept (or Return) opens the opening shop
// (ARC-UI-036, window 15), whose Accept starts the game with the presses replayed on the real session and whose Back shows this
// editor again on the same draft; Cancel (or Escape) goes back to
// the new-character window, which starts a new character as the original's does. The menu owns the keys; the pages' buttons take
// the pointer.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetPanel? _editorPanel;
    private int _editorCollege;

    private bool CharacterEditorShown => _editorPanel?.IsOpen == true;

    /// <summary>ARC-UI-147: the editor can be drawn in the original's look: the strips and buttons it shares with the opening shop and the
    /// character sheet's art read (without a game folder, the self-tests', both stay off and the editor keeps its diagnostic panel).</summary>
    private bool CharacterEditorArtReady => _mainMenu is { Window: AreaMenuWindow.CharacterEditor, QuitPending: false, Editor: { Disposed: false } } && _mainMenuPanel is { ShopArtSet: true }
        && SheetArtLookOfModule() is not null;

    /// <summary>A draft session of <paramref name="creation"/>: made as the start makes the new character's session (the scene's
    /// new-game recipe, the window's name and background); null with the reason when it cannot be made.</summary>
    private GameSessionOwner? MakeCreationDraft(AreaNewCharacter creation, out string? refused)
    {
        refused = null;
        if (creation.Hero(out var why) is not { Status: AreaHeroStatus.Ready } hero) { refused = "the new character cannot be made (" + why + ")"; return null; }
        if (_newGameRecipe is not { } recipe) { refused = "this scene has no new-game start to make the character's session from"; return null; }
        var created = AreaTransitionCoordinator.CreateSession(recipe.Clock, Guid.NewGuid(), recipe.Module, recipe.Spawn, recipe.RuntimeMap, hero, recipe.Reputations);
        if (created.Owner is not { } draft) { refused = $"the session was not created ({created.Failure})"; return null; }
        if (creation.ApplyChoices(draft, hero.Effects ?? (_ => null)) is { } choices) { draft.Dispose(); refused = choices; return null; }
        return draft;
    }

    /// <summary>Shows the editor while the menu is at window 14 (rebuilt from the draft after every change), hides it otherwise.</summary>
    private void RenderCharacterEditor()
    {
        if (_mainMenu is not { Window: AreaMenuWindow.CharacterEditor, QuitPending: false, Editor: { Disposed: false } editor } menu) { CloseCharacterEditor(); return; }
        if (editor.Sheet() is not { } view) { CloseCharacterEditor(); return; }
        if (_editorPanel is null)
        {
            _editorPanel = new AreaCharacterSheetPanel(creation: true); AddChild(_editorPanel);
            // ARC-UI-147: only with the strips and buttons it shares with the shop (an editor whose strips did not read keeps the diagnostic panel).
            if (CharacterEditorArtReady) AttachSheetArt(_editorPanel);
            SyncShopKit(); // ARC-UI-3041: the editor's sheet follows the screen's kit body from its first draw on
            _editorPanel.SoundRequested += id => PlayBarSound(id, "editor button");
            // ARC-UI-3120: the sheet's hover texts and refusals are posts to the menu's bottom message window, as the in-game sheet's are posts to the game's (CHW8, ML2).
            _editorPanel.HoverTextRequested += text => MenuHover(text is null ? null : AreaMessageEntry.Text(AreaMessageKind.Hover, text));
            _editorPanel.HoverEntryRequested += MenuHover;
            _editorPanel.MessageRequested += text => MenuPost(AreaMessageEntry.Text(AreaMessageKind.Message, text), "editor message");
            _editorPanel.AcceptRequested += () => MainMenuCommand(() => _mainMenu!.Accept());
            _editorPanel.CloseRequested += () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; });
            _editorPanel.StatSpendRequested += row => PressEditor(e => EditorPress(e.PressStat(row, spend: true), AreaSheetPage.Stat, true, row >= 2 ? AreaStatsPage.StatOrder[row - 2] : null), $"stat spend row={row}");
            _editorPanel.StatRefundRequested += row => PressEditor(e => EditorPress(e.PressStat(row, spend: false), AreaSheetPage.Stat, false, row >= 2 ? AreaStatsPage.StatOrder[row - 2] : null), $"stat refund row={row}");
            _editorPanel.SkillSpendRequested += row => PressEditor(e => EditorPress(e.PressSkill(row, spend: true), AreaSheetPage.Skill, true), $"skill spend row={row}");
            _editorPanel.SkillRefundRequested += row => PressEditor(e => EditorPress(e.PressSkill(row, spend: false), AreaSheetPage.Skill, false), $"skill refund row={row}");
            _editorPanel.TechSpendRequested += discipline => PressEditor(e => EditorPress(AreaTechPage.Press(e.PressTech(discipline, spend: true), true), AreaSheetPage.Tech, true), $"tech spend discipline={discipline}");
            _editorPanel.TechRefundRequested += discipline => PressEditor(e => EditorPress(AreaTechPage.Press(e.PressTech(discipline, spend: false), false), AreaSheetPage.Tech, false), $"tech refund discipline={discipline}");
            _editorPanel.SpellLearnRequested += college => PressEditor(e => EditorPress(e.PressSpell(college, learn: true, _spellData?.Data), AreaSheetPage.Spell, true), $"spell learn college={college}");
            _editorPanel.SpellUnlearnRequested += college => PressEditor(e => EditorPress(e.PressSpell(college, learn: false, _spellData?.Data), AreaSheetPage.Spell, false), $"spell unlearn college={college}");
            _editorPanel.SchemeChosen += scheme => PressEditor(e => EditorSchemePress(e.PressScheme(scheme), scheme), $"scheme choose scheme={scheme}");
            _editorPanel.CollegeSelected += college =>
            {
                if (college is < 0 or >= AreaSpellsPage.Colleges) return;
                _editorCollege = college; if (_mainMenu is not null) _mainMenu.Notice = null; RenderMainMenu();
            };
        }
        AdoptSkillTexts(); AdoptSpellData();
        // ARC-UI-147: the draft hero's portrait in the sheet's slot (the stored portrait's small image, as the in-game sheet's).
        _editorPanel.SetPortrait(_editorPanel.ArtMode && editor.Draft.QueryHero() is { Portrait: > 0 } hero ? EditorPortrait(hero.Portrait) : null);
        var skills = editor.Skills(_skillTexts?.Texts, AreaHoverIndex.TextProfile);
        var spells = editor.Spells(_spellData?.Data, _editorCollege);
        // ARC-UI-1792: the scheme page (the original look only: it needs the name files and the page art).
        var schemes = _sheetArtContent is { ArtReady: true } content ? editor.Schemes(content.Texts) : null;
        if (!_editorPanel.IsOpen) _editorPanel.Open(view, skills, spells, editor.Stats(), AreaTechPage.Build(editor.Draft), schemes);
        else _editorPanel.Render(view, skills, spells, editor.Stats(), AreaTechPage.Build(editor.Draft), schemes);
        // ARC-UI-3120: in the original's look the note stays at the menu's top; the message window is only for the sheet's own refusals.
        if (menu.Notice is { } notice) _editorPanel.ShowSkillMessage(notice, _editorPanel.ArtMode ? string.Empty : null);
        // The controller's rows (Accept, Cancel) as the menu's other windows highlight theirs.
        _editorPanel.ShowCreationFocus(menu.Focus);
    }

    /// <summary>ARC-UI-3120 (ledger CHW8, ML2, MW32): posts an entry to the message window of the menu's bottom strip (the editor's and the opening shop's):
    /// a refusal is a stored type-4 message with its sound, a hover text is shown only more than 3 s after the last stored entry. Nothing without the window.</summary>
    private void MenuPost(AreaMessageEntry entry, string what)
    {
        if (_mainMenuPanel is not { MessageWindowShownForSelfTest: true } panel) return;
        var post = panel.MessageLog.Post(entry, MessageLogNow);
        if (post.Sound is { } sound) PlayInterfaceSound(sound, what);
        panel.RenderMessageWindow();
    }

    /// <summary>ARC-UI-3120: a hover text or entry written to the menu's message window; null: the pointer left it (Steamwoven shows the scroll position's entry again, ML7).</summary>
    private void MenuHover(AreaMessageEntry? entry)
    {
        if (entry is not null) { MenuPost(entry, "menu hover"); return; }
        if (_mainMenuPanel is not { MessageWindowShownForSelfTest: true } panel) return;
        panel.MessageLog.ClearHover();
        panel.RenderMessageWindow();
    }

    /// <summary>ARC-UI-3120 (ledger MW32): the pointer entering the Accept or Back art button of the editor and the opening shop posts its hint: <c>MainMenu.mes</c> 6999 as the title,
    /// a new line and the hint (7003 Accept, 7002 Back), type 6; leaving it blanks the window. With no title text (6999 missing) nothing is posted.</summary>
    private void MenuButtonHover(AreaShopArtButton? button)
    {
        if (button is not { } hovered || _mainMenu is not { } menu) { MenuHover(null); return; }
        if (menu.Texts.Original(AreaMainMenuTexts.ButtonHintTitle) is not { } title || menu.Texts.Original(AreaShopArtLayout.Hint(hovered)) is not { } hint) return;
        MenuPost(AreaMessageEntry.Text(AreaMessageKind.Hover, title + "\n" + hint), "menu button hint");
    }

    private void CloseCharacterEditor()
    {
        if (_editorPanel?.IsOpen != true) return;
        _editorPanel.CloseView();
    }

    /// <summary>The project's words of a press and the text the art look shows for it: the original's <c>charedit.mes</c> refusal (ARC-UI-078, as
    /// the in-game sheet's) or nothing for a success or a silent refusal; the diagnostic panel keeps the project's words.</summary>
    private (string Project, string? Art) EditorPress(string message, Arcanum.Session.SessionPointResult result, AreaSheetPage page, bool spend,
        Arcanum.Simulation.Character.CharacterStat? pressed = null) =>
        (message, _editorPanel is { ArtMode: true } && _sheetArtContent is { ArtReady: true } content
            ? AreaSheetRefusals.Message(content.Texts, page, spend, result, message, pressed) ?? string.Empty : null);

    private (string, string?) EditorPress(AreaStatPress p, AreaSheetPage page, bool spend, Arcanum.Simulation.Character.CharacterStat? pressed) => EditorPress(p.Message, p.Result, page, spend, pressed);
    private (string, string?) EditorPress(AreaSkillPress p, AreaSheetPage page, bool spend) => EditorPress(p.Message, p.Result, page, spend);
    private (string, string?) EditorPress(AreaSpellPress p, AreaSheetPage page, bool spend) => EditorPress(p.Message, p.Result, page, spend);
    private (string, string?) EditorPress(AreaTechPress p, AreaSheetPage page, bool spend) => EditorPress(p.Message, p.Result, page, spend);

    /// <summary>ARC-UI-1792: a scheme press: no cost and no text (ALP5), so no refusal line; a refused one (the draft's own limit) is the project's word.</summary>
    private static (string, string?) EditorSchemePress(SessionSchemeResult result, int scheme) =>
        ($"scheme {scheme}: {(result.Disposition == GameSessionDisposition.Rejected ? "refused (" + result.Failure + ")" : result.Disposition.ToString())}", null);

    /// <summary>The draft hero's portrait for the sheet's slot (the stored portrait's small image), or null when it did not read.</summary>
    private Texture2D? EditorPortrait(int portrait)
    {
        LoadPortraits([portrait]);
        return PortraitTexture(portrait);
    }

    /// <summary>One + or − press of the editor: the draft changes (and the press is recorded) or the refusal is the message. In the art look the
    /// refusal's text is the original's type-4 message in the bottom message window (ARC-UI-3120, ledger CHW8, ML4), as the in-game sheet's.</summary>
    private void PressEditor(Func<AreaCreationEditor, (string Project, string? Art)> press, string what)
    {
        if (_mainMenu is not { Window: AreaMenuWindow.CharacterEditor, QuitPending: false, Editor: { Disposed: false } editor }) return;
        var (message, art) = press(editor);
        _mainMenu.Notice = null;
        GD.Print($"ARC-UI-034 editor {what}: {message} presses={editor.Creation.Presses.Count} unspent={editor.Draft.QueryHeroExperience()?.UnspentPoints}");
        // The art look re-renders the menu (its note line goes); the refusal is posted after the redraw, so the redraw's hover text does not take its place; the project's words stay on the panel (the self-tests').
        if (_editorPanel!.ArtMode) RenderMainMenu(); else RenderCharacterEditor();
        _editorPanel!.ShowSkillMessage(message, art);
    }

    /// <summary>ARC-UI-147: the menu smoke's editor part, on the game's data: the editor shows the character sheet's original look (art 22 and the
    /// strips with the shop's Accept and Back buttons, the draft's portrait and numbers) at the menu screen's (0, 41); a + and a − pressed by the
    /// pointer on the art act on the draft (the − at the start is refused with the original's text in the menu's note line), a release in the close
    /// rectangle does not close it (CM3), and a tab shows the spells page. ARC_AREA_EDITOR_CAPTURE=PATH.png saves readbacks of the skills page
    /// (PATH-skills.png), the spells page (PATH-spells.png) and the schemes page (PATH-schemes.png). The + of willpower (row 7) is the pointer's, as the stage's next lines expect.</summary>
    private async Task CharacterEditorArtForSmoke(Action<bool, string> require)
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
        var editor = _editorPanel!;
        require(editor.ArtMode && panel.ShopArtMode && !panel.PanelVisibleForSelfTest && !editor.PanelVisibleForSelfTest && _sheetArtContent is { ArtReady: true },
            "the editor shows the sheet's original look with the shop's strips and buttons: " + _sheetArtContent?.Detail);
        var art = editor.ArtViewForSelfTest!;
        await Frames(2);
        require(art.ViewForSelfTest is { UnspentPoints: 5 } && art.PortraitForSelfTest, $"the draft's sheet and portrait: {art.ViewForSelfTest?.UnspentPoints} portrait={art.PortraitForSelfTest}");
        var (wx, wy, _, _) = AreaShopArtLayout.Window;
        require(art.PointForSelfTest(0, 0).DistanceTo(panel.ShopArtForSelfTest.ScreenPointForSelfTest(wx, wy)) < 1, "the sheet stands at the menu screen's (0, 41)");
        if (ColumnMenuSmokeOn())
        {
            // ARC-UI-2924: in the column layout the editor stands in the fitted plate like the shop it shares its strips with: the screen's place, the black beside it, the strips' Accept and Back and the sheet's + − and tab hit where the plan draws them.
            var (plusX, plusY) = AreaCharacterSheetArtLayout.Plus(7);
            var (minusX, minusY) = AreaCharacterSheetArtLayout.Minus(3);
            var tabSpells = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells);
            (string Name, int X, int Y)[] spots =
            [
                .. ShopStripSpots(),
                ($"Plus:7", plusX, plusY + wy), ($"Minus:3", minusX, minusY + wy), ($"Tab:{(int)AreaSheetTab.Spells}", tabSpells.X, tabSpells.Y + wy),
            ];
            await TwoStripScreenColumnForSmoke("editor", () => (art.Position - new Vector2(0, wy * art.Scale.X), art.Scale.X), () => art.PointForSelfTest(0, 0),
                () => panel.ShopArtForSelfTest.HoveredForSelfTest is { } shopButton ? shopButton.ToString() : art.HoveredForSelfTest is { } hit ? $"{hit.Kind}:{hit.Index}" : null, spots, require, Frames, KitEditorView);
        }
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } capture)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(System.IO.Path.ChangeExtension(capture, null) + "-skills.png") == Error.Ok, "the skills page capture saved to " + capture);
        }
        var presses = _mainMenu!.Creation!.Presses.Count;
        // A − at the start values is refused with the original's text in the note line; the draft and the presses stay.
        var (mx, my) = AreaCharacterSheetArtLayout.Minus(3);
        await Click(art.PointForSelfTest(mx + 5, my + 5));
        // ARC-UI-3120: the refusal is the original's type-4 message in the bottom message window (icon 442, ML4, ML5), not the note line at the top.
        var (messageLog, messageView) = (panel.MessageLog, panel.MessageViewForSelfTest);
        require(_mainMenu.Creation.Presses.Count == presses && _mainMenu.Notice is null && panel.ArtNoteForSelfTest.Length == 0 && panel.MessageWindowShownForSelfTest
            && messageLog.Shown is { Kind: AreaMessageKind.Message, Title: { Length: > 0 } refusal } && messageView.ShownKindForSelfTest == AreaMessageKind.Message
            && messageView.IconForSelfTest == AreaMessageLayout.Icon(AreaMessageKind.Message) && messageView.ShownTextForSelfTest.Length > 0 && refusal.StartsWith(messageView.ShownTextForSelfTest[..Math.Min(8, messageView.ShownTextForSelfTest.Length)], StringComparison.Ordinal),
            $"a − at the start is refused with the original's text in the bottom message window: '{messageLog.Shown?.Title}' shown='{messageView.ShownTextForSelfTest}' icon={messageView.IconForSelfTest} note='{panel.ArtNoteForSelfTest}'");
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } noteCapture)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var noteImage = GetViewport().GetTexture().GetImage();
            require(noteImage.SavePng(System.IO.Path.ChangeExtension(noteCapture, null) + "-note.png") == Error.Ok, "the note capture saved to " + noteCapture);
        }
        // ARC-UI-3120: a hover is dropped for 3 s after the refusal (0x552770); then the pointer on the alignment gauge (region 102) writes its charedit.mes text (type 6, icon 444), on Accept the
        // button's hint (MainMenu.mes 6999, a new line, 7003), and leaving the buttons or the gauge shows the scroll position's entry again (ML7); the type-12 stat text carries no cost.
        var hoverAt = art.PointForSelfTest(40, 160);
        GetViewport().PushInput(new InputEventMouseMotion { Position = hoverAt, GlobalPosition = hoverAt });
        await Frames(2);
        require(messageLog.Shown is { Kind: AreaMessageKind.Message }, $"a hover within 3 s of a refusal is dropped: {messageLog.Shown?.Kind}");
        _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
        var hoverMove = art.PointForSelfTest(41, 161);
        GetViewport().PushInput(new InputEventMouseMotion { Position = hoverMove, GlobalPosition = hoverMove });
        await Frames(2);
        var alignmentText = art.Texts.Hint(102);
        require(alignmentText is { Length: > 0 } && messageLog.Shown is { Kind: AreaMessageKind.Hover } hovered && hovered.Title == alignmentText && messageView.IconForSelfTest == AreaMessageLayout.Icon(AreaMessageKind.Hover)
            && messageView.ShownTextForSelfTest.Length > 0, $"the alignment gauge's hover is written to the bottom message window: '{messageLog.Shown?.Title}' / '{messageView.ShownTextForSelfTest}'");
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } hoverCapture)
        {
            using var hoverImage = GetViewport().GetTexture().GetImage();
            require(hoverImage.SavePng(System.IO.Path.ChangeExtension(hoverCapture, null) + "-hover.png") == Error.Ok, "the hover capture saved");
        }
        var (_, acceptX, acceptY) = AreaShopArtLayout.Button(AreaShopArtButton.Accept);
        var acceptAt = panel.ShopArtForSelfTest.ScreenPointForSelfTest(acceptX + 15, acceptY + 15);
        GetViewport().PushInput(new InputEventMouseMotion { Position = acceptAt, GlobalPosition = acceptAt });
        await Frames(2);
        var (hintTitle, hintBody) = (_mainMenu.Texts.Original(AreaMainMenuTexts.ButtonHintTitle), _mainMenu.Texts.Original(AreaShopArtLayout.AcceptHint));
        require(hintTitle is not null && hintBody is not null ? messageLog.Shown is { Kind: AreaMessageKind.Hover } hint && hint.Title == hintTitle + "\n" + hintBody : messageLog.Shown is { Kind: AreaMessageKind.Message },
            $"the pointer on Accept writes its hint: '{messageLog.Shown?.Title}' (expected '{hintTitle}' + '{hintBody}')"); 
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } hintCapture)
        {
            using var hintImage = GetViewport().GetTexture().GetImage();
            require(hintImage.SavePng(System.IO.Path.ChangeExtension(hintCapture, null) + "-hint.png") == Error.Ok, "the hint capture saved");
        }
        var leaveAt = art.PointForSelfTest(400, 5);
        GetViewport().PushInput(new InputEventMouseMotion { Position = leaveAt, GlobalPosition = leaveAt });
        await Frames(2);
        require(messageLog.Shown is { Kind: AreaMessageKind.Message }, $"leaving the button shows the stored entry again: {messageLog.Shown?.Kind}");
        // A release in the close rectangle does not close the editor (the close rectangle is the interface modes 1 and 2's).
        var close = AreaCharacterSheetArtView.CloseRectangle;
        await Click(art.PointForSelfTest(close.X + 20, close.Y + 20));
        require(CharacterEditorShown && _mainMenu.Window == AreaMenuWindow.CharacterEditor, "a release in the close rectangle did not close the editor");
        // The spells tab shows the spells page in the original's look.
        var spellsTab = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells);
        await Click(art.PointForSelfTest(spellsTab.X + 5, spellsTab.Y + 5));
        await Frames(2);
        require(art.Tab == AreaSheetTab.Spells && (!editor.SpellsReadyForSelfTest || art.Spells.Visible), $"the spells tab: {art.Tab} spells art={editor.SpellsReadyForSelfTest}");
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } capture2)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(System.IO.Path.ChangeExtension(capture2, null) + "-spells.png") == Error.Ok, "the spells page capture saved");
        }
        var skillsTab = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Skills);
        await Click(art.PointForSelfTest(skillsTab.X + 5, skillsTab.Y + 5));
        // The + of willpower by the pointer: the draft changes, the press is recorded and the unspent points fall.
        var (px, py) = AreaCharacterSheetArtLayout.Plus(7);
        await Click(art.PointForSelfTest(px + 5, py + 5));
        require(_mainMenu.Creation.Presses.Count == presses + 1 && editor.StatsForSelfTest is { UnspentPoints: 4 } && editor.StatsForSelfTest.Rows[7].Value == 9,
            $"a + on willpower by the pointer: presses={_mainMenu.Creation.Presses.Count} {editor.StatsForSelfTest?.UnspentPoints}");
        // ARC-UI-1792: the schemes tab shows the scheme page in the editor too; a release on a row stores the scheme in the draft and in the character
        // (the start replays it), and the smoke puts the first scheme back for the stages after it.
        var schemesTab = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Schemes);
        await Click(art.PointForSelfTest(schemesTab.X + 5, schemesTab.Y + 5));
        await Frames(2);
        var schemes = art.Schemes;
        require(art.Tab == AreaSheetTab.Schemes, $"the schemes tab: {art.Tab}");
        if (editor.SchemesReadyForSelfTest)
        {
        require(schemes.Visible && schemes.ViewForSelfTest is { Entries.Count: > 0 },
            $"the schemes tab shows the scheme page in the editor: visible={schemes.Visible}");
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_EDITOR_CAPTURE") is { Length: > 0 } capture3)
        {
            using var image = GetViewport().GetTexture().GetImage();
            require(image.SavePng(System.IO.Path.ChangeExtension(capture3, null) + "-schemes.png") == Error.Ok, "the schemes page capture saved");
        }
        var startScheme = _mainMenu.Editor!.Draft.QueryHero()!.Advancement.AutoLevelScheme;
        var pressedRow = schemes.ViewForSelfTest!.Entries[0].Number == startScheme ? 1 : 0;
        var chosen = AreaCharacterSheetSchemesLayout.EntryAt(schemes.ViewForSelfTest, schemes.ScrollForSelfTest, pressedRow)!;
        var (sx, sy, _, _) = AreaCharacterSheetSchemesLayout.Row(pressedRow);
        var beforeSchemePresses = _mainMenu.Creation.Presses.Count;
        await Click(schemes.PointForSelfTest(sx + 5, sy + 5));
        require(_mainMenu.Creation.Scheme == chosen.Number && _mainMenu.Editor.Draft.QueryHero()!.Advancement.AutoLevelScheme == chosen.Number
            && _mainMenu.Creation.Presses.Count == beforeSchemePresses, $"a release on a row stores its scheme in the draft and the character: {_mainMenu.Creation.Scheme} vs {chosen.Number}");
        require(schemes.ViewForSelfTest?.Scheme == chosen.Number && schemes.ViewForSelfTest.SchemeName == chosen.Name, "the page names the chosen scheme at the top");
        _mainMenu.Editor.PressScheme(startScheme);
        }
        RenderMainMenu();
        var backTab = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Skills);
        await Click(art.PointForSelfTest(backTab.X + 5, backTab.Y + 5));
        GD.Print("ARC-UI-147 native-smoke character editor art PASS " + System.Text.Json.JsonSerializer.Serialize(new { _sheetArtContent!.Detail, Presses = _mainMenu.Creation.Presses.Count }));
    }
}
