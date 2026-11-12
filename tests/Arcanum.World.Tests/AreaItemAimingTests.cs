using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-DEV-365 (T-g, ledger IA3, IA4, IA8, IA9): the item cursor's mask, begin and apply choices.</summary>
[TestClass]
public sealed class AreaItemAimingTests
{
    private const ulong Object = SpellUiRules.ObjectBit, Tile = SpellUiRules.TileBit;

    [TestMethod]
    public void TheMaskIsTheSlotZeroSpellsForAChargedItemAnObjectOrTileForATrapDeviceAndAnyObjectForAnItemWithNoSpell()
    {
        Assert.AreEqual(Object | SpellUiRules.RadiusBit, AreaItemAiming.Mask(true, false, Object | SpellUiRules.RadiusBit));
        Assert.AreEqual(Object | Tile, AreaItemAiming.Mask(true, true, Object | Tile), "charges decide first: a charged trap device takes its spell's mask");
        Assert.AreEqual(Object | Tile, AreaItemAiming.Mask(false, true, null), "a trap device: object or tile (0x5CAC50)");
        Assert.AreEqual(Object, AreaItemAiming.Mask(false, false, null), "no slot-0 spell: any object (0x5CAC40), the target reaches the use script");
        Assert.AreEqual(Tile, AreaItemAiming.Mask(false, false, Tile), "otherwise the slot-0 spell's mask");
        Assert.AreEqual(0UL, AreaItemAiming.Mask(true, false, null), "an empty slot 0 of a charged item reads before the table (TP1): an apply at once");
    }

    [TestMethod]
    public void TheBeginAppliesAtOnceForMasksTheSpellActivationAppliesAtOnceAndOpensTheCursorOtherwise()
    {
        Assert.AreEqual(AreaItemAimBegin.Nothing, AreaItemAiming.Begin(Object, false, false, true), "a dead or unconscious user: silent");
        Assert.AreEqual(AreaItemAimBegin.UseAtOwnTile, AreaItemAiming.Begin(0, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.UseAtOwnTile, AreaItemAiming.Begin(SpellUiRules.RadiusBit, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.UseAtOwnTile, AreaItemAiming.Begin(SpellUiRules.SelfBit, true, false, true), "self without the tile bit");
        Assert.AreEqual(AreaItemAimBegin.OpenCursor, AreaItemAiming.Begin(SpellUiRules.SelfBit | Tile, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.NoActionPoints, AreaItemAiming.Begin(Object, true, false, false));
        Assert.AreEqual(AreaItemAimBegin.OpenCursor, AreaItemAiming.Begin(Object, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.InventoryTarget, AreaItemAiming.Begin(SpellUiRules.InventoryBit | Object, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.UseAtOwnTile, AreaItemAiming.Begin(SpellUiRules.InventoryBit | SpellUiRules.SelfBit, true, false, true), "IA4: the at-once test still applies with the inventory bit");
    }

    [TestMethod]
    public void LockpicksOpenTheCursorWithTheObjectMaskAndTheFourActionPointsRefuseItAsAnyItemWithNoSpell()
    {
        // ARC-INPUT-1123: lockpicks are an item with no spell, so the mask is any object (IA3) and the begin asks the action points (IA4).
        var mask = AreaItemAiming.Mask(false, false, null);
        Assert.AreEqual(Object, mask);
        Assert.AreEqual(AreaItemAimBegin.OpenCursor, AreaItemAiming.Begin(mask, true, false, true));
        Assert.AreEqual(AreaItemAimBegin.NoActionPoints, AreaItemAiming.Begin(mask, true, false, false));
    }

    [TestMethod]
    public void ShiftWithTheObjectBitAppliesToThePlayerAtOnceWithNoSelfTestAndOnlyAfterTheActionPointCheck()
    {
        Assert.AreEqual(AreaItemAimBegin.UseOnPlayer, AreaItemAiming.Begin(Object, true, true, true));
        Assert.AreEqual(AreaItemAimBegin.UseOnPlayer, AreaItemAiming.Begin(Object | SpellUiRules.NoSelfBit, true, true, true), "IA4: unlike the spell cursor, the item path does not test Tgt_Obj_No_Self");
        Assert.AreEqual(AreaItemAimBegin.OpenCursor, AreaItemAiming.Begin(Tile, true, true, true), "no object bit");
        Assert.AreEqual(AreaItemAimBegin.NoActionPoints, AreaItemAiming.Begin(Object, true, true, false));
    }

    [TestMethod]
    public void AnObjectIsUsedAtOnceInRangeAndWalkedToOutOfRangeOrWithTheNoRangedUseFlag()
    {
        Assert.AreEqual(AreaItemAimObject.UseNow, AreaItemAiming.ApplyOnObject(true, true, false));
        Assert.AreEqual(AreaItemAimObject.WalkThenUse, AreaItemAiming.ApplyOnObject(true, false, false), "out of the spell's range");
        Assert.AreEqual(AreaItemAimObject.WalkThenUse, AreaItemAiming.ApplyOnObject(true, true, true), "OIF_NO_RANGED_USE");
        Assert.AreEqual(AreaItemAimObject.UseNow, AreaItemAiming.ApplyOnObject(false, false, false), "no charges and not magical: the range is not compared");
        Assert.AreEqual(AreaItemAimObject.WalkThenUse, AreaItemAiming.ApplyOnObject(false, false, true));
    }

    [TestMethod]
    public void ATileIsUsedAtOnceForAChargedItemWalkedToForATrapDeviceAndThrownForAnythingElse()
    {
        Assert.AreEqual(AreaItemAimTile.UseNow, AreaItemAiming.ApplyOnTile(true, false));
        Assert.AreEqual(AreaItemAimTile.UseNow, AreaItemAiming.ApplyOnTile(true, true));
        Assert.AreEqual(AreaItemAimTile.TrapGoal, AreaItemAiming.ApplyOnTile(false, true));
        Assert.AreEqual(AreaItemAimTile.Throw, AreaItemAiming.ApplyOnTile(false, false));
    }

    [TestMethod]
    public void TheNoRangedUseFlagIsReadFromTheItemFlags()
    {
        var facts = new AreaItemUseFacts("kit:1", AreaItemUseFacts.FoodType, AreaItemUseFacts.NeedsTargetFlag | AreaItemUseFacts.NoRangedUseFlag, 0, false, false);
        Assert.IsTrue(facts.NoRangedUse);
        Assert.IsTrue(facts.NeedsTarget);
        Assert.IsFalse(facts with { ItemFlags = AreaItemUseFacts.NeedsTargetFlag } is { NoRangedUse: true });
    }
}
