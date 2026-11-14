using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.NpcThrowStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-COMBAT-1761 (0x4B3C00, 0x466E00 → 0x467860, 0x441980; ledger TW1, TW3): an NPC's throw runs the item's removal scripts (wield off for a worn item, remove-item) and its throw script (point 6), the thrower as the triggerer; a throw script
// that answers skip default throws nothing and gives the item back to the thrower unworn. The thrower, the dagger and the grenade are NpcThrowStateTests'; the scripts, numbers and texts are invented.
[TestClass]
public sealed class NpcThrowScriptTests
{
    private const int WieldOffScript = 700, RemoveScript = 701, ThrowScript = 702;
    private static readonly ScrProgram Runs = Program(C(ScrCatalogue.Action.ReturnRunDefault));
    private static readonly ScrProgram Skips = Program(C(ScrCatalogue.Action.ReturnSkipDefault));

    // The three points the throw runs, on the dagger or the grenade: wield off 25, remove-item 28, throw 6.
    private static GameSessionOwner Open(string item = Dagger, params int[] points)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var facts = item == Dagger ? InventoryStateTests.Facts(SessionItemType.Weapon, 2001, location: 1004, scripts: points)
            : InventoryStateTests.Facts(SessionItemType.Generic, 5640, scripts: points);
        var seed = new SessionMapInventorySeed([new(Thrower, SessionHolderKind.Npc, false, true, [], [item]), new(Other, SessionHolderKind.Npc, false, true, [], [])], [new(item, facts, null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static SessionNpcWieldScripts Wearer(Host host) => new(host, Combat().Target, new int[SessionCritterIndexedWrites.StatCount]);

    private static string[] Listed(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == Thrower).Items.Select(item => item.Entity).ToArray();
    private static bool WornNow(GameSessionOwner owner, string item) => owner.QueryMapInventory(Exterior)!.Holders.Single(holder => holder.Entity == Thrower).Items.Single(listed => listed.Entity == item).Facts.Worn;

    /// <summary>A host with the three scripts on the throw's items; it answers a line for an NPC triggerer and records what it was asked.</summary>
    internal sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [WieldOffScript] = Runs, [RemoveScript] = Runs, [ThrowScript] = Runs };
        public List<string> Asked { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return point switch
            {
                SessionScriptPoint.WieldOff => new ScrSlot(WieldOffScript, 0, 0),
                SessionScriptPoint.RemoveItem => new ScrSlot(RemoveScript, 0, 0),
                SessionScriptPoint.Throw => new ScrSlot(ThrowScript, 0, 0),
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
            return new(SessionPrintedLineStatus.Shown, "It hums.", "line 1");
        }
    }

    private static readonly ScrProgram PrintAndRun = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));

