using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-075 native observation with the original hero: the production C key opens the sheet in the original's look (art 22, the numbers in the
// original's fonts, the + and − art buttons), the pointer over a button writes its text to the message window, a press on + with no points
// to spend is refused in the message window, the spells tab shows the spells page in the original's look (ARC-UI-077) and a release in the close rectangle closes the sheet.
// ARC_AREA_SHEET_CAPTURE=PATH.png saves readbacks of the skills, tech and spells pages (PATH-skills.png, PATH-tech.png, PATH-spells.png). Prints counts only.
public partial class TerrainViewerApp
{
    private async Task RunAreaSheetArtSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-075 sheet art smoke: " + why); }
        var session = _areaSession!;
        var savedInterfaceHd = _gameSettings.HdInterface;
        var verifyHd = System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_HD_VERIFY") == "1";
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            if (verifyHd)
            {
                Require(_canvas?.HdSource?.Pack is not null, "HD verification needs ARC_HD_PACK");
                _gameSettings = _gameSettings with { HdInterface = true };
                SyncInterfaceHd();
                Require(InterfaceHd.Enabled, "HD interface switch enabled for this isolated smoke");
            }
            async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            async Task Click(Vector2 at)
            {
                foreach (var pressed in new[] { true, false })
                {
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                    });
                    await Frame();
                }
            }
            // ARC-UI-078: a refusal is the original's charedit.mes text (the project's words only when the game folder has none): with no points to spend 18
            // (a stat's + is 17 first when it is at its maximum).
            bool Original(string title, params int[] numbers) => numbers.Any(n => _sheetArtContent!.Texts.Refusal(n) == title);
            InventoryKey(Key.C);
            Require(SheetOpen && _sheetPanel!.ArtMode, "C opened the sheet in the original's look: " + _sheetStatus + " " + _sheetArtContent?.Detail);
            await Frame();
            var art = _sheetPanel!.ArtViewForSelfTest!;
            if (verifyHd)
            {
                // ARC-UI-2865: the hero's portrait is drawn at its bitmap's size, and from the pack's redraw when the pack holds portraits (the pack of this run
                // may be an interface-only one: then the portrait stays Legacy and the line says so).
                var packPortraits = _canvas!.HdSource!.Pack!.Entries.Count(e => e.Kind == Arcanum.Data.Hd.HdEntryKind.Portrait);
                var portrait = art.PortraitTextureForSelfTest as HdInterfaceTexture;
                Require(portrait is not null && portrait.GetWidth() == 64 && portrait.GetHeight() == 64, "the sheet's portrait keeps its 64 x 64 size");
                Require(packPortraits == 0 || portrait!.IsHd && portrait.HdScale == 4, $"the pack holds {packPortraits} portraits and the sheet's portrait draws HD");
                GD.Print($"ARC-UI-2865 native sheet portrait HD PASS packPortraits={packPortraits} hd={portrait!.IsHd}");
                var count = 0;
                foreach (var load in _sheetArtContent!.Art.Values)
                foreach (var frame in load.Frames.Where(f => f.Hd is not null))
                {
                    if (load.Number is 27 or 28 or 171 or 229 or 300 or 301 or 483) continue;
                    var resolved = _canvas!.HdSource!.Pack!.ResolveSprite(frame.Hd!, frame.Rgba);
                    Require(resolved.Status == Arcanum.Data.Hd.HdFrameStatus.Hd, $"HD frame {load.Number}:{count} refused ({resolved.Status} {resolved.Invalid})");
                    count++;
                }
                Require(count > 0, "character frames resolved");
                GD.Print($"ARC-UI-114 native character HD frames PASS count={count}");
            }
            var view = art.ViewForSelfTest ?? throw new InvalidOperationException("ARC-UI-075 sheet art smoke: no view");
            var texts = art.TextsForSelfTest;
            Require(texts.Count(text => text.Colour == AreaSheetColour.Gold) == 1 && texts.Any(text => text.Text.StartsWith(view.Name[..Math.Min(view.Name.Length, 3)], StringComparison.Ordinal)),
                "the name in gold");
            Require(texts.Count(text => text.Font == AreaSheetFont.Label && text.Centred) >= 10 && texts.Count(text => text.Font == AreaSheetFont.Flare) == 13
                && texts.Count(text => text.Font == AreaSheetFont.Cloister) == 3, "the stat, derived, resistance and gauge texts");
            // ARC-UI-1791 (ledger CHW25): the hero's sheet prints its age at (337, 72) when the session stores one, and its portrait slot never holds an icon.
            Require(view.Age is not { } heroAge || texts.Any(text => text.X == 337 && text.Y == 72 && text.Text.EndsWith(heroAge.ToString(), StringComparison.Ordinal)), "the hero's age line: " + view.Age);
            Require(!art.IconForSelfTest, "the hero's portrait slot holds no icon");
            // The pointer over the first stat's + button: its text in the message window.
            var (px, py) = AreaCharacterSheetArtLayout.Plus(2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = art.PointForSelfTest(px + 3, py + 3), GlobalPosition = art.PointForSelfTest(px + 3, py + 3) });
            await Frame();
            Require(art.RegionForSelfTest == AreaCharacterSheetArtLayout.RowHint(2), "the pointer over the + button: region " + art.RegionForSelfTest);
            // The window shows a hover entry only when nothing was stored in the last 3 s (ML2), so the smoke checks that the text exists.
            Require(art.Texts.Hint(AreaCharacterSheetArtLayout.RowHint(2)) is { Length: > 20 } hint && hint.StartsWith("Strength", StringComparison.Ordinal), "the + button's text from charedit.mes");
            var revision = session.Version.Revision;
            var unspent = session.QueryHeroExperience()!.UnspentPoints;
            // Only with nothing to spend: a press with points would change the hero the later smokes of the chain use.
            if (unspent == 0) await Click(art.PointForSelfTest(px + 3, py + 3));
            if (unspent == 0) Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } statRefusal && Original(statRefusal.Title, 17, 18),
                "a press with no points is refused in the message window with the original's text (charedit.mes 17 or 18): " + _messageLog.Shown?.Title);
            // ARC-UI-076: the sheet opens on the skills page in the original's look; a group tab shows another group, the pointer over a skill
            // writes its type-7 hint, a press on + with no points is refused in the message window with the original's text (ARC-UI-078).
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_CAPTURE");
            async Task Capture(string suffix)
            {
                if (string.IsNullOrEmpty(capture)) return;
                await Frame();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                if (shot.SavePng(capture.Replace(".png", suffix + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-075 sheet art smoke: capture failed");
            }
            // ARC-UI-1195: with ARC_AREA_WINDOW_ART the sheet's window and the page on show (their frames and buttons) are checked in the generated interface and captured.
            async Task WindowArt(string page, IEnumerable<int> numbers)
            {
                if (!WindowArtSmokeRequested) return;
                var found = art.TexturesForSelfTest(AreaCharacterSheetArtLayout.WindowArtNumbers.Concat(numbers));
                await WindowArtSmokeAsync("sheet-" + page, () => found.Select(pair => pair.Texture).ToArray(), found.Select(pair => pair.Art).ToArray(), art.DeviceScale, () => art.ArtSet,
                    new Vector2I(1600, 1200)); // the bars are hidden here, so the sheet stands at the whole factor of the screen: 2 from 1600 x 1200 (1 at 1280 x 800 and 1920 x 1080)
            }
            var pages = art.Pages;
            Require(_sheetPanel.PagesReadyForSelfTest && pages.Visible && pages.ShownForSelfTest == AreaSheetTab.Skills, "the pages' art read and the sheet opens on the skills page");
            var entries = new List<AreaMessageEntry?>();
            void Heard(AreaMessageEntry? entry) => entries.Add(entry);
            _sheetPanel.HoverEntryRequested += Heard;
            await Capture("-skills");
            await WindowArt("skills", AreaCharacterSheetArtLayout.SkillsPageArtNumbers);
            var (gx, gy) = AreaCharacterSheetPagesLayout.GroupTab(3);
            await Click(pages.PointForSelfTest(gx + 5, gy + 5));
            Require(pages.Group == 3, "the technological group tab");
            await Frame();
            var (sx, sy) = AreaCharacterSheetPagesLayout.SkillPlus(1);
            GetViewport().PushInput(new InputEventMouseMotion { Position = pages.PointForSelfTest(sx + 3, sy + 3), GlobalPosition = pages.PointForSelfTest(sx + 3, sy + 3) });
            await Frame();
            Require(entries.Any(entry => entry is { Kind: AreaMessageKind.SkillHint, Title.Length: > 0 }), "the pointer over a skill's + writes its hint");
            if (unspent == 0)
            {
                await Click(pages.PointForSelfTest(sx + 3, sy + 3));
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } skillRefusal && Original(skillRefusal.Title, 18),
                    "a skill's + with no points is refused in the message window");
            }
            // The tech tab shows the tech page: a discipline button, the pointer over the header, a degree's + refused with no points.
            var (_, techX, techY, _) = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Tech);
            await Click(art.PointForSelfTest(techX + 5, techY + 5));
            Require(art.Tab == AreaSheetTab.Tech && pages.Visible && pages.ShownForSelfTest == AreaSheetTab.Tech, "the tech tab shows the tech page");
            var (dx, dy) = AreaCharacterSheetPagesLayout.DisciplineTab(2);
            await Click(pages.PointForSelfTest(dx + 5, dy + 5));
            Require(pages.Discipline == 2, "a discipline button");
            entries.Clear();
            var header = AreaCharacterSheetPagesLayout.HeaderRegion;
            GetViewport().PushInput(new InputEventMouseMotion { Position = pages.PointForSelfTest(header.X + 5, header.Y + 5), GlobalPosition = pages.PointForSelfTest(header.X + 5, header.Y + 5) });
            await Frame();
            Require(entries.Any(entry => entry is { Title.Length: > 0 }), "the pointer over the header writes its text");
            await Capture("-tech");
            await WindowArt("tech", AreaCharacterSheetArtLayout.TechPageArtNumbers);
            var degree = session.QueryHeroTechnology()!.Value[2].Degree;
            var (tx2, ty2) = AreaCharacterSheetPagesLayout.DegreeSlot(degree);
            if (unspent == 0 && degree < AreaCharacterSheetPagesLayout.Degrees)
            {
                await Click(pages.PointForSelfTest(tx2 + 5, ty2 + 5));
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } techRefusal && Original(techRefusal.Title, 18),
                    "a degree's + with no points is refused in the message window");
            }
            _sheetPanel.HoverEntryRequested -= Heard;
            // ARC-UI-077: the spells tab shows the spells page in the original's look (art 31, sixteen college tabs, five spells); a tab shows another
            // college, the pointer over a tab or a spell writes its hint, a + with no points is refused in the message window.
            var (_, tx, ty, _) = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells);
            await Click(art.PointForSelfTest(tx + 5, ty + 5));
            var spells = art.Spells;
            Require(art.Tab == AreaSheetTab.Spells && !pages.Visible, "the spells tab");
            Require(_sheetPanel.SpellsReadyForSelfTest, "the spells page's art read");
            Require(spells.Visible && spells.ViewForSelfTest is not null, "the spells page shows");
            var startCollege = spells.ViewForSelfTest!.College;
            await Capture("-spells");
            await WindowArt("spells", AreaCharacterSheetArtLayout.SpellsPageArtNumbers);
            _sheetPanel.HoverEntryRequested += Heard;
            entries.Clear();
            var (cx, cy) = AreaCharacterSheetSpellsLayout.CollegeTab(9);
            await Click(spells.PointForSelfTest(cx + 5, cy + 5));
            Require(spells.ViewForSelfTest!.College == 9, "a college tab shows another college: " + spells.ViewForSelfTest.College);
            entries.Clear();
            GetViewport().PushInput(new InputEventMouseMotion { Position = spells.PointForSelfTest(cx + 5, cy + 5), GlobalPosition = spells.PointForSelfTest(cx + 5, cy + 5) });
            await Frame();
            Require(entries.Any(entry => entry is { Kind: AreaMessageKind.SpellHint, Icon: not null, Title.Length: > 0 }), "the pointer over a tab writes the college's hint");
            var rank = spells.ViewForSelfTest.Rank;
            var (ix, iy) = AreaCharacterSheetSpellsLayout.Icon(Math.Min(rank, 4));
            entries.Clear();
            GetViewport().PushInput(new InputEventMouseMotion { Position = spells.PointForSelfTest(ix + 5, iy + 5), GlobalPosition = spells.PointForSelfTest(ix + 5, iy + 5) });
            await Frame();
            // With the game bar's texts the hint is the hotkey bar's spell hint (the college's icon); without them the page writes the name.
            Require(entries.Any(entry => entry is { Title.Length: > 0 } && (_gameBarContent is null ? entry.Kind == AreaMessageKind.Hover : entry is { Kind: AreaMessageKind.SpellHint, Icon: not null })),
                "the pointer over a spell writes its hint");
            if (unspent == 0 && rank < AreaCharacterSheetSpellsLayout.Rows)
            {
                await Click(spells.PointForSelfTest(ix + 5, iy + 5));
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } spellRefusal && Original(spellRefusal.Title, 18),
                    "a spell's + with no points is refused in the message window");
            }
            _sheetPanel.HoverEntryRequested -= Heard;
            // The shown college is kept between opens (CHW17): the smoke puts it back for the stages after it.
            var (bx, by) = AreaCharacterSheetSpellsLayout.CollegeTab(startCollege);
            await Click(spells.PointForSelfTest(bx + 5, by + 5));
            Require(spells.ViewForSelfTest!.College == startCollege, "the first college is shown again");
            // ARC-UI-1742: the schemes tab shows the scheme page in the original's look (art 567, the hero's scheme named at the top, fifteen rows of names, the
            // scroll bar): the pointer over a row lights it, the wheel and the arrows scroll the list, a release on a row writes that scheme into the hero (the original
            // restores nothing: the smoke writes the first scheme back for the stages after it).
            var (_, schX, schY, _) = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Schemes);
            await Click(art.PointForSelfTest(schX + 5, schY + 5));
            var schemes = art.Schemes;
            Require(art.Tab == AreaSheetTab.Schemes && !spells.Visible && !pages.Visible, "the schemes tab");
            Require(_sheetPanel.SchemesReadyForSelfTest && schemes.Visible && schemes.ViewForSelfTest is not null, "the scheme page shows in the original's look");
            var schemePage = schemes.ViewForSelfTest!;
            var startScheme = session.QueryHero()!.Advancement.AutoLevelScheme;
            Require(schemePage.Scheme == startScheme && schemePage.Entries.Count > AreaSchemesPage.Rows && schemes.BarForSelfTest is { Maximum: > 0 } bar && bar.Value == 0
                && schemePage.Entries.Zip(schemePage.Entries.Skip(1)).All(pair => pair.First.Number < pair.Second.Number), "the hero's scheme, an ordered list longer than the rows, a bar at its top");
            await Capture("-schemes");
            await WindowArt("schemes", AreaCharacterSheetArtLayout.SchemesPageArtNumbers);
            var (rx, ry, _, _) = AreaCharacterSheetSchemesLayout.Row(2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = schemes.PointForSelfTest(rx + 5, ry + 5), GlobalPosition = schemes.PointForSelfTest(rx + 5, ry + 5) });
            await Frame();
            Require(schemes.HoverRowForSelfTest == 2, "the pointer over a row lights it: " + schemes.HoverRowForSelfTest);
            var wheelAt = schemes.PointForSelfTest(rx + 5, ry + 5);
            // A wheel turn is a press and a release (Godot keeps the mouse grab on the pressed control until the button mask empties).
            foreach (var pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = pressed, Position = wheelAt, GlobalPosition = wheelAt });
            await Frame();
            Require(schemes.ScrollForSelfTest == AreaSheetScrollBar.DefaultWheel, "the wheel scrolls three entries: " + schemes.ScrollForSelfTest);
            var bar2 = schemes.BarForSelfTest!;
            var (downX, downY) = bar2.DownArrow;
            await Click(schemes.PointForSelfTest(downX + 3, downY + 3));
            Require(schemes.ScrollForSelfTest == AreaSheetScrollBar.DefaultWheel + 1, "the down arrow scrolls one more: " + schemes.ScrollForSelfTest);
            await Capture("-schemes-scrolled");
            var rowEntry = AreaCharacterSheetSchemesLayout.EntryAt(schemePage, schemes.ScrollForSelfTest, 2)!;
            var chosenRow = rowEntry.Number == startScheme ? 3 : 2;
            var chosen = AreaCharacterSheetSchemesLayout.EntryAt(schemePage, schemes.ScrollForSelfTest, chosenRow)!;
            var (cx2, cy2, _, _) = AreaCharacterSheetSchemesLayout.Row(chosenRow);
            var schemeRevision = session.Version.Revision;
            await Click(schemes.PointForSelfTest(cx2 + 5, cy2 + 5));
            Require(session.QueryHero()!.Advancement.AutoLevelScheme == chosen.Number && session.Version.Revision == schemeRevision + 1,
                "a release on a row writes its scheme into the hero: " + session.QueryHero()!.Advancement.AutoLevelScheme);
            await Frame();
            Require(schemes.ViewForSelfTest!.Scheme == chosen.Number && schemes.ViewForSelfTest.SchemeName == chosen.Name, "the page names the chosen scheme at the top");
            Require(session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, startScheme).Disposition == GameSessionDisposition.Changed, "the first scheme written back");
            SyncAreaSheet();
            await Frame();
            Require(schemes.ViewForSelfTest!.Scheme == startScheme, "the page follows the hero's scheme again");
            // A tab away and back keeps the scroll while the sheet stays open (the exe's list offset is reset only at an open).
            var keptScroll = schemes.ScrollForSelfTest;
            await Click(art.PointForSelfTest(tx + 5, ty + 5));
            await Click(art.PointForSelfTest(schX + 5, schY + 5));
            Require(schemes.ScrollForSelfTest == keptScroll, "the list keeps its place between tabs");
            var close = AreaCharacterSheetArtView.CloseRectangle;
            await Click(art.PointForSelfTest(close.X + 20, close.Y + 20));
            Require(!SheetOpen && !session.HeroEditorOpen, $"a release in the close rectangle closed the sheet and its editor (sheet open {SheetOpen}, editor open {session.HeroEditorOpen}, tab {art.Tab})");
            // ARC-UI-088: the dialog's c-e opens the speaker's sheet in the original's mode 2: the same window over the NPC's values with no + or − button and no
            // editor snapshot; a release in the close rectangle closes it and the next C opens the hero's sheet with its buttons again.
            var npcName = "none (no speaker with a sheet)";
            if (_conversationHost is { } talk)
            {
                for (var i = 0; i < talk.Speakers.Count && _sheetNpc is null; i++)
                {
                    QueueDialogNpcSheet(talk, (AreaConversationState.EntityId(talk.Speakers[i].Row), i));
                    TakeDialogNpcSheet();
                    await Frame();
                }
                if (_sheetNpc is { } npc)
                {
                    npcName = npc.View.Name;
                    Require(SheetOpen && _sheetPanel.ArtMode && _sheetPanel.ReadOnly && art.ReadOnly && pages.ReadOnly && spells.ReadOnly, "the NPC's sheet opened read-only");
                    Require(!session.HeroEditorOpen, "the NPC's sheet took no editor snapshot");
                    await Frame();
                    var npcTexts = art.TextsForSelfTest;
                    Require(npcTexts.Any(text => text.Colour == AreaSheetColour.Gold && text.Text.StartsWith(npc.View.Name[..Math.Min(npc.View.Name.Length, 3)], StringComparison.Ordinal)),
                        "the NPC's name in gold: " + npc.View.Name);
                    // ARC-UI-1791 (ledger CHW25): an NPC's sheet has no age line; its slot shows the icon of 0x553D10 (the race's or its flag's) unless it shows the portrait of a follower.
                    Require(!npcTexts.Any(text => text.X == 337 && text.Y == 72), "no age line on the NPC's sheet");
                    Require(npc.View.IconArt is null || art.IconForSelfTest, "the NPC's slot icon, art " + npc.View.IconArt + " (the arts read: " + _sheetArtContent?.Detail + ")");
                    var shownBefore = _messageLog.Shown; var revisionNpc = session.Version.Revision;
                    await Click(art.PointForSelfTest(px + 3, py + 3));
                    Require(session.Version.Revision == revisionNpc && ReferenceEquals(_messageLog.Shown, shownBefore), "a click where the + stands presses nothing");
                    await Capture("-npc");
                    var (_, nsx, nsy, _) = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells);
                    await Click(art.PointForSelfTest(nsx + 5, nsy + 5));
                    Require(art.Tab == AreaSheetTab.Spells && spells.Visible, "the spells tab of the NPC's sheet");
                    await Capture("-npc-spells");
                    // ARC-UI-1742: the NPC sheet's scheme page names the NPC's own scheme and lists the schemes with no row button (mode 2): a press writes nothing.
                    await Click(art.PointForSelfTest(schX + 5, schY + 5));
                    Require(art.Tab == AreaSheetTab.Schemes && schemes.Visible && schemes.ReadOnly && schemes.ViewForSelfTest is { } npcPage && npcPage.Scheme == npc.AutoLevelScheme,
                        "the NPC's scheme page: " + schemes.ViewForSelfTest?.Scheme + " of " + npc.AutoLevelScheme);
                    var npcRevision = session.Version.Revision;
                    await Click(schemes.PointForSelfTest(rx + 5, ry + 5));
                    Require(session.Version.Revision == npcRevision && schemes.HoverRowForSelfTest == -1, "a press on a row of the NPC's list writes nothing");
                    await Capture("-npc-schemes");
                    var closeNpc = AreaCharacterSheetArtView.CloseRectangle;
                    await Click(art.PointForSelfTest(closeNpc.X + 20, closeNpc.Y + 20));
                    Require(!SheetOpen && _sheetNpc is null && !_sheetPanel.ReadOnly, "a release in the close rectangle closed the NPC's sheet");
                    InventoryKey(Key.C);
                    Require(SheetOpen && _sheetNpc is null && !art.ReadOnly && session.HeroEditorOpen, "the next C opens the hero's sheet with its buttons and editor");
                }
            }
            GD.Print("ARC-UI-075 native-smoke sheet art PASS " + System.Text.Json.JsonSerializer.Serialize(new { Texts = texts.Count, unspent, view.Level, NpcSheet = npcName }));
        }
        catch (Exception failure)
        {
            GD.Print("ARC-UI-075 native-smoke sheet art FAIL " + failure.Message);
            throw;
        }
        finally
        {
            if (SheetOpen) EndSheet();
            if (verifyHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd }; SyncInterfaceHd(); }
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
