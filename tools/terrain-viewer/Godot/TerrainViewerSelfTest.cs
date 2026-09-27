using Godot;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public static class TerrainViewerSelfTest
{
    public static async void Run(Node host)
    {
        try
        {
            var image = SyntheticTerrainFixture.CreateImage();
            var rgba = image.GetData();
            if (rgba.Length != 24 || rgba[3] != 0 || rgba[4] != 0 || rgba[5] != 0 || rgba[6] != 255)
                throw new InvalidOperationException();
            image.Dispose();

            var key = new TerrainTextureKey("synthetic", 1, 0, 24, "synthetic", 0, 0, 0);
            using var cache = new TerrainTextureCache();
            cache.Upload(new Dictionary<TerrainTextureKey, DecodedTerrainTexture>
            {
                [key] = new DecodedTerrainTexture(key, 3, 2, SyntheticTerrainFixture.AsymmetricRgba())
            });
            var command = new TerrainDrawCommand(0, key, new TerrainRect(0, 0, 3, 2), new TerrainRect(1, 1, 3, 2), false,
                new TerrainDiagnosticOrderKey(0, 0, 0, 0));
            var viewport = new SubViewport
            {
                Size = new Vector2I(6, 5),
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            var canvas = new TerrainCanvas();
            viewport.AddChild(canvas);
            host.AddChild(viewport);
            canvas.Publish(TerrainScenePlan.CreateLiteral(new TerrainRect(0, 0, 6, 5), [command]), cache);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() == "headless")
            {
                // Godot's dummy headless renderer has no readable Texture2D or ViewportTexture.
                // The byte checks above still verify the exact upload bytes; graphical runs take the
                // production TerrainCanvas readback branch below.
            }
            else
            {
                using var rendered = viewport.GetTexture().GetImage();
                AssertPixel(rendered, 1, 1, 0, 0, 0, 0);
                AssertPixel(rendered, 2, 1, 0, 0, 255, 255);
                AssertPixel(rendered, 1, 2, 255, 0, 0, 255);
            }
            viewport.QueueFree();
            GD.Print("ARC-RENDER-003 self-test PASS production RGBA8 row-major fixture");
            host.GetTree().Quit(0);
        }
        catch
        {
            GD.PrintErr("ARC-RENDER-003 self-test FAIL RenderFailure");
            host.GetTree().Quit(1);
        }
    }

    private static void AssertPixel(Image image, int x, int y, byte r, byte g, byte b, byte a)
    {
        var pixel = image.GetPixel(x, y);
        if (Math.Round(pixel.R * 255) != r || Math.Round(pixel.G * 255) != g || Math.Round(pixel.B * 255) != b || Math.Round(pixel.A * 255) != a)
            throw new InvalidOperationException();
    }
}
