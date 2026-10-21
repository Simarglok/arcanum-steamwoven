using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-TECHNOLOGY-006: the AREA conversation host's schematic port for the dialog action SU (ledger NC1–NC2). Synthetic content:
/// the invented SCR of <see cref="AreaReactionConversationTests"/> and an invented DLG whose reply carries SU.</summary>
[TestClass]
public sealed class AreaSchematicConversationTests
{
    private const string Dialog = "{1}{Shall I build it?}{}{}{}{}{}" + "{2}{Build it.}{}{1}{}{0}{su}";

    [TestMethod]
    public void Su_ends_the_talk_and_names_the_speaker_as_crafter_once()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.IsNull(f.Host.SchematicCrafter);
        Assert.AreEqual(DialogOptionTransition.Schematics, talk.Options[0].Transition);
        Assert.AreEqual(DialogConversationSelectStatus.Ended, talk.Select(talk.Options[0].Token).Status);
        Assert.AreEqual(DialogEndKind.SchematicWindow, talk.End!.Kind);
        Assert.AreEqual(f.Entity, f.Host.TakeSchematicCrafter());
        Assert.IsNull(f.Host.TakeSchematicCrafter(), "taken once");
        Assert.IsTrue(f.Host.EndReaction());
    }

    [TestMethod]
    public void Su_for_a_dead_speaker_or_hero_is_rejected()
    {
        using var f = AreaReactionConversationTests.Fixture.Create(Dialog);
        var talk = f.Host.Begin(f.Speaker).Conversation!;
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.Kill(f.Session.Version, SessionLifeTarget.Critter(f.Entity, new(), LifeState.Unharmed)).Disposition);
        talk.Select(talk.Options[0].Token);
        Assert.AreEqual(DialogConversationFailureKind.CommandRejected, talk.Failure!.Kind, "the original's open refuses a dead crafter");
        Assert.IsNull(f.Host.SchematicCrafter);
    }
}
