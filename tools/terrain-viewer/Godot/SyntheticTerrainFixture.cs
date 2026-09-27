using Godot;

namespace Arcanum.TerrainViewer;

public static class SyntheticTerrainFixture
{
    public static byte[] AsymmetricRgba() =>
    [
        0, 0, 0, 0,       0, 0, 255, 255, 0, 255, 0, 255,
        255, 0, 0, 255,   255, 255, 255, 255, 0, 0, 0, 255
    ];

    public static Image CreateImage() => Image.CreateFromData(3, 2, false, Image.Format.Rgba8, AsymmetricRgba());
}
