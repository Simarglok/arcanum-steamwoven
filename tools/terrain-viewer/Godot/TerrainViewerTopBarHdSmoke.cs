using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-116: opt-in extension of the native bar smoke. The pack and captures stay local.
    // The existing smoke supplies isolated settings; no game settings are saved here.
    private async Task VerifyTopBarHdAsync(AreaGameBarPanel panel, Func<int, Task> frames,
        Func<Vector2, Task> move, Func<Vector2, bool, Task> button)
    {
        static void Require(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException("ARC-UI-116 top bar HD smoke: " + why);
        }
        var pack = InterfaceHd.Pack;
        Require(InterfaceHd.Enabled && pack is not null, "HD interface enabled with a pack");
        var numbers = new[] { 185, 169, 187, 193, 186 };
        var count = 0;
        foreach (var number in numbers)
        {
            Require(_gameBarContent!.Art.TryGetValue(number, out var art) && art.Status == AreaInterfaceArtStatus.Ready,
                $"art {number} is loaded");
            Require(art!.Frames.Count == (number == 185 ? 1 : 3), $"art {number} has its expected states");
            foreach (var frame in art.Frames)
            {
                Require(frame.Hd is not null, $"art {number} has a runtime key");
                var resolved = pack!.ResolveSprite(frame.Hd!, frame.Rgba);
                Require(resolved.Status == HdFrameStatus.Hd && resolved.Width == frame.Width * pack.Scale
                    && resolved.Height == frame.Height * pack.Scale, $"art {number} frame {count} resolves at pack scale");
                count++;
            }
        }
        Require(count == 13, "13 bounded frames resolved");
        var strip = panel.StripRectForSelfTest(AreaBarStrip.Top);
        // ARC-DEV-1759: a spot on the strip that no button covers (the clock window, 648..776, filled the old spot at x 720).
        var outside = strip.Position + new Vector2(400, 20) * panel.Factor;
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE");
        var geometry = new List<object>();
        foreach (var spec in AreaGameBarLayout.Buttons.Where(s => numbers.Contains(s.Art)))
        {
            var at = panel.ButtonCentreForSelfTest(spec.Button);
            await move(outside);
            await Observe("idle", 0);
            await move(at);
            await Observe("hover", 2);
            await button(at, true);
            await Observe("down", 1);
            // Release outside the button: exercise its held frame without opening a window.
            await move(outside);
            await button(outside, false);
            Require(_gameBar.Held is null, "release outside clears the held button");

            async Task Observe(string state, int expected)
            {
                await frames(2);
                Require(panel.FrameForSelfTest(spec.Button) == expected, $"{spec.Button} {state} uses frame {expected}");
                var control = panel.GetNode<TextureRect>($"TopStrip/{spec.Button}");
                Require(control.Texture is HdInterfaceTexture hd && hd.IsHd, $"{spec.Button} {state} draws HD");
                Require(panel.GetNode<TextureRect>("TopStrip/Art").Texture is HdInterfaceTexture top && top.IsHd,
                    "the strip draws HD");
                var rect = control.GetGlobalRect();
                geometry.Add(new { art = spec.Art, state, frame = expected, x = rect.Position.X, y = rect.Position.Y,
                    width = rect.Size.X, height = rect.Size.Y, factor = panel.Factor });
                if (string.IsNullOrEmpty(capture)) return;
                var path = System.IO.Path.GetFullPath(capture);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using var image = GetViewport().GetTexture().GetImage();
                using var crop = image.GetRegion(new Rect2I((int)strip.Position.X, (int)strip.Position.Y,
                    (int)strip.Size.X, (int)strip.Size.Y));
                Require(crop.SavePng(path + $"-{spec.Art}-{state}.png") == Error.Ok, "top strip readback saved");
            }
        }
        if (!string.IsNullOrEmpty(capture))
            System.IO.File.WriteAllText(capture + "-geometry.json", System.Text.Json.JsonSerializer.Serialize(geometry));
        GD.Print($"ARC-UI-116 native top bar HD PASS frames={count} states={geometry.Count} factor={panel.Factor}");
    }
}
