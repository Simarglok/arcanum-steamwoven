using Arcanum.Simulation.Movement;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1655 (0x461810 get script before the insert check, 0x466640 end insert-item script): an NPC's ground pickup runs the item's scripts with the NPC as the triggerer. Entities, scripts and texts are invented.
[TestClass]
public sealed class NpcPickupScriptTests
{
    private const string Npc = "oid:scavenger";
    private const int InsertScript = 600, GetScript = 601;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts Arrows = new(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, 5, [SessionScriptPoint.InsertItem]) { AmmoType = 0 };

    // "relic": armour with an insert-item script, "snag": weapon with a get script, "both": weapon with both, "plain": no script, "shells": ammunition with an insert-item script and the NPC's own stack.
    private static GameSessionOwner Open()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var quiver = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, 3, []) { AmmoType = 0 };
        var seed = new SessionMapInventorySeed([new(Npc, SessionHolderKind.Npc, false, false, [], ["quiver"])],
        [
            new("quiver", quiver, null),
            new("relic", InventoryStateTests.Facts(SessionItemType.Armor, 2901, 0, 0, null, SessionScriptPoint.InsertItem), new CellCoord(90, 200)),
            new("snag", InventoryStateTests.Facts(SessionItemType.Weapon, 2902, 0, 0, null, SessionScriptPoint.Get), new CellCoord(91, 200)),
            new("both", InventoryStateTests.Facts(SessionItemType.Weapon, 2903, 0, 0, null, SessionScriptPoint.Get, SessionScriptPoint.InsertItem), new CellCoord(92, 200)),
            new("plain", InventoryStateTests.Facts(SessionItemType.Weapon, 2904), new CellCoord(93, 200)),
            new("shells", Arrows, new CellCoord(94, 200)),
        ]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static string[] Listed(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Npc).Items.Select(item => item.Entity).ToArray();
    private static string[] OnGround(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray();

    private static readonly ScrProgram PrintAndRun = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));
    private static readonly ScrProgram Skip = Program(C(ScrCatalogue.Action.ReturnSkipDefault));

    /// <summary>A host with fixed slots for the scripted items; it answers a line for an NPC triggerer in its own way and records what it was asked.</summary>
    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [InsertScript] = PrintAndRun, [GetScript] = Skip };
        public SessionPrintedLine NpcLine { get; set; } = new(SessionPrintedLineStatus.Shown, "It hums.", "line 1");
        public List<string> Asked { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return point switch
            {
                SessionScriptPoint.InsertItem when attachee.Code is "relic" or "both" or "shells" => new ScrSlot(InsertScript, 0, 0),
                SessionScriptPoint.Get when attachee.Code is "snag" or "both" => new ScrSlot(GetScript, 0, 0),
                _ => null,
            };
        }

        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;

        public bool GlobalFlag(int index) => session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new(index))).Value ?? false;
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;

        public SessionPrintedLine PrintedLine(int script, int line)
        {
            Asked.Add($"hero line {script} {line}");
            return new(SessionPrintedLineStatus.Shown, "It hums for the hero.", "line 1");
        }

        public SessionPrintedLine PrintedLineForNpc(int script, int line)
        {
            Asked.Add($"npc line {script} {line}");
            return NpcLine;
        }
    }

    [TestMethod]
    public void WithoutAHostAnItemWithAGetOrInsertItemScriptIsRefusedAsBefore()
    {
        using var owner = Open();
        foreach (var item in new[] { "relic", "snag", "both", "shells" })
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItemByNpc(owner.Version, Npc, item, Accept).Failure, item);
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "plain", Accept, scripts: null).Disposition, "an item with no script needs no host");
    }

    [TestMethod]
    public void TheInsertItemScriptRunsAfterTheInsertionWithTheNpcAsTriggererAndItsLineComesBack()
    {
        using var owner = Open();
        var host = new Host(owner);
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "relic", 1L), (result.Disposition, result.Failure, result.Item, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { "quiver", "relic" }, Listed(owner));
        CollectionAssert.DoesNotContain(OnGround(owner), "relic");
        // The line went through the NPC-triggerer reading, not the hero's; the run's result is the insert point's.
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(InsertScript, 1, 6, "It hums.") }, result.Messages.ToArray());
        CollectionAssert.Contains(host.Asked, $"npc line {InsertScript} 1");
        Assert.IsFalse(host.Asked.Any(asked => asked.StartsWith("hero line", StringComparison.Ordinal)));
        Assert.AreEqual((InsertScript, ScrStop.ReturnRunDefault), (result.InsertScript!.Script, result.InsertScript.Stop));
        Assert.IsNull(result.DeclinedBy);
    }

    [TestMethod]
    public void TheInsertItemScriptRunsForAnAmmunitionItemMergedIntoAStackToo()
    {
        using var owner = Open();
        var host = new Host(owner);
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "shells", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, "quiver", 1), (result.Disposition, result.Item, result.Messages.Count), "merged into the NPC's stack, the event still fires (AM1)");
        Assert.AreEqual(8, owner.QueryAmmo("quiver")!.Value.Units);
    }

    [TestMethod]
    public void AGetScriptAnswerOfSkipDefaultLeavesTheItemAndTheInsertCheckIsNeverAsked()
    {
        using var owner = Open();
        var host = new Host(owner);
        var asked = false;
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "snag", _ => { asked = true; return SessionInsertVerdict.Accepted; }, scripts: host);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, SessionScriptPoint.Get), (result.Disposition, result.Failure, result.DeclinedBy ?? -1));
        Assert.IsFalse(asked, "the get script is the gate before the insert check (0x461810)");
        Assert.AreEqual((0L, 1), (owner.Version.Revision, Listed(owner).Length), "nothing moved, no revision");
        CollectionAssert.Contains(OnGround(owner), "snag");

        // A get script that lets the default run goes on to the insert check and the insertion.
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnRunDefault));
        var taken = owner.PickUpItemByNpc(owner.Version, Npc, "snag", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, "snag"), (taken.Disposition, taken.Item));
        CollectionAssert.AreEqual(new[] { "quiver", "snag" }, Listed(owner));
    }

    [TestMethod]
    public void AGetScriptThatSkipsKeepsItsFlagWritesAsTheOriginalDoes()
    {
        using var owner = Open();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "snag", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionScriptPoint.Get, true, 1L), (result.Disposition, result.DeclinedBy ?? -1, ReadFlag(owner, 3), owner.Version.Revision));
        CollectionAssert.Contains(OnGround(owner), "snag");
    }

    [TestMethod]
    public void TheInsertItemScriptsFlagWriteAppliesWithThePickupInOneRevision()
    {
        using var owner = Open();
        var host = new Host(owner);
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept, scripts: host).Disposition);
        Assert.AreEqual((true, 1L), (ReadFlag(owner, 3), owner.Version.Revision));
    }

    [TestMethod]
    public void ARefusedInsertCheckDropsTheGetScriptsWritesAndRunsNoInsertItemScript()
    {
        using var owner = Open();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "both", _ => SessionInsertVerdict.TooHeavy, scripts: host);
        Assert.AreEqual((GameSessionFailure.InsertRefused, SessionInsertVerdict.TooHeavy), (result.Failure, result.Verdict));
        Assert.AreEqual((false, 0L), (ReadFlag(owner, 3), owner.Version.Revision), "a refused pickup changes nothing");
        Assert.IsFalse(host.Asked.Any(asked => asked.StartsWith("npc line", StringComparison.Ordinal)), "the insert-item script follows the insertion only");
        CollectionAssert.Contains(OnGround(owner), "both");
    }

    [TestMethod]
    public void ALineThatTheHostCannotShowForAnNpcRefusesThePickupBeforeAnyEffect()
    {
        using var owner = Open();
        var host = new Host(owner) { NpcLine = new(SessionPrintedLineStatus.Unsupported, string.Empty, "differs by gender") };
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { "quiver" }, Listed(owner));
        CollectionAssert.Contains(OnGround(owner), "relic");
    }

    [TestMethod]
    public void AScriptTheSessionCannotApplyForTheNpcRefusesThePickup()
    {
        using var owner = Open();
        var host = new Host(owner);
        // An effect on the triggerer is the hero's only: an NPC triggerer has none the session keeps.
        host.Programs[InsertScript] = Program(C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Rejected, 0L), (result.Disposition, owner.Version.Revision));
        CollectionAssert.Contains(OnGround(owner), "relic");
    }

    [TestMethod]
    public void AnItemTheHeroTookAndDroppedKeepsItsOriginRowsScriptsForTheNpc()
    {
        using var owner = Open();
        var host = new Host(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "relic", Accept, host).Disposition);
        owner.PlaceHeroItem("relic", new CellCoord(120, 210));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept).Failure, "without a host");
        var result = owner.PickUpItemByNpc(owner.Version, Npc, "relic", Accept, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, 1), (result.Disposition, result.Messages.Count), "its origin row's insert-item script runs");
        CollectionAssert.Contains(Listed(owner), "relic");
    }

    [TestMethod]
    public void TheHerosOwnLinesAreStillReadForTheHeroWhenTheNpcReadingIsRefused()
    {
        using var owner = Open();
        var host = new Host(owner) { NpcLine = new(SessionPrintedLineStatus.Unsupported, string.Empty, "differs by gender") };
        var result = owner.PickUpItem(owner.Version, "relic", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "It hums for the hero."), (result.Disposition, result.Messages.Single().Text), "only an NPC pickup reads the NPC's line");
    }

    [TestMethod]
    public void AnItemWithNoScriptNeverAsksTheHost()
    {
        using var owner = Open();
        var host = new Host(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "plain", Accept, scripts: host).Disposition);
        Assert.AreEqual(0, host.Asked.Count);
    }
}
