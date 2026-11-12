using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-1241 (docs/research/bink-movies.md MV5): fade (103), four numbers in order: seconds, sound (the movie's audio track when a movie follows), movie, and the flag
/// that picks how the screen comes back. Programs and numbers are invented.</summary>
[TestClass]
public sealed class ScrFadeTests
{
    private const int Use = 1;
    // The commands these programs use (S3: control flow, arithmetic, the single-object focus kinds) with fade; S34 itself also holds the commands that need the world.
    private static readonly ScrAdmissionSet Set = new("fade", ScrAdmissionSet.S3.Conditions, ScrAdmissionSet.S3.Actions.Append(ScrCatalogue.Action.Fade),
        ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    private static ScrRunResult Run(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Npc, Use, 2023, program);
        return ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, new() { Admission = Set });
    }

    private static Clause Fade((byte, int) seconds, (byte, int) sound, (byte, int) movie, (byte, int) flag) => C(ScrCatalogue.Action.Fade, seconds, sound, movie, flag);

    [TestMethod]
    public void The_action_hands_its_four_operands_as_read_in_action_order_and_the_run_goes_on()
    {
        // 1093's own line: no time, track 1, movie 4, the screen back at once; then a counter value read as an operand, as the original reads each operand.
        var run = Run(Program(Do(Fade(N(0), N(1), N(4), N(0))), Do(C(ScrCatalogue.Action.Assign, (Counter, 0), N(6))), Do(Fade(N(30), N(0), (Counter, 0), N(2))),
            Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        CollectionAssert.AreEqual(new[] { new ScrFade(0, 1, 4, 0), new ScrFade(30, 0, 6, 2) }, run.Effects.Fades.ToArray(), "operands as read, in action order");
        Assert.IsFalse(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void A_fade_without_a_movie_is_still_an_effect_and_a_run_without_one_has_none()
    {
        var fade = Run(Program(Do(Fade(N(5), N(0), N(0), N(1))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        CollectionAssert.AreEqual(new[] { new ScrFade(5, 0, 0, 1) }, fade.Effects.Fades.ToArray());
        var none = Run(Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(0, none.Effects.Fades.Count);
        Assert.IsTrue(none.Effects.IsEmpty);
    }

    [TestMethod]
    public void The_action_is_outside_the_old_sets()
    {
        var program = Program(Do(Fade(N(0), N(0), N(4), N(0))), Do(C(1)));
        var refused = ScrAdmission.Admit(program, ScrAdmissionSet.S33);
        Assert.AreEqual((ScrRefusalCode.ActionNotAdmitted, ScrCatalogue.Action.Fade), (refused.Refusal!.Code, refused.Refusal.Type));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S34).IsAdmitted);
        Assert.IsTrue(ScrAdmission.Admit(program, Set).IsAdmitted);
        Assert.IsTrue(ScrAdmissionSet.S33.IsSubsetOf(ScrAdmissionSet.S34) && !ScrAdmissionSet.S34.IsSubsetOf(ScrAdmissionSet.S33));
    }
}
