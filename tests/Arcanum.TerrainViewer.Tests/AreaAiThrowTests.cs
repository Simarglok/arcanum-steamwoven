using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-113 (NPC AI slice I10, the throw goal 37): the AI host's throw over invented rows, no original data: a human NPC (0) at
/// world cell (10, 20) with every primary stat 20 and 5 points of throwing, wielding a throwing dagger (1: weapon flag 0x200
/// "default throws", weight 15, damage 2..6) in its weapon slot; the hero stands at (14, 20). The attack starter takes the dagger for
/// a throw goal at the hero's tile, the stand-in throw animation plays and at its action frame the session decides the throw, lands the
/// dagger and the goal ends.
/// </summary>
[TestClass]
public sealed class AreaAiThrowTests
{
    private const int DaggerPrototype = 6072;
    private static readonly SessionItemKind Dagger = new(SessionItemType.Weapon, DaggerPrototype, 4100, 4101);
    private static readonly CellCoord HeroCell = new(14, 20), NpcCell = new(10, 20);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A weapon (type 5) wielded (location 1004): weapon flags 0x200 (default throws), weight 15, normal damage 2..6, range 1, no
    // ammunition, no projectile art.
    private static AreaObjectRecord Item(int index, uint weaponFlags = 0x200) => new(Key(index), 0, 5, DaggerPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(38, 15), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 1004), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, weaponFlags), Field(61, 0), Field(62, 0), Array(63, 2, 0, 0, 0, 0), Array(64, 6, 0, 0, 0, 0), Array(65, 0, 0, 0, 0, 0),
            Field(66, 10), Field(67, 0), Field(68, 1), Field(69, 0), Field(70, 0), Field(71, 0), Field(72, 10000), Field(73, 0), Field(74, -1),
            Field(76, 0), Field(77, 0), Field(78, 0), Field(79, 0), Field(80, 0), Field(81, 0),
        ], true, true, true);

    // A human NPC (type 16, no monster flags) with every primary stat 20, level 1, throwing 5 points, 20 hit points, at world cell (10, 20).
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, NpcCell.X, NpcCell.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Item);

    private static Scene Open(uint weaponFlags = 0x200)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var rows = new[] { Npc(0), Item(1, weaponFlags) };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)])], [new SessionSeedItem(Entity(1), new(Dagger, 15, 0, 1004, null, []), null)]),
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
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0), NpcCell)], Combat = combat,
            HeroCell = () => HeroCell,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms) until the NPC's run ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc)
    {
        var notices = new List<AreaAiNotice>();
        var now = 0L;
        for (var frame = 0; frame < 200 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Pump(now += 50);
            notices.AddRange(host.DrainNotices());
        }
        Assert.IsNull(host.RunOf(npc), "the throw run ended");
        return notices;
    }

    [TestMethod]
    public void The_attack_starter_throws_the_default_throws_dagger_at_the_heros_tile_and_it_lands()
    {
        var (session, host, npc, dagger) = Open();
        Assert.AreEqual(dagger, host.ThrowableWeapon(npc), "the wielded dagger default-throws");
        var started = session.Version;
        host.PerformAttack(npc, AiSchedulerRules.Pc);
        var run = host.RunOf(npc) ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual(AiGoalType.ThrowItem, run.Bottom.Goal, "the attack starter starts the throw goal 37 (TW1)");
        Assert.AreEqual((dagger, (CellCoord?)HeroCell), (run.Bottom.Item, run.Bottom.Tile), "with the item and the target's location");
        Assert.AreEqual(3, host.AnimationPriority(npc), "goal 37's priority");
        Assert.AreEqual(started, session.Version, "the unwield changes nothing until the throw (TA1: a lost object otherwise)");
        Assert.AreEqual(dagger, host.ThrowableWeapon(npc), "the dagger stays wielded until the throw");

        var thrown = Run(host, npc).Single(n => n.Kind == AreaAiNoticeKind.Throw);
        var result = thrown.Throw ?? throw new AssertFailedException(host.TraceOf(npc)?.Throw ?? "no throw");
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure), host.TraceOf(npc)?.Throw);
        Assert.AreEqual((dagger, AiSchedulerRules.Pc), (thrown.Item, thrown.Target), "the hero on the target tile is struck (TW4)");
        var attack = result.Attack ?? throw new AssertFailedException("no attack on the hero");
        Assert.AreEqual(AttackRefusal.None, attack.Resolution!.Refusal);
        var landed = result.Landed ?? throw new AssertFailedException("the dagger did not land");
        if (attack.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit) Assert.AreEqual(HeroCell, landed, "a hit lands on the struck tile");
        else Assert.IsTrue(Math.Max(Math.Abs(landed.X - HeroCell.X), Math.Abs(landed.Y - HeroCell.Y)) <= 4, "a miss scatters at most the distance");
        Assert.IsNull(host.ThrowableWeapon(npc), "the dagger left the NPC");
        Assert.IsTrue(host.DrainNotices().Count == 0 && host.TraceOf(npc)!.Throw!.StartsWith("throw ", StringComparison.Ordinal));
        Assert.AreEqual(AiExecutorReason.Thrown, host.TraceOf(npc)!.Reason, "the goal ends through the end step after the throw");
    }

    [TestMethod]
    public void A_weapon_without_the_default_throws_flag_is_not_thrown()
    {
        var (_, host, npc, _) = Open(weaponFlags: 0);
        Assert.IsNull(host.ThrowableWeapon(npc));
        host.PerformAttack(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(AiGoalType.Attack, (host.RunOf(npc) ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}")).Bottom.Goal,
            "an attack, not a throw");
    }

    [TestMethod]
    public void A_throw_of_an_item_the_npc_does_not_hold_fails()
    {
        var (session, host, npc, _) = Open();
        var before = session.Version;
        Assert.IsFalse(host.ThrowItem(npc, Entity(9), HeroCell), "an item the NPC does not hold (AG10)");
        Assert.IsFalse(host.ThrowItem(Entity(9), Entity(1), HeroCell), "a thrower the host does not walk");
        Assert.AreEqual(before, session.Version);
    }

    [TestMethod]
    public void A_throw_at_an_empty_tile_lands_the_dagger_there_unchecked()
    {
        var (session, host, npc, dagger) = Open();
        var draws = session.QueryRandom().Draws;
        var tile = new CellCoord(12, 22);
        Assert.IsTrue(host.ThrowItem(npc, dagger, tile));
        var thrown = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        Assert.AreEqual(((string?)null, (SessionAttackResult?)null, (CellCoord?)tile), (thrown.Target, thrown.Throw!.Attack, thrown.Throw.Landed));
        Assert.AreEqual(draws, session.QueryRandom().Draws, "nothing is rolled without a struck critter");
        Assert.IsNull(host.ThrowableWeapon(npc));
    }
}
