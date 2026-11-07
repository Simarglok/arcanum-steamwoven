using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-241 (MIG13 S14f, GT183/GT184): the six cast actions of set S29 hand one cast request each to the host, with the spell and the invocation flags, and the run goes on.</summary>
[TestClass]
public sealed class ScrCastTests
{
    private const byte Triggerer = 0;
    private const int TileScript = 1724;
    private static readonly ScrAdmissionSet Set = new("test-casts", ScrAdmissionSet.S2.Conditions,
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastSpellOn, ScrCatalogue.Action.CastFreeSpell, ScrCatalogue.Action.CastUnresistableSpellBy,
            ScrCatalogue.Action.CastFreeUnresistableSpell, ScrCatalogue.Action.CastUnresistableSpellOn]), ScrAdmissionSet.S2.ValueKinds, ScrAdmissionSet.S3.FocusKinds);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private static ScrRunResult Step(ScrProgram program)
    {
        var host = new FakeHost();
        host.Scripts[TileScript] = ScrScriptLookup.Of(program);
        return ScrDispatcher.RunTile(new(new ScrSlot(TileScript, 0, 0), Npc), host, Options);
    }

    [TestMethod]
    public void The_caster_actions_hand_over_target_caster_spell_and_flags()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.CastSpell, (Triggerer, 0), N(7), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CastFreeSpell, (Triggerer, 0), N(8), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CastUnresistableSpellBy, (Triggerer, 0), N(9), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CastFreeUnresistableSpell, (Triggerer, 0), N(10), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, "GT154: a cast goes on to the next line");
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 7, 0, Npc),
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 8, (int)ScrCastFlags.Free, Npc),
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 9, (int)ScrCastFlags.Unresistable, Npc),
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 10, (int)(ScrCastFlags.Free | ScrCastFlags.Unresistable), Npc),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void The_casterless_actions_hand_over_no_caster_and_a_missing_target_casts_nothing()
    {
        var run = Step(Program(
            Do(C(ScrCatalogue.Action.CastSpellOn, N(7), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CastUnresistableSpellOn, N(8), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.CastSpellOn, N(9), (1, -1))),
            Do(C(ScrCatalogue.Action.CastSpell, (Triggerer, 0), N(9), (1, -1))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 7),
            new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 8, (int)ScrCastFlags.Unresistable),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void A_cast_with_an_absent_caster_is_a_cast_with_none()
    {
        var run = Step(Program(Do(C(ScrCatalogue.Action.CastSpell, (1, -1), N(7), (Triggerer, 0))), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.CastSpell, Npc, 7) }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void S29_adds_the_casts_over_S28()
    {
        var program = Program(Do(C(ScrCatalogue.Action.CastSpellOn, N(7), (Triggerer, 0))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S29).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S28).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S28.IsSubsetOf(ScrAdmissionSet.S29));
    }
}
