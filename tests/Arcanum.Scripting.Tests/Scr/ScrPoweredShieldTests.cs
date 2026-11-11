using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-MAGIC-041 (ledger TI8): the Electric degree-3 shield's script shape, on an invented program and (opt-in, DAT_ROOT) on the installed script 29016. At point 24 line 0
/// removes the pending call for the attachee and schedules the script at line 3 in 15 seconds; a due call at line 3 returns when the triggerer does not wear an item named
/// 2866, else uses one unit of ammunition type 2 from the triggerer and schedules the next call.
/// </summary>
[TestClass]
public sealed class ScrPoweredShieldTests
{
    private const int Shield = 29016, ShieldName = 2866, WieldOn = 24;
    private const byte Triggerer = 0, Attachee = 1;
    private static readonly ScrObjectRef Item = new(1, "made:1");

    // What the shield's lines use: S3's commands with the wielding test, the deferred calls and use ammunition units.
    private static readonly ScrAdmissionSet Set = new("shield", ScrAdmissionSet.S3.Conditions.Append(ScrCatalogue.Condition.IsWieldingItem),
        ScrAdmissionSet.S3.Actions.Concat([ScrCatalogue.Action.CallScriptIn, ScrCatalogue.Action.RemoveScriptCall, ScrCatalogue.Action.UseAmmoUnits]),
        ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    private static readonly ScrProgram Synthetic = Program(
        Do(C(ScrCatalogue.Action.RemoveScriptCall, N(Shield), (Attachee, 0))),
        Do(C(ScrCatalogue.Action.CallScriptIn, N(Shield), N(3), (Triggerer, 0), (Attachee, 0), N(15))),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)),
        L(C(ScrCatalogue.Condition.IsWieldingItem, (Triggerer, 0), N(ShieldName)), C(0), C(ScrCatalogue.Action.ReturnRunDefault)),
        Do(C(ScrCatalogue.Action.UseAmmoUnits, N(1), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.CallScriptIn, N(Shield), N(3), (Triggerer, 0), (Attachee, 0), N(15))),
        Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    private sealed class Host(FakeHost inner, bool wearing) : IScrHost, IScrObjectQueries
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => inner.Script(number);
        public bool GlobalFlag(int index) => inner.GlobalFlag(index);
        public int StoryState() => inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => inner.IsPc(critter);
        public int QuestState(ScrObjectRef obj, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        // The wearer holds the shield in the first wield location only while it wears it.
        public int? WieldedItemName(ScrObjectRef obj, int location) => wearing && obj == Pc && location == 1005 ? ShieldName : null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => null;
    }

    private static Host HostFor(ScrProgram program, bool wearing)
    {
        var fake = new FakeHost();
        fake.Attach(Item, WieldOn, Shield, program);
        return new(fake, wearing);
    }

    private static void CheckShield(ScrProgram program)
    {
        var options = new ScrRunOptions { Admission = Set };

        // Wield on: remove the pending call for the item, schedule the next at line 3 in 15 s (120000 game milliseconds: milliseconds times 8), run default.
        var wield = ScrDispatcher.RunObject(new(WieldOn, Item, Pc, null), HostFor(program, true), options);
        Assert.AreEqual((ScrOutcome.RunDefault, ScrStop.ReturnRunDefault), (wield.Outcome, wield.Stop), wield.Refusal?.ToString());
        CollectionAssert.AreEqual(new[]
        {
            new ScrCallRequest(ScrCallKind.Remove, Shield, 0, null, Item, 0),
            new ScrCallRequest(ScrCallKind.Schedule, Shield, 3, Pc, Item, 15 * 8000),
        }, wield.Effects.Calls.ToArray());
        Assert.AreEqual(0, wield.Effects.AmmoUses.Count, "the wear itself uses no cell");

        // A due call while the wearer wears the shield: one power cell from the wearer, then the next call.
        var worn = ScrDispatcher.RunCall(new(Shield, 3, Pc, Item), HostFor(program, true), options);
        Assert.AreEqual(ScrStop.ReturnRunDefault, worn.Stop, worn.Refusal?.ToString());
        CollectionAssert.AreEqual(new[] { new ScrAmmoUse(Pc, 1, 2) }, worn.Effects.AmmoUses.ToArray());
        CollectionAssert.AreEqual(new[] { new ScrCallRequest(ScrCallKind.Schedule, Shield, 3, Pc, Item, 15 * 8000) }, worn.Effects.Calls.ToArray());

        // After the wearer took it off: the call returns and the loop ends (no cell, no next call).
        var off = ScrDispatcher.RunCall(new(Shield, 3, Pc, Item), HostFor(program, false), options);
        Assert.AreEqual(ScrStop.ReturnRunDefault, off.Stop, off.Refusal?.ToString());
        Assert.IsTrue(off.Effects.IsEmpty);
    }

    [TestMethod]
    public void The_shield_s_timer_uses_a_cell_while_worn_and_ends_when_it_is_not()
    {
        CheckShield(Synthetic);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_shield_script_runs_the_same_way()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in shield script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var key = stack.Keys("scr/").Single(k => k.StartsWith("scr/29016", StringComparison.Ordinal) && k.EndsWith(".scr", StringComparison.Ordinal));
        var read = stack.Read(stack.Resolve(key).Winner!);
        Assert.AreEqual(ContentReadStatus.Read, read.Status);
        var program = ScrProgram.Read(read.Bytes!).Program!;
        // The set the item event runs it under needs nothing the deferred call does not, and the other way round for the cell action.
        Assert.IsTrue(ScrAdmission.AdmitFrom(program, Set, 0).IsAdmitted, "wield on, from line 0");
        Assert.IsTrue(ScrAdmission.AdmitFrom(program, Set, 3).IsAdmitted, "the call, from line 3");
        CheckShield(program);
    }
}
