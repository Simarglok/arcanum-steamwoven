using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaOwnerRepositoryTests
{
    [TestMethod]
    public void StrictOwnerRetainsAllAdjacentCellsAndNullOccurrencesOnOneChart()
    {
        using var fixture=new AreaOwnerFixture();
        var snapshot=fixture.Owner.LoadStrict(); var plan=AreaScenePlan.Create(snapshot);
        Assert.AreEqual("structural-file-effective-v1",snapshot.Profile);
        Assert.AreEqual(4,snapshot.Reconciliation.CoreSectors);
        Assert.AreEqual(16384,snapshot.Reconciliation.CoreTerrain);
        Assert.AreEqual(5,snapshot.Objects.Count);
        Assert.AreEqual(16389,plan.Commands.Count);
        Assert.AreEqual(16389,plan.Coverage.Count);
        Assert.AreEqual(16389,plan.Commands.Select(c=>c.Visual).Distinct().Count());
        CollectionAssert.AreEqual(AreaOwnerFixture.Sectors,snapshot.Sectors.Where(s=>s.Scope==AreaScope.Core).Select(s=>s.Key.Packed).ToArray());
        // Independently calculated literal X seam, Y seam and shared corner.
        var expected=new[] {
            (86570436012L,4095,92991,82623,new TerrainRect(1,2520,78,40),new TerrainRect(36,2534,128,160)),
            (86570436013L,4032,92992,82623,new TerrainRect(-39,2540,78,40),new TerrainRect(12,2536,128,160)),
            (86637544876L,63,92991,82624,new TerrainRect(41,2540,78,40),new TerrainRect(76,2554,128,160)),
            (86637544877L,0,92992,82624,new TerrainRect(1,2560,78,40),new TerrainRect(52,2556,128,160)) };
        foreach(var (sector,index,x,y,terrain,body) in expected)
        {
            var command=plan.Commands.Single(c=>c.Identity is null&&c.Visual.Sector.Packed==sector&&c.Visual.SourceIndex==index);
            Assert.AreEqual(x,command.MapX); Assert.AreEqual(y,command.MapY); Assert.AreEqual(terrain,command.Destination);
            var bodies=plan.Commands.Where(c=>c.Identity?.Sector==sector).ToArray();
            Assert.AreEqual(sector==86570436012L?2:1,bodies.Length);
            foreach(var item in bodies) { Assert.AreEqual(x,item.MapX); Assert.AreEqual(y,item.MapY); Assert.AreEqual(body,item.Destination); }
        }
        Assert.AreEqual(5,plan.Commands.Where(c=>c.Identity is not null).Select(c=>c.Identity).Distinct().Count());
        Assert.AreEqual(1,snapshot.Objects.Select(o=>o.Key.ObjectIdSha256).Distinct().Count());
        Assert.AreEqual(2,snapshot.Objects.Count(o=>o.Key.SourceIndex==0&&o.Key.Offset==16436&&o.WorldY==82623));
        Assert.IsFalse(snapshot.RendererExecutionVerified); // no GUI claim from these tests
    }

    [TestMethod]
    public void ProductionCopiesOnlyUniqueFramesAndKeepsIndependentPixelsAfterOwnerDisposal()
    {
        var fixture=new AreaOwnerFixture();
        try
        {
            var snapshot=fixture.Owner.LoadStrict(); var plan=AreaScenePlan.Create(snapshot); var before=fixture.Owner.Usage;
            var frames=AreaLoadOperation.CopyFrames(fixture.Owner,snapshot,plan,default,maxFrames:2,maxCpuBytes:94720);
            Assert.AreEqual(2,frames.Count); Assert.AreEqual(94720L,frames.Values.Sum(f=>(long)f.Rgba.Length));
            Assert.AreEqual(2L,fixture.Owner.Usage.FrameRequests-before.FrameRequests);
            Assert.AreEqual(94720L,fixture.Owner.Usage.ConsumerCopyBytes-before.ConsumerCopyBytes);
            Assert.AreEqual(before.RetainedReserved,fixture.Owner.Usage.RetainedReserved);
            var tile=frames.Values.Single(f=>f.Width==80); var body=frames.Values.Single(f=>f.Width==128);
            CollectionAssert.AreEqual(new byte[]{10,20,200,255},tile.Rgba[..4]);
            CollectionAssert.AreEqual(new byte[]{0,0,0,0},body.Rgba[..4]);
            CollectionAssert.AreEqual(new byte[]{231,37,19,255},body.Rgba.AsSpan((128+1)*4,4).ToArray());
            CollectionAssert.AreEqual(new byte[]{18,197,71,255},body.Rgba.AsSpan((128+64)*4,4).ToArray());
            fixture.Owner.Dispose();
            Assert.IsTrue(fixture.Streams.Values.All(s=>s.DisposeCount==1));
            CollectionAssert.AreEqual(new byte[]{10,20,200,255},tile.Rgba[..4]);
            Assert.ThrowsExactly<ObjectDisposedException>(()=>fixture.Owner.AcquireSelectedFrame(snapshot,snapshot.Visuals[0].Key));
            Assert.AreEqual(94720L,fixture.Owner.Usage.ConsumerCopyBytes-before.ConsumerCopyBytes);
        }
        finally { fixture.Dispose(); }
        Assert.IsTrue(fixture.Streams.Values.All(s=>s.DisposeCount==1));
    }

    [TestMethod]
    public void ExactRepositoryBoundsAdmitAndCapPlusOneRejectsBeforeAnyLeaseOrCopy()
    {
        foreach(var (count,bytes) in new[]{(1,94720L),(2,94719L),(0,94720L),(2,0L)})
        {
            using var fixture=new AreaOwnerFixture(); var snapshot=fixture.Owner.LoadStrict();
            var plan=AreaScenePlan.Create(snapshot); var before=fixture.Owner.Usage;
            var error=Assert.ThrowsExactly<AreaException>(()=>AreaLoadOperation.CopyFrames(fixture.Owner,snapshot,plan,default,count,bytes));
            Assert.AreEqual(AreaErrorCode.BudgetExceeded,error.Issue.Code); Assert.AreEqual(AreaStage.Frame,error.Issue.Stage);
            Assert.AreEqual(before,fixture.Owner.Usage);
            using var lease=fixture.Owner.AcquireSelectedFrame(snapshot,snapshot.Visuals[0].Key);
            Assert.IsTrue(lease.PixelByteCount>0);
        }
        using var invalid=new AreaOwnerFixture(); var s=invalid.Owner.LoadStrict(); var p=AreaScenePlan.Create(s);
        var usage=invalid.Owner.Usage;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>AreaLoadOperation.CopyFrames(invalid.Owner,s,p,default,2445));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>AreaLoadOperation.CopyFrames(invalid.Owner,s,p,default,maxCpuBytes:81853097));
        Assert.AreEqual(usage,invalid.Owner.Usage);
    }

    [TestMethod]
    public void CancellationAtAdmissionFirstCopyAndFinalCopyNeverReturnsPartialRepositoryOrLeavesLease()
    {
        foreach(var cancelAfter in new[]{0,1,2})
        {
            using var fixture=new AreaOwnerFixture(); var s=fixture.Owner.LoadStrict(); var p=AreaScenePlan.Create(s);
            var before=fixture.Owner.Usage; using var cancel=new CancellationTokenSource(); var copied=0;
            if(cancelAfter==0) cancel.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(()=>AreaLoadOperation.CopyFrames(fixture.Owner,s,p,cancel.Token,
                copied:(count,_)=>{copied=count; if(count==cancelAfter) cancel.Cancel();}));
            Assert.AreEqual(cancelAfter,copied);
            Assert.AreEqual(before.RetainedReserved,fixture.Owner.Usage.RetainedReserved);
            Assert.AreSame(s,fixture.Owner.PublishedSnapshot);
            using var lease=fixture.Owner.AcquireSelectedFrame(s,s.Visuals[0].Key);
            Assert.IsTrue(lease.PixelByteCount>0);
        }
    }

    [TestMethod]
    public void ResourceMutationAndReadFailureKeepPublicationAndReleaseTemporaryFrameOwnership()
    {
        foreach(var mutate in new[]{false,true})
        {
            using var fixture=new AreaOwnerFixture(); var s=fixture.Owner.LoadStrict(); var p=AreaScenePlan.Create(s);
            var stream=fixture.Streams[new(AreaSourceRole.Resource1)]; var before=fixture.Owner.Usage;
            if(mutate) stream.Bytes[0]^=1;
            else stream.BeforeRead=(_,_)=>throw new IOException("synthetic frame read fault");
            if(mutate)
                Assert.AreEqual(AreaErrorCode.InputIdentityMismatch,Assert.ThrowsExactly<AreaException>(()=>
                    AreaLoadOperation.CopyFrames(fixture.Owner,s,p,default)).Issue.Code);
            else Assert.ThrowsExactly<IOException>(()=>AreaLoadOperation.CopyFrames(fixture.Owner,s,p,default));
            Assert.AreSame(s,fixture.Owner.PublishedSnapshot);
            Assert.AreEqual(before.RetainedReserved,fixture.Owner.Usage.RetainedReserved);
            Assert.AreEqual(before.ConsumerCopyBytes,fixture.Owner.Usage.ConsumerCopyBytes);
            if(mutate) stream.Bytes[0]^=1; else stream.BeforeRead=null;
            using var lease=fixture.Owner.AcquireSelectedFrame(s,s.Visuals[0].Key);
            Assert.IsTrue(lease.PixelByteCount>0);
        }
    }

    [TestMethod]
    public void SourceMutationAfterTheLastFrameCopyIsCaughtByTheBatchCompletionAndYieldsNoRepository()
    {
        using var fixture=new AreaOwnerFixture(); var s=fixture.Owner.LoadStrict(); var p=AreaScenePlan.Create(s);
        var total=p.Commands.Select(c=>c.Frame).Distinct().Count();
        var stream=fixture.Streams[new(AreaSourceRole.Resource1)]; var before=fixture.Owner.Usage; IReadOnlyDictionary<AreaFrameKey,DecodedAreaFrame>? frames=null;
        var error=Assert.ThrowsExactly<AreaException>(()=>frames=AreaLoadOperation.CopyFrames(fixture.Owner,s,p,default,
            copied:(count,_)=>{ if(count==total) stream.Bytes[0]^=1; }));
        Assert.AreEqual(AreaErrorCode.InputIdentityMismatch,error.Issue.Code);
        Assert.IsNull(frames);
        Assert.AreSame(s,fixture.Owner.PublishedSnapshot);
        Assert.AreEqual(before.RetainedReserved,fixture.Owner.Usage.RetainedReserved);
        stream.Bytes[0]^=1;
        var repository=AreaLoadOperation.CopyFrames(fixture.Owner,s,p,default);
        Assert.AreEqual(total,repository.Count);
        using var lease=fixture.Owner.AcquireSelectedFrame(s,s.Visuals[0].Key);
        Assert.IsTrue(lease.PixelByteCount>0);
    }

    [TestMethod]
    public void MissingDescriptorAndForeignSnapshotCannotYieldApparentlyCompleteRepository()
    {
        using var fixture=new AreaOwnerFixture(); var s=fixture.Owner.LoadStrict();
        var visuals=s.Visuals.Select(v=>v with { RgbaSha256=new string('f',64) }).ToArray();
        var mismatched=AreaScenePlan.Create(s.Terrain,s.Objects,visuals); var before=fixture.Owner.Usage;
        Assert.AreEqual("AREA_FRAME_DESCRIPTOR_REQUIRED",Assert.ThrowsExactly<InvalidOperationException>(()=>
            AreaLoadOperation.CopyFrames(fixture.Owner,s,mismatched,default)).Message);
        Assert.AreEqual(before,fixture.Owner.Usage);
        using var other=new AreaOwnerFixture(); other.Owner.LoadStrict();
        var error=Assert.ThrowsExactly<AreaException>(()=>AreaLoadOperation.CopyFrames(other.Owner,s,AreaScenePlan.Create(s),default));
        Assert.AreEqual(AreaErrorCode.SnapshotMismatch,error.Issue.Code);
    }
}
