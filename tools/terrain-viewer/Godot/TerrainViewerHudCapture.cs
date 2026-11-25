using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC_AREA_BAR_CAPTURE=PREFIX also saves the whole viewport with the bars as PREFIX-hud-MODE.png (a local, ignored path).
    private async Task CaptureHudAsync(string mode, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        await frames(2);
        var path = System.IO.Path.GetFullPath(capture) + $"-hud-{mode}.png";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        // ARC-UI-2936: the composition smoke's captures show what the window shows: the virtual screen reduced to s and centred on black at the render target's size.
        using var shown = _originalCompositionTarget is { } target && OriginalCompositionFactor > 0 && image is not null ? ComposeOriginalCapture(image, target) : null;
        var saved = shown ?? image;
        if (saved is null || saved.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-140 HUD readback not saved: " + path);
        GD.Print($"ARC-UI-140 HUD capture {mode} {saved.GetWidth()}x{saved.GetHeight()}");
    }

    /// <summary>ARC-UI-2936: the window's picture for a render target of <paramref name="target"/> pixels: the virtual screen <paramref name="virtualScreen"/> reduced to the composition's size (bilinear, as the window's blit does) at its
    /// centred place, black around it. A whole scale needs no reduction.</summary>
    private static Image ComposeOriginalCapture(Image virtualScreen, Vector2I target)
    {
        var zone = OriginalComposition.Zone(target.X, target.Y);
        var drawn = (Image)virtualScreen.Duplicate();
        if (drawn.GetWidth() != zone.Width || drawn.GetHeight() != zone.Height) drawn.Resize(zone.Width, zone.Height, Image.Interpolation.Bilinear);
        var result = Image.CreateEmpty(target.X, target.Y, false, drawn.GetFormat());
        result.Fill(Colors.Black);
        result.BlitRect(drawn, new Rect2I(0, 0, zone.Width, zone.Height), new Vector2I(zone.X, zone.Y));
        drawn.Dispose();
        return result;
    }
}
