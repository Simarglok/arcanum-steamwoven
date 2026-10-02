using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ProductionWorldLoadTests
{
    [TestMethod]
    public void IntegratedProductionOperationDecodesAllDomainsAndCountsSharedOrdinalsSeparately()
    {
        using var fixture = new SyntheticWorldSourceFixture(); var points = new List<TerrainLoadCheckpoint>();
        using (var operation = fixture.Operation(new(Terrain:2, Statics:5, Actor:184, Total:191, Bytes:764), points.Add))
        {
            var result = operation.Execute(1, fixture.Arguments, default);
            Assert.AreEqual(4096,result.ResolvedCount); Assert.AreEqual(3033,result.TileCount); Assert.AreEqual(1063,result.FacadeCount);
            Assert.AreEqual(2,result.Textures.Count); Assert.AreEqual(5,result.StaticTextures.Count); Assert.AreEqual(184,result.ActorTextures.Count);
            Assert.AreEqual(764L,result.CheckedRgbaBytes); Assert.AreEqual(317,result.StaticCensusCount); Assert.AreEqual(312,result.StaticOutsideScopeCount);
            Assert.IsTrue(result.DiagnosticPlan!.IsWorldDiagnostic); Assert.AreEqual(5,result.DiagnosticPlan.Statics!.Commands.Count);
            Assert.IsTrue(result.Textures.Keys.Any(k => k.DatOrdinal==4753)); Assert.IsTrue(result.ActorTextures.Keys.Any(k => k.ResourceOrdinal==4753));
            Assert.AreEqual(result.ModuleFingerprint,result.ModuleFingerprintAfter); Assert.AreEqual(result.ResourceFingerprint,result.ResourceFingerprintAfter);
            Assert.AreEqual(result.ActorResourceFingerprint,result.ActorResourceFingerprintAfter); Assert.AreEqual(result.ActorPrototypeFingerprint,result.ActorPrototypeFingerprintAfter);
            Assert.AreEqual(new TerrainLoadCheckpoint("static",5,7,28),points.Single(p=>p.Domain=="static" && p.RetainedCount==5));
            Assert.AreEqual(new TerrainLoadCheckpoint("actor",1,8,32),points.First(p=>p.Domain=="actor"));
            Assert.AreEqual(new TerrainLoadCheckpoint("actor",184,191,764),points.Last(p=>p.Domain=="actor"));
        }
        fixture.AssertDisposed(3);
    }

    [TestMethod]
    public void IntegratedCountAndExactByteBudgetsRejectBeforeNextRetention()
    {
        // Independently counted: 2 terrain + 5 static + 184 actor, all 1x1 RGBA.
        foreach (var (budget, lastCount, lastBytes) in new[] {
            (new TerrainLoadBudget(Terrain:1),1,4L), (new TerrainLoadBudget(Statics:4),6,24L),
            (new TerrainLoadBudget(Actor:183),190,760L), (new TerrainLoadBudget(Total:190),190,760L),
            (new TerrainLoadBudget(Bytes:763),190,760L), (new TerrainLoadBudget(Bytes:28),7,28L),
            (new TerrainLoadBudget(Bytes:27),6,24L) })
        {
            using var fixture = new SyntheticWorldSourceFixture(); var points=new List<TerrainLoadCheckpoint>();
            using (var operation=fixture.Operation(budget,points.Add))
            {
                var error=Assert.Throws<TerrainViewerException>(()=>operation.Execute(1,fixture.Arguments,default));
                Assert.AreEqual(TerrainViewerErrorCode.ResourceLimit,error.Error.Code);
                Assert.AreEqual(lastCount,points.Last().TotalCount); Assert.AreEqual(lastBytes,points.Last().RgbaBytes);
                Assert.IsFalse(points.Any(p=>p.Domain=="complete"));
            }
            fixture.AssertDisposed(lastCount>=7?3:2);
        }
        using var extra=new SyntheticWorldSourceFixture();
        Assert.Throws<ArgumentOutOfRangeException>(()=>extra.Operation(new(Total:4286)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>extra.Operation(new(Bytes:268435457)));
    }

    [TestMethod]
    public void IntegratedCancellationAfterStaticsAndPartialActorHasNoResultAndDisposesBeforeNextLoad()
    {
        foreach (var (domain,count,archives) in new[] { ("static",5,2), ("actor",1,3), ("actor",121,3) })
        {
            using var fixture=new SyntheticWorldSourceFixture(); using var cancellation=new CancellationTokenSource();
            var points=new List<TerrainLoadCheckpoint>();
            using (var operation=fixture.Operation(checkpoint:p=>{points.Add(p); if(p.Domain==domain && p.RetainedCount==count) cancellation.Cancel();}))
                Assert.Throws<OperationCanceledException>(()=>operation.Execute(1,fixture.Arguments,cancellation.Token));
            Assert.AreEqual(domain,points.Last().Domain); Assert.AreEqual(count,points.Last().RetainedCount);
            Assert.IsFalse(points.Any(p=>p.Domain=="complete")); fixture.AssertDisposed(archives);
        }
    }

    [TestMethod]
    public void IntegratedFailureAndAllRoleFingerprintMutationsNeverReturnPartialWorld()
    {
        foreach(var actorFailure in new[]{false,true})
        {
            using var fixture=new SyntheticWorldSourceFixture(corruptActor:actorFailure,corruptStatic:!actorFailure);
            using(var operation=fixture.Operation())
            {
                var error=Assert.Throws<TerrainViewerException>(()=>operation.Execute(1,fixture.Arguments,default));
                Assert.AreEqual(actorFailure?TerrainViewerFailureStage.ActorDecode:TerrainViewerFailureStage.StaticResource,error.Error.Stage);
                if(actorFailure) Assert.AreEqual(317,error.Error.Progress!.StaticCensus);
            }
            fixture.AssertDisposed(actorFailure?3:2);
        }
        foreach(var role in new[]{"module","resource","actor","prototypes"})
        {
            using var fixture=new SyntheticWorldSourceFixture();
            using(var operation=fixture.Operation(checkpoint:p=>{
                if(p.Domain!="complete") return;
                if(role=="prototypes") File.WriteAllBytes(Path.Combine(fixture.PrototypesPath,"fixture.pro"),[7]);
                else using(var output=new FileStream(role=="module"?fixture.ModulePath:role=="resource"?fixture.ResourcePath:fixture.ActorPath,FileMode.Append)) output.WriteByte(7);
            }))
            {
                var error=Assert.Throws<TerrainViewerException>(()=>operation.Execute(1,fixture.Arguments,default));
                Assert.AreEqual(role is "actor" or "prototypes"?TerrainViewerErrorCode.ActorFingerprint:TerrainViewerErrorCode.FingerprintMismatch,error.Error.Code);
                Assert.AreEqual(317,error.Error.Progress!.StaticCensus);
            }
            fixture.AssertDisposed(3);
        }
        using var standalone=new SyntheticWorldSourceFixture();
        using var fixedOperation=new TerrainLoadCoordinator.ProductionTerrainLoadOperation();
        Assert.AreEqual(TerrainViewerErrorCode.FingerprintMismatch,
            Assert.Throws<TerrainViewerException>(()=>fixedOperation.Execute(1,standalone.Arguments,default)).Error.Code);
    }

    [TestMethod]
    public async Task IntegratedProductionStaleSuccessFailureAndExitUseDeterministicBarriers()
    {
        foreach(var human in new[]{false,true})
        foreach(var fail in new[]{false,true})
        {
            using var fixture=new SyntheticWorldSourceFixture(human:human); using var reached=new ManualResetEventSlim(); using var release=new ManualResetEventSlim();
            var factory=new Factory(fixture,p=>{if(p.Domain=="complete"){reached.Set(); if(!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                if(fail) throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorDecode));}});
            using var coordinator=new TerrainLoadCoordinator(factory);
            var old=coordinator.BeginGeneration(); var task=coordinator.LoadAsync(old,fixture.Arguments);
            Assert.IsTrue(reached.Wait(TimeSpan.FromSeconds(10))); var current=coordinator.BeginGeneration(); release.Set();
            if(fail) await Assert.ThrowsAsync<TerrainViewerException>(async()=>await task);
            else {var result=await task; Assert.IsTrue(result.DiagnosticPlan!.IsWorldDiagnostic);}
            fixture.AssertDisposed(3); var published=0;
            Assert.IsFalse(coordinator.TryPublish(old,()=>published++)); Assert.AreEqual(0,published);
            Assert.IsTrue(coordinator.TryPublish(current,()=>published++)); Assert.AreEqual(1,published);
            Assert.AreEqual(1,coordinator.MaximumObservedConcurrency);
        }
        foreach(var human in new[]{false,true})
        using(var fixture=new SyntheticWorldSourceFixture(human:human))
        using(var reached=new ManualResetEventSlim())
        using(var release=new ManualResetEventSlim())
        using(var exitStarted=new ManualResetEventSlim())
        {
            var coordinator=new TerrainLoadCoordinator(new Factory(fixture,p=>{if(p.Domain=="actor" && p.RetainedCount==10){reached.Set(); if(!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();}}));
            var generation=coordinator.BeginGeneration(); var load=coordinator.LoadAsync(generation,fixture.Arguments);
            Assert.IsTrue(reached.Wait(TimeSpan.FromSeconds(10)));
            var exit=Task.Run(()=>{exitStarted.Set(); coordinator.Dispose();}); Assert.IsTrue(exitStarted.Wait(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(SpinWait.SpinUntil(()=>!coordinator.IsCurrentGeneration(generation),TimeSpan.FromSeconds(10)));
            Assert.IsFalse(exit.IsCompleted); release.Set(); await exit;
            await Assert.ThrowsAsync<OperationCanceledException>(async()=>await load);
            fixture.AssertDisposed(3); Assert.IsFalse(coordinator.TryPublish(generation,()=>Assert.Fail("Exit publication")));
        }
    }
    [TestMethod]
    public async Task IntegratedCancelledLoadDisposesBeforeQueuedSuccess()
    {
        foreach(var human in new[]{false,true})
        foreach(var (domain,count,archives) in new[]{("static",5,2),("actor",human?20:121,3)})
        {
            using var fixture=new SyntheticWorldSourceFixture(human:human); using var reached=new ManualResetEventSlim();
            using var release=new ManualResetEventSlim(); using var cancel=new CancellationTokenSource();
            var blocked=false;
            using var coordinator=new TerrainLoadCoordinator(new Factory(fixture,p=>{
                if(blocked || p.Domain!=domain || p.RetainedCount!=count) return;
                blocked=true; reached.Set(); if(!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }));
            var first=coordinator.BeginGeneration(); var interrupted=coordinator.LoadAsync(first,fixture.Arguments,cancel.Token);
            Assert.IsTrue(reached.Wait(TimeSpan.FromSeconds(10)));
            var next=coordinator.BeginGeneration(); var queued=coordinator.LoadAsync(next,fixture.Arguments);
            Assert.IsFalse(queued.IsCompleted); cancel.Cancel(); release.Set();
            await Assert.ThrowsAsync<OperationCanceledException>(async()=>await interrupted);
            var result=await queued;
            fixture.AssertDisposed(archives+3); Assert.AreEqual(human?248L:764L,result.CheckedRgbaBytes);
            Assert.AreEqual(1,coordinator.MaximumObservedConcurrency);
            Assert.IsFalse(coordinator.TryPublish(first,()=>Assert.Fail("Cancelled publication")));
            Assert.IsTrue(coordinator.TryPublish(next,()=>{}));
        }
    }

    private sealed class Factory(SyntheticWorldSourceFixture fixture,Action<TerrainLoadCheckpoint> checkpoint):ITerrainLoadOperationFactory
    {
        public ITerrainLoadOperation Create()
        {
            Assert.IsTrue(fixture.Identity.Streams.All(stream=>stream.DisposeCount==1), "Previous WORLD load archives outlived serialized gate");
            return fixture.Operation(checkpoint:checkpoint);
        }
    }
}
