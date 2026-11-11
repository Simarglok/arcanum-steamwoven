using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-RENDER-012. Hand-computed anchors and colours on the exterior chart (origin cell 92928,82560 at
// screen 0,0); no production helper supplies expectations.
[TestClass]
public sealed class AreaLocalLightingTests
{
    private const string Digest = AreaCompositionFixture.Digest;
    private static readonly AmbientPair Pair = new(new(10, 20, 30), new(50, 60, 70));

    private static AreaTerrainRecord Tile(long x, long y, uint raw, int index)
    {
        var key = new AreaVisualKey(AreaSourceKind.Terrain, new(Digest, 5051, 100), index, "");
        return new(key, 0, 0, x, y, raw, 0, AreaScope.Core, key);
    }
    // Indoor tile (class bit 0) at the origin cell, outdoor (bit 8) one cell east.
    private static readonly AreaTerrainRecord[] Tiles = [Tile(92928, 82560, 0, 0), Tile(92929, 82560, 0x100, 1)];

    private static (int, int, int, int, int, byte[]) Uniform(int number, int width, int height, int hotX, int hotY, byte r)
    {
        var mask = new byte[width * height * 4];
        for (var at = 0; at < mask.Length; at += 4) { mask[at] = r; mask[at + 3] = 255; }
        return (number, width, height, hotX, hotY, mask);
    }
    private static StationaryLight Light(long x, long y, int ox, int oy, int number = 7, int index = 0)
        => new(1, index, x, y, ox, oy, StationaryLightFlags.None, (uint)number << 19 | 9u << 28, AmbientColor.Neutral, Digest);

    private static AreaLocalLighting Local(IEnumerable<AreaVisualRecord> visuals, params StationaryLight[] lights)
        => AreaLocalLighting.Create(Tiles, visuals, AreaChart.Exterior,
            AreaStationaryLightSource.Create(5000, lights, [Uniform(7, 10, 10, 0, 0, 100), Uniform(8, 400, 400, 200, 200, 100)]));

    [TestMethod]
    public void Carried_light_is_added_after_the_sector_lights_to_bodies_terrain_and_the_actor()
    {
        // ARC-DEV-335 (V7d, ledger LT10, LT12): the critter's light stands where the critter stands, never dark, ART 1 in the size's colour.
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        var local = Local([visual]);
        var frame = AreaStationaryLightSource.Create(5000, [], [Uniform(1, 400, 400, 200, 200, 100)]).Frames[1];
        var plain = local.Body(command, AreaAmbientChannel.Outdoor, Pair).Left;
        // The hero stands on the body's cell: a large light (255, 214, 172) over a red-only mask adds 100 red.
        var hero = new CarriedLight(92928, 82560, 0, 0, CarriedLightSize.Large);
        var lit = local.WithCarried(frame, [hero]);
        Assert.AreEqual(1, lit.CarriedLightCount);
        Assert.AreEqual(0, local.CarriedLightCount, "the map's own lighting is untouched");
        var expected = new AmbientColor((byte)Math.Min(plain.R + 100, 255), plain.G, plain.B);
        Assert.AreEqual(expected, lit.Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        // Adding it to the cached view's colour gives the same as lighting the body with it.
        Assert.AreEqual(lit.Body(command, AreaAmbientChannel.Outdoor, Pair), lit.AddCarried(command, AreaAmbientChannel.Outdoor, Pair, local.Body(command, AreaAmbientChannel.Outdoor, Pair)));
        Assert.AreEqual(local.Body(command, AreaAmbientChannel.Outdoor, Pair), local.AddCarried(command, AreaAmbientChannel.Outdoor, Pair, local.Body(command, AreaAmbientChannel.Outdoor, Pair)), "no carried light: the basis");
        Assert.AreEqual(local.Body(command, AreaAmbientChannel.Unlit, Pair), lit.AddCarried(command, AreaAmbientChannel.Unlit, Pair, AreaLitColors.Neutral), "an unlit art stays unlit");
        // Another hero away from the body (more than the mask's reach) gives it nothing; a light of another size has its own colour.
        Assert.AreEqual(plain, local.WithCarried(frame, [hero with { X = 92928 + 40 }]).Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        // The terrain: a lattice of the carried lights alone, over their rectangles.
        var lattice = lit.CarriedLattice(Pair) ?? throw new AssertFailedException("a lattice");
        Assert.IsNull(local.CarriedLattice(Pair));
        Assert.IsTrue(lattice.Columns > 0 && lattice.Rows > 0);
        Assert.IsTrue(lattice.Lighter.Any(b => b != 0 && b != 255), "the lighter sums hold the light");
        Assert.IsTrue(lattice.Darker.Where((b, i) => i % 4 != 3).All(b => b == 0), "never dark");
        // The same light twice adds twice (a second critter), and none gives the lighting back.
        Assert.AreEqual(2, local.WithCarried(frame, [hero, hero with { X = 92929 }]).CarriedLightCount);
        Assert.AreEqual(0, lit.WithCarried(frame, []).CarriedLightCount);
        // The merged terrain colour: the base lattice's sums and the carried ones add before the node composes.
        var ambient = new AmbientColor(10, 20, 30);
        var (r, g, b2) = StationaryLightField.TerrainPixel(null, lattice, ambient, 40 * lattice.FirstColumn + 40, 20 * lattice.FirstRow + 20);
        var (nr, ng, nb) = StationaryLightField.TerrainPixel(null, null, ambient, 40 * lattice.FirstColumn + 40, 20 * lattice.FirstRow + 20);
        Assert.IsTrue(r > nr && g >= ng && b2 >= nb, "the carried light brightens the terrain at its node");
        // The merged reference is the lattice's own when nothing is carried.
        Assert.AreEqual(StationaryLightField.TerrainPixel(lattice, ambient, 40 * lattice.FirstColumn + 20, 20 * lattice.FirstRow + 10),
            StationaryLightField.TerrainPixel(lattice, null, ambient, 40 * lattice.FirstColumn + 20, 20 * lattice.FirstRow + 10));
        // A second lattice adds to the first one's lighter sum before the node composes (saturating).
        var twice = StationaryLightField.TerrainPixel(lattice, lattice, ambient, 40 * lattice.FirstColumn + 40, 20 * lattice.FirstRow + 20);
        Assert.IsTrue(twice.R >= r, "two lattices give at least one");
    }

    [TestMethod]
    public void A_critters_light_is_carried_not_fixed_and_follows_the_critters_pose()
    {
        // ARC-DEV-353 (V7d second part, ledger LT10, LT12): an NPC's saved light moves with it; the sector's fixed lights leave it out.
        var stationary = AreaStationaryLightSource.Create(5000, [], [Uniform(8, 400, 400, 200, 200, 100)]);
        ObjectLight Light(int index, int type, long x) => new(new(Digest, 5000, AreaSourceKind.Static, 100, index, index, Digest, 1), type, -1,
            x, 82560, 3, 4, StationaryLightFlags.None, 8u << 19 | 9u << 28, new(10, 20, 30), false, false, false);
        var npc = Light(0, 16, 92928);
        var lamp = Light(1, 3, 92929);
        var objects = new AreaObjectLightSource(5000, null, [npc, lamp], new Dictionary<int, StationaryLightFrame>(stationary.Frames));
        var local = AreaLocalLighting.Create(Tiles, [], AreaChart.Exterior, stationary, objects);
        Assert.AreEqual(1, local.ObjectLightCount, "only the scenery lamp is a fixed light");
        Assert.AreEqual(1, local.CritterLights.Count);
        Assert.AreEqual(npc, local.CritterLights.Single());
        Assert.IsTrue(local.CritterFrames.ContainsKey(8));
        Assert.AreEqual(1, local.AtHour(22).CritterLights.Count);
        Assert.AreEqual(1, local.AtAnimation(0).CritterLights.Count, "a posed view keeps them");

        // At its T0 place the light is as the sector load built it.
        var home = AreaCritterLights.Place(npc, null);
        Assert.AreEqual((92928L, 82560L, 3, 4), (home.X, home.Y, home.OffsetX, home.OffsetY));
        Assert.AreEqual(npc.Object, home.Owner);
        // Two cells east and a quarter step on: the screen shift is the projection of the cell difference plus what remains in the offsets.
        var shift = Arcanum.Data.Terrain.TerrainGeometry.Project(2, 0);
        var moved = AreaCritterLights.Place(npc, new AreaCritterPose(npc.Object, 92930, 82560, shift.X + 10, shift.Y + 5));
        Assert.AreEqual((92930L, 82560L, 3 + 10, 4 + 5), (moved.X, moved.Y, moved.OffsetX, moved.OffsetY));
        // The screen point of the light is the T0 point plus the shift, whichever way it is split into cell and offsets.
        (long X, long Y) Screen(StationaryLight light)
        {
            var tile = Arcanum.Data.Terrain.TerrainGeometry.Project((int)(light.X - 92928), (int)(light.Y - 82560));
            return (tile.X + light.OffsetX, tile.Y + light.OffsetY);
        }
        Assert.AreEqual((Screen(home).X + shift.X + 10, Screen(home).Y + shift.Y + 5), Screen(moved));
        Assert.ThrowsExactly<ArgumentException>(() => AreaCritterLights.Place(lamp, null));

        // The carried view holds it where it stands: the body at the critter's cell is lit, the one far away is not.
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        var lit = AreaLocalLighting.Create(Tiles, [visual], AreaChart.Exterior, stationary, objects);
        var plain = lit.Body(command, AreaAmbientChannel.Outdoor, Pair).Left;
        Assert.AreNotEqual(plain, lit.WithCarried(local.CritterFrames, [home]).Body(command, AreaAmbientChannel.Outdoor, Pair).Left, "at its place it lights the body");
        var away = AreaCritterLights.Place(npc, new AreaCritterPose(npc.Object, 92928 + 40, 82560, Arcanum.Data.Terrain.TerrainGeometry.Project(40, 0).X, Arcanum.Data.Terrain.TerrainGeometry.Project(40, 0).Y));
        Assert.AreEqual(plain, lit.WithCarried(local.CritterFrames, [away]).Body(command, AreaAmbientChannel.Outdoor, Pair).Left, "moved away it does not");
    }

    [TestMethod]
    public void Object_lights_join_by_hour_and_nocturnal_scenery_lights_only_at_night()
    {
        // ARC-RENDER-030 (ledger LT4, LT5): a nocturnal lamp on the outdoor tile is built only at night; another object light always.
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        ObjectLight Lamp(int index, long x, bool nocturnal, bool outdoor) => new(new(Digest, 5000, AreaSourceKind.Static, 100, index, index, Digest, 1), 3, -1,
            x, 82560, 0, 0, StationaryLightFlags.Off, 8u << 19 | 9u << 28, AmbientColor.Neutral, false, nocturnal, outdoor);
        var stationary = AreaStationaryLightSource.Create(5000, [], [Uniform(8, 400, 400, 200, 200, 100)]);
        var objects = new AreaObjectLightSource(5000, null, [Lamp(0, 92929, true, true)], new Dictionary<int, StationaryLightFrame>(stationary.Frames));
        var local = AreaLocalLighting.Create(Tiles, [visual], AreaChart.Exterior, stationary, objects);
        Assert.AreEqual(1, local.AtHour(22).ObjectLightCount);
        Assert.AreEqual(0, local.AtHour(12).ObjectLightCount);
        Assert.AreSame(local, local.AtHour(5));
        Assert.AreNotSame(local, local.AtHour(6));
        Assert.AreNotSame(local, local.AtHour(17));
        Assert.AreSame(local, local.AtHour(18));
        Assert.AreEqual(new AmbientColor(150, 60, 70), local.AtHour(22).Body(command, AreaAmbientChannel.Outdoor, Pair).Left, "switched on: OFF cleared");
        Assert.AreEqual(new AmbientColor(50, 60, 70), local.AtHour(12).Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        // Without a light that differs by day both hours are the same lighting; a saved OFF on another object light stays off.
        var plain = AreaLocalLighting.Create(Tiles, [visual], AreaChart.Exterior, stationary,
            new AreaObjectLightSource(5000, null, [Lamp(0, 92929, false, true)], new Dictionary<int, StationaryLightFrame>(stationary.Frames)));
        Assert.AreSame(plain, plain.AtHour(12));
        Assert.AreEqual(new AmbientColor(50, 60, 70), plain.Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => local.AtHour(24));
    }

    [TestMethod]
    public void An_animated_light_poses_the_view_and_names_its_state()
    {
        // ARC-RENDER-031: two frames at 10 per second; the key changes with the frame, the view stays the same object without
        // animation.
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var still = Local([visual], Light(92928, 82560, 0, 0, 8));
        Assert.AreSame(still, still.AtAnimation(1000));
        Assert.AreEqual("", still.AnimationKey(1000));
        var mask = Uniform(8, 400, 400, 200, 200, 100).Item6;
        var later = new StationaryLightFrame(8, "a", "h", 400, 400, 200, 200, mask, stepX: 1000);
        var first = new StationaryLightFrame(8, "a", "h", 400, 400, 200, 200, mask, stepX: -1000, later: [later], framesPerSecond: 10);
        var objects = new AreaObjectLightSource(5000, null, [new ObjectLight(new(Digest, 5000, AreaSourceKind.Static, 100, 0, 0, Digest, 1), 3, -1,
            92928, 82560, 0, 0, 0, 8u << 19 | 9u << 28, AmbientColor.Neutral, false, false, false)], new Dictionary<int, StationaryLightFrame> { [8] = first });
        var animated = AreaLocalLighting.Create(Tiles, [visual], AreaChart.Exterior, AreaStationaryLightSource.Create(5000, [], []), objects);
        Assert.AreNotEqual(animated.AnimationKey(0), animated.AnimationKey(100));
        Assert.AreEqual(animated.AnimationKey(0), animated.AnimationKey(200), "two steps make a cycle");
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        Assert.AreEqual(new AmbientColor(150, 60, 70), animated.AtAnimation(0).Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        // At frame 1 the light stands 1000 pixels away: the body keeps its tile's ambient.
        Assert.AreEqual(new AmbientColor(50, 60, 70), animated.AtAnimation(100).Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
    }

    [TestMethod]
    public void Body_takes_the_tile_class_ambient_plus_the_lights_at_its_anchor()
    {
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        // Anchor (0 + 0 + 40, 0 + 0 + 20); the 400x400 light at the origin cell covers (−160..239, −180..219).
        var local = Local([visual], Light(92928, 82560, 0, 0, 8));
        Assert.AreEqual(new AreaLitColors(new(150, 60, 70), new(150, 60, 70)), local.Body(command, AreaAmbientChannel.Outdoor, Pair),
            "indoor tile (50,60,70) + light (100,0,0); the ART channel only decides lit/unlit");
        Assert.AreEqual(AreaLitColors.Neutral, local.Body(command, AreaAmbientChannel.Unlit, Pair));
        // The same body one cell east: outdoor tile, anchor (0,40) still inside the light.
        Assert.AreEqual(new AmbientColor(110, 20, 30), local.Body(command with { MapX = 92929 }, AreaAmbientChannel.Outdoor, Pair).Left);
        // Without lights the body is its tile's plain ambient.
        Assert.AreEqual(new AmbientColor(50, 60, 70), Local([visual]).Body(command, AreaAmbientChannel.Outdoor, Pair).Left);
        // A cell without a core tile is refused, never defaulted.
        Assert.ThrowsExactly<InvalidOperationException>(() => local.Body(command with { MapY = 82561 }, AreaAmbientChannel.Outdoor, Pair));
        Assert.ThrowsExactly<ArgumentException>(() => local.Body(command with { Identity = null }, AreaAmbientChannel.Outdoor, Pair));
    }

    [TestMethod]
    public void Actor_is_coloured_at_its_placement_anchor()
    {
        var local = Local([], Light(92928, 82560, -5, -25));   // 10x10 rectangle (35,-5): covers (40,0) only
        var at = AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]) with { MapX = 0, MapY = 0, InstanceOffsetX = 0, InstanceOffsetY = -20 };
        Assert.AreEqual(new AmbientColor(150, 60, 70), local.Actor(at, Pair), "anchor (40, 0)");
        Assert.AreEqual(new AmbientColor(50, 60, 70), local.Actor(at with { InstanceOffsetY = 0 }, Pair), "anchor (40, 20) is outside");
        Assert.AreEqual(new AmbientColor(10, 20, 30), local.Actor(at with { MapX = 1, InstanceOffsetY = 0 }, Pair), "outdoor tile east");
    }

    private static AreaVisualRecord Wall(int rotation) => AreaCompositionFixture.Visual(AreaCompositionFixture.Body(0), 80, 100, 0, 0)
        with { RawArt = 0x10000000u | (uint)rotation << 11 };

    [TestMethod]
    public void Wall_samples_its_base_line_ends_from_front_side_lights_only()
    {
        var body = AreaCompositionFixture.Body(0);
        var wall = Wall(1);   // odd rotation folds to 0: base line from tile point (40,0), one y per two x
        var command = AreaScenePlan.Create([], [body], [AreaCompositionFixture.Visual(body, 80, 100, 0, 0)]).Commands.Single()
            with { Visual = wall.Key, Destination = new TerrainRect(40, -60, 80, 100) };
        // Left end (40,0); right end (40 + 79, 0 + 79 / 2) = (119,39). The 10x10 light at (35,-5) covers only the left end.
        var front = Light(92928, 82560, -5, -25);
        // Same rectangle from the cell west of the wall (tile screen (40,-20)): behind the wall for rotation 0.
        var behind = Light(92927, 82560, -45, -5, index: 1);
        Assert.AreEqual(new AreaLitColors(new(150, 60, 70), new(50, 60, 70)), Local([wall], front).Body(command, AreaAmbientChannel.Outdoor, Pair));
        Assert.IsTrue(Local([wall], front).Body(command, AreaAmbientChannel.Outdoor, Pair).Gradient);
        Assert.AreEqual(new AreaLitColors(new(50, 60, 70), new(50, 60, 70)), Local([wall], behind).Body(command, AreaAmbientChannel.Outdoor, Pair));
        // An odd width rounds up to even: the right end of a 79-wide sprite is still (119,39), one pixel past the
        // sprite, and a light there counts (the source samples the base-line band, not the sprite rectangle).
        var odd = command with { Destination = new TerrainRect(40, -60, 79, 100) };
        Assert.AreEqual(new AreaLitColors(new(50, 60, 70), new(150, 60, 70)), Local([wall], Light(92928, 82560, 75, 15)).Body(odd, AreaAmbientChannel.Outdoor, Pair));
        // Without lights (or with lit: false) both ends are the base colour.
        Assert.AreEqual(new AreaLitColors(new(50, 60, 70), new(50, 60, 70)), Local([wall], front).Body(command, AreaAmbientChannel.Outdoor, Pair, lit: false));
        // Rotations 2 and 4 take the outdoor colour whatever the tile.
        var south = Wall(2);
        Assert.AreEqual(new AreaLitColors(new(10, 20, 30), new(10, 20, 30)),
            Local([south]).Body(command with { Visual = south.Key }, AreaAmbientChannel.Outdoor, Pair));
    }

    [TestMethod]
    public void View_colours_every_body_and_builds_the_lattice_only_with_active_lights()
    {
        var body = AreaCompositionFixture.Body(0);
        var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
        var plan = AreaScenePlan.Create([], [body], [visual]);
        var channels = AreaAmbientPolicy.Channels(plan, [], [visual]);
        var lit = Local([visual], Light(92928, 82560, 0, 0, 8)).View(plan, channels, Pair);
        Assert.AreEqual(1, lit.Bodies.Count);
        Assert.IsNotNull(lit.Lattice);
        var dark = Local([visual]).View(plan, channels, Pair);
        Assert.IsNull(dark.Lattice);
        Assert.AreEqual(new AmbientColor(50, 60, 70), dark.Bodies[visual.Key].Left);
    }

    private static string Rows(Func<int, string> row) => string.Concat(Enumerable.Range(0, 24).Select(h => $"{{{h}}}{{{row(h)}}}\n"));
    private static LightSchemeTable Table(int id, Func<int, string> row)
    {
        Assert.AreEqual(LightingFailure.None, LightSchemeParser.TryParse(id, Encoding.ASCII.GetBytes(Rows(row)), out var table));
        return table!;
    }
    private static readonly string Sha = new('b', 64);
    private static readonly long Exterior = new AreaSectorCoordinate(1452, 1290).Packed, Cave = new AreaSectorCoordinate(32, 31).Packed;

    [TestMethod]
    public void Driver_rebinds_to_another_map_source_on_the_same_clock()
    {
        var startup = new StartupClockDeclaration(1885, 15, Sha);
        var exterior = new AreaLightingSource(Sha, 5000, 1, Sha, new Dictionary<long, int> { [Exterior] = 0 },
            new Dictionary<int, LightSchemeTable> { [1] = Table(1, h => $"{h},0,0,0,0,0") }, startup);
        var cave = new AreaLightingSource(Sha, 5078, 2, Sha, new Dictionary<long, int> { [Cave] = 0 },
            new Dictionary<int, LightSchemeTable> { [2] = Table(2, h => $"0,{h},0,0,0,{h}") }, startup);
        var created = AreaTransitionCoordinator.CreateSession(AreaAmbientDriver.ClockProfile(exterior),
            Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), new(100, 100), 1);
        using var session = created.Owner!;
        var driver = AreaAmbientDriver.Create(exterior, session, Exterior);
        driver.AdvanceHours(2, Exterior);
        var clock = driver.Clock; var revision = driver.Revision;
        driver.Rebind(cave, Cave);
        Assert.AreEqual(clock, driver.Clock, "the clock is not touched");
        Assert.AreEqual(new AmbientSelection(2, true, Cave, 17, new(new(0, 17, 0), new(0, 0, 17))), driver.Selection);
        Assert.AreEqual(revision + 1, driver.Revision, "revision stays monotonic across sources");
        Assert.AreSame(cave, driver.Source);
        Assert.AreEqual(new SessionLightingState(2, 17), session.QueryLighting(), "the session records the cave's scheme");
        Assert.IsTrue(driver.AdvanceHours(1, Cave));
        Assert.AreEqual(new AmbientPair(new(0, 18, 0), new(0, 0, 18)), driver.Selection.Pair);
        // A sector of the other map is refused under the current source.
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Apply(Exterior));
        driver.Rebind(exterior, Exterior);
        Assert.AreEqual(new AmbientSelection(1, true, Exterior, 18, new(new(18, 0, 0), new(0, 0, 0))), driver.Selection);
        // Another module or startup is a different clock: refused, the current source stays.
        var foreign = new AreaLightingSource(Sha, 5078, 2, Sha, new Dictionary<long, int> { [Cave] = 0 },
            new Dictionary<int, LightSchemeTable> { [2] = Table(2, h => $"0,{h},0,0,0,{h}") }, new StartupClockDeclaration(1885, 14, Sha));
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Rebind(foreign, Cave));
        Assert.ThrowsExactly<InvalidOperationException>(() => driver.Rebind(cave, Exterior), "sector not admitted by the new source");
        Assert.AreEqual(1, driver.Selection.SchemeId);
    }
}
