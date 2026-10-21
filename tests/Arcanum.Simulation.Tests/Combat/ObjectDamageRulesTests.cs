using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>ARC-COMBAT-008: hit-point damage and breaks of non-critter objects (ledger "Object damage").</summary>
[TestClass]
public sealed class ObjectDamageRulesTests
{
    private static DamageableObject Of(DamageableObjectKind kind, int max = 20) => new() { Kind = kind, MaxHitPoints = max };

    [TestMethod]
    public void AHitAddsDamageAndBreaksAtZeroHitPoints()
    {
        var door = Of(DamageableObjectKind.Portal);
        Assert.AreEqual(new ObjectHitOutcome(0, 19, false, ObjectBreak.None, 19), ObjectDamageRules.Hit(door, 0, 19));
        Assert.AreEqual(new ObjectHitOutcome(19, 20, true, ObjectBreak.Destroyed, 20), ObjectDamageRules.Hit(door, 19, 1), "current 0 breaks");
        Assert.AreEqual(new ObjectHitOutcome(5, 5, false, ObjectBreak.None, 5), ObjectDamageRules.Hit(door, 5, 0));
        Assert.AreEqual(new ObjectHitOutcome(5, 5, false, ObjectBreak.None, 5), ObjectDamageRules.Hit(door, 5, -7), "a negative amount adds nothing");
        Assert.IsTrue(ObjectDamageRules.Hit(Of(DamageableObjectKind.Portal, 0), 0, 0).Broken, "no hit points at all breaks on any hit");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ObjectDamageRules.Hit(door, -1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ObjectDamageRules.Hit(door, 0, 100_001));
    }

    [TestMethod]
    public void EachKindBreaksAsTheOriginal()
    {
        (ObjectBreak, int) Break(DamageableObject target, int damage = 25) => ObjectDamageRules.Break(target, damage);
        Assert.AreEqual((ObjectBreak.Destroyed, 25), Break(Of(DamageableObjectKind.Portal)), "a door is destroyed");
        Assert.AreEqual((ObjectBreak.WindowBusted, 25), Break(Of(DamageableObjectKind.Portal) with { Window = true }), "a window busts, damage kept");
        Assert.AreEqual((ObjectBreak.Spared, 0), Break(Of(DamageableObjectKind.Container) with { JunkPile = true }), "a junk pile is spared");
        Assert.AreEqual((ObjectBreak.ContainerSpilled, 25), Break(Of(DamageableObjectKind.Container)));
        Assert.AreEqual((ObjectBreak.SceneryRespawns, 0), Break(Of(DamageableObjectKind.Scenery) with { Respawning = true, Busted = true }),
            "respawning wins over busted");
        Assert.AreEqual((ObjectBreak.SceneryBusted, 20), Break(Of(DamageableObjectKind.Scenery)), "damage set to the maximum");
        Assert.AreEqual((ObjectBreak.SceneryBusted, 0), Break(Of(DamageableObjectKind.Scenery, -3)), "a maximum below 0 stores 0");
        Assert.AreEqual((ObjectBreak.Destroyed, 25), Break(Of(DamageableObjectKind.Scenery) with { Busted = true }));
        Assert.AreEqual((ObjectBreak.Destroyed, 25), Break(Of(DamageableObjectKind.Trap)), "no destroyed ART");
        Assert.AreEqual((ObjectBreak.TrapBusted, 0), Break(Of(DamageableObjectKind.Trap) with { HasDestroyedArt = true }));
        Assert.AreEqual((ObjectBreak.Destroyed, 25), Break(Of(DamageableObjectKind.Trap) with { HasDestroyedArt = true, Busted = true }));
        Assert.AreEqual((ObjectBreak.ItemBreak, 25), Break(Of(DamageableObjectKind.Item)));
        Assert.IsTrue(ObjectDamageRules.Removes(ObjectBreak.Destroyed) && ObjectDamageRules.Removes(ObjectBreak.ContainerSpilled));
        Assert.IsFalse(ObjectDamageRules.Removes(ObjectBreak.WindowBusted) || ObjectDamageRules.Removes(ObjectBreak.SceneryRespawns));
    }

    [TestMethod]
    public void FactsMustBelongToTheKind()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ObjectDamageRules.Break(Of(DamageableObjectKind.Container) with { Window = true }, 0));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectDamageRules.Break(Of(DamageableObjectKind.Portal) with { JunkPile = true }, 0));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectDamageRules.Break(Of(DamageableObjectKind.Trap) with { Respawning = true }, 0));
        Assert.ThrowsExactly<ArgumentException>(() => ObjectDamageRules.Break(Of(DamageableObjectKind.Item) with { Busted = true }, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ObjectDamageRules.Break(Of((DamageableObjectKind)9), 0));
    }

    [TestMethod]
    public void RespawnDelayBySign()
    {
        Assert.AreEqual(86_400_001L, ObjectDamageRules.RespawnDelayMs(0), "one day and 1 ms");
        Assert.AreEqual(8_000L, ObjectDamageRules.RespawnDelayMs(1), "seconds at the game rate");
        Assert.AreEqual(80_000L, ObjectDamageRules.RespawnDelayMs(10));
        Assert.AreEqual(60_000L, ObjectDamageRules.RespawnDelayMs(-1), "game minutes");
        Assert.AreEqual(int.MaxValue * 8_000L, ObjectDamageRules.RespawnDelayMs(int.MaxValue));
        Assert.AreEqual(2_147_483_648L * 60_000L, ObjectDamageRules.RespawnDelayMs(int.MinValue));
    }
}
