using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.TerrainViewer.Tests.AreaCompositionFixture;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaCompositionTests
{
    [TestMethod]
    public void CachedUprightOrderIsIdenticalToTheFullStableSortForEveryMarkerPosition()
    {
        var frame = new ActorDiagnosticFrame(new(2667,0x20000000,0,0,0),9,7,4,3,0,0,1,6);
        foreach (var item in Cases)
        {
            var scene = Create(item); var input = Placement(item);
            var actor = ActorDiagnosticScenePlan.Place(input,frame,ActorDiagnosticProfile.HumanFixed);
            CollectionAssert.AreEqual(AreaScenePlan.UprightOrder(scene.Plan.Commands,input,actor,ActorKey).ToArray(),
                new AreaUprightOrder(scene.Plan.Commands).With(input,actor,ActorKey).ToArray(),item.Name);
        }
        // Many bodies with duplicate keys in shuffled input order, including bodies whose
        // seven keys tie with the actor marker: stability must match the full sort.
        var random = new Random(7); var bodies = new List<AreaDrawCommand>();
        for (var i = 0; i < 400; i++)
        {
            var tie = i % 10 == 0;
            var identity = new AreaObjectKey("s", 1, tie ? AreaSourceKind.Mobile : (AreaSourceKind)random.Next(3), 1,
                tie ? int.MaxValue : random.Next(4), tie ? 0 : random.Next(3), "id" + i, 1);
            bodies.Add(new AreaDrawCommand(default, default!, identity, default, new(0,0,1,1), false, false, 4,
                AreaScenePlan.CameraOriginX + random.Next(4), AreaScenePlan.CameraOriginY + random.Next(4),
                random.Next(-2,3), random.Next(-2,3), 255, false));
        }
        var commands = bodies.OrderBy(_ => random.Next()).Concat(bodies.Take(5).Select(b => b with { Phase = 0 })).ToArray();
        var cached = new AreaUprightOrder(commands); var ties = 0;
        var actorFrame = ActorDiagnosticScenePlan.Place(Placement(Cases[0]),frame,ActorDiagnosticProfile.HumanFixed);
        for (var x = -1; x <= 4; x++)
        for (var y = -1; y <= 4; y++)
        foreach (var (ox, oy) in new[] { (0,0), (40,0), (36,-4), (44,4), (39,1), (-1,-1) })
        {
            var placement = Placement(Cases[0]) with { MapX = x, MapY = y, InstanceOffsetX = ox, InstanceOffsetY = oy };
            var expected = AreaScenePlan.UprightOrder(commands,placement,actorFrame,ActorKey);
            var actual = cached.With(placement,actorFrame,ActorKey);
            Assert.AreEqual(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++) Assert.AreEqual(expected[i], actual[i], $"x={x} y={y} offset=({ox},{oy}) index={i}");
            if (ox is 40 or 39) ties += bodies.Count(b => b.MapX == expected.Single(c => c.Identity == ActorKey).MapX
                && b.MapY == expected.Single(c => c.Identity == ActorKey).MapY && b.FirstRank == 0 && b.SecondRank == 0 && b.Identity!.Value.SourceIndex == int.MaxValue);
        }
        Assert.IsTrue(ties > 0, "the matrix must contain full seven-key ties with the actor marker");
    }

    [TestMethod]
    public void MovedBodiesOnTheirOwnKeyKeepTheStableSortOrder()
    {
        // ARC-RENDER-035: a moved command with its T0 body's key (another frame) takes the T0 place, which must equal the
        // stable sort of the remaining bodies with the moved commands appended, also among bodies with tied keys.
        var frame = new ActorDiagnosticFrame(new(2667,0x20000000,0,0,0),9,7,4,3,0,0,1,6);
        var random = new Random(11); var bodies = new List<AreaDrawCommand>();
        for (var i = 0; i < 300; i++)
        {
            var tie = i % 7 == 0;
            var identity = new AreaObjectKey("s", 1, tie ? AreaSourceKind.Mobile : (AreaSourceKind)random.Next(3), 1,
                tie ? 5 : random.Next(4), tie ? 0 : random.Next(3), "id" + i, 1);
            bodies.Add(new AreaDrawCommand(default, default!, identity, default, new(0,0,1,1), false, false, 4,
                AreaScenePlan.CameraOriginX + random.Next(3), AreaScenePlan.CameraOriginY + random.Next(3), random.Next(-1,2), random.Next(-1,2), 255, false));
        }
        var commands = bodies.OrderBy(_ => random.Next()).ToArray();
        var cached = new AreaUprightOrder(commands);
        var actor = ActorDiagnosticScenePlan.Place(Placement(Cases[0]),frame,ActorDiagnosticProfile.HumanFixed);
        for (var round = 0; round < 20; round++)
        {
            // Some bodies on another frame (same key, new destination), some truly moved.
            var moving = bodies.Where(_ => random.Next(4) == 0).Select(b => random.Next(3) == 0
                ? b with { MapX = b.MapX + 1, Destination = new(1,1,2,2) } : b with { Destination = new(5,5,3,3) }).ToList();
            var keys = moving.Select(m => m.Identity).ToHashSet();
            var expected = AreaScenePlan.UprightOrder(commands.Where(c => !keys.Contains(c.Identity)).Concat(moving), Placement(Cases[0]), actor, ActorKey);
            var actual = cached.With(Placement(Cases[0]), actor, ActorKey, moving);
            CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray(), $"round {round}");
        }
    }

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
                Assert.AreEqual(Arcanum.World.AreaDisposition.Suppressed,row.Disposition,item.Name);
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
        // Wading (0x20000) is drawn since ARC-RENDER-018 (AreaScenePlanTests).
        foreach (var bad in new[] { Body(0,flags:0x40),Body(0,blit:0x20),Body(0,scale:0),Body(0,scale:201) })
            Assert.ThrowsExactly<InvalidOperationException>(()=>AreaScenePlan.Create([], [bad], [Visual(bad,160,160,40,80)]));
    }

    [TestMethod]
    public void WallAndPortalHotspotsShiftForRotationsZeroOneSixSevenAndMirrorWithTheFlipBit()
    {
        var body = Body(0);
        AreaVisualRecord Wall(uint raw) => Visual(body,100,60,30,50) with { RawArt = raw, Mirrored = (raw & 1) != 0 };
        static uint Raw(int rotation, bool flip) => 0x10000000u | ((uint)rotation << 11) | (flip ? 0x11u : 0u);
        Assert.AreEqual((-10,70), AreaScenePlan.Hotspot(Wall(Raw(1,false))));
        Assert.AreEqual((30,50), AreaScenePlan.Hotspot(Wall(Raw(5,false))));
        Assert.AreEqual((100-30-2,50), AreaScenePlan.Hotspot(Wall(Raw(3,true))));
        Assert.AreEqual((100-(-10)-2,70), AreaScenePlan.Hotspot(Wall(Raw(7,true))), "Shift first, then mirror.");
        Assert.AreEqual((30,50), AreaScenePlan.Hotspot(Visual(body,100,60,30,50)), "Non-wall bodies keep their frame hotspot.");
        // Portals (art class 3) take the same rule (ARC-RENDER-017); critters (class 2) do not.
        Assert.AreEqual((100-(-10)-2,70), AreaScenePlan.Hotspot(Wall(Raw(7,true) & 0x0FFFFFFFu | 0x30000000u)));
        Assert.AreEqual((30,50), AreaScenePlan.Hotspot(Wall(Raw(4,false) & 0x0FFFFFFFu | 0x30000000u)));
        Assert.AreEqual((30,50), AreaScenePlan.Hotspot(Wall(Raw(7,false) & 0x0FFFFFFFu | 0x20000000u)));
        // The plan places a wall from that hotspot and draws it flipped.
        var wall = Wall(Raw(7,true));
        var command = AreaScenePlan.Create([], [body], [wall]).Commands.Single();
        Assert.AreEqual((40-108, 20-70), (command.Destination.X, command.Destination.Y));
        Assert.IsTrue(command.FlipX);
    }
}
