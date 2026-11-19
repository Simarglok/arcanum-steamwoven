using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-2182 (ledger TW3, QW5): the hero's throw of an ammunition stack or gold pile whose prototype carries a throw script (point 6) runs that script for the stack the split makes; the answer skip default throws nothing and the units stay
// where they were. Setup as HeroAmmoThrowStateTests and HeroGoldThrowStateTests; the scripts, numbers and texts are invented.
[TestClass]
public sealed class HeroStackThrowScriptTests
{
    private const int ThrowScript = 720;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);

    private sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [ThrowScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault)) };
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, "It hums for the hero.", "line 1");
    }

    // The hero holds a stack of 30 bullets (made:2); both prototypes carry the throw script (the made item inherits it from its prototype).
    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), null);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.AreEqual("made:2", owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 30, Bullets, Accept).Item);
        owner.AttachItemScripts(prototype => prototype is 7059 or 9076 ? [new(SessionScriptPoint.Throw, ThrowScript, 0, 0)] : null);
        return owner;
    }

    private static CellCoord At(GameSessionOwner owner, int dx) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    private static SessionHeroThrow Ammo(GameSessionOwner owner, Host? host, int count, params int[] points) =>
        new("made:2", new(Bullets, false, points.Length == 0 ? [SessionScriptPoint.Throw] : points), At(owner, 3)) { Count = count, SplitKind = Bullets, Scripts = host };

    private static SessionHeroThrow Gold(GameSessionOwner owner, Host? host, int count) =>
        new(SessionHeroGrid.GoldEntity, new(SessionHeroGrid.GoldKind, false, [SessionScriptPoint.Throw]), At(owner, 3)) { Count = count, SplitKind = GoldPile, Scripts = host };

    [TestMethod]
    public void TheThrowScriptRunsForTheStackTheSplitMakesAndTheThrowGoesOn()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)),
            C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 12));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (result.Disposition, result.Failure, result.ScriptDeclined), result.Failure.ToString());
        var run = result.Scripts.Single();
        Assert.AreEqual((SessionScriptPoint.Throw, "made:3"), (run.Point, run.Attachee), "the script's object is the made stack, not the source");
        Assert.AreEqual(1, result.Messages.Count);
        Assert.AreEqual(true, ReadFlag(owner, 3));
        var shot = result.Shots.Single();
        Assert.AreEqual(("made:3", 18, 12), (owner.QueryThrownShot(shot)!.Value.Item, owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:3")!.Value.Units));
        // The run's own change stays on the thrown stack's slot, copied from its prototype's array.
        Assert.AreEqual(new SessionItemScriptSlot("made:3", SessionScriptPoint.Throw, ThrowScript, 1u << 1, 0), owner.QueryItemScriptSlots().Single());
    }

    [TestMethod]
    public void ASkippedThrowThrowsNothingAndTheUnitsStayInTheStack()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
        var result = owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 12));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (result.Disposition, result.Failure, result.ScriptDeclined));
        Assert.AreEqual(0, result.Shots.Count, "nothing flies");
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units, "the stack is whole");
        Assert.IsNull(owner.QueryAmmo("made:3"), "no new stack was made");
        Assert.AreEqual(0, owner.QueryItemScriptSlots().Count, "the slot the run wrote for the never-made stack is dropped");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the run's global change stays");
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "no roll was made");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the run");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    // ARC-COMBAT-2413 (TW3, QW5; exe 0x578EA0, 0x4658E0, 0x466640): the lift of the whole stack destroys the source and makes a new object, which the give-back finds no stack to merge into, so the list's stack has a new entity.
    [TestMethod]
    public void ASkippedThrowOfTheWholeStackLeavesTheMadeStackWithItsSlotInThePlaceOfTheSource()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var (draws, revision) = (owner.QueryRandom().Draws, owner.Version.Revision);
        var sourceCell = owner.QueryHeroGrid().Items.Single(item => item.Entity == "made:2");
        var result = owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 30));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true, 0), (result.Disposition, result.Failure, result.ScriptDeclined, result.Shots.Count));
        Assert.IsTrue(owner.IsItemDestroyed("made:2"), "the lift destroyed the source");
        Assert.IsNull(owner.QueryAmmo("made:2"));
        Assert.AreEqual(30, owner.QueryAmmo("made:3")!.Value.Units, "the units are in the made stack");
        Assert.AreEqual("made:3", owner.QueryMadeItems().Last().Entity, "appended to the list's end");
        Assert.AreEqual(sourceCell.Slot, owner.QueryHeroGrid().Items.Single(item => item.Entity == "made:3").Slot, "and takes the first free cell, the one the source frees (it lay on the first-fit cell)");
        Assert.IsFalse(owner.QueryHeroGrid().Items.Any(item => item.Entity == "made:2"));
        Assert.AreEqual(new SessionItemScriptSlot("made:3", SessionScriptPoint.Throw, ThrowScript, 1u << 1, 0), owner.QueryItemScriptSlots().Single(), "the slot the run wrote stays with the made stack and the source's is gone");
        Assert.AreEqual((draws, revision + 1), (owner.QueryRandom().Draws, owner.Version.Revision));
    }

    [TestMethod]
    public void ACallTheScriptSchedulesStaysWithTheMadeStackOfAWholeStackSkip()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Schedule(ScrCatalogue.Action.ReturnSkipDefault);
        Assert.IsTrue(owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 30)).ScriptDeclined);
        Assert.AreEqual("made:3", owner.QueryScriptCalls().Single().Call.Attachee?.Entity);
    }

    [TestMethod]
    public void AWholeStackSkipLeavesNothingForTheNextMadeItemAndKeepsTheStackInTheList()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.IsTrue(owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 30)).ScriptDeclined);
        // The made stack is the one stack now: throwing it again (a new entity for the units' flying stack) works from it.
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        var again = owner.ThrowHeroItem(owner.Version, new("made:3", new(Bullets, false, [SessionScriptPoint.Throw]), At(owner, 3)) { Count = 10, SplitKind = Bullets, Scripts = host });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 20), (again.Disposition, again.Failure, owner.QueryAmmo("made:3")!.Value.Units));
        Assert.AreEqual("made:4", again.Scripts.Single().Attachee);
    }

    [TestMethod]
    public void TheGoldPilesThrowScriptRunsForTheCoinsItemAndASkipKeepsTheCoins()
    {
        using var owner = Hero();
        var host = new Host();
        var thrown = owner.ThrowHeroItem(owner.Version, Gold(owner, host, 100));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        Assert.AreEqual(("made:3", 285), (thrown.Scripts.Single().Attachee, owner.QueryGold()), "the coins are an item of their own for the script");
        Assert.AreEqual(1, thrown.Shots.Count);

        using var kept = Hero();
        var skipping = new Host();
        skipping.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var result = kept.ThrowHeroItem(kept.Version, Gold(kept, skipping, 100));
        Assert.AreEqual((GameSessionDisposition.Changed, true, 385, 0), (result.Disposition, result.ScriptDeclined, kept.QueryGold(), result.Shots.Count));
        Assert.IsNull(kept.QueryAmmo("made:3"));
        Assert.IsFalse(kept.QueryHeroKit().Any(item => item.Entity == "made:3"));
    }

    // ARC-COMBAT-2424 (TW3, QW5; exe 0x464830, 0x464970, 0x4617F0, 0x466640): the lift of the whole gold pile destroys it and makes a new gold object that merges into no pile, so the give-back takes the first cell it fits.
    // The pile keeps its one counter entity (Steamwoven difference, no installed gold carries a throw script, TW7).
    [TestMethod]
    public void ASkippedThrowOfTheWholeGoldPileRefitsThePileAtTheFirstFreeCellAndKeepsTheCoins()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 1)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var firstFit = owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 100, new(SessionHeroGrid.GoldKind, false, []), Accept).Disposition);
        Assert.AreNotEqual(firstFit, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "the pile lies away from the first fit");
        var (draws, revision, coins) = (owner.QueryRandom().Draws, owner.Version.Revision, owner.QueryGold());
        var result = owner.ThrowHeroItem(owner.Version, Gold(owner, host, coins));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true, 0), (result.Disposition, result.Failure, result.ScriptDeclined, result.Shots.Count));
        Assert.AreEqual(coins, owner.QueryGold(), "every coin is back");
        Assert.AreEqual(firstFit, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "the new gold object takes the first cell it fits");
        Assert.AreEqual(0, owner.QueryItemScriptSlots().Count, "the slot written for the never-made object is dropped (the pile keeps no slots)");
        Assert.AreEqual((draws, revision + 1), (owner.QueryRandom().Draws, owner.Version.Revision));
    }

    [TestMethod]
    public void ASkippedThrowOfPartOfTheGoldPileLeavesThePilesCell()
    {
        using var owner = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.MoveHeroItem(owner.Version, SessionHeroGrid.GoldEntity, 100, new(SessionHeroGrid.GoldKind, false, []), Accept).Disposition);
        Assert.IsTrue(owner.ThrowHeroItem(owner.Version, Gold(owner, host, owner.QueryGold() - 1)).ScriptDeclined);
        Assert.AreEqual(100, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "the units merge back into the pile that remains");
    }

    [TestMethod]
    public void ARunTheSessionCannotApplyRefusesBeforeAnySplit()
    {
        using var owner = Hero();
        var host = new Host();
        // A dialog is no command of an item event: the run is refused and nothing of it is applied.
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.Dialog, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        var revision = owner.Version.Revision;
        var result = owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 12));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, revision), (result.Disposition, result.Failure, owner.Version.Revision));
        Assert.AreEqual((30, false), (owner.QueryAmmo("made:2")!.Value.Units, ReadFlag(owner, 3) == true));
        Assert.IsNull(owner.QueryAmmo("made:3"));
    }

    [TestMethod]
    public void AStackThrowRefusesWithoutAHost()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.ThrowHeroItem(owner.Version, Ammo(owner, null, 5)).Failure, "no host: as before");
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
    }

    // ARC-SCRIPT-2855 (TQ19, 0x466640): a split stack whose throw script refuses is given back by a merge, which runs the insert-item script (21) on the incoming stack, the hero as the triggerer, before it is destroyed.
    private const int InsertScript = 721;

    [TestMethod]
    public void ADeclinedStackThrowRunsTheInsertItemScriptOnTheIncomingSplitStack()
    {
        using var owner = Hero();
        owner.AttachItemScripts(prototype => prototype is 7059 ? [new(SessionScriptPoint.Throw, ThrowScript, 0, 0), new(SessionScriptPoint.InsertItem, InsertScript, 0, 0)] : null);
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var declined = owner.ThrowHeroItem(owner.Version, Ammo(owner, host, 12, SessionScriptPoint.Throw, SessionScriptPoint.InsertItem));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (declined.Disposition, declined.Failure, declined.ScriptDeclined));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Throw, "made:3"), (SessionScriptPoint.InsertItem, "made:3") }, declined.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual((30, true), (owner.QueryAmmo("made:2")!.Value.Units, ReadFlag(owner, 3) == true), "the units merged back and the insert script's flag applied");
        Assert.IsNull(owner.QueryAmmo("made:3"));
    }

    // A call the throw script schedules on its own item (call script in, the triggerer the hero, the attachee the item).
    private static ScrProgram Schedule(int outcome) => Program(C(ScrCatalogue.Action.CallScriptIn, (Number, ThrowScript), (Number, 3), (0, 0), (1, 0), (Number, 15)), C(outcome));

    [TestMethod]
    public void ACallTheScriptSchedulesOnTheNeverMadeStackIsDroppedWithItAndKeptWhenTheStackFlies()
    {
        using var skipped = Hero();
        var host = new Host();
        host.Programs[ThrowScript] = Schedule(ScrCatalogue.Action.ReturnSkipDefault);
        Assert.IsTrue(skipped.ThrowHeroItem(skipped.Version, Ammo(skipped, host, 12)).ScriptDeclined);
        Assert.AreEqual(0, skipped.QueryScriptCalls().Count, "the item is never made, so no call is left for the next item that takes its entity");

        using var thrown = Hero();
        host.Programs[ThrowScript] = Schedule(ScrCatalogue.Action.ReturnRunDefault);
        Assert.AreEqual(1, thrown.ThrowHeroItem(thrown.Version, Ammo(thrown, host, 12)).Shots.Count);
        Assert.AreEqual("made:3", thrown.QueryScriptCalls().Single().Call.Attachee?.Entity, "the thrown stack keeps its call");
    }
}