    [TestMethod]
    public void WithoutAHostAnItemWithARemovalOrThrowScriptIsRefusedAsBefore()
    {
        foreach (var point in new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw })
        {
            using var owner = Open(Dagger, point);
            var result = owner.ThrowByNpc(owner.Version, AtHero());
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision), $"point {point}");
        }
    }

    [TestMethod]
    public void AnItemWithAScriptAtAnotherPointIsStillRefusedWithAHost()
    {
        using var owner = Open(Dagger, SessionScriptPoint.Throw, SessionScriptPoint.InsertItem);
        var host = new Host(owner);
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionFailure.ItemScriptUnsupported, 0L), (result.Failure, owner.Version.Revision));
        Assert.AreEqual(0, host.Asked.Count, "no script is run for it");
    }

    [TestMethod]
    public void TheRemovalScriptsRunBeforeTheThrowScriptAndTheThrowGoesOn()
    {
        using var owner = Open(Dagger, SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw);
        var host = new Host(owner);
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false), (result.Disposition, result.Failure, result.ScriptDeclined));
        CollectionAssert.AreEqual(new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw }, result.Scripts.Select(run => run.Point).ToArray(), "0x467860 then 0x441980");
        Assert.IsTrue(result.Scripts.All(run => run.Attachee == Dagger));
        Assert.IsNotNull(result.Attack, "the throw was decided");
        Assert.IsNotNull(result.Landed);
        CollectionAssert.DoesNotContain(Listed(owner), Dagger);
    }

    [TestMethod]
    public void AThrowOfAnItemTheThrowerDoesNotWieldRefusesBeforeAnyScriptRuns()
    {
        using var owner = Open(Dagger, SessionScriptPoint.WieldOff, SessionScriptPoint.Throw);
        var host = new Host(owner);
        // The thrower's worn items the host names do not hold the dagger: the weapon check refuses it, nothing runs.
        var refused = owner.ThrowByNpc(owner.Version, AtHero() with { ThrowerWorn = new Dictionary<EquipmentSlot, SessionWearItem>(), Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionFailure.AttackUnsupported, 0), (refused.Failure, host.Asked.Count));
    }

    [TestMethod]
    public void AThrowScriptAnswerOfSkipDefaultThrowsNothingAndGivesTheItemBackUnworn()
    {
        {
            using var owner = Open(Dagger, SessionScriptPoint.WieldOff, SessionScriptPoint.Throw);
            var host = new Host(owner);
            host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnSkipDefault));
            Assert.IsTrue(WornNow(owner, Dagger), "the map data has the dagger worn");
            var heroLife = owner.QueryHeroLife()!.Record.State;
            var draws = owner.QueryRandom().Draws;
            var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
            Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (result.Disposition, result.Failure, result.ScriptDeclined));
            Assert.IsNull(result.Attack, "no attack");
            Assert.IsNull(result.Landed, "nothing landed");
            Assert.AreEqual(0, result.Shots.Count, "no shot");
            Assert.AreEqual(draws, owner.QueryRandom().Draws, "no roll");
            Assert.AreEqual(heroLife, owner.QueryHeroLife()!.Record.State, "the hero is untouched");
            Assert.AreEqual(1L, owner.Version.Revision);
            CollectionAssert.Contains(Listed(owner), Dagger);
            Assert.IsFalse(WornNow(owner, Dagger), "put back by an insert, not a re-wield (0x4617F0)");
            Assert.IsNull(Lying(owner, Dagger), "it does not lie on the map");
            CollectionAssert.AreEqual(new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.Throw }, result.Scripts.Select(run => run.Point).ToArray());
            CollectionAssert.AreEqual(new[] { new SessionScriptMessage(ThrowScript, 1, 6, "It hums.") }, result.Messages.ToArray());
            CollectionAssert.Contains(host.Asked, $"npc line {ThrowScript} 1");
        }
    }

    [TestMethod]
    public void ARefusedThrowKeepsTheFlagWritesOfTheRemovalAndTheThrowScript()
    {
        using var owner = Open(Dagger, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw);
        var host = new Host(owner);
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Changed, true, true, true), (result.Disposition, result.ScriptDeclined, ReadFlag(owner, 3), ReadFlag(owner, 7)));
        Assert.AreEqual(1L, owner.Version.Revision, "one revision for the removal, the throw script and the give-back");
    }

    [TestMethod]
    public void ARemovalScriptsFlagWriteIsAppliedWithTheThrow()
    {
        using var owner = Open(Dagger, SessionScriptPoint.RemoveItem);
        var host = new Host(owner);
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, true), (result.Disposition, result.Failure, ReadFlag(owner, 3)));
        Assert.IsNotNull(result.Attack);
        CollectionAssert.DoesNotContain(Listed(owner), Dagger);
    }

    [TestMethod]
    public void AScriptTheSessionCannotApplyRefusesTheThrowBeforeAnyEffect()
    {
        using var owner = Open(Dagger, SessionScriptPoint.RemoveItem, SessionScriptPoint.Throw);
        var host = new Host(owner);
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A dialog is no command of an item event (SessionItemScripts.Admission): the throw script's run is refused, so the removal script's flag write is not applied either.
        host.Programs[ThrowScript] = Program(C(ScrCatalogue.Action.Dialog, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        var draws = owner.QueryRandom().Draws;
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (result.Disposition, result.Failure, owner.Version.Revision));
        Assert.AreEqual((false, draws), (ReadFlag(owner, 3) ?? false, owner.QueryRandom().Draws));
        CollectionAssert.Contains(Listed(owner), Dagger);
        Assert.IsTrue(WornNow(owner, Dagger));
    }

    [TestMethod]
    public void AnUnscriptedItemWithAHostRunsNothing()
    {
        using var owner = Open(Dagger);
        var host = new Host(owner);
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionFailure.None, 0, 0), (result.Failure, host.Asked.Count, result.Scripts.Count));
        Assert.IsNotNull(result.Attack);
    }

    [TestMethod]
    public void AGrenadesThrowScriptRunsToo()
    {
        using var owner = Open(Grenade, SessionScriptPoint.Throw);
        var host = new Host(owner);
        host.Programs[ThrowScript] = PrintAndRun;
        var flies = owner.ThrowByNpc(owner.Version, Lob(new(98, 203)) with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 1, 1), (flies.Disposition, flies.Failure, flies.Shots.Count, flies.Messages.Count), "the throw script ran and the grenade flies");
        using var declined = Open(Grenade, SessionScriptPoint.Throw);
        var skipping = new Host(declined);
        skipping.Programs[ThrowScript] = Skips;
        var kept = declined.ThrowByNpc(declined.Version, Lob(new(98, 203)) with { Scripts = Wearer(skipping) });
        Assert.AreEqual((GameSessionDisposition.Changed, true, 0), (kept.Disposition, kept.ScriptDeclined, kept.Shots.Count));
        CollectionAssert.Contains(Listed(declined), Grenade);
        Assert.IsNull(Lying(declined, Grenade));
    }

    [TestMethod]
    public void AStaleVersionRefusesBeforeAnyScriptRuns()
    {
        using var owner = Open(Dagger, SessionScriptPoint.Throw);
        var host = new Host(owner);
        var result = owner.ThrowByNpc(new(owner.Identity, owner.Version.Revision + 1), AtHero() with { Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionFailure.StaleVersion, 0), (result.Failure, host.Asked.Count));
    }

    [TestMethod]
    public void AGrenadeWithOnlyAWieldOffScriptHasNothingToRunAndFliesWithoutAHost()
    {
        // The wield-off script runs for a worn item only (0x467860); a grenade is not worn.
        using var owner = Open(Grenade, SessionScriptPoint.WieldOff);
        var result = owner.ThrowByNpc(owner.Version, Lob(new(98, 203)));
        Assert.AreEqual((GameSessionDisposition.Changed, 1, 0), (result.Disposition, result.Shots.Count, result.Scripts.Count));
    }

    [TestMethod]
    public void ARefusalTheThrowMakesWhateverTheScriptsDoIsMadeBeforeAnyScriptRuns()
    {
        using var owner = Open(Dagger, SessionScriptPoint.RemoveItem);
        var host = new Host(owner);
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A worn item the thrower's list does not hold is no wear the session accepts (InvalidWear).
        var worn = Wielded();
        worn[EquipmentSlot.Helmet] = new SessionWearItem("ghost", new(20, 0, 0, false));
        var result = owner.ThrowByNpc(owner.Version, AtHero() with { ThrowerWorn = worn, Scripts = Wearer(host) });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidWear, 0L), (result.Disposition, result.Failure, owner.Version.Revision));
        Assert.AreEqual((0, false), (host.Asked.Count, ReadFlag(owner, 3) ?? false), "no script ran, nothing was applied");
        Assert.AreEqual(0, result.Messages.Count);
    }
}
