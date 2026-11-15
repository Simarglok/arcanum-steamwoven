using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-193: the AREA conversation host's hostility port, the reply step's hostile check (ledger HR1–HR3). Synthetic content: the
/// invented SCR of <see cref="AreaReactionConversationTests"/> and an invented DLG whose one reply goes to a node.</summary>
[TestClass]
public sealed class AreaHostileConversationTests
{
    private const string Dialog = "{1}{Well?}{}{}{}{}{}" + "{2}{Go on.}{}{1}{}{3}{}" + "{3}{Argh.}{}{}{}{}{}";

    [TestMethod]
    public void A_speaker_the_host_calls_hostile_ends_the_talk_at_the_reply()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var asked = new List<string>();
        f.Host.NpcHostile = entity => { asked.Add(entity); return true; };
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { f.Entity }, asked, "once, on the speaker's entity");
        Assert.AreEqual(DialogEndKind.HostileRefusal, talk.End!.Kind);
        Assert.AreEqual("Well?", talk.NpcLine!.Text);
        Assert.AreEqual("[the NPC turned hostile and ended the dialog with its last line]", AreaConversationText.Outcome(talk));
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void A_calm_speaker_and_a_host_without_the_check_let_the_reply_go_on()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var plain = f.Host.Begin(f.Speaker).Conversation!;
        plain.Select(plain.Options[0].Token);
        Assert.AreEqual("Argh.", plain.NpcLine!.Text, "no AI host: the check is not made");
        Assert.IsTrue(f.Host.EndReaction());

        f.Host.NpcHostile = _ => false;
        var calm = f.Host.Begin(f.Speaker).Conversation!;
        calm.Select(calm.Options[0].Token);
        Assert.AreEqual("Argh.", calm.NpcLine!.Text);
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void The_check_is_not_made_while_the_viewer_has_no_ai_host_and_a_rebound_host_keeps_it()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var asked = 0;
        var available = false;
        Func<string, bool> sink = _ => { asked++; return true; };
        f.Host.NpcHostile = sink;
        f.Host.HeroAttackAvailable = () => available;
        var without = f.Host.Begin(f.Speaker).Conversation!;
        without.Select(without.Options[0].Token);
        Assert.AreEqual(0, asked);
        Assert.AreEqual("Argh.", without.NpcLine!.Text);
        Assert.IsTrue(f.Host.EndReaction());
        available = true;
        var with = f.Host.Begin(f.Speaker).Conversation!;
        with.Select(with.Options[0].Token);
        Assert.AreEqual(1, asked);
        Assert.IsTrue(f.Host.EndReaction());
        Assert.AreSame(sink, f.Host.Rebind(f.Session).NpcHostile);
    }

    /// <summary>ARC-SCRIPT-1598 (0x567460 after the dialog action's earlier actions): a dialog start the callback refuses opens no conversation and starts no reaction record; a callback that accepts changes nothing.</summary>
    [TestMethod]
    public void A_dialog_start_the_callback_refuses_opens_no_conversation()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var asked = new List<string>();
        var refused = f.Host.Begin(f.Speaker, startRefused: speaker => { asked.Add(AreaConversationState.EntityId(speaker.Row)); return true; });
        Assert.AreEqual(AreaConversationStartStatus.DialogStartRefused, refused.Status, refused.Detail);
        Assert.IsNull(refused.Conversation);
        CollectionAssert.AreEqual(new[] { f.Entity }, asked, "asked once, for the speaker");
        Assert.IsFalse(f.Host.EndReaction(), "no reaction conversation was opened");
        var accepted = f.Host.Begin(f.Speaker, startRefused: _ => false);
        Assert.AreEqual(AreaConversationStartStatus.Started, accepted.Status, accepted.Detail);
        Assert.IsNotNull(accepted.Conversation);
        Assert.IsTrue(f.Host.EndReaction());
    }
}
