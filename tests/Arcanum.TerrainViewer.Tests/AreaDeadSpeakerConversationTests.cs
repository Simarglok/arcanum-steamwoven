using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-304 (B24, SU15, GT218): the dialog point of a dead NPC. The conversation host's script gates see the NPC dead as the session holds it, so point 9 of a dead
/// NPC whose script file lacks the death-speech flag 0x8 runs default (the generic greeting, ARC-DEV-324: the start answers GenericGreeting, opens no conversation and
/// floats the greeting builder's line when the module's texts and the NPC's facts read), and the NPC whose file has the flag runs its own dialog. A living NPC is unchanged. Synthetic content (the reaction fixture's invented SCR and DLG).
/// </summary>
[TestClass]
public sealed class AreaDeadSpeakerConversationTests
{
    private static void Kill(AreaReactionConversationTests.Fixture f) =>
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.Kill(f.Session.Version, SessionLifeTarget.Critter(f.Entity, new LifeFacts(), LifeState.Unharmed)).Disposition);

    [TestMethod]
    public void A_dead_npc_whose_script_has_no_death_speech_runs_default_and_opens_no_conversation()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true);
        Kill(f);
        Assert.IsTrue(f.Session.QueryNpcDead(f.Entity));
        var begin = f.Host.Begin(f.Speaker);
        Assert.AreEqual(AreaConversationStartStatus.GenericGreeting, begin.Status, begin.Detail);
        Assert.IsNull(begin.Conversation);
        // The synthetic speaker row holds no NPC fields, so the greeting has no facts (or the module no texts): nothing is said or floated (the line itself is the unit tests' and the smoke's real-data check).
        Assert.IsNull(begin.GreetingLine);
        Assert.AreEqual(0, f.Host.Scripts.TakeFloats().Count);
        Assert.IsFalse(begin.Detail.Contains("the generic greeting:"), begin.Detail);
    }

    [TestMethod]
    public void A_resumed_dialog_script_that_runs_default_says_no_greeting()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true);
        Kill(f);
        var resumed = f.Host.Resume(f.Speaker, new Arcanum.Scripting.Dialog.DialogScriptLine(1));
        Assert.AreEqual(AreaConversationStartStatus.GenericGreeting, resumed.Status, resumed.Detail);
        StringAssert.Contains(resumed.Detail, "a resumed script: no greeting");
        Assert.IsNull(resumed.GreetingLine);
        Assert.AreEqual(0, f.Host.Scripts.TakeFloats().Count);
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

    // ARC-AI-1320 (GT122): the hero's talk goal reaches a dead target: point 9 of its script runs (0x441980 has no death test) but the dialog start callback refuses silently and the generic greeting is the empty line.
    [TestMethod]
    public void A_dead_talk_target_whose_script_asks_for_a_dialog_gets_no_conversation()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true, deathSpeech: true);
        Kill(f);
        var begin = f.Host.Begin(f.Speaker, deadTarget: true);
        Assert.AreEqual(AreaConversationStartStatus.DialogRefused, begin.Status, begin.Detail);
        Assert.IsNull(begin.Conversation);
        Assert.IsNull(begin.Document, "the dialog file is not even read");
        Assert.AreEqual(0, f.Host.Scripts.TakeFloats().Count);
    }

    [TestMethod]
    public void A_dead_talk_target_that_runs_default_says_nothing_and_floats_nothing()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(npc: true);
        Kill(f);
        var begin = f.Host.Begin(f.Speaker, deadTarget: true);
        Assert.AreEqual(AreaConversationStartStatus.GenericGreeting, begin.Status, begin.Detail);
        Assert.IsNull(begin.GreetingLine);
        Assert.AreEqual(0, f.Host.Scripts.TakeFloats().Count);
        StringAssert.Contains(begin.Detail, "the empty line");
    }
}
