using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-SKILLS-005: the viewer's bridge from stored fields to a healing item (invented values).
[TestClass]
public sealed class AreaHealingItemsTests
{
    private static CombatFields Generic(uint flags, int bonus = 5, int uses = 3, int type = AreaHealingItems.GenericType, IReadOnlyList<int>? scripts = null) =>
        new(CombatFieldOrigin.AreaObject, new CombatObjectFields(type, 15199, 0, 0, 0, 0, 0, 10, 0, 2, 4, [0, 0, 0, 0, 0], 0) { ScriptPoints = scripts ?? [] },
            new CombatItemFields(0, 1, 1, 0, 0, 0, 3, [], 0, 0, 0, 0), null, null, null, new CombatGenericFields(flags, bonus, uses), null, null);

    [TestMethod]
    public void A_generic_item_with_the_healing_flag_becomes_a_healing_item()
    {
        var item = AreaHealingItems.From("kit:4", Generic(0x08 | 0x01, bonus: 5, uses: 3, scripts: [2]))!;
        Assert.AreEqual(("kit:4", 0x09u, 5, 3), (item.Entity, item.Flags, item.UsageBonus, item.Uses));
        Assert.AreEqual((10, 2), (item.Condition.Points, item.Condition.Damage));
        CollectionAssert.AreEqual(new[] { 2 }, item.ScriptPoints.ToArray());
        Assert.IsNull(AreaHealingItems.From("x", Generic(0x02)), "a lockpick is not a healing item");
        Assert.IsNull(AreaHealingItems.From("x", Generic(0x08, type: 9)), "food is not a generic item");
    }

    [TestMethod]
    public void A_kit_healing_item_is_found_by_entity_and_marks_its_window_line()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Generic, 15199, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var kit = new SessionHealingItem?[] { AreaHealingItems.From("kit:1", Generic(0x08)) };
        var host = new AreaInventoryHost([]);
        Assert.AreEqual("kit:1", host.HealingItem(session, "kit:1", kit)!.Entity);
        Assert.IsNull(host.HealingItem(session, "kit:1", null), "no kit facts without a hero load");
        Assert.IsTrue(host.View(session, null, 1, null, kit).Pc.Single().Healing);
        Assert.IsFalse(host.View(session, null, 1).Pc.Single().Healing);
        using var heroless = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1).Owner!;
        Assert.IsNull(host.HealingItem(heroless, "kit:1", kit), "nobody to use it without a hero");
    }
}
