using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-304 (B24, SU15, GT218): the dialog point of a dead NPC. The conversation host's script gates see the NPC dead as the session holds it, so point 9 of a dead
/// NPC whose script file lacks the death-speech flag 0x8 runs default (the generic greeting, which is not modelled: the start answers GenericGreeting and opens
/// no window), and the NPC whose file has the flag runs its own dialog. A living NPC is unchanged. Synthetic content (the reaction fixture's invented SCR and DLG).
/// </summary>
[TestClass]
public sealed class AreaDeadSpeakerConversationTests
{
    private static void Kill(AreaReactionConversationTests.Fixture f) =>
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.Kill(f.Session.Version, SessionLifeTarget.Critter(f.Entity, new LifeFacts(), LifeState.Unharmed)).Disposition);

    [TestMethod]
    public void A_dead_npc_whose_script_has_no_death_speech_runs_default_and_opens_no_window()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true);
        Kill(f);
        Assert.IsTrue(f.Session.QueryNpcDead(f.Entity));
        var begin = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.GenericGreeting, begin.Status, begin.Detail);
        Assert.IsNull(begin.Conversation);
    }

    [TestMethod]
    public void A_dead_npc_whose_script_has_death_speech_runs_its_own_dialog()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true, deathSpeech: true);
        Kill(f);
        var begin = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.Started, begin.Status, begin.Detail);
        Assert.IsNotNull(begin.Conversation);
    }

    [TestMethod]
    public void A_living_npc_runs_its_dialog_whatever_the_flag()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true);
        var begin = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.Started, begin.Status, begin.Detail);
    }
}
