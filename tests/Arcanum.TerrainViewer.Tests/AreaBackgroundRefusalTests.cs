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
        Assert.AreEqual("no room for the product (the original would leave it on the ground)", AreaSchematics.Refused(Refused(SessionInsertVerdict.NoRoom)));
    }
}
