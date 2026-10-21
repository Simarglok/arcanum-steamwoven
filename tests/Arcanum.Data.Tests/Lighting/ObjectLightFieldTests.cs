using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// ARC-RENDER-030: object lights joined to the stationary ones by hour (ledger LT4, LT5), and the bucketed field and per-light
// lattice against a node-by-node reference written here. Invented lights and masks; no original data.
[TestClass]
public sealed class ObjectLightFieldTests
{
    private const long Packed = 2080374816;
    private const uint Art7 = 7u << 19 | 9u << 28;

    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var mask = new byte[width * height * 4];
        for (var at = 0; at < mask.Length; at += 4) { mask[at] = r; mask[at + 1] = g; mask[at + 2] = b; mask[at + 3] = 255; }
        return mask;
    }

    private static ObjectLight Object(int index, long x, long y, StationaryLightFlags flags, bool nocturnal, bool outdoor, uint art = Art7) =>
        new(new(new string('a', 64), 79, AreaSourceKind.Static, Packed, index, index, new string((char)('b' + index), 64), 1), 3, -1, x, y, 0, 0, flags, art,
            new(10, 20, 30), false, nocturnal, outdoor);

    [TestMethod]
    public void Night_and_day_differ_only_by_nocturnal_scenery_on_an_outdoor_tile()
    {
        var stationary = AreaStationaryLightSource.Create(5078, [new StationaryLight(Packed, 0, 2060, 2000, 0, 0, 0, Art7, new(1, 2, 3), "s")],
            [(7, 4, 2, 1, 1, Solid(4, 2, 50, 0, 0))]);
        var objects = new AreaObjectLightSource(5078, null, [
            Object(0, 2061, 2001, StationaryLightFlags.Off | StationaryLightFlags.Outdoor, nocturnal: true, outdoor: true),
            Object(1, 2062, 2002, StationaryLightFlags.Off, nocturnal: true, outdoor: false),
            Object(2, 2063, 2003, StationaryLightFlags.Off | StationaryLightFlags.Dark, nocturnal: false, outdoor: true),
        ], new Dictionary<int, StationaryLightFrame>(stationary.Frames));
        var night = stationary.WithObjectLights(objects, 22);
        Assert.AreEqual(4, night.Lights.Count);
        Assert.IsNull(night.Lights[0].Owner);
        // A built nocturnal light is switched on with its object; another object light keeps its saved OFF.
        CollectionAssert.AreEqual(new[] { StationaryLightFlags.Outdoor, StationaryLightFlags.None, StationaryLightFlags.Off | StationaryLightFlags.Dark },
            night.Lights.Skip(1).Select(l => l.Flags).ToArray());
        Assert.AreEqual(objects.Lights[1].Object, night.Lights[2].Owner);
        Assert.AreEqual(-1, night.Lights[2].Index);
        var day = stationary.WithObjectLights(objects, 12);
        CollectionAssert.AreEqual(new[] { 2062L, 2063L }, day.Lights.Skip(1).Select(l => l.X).ToArray(), "the outdoor nocturnal lamp is not built by day");
        Assert.ThrowsExactly<ArgumentException>(() => stationary.WithObjectLights(new AreaObjectLightSource(5000, null, [], new Dictionary<int, StationaryLightFrame>()), 12));
    }

    [TestMethod]
    public void An_object_light_shows_its_saved_frame()
    {
        var later = new StationaryLightFrame(7, "a", "h", 2, 2, 0, 0, Solid(2, 2, 0, 90, 0));
        var first = new StationaryLightFrame(7, "a", "h", 2, 2, 0, 0, Solid(2, 2, 90, 0, 0), later: [later], framesPerSecond: 8);
        var source = AreaStationaryLightSource.Create(1, [], []).WithObjectLights(new AreaObjectLightSource(1, null,
            [Object(0, 0, 0, 0, false, false, Art7 | 1u << 12)], new Dictionary<int, StationaryLightFrame> { [7] = first }), 12);
        var field = new StationaryLightField(source, 0, 0);
        // Tile (0, 0) projects to (0, 0); the 2x2 frame sits at (40, 20). Frame 1 (green 90) tinted by the colour (10, 20, 30).
        Assert.AreEqual(new AmbientColor(0, 90 * 20 / 255, 0), field.Point(40, 20, default, AmbientPair.Neutral));
        var outOfRange = AreaStationaryLightSource.Create(1, [], []).WithObjectLights(new AreaObjectLightSource(1, null,
            [Object(0, 0, 0, 0, false, false, Art7 | 2u << 12)], new Dictionary<int, StationaryLightFrame> { [7] = first }), 12);
        Assert.ThrowsExactly<InvalidOperationException>(() => new StationaryLightField(outOfRange, 0, 0));
    }

    [TestMethod]
    public void An_animated_light_steps_its_frame_and_offsets_with_the_animation_time()
    {
        // ARC-RENDER-031 (ledger LT6, LT7): three frames at 5 per second (a step every 200 ms); stepping to frame 1 moves the
        // light by (4, 0), to frame 2 by (0, 2), back to frame 0 by (-4, -2).
        var f1 = new StationaryLightFrame(7, "a", "h", 2, 2, 0, 0, Solid(2, 2, 0, 90, 0), stepX: 4);
        var f2 = new StationaryLightFrame(7, "a", "h", 2, 2, 0, 0, Solid(2, 2, 0, 0, 90), stepY: 2);
        var f0 = new StationaryLightFrame(7, "a", "h", 2, 2, 0, 0, Solid(2, 2, 90, 0, 0), stepX: -4, stepY: -2, later: [f1, f2], framesPerSecond: 5);
        var source = AreaStationaryLightSource.Create(1, [], []).WithObjectLights(new AreaObjectLightSource(1, null,
            [Object(0, 0, 0, 0, false, false), Object(1, 0, 0, StationaryLightFlags.NoAnimation, false, false)],
            new Dictionary<int, StationaryLightFrame> { [7] = f0 }), 12);
        var field = new StationaryLightField(source, 0, 0);
        CollectionAssert.AreEqual(new[] { 0 }, field.Animated.ToArray(), "0x20 keeps the second light still");
        Assert.AreEqual(new LightPose(0, 0, 0), field.PosesAt(199)[0]);
        Assert.AreEqual(new LightPose(1, 4, 0), field.PosesAt(200)[0]);
        Assert.AreEqual(new LightPose(2, 4, 2), field.PosesAt(400)[0]);
        Assert.AreEqual(new LightPose(0, 0, 0), field.PosesAt(600)[0], "a whole cycle comes back");
        Assert.AreEqual(new LightPose(1, 4, 0), field.PosesAt(600 * 1000 + 200)[0]);
        // Posed, the light draws its frame where it stands: frame 1 at (40 + 4, 20), tinted by the colour (10, 20, 30).
        var poses = field.PosesAt(200);
        Assert.AreEqual(new AmbientColor(0, 90 * 20 / 255, 0), field.Point(44, 20, default, AmbientPair.Neutral, poses: poses));
        Assert.AreEqual(new AmbientColor(90 * 10 / 255, 0, 0), field.Point(40, 20, default, AmbientPair.Neutral, poses: poses), "only the still light is left there");
        Assert.AreEqual(new AmbientColor(90 * 10 / 255 * 2, 0, 0), field.Point(40, 20, default, AmbientPair.Neutral), "unposed: both at frame 0");
        Assert.AreEqual(new AmbientColor(90 * 10 / 255 * 2, 0, 0), field.Point(40, 20, default, AmbientPair.Neutral, poses: field.PosesAt(0)));
        // The lattice follows the pose too: the node (40, 20) loses the moved light.
        var still = field.Lattice(AmbientPair.Neutral)!; var moved = field.Lattice(AmbientPair.Neutral, poses)!;
        Assert.AreEqual(new AmbientColor(90 * 10 / 255 * 2, 0, 0), still.At(1, 1).Lighter);
        Assert.AreEqual(new AmbientColor(90 * 10 / 255, 0, 0), moved.At(1, 1).Lighter);
    }

    [TestMethod]
    public void The_bucketed_field_and_the_per_light_lattice_equal_the_node_by_node_reference()
    {
        var random = new Random(30);
        var frames = new List<(int, int, int, int, int, byte[])>();
        for (var n = 0; n < 4; n++)
        {
            var (w, h) = (random.Next(30, 400), random.Next(20, 300));
            var mask = new byte[w * h * 4];
            for (var at = 0; at < mask.Length; at += 4)
            {
                mask[at] = (byte)random.Next(256); mask[at + 1] = (byte)random.Next(256); mask[at + 2] = (byte)random.Next(256);
                mask[at + 3] = (byte)(random.Next(5) == 0 ? 0 : 255);
            }
            frames.Add((n, w, h, random.Next(w), random.Next(h), mask));
        }
        var lights = Enumerable.Range(0, 120).Select(i => new StationaryLight(Packed, i, 2048 + random.Next(64), 1984 + random.Next(64),
            random.Next(-300, 300), random.Next(-300, 300), (StationaryLightFlags)new[] { 0u, 1u, 2u, 8u, 16u, 18u }[random.Next(6)],
            (uint)random.Next(4) << 19 | 9u << 28, new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)), "r")).ToArray();
        var field = new StationaryLightField(AreaStationaryLightSource.Create(1, lights, frames), 2048, 1984);
        var pair = new AmbientPair(new(200, 180, 160), new(90, 100, 110));
        var placements = field.Placements.Select(p => (p.Light, Frame: frames[p.Light.ArtNumber], p.Rect)).ToArray();

        bool Contribution((StationaryLight Light, (int, int W, int H, int, int, byte[] Mask) Frame, TerrainRect Rect) p, long x, long y, out AmbientColor c)
        {
            c = default;
            if ((p.Light.Flags & StationaryLightFlags.Off) != 0 || x < p.Rect.X || y < p.Rect.Y || x >= p.Rect.X + (long)p.Rect.Width || y >= p.Rect.Y + (long)p.Rect.Height) return false;
            var at = ((int)(y - p.Rect.Y) * p.Frame.W + (int)(x - p.Rect.X)) * 4;
            if (p.Frame.Mask[at + 3] == 0) return false;
            var tint = (p.Light.Flags & StationaryLightFlags.Indoor) != 0 ? pair.Indoor : (p.Light.Flags & StationaryLightFlags.Outdoor) != 0 ? pair.Outdoor : p.Light.Color;
            c = new(p.Frame.Mask[at], p.Frame.Mask[at + 1], p.Frame.Mask[at + 2]);
            if (tint != AmbientColor.Neutral) c = new((byte)(c.R * tint.R / 255), (byte)(c.G * tint.G / 255), (byte)(c.B * tint.B / 255));
            return true;
        }

        int hits = 0, overlaps = 0, negative = 0;
        for (var probe = 0; probe < 400; probe++)
        {
            // Half the probes inside a light's rectangle, half anywhere over the region.
            var inside = placements[random.Next(placements.Length)].Rect;
            long x = probe % 2 == 0 ? inside.X + random.Next(inside.Width) : random.Next(-700, 900);
            long y = probe % 2 == 0 ? inside.Y + random.Next(inside.Height) : random.Next(-500, 700);
            var expected = new AmbientColor(60, 70, 80);
            var here = placements.Count(p => Contribution(p, x, y, out _));
            if (here > 0) { hits++; if (x < 0 || y < 0) negative++; }
            if (here > 1) overlaps++;
            foreach (var p in placements)
                if (Contribution(p, x, y, out var c))
                    expected = (p.Light.Flags & StationaryLightFlags.Dark) != 0
                        ? new((byte)Math.Max(expected.R - c.R, 0), (byte)Math.Max(expected.G - c.G, 0), (byte)Math.Max(expected.B - c.B, 0))
                        : new((byte)Math.Min(expected.R + c.R, 255), (byte)Math.Min(expected.G + c.G, 255), (byte)Math.Min(expected.B + c.B, 255));
            Assert.AreEqual(expected, field.Point(x, y, new(60, 70, 80), pair), $"point ({x},{y})");
        }

        // The probes are not vacuous: lit points, overlapping lights and lit points left of or above the origin.
        Assert.IsTrue(hits > 40 && overlaps > 10 && negative > 5, $"hits {hits}, overlaps {overlaps}, negative {negative}");
        var lattice = field.Lattice(pair)!;
        for (var r = 0; r < lattice.Rows; r++)
            for (var col = 0; col < lattice.Columns; col++)
            {
                int[] plus = new int[3], minus = new int[3];
                long x = (long)(lattice.FirstColumn + col) * 40, y = (long)(lattice.FirstRow + r) * 20;
                foreach (var p in placements)
                    if (Contribution(p, x, y, out var c))
                    {
                        var into = (p.Light.Flags & StationaryLightFlags.Dark) != 0 ? minus : plus;
                        into[0] = Math.Min(into[0] + c.R, 255); into[1] = Math.Min(into[1] + c.G, 255); into[2] = Math.Min(into[2] + c.B, 255);
                    }
                var (lighter, darker) = lattice.At(lattice.FirstColumn + col, lattice.FirstRow + r);
                Assert.AreEqual(new AmbientColor((byte)plus[0], (byte)plus[1], (byte)plus[2]), lighter, $"lighter node {col},{r}");
                Assert.AreEqual(new AmbientColor((byte)minus[0], (byte)minus[1], (byte)minus[2]), darker, $"darker node {col},{r}");
            }
    }
}
