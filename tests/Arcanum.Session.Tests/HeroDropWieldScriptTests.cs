using Arcanum.Data.Combat;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2886 (exe read, ledger PI2, VP11, TQ20; 0x461AB0 → 0x466E00 → 0x467860): the removal the hero's critical drop makes of a worn item runs the item's wield-off script (25) and then its remove-item script (28) with the hero as the triggerer, through the
// host's HeroWearerScripts, and applies their changes with the drop (the host's ItemRunOutput gets their printed lines); an item with such a script and no host stays. The drop is a spell's weapon block (SpellWeaponDrop, the step an attack's critical and arm hit
// use too) on the hero's blade. Scripts, numbers and texts are invented.
[TestClass]
public sealed class HeroDropWieldScriptTests
{
    private const string Blade = "kit:1";
    private const int WieldOffScript = 700, RemoveScript = 701;
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);
    private static readonly ScrProgram PrintAndRun = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnRunDefault));

    private static EquipmentItem Sword() => new()
    {
        Kind = EquipmentItemKind.Weapon, HitPoints = 1000, MaxHitPoints = 1000,
        Weapon = new EquipmentWeapon { Skill = CombatSkill.Melee, Damage = DamageRanges.None with { Normal = new(1, 2) }, SpeedFactor = 15 },
    };

    private static GameSessionOwner Owner(params int[] points)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.Equip(owner.Version, Blade, EquipmentSlot.Weapon, new(Kit[0].Kind, points, false, Sword()), (_, _) => true, _ => SessionInsertVerdict.Accepted, points.Length == 0 ? null : new NpcThrowScriptTests.Host(owner)).Failure);
        return owner;
    }

    private static NpcThrowScriptTests.Host Context(GameSessionOwner owner)
    {
        var host = new NpcThrowScriptTests.Host(owner);
        owner.HeroWearerScripts = () => host;
        return host;
    }

    private static SessionCriticalStepOutcome Drop(GameSessionOwner owner, bool noDrop = false)
    {
        var worn = new Dictionary<EquipmentSlot, SessionWearItem> { [EquipmentSlot.Weapon] = new(Blade, Sturdy) { NoDrop = noDrop } };
        var steps = owner.SpellWeaponDrop(SessionLifeTarget.Hero, worn, null, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        return steps.Single().Outcome;
    }

    private static bool Worn(GameSessionOwner owner) => owner.QueryEquipment().Worn.Any(item => item.Slot == EquipmentSlot.Weapon);

    private static string[] Slots(NpcThrowScriptTests.Host host) => host.Asked.Where(line => line.StartsWith("slot ", StringComparison.Ordinal)).ToArray();

    [TestMethod]
    public void AWieldOffOrRemoveItemScriptWithoutAHostLeavesTheDropUnapplied()
    {
        foreach (var point in new[] { SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem })
        {
            using var owner = Owner(point);
            Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(owner), $"point {point}");
            Assert.IsTrue(Worn(owner));
            Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        }
        // A host that has none for the hero is the same, and a host that throws.
        using var unknown = Owner(SessionScriptPoint.WieldOff);
        unknown.HeroWearerScripts = () => null;
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(unknown));
        using var throwing = Owner(SessionScriptPoint.WieldOff);
        throwing.HeroWearerScripts = () => throw new InvalidOperationException("a host refusal");
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(throwing));
        Assert.IsTrue(Worn(throwing));
    }

    [TestMethod]
    public void TheRemovalRunsWieldOffThenRemoveItemWithTheHeroAsTriggerAndAppliesTheirWritesWithTheDrop()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        var host = Context(owner);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { $"slot {Blade} {SessionScriptPoint.WieldOff}", $"slot {Blade} {SessionScriptPoint.RemoveItem}" }, Slots(host), "0x467860: 25 then 28");
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)), "the runs' writes are applied");
        Assert.IsFalse(Worn(owner));
        Assert.AreEqual(Blade, owner.QueryPlacedItems().Single().Entity, "the blade lies on the ground");
    }

    [TestMethod]
    public void OnlyTheScriptTheItemHasRuns()
    {
        using var owner = Owner(SessionScriptPoint.RemoveItem);
        var host = Context(owner);
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { $"slot {Blade} {SessionScriptPoint.RemoveItem}" }, Slots(host));
    }

    [TestMethod]
    public void ThePrintedLinesOfTheRunsGoToTheHostSink()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff);
        var host = Context(owner);
        host.Programs[WieldOffScript] = PrintAndRun;
        var lines = new List<SessionScriptMessage>();
        owner.ItemRunOutput = (messages, _) => lines.AddRange(messages);
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(WieldOffScript, 1, 6, "It hums for the hero.") }, lines);
        CollectionAssert.Contains(host.Asked, $"hero line {WieldOffScript} 1");
    }

    [TestMethod]
    public void AnItemWithoutTheseScriptsAsksNoContext()
    {
        using var owner = Owner();
        owner.HeroWearerScripts = () => throw new AssertFailedException("no wield-off or remove-item script, nothing to ask");
        owner.ItemRunOutput = (_, _) => throw new AssertFailedException("nothing ran");
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        Assert.IsFalse(Worn(owner));
    }

    [TestMethod]
    public void AScriptTheSessionCannotApplyLeavesTheItemAndAppliesNoneOfTheRuns()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        var host = Context(owner);
        host.Programs[WieldOffScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // A dialog is no command of an item event (SessionItemScripts.Admission): the remove-item run is refused, so the wield-off run's flag write is not applied either.
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.Dialog, (Number, 1)), C(ScrCatalogue.Action.ReturnRunDefault));
        var version = owner.Version;
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, Drop(owner));
        Assert.AreEqual((false, true, version), (ReadFlag(owner, 3) ?? false, Worn(owner), owner.Version));
    }

    [TestMethod]
    public void ANoDropItemRefusesBeforeTheRemovalSoNoScriptRuns()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem);
        owner.HeroWearerScripts = () => throw new AssertFailedException("the no-drop check comes before the removal");
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, Drop(owner, noDrop: true));
        Assert.IsTrue(Worn(owner));
    }

    [TestMethod]
    public void AHostSinkThatThrowsLeavesTheDropWhole()
    {
        using var owner = Owner(SessionScriptPoint.WieldOff);
        var host = Context(owner);
        host.Programs[WieldOffScript] = PrintAndRun;
        owner.ItemRunOutput = (_, _) => throw new InvalidOperationException("a host refusal");
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, Drop(owner));
        Assert.IsFalse(Worn(owner));
        Assert.AreEqual(1, owner.QueryPlacedItems().Count);
    }
}
