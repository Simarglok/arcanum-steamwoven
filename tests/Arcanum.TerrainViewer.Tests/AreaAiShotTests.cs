using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
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
    private static AreaObjectRecord Item(int index, uint weaponFlags = 0, uint itemFlags = 0, int weight = 15, int worth = 1, int location = 1004, long useArt = 0x60000000) => new(Key(index), 0, 5, BowPrototype, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, itemFlags), Field(38, weight), Field(39, 0), Field(40, worth), Field(41, 0), Field(42, 0x60000000), Field(43, location), Field(44, useArt), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, weaponFlags), Field(61, 0), Field(62, 0), Array(63, 2, 0, 0, 0, 0), Array(64, 6, 0, 0, 0, 0), Array(65, 0, 0, 0, 0, 0),
            Field(66, 10), Field(67, 0), Field(68, 12), Field(69, 0), Field(70, 0), Field(71, 0), Field(72, 0), Field(73, 1), Field(74, 0x20000),
            Field(76, 0), Field(77, 0), Field(78, 0), Field(79, 0), Field(80, 0), Field(81, 0),
        ], true, true, true);

    // A human NPC (type 16, no monster flags) with every primary stat 20, level 1, throwing 5 points, 20 hit points, at world cell (10, 20).
    private static AreaObjectRecord Npc(int index, CellCoord? at = null, long faction = 0) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, (at ?? NpcCell).X, (at ?? NpcCell).Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, faction), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);


    private sealed record Scene(GameSessionOwner Session, AreaAiHost Host, string Npc, string Bow);

    private sealed class NoScreen : IAreaRestockScreen
    {
        public int? ShownMap => null;
        public AreaPixelRect? GameWindow => null;
        public (double Width, double Height) WindowSize => (0, 0);
        public AreaPixelRect? BodyRect(AreaObjectKey key) => null;
    }

    private static readonly CellCoord BystanderCell = new(12, 20);

    private sealed class Line(AreaObjectRecord? blocker) : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 32 && tile.Y < 32;
        public CellCoord? Step(CellCoord from, int rotation) => new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y) is var next && Contains(next) ? next : null;
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => blocker is not null && tile == BystanderCell ? [new(blocker, false, 0)] : [];
    }

    // ARC-COMBAT-1140: a container (type 2) on the bystander's cell that stops the line of fire.
    private static AreaObjectRecord Chest() => new(Key(5), 0, 2, 9999, "p", "p", "r", "s", AreaScope.Core, null, BystanderCell.X, BystanderCell.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static AreaObjectAttackTarget ChestTarget(AreaObjectRecord row, int points = 6) => new("sector:1:5", row, DamageableObjectKind.Container,
        new SessionObjectTarget(new SessionMapId(1), "sector:1:5", new SessionObjectBaseline
        {
            Kind = DamageableObjectKind.Container, Points = points, Material = ObjectMaterial.Cloth, Cell = BystanderCell,
            Openable = new SessionOpenableBaseline(Arcanum.Simulation.Locks.OpenableKind.Container, 0, 0, 0),
        }), null, null, null);

    private static Scene Open(Func<string, string, SessionShot, CellCoord, CellCoord, bool>? launch, bool bystander = false, AreaObjectRecord? blockerRow = null,
        Func<AreaObjectRecord, AreaObjectAttackTarget?>? objectTarget = null, Func<string, AreaObjectRecord, SessionShot, CellCoord, CellCoord, bool>? launchAtObject = null,
        Action<string, AreaObjectRecord, AreaObjectAttackTarget?, SessionObjectAttackResult>? struck = null,
        IReadOnlyList<(AreaObjectRecord Row, SessionSeedItem Seed)>? ground = null, string? junkPiles = null, long bowUseArt = 0x60000000, long faction = 0, bool bystanderOnLine = true,
        bool npcHome2 = false, int bowWeight = 15, Func<bool>? lootWindowOpen = null, (int Index, CellCoord At, IReadOnlyList<(AreaObjectRecord Row, SessionSeedItem Seed)> Items)? mapPile = null)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero));
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        if (junkPiles is not null)
        {
            // ARC-AI-1309: a save whose junk piles hold the items lying on their cells (the piles a drop there would have made), as AreaInventoryHostTests builds one.
            var snapshot = GameSessionSnapshotCodec.TryDecode(GameSessionSnapshotCodec.Encode(session.Capture()).Replace("\"junkPiles\":[]", "\"junkPiles\":" + junkPiles)).Snapshot!;
            session.Dispose();
            session = GameSessionOwner.Restore(snapshot, new(snapshot.Identity, snapshot.Clock.Profile, new OwnerEpoch(Guid.NewGuid()))).Owner!;
        }
        var rows = (bystander ? new[] { Npc(0, null, faction), Item(1, useArt: bowUseArt), Npc(3, BystanderCell, faction) } : new[] { Npc(0), Item(1, useArt: bowUseArt) }).Concat(ground?.Select(g => g.Row) ?? [])
            .Concat(mapPile is { } piled ? [PileRow(piled.Index, piled.At), .. piled.Items.Select(i => i.Row)] : []).ToArray();
        // ARC-AI-1486: npcHome2 lists the NPC and its bow on map 2's seed only, as a companion carried from another map is (its home map's seed; the shown map's view does not list it).
        var npcHolder = new SessionSeedHolder(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)]);
        var bow = new SessionSeedItem(Entity(1), new(BowKind, bowWeight, 0, 1004, null, []), null);
        SessionSeedHolder[] shownHolders = npcHome2 ? [] : bystander ? [npcHolder, new(Entity(3), SessionHolderKind.Npc, false, false, [], [])] : [npcHolder];
        // ARC-AI-1486: a junk pile of the map's data: a container row of prototype 3043 whose seed holder lists the items.
        if (mapPile is { } pileSeed) shownHolders = [.. shownHolders, new(Entity(pileSeed.Index), SessionHolderKind.Container, false, false, [], [.. pileSeed.Items.Select(i => i.Seed.Entity)])];
        var map = new AreaInventoryMap(1,
            new(shownHolders, [.. npcHome2 ? [] : new[] { bow }, .. ground?.Select(g => g.Seed) ?? [], .. mapPile?.Items.Select(i => i.Seed) ?? []]),
            rows.Where(row => !npcHome2 || row.Key.SourceIndex > 1).ToDictionary(row => Entity(row.Key.SourceIndex)));
        var home2 = new AreaInventoryMap(2, new([npcHolder], [bow]), rows.Where(row => row.Key.SourceIndex <= 1).ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost(npcHome2 ? [map, home2] : [map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 64; x++) for (var y = 0; y < 64; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST") { MinimumCell = -64, MaximumCell = 127 }, Steps = steps, Npcs = bystander ? [new(Entity(0), Npc(0, null, faction), NpcCell), new(Entity(3), Npc(3, BystanderCell, faction), BystanderCell)] : [new(Entity(0), Npc(0), NpcCell)], Combat = combat,
            HeroCell = () => HeroCell, LaunchShot = launch,
            LineOfFire = blockerRow is not null ? () => new Line(blockerRow) : bystander && bystanderOnLine ? () => new Line(Npc(3, BystanderCell)) : null,
            ObjectTarget = objectTarget, LaunchShotAtObject = launchAtObject, ObjectStruck = struck, LootWindowOpen = lootWindowOpen,
        };
        // ARC-AI-1139: the restock host answers the NPC wield the AI gate's re-wield asks for (no barter data: the map's own rows are the items).
        var restock = new AreaRestockHost(session, inventory, null, new NoScreen());
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null, restock: () => restock), new AreaCombatWorld(1, () => [], _ => null));
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

    // ARC-WORLD-124 (0x4AA0D0, ledger TP2): the alarm of a record 176 trap device. The NPC four tiles from the hero that perceives it is attacked by the hero through the attack event (it turns on the hero);
    // an NPC beyond the half-size 10 square is not asked, and a second call attacks the same NPC again (the original has no memory of its own).
    [TestMethod]
    public void The_trap_device_alarm_turns_the_critters_in_the_square_that_see_or_hear_the_hero_against_it()
    {
        var (_, host, npc, _) = Open(null, bystander: true);
        var alerted = host.TrapDeviceAlarm();
        CollectionAssert.IsSubsetOf(alerted.ToArray(), new[] { npc, Entity(3) });
        Assert.IsTrue(alerted.Contains(npc), "the NPC four tiles away perceives the hero");
        Assert.AreNotEqual(DangerState.None, host.Danger(npc).State, "it turned on the hero");
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

    // ARC-AI-1516 (EX14, VBG5, 0x4B2870/0x4B2690 → 0x4A9650): a missile that meets an object other than its aimed target raises the attack event with the accidental bit: a same-faction
    // bystander struck by a stray arrow records no who-hit-me-last (the shielded case); one of another faction does (the accidental bit only shields one faction).
    private static void ShootAtHeroAndStrike(AreaAiHost host, GameSessionOwner session, string npc, string struck, List<SessionShot> launched)
    {
        for (var attempt = 0; attempt < 80 && launched.Count == 0; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(1, launched.Count, host.TraceOf(npc)?.Hit);
        host.DrainNotices();
        Assert.IsNull(session.QueryDanger(struck).HitBy, "no event yet");
        var landed = host.LandShot(npc, launched[0], struck);
        Assert.IsNotNull(landed, host.TraceOf(npc)?.Hit);
        host.DrainNotices();
    }

    [TestMethod]
    public void A_stray_arrow_on_an_ally_records_nobody_who_hit_it()
    {
        var launched = new List<SessionShot>();
        var (session, host, npc, _) = Open((_, _, shot, _, _) => { launched.Add(shot); return true; }, bystander: true, faction: 5, bystanderOnLine: false);
        Assert.IsTrue(host.FactionSame(npc, Entity(3)));
        ShootAtHeroAndStrike(host, session, npc, Entity(3), launched);
        Assert.AreEqual("None", host.TraceOf(Entity(3))?.Event, "the event ran to the reaction step");
        Assert.IsNull(session.QueryDanger(Entity(3)).HitBy, "the accidental event between one faction writes no who-hit-me-last");
    }

    [TestMethod]
    public void A_stray_arrow_on_a_stranger_still_records_the_shooter()
    {
        var launched = new List<SessionShot>();
        var (session, host, npc, _) = Open((_, _, shot, _, _) => { launched.Add(shot); return true; }, bystander: true, faction: 0, bystanderOnLine: false);
        Assert.IsFalse(host.FactionSame(npc, Entity(3)));
        ShootAtHeroAndStrike(host, session, npc, Entity(3), launched);
        Assert.AreEqual(npc, session.QueryDanger(Entity(3)).HitBy?.Entity, "another faction: the event writes the shooter");
    }

    [TestMethod]
    public void The_aimed_target_of_a_shot_at_an_ally_is_a_deliberate_event_still()
    {
        var launched = new List<SessionShot>();
        var (session, host, npc, _) = Open((_, _, shot, _, _) => { launched.Add(shot); return true; }, bystander: true, faction: 5);
        for (var attempt = 0; attempt < 80 && launched.Count == 0; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(1, launched.Count, host.TraceOf(npc)?.Hit);
        Assert.AreEqual(npc, session.QueryDanger(Entity(3)).HitBy?.Entity, "the line's blocker is the shot's target: its event is raised at the shot, deliberate (EX15)");
        host.DrainNotices();
        Assert.IsNotNull(host.LandShot(npc, launched[0], Entity(3)));
        Assert.AreEqual(npc, session.QueryDanger(Entity(3)).HitBy?.Entity, "the strike of the aimed target stays recorded");
    }

    // ARC-COMBAT-1140 (RA4, RH5): a container that stops the line of fire takes the NPC's shot in place of the hero: the shot is decided a hit on it without a skill roll, the viewer is asked to fly it at the blocker,
    // and the strike (the host's, as where the flight ends) damages the container while the hero is not hurt.
    [TestMethod]
    public void An_object_in_the_line_of_fire_takes_the_shot_instead_of_the_target()
    {
        var chest = Chest();
        var target = ChestTarget(chest);
        var launched = new List<(string Shooter, AreaObjectRecord Blocker, SessionShot Shot, CellCoord From, CellCoord To)>();
        var (session, host, npc, _) = Open((_, _, _, _, _) => true, blockerRow: chest, objectTarget: row => row.Key == chest.Key ? target : null,
            launchAtObject: (who, blocker, shot, from, to) => { launched.Add((who, blocker, shot, from, to)); return true; });
        var heroHp = session.QueryHeroLife()!.Status.HitPoints;
        for (var attempt = 0; attempt < 5 && launched.Count == 0; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(1, launched.Count, host.TraceOf(npc)?.Hit);
        Assert.AreEqual((npc, chest.Key, NpcCell, HeroCell), (launched[0].Shooter, launched[0].Blocker.Key, launched[0].From, launched[0].To), "the arrow flies at the hero's tile and strikes the chest where it crosses it");
        Assert.IsTrue(session.QueryNpcShot(launched[0].Shot) is { Hit: true } facts && facts.Target == target.Entity, "decided a hit on the chest without the skill roll: " + host.TraceOf(npc)?.Hit);
        Assert.AreEqual(heroHp, session.QueryHeroLife()!.Status.HitPoints, "the hero is not struck");
        Assert.AreEqual(0, session.QueryObject(target.Object!).Damage, "the damage waits for the strike");

        var landed = host.LandShotOnObject(npc, launched[0].Shot, target.Object, wall: false);
        Assert.IsTrue(landed is { Object.Damage: > 0 }, "the chest took the carried hit: " + host.TraceOf(npc)?.Hit);
        Assert.IsTrue(session.QueryObject(target.Object!).Damage > 0);
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
        Assert.AreEqual(heroHp, session.QueryHeroLife()!.Status.HitPoints);
    }

    [TestMethod]
    public void A_shot_at_an_object_the_viewer_cannot_fly_strikes_it_at_once()
    {
        var chest = Chest();
        var target = ChestTarget(chest);
        var struck = new List<(string Shooter, AreaObjectRecord Row, AreaObjectAttackTarget? Target, SessionObjectAttackResult Result)>();
        var (session, host, npc, _) = Open((_, _, _, _, _) => true, blockerRow: chest, objectTarget: row => row.Key == chest.Key ? target : null,
            launchAtObject: (_, _, _, _, _) => false, struck: (who, row, info, result) => struck.Add((who, row, info, result)));
        for (var attempt = 0; attempt < 5 && struck.Count == 0; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(1, struck.Count, host.TraceOf(npc)?.Hit);
        Assert.AreEqual((npc, chest.Key, target.Entity), (struck[0].Shooter, struck[0].Row.Key, struck[0].Target!.Entity));
        Assert.IsTrue(struck[0].Result.Object is { Damage: > 0 }, "it landed on the chest");
        Assert.AreEqual(0, session.QueryShotsInFlight().Count);
    }

    [TestMethod]
    public void Without_an_object_hook_or_for_an_item_the_target_keeps_the_shot()
    {
        var chest = Chest();
        var launched = new List<string>();
        var (_, host, npc, _) = Open((_, target, _, _, _) => { launched.Add(target); return true; }, blockerRow: chest);
        for (var attempt = 0; attempt < 5 && launched.Count == 0; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        CollectionAssert.AreEqual(new[] { AiSchedulerRules.Pc }, launched, "no object hook: the shot goes at the hero, whose check the blocked line fails");

        launched.Clear();
        var (_, host2, npc2, _) = Open((_, target, _, _, _) => { launched.Add(target); return true; }, blockerRow: chest, objectTarget: _ => null);
        for (var attempt = 0; attempt < 5 && launched.Count == 0; attempt++) host2.Hit(npc2, AiSchedulerRules.Pc);
        CollectionAssert.AreEqual(new[] { AiSchedulerRules.Pc }, launched, "an object the session does not damage keeps the shot at the target");
    }

    // ARC-AI-1139 (0x4B3170 → 0x4ADFF0): a companion's bow shot the ammunition check refuses (no arrows in its pack) marks it LOOK_FOR_AMMO, LOOK_FOR_WEAPON and CHECK_WEAPON; one that is no companion
    // spends no ammunition, is never refused and is marked with nothing.
    private const DangerFlags EmptyBowBits = DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.CheckWeapon;

    [TestMethod]
    public void A_companions_shot_without_arrows_marks_it_as_needing_ammunition_a_weapon_and_a_weapon_check()
    {
        var (session, host, npc, _) = Open(null);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, npc).Disposition);
        Assert.AreEqual(DangerFlags.None, session.QueryDanger(npc).Flags & EmptyBowBits);
        host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(EmptyBowBits, session.QueryDanger(npc).Flags & EmptyBowBits, host.TraceOf(npc)?.Hit);
        var notice = host.DrainNotices().Single(n => n.Kind == AreaAiNoticeKind.Hit);
        Assert.AreEqual(AttackRefusal.OutOfAmmunition, notice.Result!.Resolution!.Refusal);
    }

    [TestMethod]
    public void An_npc_without_a_pc_leader_is_never_marked_as_needing_ammunition()
    {
        var (session, host, npc, _) = Open((_, _, _, _, _) => false);
        for (var attempt = 0; attempt < 10; attempt++) host.Hit(npc, AiSchedulerRules.Pc);
        Assert.AreEqual(DangerFlags.None, session.QueryDanger(npc).Flags & EmptyBowBits);
    }

    // ARC-AI-1139 (0x4A8570 → 0x465170): the host's part of the gate's weapon re-wield (the gate itself, its bit clearing and the order of its steps are the scheduler's, SchedulerRulesTests and AiSchedulerTests): the
    // empty bow of the marked companion goes to the pack (no arrows are carried), so no weapon is worn.
    [TestMethod]
    public void The_hosts_rewield_takes_the_empty_bow_of_a_marked_companion_off()
    {
        var (session, host, npc, bow) = Open(null);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, npc).Disposition);
        host.Hit(npc, AiSchedulerRules.Pc);
        host.DrainNotices();
        Assert.AreEqual(EmptyBowBits, session.QueryDanger(npc).Flags & EmptyBowBits);
        Assert.IsNull(session.QueryNpcWorn(npc), "the map data's inventory location wields the bow");
        host.ReWield(npc, allSlots: false);
        Assert.AreEqual(0, session.QueryNpcWorn(npc)!.Worn.Count, host.TraceOf(npc)?.Item);
        Assert.AreEqual(-1, session.QueryHolderItems(npc)!.Single(item => item.Entity == bow).Facts.InventoryLocation, "the bow went to the pack");
    }

    // ARC-RENDER-1485 (0x465020): the weapon part the NPC's body shows follows what it wields. In combat mode it shows its bow (use-art subtype 4 here); the re-wield that takes the empty bow off shows bare hands (1), and the
    // viewer is told (the Rewield notice) so it draws the body again. A re-wield that changes nothing tells nothing.
    [TestMethod]
    public void The_hosts_rewield_changes_the_weapon_the_body_shows_and_tells_the_viewer()
    {
        var (session, host, npc, _) = Open(null, bowUseArt: 0x60000000 | 4 << 6);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetCombatMode(session.Version, SessionCombatant.Critter(npc), true, new SessionCombatModeContext { Facts = new() { Speed = 7, Life = new LifeFacts() } }).Disposition);
        Assert.AreEqual((4, 0), host.ShownWeapon(npc), "it shows the bow it wields");
        host.Hit(npc, AiSchedulerRules.Pc);
        host.DrainNotices();
        host.ReWield(npc, allSlots: false);
        Assert.AreEqual((WeaponArtRules.Unarmed, 0), host.ShownWeapon(npc), "the empty bow is in the pack: bare hands");
        Assert.AreEqual(1, host.DrainNotices().Count(n => n.Kind == AreaAiNoticeKind.Rewield && n.Npc == npc));
        host.ReWield(npc, allSlots: false);
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Rewield), "nothing changed, nothing told");
    }

    [TestMethod]
    public void The_hosts_item_search_clears_each_look_for_bit_and_starts_no_pickup_and_a_full_rewield_is_traced()
    {
        var (session, host, npc, _) = Open(null);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, npc).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForAmmo | DangerFlags.LookForArmor).Disposition);
        Assert.IsFalse(host.SearchItems(npc));
        Assert.AreEqual(DangerFlags.None, session.QueryDanger(npc).Flags & (DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.LookForArmor));
        host.ReWield(npc, allSlots: true);
        Assert.IsTrue(host.TraceOf(npc)!.Item!.StartsWith("rewield all "), host.TraceOf(npc)?.Item);
    }

    // ARC-AI-1309: the item search's ground. A loose weapon is a bow-shaped row (type 5) with the item flags and worth the search reads (fields 36 and 40) lying at a cell of the seed.
    private static (AreaObjectRecord Row, SessionSeedItem Seed) Loose(int index, CellCoord at, uint flags = 0, int worth = 1, SessionItemType type = SessionItemType.Weapon, int weight = 3)
    {
        return (Item(index, 0, flags, weight, worth, -1), new SessionSeedItem(Entity(index), new(BowKind with { Type = type, Prototype = BowPrototype + index }, weight, 0, -1, type == SessionItemType.Ammo ? 5 : null, []) { AmmoType = type == SessionItemType.Ammo ? 0 : null }, at));
    }

    // ARC-AI-1486: a junk pile of the map's data (a container row of prototype 3043, as the Crash Site cave's) and the item it holds (a bow-shaped weapon with no cell of its own).
    private static AreaObjectRecord PileRow(int index, CellCoord at) => new(Key(index), 0, 2, SessionObjectFacts.JunkPilePrototypeFile, "p", "p", "r", "s", AreaScope.Core, null, at.X, at.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x20000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 2),
            Array(30), Empty(31), Field(36, 0), Field(38, 0),
        ], true, true, true);

    private static (AreaObjectRecord Row, SessionSeedItem Seed) PileItem(int index, uint flags = 0) =>
        (Item(index, 0, flags, 3, 1, -1), new SessionSeedItem(Entity(index), new(BowKind with { Prototype = BowPrototype + index }, 3, 0, -1, null, []), null));

    private static readonly DangerFlags Looking = DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.LookForArmor;

    [TestMethod]
    public void The_hosts_ground_lists_the_loose_items_of_the_kind_in_the_square_with_their_distances_and_tests()
    {
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(13, 20)), Loose(3, new(11, 21)), Loose(4, new(40, 20)), Loose(5, new(12, 20), type: SessionItemType.Armor),
            Loose(6, new(10, 22), flags: 0x80000), Loose(7, new(10, 23), worth: 0), Loose(8, new(9, 20), flags: 0x10)]);
        var weapons = host.LooseItems(npc, AiGroundKind.Weapon, 10).ToDictionary(item => item.Id);
        CollectionAssert.AreEquivalent(new[] { Entity(2), Entity(3), Entity(6), Entity(7), Entity(8) }, weapons.Keys.ToArray(), "the armour and the far item are not in the weapon query");
        Assert.AreEqual((3L, 1L), (weapons[Entity(2)].Distance, weapons[Entity(3)].Distance));
        Assert.IsTrue(weapons[Entity(6)].NoNpcPickup && weapons[Entity(7)].Worthless && weapons[Entity(8)].NoDisplay);
        Assert.IsFalse(weapons[Entity(3)].NoDisplay || weapons[Entity(3)].NoNpcPickup || weapons[Entity(3)].Worthless || weapons[Entity(3)].CannotTake);
        CollectionAssert.AreEqual(new[] { Entity(5) }, host.LooseItems(npc, AiGroundKind.Armor, 10).Select(item => item.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { Entity(3), Entity(8) }, host.LooseItems(npc, AiGroundKind.Weapon, 1).Select(item => item.Id).ToArray(), "a square of half-size 1");
        Assert.AreEqual(0, host.JunkPiles(npc, 10).Count);
        Assert.IsNull(host.FirstInPile("junk:1", AiGroundKind.Weapon));
    }

    [TestMethod]
    public void An_item_too_heavy_for_the_npc_is_one_it_cannot_take()
    {
        var (_, host, npc, _) = Open(null, ground: [Loose(2, new(11, 20), weight: 100000)]);
        Assert.IsTrue(host.LooseItems(npc, AiGroundKind.Weapon, 10).Single().CannotTake);
    }

    // ARC-AI-1486: a companion carried from another map lists on its home map's seed, which the shown map's view does not show; its carried weight still counts (0x466510 totals the holder's own list).
    [TestMethod]
    public void The_weight_of_a_listed_npc_counts_when_its_list_is_on_another_maps_seed()
    {
        var (_, shown, shownNpc, _) = Open(null, ground: [Loose(2, new(11, 20), weight: 1)], bowWeight: 100000);
        Assert.IsTrue(shown.LooseItems(shownNpc, AiGroundKind.Weapon, 10).Single().CannotTake, "control: the shown map's own NPC, overloaded by its bow");
        var (_, carried, carriedNpc, _) = Open(null, ground: [Loose(2, new(11, 20), weight: 1)], bowWeight: 100000, npcHome2: true);
        Assert.IsTrue(carried.LooseItems(carriedNpc, AiGroundKind.Weapon, 10).Single().CannotTake, "its list is on map 2's seed");
        var (_, light, lightNpc, _) = Open(null, ground: [Loose(2, new(11, 20), weight: 1)], npcHome2: true);
        Assert.IsFalse(light.LooseItems(lightNpc, AiGroundKind.Weapon, 10).Single().CannotTake, "a light list leaves room for it");
    }

    // ARC-AI-1486 (0x5E8800 lowered by an open loot window on a container, 0x5727B0 → 0x468190): no NPC starts a pickup while it is open, and one starts again once it closes.
    [TestMethod]
    public void No_npc_starts_a_pickup_while_a_loot_window_is_open_on_a_container()
    {
        var open = true;
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(13, 20))], lootWindowOpen: () => open);
        Assert.IsFalse(host.NpcPickupsOn);
        Assert.IsFalse(host.StartPickup(npc, Entity(2)), "the starter reads the switch");
        Assert.IsNull(host.RunOf(npc));
        open = false;
        Assert.IsTrue(host.NpcPickupsOn);
        Assert.IsTrue(host.StartPickup(npc, Entity(2)));
        Assert.AreEqual(Arcanum.Simulation.Goals.GoalType.PickupItem, host.RunOf(npc)!.Bottom.Goal);
    }

    // ARC-AI-1486: the map's own junk piles (containers of prototype 3043 named 3023) are searched as the session's are; the item leaves its container and the emptied pile dissolves at once (JP5).
    [TestMethod]
    public void The_search_reads_a_junk_pile_of_the_maps_data_and_passes_it_over_when_its_first_item_is_not_pickable()
    {
        var (session, host, npc, _) = Open(null, mapPile: (9, new(12, 22), [PileItem(10, flags: 0x80000), PileItem(11), PileItem(12)]));
        CollectionAssert.AreEqual(new[] { Entity(9) }, host.JunkPiles(npc, 10).ToArray());
        Assert.AreEqual(0, host.JunkPiles(npc, 1).Count, "a square of half-size 1 does not reach it");
        Assert.IsTrue(host.FirstInPile(Entity(9), AiGroundKind.Weapon) is { NoNpcPickup: true } first && first.Id == Entity(10), "the first weapon of the container's list");
        Assert.IsNull(host.FirstInPile(Entity(9), AiGroundKind.Armor));
        Assert.IsNull(host.FirstInPile(Entity(1), AiGroundKind.Weapon), "an item row is no pile");
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        // The first item of the pile is NO_NPC_PICKUP: the search passes the pile over (SP21), no pickup starts.
        Assert.IsFalse(host.SearchItems(npc));
        Assert.IsNull(host.RunOf(npc));
    }

    [TestMethod]
    public void An_npc_takes_the_first_item_of_a_junk_pile_of_the_maps_data_and_a_pile_left_with_one_item_drops_it()
    {
        var (session, host, npc, _) = Open(null, mapPile: (9, new(12, 22), [PileItem(10), PileItem(11)]));
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsTrue(host.SearchItems(npc), host.TraceOf(npc)?.Item);
        Assert.AreEqual((Arcanum.Simulation.Goals.GoalType.PickupItem, Entity(10)), (host.RunOf(npc)!.Bottom.Goal, host.RunOf(npc)!.Bottom.Item));
        var notices = Run(host, npc);
        Assert.IsNull(host.RunOf(npc), host.TraceOf(npc)?.Item);
        Assert.IsTrue(session.QueryHolderItems(npc)!.Any(item => item.Entity == Entity(10)), $"{host.TraceOf(npc)?.Item} {host.TraceOf(npc)?.Perform}");
        Assert.AreEqual(Entity(10), notices.Single(n => n.Kind == AreaAiNoticeKind.PickUp).Item);
        Assert.AreEqual(SessionObjectCondition.Destroyed, session.QueryObjectCondition(new(1), Entity(9), DamageableObjectKind.Container), "the removal checks the container: one item left, it goes");
        Assert.AreEqual((Entity(11), new CellCoord(12, 22)), session.QueryMapInventory(new SessionMapId(1))!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single());
        Assert.AreEqual(0, host.JunkPiles(npc, 10).Count, "the pile is gone");
    }

    [TestMethod]
    public void An_open_container_window_stops_the_pile_pickup_and_fails_one_already_under_way_before_anything_moves()
    {
        var open = false;
        var (session, host, npc, _) = Open(null, mapPile: (9, new(12, 22), [PileItem(10)]), lootWindowOpen: () => open);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        open = true;
        Assert.IsFalse(host.SearchItems(npc), "the switch is lowered: the starter refuses");
        Assert.IsNull(host.RunOf(npc));
        open = false;
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsTrue(host.SearchItems(npc));
        open = true;
        var notices = Run(host, npc);
        Assert.IsNull(host.RunOf(npc), "the run ended");
        Assert.AreEqual("pickup " + Entity(10) + ": loot window open", host.TraceOf(npc)?.Item);
        Assert.IsFalse(session.QueryHolderItems(npc)!.Any(item => item.Entity == Entity(10)), "the goal's end reads the switch (0x42AA70): the item stays in its pile");
        Assert.AreEqual(0, notices.Count(n => n.Kind == AreaAiNoticeKind.PickUp));
        Assert.AreEqual(SessionObjectCondition.Whole, session.QueryObjectCondition(new(1), Entity(9), DamageableObjectKind.Container));
        Assert.AreEqual(Entity(10), session.QueryHolderItems(Entity(9))!.Single().Entity);
        open = false;
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsTrue(host.SearchItems(npc), "once the window closes the search starts it again");
    }

    [TestMethod]
    public void The_marked_npc_walks_to_the_nearest_loose_weapon_picks_it_up_and_the_ground_loses_it()
    {
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(13, 20)), Loose(3, new(11, 21)), Loose(4, new(11, 20), flags: 0x80000)]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, Looking).Disposition);
        Assert.IsTrue(host.SearchItems(npc), host.TraceOf(npc)?.Item);
        Assert.AreEqual(DangerFlags.LookForArmor, session.QueryDanger(npc).Flags & Looking, "the weapon and ammunition bits are cleared before their searches; the pickup ends the step and the armour bit stays");
        Assert.AreEqual((Arcanum.Simulation.Goals.GoalType.PickupItem, Entity(3)), (host.RunOf(npc)!.Bottom.Goal, host.RunOf(npc)!.Bottom.Item), "the nearest one that is not NO_NPC_PICKUP");
        var notices = Run(host, npc);
        Assert.IsNull(host.RunOf(npc), host.TraceOf(npc)?.Item);
        Assert.IsTrue(session.QueryHolderItems(npc)!.Any(item => item.Entity == Entity(3)), $"{host.TraceOf(npc)?.Item} reason={host.TraceOf(npc)?.Reason} step={host.TraceOf(npc)?.Step} perform={host.TraceOf(npc)?.Perform}");
        var ground = session.QueryMapInventory(new SessionMapId(1))!.Ground.Select(g => g.Item.Entity).ToArray();
        CollectionAssert.AreEquivalent(new[] { Entity(2), Entity(4) }, ground);
        Assert.AreEqual(Entity(3), notices.Single(n => n.Kind == AreaAiNoticeKind.PickUp).Item);
        Assert.IsTrue(session.QueryDanger(npc).Flags.HasFlag(DangerFlags.CheckWield), "the insert asks for a re-wield");
    }

    [TestMethod]
    public void The_search_looks_for_the_kind_of_the_bit_that_is_set_and_an_empty_ground_starts_nothing()
    {
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(11, 20), type: SessionItemType.Armor)]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsFalse(host.SearchItems(npc), "no weapon lies there");
        Assert.IsNull(host.RunOf(npc));
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForArmor).Disposition);
        Assert.IsTrue(host.SearchItems(npc));
        Assert.AreEqual(Entity(2), host.RunOf(npc)!.Bottom.Item);
    }

    [TestMethod]
    public void A_pickup_whose_item_was_taken_meanwhile_ends_without_a_move()
    {
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(13, 20))]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsTrue(host.SearchItems(npc));
        Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, Entity(2), AreaInventoryHost.DiagnosticInsertCheck).Disposition);
        Run(host, npc);
        Assert.IsNull(host.RunOf(npc));
        Assert.IsFalse(session.QueryHolderItems(npc)!.Any(item => item.Entity == Entity(2)));
        Assert.AreEqual($"pickup {Entity(2)}: not there", host.TraceOf(npc)?.Item, "the goal's end found nothing and charged nothing");
    }

    [TestMethod]
    public void The_search_takes_the_first_item_of_the_kind_of_a_junk_pile_when_no_loose_item_qualifies()
    {
        // A pile on (11, 20) holds a weapon, a pile on (12, 20) an item NO_NPC_PICKUP: the first pile's first weapon is taken.
        var piles = "[{\"entity\":\"junk:1\",\"map\":1,\"x\":11,\"y\":20,\"items\":[{\"entity\":\"" + Entity(2) + "\",\"slot\":0,\"width\":1,\"height\":1}]},"
            + "{\"entity\":\"junk:2\",\"map\":1,\"x\":12,\"y\":20,\"items\":[{\"entity\":\"" + Entity(3) + "\",\"slot\":0,\"width\":1,\"height\":1}]}]";
        var (session, host, npc, _) = Open(null, ground: [Loose(2, new(11, 20)), Loose(3, new(12, 20), flags: 0x80000)], junkPiles: piles);
        Assert.AreEqual(0, host.LooseItems(npc, AiGroundKind.Weapon, 10).Count, "an item in a pile is no loose item");
        CollectionAssert.AreEqual(new[] { "junk:1", "junk:2" }, host.JunkPiles(npc, 10).ToArray());
        Assert.AreEqual(Entity(2), host.FirstInPile("junk:1", AiGroundKind.Weapon) is { } found ? found.Id : null, "its distance is not read for a pile's item");
        Assert.IsNull(host.FirstInPile("junk:1", AiGroundKind.Armor));
        Assert.IsTrue(host.FirstInPile("junk:2", AiGroundKind.Weapon) is { NoNpcPickup: true });
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetAiSupportBits(session.Version, npc, DangerFlags.LookForWeapon).Disposition);
        Assert.IsTrue(host.SearchItems(npc), host.TraceOf(npc)?.Item);
        Assert.AreEqual(Entity(2), host.RunOf(npc)!.Bottom.Item);
        var notices = Run(host, npc);
        Assert.IsTrue(session.QueryHolderItems(npc)!.Any(item => item.Entity == Entity(2)), $"{host.TraceOf(npc)?.Item} reason={host.TraceOf(npc)?.Reason}");
        Assert.AreEqual(Entity(2), notices.Single(n => n.Kind == AreaAiNoticeKind.PickUp).Item);
        CollectionAssert.AreEqual(new[] { Entity(3) }, session.QueryJunkPiles().SelectMany(pile => pile.Items).Select(item => item.Entity).ToArray());
    }
}
