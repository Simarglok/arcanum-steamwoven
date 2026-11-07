using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-191: the AREA conversation host's story state, known area and map area ports for the dialog codes SS, AR and IA (ledger DCC7, DAC7, SS1, WT2,
/// GA2). Synthetic content: the invented SCR of <see cref="AreaReactionConversationTests"/> and an invented DLG.</summary>
[TestClass]
public sealed class AreaStoryConversationTests
{
    private const string Dialog = "{1}{Well?}{}{}{}{}{}"
        + "{2}{Learn.}{}{1}{ss -5, ar -9}{0}{mm 9, ss 6, ss 2}"
        + "{3}{Known.}{}{1}{ss 6, ar 9}{0}{}";
    private const string MapAreaDialog = "{1}{Well?}{}{}{}{}{}" + "{2}{Where.}{}{1}{ia 3}{0}{}";

    [TestMethod]
    public void Mm_and_ss_write_the_session_and_ar_and_ss_read_it_back()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var first = f.Host.Begin(f.Speaker).Conversation!;
        CollectionAssert.AreEqual(new[] { "Learn." }, first.Options.Select(o => o.Text).ToArray(), "unknown area, story state 0: only the first gate holds: ");
        first.Select(first.Options[0].Token);
        Assert.IsNull(first.Failure);
        Assert.IsTrue(f.Session.IsAreaKnown(9), "mm marks the area known in the session (the world map and ar read it)");
        Assert.AreEqual(6, f.Session.QueryStoryState(), "ss raises the state and keeps the larger of 6 and 2");
        Assert.IsTrue(f.Host.EndReaction());
        var second = f.Host.Begin(f.Speaker).Conversation!;
        CollectionAssert.AreEqual(new[] { "Known." }, second.Options.Select(o => o.Text).ToArray(), "story state 6 and area 9 known");
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void Ia_stays_outside_the_subset_without_a_map_area_answer()
    {
        // The fixture has no use host of the shown map (no map mode): the area of the PC's map is unknown, so the port is not offered.
        using var f = AreaReactionConversationTests.Fixture.Create(MapAreaDialog);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogFrontierKind.UnsupportedOperation, talk.Frontier!.Kind);
        Assert.IsTrue(f.Host.EndReaction());
    }
}
