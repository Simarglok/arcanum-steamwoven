using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-195: the AREA conversation host passes the companion port, so the fo condition and the wa, lv, so and sc actions read and change the speaker's party record in the session.
/// Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG; the speaker is no roster companion (no wait timer facts).
/// </summary>
[TestClass]
public sealed class AreaCompanionConversationTests
{
    private const string Dialog = "{1}{Hello.}{}{}{}{}{}"
        + "{2}{Free.}{}{1}{fo 0}{0}{}"
        + "{3}{Led.}{}{1}{fo 1}{0}{}"
        + "{4}{Spread.}{}{1}{}{0}{so}"
        + "{5}{Clear.}{}{1}{}{0}{sc}"
        + "{6}{Wait.}{}{1}{fo 1}{0}{wa}"
        + "{7}{Leave.}{}{1}{fo 1}{0}{lv}";

    private static string[] Offered(AreaReactionConversationTests.Fixture f)
    {
        f.Host.EndReaction();
        var begin = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.Started, begin.Status, begin.Detail);
        return begin.Conversation!.Options.Select(option => option.Text).ToArray();
    }

    private static void Pick(AreaReactionConversationTests.Fixture f, string text)
    {
        f.Host.EndReaction();
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(option => option.Text == text).Token).Status, text);
        Assert.IsNull(talk.Failure, text);
    }

    [TestMethod]
    public void The_replies_follow_the_party_record_and_the_actions_change_it()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        // Free: fo 0 passes, fo 1 does not.
        CollectionAssert.AreEqual(new[] { "Free.", "Spread.", "Clear." }, Offered(f));
        Pick(f, "Spread.");
        Assert.IsTrue(f.Session.QueryCompanion(f.Entity).SpreadOut);
        Pick(f, "Clear.");
        Assert.IsFalse(f.Session.QueryCompanion(f.Entity).SpreadOut);

        // Following: fo 1 passes, fo 0 does not; wa makes it wait (still led by the PC), lv forces it out.
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ForceJoinParty(f.Session.Version, f.Entity).Disposition);
        CollectionAssert.AreEqual(new[] { "Led.", "Spread.", "Clear.", "Wait.", "Leave." }, Offered(f));
        Pick(f, "Wait.");
        Assert.AreEqual(CompanionStanding.Waiting, f.Session.QueryCompanion(f.Entity).Standing);
        CollectionAssert.AreEqual(new[] { "Led.", "Spread.", "Clear.", "Wait.", "Leave." }, Offered(f), "a waiting NPC is still led by the PC");
        Pick(f, "Leave.");
        Assert.AreEqual(CompanionStanding.Free, f.Session.QueryCompanion(f.Entity).Standing);
        Assert.IsFalse(f.Session.QueryCompanion(f.Entity).Jilted, "a forced leave does not jilt");
    }

    [TestMethod]
    public void A_wait_on_an_npc_that_is_not_in_the_party_fails_the_conversation_and_changes_nothing()
    {
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Hello.}{}{}{}{}{}{2}{Wait.}{}{1}{}{0}{wa}");
        f.Host.EndReaction();
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        var revision = f.Session.Version.Revision;
        talk.Select(talk.Options.Single().Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, talk.Failure!.Kind);
        Assert.AreEqual(CompanionStanding.Free, f.Session.QueryCompanion(f.Entity).Standing);
        Assert.AreEqual(revision, f.Session.Version.Revision);
        StringAssert.Contains(AreaConversationText.Effect(talk.Effects.Last()), "WA NPC waits");
    }
}
