using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-MAGIC-041 (ledger TI8): use ammunition units (135), operand 0 the count and operand 1 the object. Programs and numbers are invented.</summary>
[TestClass]
public sealed class ScrAmmoUseTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1;
    // The commands these programs use (S3: control flow, arithmetic, the single-object focus kinds) with use ammunition units; S32 itself also holds
    // the commands that need the world, so its host would have to answer more.
    private static readonly ScrAdmissionSet Set = new("ammo-use", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Append(ScrCatalogue.Action.UseAmmoUnits),
        ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    private static ScrRunResult Run(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, program);
        return ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, new() { Admission = Set });
    }

    private static Clause Spend((byte, int) count, (byte, int) holder) => C(ScrCatalogue.Action.UseAmmoUnits, count, holder);

    [TestMethod]
    public void The_action_asks_for_its_count_of_power_cells_from_the_object_in_action_order_and_goes_on()
    {
        var run = Run(Program(Do(Spend(N(1), (Triggerer, 0))), Do(C(ScrCatalogue.Action.Assign, (Counter, 0), N(3))), Do(Spend((Counter, 0), (Attachee, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { new ScrAmmoUse(Pc, 1, 2), new ScrAmmoUse(Npc, 3, 2) }, run.Effects.AmmoUses.ToArray(), "type 2 (the power cells), each request in action order");
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_zero_or_negative_count_or_an_absent_object_asks_for_nothing()
    {
        // The unit use (0x4658E0) does nothing for a count of 0 and for a null source; a negative count would add to the source, which no
        // installed script asks and the project does not model.
        var run = Run(Program(Do(Spend(N(0), (Triggerer, 0))), Do(Spend(N(-2), (Triggerer, 0))), Do(Spend(N(1), (12, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrStop.ReturnRunDefault, run.Stop);
        Assert.AreEqual(0, run.Effects.AmmoUses.Count);
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void The_action_is_outside_the_old_sets()
    {
        var program = Program(Do(Spend(N(1), (Triggerer, 0))), Do(C(1)));
        var refused = ScrAdmission.Admit(program, ScrAdmissionSet.S31);
        Assert.AreEqual((ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.UseAmmoUnits), (refused.Refusal!.Code, refused.Refusal.Type));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S32).IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(program, Set).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S31.IsSubsetOf(ScrAdmissionSet.S32) && !ScrAdmissionSet.S32.IsSubsetOf(ScrAdmissionSet.S31));
    }
}
