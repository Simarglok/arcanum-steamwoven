using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-040: the three sound actions as dispatcher effects (docs/research/audio-evidence.md SN28: play sound
/// once without a position, play sound on once on the object, play sound scheme as the scheme request). Programs are invented.</summary>
[TestClass]
public sealed class ScrSoundTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    private static readonly ScrRunOptions S7 = new() { Admission = ScrAdmissionSet.S7 };

    private static ScrProgram Sounds() => Program(
        Do(C(ScrCatalogue.Action.PlaySound, N(3008))),
        Do(C(ScrCatalogue.Action.Assign, (Local, 0), N(4010))),
        Do(C(ScrCatalogue.Action.PlaySoundOn, (Local, 0), (Attachee, 0))),
        Do(C(ScrCatalogue.Action.PlaySoundOn, N(4011), (Triggerer, 0))),
        Do(C(ScrCatalogue.Action.PlaySoundOn, N(4012), (Extra, 0))),
        Do(C(ScrCatalogue.Action.PlaySoundScheme, N(15), N(39))),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)));

    [TestMethod]
    public void Sound_actions_are_admitted_from_s7_and_returned_in_action_order()
    {
        var host = new FakeHost();
        host.Attach(Chest, Use, 500, Sounds());
        var refused = ScrDispatcher.RunObject(new(Use, Chest, Pc, null), host, new() { Admission = ScrAdmissionSet.S6 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.PlaySound), (refused.Outcome, refused.Refusal!.Code, refused.Refusal.Type));

        var run = ScrDispatcher.RunObject(new(Use, Chest, Pc, null), host, S7);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[]
        {
            new ScrSound(ScrSoundKind.Effect, 3008),
            new ScrSound(ScrSoundKind.OnObject, 4010, 0, Chest),
            new ScrSound(ScrSoundKind.OnObject, 4011, 0, Pc),
            new ScrSound(ScrSoundKind.Scheme, 15, 39),
        }, run.Effects.Sounds.ToArray(), "a value operand is read; an absent object (no extra object) plays nothing");
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_run_with_only_sounds_is_not_empty_and_s6_stays_inside_s7()
    {
        var host = new FakeHost();
        host.Attach(Chest, Use, 501, Program(Do(C(ScrCatalogue.Action.PlaySoundScheme, N(0), N(0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        var run = ScrDispatcher.RunObject(new(Use, Chest, Pc, null), host, S7);
        CollectionAssert.AreEqual(new[] { new ScrSound(ScrSoundKind.Scheme, 0, 0) }, run.Effects.Sounds.ToArray(), "0/0 silences both slots");
        Assert.IsFalse(run.Effects.IsEmpty);
        Assert.IsTrue(ScrAdmissionSet.S6.IsSubsetOf(ScrAdmissionSet.S7));
        Assert.IsTrue(ScrDispatcher.RunObject(new(Use, Chest, Pc, null), host, new() { Admission = ScrAdmissionSet.S6 }).Outcome == ScrOutcome.Refused);
    }
}
