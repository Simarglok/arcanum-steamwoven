using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ScreenGridInputTests
{
    [TestMethod]
    public void LiteralScreenSectorsAndAllInversionsHaveProjectedSignsForKeyboardAndStick()
    {
        Vec2[] vectors=[new(0,-1),new(1,-1),new(1,0),new(1,1),new(0,1),new(-1,1),new(-1,0),new(-1,-1)];
        // Independent projection oracle: map -X projects NE; map -Y projects NW.
        // This deliberately exposes Planner1111's contradictory NE/NW rows.
        NeighborDirection[] directions=[NeighborDirection.XMinusYMinus,NeighborDirection.XMinus,NeighborDirection.XMinusYPlus,
            NeighborDirection.YPlus,NeighborDirection.XPlusYPlus,NeighborDirection.XPlus,NeighborDirection.XPlusYMinus,NeighborDirection.YMinus];
        int[] facings=[0,1,2,3,4,5,6,7];
        DiagnosticSectorPosition[] offsets=[new(-1,-1),new(-1,0),new(-1,1),new(0,1),new(1,1),new(1,0),new(1,-1),new(0,-1)];
        int[][] inverted=[new[]{0,1,2,3,4,5,6,7},new[]{0,7,6,5,4,3,2,1},new[]{4,3,2,1,0,7,6,5},new[]{4,5,6,7,0,1,2,3}];
        for(var sector=0;sector<8;sector++) for(var flags=0;flags<4;flags++) foreach(var controller in new[]{false,true})
        {
            var reducer=new InputStateReducer(); reducer.SetStartupDevices(controller?new[]{3}:Array.Empty<int>());
            var context=new InputContext(Delta:.1,ActorSensitivity:1,CameraSensitivity:1);
            var neutral=new RawInputFrame(controller?3:-1,default);
            reducer.Reduce(neutral,context); reducer.Reduce(neutral,context);
            var raw=controller?neutral with{LeftStick=vectors[sector]}:neutral with{KeyboardActor=vectors[sector],KeyboardActorHeld=true};
            var reduced=reducer.Reduce(raw,context);
            var wanted=inverted[flags][sector];
            Assert.AreEqual(directions[wanted],DiagnosticGridActor.Resolve(reduced.Movement,(flags&1)!=0,(flags&2)!=0));
            Assert.AreEqual(facings[wanted],DiagnosticGridActor.Octant(directions[wanted]));
            var projected=DiagnosticActorMotion.Project(offsets[wanted]);
            Assert.AreEqual(Math.Sign(vectors[wanted].X),Math.Sign(projected.X));Assert.AreEqual(Math.Sign(vectors[wanted].Y),Math.Sign(projected.Y));
            Assert.IsTrue(reduced.ActorActive);
        }
        for(var x=-1;x<=1;x+=2) for(var y=-1;y<=1;y+=2)
        {
            Assert.AreEqual(DiagnosticGridActor.Resolve(new(x,y)),DiagnosticGridActor.Resolve(new(x,y*.5)));
            Assert.AreEqual(DiagnosticGridActor.Resolve(new(x,0)),DiagnosticGridActor.Resolve(new(x,y*.499999)));
            Assert.AreEqual(DiagnosticGridActor.Resolve(new(x,y)),DiagnosticGridActor.Resolve(new(x*.5,y)));
            Assert.AreEqual(DiagnosticGridActor.Resolve(new(0,y)),DiagnosticGridActor.Resolve(new(x*.499999,y)));
        }
        Assert.IsNull(DiagnosticGridActor.Resolve(default));
        foreach(var invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {Assert.Throws<ArgumentException>(()=>DiagnosticGridActor.Resolve(new(invalid,0)));Assert.Throws<ArgumentException>(()=>DiagnosticGridActor.Resolve(new(0,invalid)));}
    }

    [TestMethod]
    public void V1MigrationPreservesAuthoredCameraAndExplicitActorFalseOverridesFallbackTrue()
    {
        var old="{\"version\":1,\"cameraDeadzone\":0.31,\"actorDeadzone\":0.12,\"cameraSensitivity\":1.7,\"actorSensitivity\":0.8,\"invertCameraX\":true,\"invertCameraY\":false}";
        var migrated=InputSettingsCodec.DeserializeOrDefault(old,DiagnosticWorldPolicy.DefaultInputSettings);
        Assert.AreEqual(new InputSettings(CameraDeadzone:.31,ActorDeadzone:.12,CameraSensitivity:1.7,ActorSensitivity:.8,InvertCameraX:true,InvertCameraY:false),migrated);
        foreach(var x in new[]{false,true}) foreach(var y in new[]{false,true})
        {
            var expected=migrated with{InvertActorX=x,InvertActorY=y};
            Assert.AreEqual(expected,InputSettingsCodec.DeserializeOrDefault(InputSettingsCodec.Serialize(expected),new(InvertActorX:true,InvertActorY:true)));
        }
        var fallback=DiagnosticWorldPolicy.DefaultInputSettings with{InvertActorX=true,InvertActorY=true};
        Assert.AreEqual(migrated with{InvertActorX=true,InvertActorY=true},InputSettingsCodec.DeserializeOrDefault(old,fallback));
        foreach(var json in new[]{"bad","null","{\"version\":2}"})Assert.AreEqual(fallback,InputSettingsCodec.DeserializeOrDefault(json,fallback));
        Assert.IsFalse(DiagnosticWorldPolicy.DefaultInputSettings.InvertActorX);Assert.IsFalse(DiagnosticWorldPolicy.DefaultInputSettings.InvertActorY);
    }

    private static InputBatch At(int n,int d,long sequence,NeighborDirection? direction)=>new(SimulationTime.At(n,d),
        [MovementInput.Set(sequence,direction is{} value?ActorIntent.Toward(value):ActorIntent.Neutral)]);
    private static ActorDiagnosticScenePlan Plan()
    {
        var frames=new List<ActorDiagnosticFrame>();
        foreach(var (selector,ordinal,count,fps) in new[]{(0x20000000u,2667,1,6),(0x20000040u,2668,10,17)})
        foreach(var group in new[]{0,4,5,6,7})for(var frame=0;frame<count;frame++)frames.Add(new(new(ordinal,selector,group,frame,0),10,9,3,7,29,-13,count,fps));
        return ActorDiagnosticScenePlan.CreateSynthetic(new(6,1,0,0,0,0,ActorDiagnosticRenderBranch.NormalUncached,100,false,ActorDiagnosticAction.Idle,5,0,10,9,3,7,29,-13),frames,ActorDiagnosticProfile.HumanFixed);
    }

    [TestMethod]
    [DataRow(NeighborDirection.XMinusYMinus,0,0,false,4,4,false)]
    [DataRow(NeighborDirection.XMinus,1,7,true,5,5,false)]
    [DataRow(NeighborDirection.XMinusYPlus,2,6,true,6,6,false)]
    [DataRow(NeighborDirection.YPlus,3,5,true,7,7,false)]
    [DataRow(NeighborDirection.XPlusYPlus,4,4,false,0,0,false)]
    [DataRow(NeighborDirection.XPlus,5,5,false,1,7,true)]
    [DataRow(NeighborDirection.XPlusYMinus,6,6,false,2,6,true)]
    [DataRow(NeighborDirection.YMinus,7,7,false,3,5,true)]
    public void AllEightEventDerivedFacingsSelectLiteralRowsAndEarlyReturn(NeighborDirection direction,int facing,int group,bool flip,int reverse,int reverseGroup,bool reverseFlip)
    {
        using var f=new GridWireFixture(policy:CardinalTraversalAdmissionPolicy.BoundedAdjacent);var actor=new DiagnosticGridActor();var plan=Plan();
        actor.Configure(f.Snapshot,SimulationTime.At(0));
        actor.Advance(SimulationTime.At(1),[At(0,1,0,NeighborDirection.XPlusYPlus),At(1,2,1,null)],p=>plan.Select(p));
        Assert.AreEqual(new CellCoord(7,2),actor.Session!.State.CommittedCell);
        var outbound=actor.Advance(SimulationTime.At(17,8),[At(2,1,2,direction)],p=>plan.Select(p));
        Assert.AreEqual(1,outbound.Evidence.Count);Assert.AreEqual(1,outbound.Movement.Events.Count(e=>e.Kind==MovementEventKind.StepStarted));
        var row=plan.Select(actor.Placement()).Current;Assert.AreEqual(facing,row.RequestedFacing);Assert.AreEqual(group,row.PhysicalGroup);Assert.AreEqual(flip,row.FlipX);Assert.AreEqual(2,row.Frame);
        // Fresh release35/16 is strictly after the previous complete target17/8,
        // before entry9/4. Return arrival is2 + 2*(35/16 - 2) =19/8.
        actor.Advance(SimulationTime.At(37,16),[At(35,16,3,null)],p=>plan.Select(p));
        row=plan.Select(actor.Placement()).Current;Assert.AreEqual(MovementPhase.Returning,actor.Session.State.Phase);
        Assert.AreEqual(reverse,row.RequestedFacing);Assert.AreEqual(reverseGroup,row.PhysicalGroup);Assert.AreEqual(reverseFlip,row.FlipX);Assert.AreEqual(ActorDiagnosticAction.Walk,row.Action);
        actor.Advance(SimulationTime.At(19,8),[],p=>plan.Select(p));Assert.AreEqual(new CellCoord(7,2),actor.Session.State.CommittedCell);Assert.AreEqual(ActorDiagnosticAction.Idle,actor.State.Action);
    }
}
