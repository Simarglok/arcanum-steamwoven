using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-088: the AREA conversation host's window port for the dialog action c-e (ledger WN1, WN2): the talk ends and the host names
/// the speaker whose sheet opens. Synthetic content: the invented SCR of <see cref="AreaReactionConversationTests"/> and an invented DLG whose
/// reply carries the action (spelled in two parts here as in the dialog grammar).</summary>
[TestClass]
public sealed class AreaNpcSheetConversationTests
{
    private static readonly string Dialog = "{1}{What can you do?}{}{}{}{}{}" + "{2}{Show me your abilities.}{}{1}{}{0}{" + "c" + "e" + "}";

    [TestMethod]
    public void The_sheet_action_ends_the_talk_and_names_the_speaker_once()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog, hero: true);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsNull(f.Host.NpcSheetSpeaker);
        Assert.AreEqual(DialogOptionTransition.NpcSheet, talk.Options[0].Transition);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        Assert.AreEqual(DialogEndKind.NpcSheetWindow, talk.End!.Kind);
        Assert.AreEqual((f.Entity, f.Speaker.Index), f.Host.TakeNpcSheetSpeaker());
        Assert.IsNull(f.Host.TakeNpcSheetSpeaker(), "taken once");
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void Without_a_hero_the_session_offers_no_sheet_window()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreNotEqual(DialogOptionTransition.NpcSheet, talk.Options[0].Transition, "a session with no hero has no local PC window to open");
        Assert.IsNull(f.Host.NpcSheetSpeaker);
    }
}
