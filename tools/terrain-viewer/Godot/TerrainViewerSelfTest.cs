using Godot;

namespace Arcanum.TerrainViewer;

public static class TerrainViewerSelfTest
{
    public static void Run(Node host)
    {
        try
        {
            var image = SyntheticTerrainFixture.CreateImage();
            var rgba = image.GetData();
            if (rgba.Length != 24 || rgba[3] != 0 || rgba[4] != 0 || rgba[5] != 0 || rgba[6] != 255)
                throw new InvalidOperationException();
            var texture = ImageTexture.CreateFromImage(image);
            texture.Dispose();
            GD.Print("ARC-RENDER-003 self-test PASS synthetic RGBA8 row-major fixture");
            host.GetTree().Quit(0);
        }
        catch
        {
            GD.PrintErr("ARC-RENDER-003 self-test FAIL RenderFailure");
            host.GetTree().Quit(1);
        }
    }
}
