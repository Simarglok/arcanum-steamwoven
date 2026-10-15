using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaOwnerCoordinatorTests
{
    private static TerrainViewerArguments Arguments => TerrainViewerArguments.Parse([
        "--mode","crash-site-area","--module","synthetic-module","--resource","synthetic-resource",
        "--actor-resource","synthetic-actor","--area-mobile","synthetic-mobile","--area-effects","synthetic-effects","--area-patch","synthetic-patch","--prototypes","synthetic-prototypes",
        "--source-id","public-fixture"]);

    [TestMethod]
    public async Task StrictAreaPhaseSerializesFreshOwnersAndRejectsStaleGenerationPublication()
    {
        var first=new OwnerOperation(pauseAfterRead:true); var second=new OwnerOperation();
        var factory=new OwnerFactory(first,second);
        using var coordinator=new TerrainLoadCoordinator(factory);
        var old=coordinator.BeginGeneration(); var oldLoad=coordinator.LoadAsync(old,Arguments);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var current=coordinator.BeginGeneration(); var newLoad=coordinator.LoadAsync(current,Arguments);
        Assert.AreEqual(1,factory.Created); Assert.IsFalse(newLoad.IsCompleted);
        first.Release.TrySetResult(); var stale=await oldLoad; var replacement=await newLoad;
        Assert.AreEqual(old,stale.Area!.Generation); Assert.AreEqual(current,replacement.Area!.Generation);
        Assert.AreNotSame(stale.Area.Snapshot,replacement.Area.Snapshot);
        Assert.IsFalse(coordinator.TryPublish(old,()=>Assert.Fail("stale AREA was published")));
        AreaLoadResult? published=null;
        Assert.IsTrue(coordinator.TryPublish(current,()=>published=replacement.Area));
        Assert.AreSame(replacement.Area,published); Assert.AreEqual(1,coordinator.MaximumObservedConcurrency);
        Assert.AreEqual(2,factory.Created);
        foreach(var operation in new[]{first,second}) AssertComplete(operation);
        Assert.AreEqual(16389,replacement.Area.Plan.Commands.Count);
        Assert.AreEqual(16389,replacement.Area.Plan.Commands.Select(c=>c.Visual).Distinct().Count());
        Assert.AreEqual(2,replacement.Area.Frames.Count); Assert.AreEqual(94720L,replacement.Area.CpuRgbaBytes);
        Assert.AreEqual(2L,replacement.Area.FinalUsage.FrameRequests-replacement.Area.InitialUsage.FrameRequests);
        Assert.AreEqual(94720L,replacement.Area.FinalUsage.ConsumerCopyBytes-replacement.Area.InitialUsage.ConsumerCopyBytes);
        CollectionAssert.AreEqual(new byte[]{10,20,200,255},replacement.Area.Frames.Values.Single(f=>f.Width==80).Rgba[..4]);
    }

    [TestMethod]
    public async Task CancelledStrictReadDisposesOwnerBeforeNextGenerationAndPublishesNoPartialResult()
    {
        var cancelled=new OwnerOperation(pauseBeforeRead:true); var current=new OwnerOperation();
        var factory=new OwnerFactory(cancelled,current); using var coordinator=new TerrainLoadCoordinator(factory);
        using var cancellation=new CancellationTokenSource();
        var pending=coordinator.LoadAsync(coordinator.BeginGeneration(),Arguments,cancellation.Token);
        await cancelled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async()=>await pending);
        Assert.IsNull(cancelled.Result); AssertComplete(cancelled);
        var result=await coordinator.LoadAsync(coordinator.BeginGeneration(),Arguments);
        Assert.AreEqual(16389,result.Area!.Plan.Commands.Count); AssertComplete(current);
        Assert.AreEqual(1,coordinator.MaximumObservedConcurrency);
    }

    [TestMethod]
    public void StrictWireMirrorsFourCornerCellsButKeepsFullOccurrenceIdentityAndCrop()
    {
        using var fixture=new AreaOwnerFixture(seamMarkers:true);
        var result=AreaLoadOperation.ReadArea(17,fixture.Owner,default);
        Assert.AreEqual(17L,result.Generation); Assert.AreEqual(16389,result.Plan.Commands.Count);
        var mirrors=result.Plan.Commands.Where(c=>c.Identity is null&&c.FlipX).ToArray();
        Assert.AreEqual(4,mirrors.Length);
        CollectionAssert.AreEquivalent(new[]{4095,4032,63,0},mirrors.Select(c=>c.Visual.SourceIndex).ToArray());
        foreach(var command in mirrors) Assert.AreEqual(new TerrainRect(0,0,78,40),command.Source);
        var tile=result.Frames.Values.Single(f=>f.Width==80);
        CollectionAssert.AreEqual(new byte[]{10,20,200,255},tile.Rgba[..4]);
        CollectionAssert.AreEqual(new byte[]{240,100,30,255},tile.Rgba.AsSpan(39*4,4).ToArray());
        CollectionAssert.AreEqual(new byte[]{255,0,255,255},tile.Rgba.AsSpan(78*4,4).ToArray());
        Assert.AreEqual(2,result.Frames.Count); Assert.AreEqual(94720L,result.CpuRgbaBytes);
    }

    private static void AssertComplete(OwnerOperation operation)
    {
        Assert.AreEqual(1,operation.DisposeCount);
        Assert.IsNotNull(operation.Fixture);
        Assert.IsTrue(operation.Fixture.Streams.Values.All(s=>s.DisposeCount==1));
    }

    private sealed class OwnerFactory(params OwnerOperation[] operations) : ITerrainLoadOperationFactory
    {
        private readonly Queue<OwnerOperation> _operations=new(operations);
        private OwnerOperation? _prior;
        internal int Created { get; private set; }
        public ITerrainLoadOperation Create()
        {
            if(_prior is not null) AssertComplete(_prior);
            Created++; return _prior=_operations.Dequeue();
        }
    }

    // Only the legacy result envelope is a harness transport. The area payload
    // runs the SAME strict snapshot/plan/repository phase called by Execute.
    // This does not pretend to exercise original actor/traversal authentication.
    private sealed class OwnerOperation(bool pauseAfterRead=false,bool pauseBeforeRead=false) : ITerrainLoadOperation
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal AreaOwnerFixture? Fixture;
        internal AreaLoadResult? Result;
        internal int DisposeCount;
        public TerrainLoadResult Execute(long generation,TerrainViewerArguments arguments,CancellationToken token)
        {
            Fixture=new AreaOwnerFixture();
            if(pauseBeforeRead)
            {
                Fixture.Streams[new(AreaSourceRole.Module)].BeforeRead=(_,_)=>
                { Entered.TrySetResult(); Release.Task.Wait(token); };
            }
            Result=AreaLoadOperation.ReadArea(generation,Fixture.Owner,token);
            if(pauseAfterRead) { Entered.TrySetResult(); Release.Task.Wait(token); }
            var key=new TerrainTextureKey(new string('a',64),0,1,1,new string('b',64),0,0,0);
            var plan=TerrainScenePlan.CreateSynthetic(new(0,0,78,40),[new(0,key,TerrainKind.Tile,0,0,0,80,40,
                new(TerrainDiagnosticMode.EditorDiagnostic,0,0,0))]);
            var identity=new InputFingerprint(0,new string('c',64));
            return new(generation,plan,new Dictionary<TerrainTextureKey,DecodedTerrainTexture>(),16384,16384,0,0,0,
                identity,identity,identity,identity,Result.CpuRgbaBytes,Result.ElapsedMilliseconds,0) { Area=Result };
        }
        public void Dispose() { DisposeCount++; Fixture?.Dispose(); }
    }
}
