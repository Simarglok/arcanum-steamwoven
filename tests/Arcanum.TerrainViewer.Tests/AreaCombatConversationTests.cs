using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-190: the AREA conversation host's combat port for the dialog action CO (ledger SU13, DAC1, CA1–CA2). Synthetic content:
/// the invented SCR of <see cref="AreaReactionConversationTests"/> and an invented DLG whose reply carries CO and a final line.</summary>
[TestClass]
public sealed class AreaCombatConversationTests
{
    private const string Dialog = "{1}{Well?}{}{}{}{}{}" + "{2}{Fight.}{}{1}{}{0}{co, fl 3}" + "{3}{Argh.}{}{}{}{}{}";

    [TestMethod]
    public void Co_runs_the_hero_attack_on_the_speaker_after_the_final_line()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var attacked = new List<string>();
        f.Host.HeroAttack = entity => { attacked.Add(entity); return true; };
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsNull(talk.Options[0].Block);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        CollectionAssert.AreEqual(new[] { f.Entity }, attacked, "once, on the speaker's entity");
        Assert.AreEqual(DialogEndKind.NpcFinalLine, talk.End!.Kind);
        Assert.IsInstanceOfType<StartCombatCommand>(talk.Effects[^1].Command);
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void Co_stays_unadmitted_without_the_viewer_sink_and_fails_when_the_sink_refuses()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var plain = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, plain.Options[0].Block!.Kind, "no AI host: the action is outside the subset");
        Assert.IsTrue(f.Host.EndReaction());
        f.Host.HeroAttack = _ => false;
        var refused = f.Host.Begin(f.Speaker).Conversation!;
        refused.Select(refused.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, refused.Failure!.Kind);
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void Co_is_not_offered_while_the_viewer_has_no_ai_host()
    {
        // The viewer's sink exists always; its availability (the AI host) is read when the talk starts, so a talk is never offered a co it must then refuse.
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var attacked = 0;
        var available = false;
        f.Host.HeroAttack = _ => { attacked++; return true; };
        f.Host.HeroAttackAvailable = () => available;
        var without = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(DialogOptionBlockKind.UnsupportedOperation, without.Options[0].Block!.Kind);
        Assert.IsTrue(f.Host.EndReaction());
        available = true;
        var with = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsNull(with.Options[0].Block);
        with.Select(with.Options[0].Token);
        Assert.AreEqual(1, attacked);
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void A_rebound_host_keeps_the_sink()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        Func<string, bool> sink = _ => true;
        Func<bool> available = () => true;
        f.Host.HeroAttack = sink;
        f.Host.HeroAttackAvailable = available;
        var rebound = f.Host.Rebind(f.Session);
        Assert.AreSame(sink, rebound.HeroAttack);
        Assert.AreSame(available, rebound.HeroAttackAvailable);
    }
}
