using System.Globalization;
using Arcanum.Experiments.HdUiDensityLab.Core;
using Godot;

namespace Arcanum.Experiments.HdUiDensityLab;

/// <summary>Renders the mock screen off screen at fixed scales, reads each frame back and checks the device-pixel grid:
/// every band of the window frame and of the top strip is exactly its token colour and exactly its rounded width, and the
/// vector lens was drawn. Optionally saves the frames (synthetic, local only).</summary>
internal static class SelfTest
{
    private static readonly double[] Scales = [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.75, 4];
    private const double ScreenUnitsWide = 960;

    internal static async Task RunAsync(Node host, HostKind hostKind, string? captureDir)
    {
        try
        {
            await RunChecksAsync(host, hostKind, captureDir);
        }
        catch (Exception e)
        {
            GD.PrintErr($"ARC-RES-122 self-test result=FAIL exception={e.GetType().Name}: {e.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task RunChecksAsync(Node host, HostKind hostKind, string? captureDir)
    {
        if (captureDir is not null && !Path.IsPathRooted(captureDir))
            throw new ArgumentException($"--capture needs an absolute directory, got '{captureDir}'");
        var reading = (DisplayServer.WindowGetSize(), DisplayServer.ScreenGetScale(), DisplayServer.ScreenGetDpi());
        GD.Print($"ARC-RES-122 self-test start host={hostKind} driver={DisplayServer.GetName()} window={reading.Item1.X}x{reading.Item1.Y} reportedScale={F(reading.Item2)} dpi={reading.Item3}");
        if (captureDir is not null) Directory.CreateDirectory(captureDir);
        var failures = 0;
        var checks = 0;
        foreach (var s in Scales)
        {
            var width = DeviceGrid.Round(ScreenUnitsWide * s);
            var height = DeviceGrid.Round(DensityScale.UnitsHigh * s);
            var viewport = new SubViewport
            {
                Size = new Vector2I(width, height),
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                Snap2DTransformsToPixel = true,
            };
            host.AddChild(viewport);
            var canvas = new MockCanvas();
            viewport.AddChild(canvas);
            canvas.Size = new Vector2(width, height);
            var model = MockScreen.Build(width, height, s, Interaction.None);
            canvas.Model = model;
            for (var i = 0; i < 3; i++)
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = viewport.GetTexture().GetImage();
            if (image is null || image.IsEmpty() || image.GetWidth() != width || image.GetHeight() != height)
                throw new InvalidOperationException($"no frame read back at s={F(s)} (a headless or dummy renderer cannot run this test)");
            if (captureDir is not null)
            {
                var saved = image.SavePng(Path.Combine(captureDir, $"res122-s{s.ToString("0.00", CultureInfo.InvariantCulture)}.png"));
                if (saved != Error.Ok) throw new IOException($"capture at s={F(s)} failed: {saved}");
            }

            foreach (var (name, problem) in Check(model, image))
            {
                checks++;
                if (problem is null)
                {
                    GD.Print($"ARC-RES-122 self-test s={F(s)} {name} PASS");
                }
                else
                {
                    failures++;
                    GD.PrintErr($"ARC-RES-122 self-test s={F(s)} {name} FAIL {problem}");
                }
            }
            viewport.QueueFree();
        }
        GD.Print($"ARC-RES-122 self-test result={(failures == 0 ? "PASS" : "FAIL")} checks={checks} failures={failures}");
        host.GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private static IEnumerable<(string Name, string? Problem)> Check(ScreenModel model, Image image)
    {
        var s = model.Scale;
        var window = model.WindowOuter;
        var row = window.Y + window.Height / 2;
        var panel = Chrome.PanelFrame;
        var panelWidths = Chrome.BandWidths(panel, s);
        yield return ("window-left", Runs(image, window.X, row, 1, 0,
            panel.Select(b => b.Lit).ToArray(), panelWidths, Token.Wood));
        yield return ("window-right", Runs(image, window.Right - 1, row, -1, 0,
            panel.Select(b => b.Shaded).ToArray(), panelWidths, Token.Wood));

        var strip = model.TopStripOuter;
        var column = strip.X + DeviceGrid.Edge(780, s);
        var stripBands = Chrome.ButtonFrame;
        var stripWidths = Chrome.BandWidths(stripBands, s);
        var interior = strip.Height - 2 * stripWidths.Sum();
        yield return ("strip-top", Runs(image, column, strip.Y, 0, 1,
            stripBands.Select(b => b.Lit).ToArray(), stripWidths, Token.Wood, interior));
        yield return ("strip-bottom", Runs(image, column, strip.Bottom - 1, 0, -1,
            stripBands.Select(b => b.Shaded).ToArray(), stripWidths, Token.Wood, interior));

        var lower = model.BottomStripOuter;
        var lowerColumn = lower.X + DeviceGrid.Edge(605, s);
        var lowerInterior = lower.Height - 2 * stripWidths.Sum();
        yield return ("lower-strip-top", Runs(image, lowerColumn, lower.Y, 0, 1,
            stripBands.Select(b => b.Lit).ToArray(), stripWidths, Token.Wood, lowerInterior));

        var lens = model.Lenses[0];
        var centre = image.GetPixel(lens.CenterX, lens.CenterY);
        var ringX = lens.CenterX + lens.OuterRadiusPx - lens.RingPx / 2;
        var ring = image.GetPixel(ringX, lens.CenterY);
        yield return ("lens-drawn", centre.B > centre.R && ring.R > ring.B
            ? null
            : $"centre {Hex(centre)} ring {Hex(ring)}");
    }

    /// <summary>Walks from (x, y) in direction (dx, dy) and checks each band is exactly its colour for exactly its width,
    /// followed by the interior colour for <paramref name="interiorPixels"/> pixels (at least one).</summary>
    private static string? Runs(Image image, int x, int y, int dx, int dy, IReadOnlyList<Token> tokens, IReadOnlyList<int> widths,
        Token interior, int interiorPixels = 1)
    {
        var expected = new List<Token>();
        for (var i = 0; i < tokens.Count; i++)
            expected.AddRange(Enumerable.Repeat(tokens[i], widths[i]));
        expected.AddRange(Enumerable.Repeat(interior, Math.Max(1, interiorPixels)));
        for (var i = 0; i < expected.Count; i++)
        {
            var px = x + dx * i;
            var py = y + dy * i;
            if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight())
                return $"walk left the frame at step {i}";
            var actual = image.GetPixel(px, py);
            var (r, g, b) = Palette.Rgb(expected[i]);
            if (Byte(actual.R) != r || Byte(actual.G) != g || Byte(actual.B) != b)
                return $"step {i} at ({px},{py}) is {Hex(actual)}, expected {expected[i]} widths={string.Join(",", widths)}";
        }
        return null;
    }

    private static int Byte(float channel) => (int)Math.Round(channel * 255);

    private static string Hex(Color c) => $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
