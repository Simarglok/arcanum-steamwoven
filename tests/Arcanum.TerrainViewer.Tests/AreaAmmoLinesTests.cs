using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-066: the inventory and loot windows show an ammunition stack's units (invented seed: a corpse with cells, cells and
// bullets on the ground).
[TestClass]
public sealed class AreaAmmoLinesTests
{
    private static SessionItemFacts Ammo(int prototype, int type, int units) =>
        new(new(SessionItemType.Ammo, prototype, 100, 200), 1, 0, 0, units, []) { AmmoType = type };

    [TestMethod]
    public void Ammunition_lines_show_their_units_and_a_merge_adds_them()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var spawn = AreaGridSession.Cell(AreaGridSession.Spawn);
        var seed = new SessionMapInventorySeed([new("corpse", SessionHolderKind.Npc, true, false, [], ["corpse-cells"])],
            [new("cells", Ammo(7060, 2, 10), new CellCoord(checked((int)spawn.X), checked((int)spawn.Y))), new("corpse-cells", Ammo(7060, 2, 5), null)]);
        Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(1), seed).Disposition);
        var host = new AreaInventoryHost([]);
        StringAssert.Contains(host.View(session, null, 1, "corpse").Loot.Single().Text, "×5");
        Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, "cells", _ => SessionInsertVerdict.Accepted).Disposition);
        StringAssert.Contains(host.View(session, null, 1).Pc.Single().Text, "×10");
        Assert.AreEqual(GameSessionDisposition.Changed, session.LootItem(session.Version, "corpse", "corpse-cells", _ => SessionInsertVerdict.Accepted).Disposition);
        var view = host.View(session, null, 1, "corpse");
        Assert.AreEqual(0, view.Loot.Count);
        StringAssert.Contains(view.Pc.Single().Text, "×15", "the looted cells joined the held stack");
    }
}
