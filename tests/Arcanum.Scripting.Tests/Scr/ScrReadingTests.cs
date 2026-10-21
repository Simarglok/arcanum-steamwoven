using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-UI-049: show book (64), show picture (65) and show plaque (114) as dispatcher effects (ledger WR rows: the number and
/// the viewer object, an absent viewer shows nothing, the run goes on). Programs are invented.</summary>
[TestClass]
public sealed class ScrReadingTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    // S7 plus the reading actions (a subset of ScrAdmissionSet.S10 without S4's guard conditions, which need object queries).
    private static readonly ScrRunOptions S7Reading = new()
    {
        Admission = new("S7-reading", ScrAdmissionSet.S7.Conditions,
            ScrAdmissionSet.S7.Actions.Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque]),
            ScrAdmissionSet.S7.ValueKinds, ScrAdmissionSet.S7.FocusKinds),
    };

    [TestMethod]
    public void The_reading_actions_are_admitted_with_s10_and_returned_in_order()
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, Program(
            Do(C(ScrCatalogue.Action.ShowBook, N(1020), (Triggerer, 0))),
            Do(C(ScrCatalogue.Action.ShowPicture, N(900), (Extra, 0))),
            Do(C(ScrCatalogue.Action.ShowPlaque, N(1500), (Attachee, 0))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        var refused = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, new() { Admission = ScrAdmissionSet.S7 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.ShowBook), (refused.Outcome, refused.Refusal!.Code, refused.Refusal.Type));
        var run = ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, S7Reading);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop, "the run goes on after each request");
        CollectionAssert.AreEqual(new[] { new ScrReadingRequest(ScrReadingKind.Book, 1020, Pc), new ScrReadingRequest(ScrReadingKind.Plaque, 1500, Npc) },
            run.Effects.Readings.ToArray(), "an absent viewer (no extra object) shows nothing");
        Assert.IsFalse(run.Effects.IsEmpty);
        Assert.IsTrue(ScrAdmissionSet.S9.IsSubsetOf(ScrAdmissionSet.S10));
        Assert.IsTrue(S7Reading.Admission.IsSubsetOf(ScrAdmissionSet.S10));
        Assert.IsFalse(ScrAdmissionSet.S9.Actions.Contains(ScrCatalogue.Action.ShowPlaque));
    }
}
