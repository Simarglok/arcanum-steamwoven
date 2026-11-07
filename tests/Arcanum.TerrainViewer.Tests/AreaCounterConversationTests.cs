using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-196 (the fixture's dialog attachment loads the counters 1, 2, 0, 0): the AREA conversation host's state is the counter port, so lc reads and writes the byte counters of the speaker's dialog attachment, which the script host reads back.
/// Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaCounterConversationTests
{
    private const string Dialog = "{1}{Hello.}{}{}{}{}{}"
        + "{2}{Fresh.}{}{1}{lc 1 2}{0}{lc 1 9}"
        + "{3}{Nine.}{}{1}{lc 1 9}{0}{lc 3 200}"
        + "{4}{Reset.}{}{1}{lc 3 200}{0}{lc 1 2, lc 3 0}";

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
    public void The_counters_start_at_the_loaded_values_and_follow_the_actions_and_a_reset_clears_the_session_entry()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var key = MapChangeKey.ForEntity(f.Entity, AreaScriptHost.CountersProperty);
        var map = f.Host.State.MapOf(0);
        Assert.IsNull(f.Session.QueryObjectChange(map, key));
        CollectionAssert.AreEqual(new[] { "Fresh." }, Offered(f));
        Pick(f, "Fresh.");
        Assert.AreEqual(0x0901L, f.Session.QueryObjectChange(map, key), "counter 1 holds 9; counter 0 keeps the loaded 1");
        CollectionAssert.AreEqual(new[] { "Nine." }, Offered(f));
        Pick(f, "Nine.");
        Assert.AreEqual(0xC8000901L, f.Session.QueryObjectChange(map, key), "counter 3 holds 200 in the top byte, counter 1 is kept");
        CollectionAssert.AreEqual(new[] { "Nine.", "Reset." }, Offered(f));
        Pick(f, "Reset.");
        Assert.IsNull(f.Session.QueryObjectChange(map, key), "back at the loaded value the entry is cleared");
        CollectionAssert.AreEqual(new[] { "Fresh." }, Offered(f));
    }
}
