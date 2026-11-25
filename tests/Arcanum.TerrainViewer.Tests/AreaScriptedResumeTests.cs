using Arcanum.Session;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2961 (ledger TG10): a conversation a use, tile or call-script run opened reads the speaker's dialog slot (point 9) for its LF and LC, and its "return to script" end resumes that slot
/// at the held line + 1 or the reply's line, as the original's 0x567E30 → 0x568480 does whatever script started the talk. Synthetic content (the fixture's SCR: line 0 dialog node 1, line 1 return skip default).
/// </summary>
[TestClass]
public sealed class AreaScriptedResumeTests
{
    // Counter 1 is loaded as 2: "Fresh." is offered and sets it to 9, then ends with "return to script" (response 0).
    private const string Dialog = "{1}{Hello.}{}{}{}{}{}"
        + "{2}{Fresh.}{}{1}{lc 1 2}{0}{lc 1 9}"
        + "{3}{Back.}{}{1}{}{-1}{}";

    private static ScrDialogStart Use(AreaReactionConversationTests.Fixture f, int line) =>
        new(f.Host.Scripts.Pc, f.Host.Scripts.Object(f.Speaker), 777, line, 1) { Point = ScrPoint.Use };

    [TestMethod]
    public void A_use_started_talk_runs_lf_and_lc_on_the_speakers_dialog_slot_and_its_end_names_the_held_line_plus_one()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var start = f.Host.BeginScripted(f.Speaker, Use(f, 5));
        Assert.AreEqual(AreaConversationStartStatus.Started, start.Status, start.Detail);
        var talk = start.Conversation!;
        CollectionAssert.AreEqual(new[] { "Fresh.", "Back." }, talk.Options.Select(option => option.Text).ToArray(), "lc reads the dialog slot's counters (1, 2)");
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(option => option.Text == "Fresh.").Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(6)), talk.End, "the held line is the use script's own line 5, plus one");
        Assert.AreEqual(0x0901L, f.Session.QueryObjectChange(f.Host.State.MapOf(0), MapChangeKey.ForEntity(f.Entity, AreaScriptHost.CountersProperty)), "lc wrote the dialog slot's counter");
    }

    [TestMethod]
    public void The_resume_runs_the_speakers_point_nine_script_at_the_held_line_of_the_script_that_started_the_talk()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        // Held line 0 of a use script: the resume starts the dialog script at line 1 (return, skip default), a line of another script applied to this one (TG10).
        var talk = f.Host.BeginScripted(f.Speaker, Use(f, 0)).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(option => option.Text == "Fresh.").Token).Status);
        var resumed = f.Host.Resume(f.Speaker, talk.End!.ScriptLine!.Value);
        Assert.AreEqual(AreaConversationStartStatus.ScriptEnded, resumed.Status, resumed.Detail);
        Assert.AreEqual(1, f.Host.LastRun!.StopLine);
        // A reply's own line, -1, runs line 1 too; line 0 would be the dialog action again (a new talk).
        var again = f.Host.BeginScripted(f.Speaker, Use(f, 3)).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, again.Select(again.Options.Single(option => option.Text == "Back.").Token).Status);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(1)), again.End);
        Assert.AreEqual(AreaConversationStartStatus.ScriptEnded, f.Host.Resume(f.Speaker, again.End!.ScriptLine!.Value).Status);
    }

    [TestMethod]
    public void A_resume_line_past_the_script_runs_nothing()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var resumed = f.Host.Resume(f.Speaker, new DialogScriptLine(6));
        Assert.AreNotEqual(AreaConversationStartStatus.Started, resumed.Status, resumed.Detail);
        Assert.IsNull(resumed.Conversation);
    }

    [TestMethod]
    public void A_speaker_with_no_dialog_script_still_refuses_a_scripted_talk_that_reads_lf_or_lc_but_admits_one_that_does_not()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog, dialogSlotScript: 0);
        var refused = f.Host.BeginScripted(f.Speaker, Use(f, 0));
        Assert.AreEqual(AreaConversationStartStatus.EntryNotAdmitted, refused.Status, refused.Detail);
        using var plain = AreaReactionConversationTests.Fixture.Create("{1}{Hello.}{}{}{}{}{}{2}{Bye.}{}{1}{}{0}{}", dialogSlotScript: 0);
        var admitted = plain.Host.BeginScripted(plain.Speaker, Use(plain, 0));
        Assert.AreEqual(AreaConversationStartStatus.Started, admitted.Status, admitted.Detail);
    }
}
