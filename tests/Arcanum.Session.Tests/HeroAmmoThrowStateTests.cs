using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-086: the hero throws part or all of an ammunition stack at a location, after the lift's quantity question (ledger QW1, QW5, TT1, TT2).
// The half-elf hero takes a stack of 30 bullets from virgil first (made:2), as HeroAmmoDropStateTests. Entities and numbers are invented.
[TestClass]
public sealed class HeroAmmoThrowStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), null);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.AreEqual("made:2", owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 30, Bullets, Accept).Item);
        return owner;
    }

    private static CellCoord At(GameSessionOwner owner, int dx) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    private static SessionHeroThrow Throw(GameSessionOwner owner, int? count, SessionItemKind? kind = null, bool noDrop = false) =>
        new("made:2", new(Bullets, noDrop, []), At(owner, 3)) { Count = count, SplitKind = kind ?? Bullets };

    [TestMethod]
    public void Part_of_a_stack_is_split_off_and_thrown_as_a_new_stack_that_lands_where_it_ends()
    {
        using var owner = Hero();
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, 12));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        var shot = thrown.Shots.Single();
        Assert.AreEqual(("made:3", 18, 12), (owner.QueryThrownShot(shot)!.Value.Item, owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:3")!.Value.Units),
            "the new stack flies, the rest stays in the hero's list");
        CollectionAssert.AreEqual(new[] { "made:2" }, owner.QueryHeroAmmo().Select(stack => stack.Entity).ToArray());
        Assert.AreEqual(At(owner, 3), thrown.Destination);
        // It lands where its flight ends, with its units, as a placed made item.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, At(owner, 3)).Disposition);
        var placed = owner.QueryPlacedItems().Single(item => item.Entity == "made:3");
        Assert.AreEqual((At(owner, 3), 12), (placed.Cell, owner.QueryAmmo("made:3")!.Value.Units));
    }

    [TestMethod]
    public void The_whole_stack_goes_and_a_no_drop_flag_does_not_refuse_the_stack_path()
    {
        using var owner = Hero();
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, 30, noDrop: true));
        Assert.AreEqual(GameSessionFailure.None, thrown.Failure);
        Assert.IsTrue(owner.IsItemDestroyed("made:2"), "exactly the count deletes the source stack");
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count);
        Assert.AreEqual(30, owner.QueryAmmo("made:3")!.Value.Units);
    }

    [TestMethod]
    public void The_throw_of_a_stack_is_refused_before_any_effect()
    {
        using var owner = Hero();
        var before = owner.Version;
        Assert.AreEqual(GameSessionFailure.AttackUnsupported, owner.ThrowHeroItem(owner.Version, Throw(owner, null)).Failure, "no count: as before");
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.ThrowHeroItem(owner.Version, Throw(owner, 0)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.ThrowHeroItem(owner.Version, Throw(owner, 5, new(SessionItemType.Generic, 1, 0, 0))).Failure, "the split kind is ammunition");
        Assert.AreEqual(GameSessionFailure.AmmoShort, owner.ThrowHeroItem(owner.Version, Throw(owner, 31)).Failure);
        Assert.AreEqual(before, owner.Version);
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
        Assert.IsNull(owner.QueryAmmo("made:3"));
    }

    [TestMethod]
    public void A_throw_script_without_a_script_host_refuses_the_stack_before_any_split()
    {
        using var owner = Hero();
        var request = Throw(owner, 5) with { Facts = new(Bullets, false, [SessionScriptPoint.Throw]) };
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.ThrowHeroItem(owner.Version, request).Failure);
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
        Assert.IsNull(owner.QueryAmmo("made:3"));
    }

    [TestMethod]
    public void The_range_is_that_of_the_units_thrown()
    {
        using var owner = Hero();
        var one = owner.QueryHeroThrowRange("made:2", 1)!.Value;
        var all = owner.QueryHeroThrowRange("made:2", 30)!.Value;
        Assert.IsTrue(one >= all, $"{one} for 1 unit, {all} for 30");
        Assert.AreEqual(owner.QueryHeroThrowRange("made:2"), owner.QueryHeroThrowRange("made:2", 30), "no count: the whole stack");
    }

    [TestMethod]
    public void A_throw_at_a_critter_splits_when_the_item_leaves()
    {
        using var owner = Hero();
        var rat = SessionCritterCombat.From("oid:rat", AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;
        var request = Throw(owner, 10) with { Struck = rat, StruckCell = At(owner, 3) };
        var thrown = owner.ThrowHeroItem(owner.Version, request);
        Assert.AreEqual(GameSessionFailure.None, thrown.Failure);
        Assert.AreEqual((20, 10), (owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:3")!.Value.Units));
        Assert.AreEqual("made:3", owner.QueryThrownShot(thrown.Shots[0])!.Value.Item);
    }
}
