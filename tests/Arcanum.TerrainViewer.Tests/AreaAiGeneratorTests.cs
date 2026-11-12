using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-1157 (E2-a, ledger PO1 to PO6, SZ6): the AI host's spawner (<see cref="AreaAiHost.Spawn"/>) over a synthetic generator NPC: the rate's delay is the answer whatever was made, the session makes the copies
/// as created critters on the generator's map, the tile class and the view decide the spots, and an object that is no generator is refused. Invented sectors and rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiGeneratorTests
{
    private const int Map = 1;
    private static readonly CellCoord HeroCell = new(58, 12), GeneratorCell = new(62, 10);
    private const string Profile = "arc-data-019-whole-map-original-rules-v1";
    private const int Day = GeneratorSettings.ByDay | GeneratorSettings.ByNight;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), Map, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // The synthetic NPC of the rebind fixture with its flag word and generator dword replaced.
    private static AreaObjectRecord Row(int index, uint npcFlags, uint packed)
    {
        var plain = AreaAiHostRebindTests.NpcWith(Key(index), GeneratorCell, (1, 1));
        var fields = plain.EffectiveFields.Where(f => f.Slot is not (69 or 90)).Concat(
        [
            new AreaFieldValue(69, AreaPresence.Value, AreaValueOrigin.Instance, npcFlags, [], null),
            new AreaFieldValue(90, AreaPresence.Value, AreaValueOrigin.Instance, packed, [], null),
        ]).OrderBy(f => f.Slot).ToArray();
        return plain with { EffectiveFields = fields };
    }

    private static AreaStepSnapshot Sector(Func<long, long, uint> art)
    {
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++)
            for (var y = 0; y < 64; y++)
                terrain[new(x, y)] = new(new(x, y), art(x, y), CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        return new(new(AreaStepInputKind.Structural, Profile, [], "sector-0-0"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
    }

    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to; at = Step(at, steps[^1])!.Value) steps.Add(Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y))));
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => cell == GeneratorCell;
    }

    private sealed class Screen(AreaPixelRect? window, AreaPixelRect? body) : IAreaRestockScreen
    {
        public int? ShownMap => Map;
        public AreaPixelRect? GameWindow => window;
        public (double Width, double Height) WindowSize => (800, 400);
        public AreaPixelRect? BodyRect(AreaObjectKey key) => body;
    }

    private static (GameSessionOwner Session, AreaAiHost Host) Open(AreaObjectRecord row, Func<long, long, uint>? art = null, IAreaRestockScreen? screen = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(Map), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var map = new AreaInventoryMap(Map, new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [])], []), new Dictionary<string, AreaObjectRecord> { [Entity(0)] = row });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        var scene = new AreaAiScene
        {
            RuntimeMap = Map, Chart = new AreaChart(0, 0, new(0, 0), "TEST") { MinimumCell = -64, MaximumCell = 127 }, Steps = Sector(art ?? ((_, _) => 0)),
            Npcs = [new AreaAiNpc(Entity(0), row, GeneratorCell)], Combat = combat, HeroCell = () => HeroCell,
            DropMap = () => new OpenMap(), Screen = screen is null ? null : () => screen,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(Map, () => [], _ => null));
        return (session, host);
    }

    private static uint Packed(int options, int slot, int cap, int budget) => new GeneratorSettings(options, slot, cap, budget, 0).Pack();
    private static uint Flags(int rate) => 0x80000u | ((uint)rate << 21);

    [TestMethod]
    public void A_generator_tick_makes_a_created_critter_near_it_and_answers_the_rates_delay()
    {
        var (session, host) = Open(Row(0, Flags(rate: 2), Packed(Day, 4, 3, 5)));
        Assert.IsTrue(host.IsGenerator(Entity(0)));
        Assert.AreEqual(60_000L, host.Spawn(Entity(0)), "rate 2 is a minute of the game clock");
        var made = session.QueryCreatedObjects(new(Map)).Single();
        Assert.AreEqual((SessionCreatedKind.Critter, 28359), (made.Kind, made.Prototype), "the copy is of the generator's prototype");
        Assert.IsTrue(Math.Abs(made.Cell.X - GeneratorCell.X) <= 5 && Math.Abs(made.Cell.Y - GeneratorCell.Y) <= 5);
        Assert.AreEqual((1, false), session.QueryGeneratorSlot(4));
        Assert.AreEqual(1, session.QueryGeneratorMade(Entity(0)));
        // A full slot makes nothing and still answers the delay.
        Assert.AreEqual(60_000L, host.Spawn(Entity(0)));
        Assert.AreEqual(60_000L, host.Spawn(Entity(0)));
        Assert.AreEqual(60_000L, host.Spawn(Entity(0)));
        Assert.AreEqual(3, session.QueryCreatedObjects(new(Map)).Count, "the cap of three");
    }

    [TestMethod]
    public void An_npc_that_is_no_generator_is_refused()
    {
        var (session, host) = Open(Row(0, 0x0, Packed(Day, 4, 3, 5)));
        Assert.IsFalse(host.IsGenerator(Entity(0)));
        Assert.IsNull(host.Spawn(Entity(0)));
        Assert.IsNull(host.Spawn("oid:nobody"));
        Assert.AreEqual(0, session.QueryCreatedObjects(new(Map)).Count);
    }

    [TestMethod]
    public void The_tile_class_keeps_the_spots_on_the_generators_kind_of_ground()
    {
        // Only the generator's own tile has the class bit (bit 8 of a plain tile's art word): no spot of another class counts, so the copy stands on the generator's tile (a try that stays there counts, or the ten tries fail).
        var (session, host) = Open(Row(0, Flags(0), Packed(Day, 4, 3, 5)), (x, y) => x == GeneratorCell.X && y == GeneratorCell.Y ? 0x100u : 0u);
        var draws = session.QueryRandom().Draws;
        Assert.AreEqual(1_000L, host.Spawn(Entity(0)));
        Assert.AreEqual(GeneratorCell, session.QueryCreatedObjects(new(Map)).Single().Cell);
        var drawn = session.QueryRandom().Draws - draws;
        Assert.IsTrue(drawn is >= 2 and <= 20 && drawn % 2 == 0, $"tries of a distance and a direction, at most ten ({drawn} draws): a try that stays on the generator's own tile (direction 8, no path) is of its class");
        // Art type 11 reads bit 25; any other type reads none.
        var (otherSession, otherHost) = Open(Row(0, Flags(0), Packed(Day, 4, 3, 5)), (x, y) => x == GeneratorCell.X && y == GeneratorCell.Y ? 0xB2000000u : 0xB0000000u);
        otherHost.Spawn(Entity(0));
        Assert.AreEqual(GeneratorCell, otherSession.QueryCreatedObjects(new(Map)).Single().Cell);
        var (plainSession, plainHost) = Open(Row(0, Flags(0), Packed(Day, 4, 3, 5)), (x, y) => x == GeneratorCell.X && y == GeneratorCell.Y ? 0xF2000100u : 0u);
        plainHost.Spawn(Entity(0));
        Assert.AreNotEqual(0, plainSession.QueryCreatedObjects(new(Map)).Count, "a tile of another art type has class 0, like the rest");
    }

    [TestMethod]
    public void The_off_screen_option_waits_for_the_generator_to_leave_the_view_and_keeps_the_spots_off_it()
    {
        var packed = Packed(Day | GeneratorSettings.OffScreen, 4, 3, 5);
        var window = new AreaPixelRect(0, 0, 800, 400);
        // The generator's rectangle meets the window: nothing is made, the delay is still the answer.
        var (shown, shownHost) = Open(Row(0, Flags(0), packed), screen: new Screen(window, new AreaPixelRect(100, 100, 60, 80)));
        Assert.AreEqual(1_000L, shownHost.Spawn(Entity(0)));
        Assert.AreEqual(0, shown.QueryCreatedObjects(new(Map)).Count);
        // Off the view (right of it): the copy is made at a spot whose rectangle moved there is off the view too; a window that covers every spot leaves the generator's own tile.
        var (hidden, hiddenHost) = Open(Row(0, Flags(0), packed), screen: new Screen(window, new AreaPixelRect(5000, 100, 60, 80)));
        Assert.AreEqual(1_000L, hiddenHost.Spawn(Entity(0)));
        Assert.AreEqual(1, hidden.QueryCreatedObjects(new(Map)).Count);
        var (everywhere, everywhereHost) = Open(Row(0, Flags(0), packed), screen: new Screen(new AreaPixelRect(-100000, -100000, 200000, 200000), new AreaPixelRect(5000, 100, 60, 80)));
        Assert.AreEqual(1_000L, everywhereHost.Spawn(Entity(0)));
        Assert.AreEqual(0, everywhere.QueryCreatedObjects(new(Map)).Count, "the window meets the generator's rectangle");
        // Without a screen nothing is on the view.
        var (none, noneHost) = Open(Row(0, Flags(0), packed));
        noneHost.Spawn(Entity(0));
        Assert.AreEqual(1, none.QueryCreatedObjects(new(Map)).Count);
    }
}
