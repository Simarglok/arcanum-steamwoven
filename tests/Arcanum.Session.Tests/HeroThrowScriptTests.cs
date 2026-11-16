using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.EquipmentStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1955 (0x4B3C00, 0x466E00 → 0x467860, 0x441980; ledger TT1, TT8, TW3): the hero's throw runs the item's removal scripts (wield off for a worn item, remove-item) and its throw script (point 6), the hero as the triggerer; a throw
// script that answers skip default throws nothing and puts the item back in the hero's list unworn. The half-elf's kit is HeroItemThrowStateTests' (kit:1 the weapon, kit:2 and kit:3 armour); scripts, numbers and texts are invented.
[TestClass]
public sealed class HeroThrowScriptTests
{
    private const int WieldOffScript = 710, RemoveScript = 711, ThrowScript = 712;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly ScrProgram Runs = Program(C(ScrCatalogue.Action.ReturnRunDefault));

    private static GameSessionOwner Owner() => GameSessionOwner.Create(Descriptor() with
    {
        Hero = HalfElf() with { Kit = [new(Kit[0].Kind) { Weight = new(15) }, new(Kit[1].Kind) { Weight = new(150) }, new(Kit[2].Kind)] },
    }).Owner!;

    private static readonly EquipmentItem Coat = new() { Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = ArmorCoverage.Torso, Sizes = ArmorSizes.Medium } };

    private static CellCoord Here(GameSessionOwner owner, int dx = 0) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    // The armour kit:2 thrown at a tile with no critter, with the given script points on its facts.
    private static SessionHeroThrow At(GameSessionOwner owner, Host? host, params int[] points) =>
        new("kit:2", new(Kit[1].Kind, false, points), Here(owner, 3)) { Scripts = host };

    private static bool Holds(GameSessionOwner owner, string item) => owner.QueryHeroKit().Any(held => held.Entity == item);

    /// <summary>A host with the three scripts on kit:2; a printed line is the hero's.</summary>
    private sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [WieldOffScript] = Runs, [RemoveScript] = Runs, [ThrowScript] = Runs };
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => attachee.Code != "kit:2" ? null : point switch
        {
            SessionScriptPoint.WieldOff => new ScrSlot(WieldOffScript, 0, 0),
            SessionScriptPoint.RemoveItem => new ScrSlot(RemoveScript, 0, 0),
            SessionScriptPoint.Throw => new ScrSlot(ThrowScript, 0, 0),
            _ => null,
        };
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

    [TestMethod]
    public void WithoutAHostAnItemWithARemovalOrThrowScriptIsRefusedAsBefore()
    {
        foreach (var point in new[] { SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw })
        {
            using var owner = Owner();
            var result = owner.ThrowHeroItem(owner.Version, At(owner, null, point));
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision), $"point {point}");
            Assert.IsTrue(Holds(owner, "kit:2"));
        }
    }

    [TestMethod]
    public void AThrowScriptWithAnInsertItemScriptIsStillRefusedWithAHost()
    {
        using var owner = Owner();
        var host = new Host();
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.Throw, SessionScriptPoint.InsertItem));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision), "the give-back would run the insert-item script");
        Assert.AreEqual(0, result.Scripts.Count, "no script is run for it");
    }

    [TestMethod]
    public void TheRemoveItemScriptRunsBeforeTheThrowScriptAndTheThrowGoesOn()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        var revision = owner.Version.Revision;
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (result.Disposition, result.Failure, result.ScriptDeclined), result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw }, result.Scripts.Select(run => run.Point).ToArray(), "0x467860 then 0x441980");
        Assert.IsTrue(result.Scripts.All(run => run.Attachee == "kit:2"));
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "both runs' writes are kept");
        Assert.AreEqual(1, result.Messages.Count, "the removal script's line is the host's to post");
        Assert.AreEqual(1, result.Shots.Count, "the throw went on");
        Assert.IsFalse(Holds(owner, "kit:2"), "the item left the hero");
        Assert.AreEqual("kit:2", owner.QueryPlacedItems().Single().Entity);
        Assert.IsTrue(owner.Version.Revision > revision + 1, "the runs' revision and the throw's own");
    }

    [TestMethod]
    public void AThrowScriptThatOnlyPrintsALineStillShowsItWhenTheThrowSucceeds()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(1, result.Messages.Count, "a line printed by a run that changes no state is shown");
        Assert.AreEqual(1, result.Shots.Count);
    }

    [TestMethod]
    public void AWornItemsWieldOffScriptRunsFirstThenTheThrowGoesOn()
    {
        using var owner = Owner();
        var host = new Host();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Coat, [SessionScriptPoint.WieldOff]), Drawn, Accept, host).Disposition);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.WieldOff));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure), result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.WieldOff }, result.Scripts.Select(run => run.Point).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 7));
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "the item left its slot");
        Assert.AreEqual(1, result.Shots.Count);
    }

    [TestMethod]
    public void AThrowScriptThatSkipsDefaultThrowsNothingAndKeepsTheItemInTheList()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var draws = owner.QueryRandom().Draws;
        var revision = owner.Version.Revision;
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (result.Disposition, result.Failure, result.ScriptDeclined));
        Assert.IsNull(result.Attack);
        Assert.AreEqual(0, result.Shots.Count, "nothing flies");
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "the removal's and the throw script's changes stay");
        Assert.AreEqual(1, result.Messages.Count);
        Assert.IsTrue(Holds(owner, "kit:2"), "the item is back in the hero's list");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "it lies on no tile");
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "no roll was made");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the runs");
    }

    [TestMethod]
    public void ADeclinedThrowOfAWornItemTakesItOffAndPutsItInTheList()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:2", EquipmentSlot.Armor, Wearable(2, Coat, [SessionScriptPoint.WieldOff]), Drawn, Accept, host).Disposition);
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.WieldOff, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (result.Disposition, result.Failure, result.ScriptDeclined), result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.Throw }, result.Scripts.Select(run => run.Point).ToArray());
        Assert.AreEqual(0, owner.QueryEquipment().Worn.Count, "worn no more: the removal took it off");
        Assert.IsTrue(Holds(owner, "kit:2"));
        Assert.IsTrue(owner.QueryHeroGrid().Items.Any(item => item.Entity == "kit:2"), "it lies in the hero's grid again");
    }

    [TestMethod]
    public void ARunTheSessionCannotApplyRefusesBeforeAnyEffect()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A dialog is no command of an item event (SessionItemScripts.Admission): the throw script's run is refused, so the removal script's flag write is not applied either.
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.Dialog, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        var draws = owner.QueryRandom().Draws;
        var result = owner.ThrowHeroItem(owner.Version, At(owner, host, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision));
        Assert.AreEqual(false, ReadFlag(owner, 3));
        Assert.IsTrue(Holds(owner, "kit:2"));
        Assert.AreEqual(draws, owner.QueryRandom().Draws);
    }

    [TestMethod]
    public void AStaleVersionRefusesBeforeAnyScriptRuns()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var stale = owner.Version;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:3", EquipmentSlot.Armor, Wearable(3, Coat), Drawn, Accept).Disposition);
        var result = owner.ThrowHeroItem(stale, At(owner, host, SessionScriptPoint.RemoveItem));
        Assert.AreEqual((GameSessionFailure.StaleVersion, false), (result.Failure, ReadFlag(owner, 3) == true), "a stale version refuses before the scripts run");
        Assert.AreEqual(0, result.Scripts.Count);
    }

    // ARC-COMBAT-2176: the refusals of the struck critter's attack come before any script runs (the preflight, as NpcThrowPreflight does for an NPC).
    private static SessionHeroThrow AtRat(GameSessionOwner owner, Host host, params int[] points) =>
        At(owner, host, points) with { Struck = SessionCritterCombat.From("oid:rat", AttackStateTests.Npc(hpAdjustment: 5000)).Combat!, StruckCell = Here(owner, 3) };

    private static void AssertRefusedBeforeScripts(GameSessionOwner owner, Host host, SessionHeroThrow request, GameSessionFailure expected, string why)
    {
        var (version, draws) = (owner.Version, owner.QueryRandom().Draws);
        var result = owner.ThrowHeroItem(owner.Version, request);
        Assert.AreEqual((GameSessionDisposition.Rejected, expected), (result.Disposition, result.Failure), why);
        Assert.AreEqual(0, result.Scripts.Count, $"{why}: no script ran");
        Assert.AreEqual(false, ReadFlag(owner, 3), $"{why}: no script change applied");
        Assert.AreEqual((version, draws), (owner.Version, owner.QueryRandom().Draws), $"{why}: nothing changed");
        Assert.IsTrue(Holds(owner, "kit:2"), why);
    }

    [TestMethod]
    public void AStruckThrowTheAttackWouldRefuseIsRefusedBeforeTheScriptsRun()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var request = AtRat(owner, host, SessionScriptPoint.Throw);
        AssertRefusedBeforeScripts(owner, host, request with { StruckLight = 300 }, GameSessionFailure.InvalidAttack, "a light level outside 0..255");
        AssertRefusedBeforeScripts(owner, host, request with { StruckCell = Here(owner, 200_000), Target = Here(owner, 200_000) }, GameSessionFailure.InvalidAttack, "a distance beyond the thrown range");
        Assert.IsTrue(owner.RestoreRandom(new(owner.QueryRandom().Seed, long.MaxValue)));
        AssertRefusedBeforeScripts(owner, host, request, GameSessionFailure.RandomExhausted, "the rolls of the attack run out");
    }

    [TestMethod]
    public void AStruckThrowTheAttackAcceptsRunsItsScriptsAndStrikes()
    {
        using var owner = Owner();
        var host = new Host();
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.ThrowHeroItem(owner.Version, AtRat(owner, host, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.IsNotNull(result.Attack?.Resolution, "the critter was attacked");
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw }, result.Scripts.Select(run => run.Point).ToArray());
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)));
    }
}
