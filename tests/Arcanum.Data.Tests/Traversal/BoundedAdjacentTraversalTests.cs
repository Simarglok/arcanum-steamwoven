using System.Buffers.Binary;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Traversal.CardinalTraversalFixture;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class BoundedAdjacentTraversalTests
{
    private static CardinalTraversalRequest Request(CardinalTraversalSnapshot s, int x, int y, int direction) =>
        new(s.ProfileId, s.InputIdentity, s.EvidenceEpoch, 1452, 1290, new(6, 1), new(x, y),
            (CardinalTraversalDirection)direction, s.DeclaredContext);
    private static CardinalTraversalSnapshot Adjacent(CardinalTraversalFixture f) => f.Inspect(policy: CardinalTraversalAdmissionPolicy.BoundedAdjacent);

    [TestMethod]
    [DataRow(5, 0, 0, 1, 7)]
    [DataRow(5, 2, 2, 1, 3)]
    [DataRow(7, 2, 4, 5, 3)]
    [DataRow(7, 0, 6, 5, 7)]
    public void AllDiagonalsHaveFourLiteralObjectLegsAndOneFinalTerrain(int x, int y, int direction, int dx, int dy)
    {
        using var f = new CardinalTraversalFixture(); var s = Adjacent(f); var a = s.Assess(Request(s, x, y, direction));
        Assert.AreEqual("BASE_SECTOR_1452_1290_ADJACENT_V1", s.ProfileId);
        Assert.AreEqual(CardinalTraversalDisposition.Open, a.Disposition); Assert.IsTrue(a.StepEvidence.CompleteCoverage);
        Assert.AreSame(a.Legs, a.StepEvidence.Legs); Assert.AreEqual(s.ProfileId, a.StepEvidence.ProfileId);
        CollectionAssert.AreEqual(new[]{new CardinalTraversalCell(6,1),new(x,1),new(6,1),new(6,y)},a.Legs.Select(l=>l.From).ToArray());
        CollectionAssert.AreEqual(new[]{new CardinalTraversalCell(x,1),new(x,y),new(6,y),new(x,y)},a.Legs.Select(l=>l.To).ToArray());
        CollectionAssert.AreEqual(new[]{dx,dy,dy,dx},a.Legs.Select(l=>(int)l.Direction).ToArray());
        CollectionAssert.AreEqual(new[]{0,1,2,3},a.Legs.Select(l=>(int)l.Role).ToArray());
        Assert.IsTrue(a.Legs.All(l => (int)l.OriginalDirection == direction && l.Gaps.Count == 0
            && l.Components.All(c => (int)c.ComponentKind >= 7 && c.Inputs.Single().Value == direction)));
        Assert.AreEqual(3,a.Components.Count(c=>(int)c.ComponentKind is >= 4 and <= 6));
        var old=f.Inspect(); Assert.AreEqual("DIAGONAL_RULE_UNPROVED",old.Assess(Request(old,x,y,direction)).ReasonCode);
        Assert.AreEqual(CardinalTraversalCoverage.Unsupported, s.Assess(Request(old,x,y,direction)).Coverage);
    }

    [TestMethod]
    [DataRow(5, 0, 0)] [DataRow(5, 2, 2)] [DataRow(7, 2, 4)] [DataRow(7, 0, 6)]
    public void EitherShoulderAndBothFinalLegsDenyWithoutLosingOtherEvidence(int x, int y, int direction)
    {
        foreach(var cell in new[]{new CardinalTraversalCell(x,1),new(6,y),new(x,y)})
        {
            using var f=new CardinalTraversalFixture([Record(0,World(92928+cell.X,82560+cell.Y)),Record(1024)]);
            var s=Adjacent(f);var a=s.Assess(Request(s,x,y,direction));
            Assert.AreEqual(CardinalTraversalCoverage.Supported,a.Coverage);Assert.AreEqual(CardinalTraversalDisposition.Blocked,a.Disposition);
            Assert.AreEqual("ADMITTED_ADJACENT_SCENERY_BLOCKER",a.ReasonCode);Assert.AreEqual(4,a.Legs.Count);
            Assert.IsTrue(a.Legs.All(l=>l.RecordAssessments.Count==2 && l.RecordAssessments.All(r=>r.Result.Inputs.Any(i=>i.Code=="ORIGINAL_DIRECTION" && i.Value==direction))));
            var blocked=a.Legs.Where(l=>l.RecordAssessments[0].Result.Disposition==CardinalTraversalDisposition.Blocked).Select(l=>(int)l.Role).ToArray();
            CollectionAssert.AreEqual(cell==new CardinalTraversalCell(x,1)?new[]{0}:cell==new CardinalTraversalCell(6,y)?new[]{2}:new[]{1,3},blocked);
            Assert.IsTrue(a.RecordAssessments[0].Result.Disposition==CardinalTraversalDisposition.Blocked || cell!=new CardinalTraversalCell(x,y));
        }
    }

    [TestMethod]
    public void ShoulderAndSourceTerrainAreNotQueriedWhileFinalTerrainAndMissingFactsRemainRequired()
    {
        foreach(var ordinal in new[]{71,134,70})
        {
            using var f=new CardinalTraversalFixture(blockedOrdinal:ordinal);var s=Adjacent(f);
            Assert.AreEqual(CardinalTraversalDisposition.Open,s.Assess(Request(s,7,2,4)).Disposition);
        }
        foreach(var raw in new[]{0x180u,0xB0000000u,0x10000000u})
        {
            var sector=Sector([]);BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(4+135*4),raw);
            using var f=new CardinalTraversalFixture(sector:sector);var s=Adjacent(f);var a=s.Assess(Request(s,7,2,4));
            Assert.AreEqual(raw==0x180?CardinalTraversalDisposition.Open:raw==0xB0000000?CardinalTraversalDisposition.Blocked:null,a.Disposition);
        }
        using(var f=new CardinalTraversalFixture(blockedOrdinal:135,names:null))
        {
            var s=Adjacent(f);var a=s.Assess(Request(s,7,2,4));Assert.AreEqual(CardinalTraversalDisposition.Blocked,a.Disposition);
            Assert.IsTrue(a.Gaps.Any(g=>g.ReasonCode=="TILE_NAME_ENTRY_MISSING"));Assert.AreEqual(4,a.Legs.Count);
        }
        using(var f=new CardinalTraversalFixture([Record(0,World(92935,82561))],names:null))
        {
            var s=Adjacent(f);var a=s.Assess(Request(s,7,2,4));Assert.AreEqual(CardinalTraversalDisposition.Blocked,a.Disposition);Assert.AreEqual(1,a.Gaps.Count);
        }
    }

    [TestMethod]
    public void CensusUncertaintySurvivesEveryLegAndBlocksNoUnknownIntoOpen()
    {
        foreach(var row in new[]{Record(null),Record(8192),Record(0,type:0),Record(0,type:2),AbsentLocation(),Record(0,World(int.MinValue,int.MaxValue))})
        {
            using var f=new CardinalTraversalFixture([Record(0,World(92935,82561)),row]);var s=Adjacent(f);var a=s.Assess(Request(s,7,2,4));
            Assert.AreEqual(CardinalTraversalCoverage.Unknown,a.Coverage);Assert.IsNull(a.Disposition);Assert.AreEqual(4,a.Legs.Count);
            Assert.IsTrue(a.Legs.All(l=>l.Gaps.Count>0 && l.Gaps.All(g=>g.LegRole==l.Role)));
            Assert.IsTrue(a.Gaps.Count>16 || a.Gaps.Count>=a.Legs.Sum(l=>l.Gaps.Count));
        }
        using var missing=new CardinalTraversalFixture(mask:false);var ms=Adjacent(missing);
        Assert.AreEqual(CardinalTraversalCoverage.Unknown,ms.Assess(Request(ms,7,2,4)).Coverage);
    }

    [TestMethod]
    public void OwnedPolicyEvidenceFailsClosedAtEpochContextZoneAndSurvivesDisposal()
    {
        var f=new CardinalTraversalFixture();var s=Adjacent(f);var request=Request(s,7,2,4);f.Dispose();
        Assert.AreEqual(CardinalTraversalDisposition.Open,s.Assess(request).Disposition);
        foreach(var r in new[]{new CardinalTraversalRequest(s.ProfileId,s.InputIdentity,8,1452,1290,new(6,1),new(7,2),CardinalTraversalDirection.XPlusYPlus,s.DeclaredContext),
            new(s.ProfileId,s.InputIdentity,7,1452,1290,new(8,3),new(9,4),CardinalTraversalDirection.XPlusYPlus,s.DeclaredContext),
            new(s.ProfileId,s.InputIdentity,7,1452,1290,new(6,1),new(int.MaxValue,int.MinValue),CardinalTraversalDirection.XPlusYPlus,s.DeclaredContext)})
        {var a=s.Assess(r);Assert.AreEqual(CardinalTraversalCoverage.Unsupported,a.Coverage);Assert.AreEqual(0,a.Legs.Count);}
        var path=new CardinalTraversalContext(s.DeclaredContext.ContextId,15,2,0,0x1000,false,false,CardinalTraversalActorState.OrdinaryGround,0);
        var branch=s.Assess(new(s.ProfileId,s.InputIdentity,7,1452,1290,new(6,1),new(7,2),CardinalTraversalDirection.XPlusYPlus,path));
        Assert.AreEqual("PATH_BRANCH_UNAVAILABLE",branch.ReasonCode);Assert.AreEqual(0,branch.Legs.Count);
        Assert.Throws<NotSupportedException>(()=>((IList<CardinalTraversalLegEvidence>)s.Assess(request).Legs).Clear());
    }
}
