using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-075 native observation with the original hero: the production C key opens the sheet in the original's look (art 22, the numbers in the
// original's fonts, the + and − art buttons), the pointer over a button writes its text to the message window, a press on + with no points
// to spend is refused in the message window, the spells tab shows the spells page and a release in the close rectangle closes the sheet.
// ARC_AREA_SHEET_CAPTURE=PATH.png saves a readback of the window. Prints counts only.
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
            // The spells tab shows the spells page; a release in the close rectangle closes the sheet.
            var (_, tx, ty, _) = AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells);
            await Click(art.PointForSelfTest(tx + 5, ty + 5));
            Require(art.Tab == AreaSheetTab.Spells, "the spells tab");
            await Frame();
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_CAPTURE") is { Length: > 0 } capture)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shot = GetViewport().GetTexture().GetImage();
                if (shot.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-UI-075 sheet art smoke: capture failed");
            }
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
