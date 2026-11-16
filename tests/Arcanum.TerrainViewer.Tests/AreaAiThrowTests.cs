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
using Arcanum.Simulation.Goals;

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
    private static AreaObjectRecord Item(int index, uint weaponFlags = 0x200, long weight = 15) => new(Key(index), 0, 5, DaggerPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(38, weight), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 1004), Field(44, 0x60000000), Field(45, 0),
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

    // ARC-DEV-258 (MIG13 S14n): a grenade, a generic item (type 14) the NPC carries in its list (no inventory location).
    private static readonly SessionItemKind GrenadeKind = new(SessionItemType.Generic, 5640, 4102, 4103);
    private static AreaObjectRecord Grenade(int index, long genericFlags = 0x1) => new(Key(index), 0, 14, 5640, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(38, 3), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, genericFlags), Field(60, 0), Field(61, 1),
        ], true, true, true);

    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Item);

    private static Scene Open(uint weaponFlags = 0x200, bool grenade = false, long grenadeFlags = 0x1, Func<string, SessionShot, CellCoord, CellCoord, bool>? launch = null, ulong seed = 0, long weight = 15,
        ISessionItemScriptHost? itemScripts = null, int[]? daggerScripts = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero) { RandomSeed = seed });
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var rows = grenade ? new[] { Npc(0), Item(1, weaponFlags, weight), Grenade(2, grenadeFlags) } : new[] { Npc(0), Item(1, weaponFlags, weight) };
        var map = new AreaInventoryMap(1,
            new([new(Entity(0), SessionHolderKind.Npc, false, false, [], grenade ? [Entity(1), Entity(2)] : [Entity(1)])],
                grenade ? [new SessionSeedItem(Entity(1), new(Dagger, 15, 0, 1004, null, daggerScripts ?? []), null), new SessionSeedItem(Entity(2), new(GrenadeKind, 3, 0, 0, null, []), null)]
                    : [new SessionSeedItem(Entity(1), new(Dagger, 15, 0, 1004, null, daggerScripts ?? []), null)]),
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
            HeroCell = () => HeroCell, LaunchThrown = launch,
            NpcSheet = entity => combat.Fields(entity) is { } fields ? AreaAdvancement.Npc(entity, fields, _ => null) : null,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null, itemScripts: () => itemScripts), new AreaCombatWorld(1, () => [], _ => null));
        return new(session, host, Entity(0), Entity(1));
    }

    // Steps the host frame by frame (50 ms) until the NPC's run ends; the notices it gave.
    private static List<AreaAiNotice> Run(AreaAiHost host, string npc)
    {
        var notices = new List<AreaAiNotice>();
        var now = 0L;
        for (var frame = 0; frame < 200 && (frame == 0 || host.RunOf(npc) is not null); frame++)
        {
            host.Frames().AdvanceHost(50); now += 50;
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
        Assert.AreEqual(GoalType.ThrowItem, run.Bottom.Goal, "the attack starter starts the throw goal 37 (TW1)");
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

    // ARC-SCRIPT-2397 (IV21, IV22): a switched-off default-throws weapon is refused by the throw node, so the starter attacks with it instead of repeating a throw that always fails.
    [TestMethod]
    public void A_switched_off_default_throws_dagger_is_not_a_throw_but_an_attack()
    {
        var (session, host, npc, dagger) = Open();
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, [new(session.QueryActor().Map, SessionItemOff.Key(dagger), 1)]).Disposition);
        Assert.IsNull(host.ThrowableWeapon(npc));
        host.PerformAttack(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(GoalType.Attack, (host.RunOf(npc) ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}")).Bottom.Goal, "an attack, not a throw");
    }

    [TestMethod]
    public void A_weapon_without_the_default_throws_flag_is_not_thrown()
    {
        var (_, host, npc, _) = Open(weaponFlags: 0);
        Assert.IsNull(host.ThrowableWeapon(npc));
        host.PerformAttack(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(GoalType.Attack, (host.RunOf(npc) ?? throw new AssertFailedException($"no run: {host.TraceOf(npc)?.Perform}")).Bottom.Goal,
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

    [TestMethod]
    public void A_thrown_grenade_flies_as_a_shot_the_viewer_launches_and_lands()
    {
        var launched = new List<(string Thrower, SessionShot Shot, CellCoord From, CellCoord To)>();
        var (session, host, npc, _) = Open(grenade: true, launch: (who, shot, from, to) => { launched.Add((who, shot, from, to)); return true; });
        var grenade = Entity(2);
        var draws = session.QueryRandom().Draws;
        var tile = new CellCoord(14, 20);
        Assert.IsTrue(host.ThrowItem(npc, grenade, tile), host.TraceOf(npc)?.Throw);
        var thrown = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        var shot = thrown.Throw!.Shots.Single();
        Assert.AreEqual((npc, shot, NpcCell, tile), launched.Single(), "the viewer is asked to fly the shot from the thrower's tile to the location");
        Assert.AreEqual((SessionAttackResult?)null, thrown.Throw.Attack, "no attack: the dispatcher's check is not ready for a thrown generic item");
        Assert.AreEqual(draws, session.QueryRandom().Draws);
        Assert.IsTrue(session.QueryItemInFlight(grenade), "the grenade flies, hidden");
        // ARC-SCRIPT-2386 (IV15, 0x4B3C00 sets the off flag, 0x4B2690 clears it): the flight is the flag, with no record written.
        Assert.IsTrue(session.QueryItemOff(session.QueryActor().Map, grenade, dataOff: false), "a thrown item in flight is switched off");
        Assert.IsNull(session.QueryObjectChange(session.QueryActor().Map, SessionItemOff.Key(grenade)));
        Assert.IsTrue(host.TraceOf(npc)!.Throw!.Contains("flies shot=", StringComparison.Ordinal), host.TraceOf(npc)!.Throw);
        // The viewer lands it where its flight ends.
        var landing = new CellCoord(13, 20);
        Assert.AreEqual(GameSessionDisposition.Changed, session.LandThrownItem(session.Version, shot, landing).Disposition);
        Assert.IsFalse(session.QueryItemInFlight(grenade));
        Assert.IsFalse(session.QueryItemOff(session.QueryActor().Map, grenade, dataOff: false), "the landing clears it");
    }

    // ARC-COMBAT-1761: the scene's item script host reaches the session's throw. The dagger's throw script (point 6) prints a line and answers skip default, so nothing is thrown, the dagger stays with the NPC and the line is in the notice.
    private sealed class ThrowScriptHost : ISessionItemScriptHost
    {
        private const int ScriptNumber = 710;
        private readonly Arcanum.Data.Scripts.ScrProgram _refuse = Build(
            (Arcanum.Data.Scripts.ScrCatalogue.Action.PrintLine, [(3, 1), (3, 6)]), (Arcanum.Data.Scripts.ScrCatalogue.Action.ReturnSkipDefault, []));

        // Lines of (action type, operands (kind, value)): value kind 3 is a number.
        private static Arcanum.Data.Scripts.ScrProgram Build(params (int Type, (byte Kind, int Value)[] Operands)[] lines)
        {
            var bytes = new byte[64 + lines.Length * 132];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
            for (var line = 0; line < lines.Length; line++)
            {
                var slot = bytes.AsSpan(64 + line * 132 + 44, 44);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(slot, lines[line].Type);
                for (var i = 0; i < lines[line].Operands.Length; i++)
                {
                    slot[4 + i] = lines[line].Operands[i].Kind;
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], lines[line].Operands[i].Value);
                }
            }
            var read = Arcanum.Data.Scripts.ScrProgram.Read(bytes);
            Assert.AreEqual(Arcanum.Data.Scripts.ScrReadStatus.Read, read.Status);
            return read.Program!;
        }

        public Arcanum.Scripting.Scr.ScrSlot? Slot(Arcanum.Scripting.Scr.ScrObjectRef attachee, int point) => point == SessionScriptPoint.Throw ? new(ScriptNumber, 0, 0) : null;
        public Arcanum.Scripting.Scr.ScrScriptLookup Script(int number) => number == ScriptNumber ? Arcanum.Scripting.Scr.ScrScriptLookup.Of(_refuse) : Arcanum.Scripting.Scr.ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => false;
        public int StoryState() => 0;
        public bool IsMonsterGenerator(Arcanum.Scripting.Scr.ScrObjectRef attachee) => false;
        public Arcanum.Scripting.Scr.ScrDialogState DialogState(Arcanum.Scripting.Scr.ScrObjectRef attachee) => Arcanum.Scripting.Scr.ScrDialogState.Normal;
        public Arcanum.Scripting.Scr.ScrTrapCheck CheckTrapBeforeUse(Arcanum.Scripting.Scr.ScrObjectRef triggerer, Arcanum.Scripting.Scr.ScrObjectRef attachee) => Arcanum.Scripting.Scr.ScrTrapCheck.NoTrap;
        public bool IsTrapObject(Arcanum.Scripting.Scr.ScrObjectRef attachee) => false;
        public Arcanum.Scripting.Scr.ScrObjectRef? Player() => null;
        public Arcanum.Scripting.Scr.ScrObjectRef? Leader(Arcanum.Scripting.Scr.ScrObjectRef critter) => null;
        public bool IsJilted(Arcanum.Scripting.Scr.ScrObjectRef critter) => false;
        public bool IsPc(Arcanum.Scripting.Scr.ScrObjectRef critter) => false;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, "hero text", "line 1");
        public SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Shown, "It hums.", "line 1");
    }

    [TestMethod]
    public void A_throw_script_that_refuses_keeps_the_dagger_with_the_npc_and_its_line_is_in_the_notice()
    {
        // Without the scene's script host the scripted dagger is refused, as before.
        var bare = Open(daggerScripts: [SessionScriptPoint.Throw]);
        bare.Host.ThrowItem(bare.Npc, bare.Item, HeroCell);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.Host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw).Throw!.Failure, "no host: refused");
        Assert.AreEqual(bare.Npc, bare.Host.ItemHolder(bare.Item));

        var scene = Open(itemScripts: new ThrowScriptHost(), daggerScripts: [SessionScriptPoint.Throw]);
        var draws = scene.Session.QueryRandom().Draws;
        Assert.IsTrue(scene.Host.ThrowItem(scene.Npc, scene.Item, HeroCell), scene.Host.TraceOf(scene.Npc)?.Throw);
        var notice = scene.Host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        Assert.AreEqual((GameSessionFailure.None, true, (SessionAttackResult?)null), (notice.Throw!.Failure, notice.Throw.ScriptDeclined, notice.Result));
        CollectionAssert.AreEqual(new[] { "It hums." }, notice.Messages.Select(message => message.Text).ToArray(), "the line the throw script printed");
        Assert.AreEqual(draws, scene.Session.QueryRandom().Draws, "nothing was rolled");
        Assert.AreEqual(scene.Npc, scene.Host.ItemHolder(scene.Item), "the dagger is back with the thrower");
        Assert.IsTrue(scene.Host.TraceOf(scene.Npc)!.Throw!.Contains("script-declined", StringComparison.Ordinal));
    }

    // ARC-COMBAT-1717: the seed whose throw at the hero decides a hit (or a miss), found by a bounded search; the scene it opened.
    private static Scene OpenThrowing(bool hit, Func<string, SessionShot, CellCoord, CellCoord, bool> launch)
    {
        // A weight that makes the thrown range 1 (RH4) makes the throw at four tiles a sure miss.
        var weight = hit ? 15 : ThrowRules.MaxWeight;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var scene = Open(launch: launch, seed: seed, weight: weight);
            var probe = Open(launch: (_, _, _, _) => true, seed: seed, weight: weight);
            Assert.IsTrue(probe.Host.ThrowItem(probe.Npc, probe.Item, HeroCell));
            if (probe.Host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw).Throw!.Attack is { Resolution.Outcome: var outcome }
                && (outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit) == hit) return scene;
        }
        throw new AssertFailedException($"no seed gave a {(hit ? "hit" : "miss")}");
    }

    [TestMethod]
    public void A_thrown_dagger_that_hits_flies_to_the_hero_and_its_strike_and_landing_are_the_flights()
    {
        var launched = new List<(SessionShot Shot, CellCoord From, CellCoord To)>();
        var (session, host, npc, dagger) = OpenThrowing(hit: true, (_, shot, from, to) => { launched.Add((shot, from, to)); return true; });
        var life = session.QueryHeroLife()!.Record.State.HitPointDamage;
        Assert.IsTrue(host.ThrowItem(npc, dagger, HeroCell), host.TraceOf(npc)?.Throw);
        var thrown = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        var shot = thrown.Throw!.Shots.Single();
        Assert.AreEqual((shot, NpcCell, HeroCell), launched.Single(), "the flight goes from the thrower's tile to the struck critter's");
        Assert.IsNull(thrown.Throw.Attack!.Life, "the damage waits for the strike");
        Assert.AreEqual(life, session.QueryHeroLife()!.Record.State.HitPointDamage);
        Assert.IsTrue(session.QueryItemInFlight(dagger), "the dagger flies, hidden");
        Assert.IsTrue(host.TraceOf(npc)!.Throw!.Contains("flies shot=", StringComparison.Ordinal), host.TraceOf(npc)!.Throw);
        // The viewer strikes where the flight meets the hero (the AI host's LandShot), then lands the item on the tile left.
        var landed = host.LandShot(npc, shot, AiSchedulerRules.Pc);
        Assert.AreEqual((GameSessionDisposition.Changed, true), (landed?.Disposition, landed?.Life is not null), "the hit lands");
        Assert.IsTrue(session.QueryItemInFlight(dagger), "the struck throw waits for the landing (LandShot does not end it)");
        Assert.AreEqual(GameSessionDisposition.Changed, session.LandThrownItem(session.Version, shot, HeroCell).Disposition);
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void A_thrown_dagger_that_misses_flies_free_to_the_scatter_tile()
    {
        var launched = new List<CellCoord>();
        var (session, host, npc, dagger) = OpenThrowing(hit: false, (_, _, _, to) => { launched.Add(to); return true; });
        Assert.IsTrue(host.ThrowItem(npc, dagger, HeroCell), host.TraceOf(npc)?.Throw);
        var thrown = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        Assert.AreEqual(thrown.Throw!.Destination, launched.Single(), "the flight goes to the scatter tile");
        Assert.AreNotEqual(HeroCell, launched[0]);
        Assert.IsTrue(session.QueryItemInFlight(dagger));
        Assert.AreEqual(false, session.QueryNpcShot(thrown.Throw.Shots.Single())!.Value.Hit);
    }

    [TestMethod]
    public void A_thrown_dagger_the_viewer_cannot_fly_resolves_at_once_a_hit_striking_first()
    {
        var (session, host, npc, dagger) = OpenThrowing(hit: true, (_, _, _, _) => false);
        var life = session.QueryHeroLife()!.Record.State.HitPointDamage;
        Assert.IsTrue(host.ThrowItem(npc, dagger, HeroCell), host.TraceOf(npc)?.Throw);
        Assert.IsTrue(host.TraceOf(npc)!.Throw!.Contains("lands at once", StringComparison.Ordinal), host.TraceOf(npc)!.Throw);
        Assert.AreEqual((false, 0), (session.QueryItemInFlight(dagger), session.QueryShotsInFlight().Count), "the shot is over");
        Assert.IsTrue(session.QueryHeroLife()!.Record.State.HitPointDamage >= life);
        Assert.IsNull(host.ThrowableWeapon(npc));
        Assert.IsTrue(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Hit && n.Result?.Life is not null), "the hit struck at once (no art, PF1)");
    }

    [TestMethod]
    public void A_thrown_dagger_at_an_empty_tile_flies_unchecked_to_it()
    {
        var launched = new List<CellCoord>();
        var (session, host, npc, dagger) = Open(launch: (_, _, _, to) => { launched.Add(to); return true; });
        var tile = new CellCoord(12, 22);
        Assert.IsTrue(host.ThrowItem(npc, dagger, tile));
        var thrown = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw);
        Assert.AreEqual((tile, (SessionAttackResult?)null), (launched.Single(), thrown.Throw!.Attack));
        Assert.IsTrue(session.QueryItemInFlight(dagger));
    }

    [TestMethod]
    public void A_thrown_grenade_without_a_viewer_to_fly_it_lands_on_the_location_at_once()
    {
        var (session, host, npc, _) = Open(grenade: true);
        var tile = new CellCoord(12, 22);
        Assert.IsTrue(host.ThrowItem(npc, Entity(2), tile));
        var shot = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Throw).Throw!.Shots.Single();
        Assert.IsFalse(session.QueryItemInFlight(Entity(2)), "the flight ended with the landing");
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
        Assert.IsFalse(session.EndShot(shot));
        Assert.IsTrue(host.TraceOf(npc)!.Throw!.Contains("lands at once", StringComparison.Ordinal));
    }

    // ---- ARC-DEV-364 (T-h): the grenade step over the host ----

    // The danger setter sets CHECK_GRENADE when a fight starts (SP14, the enter bits): a fight on another critter keeps the setup free of the hero's reaction.
    private static void SetCheckGrenade(GameSessionOwner session, string npc)
    {
        var focus = session.SetDangerState(session.Version, new SessionDangerRequest { Entity = npc, State = DangerState.CombatFocus, Target = SessionCombatant.Critter(Entity(9)) });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (focus.Disposition, focus.Failure));
        Assert.IsTrue(session.QueryDanger(npc).Flags.HasFlag(DangerFlags.CheckGrenade), "the fight start set CHECK_GRENADE");
    }

    [TestMethod]
    public void The_grenade_step_takes_the_first_generic_item_with_the_grenade_flag_and_starts_the_throw_goal_at_the_target()
    {
        var (session, host, npc, _) = Open(grenade: true, grenadeFlags: 0x10 | 0x1);
        Assert.AreEqual(Entity(2), host.Grenade(npc), "the first generic item holding generic flag 0x10 (TI3)");
        Assert.IsNull(host.Grenade(AiSchedulerRules.Pc), "the hero's items are not the AI's");
        Assert.IsTrue(host.HasThrowingPoints(npc), "5 points of throwing");
        Assert.AreEqual(0, host.BlastRadius(Entity(2)), "no cast host, no spell radius");
        Assert.IsNull(host.LineOfFireBlocker(npc, AiSchedulerRules.Pc), "no map, no blocker");
        Assert.AreEqual(0, host.DangerBits(npc).HasFlag(DangerFlags.CheckGrenade) ? 1 : 0);

        // Without CHECK_GRENADE the step does nothing; with it, the step clears the bit and chooses the throw.
        Assert.AreEqual(AiGrenadeStop.NoCheck, host.Grenade(npc, AiSchedulerRules.Pc).Stop);
        SetCheckGrenade(session, npc);
        var result = host.Grenade(npc, AiSchedulerRules.Pc);
        Assert.AreEqual((AiGrenadeStop.Throw, Entity(2)), (result.Stop, result.Grenade), host.TraceOf(npc)?.Grenade);
        Assert.IsFalse(host.DangerBits(npc).HasFlag(DangerFlags.CheckGrenade), "one consideration per fight start (SP14)");
    }

    [TestMethod]
    public void A_grenade_without_the_flag_or_an_npc_without_throwing_points_is_not_thrown()
    {
        var (session, host, npc, _) = Open(grenade: true, grenadeFlags: 0x1);
        Assert.IsNull(host.Grenade(npc), "a generic item without flag 0x10 is no grenade");
        SetCheckGrenade(session, npc);
        Assert.AreEqual(AiGrenadeStop.NoGrenade, host.Grenade(npc, AiSchedulerRules.Pc).Stop);
        var (_, plain, other, _) = Open();
        Assert.IsNull(plain.Grenade(other), "a dagger is not a generic item");
    }
}
