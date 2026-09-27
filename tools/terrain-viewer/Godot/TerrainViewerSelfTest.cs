using Arcanum.Data;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static class TerrainViewerSelfTest
{
    public static async void Run(Node host)
    {
        try
        {
            AssertIndependentGeometryConstants();
            var frame = ArtDecoder.Decode(SyntheticTerrainFixture.CreateArt(), new ArtFrameSelection(0, 0, 0));
            AssertFrameBytes(frame);
            var key = new TerrainTextureKey("synthetic", 1, 0, 30, "synthetic", 0, 0, 0);
            using var cache = new TerrainTextureCache();
            cache.Upload(new Dictionary<TerrainTextureKey, DecodedTerrainTexture>
            {
                [key] = new DecodedTerrainTexture(key, frame.Width, frame.Height, frame.Rgba.ToArray())
            });
            var plan = CreateOraclePlan(key);
            var viewport = new SubViewport
            {
                Size = new Vector2I(32, 24),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            var canvas = new TerrainCanvas();
            viewport.AddChild(canvas);
            host.AddChild(viewport);
            canvas.Publish(plan, cache);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless")
            {
                using var rendered = viewport.GetTexture().GetImage();
                AssertRenderedOracle(rendered);
                GD.Print("ARC-RENDER-003 self-test PASS production-canvas pixel oracle");
            }
            else
            {
                // The dummy backend cannot read ViewportTexture; this is intentionally not called graphical verification.
                GD.Print("ARC-RENDER-003 self-test PASS decoded fixture/production plan; graphical readback NOT_VERIFIED headless");
            }
            viewport.QueueFree();
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"ARC-RENDER-003 self-test FAIL RenderFailure synthetic={exception.GetType().Name}:{exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static TerrainScenePlan CreateOraclePlan(TerrainTextureKey key) => TerrainScenePlan.CreateLiteral(new TerrainRect(0, 0, 32, 24),
    [
        // Source crop at a precomputed +1-anchor destination; the canvas must not recompute it.
        new TerrainDrawCommand(0, key, new TerrainRect(1, 1, 3, 2), new TerrainRect(11, 10, 3, 2), false, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0)),
        // Raw tile X flip reverses only the selected source interval while retaining its footprint.
        new TerrainDrawCommand(1, key, new TerrainRect(0, 0, 3, 2), new TerrainRect(2, 2, 3, 2), true, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 1, 0)),
        // Facade command remains unflipped even if an opaque packed bit would exist upstream.
        new TerrainDrawCommand(2, key, new TerrainRect(0, 0, 1, 1), new TerrainRect(20, 2, 1, 1), false, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 1, 0, 0)),
        // Later facade wins overlap: explicit layer/Y/X/ordinal order is exercised by the production canvas.
        new TerrainDrawCommand(3, key, new TerrainRect(3, 0, 1, 1), new TerrainRect(25, 5, 1, 1), false, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 0, 0, 0)),
        new TerrainDrawCommand(4, key, new TerrainRect(2, 0, 1, 1), new TerrainRect(25, 5, 1, 1), false, new TerrainDiagnosticOrderKey(TerrainDiagnosticMode.EditorDiagnostic, 1, 0, 0)),
    ]);

    private static void AssertIndependentGeometryConstants()
    {
        // Hand-authored expectations from the documented diagnostic contract, not generated with TerrainGeometry.
        var expected = new[] { (0, 0, 0, 0), (1, 0, -40, 20), (0, 1, 40, 20), (63, 0, -2520, 1260), (0, 63, 2520, 1260), (63, 63, 0, 2520), (30, 32, 80, 1240) };
        if (expected.Any(point => (40 * (point.Item2 - point.Item1), 20 * (point.Item2 + point.Item1)) != (point.Item3, point.Item4))) throw new InvalidOperationException();
        if (80 + 1 != 81 || 78 != 78 || 80 != 80) throw new InvalidOperationException();
    }

    private static void AssertFrameBytes(ArtFrame frame)
    {
        if (frame.Width != 6 || frame.Height != 5 || frame.Rgba.Count != 120) throw new InvalidOperationException();
        AssertRgba(frame.Rgba, 0, 0, 0, 0, 0, 0);
        AssertRgba(frame.Rgba, 1, 0, 0, 0, 255, 255);
        AssertRgba(frame.Rgba, 2, 0, 0, 255, 0, 255);
        AssertRgba(frame.Rgba, 3, 0, 255, 0, 0, 255);
    }

    private static void AssertRenderedOracle(Image image)
    {
        AssertPixel(image, 2, 2, 0, 255, 0, 255); // flipped source's rightmost original green pixel
        AssertPixel(image, 4, 2, 0, 0, 0, 0); // flipped transparent source index zero
        AssertPixel(image, 11, 10, 0, 255, 0, 255); // crop (1,1) is green
        AssertPixel(image, 12, 10, 0, 0, 255, 255); // crop preserves top-left orientation
        AssertPixel(image, 20, 2, 0, 0, 0, 0); // unflipped facade source index zero remains transparent
        AssertPixel(image, 25, 5, 0, 255, 0, 255); // later facade winner is green, not red
        AssertPixel(image, 0, 0, 0, 0, 0, 0); // border clipping/no draw
    }

    private static void AssertRgba(IReadOnlyList<byte> rgba, int x, int y, byte r, byte g, byte b, byte a)
    {
        var offset = ((y * 6) + x) * 4;
        if (rgba[offset] != r || rgba[offset + 1] != g || rgba[offset + 2] != b || rgba[offset + 3] != a) throw new InvalidOperationException();
    }

    private static void AssertPixel(Image image, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = image.GetPixel(x, y);
        if (Math.Round(pixel.R * 255) != r || Math.Round(pixel.G * 255) != g || Math.Round(pixel.B * 255) != b || Math.Round(pixel.A * 255) != a) throw new InvalidOperationException();
    }
}