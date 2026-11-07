using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-229: the AREA conversation host offers none of the window actions yet (ledger WN3): the NPC sheet window and the ii and ri item windows are not built, so their replies are offered
/// blocked. Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaWindowConversationTests
{
    [TestMethod]
    public void The_window_actions_stay_unadmitted_in_the_viewer()
    {
        foreach (var action in new[] { "c" + "e", "ii", "ri" })
        {
            using var f = AreaReactionConversationTests.Fixture.Create("{1}{Well?}{}{}{}{}{}" + "{2}{Go.}{}{1}{}{0}{" + action + "}");
            f.Host.EndReaction();
            var talk = f.Host.Begin(f.Speaker).Conversation!;
            Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, talk.Options.Single(option => option.Text == "Go.").Block?.Kind, action);
        }
    }
}
