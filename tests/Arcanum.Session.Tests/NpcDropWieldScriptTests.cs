using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2856 (exe read, ledger PI2, VP1, VP6, TQ20; 0x461AB0 → 0x466E00 → 0x467860): the removal an NPC's drop makes of a worn item runs the item's wield-off script (25) and then its remove-item script (28) with the NPC as the triggerer
// and the wearer, through the host's NpcWearerScripts, and applies their changes with the drop (the host's ItemRunOutput gets their printed lines); an item with such a script and no host context stays. The item is a spell's weapon block
// (SpellWeaponDrop, the step an attack's critical and arm hit use too) of NpcDropScriptTests' golem. Scripts, numbers and texts are invented.
[TestClass]
public sealed class NpcDropWieldScriptTests
{
    private const string Golem = "golem", Sword = "golem-sword";
    private const int WieldOffScript = 700, RemoveScript = 701;
    private static readonly CellCoord GolemCell = new(101, 200);
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly ScrProgram PrintAndRun = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));

    private static SessionCritterCombat GolemCombat()
    {
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0);
        return SessionCritterCombat.From(Golem, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }).Combat!;
    }

    private static GameSessionOwner Owner(params int[] swordScripts)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new(Golem, SessionHolderKind.Npc, false, false, [], [Sword])],
            [new SessionSeedItem(Sword, InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon, 0, null, swordScripts), null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    // The host's context for the golem: the three-point host of the NPC throw tests (its scripts 700 for wield off, 701 for remove item, a line printed for an NPC triggerer), the golem's life target and its stored stats.
    private static NpcThrowScriptTests.Host Context(GameSessionOwner owner, out SessionNpcWieldScripts context)
    {
        var host = new NpcThrowScriptTests.Host(owner);
        context = new(host, GolemCombat().Target, new int[SessionCritterIndexedWrites.StatCount]);
        return host;
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> Worn(bool noDrop = false) => new() { [EquipmentSlot.Weapon] = new(Sword, Sturdy) { NoDrop = noDrop } };

    private static bool InList(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Golem).Items.Any(i => i.Entity == Sword);

    private static SessionCriticalStepOutcome Drop(GameSessionOwner owner, bool noDrop = false)
    {
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(noDrop), GolemCell, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        return steps.Single().Outcome;
    }

    [TestMethod]
    public void AWieldOffOrRemoveItemScriptWithoutAHostContextLeavesTheDropUnapplied()
    {
        foreach (var point in new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem })
        {
            using var owner = Owner(point);
            Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(owner), $"point {point}");
            Assert.IsTrue(InList(owner));
            Assert.AreEqual(DangerFlags.None, owner.QueryDanger(Golem).Flags & DangerFlags.LookForWeapon);
        }
        // A context the host does not have for the NPC is the same, and a host that throws.
        using var unknown = Owner(SessionScriptPoint.WieldOff);
        unknown.NpcWearerScripts = _ => null;
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(unknown));
        using var throwing = Owner(SessionScriptPoint.WieldOff);
        throwing.NpcWearerScripts = _ => throw new InvalidOperationException("a host refusal");
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(throwing));
        Assert.IsTrue(InList(throwing));
    }

    [TestMethod]
    public void TheRemovalRunsWieldOffThenRemoveItemWithTheNpcAsTriggerAndAppliesTheirWritesWithTheDrop()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        var host = Context(owner, out var context);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        var asked = new List<string>();
        owner.NpcWearerScripts = npc => { asked.Add(npc); return context; };
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { Golem }, asked);
        CollectionAssert.AreEqual(new[] { $"slot {Sword} {SessionScriptPoint.WieldOff}", $"slot {Sword} {SessionScriptPoint.RemoveItem}" }, host.Asked.Where(line => line.StartsWith("slot ", StringComparison.Ordinal)).ToArray(), "0x467860: 25 then 28");
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "the runs' writes are applied");
        Assert.IsFalse(InList(owner));
        Assert.AreEqual(DangerFlags.LookForWeapon, owner.QueryDanger(Golem).Flags & DangerFlags.LookForWeapon, "the drop's tail still runs");
    }

    [TestMethod]
    public void OnlyTheScriptTheItemHasRuns()
    {
        using var owner = Owner(SessionScriptPoint.RemoveItem);
        var host = Context(owner, out var context);
        owner.NpcWearerScripts = _ => context;
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { $"slot {Sword} {SessionScriptPoint.RemoveItem}" }, host.Asked.Where(line => line.StartsWith("slot ", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public void ThePrintedLinesAndEyeCandyOfTheRunsGoToTheHostSink()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff);
        var host = Context(owner, out var context);
        host.Programs[WieldOffScript] = PrintAndRun;
        owner.NpcWearerScripts = _ => context;
        var lines = new List<SessionScriptMessage>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages);
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(WieldOffScript, 1, 6, "It hums.") }, lines);
        CollectionAssert.Contains(host.Asked, $"npc line {WieldOffScript} 1");
    }

    [TestMethod]
    public void AnItemWithoutTheseScriptsAsksNoContext()
    {
        using var owner = Owner();
        owner.NpcWearerScripts = _ => throw new AssertFailedException("no wield-off or remove-item script, nothing to ask");
        owner.ItemRunOutput = (_, _) => throw new AssertFailedException("nothing ran");
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        Assert.IsFalse(InList(owner));
    }

    [TestMethod]
    public void AScriptTheSessionCannotApplyLeavesTheItemAndAppliesNoneOfTheRuns()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        var host = Context(owner, out var context);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A dialog is no command of an item event (SessionItemScripts.Admission): the remove-item run is refused, so the wield-off run's flag write is not applied either.
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.Dialog, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        owner.NpcWearerScripts = _ => context;
        var version = owner.Version;
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(owner));
        Assert.AreEqual((false, true, version), (ReadFlag(owner, 3) ?? false, InList(owner), owner.Version));
    }

    [TestMethod]
    public void ANoDropItemRefusesBeforeTheRemovalSoNoScriptRuns()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        owner.NpcWearerScripts = _ => throw new AssertFailedException("the no-drop check comes before the removal");
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, Drop(owner, noDrop: true));
        Assert.IsTrue(InList(owner));
    }

    [TestMethod]
    public void ADropScriptThatSkipsTheDefaultRunsNoRemovalScript()
    {
        using var owner = Owner(SessionScriptPoint.Drop, SessionScriptPoint.WieldOff);
        owner.DropScript = _ => SessionDropScriptAnswer.SkipDefault;
        owner.NpcWearerScripts = _ => throw new AssertFailedException("a refused drop makes no removal");
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, Drop(owner));
        Assert.IsTrue(InList(owner));
    }

    [TestMethod]
    public void AHostSinkThatThrowsLeavesTheDropWhole()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff);
        var host = Context(owner, out var context);
        host.Programs[WieldOffScript] = PrintAndRun;
        owner.NpcWearerScripts = _ => context;
        owner.ItemRunOutput = (_, _) => throw new InvalidOperationException("a host refusal");
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        Assert.IsFalse(InList(owner));
        Assert.AreEqual(DangerFlags.LookForWeapon, owner.QueryDanger(Golem).Flags & DangerFlags.LookForWeapon);
    }

    [TestMethod]
    public void ARunThatChangesNothingTakesNoRevisionOfItsOwn()
    {
        using var printing = Owner(SessionScriptPoint.WieldOff);
        var host = Context(printing, out var context);
        host.Programs[WieldOffScript] = PrintAndRun;
        printing.NpcWearerScripts = _ => context;
        var before = printing.Version.Revision;
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(printing));
        var printed = printing.Version.Revision - before;
        using var writing = Owner(SessionScriptPoint.WieldOff);
        var other = Context(writing, out var writeContext);
        other.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        writing.NpcWearerScripts = _ => writeContext;
        before = writing.Version.Revision;
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(writing));
        Assert.AreEqual(printed + 1, writing.Version.Revision - before, "a run that writes takes one more revision");
    }
}
