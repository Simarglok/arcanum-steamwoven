using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-282 (PF1, PF2, PF3): the AI host's bow attack over invented rows, no original data: a human NPC at world cell (10, 20) with every basic skill at 8 points, wielding a bow (range 12, arrow
/// ammunition, projectile art 0x20000); the hero stands at (14, 20). At the attack's action frame the session decides the shot, the viewer is asked to fly it, and the host's strike lands its damage.
/// </summary>
[TestClass]
public sealed class AreaAiShotTests
{
    private const int BowPrototype = 6001;
    private static readonly SessionItemKind BowKind = new(SessionItemType.Weapon, BowPrototype, 4100, 4101);
    private static readonly CellCoord HeroCell = new(14, 20), NpcCell = new(10, 20);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A bow (type 5) wielded (location 1004): weight 15, normal damage 2..6, range 12, arrow ammunition (type 0), projectile art 0x20000.
    private static AreaObjectRecord Item(int index, uint weaponFlags = 0) => new(Key(index), 0, 5, BowPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(38, 15), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 1004), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, weaponFlags), Field(61, 0), Field(62, 0), Array(63, 2, 0, 0, 0, 0), Array(64, 6, 0, 0, 0, 0), Array(65, 0, 0, 0, 0, 0),
            Field(66, 10), Field(67, 0), Field(68, 12), Field(69, 0), Field(70, 0), Field(71, 0), Field(72, 0), Field(73, 1), Field(74, 0x20000),
            Field(76, 0), Field(77, 0), Field(78, 0), Field(79, 0), Field(80, 0), Field(81, 0),
        ], true, true, true);

    // A human NPC (type 16, no monster flags) with every primary stat 20, level 1, throwing 5 points, 20 hit points, at world cell (10, 20).
    private static AreaObjectRecord Npc(int index, CellCoord? at = null) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, (at ?? NpcCell).X, (at ?? NpcCell).Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);


    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Bow);

    private static readonly CellCoord BystanderCell = new(12, 20);

    private sealed class Line(AreaObjectRecord? blocker) : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 32 && tile.Y < 32;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => blocker is not null && tile == BystanderCell ? [new(blocker, false, 0)] : [];
    }

    private static Scene Open(Func<string, string, SessionShot, CellCoord, CellCoord, bool>? launch, bool bystander = false)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var rows = bystander ? new[] { Npc(0), Item(1), Npc(3, BystanderCell) } : new[] { Npc(0), Item(1) };
        var map = new AreaInventoryMap(1,
            new(bystander ? [new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)]), new(Entity(3), SessionHolderKind.Npc, false, false, [], [])] : [new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)])],
                [new SessionSeedItem(Entity(1), new(BowKind, 15, 0, 1004, null, []), null)]),
            rows.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = bystander ? [new(Entity(0), Npc(0), NpcCell), new(Entity(3), Npc(3, BystanderCell), BystanderCell)] : [new(Entity(0), Npc(0), NpcCell)], Combat = combat,
            HeroCell = () => HeroCell, LaunchShot = launch,
            LineOfFire = bystander ? () => new Line(Npc(3, BystanderCell)) : null,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms) until the NPC's run ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc)
    {
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 200 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Frames().AdvanceHost(50);
            notices.AddRange(host.DrainNotices());
        }
        return notices;
    }

    [TestMethod]
    public void A_bow_attack_launches_the_arrow_and_the_strike_lands_the_damage()
    {
        var launched = new List<(string Shooter, string Target, SessionShot Shot, CellCoord From, CellCoord To)>();
        var (session, host, npc, _) = Open((who, target, shot, from, to) => { launched.Add((who, target, shot, from, to)); return true; });
        SessionAttackResult? decided = null;
        for (var attempt = 0; attempt < 60 && launched.Count == 0; attempt++)
        {
            host.PerformAttack(npc, AiSchedulerRules.Pc);
            decided = Run(host, npc).FirstOrDefault(n => n.Kind == AreaAiNoticeKind.Hit)?.Result;
        }
        Assert.AreEqual(1, launched.Count, host.TraceOf(npc)?.Hit ?? host.TraceOf(npc)?.Perform);
        Assert.AreEqual((npc, AiSchedulerRules.Pc, NpcCell, HeroCell), (launched[0].Shooter, launched[0].Target, launched[0].From, launched[0].To));
        Assert.AreEqual(1, decided!.Shots.Count);
        Assert.IsNull(decided.Life, "the damage waits for the strike");
        Assert.AreEqual(1, session.QueryShotsInFlight().Count);

        var facts = session.QueryNpcShot(launched[0].Shot)!.Value;
        var before = session.QueryHeroLife()!.Status.HitPoints;
        var landed = host.LandShot(npc, launched[0].Shot, AiSchedulerRules.Pc);
        Assert.IsNotNull(landed);
        if (facts.Hit)
        {
            Assert.IsTrue(landed.Life is { HitPointDamage: > 0 }, "the carried hit lands");
            Assert.IsTrue(session.QueryHeroLife()!.Status.HitPoints < before);
        }
        else Assert.AreEqual(GameSessionFailure.InvalidAttack, landed.Failure, "a missed shot does not strike its own target");
        Assert.AreEqual(0, session.QueryShotsInFlight().Count, "the shot ends with its strike");
        Assert.IsTrue(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Hit) == facts.Hit, "the strike is noticed as a hit");
    }

    [TestMethod]
    public void A_shot_the_viewer_cannot_fly_strikes_the_aimed_target_at_once()
    {
        var (session, host, npc, _) = Open((_, _, _, _, _) => false);
        var hit = false;
        for (var attempt = 0; attempt < 120 && !hit; attempt++)
        {
            host.PerformAttack(npc, AiSchedulerRules.Pc);
            hit = Run(host, npc).Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Result?.Life is { HitPointDamage: > 0 });
        }
        Assert.IsTrue(hit, "the damage landed at once");
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void Without_a_flying_viewer_the_attack_lands_at_once()
    {
        var (session, host, npc, _) = Open(null);
        var hit = false;
        for (var attempt = 0; attempt < 120 && !hit; attempt++)
        {
            host.PerformAttack(npc, AiSchedulerRules.Pc);
            var notices = Run(host, npc);
            Assert.IsTrue(notices.Where(n => n.Kind == AreaAiNoticeKind.Hit).All(n => n.Result!.Shots.Count == 0));
            hit = notices.Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Result?.Life is { HitPointDamage: > 0 });
        }
        Assert.IsTrue(hit);
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void A_critter_in_the_line_of_fire_is_struck_instead_of_the_target()
    {
        var launched = new List<(string Shooter, string Target, SessionShot Shot, CellCoord From, CellCoord To)>();
        var (session, host, npc, _) = Open((who, target, shot, from, to) => { launched.Add((who, target, shot, from, to)); return true; }, bystander: true);
        for (var attempt = 0; attempt < 80 && launched.Count == 0; attempt++)
        {
            host.Hit(npc, AiSchedulerRules.Pc);
            host.DrainNotices();
        }
        Assert.IsTrue(launched.Count > 0, host.TraceOf(npc)?.Hit);
        Assert.AreEqual((npc, Entity(3), NpcCell, BystanderCell), (launched[0].Shooter, launched[0].Target, launched[0].From, launched[0].To),
            "the arrow is shot at the critter that stops the line, not at the hero: " + host.TraceOf(npc)?.Hit);
    }
}
