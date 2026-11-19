using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-UI-2865: a portrait the viewer read draws the HD pack's redraw when the HD setting is on and the pack has an entry for that bitmap; the texture
    // keeps the bitmap's size, a bitmap with other pixels than the pack was built from (or without an entry) and a file the portrait reader does not take
    // draw Legacy, and a file with no portrait path is decoded by Godot as before (a file that is no bitmap at all gives no texture and Godot's error line, as before). A synthetic 8 × 8 bitmap and a 4× redraw; no original pixel.
    private static void AssertPortraitHd()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-2865 portrait HD self-test: " + why); }
        const int Side = 8, Scale = 4;
        static byte[] Bmp(Func<int, int, (byte R, byte G, byte B)> pixel)
        {
            var stride = (Side * 3 + 3) / 4 * 4;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + stride * Side); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(Side); writer.Write(Side); writer.Write((short)1); writer.Write((short)24);
            writer.Write(0); writer.Write(stride * Side); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = Side - 1; y >= 0; y--)
            {
                for (var x = 0; x < Side; x++) { var (r, g, b) = pixel(x, y); writer.Write(b); writer.Write(g); writer.Write(r); }
                writer.Write(new byte[stride - Side * 3]);
            }
            return stream.ToArray();
        }
        var bytes = Bmp((x, y) => ((byte)(x * 20), (byte)(y * 20), 90));
        var decoded = PortraitBitmap.Decode(bytes) ?? throw new InvalidOperationException("ARC-UI-2865 portrait HD self-test: the synthetic bitmap does not decode");
        Require(decoded.Width == Side && decoded.Height == Side, "the bitmap decodes at its size");
        // Godot decodes the same bitmap to the same pixels (the HD key hashes the portrait reader's, the Legacy texture is Godot's before this change).
        var godot = new Image();
        Require(godot.LoadBmpFromBuffer(bytes) == Error.Ok, "Godot reads the synthetic bitmap");
        godot.Convert(Image.Format.Rgba8);
        Require(godot.GetData().AsSpan().SequenceEqual(decoded.Rgba), "the portrait reader and Godot decode a bitmap to the same pixels");

        var hd = new byte[Side * Scale * Side * Scale * 4];
        for (var y = 0; y < Side * Scale; y++)
        for (var x = 0; x < Side * Scale; x++)
        {
            var from = ((y / Scale) * Side + x / Scale) * 4; var to = (y * Side * Scale + x) * 4;
            hd[to] = (byte)(255 - decoded.Rgba[from]); hd[to + 1] = decoded.Rgba[from + 1]; hd[to + 2] = decoded.Rgba[from + 2]; hd[to + 3] = 255;
        }
        var root = System.IO.Directory.CreateTempSubdirectory("arc-ui-2865-selftest-").FullName;
        try
        {
            var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "pack"), "portrait-selftest", Scale, [], DateTimeOffset.UnixEpoch,
                [new("portrait/hum1.bmp", Side, Side, decoded.Rgba, hd)]);
            Require(written.Status == HdInterfaceWriteStatus.Written, "the synthetic pack is written: " + written.Status + " " + written.Detail);
            var pack = HdPack.Open(System.IO.Path.Combine(root, "pack")).Pack ?? throw new InvalidOperationException("ARC-UI-2865 portrait HD self-test: the pack does not open");

            AreaPortraitFile File(string? path, byte[] data, AreaPortraitStatus status = AreaPortraitStatus.Ready) => new(1, status, path, data, "synthetic");
            InterfaceHd.Configure(null, false);
            var texture = PortraitHd.Texture(File("portrait/Hum1.bmp", bytes)) as HdInterfaceTexture;
            Require(texture is not null, "a portrait with a bitmap path is a texture that can draw HD");
            Require(texture!.GetWidth() == Side && texture.GetHeight() == Side && !texture.IsHd, "no pack: the Legacy size and Legacy pixels");
            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd, "a pack alone does not switch the portrait");
            InterfaceHd.Configure(pack, true);
            Require(texture.IsHd && texture.HdScale == Scale, "pack and HD on: the redraw draws");
            Require(texture.GetWidth() == Side && texture.GetHeight() == Side && texture.GetSize() == new Vector2(Side, Side), "the HD portrait keeps the bitmap's size (the draw sites size by it)");
            var other = Bmp((x, y) => ((byte)(x * 20 + 1), (byte)(y * 20), 90));
            Require(PortraitHd.Texture(File("portrait/hum1.bmp", other)) is HdInterfaceTexture { IsHd: false }, "a bitmap with other pixels than the pack was built from draws Legacy");
            Require(PortraitHd.Texture(File("portrait/hum2.bmp", bytes)) is HdInterfaceTexture { IsHd: false }, "a bitmap the pack has no entry for draws Legacy");
            Require(PortraitHd.Texture(File(null, bytes)) is { } plain and not HdInterfaceTexture && plain.GetWidth() == Side, "a file with no portrait path is decoded by Godot as before (a file that is no bitmap at all gives no texture and Godot's error line, as before)");
            Require(PortraitHd.Texture(File("portrait/hum1.bmp", bytes, AreaPortraitStatus.NotFound)) is null && PortraitHd.Texture(null) is null, "a portrait that did not read gives no texture");
            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd, "HD off again: Legacy");
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            System.IO.Directory.Delete(root, recursive: true);
        }
        GD.Print("ARC-UI-2865 self-test portrait HD PASS");
    }
}
