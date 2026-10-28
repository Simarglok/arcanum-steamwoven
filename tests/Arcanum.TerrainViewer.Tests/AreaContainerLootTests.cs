using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Traps;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-101: the viewer's loot window on a container over an invented map (entities, cells, flags, sets and prototypes are invented):
// the target, the admission (open attempt, unlock and use scripts), the open frame and its close, the first open's generation of a
// container with flag 0x200 through the restock host's seed facts, and the loot of its goods.
[TestClass]
public sealed class AreaContainerLootTests
{
    private const int Axe = 4040, CrateSource = 5, CrateX = 107;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A container's common block (as AreaObjectTargetsTests) with its flags (36), lock key (38) and scripts (31: point → script).
    private static AreaObjectRecord Container(int index, long x, long flags = 0, int script = 0) =>
        new(Key(index), 0, 2, 1000, "p", "p", "r", "s", AreaScope.Core, null, x, 100, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x20000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 2),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            script == 0 ? new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null)
                : new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(script, [0, 0, 77])], null),
            Field(36, flags), Field(38, 0),
        ], true, true, true);

    private static AreaObjectRecord Item(int index, int type, long x) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, 100, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    private static SessionItemFacts Facts(SessionItemType type, int prototype) => new(new(type, prototype, 4200, 34200), 10, 0, 0, null, []);

    // 0 a chest at (100,100) holding 9 a ring; 1 a locked chest; 2 a jammed one; 3 one with a use script; 4 one with an unlock script;
    // 5 a crate with flag 0x200 and source 5 at (107,100) holding 10 an axe and 11 a persistent amulet; 6 a magically held chest; 7 one
    // with flag 0x100 (it stays open); 8 a second crate with flag 0x200 and a use script.
    private static AreaInventoryMap Map()
    {
        var seed = new SessionMapInventorySeed(
            [
                new(Entity(0), SessionHolderKind.Container, false, false, [], [Entity(9)]),
                new(Entity(1), SessionHolderKind.Container, false, false, [], []),
                new(Entity(2), SessionHolderKind.Container, false, false, [], []),
                new(Entity(3), SessionHolderKind.Container, false, false, [SessionScriptPoint.Use], []),
                new(Entity(4), SessionHolderKind.Container, false, false, [3], []),
                new(Entity(5), SessionHolderKind.Container, false, false, [], [Entity(10), Entity(11)]),
                new(Entity(6), SessionHolderKind.Container, false, false, [], []),
                new(Entity(7), SessionHolderKind.Container, false, false, [], []),
                new(Entity(8), SessionHolderKind.Container, false, false, [SessionScriptPoint.Use], []),
            ],
            [
                new(Entity(9), Facts(SessionItemType.Armor, 2805), null),
                new(Entity(10), Facts(SessionItemType.Weapon, 2002), null),
                new(Entity(11), Facts(SessionItemType.Armor, 2806), null),
            ]);
        var rows = new[]
        {
            Container(0, 100), Container(1, 101, LockRules.LockedBit), Container(2, 102, LockRules.JammedBit), Container(3, 103, script: SessionScriptPoint.Use),
            Container(4, 104, script: 3), Container(5, CrateX, RestockRules.GeneratedOnceFlag), Container(6, 108, LockRules.MagicallyHeldBit),
            Container(7, 109, AreaContainerLoot.StaysOpenFlag), Container(8, 110, RestockRules.GeneratedOnceFlag, script: SessionScriptPoint.Use), Item(9, 7, 100), Item(10, 5, CrateX), Item(11, 7, CrateX),
        }.ToDictionary(row => Entity(row.Key.SourceIndex));
        return new(1, seed, rows, restock: new Dictionary<string, AreaRestockSeed>
            { [Entity(5)] = new(CrateSource, RestockRules.GeneratedOnceFlag), [Entity(8)] = new(CrateSource, RestockRules.GeneratedOnceFlag) },
            persistent: [Entity(11)]);
    }

    private static CombatFields ItemFields(int type, int prototype) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields(type, prototype, 0, 0, 3000 + type, 33000 + type, 0, 40, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(0, 10, 100, 0, 0, 0, 0, [], 0, 0, 0, 0), null, null, null, null, null, null);

    // Set 5: 10..900 coins, an axe at rate 50 three times (the seed decides which).
    private static AreaBarterData Data() => AreaBarterData.Create(
        MesFile.Parse(Encoding.ASCII.GetBytes("{1}{A: 1,1}{2}{B: 1,1}{3}{C: 1,1}{4}{D: 1,1}{5}{Crate: 10,900 50,4020 50,4020 50,4020}")).File!,
        null, new Dictionary<int, CombatFields>
        {
            [Axe] = ItemFields((int)SessionItemType.Weapon, Axe),
            [AreaBarterData.GoldPrototype] = ItemFields((int)SessionItemType.Gold, AreaBarterData.GoldPrototype),
        });

    private sealed class Screen : IAreaRestockScreen
    {
        public int? ShownMap => 1;
        public AreaPixelRect? GameWindow => new(0, 0, 800, 600);
        public (double Width, double Height) WindowSize => (800, 600);
        public AreaPixelRect? BodyRect(AreaObjectKey key) => null;
    }

    private static readonly byte[] Name = [(byte)'S', (byte)'y', (byte)'n'];

    // A pre-made hero load whose prototype read carries a player name (or none).
    private static AreaHeroLoad Hero(IReadOnlyList<byte>? name = null)
    {
        var read = new PremadeHero(16069, 16089, 3, 1, 0, 20, 1032, 41, 0, new(67, true, []), null, 0, 0, []) { PlayerName = name ?? [] };
        return new(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!,
            new(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
                new(67, [new(CharacterStat.Dexterity, StatModifierOperator.Add, 1)]), null, 0, []) { Background = 41 }, 2352, 0,
            new PremadeHeroRead(PremadeHeroFailure.None, read), "synthetic");
    }

    private static (GameSessionOwner Session, AreaInventoryMap Map, AreaInventoryHost Inventory, AreaEventWorld World) Start(bool hero = false,
        IReadOnlyList<byte>? name = null, bool ready = true)
    {
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero ? Hero(name) : null);
        var session = created.Owner ?? throw new InvalidOperationException("SESSION_" + created.Failure);
        var map = Map();
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var host = new AreaRestockHost(session, inventory, Data(), new Screen());
        return (session, map, inventory, new AreaEventWorld(() => null, () => null, restock: () => ready ? host : null));
    }

    [TestMethod]
    public void A_press_on_a_standing_container_targets_its_loot_window()
    {
        var (session, map, inventory, _) = Start();
        using var owned = session;
        Assert.AreEqual(AreaInventoryTargetKind.Container, inventory.Target(session, 1, Key(0))?.Kind);
        Assert.AreEqual(AreaInventoryTargetKind.Container, inventory.Target(session, 1, Key(1))?.Kind, "a locked one too: the open refuses it");
        Assert.IsTrue(AreaContainerLoot.Lootable(session, map, Entity(0)));
        Assert.IsFalse(AreaContainerLoot.Lootable(session, map, Entity(9)), "an item is no container");
        Assert.AreSame(map, inventory.Map(1));
        Assert.IsNull(inventory.Map(2));
    }

    [TestMethod]
    public void An_unlocked_container_opens_a_frame_its_items_are_looted_and_its_close_shuts_it()
    {
        var (session, map, inventory, world) = Start();
        using var owned = session;
        var open = AreaContainerLoot.Open(session, map, Entity(0), frameCount: 2, world);
        Assert.AreEqual((AreaContainerOpenOutcome.Opened, true, false), (open.Outcome, open.Opening, open.LockedSound));
        Assert.IsNull(open.Generation, "no flag 0x200: the session is not asked");
        var openable = new SessionOpenable(new(1), Entity(0));
        var baseline = new SessionOpenableBaseline(OpenableKind.Container, 0, 0, 0);
        Assert.AreEqual(1, session.QueryOpenable(openable, baseline).Frame, "the next frame");
        Assert.AreEqual(GameSessionDisposition.Changed, inventory.Loot(session, Entity(0), Entity(9)).Disposition);
        Assert.AreEqual(0, inventory.View(session, null, 1, Entity(0)).Loot.Count);
        // An open container does not open again; its close steps back to frame 0, and a closed one has nothing to close.
        Assert.IsFalse(AreaContainerLoot.Open(session, map, Entity(0), 2, world).Opening);
        Assert.IsTrue(AreaContainerLoot.Close(session, map, Entity(0)));
        Assert.AreEqual(0, session.QueryOpenable(openable, baseline).Frame);
        Assert.IsFalse(AreaContainerLoot.Close(session, map, Entity(0)));
        // A container with flag 0x100 opens but stays open when its window closes.
        Assert.IsTrue(AreaContainerLoot.Open(session, map, Entity(7), 2, world).Opening);
        Assert.IsFalse(AreaContainerLoot.Close(session, map, Entity(7)));
        Assert.AreEqual(1, session.QueryOpenable(new(new(1), Entity(7)), new(OpenableKind.Container, AreaContainerLoot.StaysOpenFlag, 0, 0)).Frame);
        // An ART of one frame keeps frame 0, though the open sound plays.
        var single = AreaContainerLoot.Open(session, map, Entity(0), frameCount: 1, world);
        Assert.AreEqual((AreaContainerOpenOutcome.Opened, true), (single.Outcome, single.Opening));
        Assert.AreEqual(0, session.QueryOpenable(openable, baseline).Frame);
    }

    [TestMethod]
    public void A_locked_jammed_or_scripted_container_refuses_the_open_and_changes_nothing()
    {
        var (session, map, _, world) = Start();
        using var owned = session;
        var revision = session.Version.Revision;
        foreach (var (index, outcome, sound) in new[]
        {
            (1, AreaContainerOpenOutcome.Locked, true), (2, AreaContainerOpenOutcome.Jammed, true), (6, AreaContainerOpenOutcome.MagicallyHeld, true),
            (3, AreaContainerOpenOutcome.UseScriptUnsupported, false), (4, AreaContainerOpenOutcome.UnlockScriptUnsupported, false),
        })
        {
            var open = AreaContainerLoot.Open(session, map, Entity(index), 2, world);
            Assert.AreEqual((outcome, sound, (SessionGenerateOnceResult?)null), (open.Outcome, open.LockedSound, open.Generation), index.ToString());
        }
        Assert.AreEqual(AreaContainerOpenOutcome.Unavailable, AreaContainerLoot.Open(session, map, Entity(9), 2, world).Outcome);
        // The use script runs through the viewer's host: a run the host refuses keeps the container shut; the locked container's script
        // is never asked (the attempt comes first).
        var asked = new List<string>();
        AreaScriptedUse Refused(AreaObjectRecord row) { asked.Add("oid:" + row.Key.ObjectIdSha256); return new(AreaScriptedUseOutcome.Refused, null, [], null, "synthetic"); }
        var refused = AreaContainerLoot.Open(session, map, Entity(3), 2, world, Refused);
        Assert.AreEqual((AreaContainerOpenOutcome.UseScriptUnsupported, AreaScriptedUseOutcome.Refused), (refused.Outcome, refused.UseScript!.Outcome));
        Assert.AreEqual(AreaContainerOpenOutcome.Locked, AreaContainerLoot.Open(session, map, Entity(1), 2, world, Refused).Outcome);
        CollectionAssert.AreEqual(new[] { Entity(3) }, asked);
        Assert.AreEqual(revision, session.Version.Revision);
        // A run that ends without skip default lets the open go on.
        var ran = AreaContainerLoot.Open(session, map, Entity(3), 2, world, _ => new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "synthetic"));
        Assert.AreEqual(AreaContainerOpenOutcome.Opened, ran.Outcome);
        StringAssert.Contains(ran.Detail, "use-script=run-default");
    }

    [TestMethod]
    public void A_trap_met_on_the_use_keeps_the_container_shut()
    {
        // ARC-WORLD-110: the use-script host's trap meeting comes before the use script; one that cancels the use refuses the open.
        var (session, map, _, world) = Start();
        using var owned = session;
        var run = TrapRules.NativeRun(30001, 0x1)!.Value;
        AreaScriptedUse Met(SessionTrapOutcome outcome, NativeTrapRun? fired, GameSessionDisposition disposition = GameSessionDisposition.NoChange) =>
            new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "synthetic")
            {
                Trap = new(Container(3, 103), SessionTrapCause.Use, new(new(disposition, GameSessionFailure.None, session.Version), outcome, fired,
                    new(TrapClass.Nonmagical, 30001, 0, false)), null, null),
            };
        foreach (var (used, outcome) in new[]
        {
            (Met(SessionTrapOutcome.Spotted, null, GameSessionDisposition.Changed), AreaContainerOpenOutcome.TrapSpotted),
            (Met(SessionTrapOutcome.Fired, run), AreaContainerOpenOutcome.TrapFired),
            (Met(SessionTrapOutcome.NoTrap, null, GameSessionDisposition.Rejected), AreaContainerOpenOutcome.TrapRefused),
        })
        {
            var open = AreaContainerLoot.Open(session, map, Entity(3), 2, world, _ => used);
            Assert.AreEqual((outcome, false, false), (open.Outcome, open.Opening, open.LockedSound), outcome.ToString());
            Assert.AreSame(used, open.UseScript);
        }
        // An ordinary trap script that fired ran as the use script: its run decides.
        Assert.AreEqual(AreaContainerOpenOutcome.Opened, AreaContainerLoot.Open(session, map, Entity(3), 2, world, _ => Met(SessionTrapOutcome.Fired, null)).Outcome);
    }

    [TestMethod]
    public void The_first_open_generates_a_flag_0x200_container_from_the_tile_x_and_the_hero_s_name()
    {
        // Without a hero the seed is the tile x alone (RK10), and the host's name is empty.
        var (session, map, inventory, world) = Start();
        using var owned = session;
        var facts = world.GeneratedOnceFacts(new(1), Entity(5))!;
        Assert.AreEqual((CrateX, 0), (facts.LocationX, facts.PlayerName.Count));
        Assert.IsNull(world.GeneratedOnceFacts(new(1), Entity(9)), "no container");
        var open = AreaContainerLoot.Open(session, map, Entity(5), 2, world);
        Assert.AreEqual((AreaContainerOpenOutcome.Opened, GameSessionDisposition.Changed), (open.Outcome, open.Generation!.Disposition));
        var done = open.Generation.Regeneration!;
        var set = AreaRestockHostSet();
        var loot = RestockRules.GenerateOnce(set, CrateX);
        Assert.AreEqual((SessionRestockOutcome.Regenerated, 1, loot.Coins, loot.BasicPrototypes.Count), (done.Outcome, done.Removed, done.Coins, done.Created.Count));
        Assert.IsTrue(session.QueryContainerGenerated(new(1), Entity(5)));
        Assert.AreEqual(GameSessionDisposition.NoChange, AreaRestockHost.Schedule(session, inventory, Entity(5))?.Disposition, "no source after it (RK12)");
        // The kept amulet and the new goods are looted; the destroyed axe is gone.
        Assert.AreEqual(GameSessionDisposition.Changed, inventory.Loot(session, Entity(5), Entity(11)).Disposition);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, inventory.Loot(session, Entity(5), Entity(10)).Failure);
        Assert.IsTrue(loot.BasicPrototypes.Count > 0, "the fixture's seed creates at least one axe");
        Assert.AreEqual(GameSessionDisposition.Changed, inventory.Loot(session, Entity(5), done.Created[0]).Disposition);
        Assert.AreEqual(GameSessionFailure.GoldUnavailable, session.TakeGoldFromNpc(session.Version, Entity(5), 1, done.Pile).Failure, "no hero holds gold");
    }

    [TestMethod]
    public void A_pre_made_hero_starts_with_its_prototype_name_which_seeds_the_generation()
    {
        // ARC-WORLD-101: CreateSession sets the session hero's player name from the pre-made prototype (field 0x10E); the restock host
        // gives the session's name; a hero without one gives an empty name, so the seed has no name term.
        var (session, map, _, world) = Start(hero: true, name: Name);
        using (session)
        {
            CollectionAssert.AreEqual(Name, session.QueryHero()!.PlayerName.ToArray());
            CollectionAssert.AreEqual(Name, world.GeneratedOnceFacts(new(1), Entity(5))!.PlayerName.ToArray());
            var done = AreaContainerLoot.Open(session, map, Entity(5), 2, world).Generation!.Regeneration!;
            Assert.AreEqual(SessionRestockOutcome.Regenerated, done.Outcome);
            // Its coin pile moves through the quantity window's gold take with the pile given.
            Assert.IsNotNull(done.Pile);
            Assert.AreEqual(GameSessionDisposition.Changed, session.TakeGoldFromNpc(session.Version, Entity(5), done.Coins, done.Pile).Disposition);
            Assert.AreEqual(done.Coins, session.QueryGold());
        }
        var (unnamed, _, _, unnamedWorld) = Start(hero: true, name: null);
        using (unnamed)
        {
            Assert.AreEqual(0, unnamed.QueryHero()!.PlayerName.Length);
            Assert.AreEqual((CrateX, 0), (unnamedWorld.GeneratedOnceFacts(new(1), Entity(5))!.LocationX, unnamedWorld.GeneratedOnceFacts(new(1), Entity(5))!.PlayerName.Count));
        }
        // A name the session refuses (over 64 bytes) fails the start.
        var refused = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, Hero(Enumerable.Repeat((byte)'A', SessionHeroLimits.MaxPlayerNameBytes + 1).ToArray()));
        Assert.AreEqual((null, GameSessionFailure.InvalidPlayerName), (refused.Owner, refused.Failure));
    }

    [TestMethod]
    public void A_restock_host_not_ready_refuses_the_open_before_any_change()
    {
        var (session, map, _, world) = Start(ready: false);
        using var owned = session;
        var revision = session.Version.Revision;
        var open = AreaContainerLoot.Open(session, map, Entity(5), 2, world);
        Assert.AreEqual((AreaContainerOpenOutcome.GenerationRefused, (SessionGenerateOnceResult?)null), (open.Outcome, open.Generation));
        Assert.AreEqual(revision, session.Version.Revision, "no frame was written");
        // Refused before the use script: a retry does not run it twice.
        var runs = 0;
        AreaScriptedUse Count(AreaObjectRecord row) { runs++; return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "synthetic"); }
        Assert.AreEqual(AreaContainerOpenOutcome.GenerationRefused, AreaContainerLoot.Open(session, map, Entity(8), 2, world, Count).Outcome);
        Assert.AreEqual(AreaContainerOpenOutcome.GenerationRefused, AreaContainerLoot.Open(session, map, Entity(8), 2, world, Count).Outcome);
        Assert.AreEqual(0, runs);
        // Once the host is ready, the crate's script runs once and the open goes on.
        var (readySession, readyMap, _, readyWorld) = Start();
        using (readySession)
        {
            Assert.AreEqual(AreaContainerOpenOutcome.Opened, AreaContainerLoot.Open(readySession, readyMap, Entity(8), 2, readyWorld, Count).Outcome);
            Assert.AreEqual(1, runs);
        }
        // A container without flag 0x200 still opens: the generation changes nothing there.
        Assert.AreEqual(AreaContainerOpenOutcome.Opened, AreaContainerLoot.Open(session, map, Entity(0), 2, world).Outcome);
    }

    private static RestockSet AreaRestockHostSet() => new(10, 900, [new(50, Axe - 20), new(50, Axe - 20), new(50, Axe - 20)]);
}
