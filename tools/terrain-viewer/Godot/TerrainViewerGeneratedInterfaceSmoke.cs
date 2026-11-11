using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-140: opt-in part of the native bar smoke (ARC_AREA_BAR_GENERATED_VERIFY=1). Both strips and every bar button have a
    // recipe, so each draws generated chrome at the same size and place as its Legacy frame; the layout and hit tests are unchanged.
    private async Task VerifyGeneratedInterfaceAsync(AreaGameBarPanel panel, Func<int, Task> frames)
    {
        static void Require(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException("ARC-UI-140 generated interface smoke: " + why);
        }
        await frames(2);
        Require(InterfaceHd.Generated, "the generated interface is on");
        var checkedCount = 0;
        foreach (var strip in new[] { AreaBarStrip.Top, AreaBarStrip.Bottom })
        {
            var art = panel.GetNode<TextureRect>($"{strip}Strip/Art");
            Require(art.Texture is HdInterfaceTexture { IsGenerated: true } texture
                && texture.GetSize() == new Vector2(AreaGameBarLayout.ScreenWidth, strip == AreaBarStrip.Top ? AreaGameBarLayout.TopHeight : AreaGameBarLayout.BottomHeight),
                $"the {strip} strip draws generated chrome at its Legacy size");
            checkedCount++;
        }
        foreach (var spec in AreaGameBarLayout.Buttons)
        {
            var frames0 = panel.ArtFrames(spec.Art);
            Require(frames0 is { Length: > 0 }, $"{spec.Button} has art");
            foreach (var frame in frames0!)
            {
                Require(frame is HdInterfaceTexture { IsGenerated: true }, $"{spec.Button} frame draws generated chrome");
                checkedCount++;
            }
            var control = panel.GetNode<TextureRect>($"{spec.Strip}Strip/{spec.Button}");
            var (left, top, _, _) = AreaGameBarLayout.StripRect(spec.Strip);
            Require(control.Position == new Vector2(spec.X - left, spec.Y - top) && control.Size == control.Texture?.GetSize(),
                $"{spec.Button} stays at its Legacy place and size");
        }
        GD.Print($"ARC-UI-140 native generated interface PASS textures={checkedCount} factor={panel.Factor}");
    }

    // ARC_AREA_BAR_CAPTURE=PREFIX also saves the whole viewport with the bars as PREFIX-hud-MODE.png (a local, ignored path).
    private async Task CaptureHudAsync(string mode, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        await frames(2);
        var path = System.IO.Path.GetFullPath(capture) + $"-hud-{mode}.png";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-140 HUD readback not saved: " + path);
        GD.Print($"ARC-UI-140 HUD capture {mode} {image.GetWidth()}x{image.GetHeight()}");
    }
}
