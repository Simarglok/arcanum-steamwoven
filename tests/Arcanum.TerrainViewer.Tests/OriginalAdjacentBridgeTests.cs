using System.Text.Json;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class OriginalAdjacentBridgeTests
{
    [TestMethod, TestCategory("RealData")]
    public void GuardedOriginalAdjacentEvidenceAndAllEightHumanFacingsStayInZone()
    {
        if(Environment.GetEnvironmentVariable("ARCANUM_GRID_ORIGINAL")!="1")Assert.Inconclusive("Opt-in actual original input gate");
        string Input(string name)=>Environment.GetEnvironmentVariable(name)??throw new InvalidOperationException("Required original role absent: "+name);
        var args=TerrainViewerArguments.Parse(["--mode","world-diagnostic","--module",Input("ARCANUM_STATIC_MODULE"),"--resource",Input("ARCANUM_STATIC_RESOURCE"),
            "--prototypes",Input("ARCANUM_STATIC_PROTOTYPES"),"--actor-resource",Input("ARCANUM_ACTOR_RESOURCE"),"--source-id","ORIGINAL_ADJACENT_TEST"]);
        using var operation=new TerrainLoadCoordinator.ProductionTerrainLoadOperation();var loaded=operation.Execute(7,args,default);var s=loaded.TraversalSnapshot!;
        Assert.AreEqual(CardinalTraversalAdmissionPolicy.BoundedAdjacent,s.AdmissionPolicy);Assert.AreEqual("BASE_SECTOR_1452_1290_ADJACENT_V1",s.ProfileId);
        Assert.AreEqual(317,s.Census.Count);
        var rows=new List<object>();
        foreach(var (x,y,d) in new[]{(5,0,0),(5,2,2),(7,2,4),(7,0,6)})
        {
            var a=s.Assess(new(s.ProfileId,s.InputIdentity,7,1452,1290,new(6,1),new(x,y),(CardinalTraversalDirection)d,s.DeclaredContext));
            Assert.AreEqual(CardinalTraversalCoverage.Supported,a.Coverage);Assert.AreEqual(0,a.Gaps.Count);Assert.AreEqual(4,a.Legs.Count);
            Assert.AreEqual(d is 0 or 2?CardinalTraversalDisposition.Blocked:CardinalTraversalDisposition.Open,a.Disposition);
            Assert.IsTrue(a.Legs.All(l=>l.RecordAssessments.Count==317 && l.Gaps.Count==0));
            rows.Add(new{from=new[]{6,1},to=new[]{x,y},direction=d,profile=a.StepEvidence.ProfileId,coverage=a.Coverage.ToString(),disposition=a.Disposition.ToString(),reason=a.ReasonCode,
                legs=a.Legs.Select(l=>new{role=l.Role.ToString(),from=l.From,to=l.To,direction=l.Direction,originalDirection=l.OriginalDirection,records=l.RecordAssessments.Count,
                    blockers=l.RecordAssessments.Where(r=>r.Result.Disposition==CardinalTraversalDisposition.Blocked).Select(r=>r.SourceIndex).ToArray(),gaps=l.Gaps.Count}).ToArray()});
        }
        foreach(var (direction,x,y,facing) in new[]{(NeighborDirection.XMinusYMinus,6,1,0),(NeighborDirection.XMinus,6,2,1),(NeighborDirection.XMinusYPlus,6,3,2),
            (NeighborDirection.YPlus,7,3,3),(NeighborDirection.XPlusYPlus,8,3,4),(NeighborDirection.XPlus,8,2,5),(NeighborDirection.XPlusYMinus,8,1,6),(NeighborDirection.YMinus,7,1,7)})
        {
            var actor=new DiagnosticGridActor();actor.Configure(s,SimulationTime.At(0));
            InputBatch At(int n,int d,long seq,NeighborDirection? dir)=>new(SimulationTime.At(n,d),[MovementInput.Set(seq,dir is{} v?ActorIntent.Toward(v):ActorIntent.Neutral)]);
            actor.Advance(SimulationTime.At(1),[At(0,1,0,NeighborDirection.XPlusYPlus),At(1,2,1,null)],p=>loaded.ActorPlan!.Select(p));
            Assert.AreEqual(new CellCoord(7,2),actor.Session!.State.CommittedCell);
            var move=actor.Advance(SimulationTime.At(9,4),[At(2,1,2,direction)],p=>loaded.ActorPlan!.Select(p));
            Assert.AreEqual(MovementStatus.Completed,move.Movement.Status);Assert.AreEqual(CardinalTraversalDisposition.Open,move.Evidence.Single().Assessment.Disposition);
            Assert.AreEqual(new CellCoord(x,y),actor.Session.State.CommittedCell);Assert.AreEqual(facing,actor.State.Facing);
            rows.Add(new{from=new[]{7,2},to=new[]{x,y},direction=direction.ToString(),facing,coverage="Supported",disposition="Open",keys=move.Evidence.Select(e=>e.Key).ToArray()});
        }
        Console.WriteLine("ORIGINAL_ADJACENT_NUMERIC "+JsonSerializer.Serialize(rows));
    }
}
