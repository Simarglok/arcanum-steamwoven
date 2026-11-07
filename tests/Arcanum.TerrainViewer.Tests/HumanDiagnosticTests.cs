using System.Buffers.Binary;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class HumanDiagnosticTests
{
    [TestMethod]
    public void ClosedProfilesHaveLiteralIndependentIdentityClockAndCoverage()
    {
        var human = ActorDiagnosticProfile.ForMode(TerrainViewerMode.WorldDiagnostic);
        Assert.AreSame(ActorDiagnosticProfile.HumanFixed, human);
        Assert.AreSame(ActorDiagnosticProfile.MonsterRegression, ActorDiagnosticProfile.ForMode(TerrainViewerMode.ActorDiagnostic));
        Assert.IsNull(human.ModuleOrdinal);
        Assert.AreEqual(new ActorDiagnosticResource(ActorDiagnosticAction.Idle, 0x20000000, 2667, 0, 8, 1, 6, 6), human.Idle);
        Assert.AreEqual(new ActorDiagnosticResource(ActorDiagnosticAction.Walk, 0x20000040, 2668, 0, 8, 10, 17, 17) { Stride = new(10, 17, 42, 22, 60) }, human.Walk);
        Assert.AreEqual(ActorDiagnosticClock.ArtMetadata, human.Clock);
        Assert.AreEqual(55, human.DistinctKeys); Assert.AreEqual(457676L, human.OriginalRgbaBytes);
        foreach (var action in new[] { ActorDiagnosticAction.Idle, ActorDiagnosticAction.Walk })
        for (var slot = 0; slot < 8; slot++)
        {
            var binding = human.Facing(action, slot);
            Assert.AreEqual(slot, binding.ProjectOctant); Assert.AreEqual(slot, binding.SourceFacing);
            Assert.AreEqual(new[] { 0, 7, 6, 5, 4, 5, 6, 7 }[slot], binding.PhysicalGroup);
            Assert.AreEqual(slot is >= 1 and <= 3, binding.FlipX);
        }
        Assert.ThrowsExactly<TerrainViewerException>(() => human.Facing(ActorDiagnosticAction.Walk, 8));
        Assert.ThrowsExactly<TerrainViewerException>(() => human.Resource((ActorDiagnosticAction)9));
    }

    [TestMethod]
    public void HumanWireFixtureUsesRealProductionDecodeDedupAccountingAndDisposal()
    {
        using var fixture = new SyntheticWorldSourceFixture(human: true);
        var points = new List<TerrainLoadCheckpoint>();
        using (var operation = fixture.Operation(new(Actor: 55, Total: 62, Bytes: 248), points.Add))
        {
            var result = operation.Execute(1, fixture.Arguments, default);
            Assert.AreEqual(55, result.ActorTextures.Count); Assert.AreEqual(248L, result.CheckedRgbaBytes);
            Assert.AreEqual(ActorDiagnosticProfileId.HumanFixed, result.ActorPlan!.Profile.Id);
            Assert.AreEqual(5, result.ActorTextures.Keys.Count(k => k.ResourceOrdinal == 2667 && k.Selector == 0x20000000));
            Assert.AreEqual(50, result.ActorTextures.Keys.Count(k => k.ResourceOrdinal == 2668 && k.Selector == 0x20000040));
            Assert.IsTrue(result.ActorTextures.Keys.All(k => k.Palette == 0 && new[] { 0,4,5,6,7 }.Contains(k.PhysicalGroup)));
            Assert.AreEqual(new TerrainLoadCheckpoint("actor",55,62,248), points.Last(p => p.Domain == "actor"));
        }
        fixture.AssertDisposed(3);
        // Tiny synthetic identity admission is explicitly simulated, not proof
        // of original ART identity. Production must reject this same archive.
        using var real = new TerrainLoadCoordinator.ProductionTerrainLoadOperation();
        Assert.AreEqual(TerrainViewerErrorCode.FingerprintMismatch,
            Assert.Throws<TerrainViewerException>(() => real.Execute(2, fixture.Arguments, default)).Error.Code);
    }

    [TestMethod]
    public void HumanOriginalCommitmentRejectsSameShapeAndSourceRejectsMalformedOrSubstitutedMetadata()
    {
        // Admitted synthetic archive identity alone must NOT disable the
        // original profile's independently serialized metadata commitment.
        using (var fixture = new SyntheticWorldSourceFixture(human:true,authenticateHumanMetadata:true))
        {
            using (var operation = fixture.Operation())
                Assert.AreEqual(TerrainViewerErrorCode.ActorMetadata, Assert.Throws<TerrainViewerException>(
                    () => operation.Execute(1,fixture.Arguments,default)).Error.Code);
            fixture.AssertDisposed(3);
        }
        foreach (var change in new Action<byte[]>[] {
            bytes => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),0),
            bytes => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),17),
            bytes => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32),2),
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes,1),
            bytes => { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12),0); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16),1); },
            bytes => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1156),0) })
        {
            using var fixture = new SyntheticWorldSourceFixture(human:true,humanIdleTransform:bytes => { change(bytes); return bytes; });
            using (var operation = fixture.Operation())
                Assert.IsTrue(Assert.Throws<TerrainViewerException>(() => operation.Execute(1,fixture.Arguments,default)).Error.Code
                    is TerrainViewerErrorCode.ActorMetadata or TerrainViewerErrorCode.ActorDecode);
            fixture.AssertDisposed(3);
        }
        using var moved = new SyntheticWorldSourceFixture(human:true,moveHumanIdle:true);
        using (var operation = moved.Operation())
            Assert.AreEqual(TerrainViewerErrorCode.ActorDecode, Assert.Throws<TerrainViewerException>(
                () => operation.Execute(1,moved.Arguments,default)).Error.Code);
        moved.AssertDisposed(3);
    }

    [TestMethod]
    public void HumanBudgetCancellationAndPostReadMutationDoNotPublishPartialResults()
    {
        foreach (var budget in new[] { new TerrainLoadBudget(Actor:54), new(Total:61), new(Bytes:247) })
        {
            using var fixture = new SyntheticWorldSourceFixture(human:true); var points = new List<TerrainLoadCheckpoint>();
            using (var operation = fixture.Operation(budget, points.Add))
                Assert.AreEqual(TerrainViewerErrorCode.ResourceLimit, Assert.Throws<TerrainViewerException>(() => operation.Execute(1,fixture.Arguments,default)).Error.Code);
            Assert.AreEqual(61, points.Last().TotalCount); Assert.AreEqual(244L, points.Last().RgbaBytes); fixture.AssertDisposed(3);
        }
        foreach (var count in new[] { 1,5,6,55 })
        {
            using var fixture = new SyntheticWorldSourceFixture(human:true); using var cancel = new CancellationTokenSource();
            using (var operation = fixture.Operation(checkpoint:p => { if(p.Domain == "actor" && p.RetainedCount == count) cancel.Cancel(); }))
                Assert.Throws<OperationCanceledException>(() => operation.Execute(1,fixture.Arguments,cancel.Token));
            fixture.AssertDisposed(3);
        }
        using var mutation = new SyntheticWorldSourceFixture(human:true);
        using (var operation = mutation.Operation(checkpoint:p => { if(p.Domain == "complete") using(var file = new FileStream(mutation.ActorPath,FileMode.Append)) file.WriteByte(1); }))
            Assert.AreEqual(TerrainViewerErrorCode.ActorFingerprint, Assert.Throws<TerrainViewerException>(() => operation.Execute(1,mutation.Arguments,default)).Error.Code);
        mutation.AssertDisposed(3);
    }

    [TestMethod]
    public void HumanSignedUnequalGeometryMirrorsHotspotButNeverAddsArtOffsets()
    {
        var profile = ActorDiagnosticProfile.HumanFixed;
        foreach (var (slot, group, flip) in new[] { (0,0,false), (1,7,true), (2,6,true), (3,5,true), (4,4,false), (5,5,false), (6,6,false), (7,7,false) })
        {
            // Independent anchor: camera(7,-11)+Project(-2,3)+instance(-13,17)+(0,20)=(194,46).
            // Signed hotspot -3, height hotspot 13. Mirror hotX=15-(-3)-1=17.
            var key = new ActorDiagnosticTextureKey(2668,0x20000040,group,9,0);
            var frame = new ActorDiagnosticFrame(key,15,21,-3,13,987,-456,10,17);
            var input = new ActorDiagnosticPlacementInput(-2,3,7,-11,-13,17,ActorDiagnosticRenderBranch.NormalUncached,100,false,ActorDiagnosticAction.Walk,slot,9,15,21,-3,13,987,-456);
            var command = ActorDiagnosticScenePlan.Place(input,frame,profile);
            Assert.AreEqual(194L, command.AnchorX); Assert.AreEqual(46L, command.AnchorY);
            Assert.AreEqual(flip ? 177 : 197, command.Destination.X); Assert.AreEqual(33,command.Destination.Y);
            Assert.AreEqual(15,command.Destination.Width); Assert.AreEqual(21,command.Destination.Height);
            Assert.AreEqual(flip,command.FlipX); Assert.AreEqual(slot,command.SourceFacing);
            Assert.AreEqual(flip ? -987 : 987, command.EffectiveOffsetX);

            foreach (var bad in new[] { frame with {TextureKey=key with {Palette=1}}, frame with {TextureKey=key with {ResourceOrdinal=4754}}, frame with {RawFramesPerSecond=10}, frame with {FramesPerAction=8} })
                Assert.ThrowsExactly<TerrainViewerException>(() => ActorDiagnosticScenePlan.Place(input,bad,profile));
            Assert.ThrowsExactly<TerrainViewerException>(() => ActorDiagnosticScenePlan.Place(input with {ScalePercent=200},frame,profile));
            Assert.AreEqual(TerrainViewerErrorCode.ActorFrame, Assert.ThrowsExactly<TerrainViewerException>(
                () => ActorDiagnosticScenePlan.Place(input with {Frame=0},frame,profile)).Error.Code);
            Assert.ThrowsExactly<TerrainViewerException>(() => ActorDiagnosticScenePlan.Place(input with {MapX=long.MaxValue},frame,profile));
        }
        var idle = new ActorDiagnosticFrame(new(2667,0x20000000,0,0,0),28,77,9,75,0,0,1,6);
        var baseline = new ActorDiagnosticPlacementInput(0,0,0,0,0,0,ActorDiagnosticRenderBranch.NormalUncached,100,false,ActorDiagnosticAction.Idle,0,0,28,77,9,75,0,0);
        Assert.AreEqual(new TerrainRect(-9,-55,28,77), ActorDiagnosticScenePlan.Place(baseline,idle,profile).Destination);
        var walk = new ActorDiagnosticFrame(new(2668,0x20000040,0,0,0),27,75,14,73,9,-2,10,17);
        Assert.AreEqual(new TerrainRect(-14,-53,27,75), ActorDiagnosticScenePlan.Place(baseline with {Action=ActorDiagnosticAction.Walk,Width=27,Height=75},walk,profile).Destination);
    }

    [TestMethod]
    public void HumanArtClockTransitionsFacingWrapSafetyAndFailedSelectionAreTransactional()
    {
        var actor = new DiagnosticActorController(ActorDiagnosticProfile.HumanFixed);
        actor.Update(default,100); Assert.AreEqual(0,actor.CurrentFrame);
        actor.Update(new(1,0),1d/34); Assert.AreEqual(0,actor.CurrentFrame);
        actor.Update(new(0,-1),1d/34); Assert.AreEqual(1,actor.CurrentFrame);
        actor.Update(new(-1,0),9d/17); Assert.AreEqual(0,actor.CurrentFrame);
        actor.Update(new(1,0),1d/34); var prior = actor.State;
        Assert.ThrowsExactly<InvalidOperationException>(() => actor.Update(new(0,1),1d/34,_ => throw new InvalidOperationException()));
        Assert.AreEqual(prior,actor.State); Assert.AreEqual(0,actor.CurrentFrame);
        actor.Update(new(0,1),1d/34); Assert.AreEqual(1,actor.CurrentFrame);
        actor.Update(default,0); Assert.AreEqual(0,actor.CurrentFrame); Assert.AreEqual(ActorDiagnosticAction.Idle,actor.State.Action);
        actor.Update(new(1,0),1d/17); Assert.AreEqual(1,actor.CurrentFrame);
        actor.SafetyStop(); Assert.AreEqual(0,actor.CurrentFrame); Assert.IsFalse(actor.IsPlaying);
        actor.Update(new(1,0),double.NaN); Assert.IsFalse(actor.IsPlaying);
        actor.Configure(ActorDiagnosticProfile.MonsterRegression); actor.Update(new(1,0),.1); Assert.AreEqual(1,actor.CurrentFrame);
    }
}
