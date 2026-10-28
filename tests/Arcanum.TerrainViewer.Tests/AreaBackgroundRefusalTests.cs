using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-WORLD-069: the viewer names the Technophobia refusal of the insert check in its inventory and schematic messages.
[TestClass]
public sealed class AreaBackgroundRefusalTests
{
    [TestMethod]
    public void The_technophobia_refusal_has_its_own_text()
    {
        Assert.AreEqual("the hero's Technophobia refuses technological items",
            AreaInventoryHost.Refusal(GameSessionFailure.InsertRefused, SessionInsertVerdict.TechnologyRefused));
        Assert.AreEqual("the PC cannot take it", AreaInventoryHost.Refusal(GameSessionFailure.InsertRefused, SessionInsertVerdict.NoRoom));
        Assert.AreEqual("the PC cannot take it", AreaInventoryHost.Refusal(GameSessionFailure.InsertRefused));
        SessionAssemblyResult Refused(SessionInsertVerdict? verdict) =>
            new(GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, null, [], [], default) { Verdict = verdict };
        Assert.AreEqual("the hero's Technophobia refuses the technological product", AreaSchematics.Refused(Refused(SessionInsertVerdict.TechnologyRefused)));
        Assert.AreEqual("no room for the product and no tile to leave it on", AreaSchematics.Refused(Refused(SessionInsertVerdict.NoRoom)));
    }

    // ARC-WORLD-070: a completed assembly whose inventory refused units names where they went.
    [TestMethod]
    public void A_product_left_on_the_ground_is_named_after_the_completion()
    {
        SessionAssemblyResult Done(SessionInsertVerdict? verdict, params string[] ground) =>
            new(GameSessionDisposition.Changed, GameSessionFailure.None, null, [], [], default) { Verdict = verdict, OnGround = ground };
        Assert.AreEqual("", AreaSchematics.OnGroundText(Done(null)));
        Assert.AreEqual(" — the PC cannot take it: the product lies on the ground", AreaSchematics.OnGroundText(Done(SessionInsertVerdict.NoRoom, "made:1")));
        Assert.AreEqual(" — the hero's Technophobia refuses technological items: 2 products lie on the ground",
            AreaSchematics.OnGroundText(Done(SessionInsertVerdict.TechnologyRefused, "made:1", "made:2")));
        Assert.AreEqual(" — the NPC cannot take it: the product lies on the ground", AreaSchematics.OnGroundText(Done(SessionInsertVerdict.NoRoom, "made:1"), npc: true));
    }
}
