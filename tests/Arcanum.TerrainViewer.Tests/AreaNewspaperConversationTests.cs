using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-230 (ledger NQ1–NQ6): the AREA conversation host queues newspaper issues in the session's queue (np) and sets the speaker's origin as a session object field (or), kept only
/// while it differs from the map data's, with the live talk's reputation scope following. Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaNewspaperConversationTests
{
    private static void Pick(AreaReactionConversationTests.Fixture f, string text)
    {
        f.Host.EndReaction();
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options.Single(option => option.Text == text).Token).Status, text);
        Assert.IsNull(talk.Failure, text);
    }

    [TestMethod]
    public void The_np_action_queues_the_issues_in_the_session_queue_by_the_originals_rule()
    {
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Well?}{}{}{}{}{}" + "{2}{Print.}{}{1}{}{0}{np 1001, np 1002 1, np 1001}" + "{3}{Bare.}{}{1}{}{0}{np }");
        CollectionAssert.AreEqual(new[] { -1, -1, -1, -1, 5000 }, f.Session.QueryNewspaper().Entries.Take(5).ToArray());
        Pick(f, "Print.");
        // 1001 appended, 1002 pushed in behind the head, the second 1001 already queued: no change.
        CollectionAssert.AreEqual(new[] { 5000, 1002, 1001, 0 }, f.Session.QueryNewspaper().Entries.Skip(4).Take(4).ToArray());
        Assert.AreEqual(7, f.Session.QueryNewspaper().Count);
        var before = f.Session.QueryNewspaper();
        Pick(f, "Bare.");
        CollectionAssert.AreEqual(before.Entries.ToArray(), f.Session.QueryNewspaper().Entries.ToArray(), "np without an operand is np 0: issue 0 is found among the empty words");
    }

    [TestMethod]
    public void The_or_action_sets_the_speakers_origin_in_the_session_and_in_the_live_scope_and_clears_it_at_the_loaded_value()
    {
        const int loaded = 0;
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Well?}{}{}{}{}{}" + "{2}{Become.}{}{1}{}{0}{or 7}" + $"{{3}}{{Revert.}}{{}}{{1}}{{}}{{0}}{{or {loaded}}}", npc: true);
        var map = new SessionMapId(f.Speaker.Row.Key.RuntimeMap);
        var key = SessionNpcOrigin.Key(f.Entity);
        Assert.IsNull(f.Session.QueryMapChange(map, key));
        Assert.AreEqual(loaded, AreaConversationHost.Scope(f.Speaker.Row, f.Session).Origin, "the map data's origin of the fixture NPC");
        Pick(f, "Become.");
        Assert.AreEqual(7L, f.Session.QueryMapChange(map, key));
        Assert.AreEqual(7, AreaConversationHost.Scope(f.Speaker.Row, f.Session).Origin, "a later talk reads the set origin");
        Pick(f, "Revert.");
        Assert.IsNull(f.Session.QueryMapChange(map, key), "back at the map data's origin the field is cleared");
        Assert.AreEqual(loaded, AreaConversationHost.Scope(f.Speaker.Row, f.Session).Origin);
    }

    [TestMethod]
    public void The_or_action_on_a_speaker_that_is_not_an_npc_does_nothing_as_in_the_original()
    {
        using var f = AreaReactionConversationTests.Fixture.Create("{1}{Well?}{}{}{}{}{}" + "{2}{Become.}{}{1}{}{0}{or 7}");
        Pick(f, "Become.");
        Assert.IsNull(f.Session.QueryMapChange(new SessionMapId(f.Speaker.Row.Key.RuntimeMap), SessionNpcOrigin.Key(f.Entity)));
    }
}
