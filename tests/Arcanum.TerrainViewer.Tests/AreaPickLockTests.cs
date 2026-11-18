using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-SKILLS-006: the viewer's bridge from stored fields to lockpicks and the kit's lockpicks (invented values).
[TestClass]
public sealed class AreaPickLockTests
{
    private static CombatFields Generic(uint flags, int bonus = 5, int type = AreaHealingItems.GenericType, IReadOnlyList<int>? scripts = null) =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(type, 15104, 0, 0, 0, 0, 0, 10, 0, 2, 4, [0, 0, 0, 0, 0], 0) { ScriptPoints = scripts ?? [] },
            new CombatItemFields(0, 1, 1, 0, 0, 0, 3, [], 0, 0, 0, 0), null, null, null, new CombatGenericFields(flags, bonus, 0), null, null);

    [TestMethod]
    public void A_generic_item_with_the_lockpick_flag_becomes_lockpicks_and_marks_its_kit_line()
    {
        var picks = AreaLockpicks.From("kit:1", Generic(0x02 | 0x01, bonus: 5, scripts: [2]))!;
        Assert.AreEqual(("kit:1", 0x03u, 5, true), (picks.Entity, picks.Flags, picks.UsageBonus, picks.Lockpick));
        Assert.AreEqual((10, 2), (picks.Condition.Points, picks.Condition.Damage));
        Assert.IsNull(AreaLockpicks.From("x", Generic(0x08)), "a healing item is not lockpicks");
        Assert.IsNull(AreaLockpicks.From("x", Generic(0x02, type: 9)), "food is not a generic item");

        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Generic, 15104, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var kit = new[] { picks };
        var host = new AreaInventoryHost([]);
        Assert.AreEqual("kit:1", host.Lockpick(session, "kit:1", kit)!.Entity);
        var line = host.View(session, null, 1, null, null, kit).Pc.Single();
        Assert.AreEqual((true, false), (line.Lockpick, line.Healing));
        Assert.IsFalse(host.View(session, null, 1).Pc.Single().Lockpick);
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(host.Lockpick(heroless, "kit:1", kit));
    }

    // ARC-COMBAT-1878 (the finder 0x462760): the PC's first lockpicks in inventory order, armed or not.
    [TestMethod]
    public void The_first_lockpicks_of_the_hero_are_the_first_in_inventory_order_and_none_without_a_hero()
    {
        var first = AreaLockpicks.From("kit:2", Generic(0x02, bonus: 5))!;
        var second = AreaLockpicks.From("kit:3", Generic(0x02, bonus: 9))!;
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Generic, 15100, 6000, 6001)), new(new(SessionItemType.Generic, 15104, 6000, 6001)), new(new(SessionItemType.Generic, 15104, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var host = new AreaInventoryHost([]);
        Assert.AreEqual("kit:2", host.FirstLockpicks(session, [null, first, second])!.Entity, "the first of two lockpicks, skipping the kit item that is none");
        Assert.AreEqual("kit:3", host.FirstLockpicks(session, [null, null, second])!.Entity);
        Assert.IsNull(host.FirstLockpicks(session, [null, null, null]), "none held");
        Assert.IsNull(host.FirstLockpicks(session, null), "no kit facts");
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(host.FirstLockpicks(heroless, [null, first, second]));
    }

    /// <summary>Opt-in (DAT_ROOT = the game folder): only the halfling-male kit holds lockpicks (first observation 2026-10-21),
    /// and a pre-made hero picks the first locked door with a lock difficulty of the installed interiors through the viewer's
    /// portal path, with the session's answer matching the door's state. Counts and outcomes only.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Original_kits_and_a_locked_door_of_the_interiors()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var kits = AreaStartingHero.Premade.Select(premade => (premade.Name, Count: AreaStartingHero.Load(module, premade).KitLockpicks!.Count(item => item is not null)))
            .Where(kit => kit.Count > 0).Select(kit => $"{kit.Name} {kit.Count}").ToArray();
        Console.WriteLine($"kit lockpicks: {string.Join(", ", kits)}");
        Assert.AreEqual("halfling-male 1", string.Join(", ", kits), "the census changed: update the contract");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        foreach (var map in WorldCatalog.Open(stack).Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            TerrainLoadResult result;
            try
            {
                result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module,
                    "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
                    "--game-root", root, "--area-map", map.Name]), default);
            }
            catch (Exception error) when (error is InvalidOperationException or Arcanum.Data.Area.AreaException) { continue; }
            var area = result.Area!;
            var portals = AreaPortals.From(area.Snapshot, area.Plan);
            var hero = AreaStartingHero.Load(module, AreaStartingHero.Find("dwarf-male")!);
            using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
                AreaGridSession.Cell(AreaGridSession.Spawn), area.Snapshot.RuntimeMap, hero).Owner!;
            var door = portals.All.FirstOrDefault(p => p.LockDifficulty > 0 && session.QueryOpenable(portals.Openable(p), p.Baseline).Locked);
            if (door is null) continue;
            var picked = portals.Pick(session, door, null);
            Assert.AreEqual(GameSessionDisposition.Changed, picked.Disposition, picked.Failure.ToString());
            Assert.AreEqual(picked.Outcome!.Value.Result != PickLockResult.Unlocked, picked.State!.Value.Locked, "the state follows the outcome");
            Console.WriteLine($"map {map.Name}: door difficulty {door.LockDifficulty}, success {picked.Check!.Success}, result {picked.Outcome.Value.Result}");
            return;
        }
        Assert.Fail("no locked door with a lock difficulty on the interiors");
    }
}
