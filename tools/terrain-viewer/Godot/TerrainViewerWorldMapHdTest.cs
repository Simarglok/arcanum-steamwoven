using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-3079: a world-map tile (250 x 250) and the continent picture (365 x 365) the viewer read draw the HD pack's redraw when the HD setting is on and the pack has an entry for
    // that bitmap; the texture keeps the bitmap's own size (so the draw rectangles, dots, hover boxes and clicks stay in the original's coordinates), a bitmap with other pixels than the
    // pack was built from (or without an entry) draws Legacy, and a bitmap with no path or one the reader does not take is decoded by Godot as before. Synthetic bitmaps and a 2x redraw; no original pixel.
    private static void AssertWorldMapHd()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: " + why); }
        const int Scale = 2;
        static byte[] Bmp(int side, Func<int, int, (byte R, byte G, byte B)> pixel)
        {
            var stride = (side * 3 + 3) / 4 * 4;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + stride * side); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(side); writer.Write(side); writer.Write((short)1); writer.Write((short)24);
            writer.Write(0); writer.Write(stride * side); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = side - 1; y >= 0; y--)
            {
                for (var x = 0; x < side; x++) { var (r, g, b) = pixel(x, y); writer.Write(b); writer.Write(g); writer.Write(r); }
                writer.Write(new byte[stride - side * 3]);
            }
            return stream.ToArray();
        }
        static byte[] Redraw(PortraitBitmapImage legacy)
        {
            var hd = new byte[legacy.Width * Scale * legacy.Height * Scale * 4];
            for (var y = 0; y < legacy.Height * Scale; y++)
            for (var x = 0; x < legacy.Width * Scale; x++)
            {
                var from = ((y / Scale) * legacy.Width + x / Scale) * 4; var to = (y * legacy.Width * Scale + x) * 4;
                hd[to] = (byte)(255 - legacy.Rgba[from]); hd[to + 1] = legacy.Rgba[from + 1]; hd[to + 2] = legacy.Rgba[from + 2]; hd[to + 3] = 255;
            }
            return hd;
        }
        var tileBytes = Bmp(250, (x, y) => ((byte)(x % 250), (byte)(y % 250), 90));
        var zoomedBytes = Bmp(365, (x, y) => ((byte)(y % 250), (byte)(x % 250), 40));
        var tile = PortraitBitmap.Decode(tileBytes) ?? throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: the synthetic tile does not decode");
        var zoomed = PortraitBitmap.Decode(zoomedBytes) ?? throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: the synthetic continent picture does not decode");
        var godot = new Image();
        Require(godot.LoadBmpFromBuffer(tileBytes) == Error.Ok, "Godot reads the synthetic tile");
        godot.Convert(Image.Format.Rgba8);
        Require(godot.GetData().AsSpan().SequenceEqual(tile.Rgba), "the reader and Godot decode a tile to the same pixels (the original look draws the same pixels as before)");

        var root = System.IO.Directory.CreateTempSubdirectory("arc-render-3079-selftest-").FullName;
        try
        {
            var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "pack"), "map-selftest", Scale, [], DateTimeOffset.UnixEpoch, maps:
                [new(HdEntryKind.WorldMap, "worldmap/smallmapchunks001.bmp", 250, 250, tile.Rgba, Redraw(tile)), new(HdEntryKind.MapZoomed, "worldmap/map_zoomed.bmp", 365, 365, zoomed.Rgba, Redraw(zoomed))]);
            Require(written.Status == HdInterfaceWriteStatus.Written, "the synthetic pack is written: " + written.Status + " " + written.Detail);
            var pack = HdPack.Open(System.IO.Path.Combine(root, "pack")).Pack ?? throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: the pack does not open");

            InterfaceHd.Configure(null, false);
            var tileTexture = WorldMapHd.Texture("worldmap/SmallMapChunks001.bmp", tileBytes) as HdInterfaceTexture
                ?? throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: a tile with a path is no texture that can draw HD");
            var zoomedTexture = WorldMapHd.Texture("worldmap/map_zoomed.bmp", zoomedBytes) as HdInterfaceTexture
                ?? throw new InvalidOperationException("ARC-RENDER-3079 world-map HD self-test: the continent picture with a path is no texture that can draw HD");
            Require(tileTexture.GetSize() == new Vector2(250, 250) && zoomedTexture.GetSize() == new Vector2(365, 365) && !tileTexture.IsHd && !zoomedTexture.IsHd, "no pack: the Legacy size and Legacy pixels");
            InterfaceHd.Configure(pack, false);
            Require(!tileTexture.IsHd && !zoomedTexture.IsHd, "a pack alone does not switch the map");
            InterfaceHd.Configure(pack, true);
            Require(tileTexture.IsHd && tileTexture.HdScale == Scale && zoomedTexture.IsHd && zoomedTexture.HdScale == Scale, "pack and HD on: the redraws draw");
            Require(tileTexture.GetSize() == new Vector2(250, 250) && zoomedTexture.GetSize() == new Vector2(365, 365),
                "the HD tile and the HD continent picture keep the bitmaps' sizes (the draws are into the original rectangles, 365 x 365 for Map_Zoomed)");
            Require(WorldMapHd.Check(tileTexture, pack, 250) is (true, "HD") && WorldMapHd.Check(zoomedTexture, pack, 365) is (true, "HD"), "the check sees both drawing HD");
            Require(!WorldMapHd.Check(tileTexture, pack, 365).Ok, "the check refuses a texture of another size");
            var other = Bmp(250, (x, y) => ((byte)(x % 250 + 1), (byte)(y % 250), 90));
            Require(WorldMapHd.Texture("worldmap/smallmapchunks001.bmp", other) is HdInterfaceTexture { IsHd: false }, "a bitmap with other pixels than the pack was built from draws Legacy");
            Require(WorldMapHd.Texture("worldmap/smallmapchunks002.bmp", tileBytes) is HdInterfaceTexture { IsHd: false }, "a bitmap the pack has no entry for draws Legacy");
            Require(WorldMapHd.Texture("worldmap/odd name.bmp", tileBytes) is { } named and not HdInterfaceTexture && named.GetWidth() == 250, "a bitmap whose name has no key is decoded by Godot as before");
            Require(WorldMapHd.Texture(null, tileBytes) is { } plain and not HdInterfaceTexture && plain.GetWidth() == 250, "a bitmap with no path is decoded by Godot as before");
            Require(WorldMapHd.Texture("worldmap/smallmapchunks001.bmp", Bmp(100, (x, y) => ((byte)x, (byte)y, 0))) is { } small and not HdInterfaceTexture && small.GetWidth() == 100, "a bitmap of another size has no HD question");
            InterfaceHd.Configure(pack, false);
            Require(!tileTexture.IsHd && !zoomedTexture.IsHd, "HD off again: Legacy");
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            System.IO.Directory.Delete(root, recursive: true);
        }
        GD.Print("ARC-RENDER-3079 self-test world-map HD PASS");
    }
}
