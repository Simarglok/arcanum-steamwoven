using Arcanum.TerrainViewer.Core;
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
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
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
            InventoryKey(Key.C);
            Require(SheetOpen && _sheetPanel!.ArtMode, "C opened the sheet in the original's look: " + _sheetStatus + " " + _sheetArtContent?.Detail);
            await Frame();
            var art = _sheetPanel!.ArtViewForSelfTest!;
            var view = art.ViewForSelfTest ?? throw new InvalidOperationException("ARC-UI-075 sheet art smoke: no view");
            var texts = art.TextsForSelfTest;
            Require(texts.Count(text => text.Colour == AreaSheetColour.Gold) == 1 && texts.Any(text => text.Text.StartsWith(view.Name[..Math.Min(view.Name.Length, 3)], StringComparison.Ordinal)),
                "the name in gold");
            Require(texts.Count(text => text.Font == AreaSheetFont.Label && text.Centred) >= 10 && texts.Count(text => text.Font == AreaSheetFont.Flare) == 13
                && texts.Count(text => text.Font == AreaSheetFont.Cloister) == 3, "the stat, derived, resistance and gauge texts");
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
            if (unspent == 0) Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message }, "a press with no points is refused in the message window");
            // ARC-UI-076: the sheet opens on the skills page in the original's look; a group tab shows another group, the pointer over a skill
            // writes its type-7 hint, a press on + with no points is refused in the message window.
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_CAPTURE");
            async Task Capture(string suffix)
            {
                if (string.IsNullOrEmpty(capture)) return;
                await Frame();
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                if (shot.SavePng(capture.Replace(".png", suffix + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-075 sheet art smoke: capture failed");
            }
            var pages = art.Pages;
            Require(_sheetPanel.PagesReadyForSelfTest && pages.Visible && pages.ShownForSelfTest == AreaSheetTab.Skills, "the pages' art read and the sheet opens on the skills page");
            var entries = new List<AreaMessageEntry?>();
            void Heard(AreaMessageEntry? entry) => entries.Add(entry);
            _sheetPanel.HoverEntryRequested += Heard;
            await Capture("-skills");
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
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } skillRefusal && skillRefusal.Title.StartsWith("refused", StringComparison.Ordinal),
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
            var degree = session.QueryHeroTechnology()!.Value[2].Degree;
            var (tx2, ty2) = AreaCharacterSheetPagesLayout.DegreeSlot(degree);
            if (unspent == 0 && degree < AreaCharacterSheetPagesLayout.Degrees)
            {
                await Click(pages.PointForSelfTest(tx2 + 5, ty2 + 5));
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } techRefusal && techRefusal.Title.StartsWith("refused", StringComparison.Ordinal),
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
                Require(session.Version.Revision == revision && _messageLog.Shown is { Kind: AreaMessageKind.Message } spellRefusal && spellRefusal.Title.StartsWith("refused", StringComparison.Ordinal),
                    "a spell's + with no points is refused in the message window");
            }
            _sheetPanel.HoverEntryRequested -= Heard;
            // The shown college is kept between opens (CHW17): the smoke puts it back for the stages after it.
            var (bx, by) = AreaCharacterSheetSpellsLayout.CollegeTab(startCollege);
            await Click(spells.PointForSelfTest(bx + 5, by + 5));
            Require(spells.ViewForSelfTest!.College == startCollege, "the first college is shown again");
            var close = AreaCharacterSheetArtView.CloseRectangle;
            await Click(art.PointForSelfTest(close.X + 20, close.Y + 20));
            Require(!SheetOpen && !session.HeroEditorOpen, "a release in the close rectangle closed the sheet and its editor");
            GD.Print("ARC-UI-075 native-smoke sheet art PASS " + System.Text.Json.JsonSerializer.Serialize(new { Texts = texts.Count, unspent, view.Level }));
        }
        catch (Exception failure)
        {
            GD.Print("ARC-UI-075 native-smoke sheet art FAIL " + failure.Message);
            throw;
        }
        finally
        {
            if (SheetOpen) EndSheet();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
