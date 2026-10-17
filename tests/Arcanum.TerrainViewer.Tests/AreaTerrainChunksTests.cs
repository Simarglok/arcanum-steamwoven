using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaTerrainChunksTests
{
    // An independent CPU raster of the exact nearest rule (AreaNearestAxis per item,
    // binary alpha, painter order): drawing the terrain as chunks must give the same
    // pixels as drawing every terrain command, at every zoom and origin a chunk level
    // is used for.
    [TestMethod]
    public void ChunkRasterEqualsPerCommandRasterAtEveryAdmittedLevel()
    {
        var random = new Random(9); var (commands, frames) = Terrain(random, 220);
        var chunks = AreaTerrainChunks.TryCreate(commands, frames);
        Assert.IsNotNull(chunks);
        var cases = new List<(float Zoom, float OriginX, float OriginY, int Level)>
        {
            (1, 640, 400, 0), (2, 640, 400, 0), (1.2651956f, 640, 400, 0), (.8f, 640, 400, 0), (1, 640.5f, 399.25f, 0), (.5f, 333.75f, 201.5f, 0),
            (.5f, 640, 400, 1), (.25f, 640, 400, 2), (.125f, 640, 400, 3), (.5f, 641, 399, 1), (.25f, 639, 401, 2), (.125f, 642, 397, 3),
        };
        foreach (var (zoom, ox, oy, level) in cases)
        {
            const int width = 1280, height = 800;
            var expected = Raster(width, height, zoom, ox, oy, commands.Select(c => (c, frames[c.Frame].Width, frames[c.Frame].Rgba)));
            var drawn = chunks!.Intersecting((0 - ox) / zoom, (0 - oy) / zoom, width / zoom, height / zoom)
                .Select(chunk => (AreaTerrainChunks.Command(chunk, level), AreaTerrainChunks.Side(level), chunks.Compose(chunk, level)));
            var actual = Raster(width, height, zoom, ox, oy, drawn);
            var opaque = expected.Where((value, index) => index % 4 == 3 && value == 255).Count();
            Assert.IsTrue(opaque > 1000, $"zoom {zoom} level {level}: the view must show terrain");
            CollectionAssert.AreEqual(expected, actual, $"zoom {zoom} origin ({ox},{oy}) level {level}");
        }
    }

    [TestMethod]
    public void UnsupportedTerrainDisablesChunksInsteadOfApproximating()
    {
        var (commands, frames) = Terrain(new Random(3), 20);
        Assert.IsNotNull(AreaTerrainChunks.TryCreate(commands, frames));
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands.Select((c, i) => i == 5 ? c with { Alpha = 254 } : c).ToArray(), frames));
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands.Select((c, i) => i == 5 ? c with { Additive = true } : c).ToArray(), frames));
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands.Select((c, i) => i == 5 ? c with { FlipY = true } : c).ToArray(), frames));
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands.Select((c, i) => i == 5 ? c with { Phase = 2 } : c).ToArray(), frames));
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands.Select((c, i) => i == 5 ? c with { Destination = new TerrainRect(c.Destination.X, c.Destination.Y, c.Destination.Width + 1, c.Destination.Height) } : c).ToArray(), frames));
        var body = commands[0] with { Identity = new AreaObjectKey("s", 1, AreaSourceKind.Static, 1, 0, 0, "b", 1), Phase = 4 };
        Assert.IsNull(AreaTerrainChunks.TryCreate([body, .. commands], frames));
        var partial = frames.ToDictionary(pair => pair.Key, pair => pair.Value);
        var key = commands[7].Frame; var rgba = (byte[])partial[key].Rgba.Clone();
        var crop = commands[7].Source; rgba[((crop.Y + 1) * partial[key].Width + crop.X + 1) * 4 + 3] = 128;
        partial[key] = partial[key] with { Rgba = rgba };
        Assert.IsNull(AreaTerrainChunks.TryCreate(commands, partial));
    }

    // Terrain-like commands: 78x40 tiles and 80x40 facades, cropped to frames that may be
    // larger, X-mirrored tiles, overlaps, transparent texels with non-zero RGB, negative
    // and chunk-crossing destinations.
    internal static (AreaDrawCommand[] Commands, Dictionary<AreaFrameKey, DecodedAreaFrame> Frames) Terrain(Random random, int count)
    {
        var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame>(); var keys = new List<AreaFrameKey>();
        for (var f = 0; f < 12; f++)
        {
            var width = 80 + random.Next(0, 24); var height = 40 + random.Next(0, 12); var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = (byte)random.Next(256); rgba[i * 4 + 1] = (byte)random.Next(256); rgba[i * 4 + 2] = (byte)random.Next(256);
                rgba[i * 4 + 3] = random.Next(3) == 0 ? (byte)0 : (byte)255;
            }
            var key = new AreaFrameKey(new(new(AreaSourceRole.Resource2), f, 0, 1, 1, 0, "n"), 0, 0, f, "p", "s" + f, 1, "r" + f);
            frames.Add(key, new(width, height, rgba)); keys.Add(key);
        }
        var commands = new List<AreaDrawCommand>();
        for (var i = 0; i < count; i++)
        {
            var facade = random.Next(3) == 0; var key = keys[random.Next(keys.Count)]; var frame = frames[key];
            var w = Math.Min(facade ? 80 : 78, frame.Width); var h = Math.Min(40, frame.Height);
            // Non-zero crop origins inside larger frames as well as the plan's (0,0) crops.
            var cx = random.Next(2) == 0 ? 0 : random.Next(0, frame.Width - w + 1); var cy = random.Next(2) == 0 ? 0 : random.Next(0, frame.Height - h + 1);
            var x = random.Next(-700, 1400); var y = random.Next(-500, 1100);
            commands.Add(new(default, key, null, new TerrainRect(cx, cy, w, h), new TerrainRect(x, y, w, h),
                !facade && random.Next(2) == 0, false, facade ? 1 : 0, 0, 0, 0, 0, 255, false));
        }
        return (commands.OrderBy(c => c.Phase).ToArray(), frames);
    }

    private static byte[] Raster(int width, int height, float zoom, float originX, float originY,
        IEnumerable<(AreaDrawCommand Command, int TextureWidth, byte[] Rgba)> items)
    {
        var image = new byte[width * height * 4];
        foreach (var (command, textureWidth, rgba) in items)
        {
            var d = command.Destination; var s = command.Source;
            var x = new AreaNearestAxis(originX, zoom, d.X, d.Width, s.X, s.Width, command.FlipX);
            var y = new AreaNearestAxis(originY, zoom, d.Y, d.Height, s.Y, s.Height, command.FlipY);
            var columns = Enumerable.Range(0, width).Select(x.SourceIndex).ToArray();
            for (var py = 0; py < height; py++)
            {
                var sy = y.SourceIndex(py); if (sy < 0) continue;
                for (var px = 0; px < width; px++)
                {
                    var sx = columns[px]; if (sx < 0) continue;
                    var at = (sy * textureWidth + sx) * 4;
                    if (rgba[at + 3] != 255) continue;
                    var to = (py * width + px) * 4;
                    image[to] = rgba[at]; image[to + 1] = rgba[at + 1]; image[to + 2] = rgba[at + 2]; image[to + 3] = 255;
                }
            }
        }
        return image;
    }
}
