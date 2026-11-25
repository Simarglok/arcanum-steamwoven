using Arcanum.Presentation.Hud;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-2510: a local capture (ARC_AREA_BAR_CAPTURE, never in git) of the parts that hang from the screen of the Steamwoven column: two follower portraits, one text bubble over a marked point, and the outline of
    // the column the windows lie in (ARC-UI-3069: restored after the full-width layout was removed, ARC-UI-2926). Uses only the parts' public places, so the same capture runs on the build before the change.
    private async Task CaptureSteamwovenPartsAsync(string tag, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 }) return;
        var size = GetViewport().GetVisibleRect().Size;
        var (freeTop, free) = AreaGameBarPanel.FreeArea(size);
        // ARC-UI-2940: both draw in the original's text font only, so the capture gives them the game's font (a synthetic one without a game folder).
        var font = TextFonts()?.Text ?? AreaArtFontFace.SyntheticForSelfTest();
        var layer = new AreaFloaterLayer(font);
        var portraits = new AreaFollowerPortraits { MenuFont = font };
        var overlay = new CanvasLayer { Layer = 20 };
        var outline = new ReferenceRect { BorderColor = new Color(0.1f, 1f, 0.1f), BorderWidth = 3, EditorOnly = false, Position = new Vector2(AreaGameBarPanel.FreeLeft, freeTop), Size = free };
        var marker = new ColorRect { Color = Colors.Red, Size = new Vector2(8, 8), Position = new Vector2(size.X / 2 - 4, size.Y / 2 - 4) };
        overlay.AddChild(outline); overlay.AddChild(marker);
        try
        {
            AddChild(layer); AddChild(portraits); AddChild(overlay);
            if (_portraitContent is { } content) portraits.Bind(InterfaceTextures(content.Art));
            portraits.Show(Enumerable.Range(0, 3).Select(i => ($"capture:{i}", $"F{i}", (AreaLife?)new AreaLife(100 - 20 * i, 100, 60, 60, 0), (Texture2D?)null)).ToArray());
            layer.Anchor = _ => new Vector2(size.X / 2, size.Y / 2);
            layer.BubbleAnchor = _ => new Vector2(size.X / 2, size.Y / 2);
            layer.AddBubble("capture:0", "A text bubble over a character, drawn in the original's pixels", BubbleColor.White, (long)Time.GetTicksMsec());
            layer.Field.Add("capture:0", "Floating line", FloaterColor.White);
            await frames(3);
            await WaitFramesResampledAsync(frames);
            await CaptureHudAsync("steamwoven-parts-" + tag, frames);
        }
        finally { foreach (var node in new Node[] { layer, portraits, overlay }) { RemoveChild(node); node.QueueFree(); } }
    }
}
