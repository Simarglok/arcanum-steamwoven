using Arcanum.Data.Characters;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-3106: a town-map tile (64 x 32) the viewer read draws the HD pack's redraw (256 x 128 at a 4x pack) when the HD setting is on and the pack has an entry for that tile; the texture keeps
    // the tile's own size (the grid, the party marker, the fog cell and the clicks stay in the original's coordinates), at a fractional device scale (1080p: 1.8) the Legacy pixels draw while the area copy is
    // made on a worker and then the copy (never a mip chain built per tile on the main thread), a tile with other pixels than the pack was built from (or without an entry) draws Legacy, and a tile the
    // reader does not take or one of another size is decoded by Godot as before. Synthetic tiles and a 4x redraw; no original pixel.
    private static async Task AssertTownMapHdAsync(Node host)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-RENDER-3106 town-map HD self-test: " + why); }
        const int Scale = 4;
        static byte[] Bmp(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
        {
            var stride = (width * 3 + 3) / 4 * 4;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(54 + stride * height); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24);
            writer.Write(0); writer.Write(stride * height); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++) { var (r, g, b) = pixel(x, y); writer.Write(b); writer.Write(g); writer.Write(r); }
                writer.Write(new byte[stride - width * 3]);
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
        var tileBytes = Bmp(64, 32, (x, y) => ((byte)(x * 3), (byte)(y * 7), 90));
        var tile = PortraitBitmap.Decode(tileBytes) ?? throw new InvalidOperationException("ARC-RENDER-3106 town-map HD self-test: the synthetic tile does not decode");
        var godot = new Image();
        Require(godot.LoadBmpFromBuffer(tileBytes) == Error.Ok, "Godot reads the synthetic tile");
        godot.Convert(Image.Format.Rgba8);
        Require(godot.GetData().AsSpan().SequenceEqual(tile.Rgba), "the reader and Godot decode a tile to the same pixels (the original look draws the same pixels as before)");
        const string Path = "townmap/ashbury/ashbury000748.bmp";

        var root = System.IO.Directory.CreateTempSubdirectory("arc-render-3106-selftest-").FullName;
        try
        {
            var written = HdInterfacePackWriter.Write(System.IO.Path.Combine(root, "pack"), "town-selftest", Scale, [], DateTimeOffset.UnixEpoch, townTiles:
                [new(Path, 64, 32, tile.Rgba, () => Redraw(tile))]);
            Require(written.Status == HdInterfaceWriteStatus.Written, "the synthetic pack is written: " + written.Status + " " + written.Detail);
            var pack = HdPack.Open(System.IO.Path.Combine(root, "pack")).Pack ?? throw new InvalidOperationException("ARC-RENDER-3106 town-map HD self-test: the pack does not open");

            InterfaceHd.Configure(null, false);
            var texture = TownMapHd.Texture("townmap/Ashbury/Ashbury000748.bmp", tileBytes) as HdInterfaceTexture
                ?? throw new InvalidOperationException("ARC-RENDER-3106 town-map HD self-test: a tile with a path is no texture that can draw HD");
            Require(texture.GetSize() == new Vector2(64, 32) && !texture.IsHd, "no pack: the Legacy size and Legacy pixels");
            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd, "a pack alone does not switch the map");
            InterfaceHd.Configure(pack, true);
            Require(texture.IsHd && texture.HdScale == Scale && texture.GetSize() == new Vector2(64, 32), "pack and HD on, no device scale claimed: the redraw draws at its own size and the texture keeps 64 x 32");
            // A headless run has no renderer behind its textures (the RIDs are empty there), so the polygon texture's RID is only read in a run with a display.
            var rids = DisplayServer.GetName() != "headless";
            Require(TownMapHd.Check(texture, pack) is (true, "HD"), "the check sees it drawing HD: " + TownMapHd.Check(texture, pack));
            Require(texture.DrawSource() is { } source && (!rids || source.GetRid().IsValid), "a polygon has a texture to take");

            // 1080p (1.8 device pixels per Legacy pixel, 0.45 per tile pixel of the render): the Legacy pixels draw while the copy is made, then the area copy, never the mip chain.
            var device = 1.8f;
            var scaled = (HdInterfaceTexture)TownMapHd.Texture(Path, tileBytes)!;
            scaled.ScaleSource = () => device;
            Require(!scaled.IsHd || scaled.IsAreaResampled, "a copy pending: the Legacy pixels draw, no mip chain is built on the main thread (a copy already there is the only HD that may draw)");
            for (var wait = 0; wait < 900 && !(scaled.IsAreaResampled && InterfaceHd.DirectPending == 0); wait++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var (width, height) = InterfaceResample.PixelSize(64, 32, 1.8);
            Require(scaled.IsHd && scaled.IsAreaResampled && scaled.AreaPixelSize == new Vector2I(width, height) && scaled.GetSize() == new Vector2(64, 32),
                $"the tile draws from the area copy of {width}x{height} device pixels and keeps 64 x 32 ({scaled.AreaPixelSize})");
            var expected = InterfaceResample.ResampleStraight(Redraw(tile), 256, 128, width, height);
            Require(scaled.AreaImageBytes() is { } drawn && drawn.AsSpan().SequenceEqual(expected), "the uploaded pixels are the area resample of the 256 x 128 redraw");
            Require(TownMapHd.Check(scaled, pack) is (true, "HD") && scaled.DrawSource() is { } copySource && (!rids || copySource.GetRid().IsValid), "the checked, drawing tile has a polygon texture: " + TownMapHd.Check(scaled, pack));
            // Whole scales and the pack's own scale and above are made on the worker too (no PNG decode and mip chain per tile on the main thread): 2 gives 128 x 64, 4 gives the redraw's own 256 x 128.
            foreach (var (whole, copyW, copyH) in new[] { (2f, 128, 64), (4f, 256, 128) })
            {
                device = whole;
                for (var wait = 0; wait < 900 && !(scaled.IsAreaResampled && scaled.AreaPixelSize == new Vector2I(copyW, copyH) && InterfaceHd.DirectPending == 0); wait++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(scaled.IsHd && scaled.IsAreaResampled && scaled.AreaPixelSize == new Vector2I(copyW, copyH), $"device scale {whole}: the copy of {copyW}x{copyH} draws ({scaled.AreaPixelSize})");
            }
            Require(InterfaceHd.DirectScaleFirst(4.8, 4) == 4 && InterfaceHd.DirectScaleFirst(1.8, 4) == 1.8 && InterfaceHd.DirectScaleFirst(0, 4) is null && InterfaceHd.DirectScaleFirst(2, 1) is null, "the copy's scale is the device scale, at most the pack's");

            // Another tile's pixels, a tile the pack has no entry for, a bad name, a tile of another size and a tile Godot reads alone each draw as before.
            var other = Bmp(64, 32, (x, y) => ((byte)(x * 3 + 1), (byte)(y * 7), 90));
            Require(TownMapHd.Texture(Path, other) is HdInterfaceTexture { IsHd: false }, "a tile with other pixels than the pack was built from draws Legacy");
            Require(TownMapHd.Texture("townmap/ashbury/ashbury000749.bmp", tileBytes) is HdInterfaceTexture { IsHd: false }, "a tile the pack has no entry for draws Legacy");
            Require(TownMapHd.Texture("townmap/ashbury/odd000748.bmp", tileBytes) is { } named and not HdInterfaceTexture && named.GetWidth() == 64, "a tile whose name has no key is decoded by Godot as before");
            Require(TownMapHd.Texture(null, tileBytes) is { } plain and not HdInterfaceTexture && plain.GetWidth() == 64, "a tile with no path is decoded by Godot as before");
            Require(TownMapHd.Texture(Path, Bmp(128, 64, (x, y) => ((byte)x, (byte)y, 0))) is { } wide and not HdInterfaceTexture && wide.GetSize() == new Vector2(128, 64),
                "a tile of another size (a mod's) has no HD question and keeps its own size, so the layout is the original's");

            // A town that is gone lets its tiles go: a refresh no longer reaches them and they draw Legacy.
            scaled.Release();
            InterfaceHd.Configure(pack, false);
            InterfaceHd.Configure(pack, true);
            Require(!scaled.IsHd, "a released tile draws Legacy");
            InterfaceHd.Configure(pack, false);
            Require(!texture.IsHd, "HD off again: Legacy");
        }
        finally
        {
            InterfaceHd.Configure(null, false);
            System.IO.Directory.Delete(root, recursive: true);
        }
        GD.Print("ARC-RENDER-3106 self-test town-map HD PASS");
    }
}
