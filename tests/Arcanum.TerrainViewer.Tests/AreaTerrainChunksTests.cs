using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    // ARC-RENDER-3438: a chunk with terrain on two light channels is drawn as one texture per channel. Each texel of the
    // whole chunk is in exactly the layer of its winning command's channel, so lighting a layer lights what the
    // per-command draws would light.
    [TestMethod]
    public void ChannelLayersPartitionTheChunkByItsWinningCommand()
    {
        var (commands, frames) = Terrain(new Random(11), 220);
        commands = commands.Select((c, i) => c with { Visual = new AreaVisualKey(AreaSourceKind.Static, default, i, "v") }).ToArray();
        var channels = commands.Select((c, i) => (c.Visual, Channel: i % 2 == 0 ? AreaAmbientChannel.Outdoor : AreaAmbientChannel.Indoor))
            .ToDictionary(pair => pair.Visual, pair => pair.Channel);
        var chunks = AreaTerrainChunks.TryCreate(commands, frames)!;
        var split = 0;
        for (var level = 0; level <= AreaTerrainChunks.MaxLevel; level++)
        foreach (var chunk in chunks.Chunks)
        {
            var present = chunks.ChannelsIn(chunk, channels);
            CollectionAssert.AreEqual(chunks.CommandsIn(chunk).Select(c => channels[c.Visual]).Distinct().Order().ToArray(), present);
            if (present.Length > 1) split++;
            var whole = chunks.Compose(chunk, level);
            var union = new byte[whole.Length];
            foreach (var channel in present)
            {
                var layer = chunks.Compose(chunk, level, c => channels[c.Visual] == channel);
                // The expected layer, from each texel's winning command found separately.
                var owner = Owners(chunks, chunk, level, frames, channels);
                for (var at = 0; at < whole.Length; at += 4)
                {
                    var expected = owner[at / 4] == channel ? whole.AsSpan(at, 4) : new byte[4];
                    Assert.IsTrue(layer.AsSpan(at, 4).SequenceEqual(expected), $"chunk {chunk} level {level} {channel} texel {at / 4}");
                    if (layer[at + 3] == 0) continue;
                    Assert.AreEqual(0, union[at + 3], "a texel is in one layer only");
                    layer.AsSpan(at, 4).CopyTo(union.AsSpan(at, 4));
                }
            }
            CollectionAssert.AreEqual(whole, union, $"chunk {chunk} level {level}");
        }
        Assert.IsTrue(split > 8, "the fixture must mix channels inside chunks");
        Assert.ThrowsExactly<InvalidOperationException>(() => chunks.ChannelsIn(chunks.Chunks.First(), new Dictionary<AreaVisualKey, AreaAmbientChannel>()));
    }

    // The channel of each kept texel's last opaque command, found texel by texel (independent of Compose's clipping).
    private static AreaAmbientChannel?[] Owners(AreaTerrainChunks chunks, (int X, int Y) chunk, int level,
        Dictionary<AreaFrameKey, DecodedAreaFrame> frames, Dictionary<AreaVisualKey, AreaAmbientChannel> channels)
    {
        var side = AreaTerrainChunks.Side(level); var step = 1 << level; var phase = level == 0 ? 0 : step >> 1;
        var owners = new AreaAmbientChannel?[side * side];
        foreach (var command in chunks.CommandsIn(chunk))
        {
            var d = command.Destination; var s = command.Source; var frame = frames[command.Frame];
            for (var y = d.Y; y < d.Y + d.Height; y++)
            for (var x = d.X; x < d.X + d.Width; x++)
            {
                var lx = x - chunk.X * AreaTerrainChunks.ChunkSize - phase; var ly = y - chunk.Y * AreaTerrainChunks.ChunkSize - phase;
                if (lx < 0 || ly < 0 || lx % step != 0 || ly % step != 0 || lx / step >= side || ly / step >= side) continue;
                var sx = command.FlipX ? s.X + s.Width - 1 - (x - d.X) : s.X + (x - d.X);
                if (frame.Rgba[((s.Y + y - d.Y) * frame.Width + sx) * 4 + 3] == 255) owners[ly / step * side + lx / step] = channels[command.Visual];
            }
        }
        return owners;
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
