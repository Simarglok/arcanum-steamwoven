using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaCompositionFixture;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaCompositionTests
{
    [TestMethod]
    public void CompleteNormalBranchMatrixHasLiteralGeometryAndInterleaving()
    {
        var frame = new ActorDiagnosticFrame(new(2667,0x20000000,0,0,0),9,7,4,3,0,0,1,6);
        foreach (var item in Cases)
        {
            var scene = Create(item); var input = Placement(item);
            var actor = ActorDiagnosticScenePlan.Place(input,frame,ActorDiagnosticProfile.HumanFixed);
            Assert.AreEqual(item.ActorRectangle,actor.Destination,item.Name);
            Assert.AreEqual(item.Order,Order(scene.Plan,input,actor),item.Name);
            Assert.AreEqual(2,scene.Plan.Coverage.Count,item.Name);
            Assert.AreEqual(scene.Plan.Commands.Count,scene.Plan.Commands.Select(command=>command.Identity).Distinct().Count(),item.Name);
            if (item.Order.Contains('A'))
            {
                var a = scene.Plan.Commands.Single(command=>command.Identity==scene.A.Key);
                Assert.AreEqual(item.CanopyRectangle,a.Destination,item.Name);
                Assert.AreEqual((92928,82560,20,-20),(a.MapX,a.MapY,a.FirstRank,a.SecondRank),item.Name);
            }
            else
            {
                var row = scene.Plan.Coverage.Single(row=>row.Identity==scene.A.Key);
                Assert.AreEqual(Arcanum.TerrainViewer.Core.AreaDisposition.Suppressed,row.Disposition,item.Name);
                Assert.AreEqual(item.Name == "inventory" ? "ExteriorSectorInsertionRefusedByInventory"
                    : item.Name == "invisible" ? "InvisibleNoDetectionObserver" : "BodySuppressedBySerializedState",row.Reason,item.Name);
            }
            Assert.AreEqual(new TerrainRect(0,-120,160,200),scene.Plan.Commands.Single(command=>command.Identity==scene.B.Key).Destination,item.Name);
        }
    }

    [TestMethod]
    public void BodyReflectionUsesSerializedFlipAndDescriptorMirrorWithoutChangingPlacementOrRank()
    {
        foreach (var mirrored in new[] { false,true })
        foreach (var blit in new uint[] { 0,1,2,3 })
        {
            var body = Body(0,blit:blit); var visual = Visual(body,160,160,40,80,mirrored:mirrored);
            var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
            Assert.AreEqual(new TerrainRect(0,-60,160,160),command.Destination);
            Assert.AreEqual(mirrored != ((blit & 1) != 0),command.FlipX);
            Assert.AreEqual((blit & 2) != 0,command.FlipY);
            Assert.AreEqual((92928,82560,20,-20),(command.MapX,command.MapY,command.FirstRank,command.SecondRank));
        }
    }

    [TestMethod]
    public void MissingStateAndUnsupportedTranslucencyAndBlendFailRatherThanBecomeHidden()
    {
        var body = Body(0);
        foreach (var slot in new[] { 8,9,10,11,18 })
            Assert.ThrowsExactly<InvalidOperationException>(()=>AreaScenePlan.Create([], [body with {
                EffectiveFields = body.EffectiveFields.Where(field=>field.Slot!=slot).ToArray() }], [Visual(body,160,160,40,80)]));
        foreach (var bad in new[] { Body(0,flags:0x40),Body(0,flags:0x20000),Body(0,blit:0x20),Body(0,scale:0),Body(0,scale:201) })
            Assert.ThrowsExactly<InvalidOperationException>(()=>AreaScenePlan.Create([], [bad], [Visual(bad,160,160,40,80)]));
    }
}
