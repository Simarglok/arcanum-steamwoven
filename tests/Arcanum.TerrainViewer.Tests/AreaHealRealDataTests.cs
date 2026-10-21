using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Inventory;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SKILLS-005 on the installed original (opt-in, <c>ARC_AREA_CONVERSATION_GAME_ROOT</c>): the healing items of every pre-made
/// hero's kit and of the Crash Site exterior's seed (any holder or the ground), then a hero takes one and heals itself with it
/// through the viewer's inventory host. Counts only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaHealRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Healing_items_of_the_kits_and_the_crash_site_and_a_self_heal()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        var kits = AreaStartingHero.Premade.Sum(premade => AreaStartingHero.Load(module, premade).KitHealing!.Count(item => item is not null));
        const string Mobile = "modules/Arcanum/maps/qrmvs-420-1zjcnpgn";
        using var data = AreaOriginalValidator.OpenFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), P(Mobile), P("data/proto"));
        var snapshot = data.LoadStrict();
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, module, P("arcanum2.dat"), P("arcanum3.dat"), P("modules/Arcanum.PATCH0"), P(Mobile), 1);
        var map = AreaInventoryMap.Build(snapshot, steps).Map!;
        var healing = map.Seed.Items.Where(item => CombatFieldReader.Read(map.Row(item.Entity)).Fields is { } fields && AreaHealingItems.From(item.Entity, fields) is not null)
            .ToArray();
        var ground = healing.Where(item => item.Cell is not null).ToArray();
        Console.WriteLine($"kit healing items {kits}; crash site healing items {healing.Length} (ground {ground.Length})");
        // First observation (2026-10-21): no pre-made hero's kit and nothing on the Crash Site exterior is a healing item.
        Assert.AreEqual((0, 0), (kits, healing.Length), "the census changed: update the contract and the self-heal check below");

        var hero = AreaStartingHero.Load(module, AreaStartingHero.Find("half-elf-male")!);
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), snapshot.RuntimeMap, hero).Owner!;
        var host = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        var taken = healing.Select(item => item.Entity).FirstOrDefault(entity =>
            (ground.Any(g => g.Entity == entity) ? host.PickUp(session, entity)
                : host.Loot(session, map.Seed.Holders.First(h => h.Items.Contains(entity)).Entity, entity)).Disposition == GameSessionDisposition.Changed);
        // The self-heal below runs only once an installation has a takeable healing item on the Crash Site (the census above).
        if (taken is null) { Assert.AreEqual(0, healing.Length, "a healing item the hero could take"); return; }
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 5 }).Disposition);
        var item = host.HealingItem(session, taken, hero.KitHealing)!;
        Assert.IsTrue(host.View(session, null, snapshot.RuntimeMap, null, hero.KitHealing).Pc.Single(line => line.Entity == taken).Healing);
        var result = session.HealWithItem(session.Version, SessionLifeTarget.Hero, item);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, result.Failure.ToString());
        Assert.AreEqual(item.Uses - result.Heal!.Value.UsesSpent, result.UsesLeft ?? 0);
        Console.WriteLine($"self heal: success {result.Check!.Success}, healed {result.Heal.Value.HitPoints}, uses {item.Uses} -> {result.UsesLeft}");
    }
}
