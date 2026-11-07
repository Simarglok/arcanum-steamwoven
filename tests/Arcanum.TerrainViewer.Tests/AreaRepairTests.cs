using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-SKILLS-007: the inventory window's repair line over the session's condition of a kit item (invented values).
[TestClass]
public sealed class AreaRepairTests
{
    [TestMethod]
    public void A_damaged_kit_item_is_repairable_and_its_baseline_comes_from_the_hero_load()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Weapon, 4027, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var baseline = new ItemCondition(40, 10, 0, false);
        var kit = new SessionWearItem?[] { new("kit:1", baseline) };
        var host = new AreaInventoryHost([]);
        Assert.AreEqual(baseline, host.RepairBaseline(session, "kit:1", kit));
        Assert.IsFalse(host.View(session, null, 1, null, null, null, kit).Pc.Single().Repairable, "undamaged");
        Assert.AreEqual(GameSessionDisposition.Changed, session.DamageItem(session.Version, "kit:1", baseline, 12).Disposition);
        Assert.IsTrue(host.View(session, null, 1, null, null, null, kit).Pc.Single().Repairable);
        Assert.IsFalse(host.View(session, null, 1).Pc.Single().Repairable, "no kit condition without the hero load's");
        var repaired = session.RepairWithSkill(session.Version, "kit:1", host.RepairBaseline(session, "kit:1", kit)!.Value);
        Assert.AreEqual((GameSessionDisposition.Changed, ItemRepairStatus.Repaired), (repaired.Disposition, repaired.Repair));
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(host.RepairBaseline(heroless, "kit:1", kit));
    }
}
