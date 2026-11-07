using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-229: the AREA conversation host's window port (ledger WN1–WN4). Only the character editor's action is offered, and only with a session hero; the ii and ri item windows are
/// not built, so those codes stay outside the subset. Synthetic content: an invented SCR whose dialog action starts DLG node 1, and an invented DLG.
/// </summary>
[TestClass]
public sealed class AreaWindowConversationTests
{
    [TestMethod]
    public void Without_a_session_hero_and_for_the_item_windows_the_window_actions_stay_unadmitted()
    {
        foreach (var action in new[] { "c" + "e", "ii", "ri" })
        {
            using var f = AreaReactionConversationTests.Fixture.Create("{1}{Well?}{}{}{}{}{}" + "{2}{Go.}{}{1}{}{0}{" + action + "}");
            Assert.IsNull(f.Session.QueryHero(), "the fixture's diagnostic PC has no hero");
            f.Host.EndReaction();
            var talk = f.Host.Begin(f.Speaker).Conversation!;
            Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, talk.Options.Single(option => option.Text == "Go.").Block?.Kind, action);
            Assert.IsFalse(f.Host.TakeEditorOpen(), action);
        }
    }
}
