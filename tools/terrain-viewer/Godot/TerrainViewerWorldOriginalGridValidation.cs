using System.Text.Json;
using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;
using Environment = System.Environment;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private async Task RunGridOriginalReadbackAsync(TerrainLoadResult result)
    {
        if (Environment.GetEnvironmentVariable("WORLD003_GRID_VALIDATE") != "1") return;
        if (!_nativeSmoke || DisplayServer.GetName()=="headless"
            || result.TraversalSnapshot?.InputIdentity.Kind!=CardinalTraversalInputKind.AuthenticatedOriginal
            || result.ActorPlan?.Profile.Id!=ActorDiagnosticProfileId.HumanFixed) throw HumanFailure();
        await WithWorldReadbackScopeAsync(result, () => RunOwnedGridOriginalReadbackAsync(result));
    }
    private async Task RunOwnedGridOriginalReadbackAsync(TerrainLoadResult result)
    {
        static void Require(bool value,string why) { if(!value) throw new InvalidOperationException("GRID original readback: "+why); }
        using var oracle=JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("RENDER006_HUMAN_ORACLE")!));
        var root=oracle.RootElement;
        Require(root.GetProperty("schema").GetString()=="arcanum-human-private-oracle-v1"
            && root.GetProperty("actorSha256").GetString()==result.ActorResourceFingerprint!.Sha256
            && root.GetProperty("actorBytes").GetInt64()==result.ActorResourceFingerprint.Size,"independent original human identity");
        var rows=root.GetProperty("rows").EnumerateArray().ToArray();
        var snapshot=result.TraversalSnapshot!; var none=new RawInputFrame(-1,default); var level=none;
        var events=new List<MovementEvent>(); var evidence=new List<GridStepEvidence>(); var cases=new List<object>();
        var screenCases=new List<object>(); var keyboardInert=new List<object>();
        var savedSettings=_worldSettings;
        var viewports=new List<int[]>(); var renderedPhases=new HashSet<string>();
        void Frame(int numerator,int denominator=16)
        {
            // ARC-INPUT-001: the actor moves by the selected pad's stick; `level` is
            // the keyboard frame (arrows only in WORLD).
            WorldFrameForSelfTest(level,new[]{SimulatedPadLevel()},.1,true,SimulationTime.At(numerator,denominator));
            var advance=_worldActor.Session!.LastAdvance;
            if(advance is null) return;
            events.AddRange(advance.Movement.Events.Where(e=>!events.Any(old=>old.Ordinal==e.Ordinal)));
            evidence.AddRange(advance.Evidence.Where(e=>!evidence.Any(old=>old.Key==e.Key)));
        }
        void KeyAt(int numerator,int denominator,Key key,bool pressed)
        {
            _worldTestTime=SimulationTime.At(numerator,denominator);
            // Preserve the historical literal map-X transcripts with corrected
            // screen chords: A-proxy is NE/W+D, D-proxy is SW/S+A.
            // The stick replays those chords edge for edge (W/S -> LeftY, A/D -> LeftX).
            foreach(var physical in key==Key.A?new[]{Key.W,Key.D}:new[]{Key.S,Key.A})
                SimulatedSectorKey(physical,pressed);
        }
        void Prepare()
        {
            if(WorldModal) ToggleWorldSettings(); GetViewport().GuiReleaseFocus();
            _worldSettings=DiagnosticWorldPolicy.DefaultInputSettings;
            ClearSimulatedSectorKeys(); _worldAdapter.Clear(); _worldReducer.SetStartupDevices(new[]{SimulatedPadId}); RetireWorldLifecycle();
            _worldActor=new(); _worldActor.Configure(snapshot,SimulationTime.At(0));
            _worldGridInput=new(); _worldTestTime=SimulationTime.At(0); _worldFocused=true; level=none;
            QuarantineWorld(); Frame(1,10); Frame(2,10); Frame(3,10);
            Require(_worldGate.IsOpen,"two real complete neutral observations"); events.Clear(); evidence.Clear();
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor,WorldViewport,2);
        }
        JsonElement Reference()
        {
            var state=_worldActor.State; var action=state.Action==ActorDiagnosticAction.Walk?1:0;
            // Independent literal eight-facing table, not Profile.Facing/MapFacing.
            var group=new[]{0,7,6,5,4,5,6,7}[state.Facing];
            return rows.Single(r=>r.GetProperty("action").GetInt32()==action
                && r.GetProperty("group").GetInt32()==group && r.GetProperty("frame").GetInt32()==_worldActor.CurrentFrame);
        }
        async Task Render(string phase,bool negative=false)
        {
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor,WorldViewport,2); await NextFrameAsync();
            var reference=Reference(); using(var image=GetViewport().GetTexture().GetImage())
                Require(MatchesHumanReference(image,reference,_worldActor.State.Facing),"actual original "+phase+" actor pixels");
            renderedPhases.Add(phase);
            if(negative)
            {
                _canvas!.SetActorVisibilityForReadback(false); await NextFrameAsync();
                using(var hidden=GetViewport().GetTexture().GetImage())
                    Require(!MatchesHumanReference(hidden,reference,_worldActor.State.Facing),"hidden actor negative control");
                _canvas.SetActorVisibilityForReadback(true); await NextFrameAsync();
            }
        }
        async Task Blocker()
        {
            var command=result.StaticPlan!.Commands.Single(c=>c.SourceIndex==1);
            Require((command.AnchorX,command.AnchorY)==(-163,134)
                && (command.Destination.X,command.Destination.Y,command.Destination.Width,command.Destination.Height)==(-276,-128,204,267),"literal admitted blocker geometry");
            await NextFrameAsync(); using var image=GetViewport().GetTexture().GetImage();
            Require(_canvas!.TryGetOpaqueStaticLandmark(candidate=>candidate.Ordinal==1
                && !_hud!.Rect.HasPoint(_canvas.GetGlobalTransformWithCanvas()*new Vector2(candidate.WorldX,candidate.WorldY))
                && TryAssertLandmark(image,candidate),out var landmark),"index1 actual decoded opaque landmark");
            Require(await HasStaticDifferenceInFullSceneAsync(image,landmark),"visible blocker hidden-static negative control");
            renderedPhases.Add("Blocked");
        }
        async Task ScreenMatrix(Vector2I size)
        {
            Vec2[] vectors=[new(0,-1),new(1,-1),new(1,0),new(1,1),new(0,1),new(-1,1),new(-1,0),new(-1,-1)];
            Key[][] keys=[[Key.W],[Key.W,Key.D],[Key.D],[Key.S,Key.D],[Key.S],[Key.S,Key.A],[Key.A],[Key.W,Key.A]];
            // Literal projected-sign oracle (manager F-PLAN-NE-NW). These are
            // independent map displacements, not Resolve/Octant output.
            int[] mx=[-1,-1,-1,0,1,1,1,0], my=[-1,0,1,1,1,0,-1,-1];
            int[][] inversions=[[0,1,2,3,4,5,6,7],[0,7,6,5,4,3,2,1],[4,3,2,1,0,7,6,5],[4,5,6,7,0,1,2,3]];
            void Interior()
            {
                Prepare();
                // Reach the interior center through the real stick callback/queue,
                // never by teleporting/configuring another diagnostic spawn.
                _worldTestTime=SimulationTime.At(1); SimulatedSectorKey(Key.S,true); Frame(17); Frame(18);
                _worldTestTime=SimulationTime.At(5,4); SimulatedSectorKey(Key.S,false);
                Frame(25); Frame(26);
                Require(_worldActor.Session!.State.CommittedCell==new CellCoord(7,2)&&_worldActor.Session.State.Phase==MovementPhase.Settled,"interior original S admission");
                events.Clear(); evidence.Clear();
            }
            // ARC-INPUT-001 keyboard route: the former W/A/S/D sector chords create
            // no step from the interior center, through raw events and held levels.
            for(var sector=0;sector<8;sector++)
            {
                Interior(); _worldTestTime=SimulationTime.At(2);
                foreach(var key in keys[sector]) _Input(new InputEventKey{Keycode=key,Pressed=true});
                level=_worldAdapter.SampleKeyboard(true); Frame(33); Frame(34);
                foreach(var key in keys[sector]) _Input(new InputEventKey{Keycode=key,Pressed=false});
                level=none; Frame(41); Frame(42);
                Require(events.Count==0&&_worldActor.Session!.State.CommittedCell==new CellCoord(7,2)&&_worldActor.Session.State.Phase==MovementPhase.Settled,"keyboard sector chord moved the actor");
                keyboardInert.Add(new{viewport=new[]{size.X,size.Y},sector,events=events.Count,cell=new[]{7,2},phase="Settled"});
            }
            for(var flags=0;flags<4;flags++) for(var sector=0;sector<8;sector++)
            {
                Interior();
                _worldSettings=_worldSettings with{InvertActorX=(flags&1)!=0,InvertActorY=(flags&2)!=0};
                _worldTestTime=SimulationTime.At(2);
                _Input(new InputEventJoypadMotion{Device=3,Axis=JoyAxis.LeftX,AxisValue=(float)vectors[sector].X});
                _Input(new InputEventJoypadMotion{Device=3,Axis=JoyAxis.LeftY,AxisValue=(float)vectors[sector].Y});
                Frame(33); Frame(34);
                var wanted=inversions[flags][sector]; var started=events.Single(e=>e.Kind==MovementEventKind.StepStarted);
                Require(started.Key.From==new CellCoord(7,2)&&started.Key.To==new CellCoord(7+mx[wanted],2+my[wanted])
                    &&(int)started.Key.Direction==wanted&&_worldActor.State.Facing==wanted,"literal screen endpoint/facing");
                var delta=new[]{40*(my[wanted]-mx[wanted]),20*(my[wanted]+mx[wanted])};
                Require(Math.Sign(delta[0])==Math.Sign(vectors[wanted].X)&&Math.Sign(delta[1])==Math.Sign(vectors[wanted].Y),"independent projected signs all8 including NE/NW");
                await Render("Walk",sector==0&&flags==0);
                _worldTestTime=SimulationTime.At(9,4);
                _Input(new InputEventJoypadMotion{Device=3,Axis=JoyAxis.LeftX,AxisValue=0});
                _Input(new InputEventJoypadMotion{Device=3,Axis=JoyAxis.LeftY,AxisValue=0});
                Frame(41); Frame(42); await Render("Idle");
                var admitted=evidence.Single(); var assessment=admitted.Assessment;
                Require(_worldActor.Session!.State.Phase==MovementPhase.Settled&&_worldActor.Session.State.CommittedCell==started.Key.To
                    &&assessment.Coverage==CardinalTraversalCoverage.Supported&&assessment.Disposition==CardinalTraversalDisposition.Open
                    &&assessment.Gaps.Count==0&&assessment.Legs.Count==((wanted&1)==0?4:0),"actual all8 original admission closure");
                screenCases.Add(new{viewport=new[]{size.X,size.Y},device="stick",sector,flags,facing=wanted,projected=delta,
                    from=new[]{7,2},to=new[]{started.Key.To.X,started.Key.To.Y},direction=wanted,rendered="PASS",coverage="Supported",disposition="Open",gaps=assessment.Gaps.Count,
                    profile=assessment.StepEvidence.ProfileId,evidenceEpoch=snapshot.EvidenceEpoch,selectedEvidenceSha256=snapshot.InputIdentity.SelectedEvidenceSha256,
                    trace=string.Join("|",events.Select(e=>$"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}")),
                    legs=assessment.Legs.Select(l=>new{role=l.Role.ToString(),from=new[]{l.From.X,l.From.Y},to=new[]{l.To.X,l.To.Y},direction=(int)l.Direction,
                        originalDirection=(int)l.OriginalDirection,records=l.RecordAssessments.Count,gaps=l.Gaps.Count,blockers=l.RecordAssessments.Where(r=>r.Result.Disposition==CardinalTraversalDisposition.Blocked).Select(r=>r.SourceIndex).ToArray()}).ToArray()});
            }
        }
        foreach(var size in new[]{new Vector2I(1280,800),new(1440,900),new(800,600)})
        {
            await SetSmokeWindowSizeAsync(size); viewports.Add([size.X,size.Y]);
            Require(GetViewport().GetVisibleRect().Size==new Vector2(size.X,size.Y),"actual viewport");
            await ScreenMatrix(size);
            foreach(var name in new[]{"early","equality","late","open-reverse","blocker"})
            {
                Prepare(); await Render("Idle");
                if(name=="blocker")
                {
                    KeyAt(1,1,Key.A,true); Frame(17); Frame(18); await Render("Idle"); await Blocker();
                    Require(evidence.Single().Assessment.ReasonCode=="ADMITTED_DESTINATION_SCENERY_BLOCKER"
                        && _worldActor.Session!.State.CommittedCell==new CellCoord(6,1),"blocked admission leaves center");
                }
                else
                {
                    KeyAt(1,1,Key.D,true); Frame(17); Frame(18); await Render("Walk",true);
                    var release=name=="early"?9:name=="late"?11:12;

                    // Open/reverse neutral at arrival; equality at exact entry5/4.
                    if(name=="equality") release=10;
                    KeyAt(release,8,Key.D,false);
                    if(name=="early")
                    {
                        Frame(19); Frame(20);
                        Require(_worldActor.Session!.State.Phase==MovementPhase.Returning
                            && _worldActor.Session.State.Anchor.U==ExactScalar.Create(49,8),"literal early return19/16");
                        await Render("Returning"); Frame(21);
                    }
                    else
                    {
                        Frame(release*2+1);
                        if(name!="open-reverse")
                        {
                            Require(_worldActor.Session!.State.Phase==MovementPhase.OutboundAfterEntry,"neutral post-entry execution");
                            await Render("OutboundAfterEntry"); Frame(24); Frame(25);
                        }
                    }
                    await Render("Idle");
                    if(name=="open-reverse")
                    {
                        KeyAt(2,1,Key.A,true); Frame(33); await Render("Walk");
                        Frame(40); KeyAt(5,2,Key.A,false); Frame(41); await Render("Idle");
                    }
                }
                var state=_worldActor.Session!.State;
                Require(state.Phase==MovementPhase.Settled,"final settled center");
                cases.Add(new {name,viewport=new[]{size.X,size.Y},trace=string.Join("|",events.Select(e=>$"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}")),
                    cell=new[]{state.CommittedCell.X,state.CommittedCell.Y},phase=state.Phase.ToString(),rendered="PASS",
                    admissions=evidence.Select(e=>new {from=new[]{e.Key.From.X,e.Key.From.Y},to=new[]{e.Key.To.X,e.Key.To.Y},direction=(int)e.Key.Direction,
                        records=e.Assessment.RecordAssessments.Count,gaps=e.Assessment.Gaps.Count,selectedEvidenceSha256=e.Assessment.StepEvidence.SelectedEvidenceSha256,
                        evidenceEpoch=snapshot.EvidenceEpoch,coverage=e.Assessment.Coverage.ToString(),disposition=e.Assessment.Disposition?.ToString(),key=e.Key}).ToArray()});
            }
        }
        Require(renderedPhases.SetEquals(new[]{"Idle","Walk","Returning","OutboundAfterEntry","Blocked"}),"full rendered phase coverage");
        GD.Print("ARC-WORLD-003 grid-readback "+JsonSerializer.Serialize(new {
            schema="arcanum-grid-validation-v1",task="ARC-WORLD-003",candidateCommit=Environment.GetEnvironmentVariable("RENDER006_CANDIDATE"),
            host=Environment.GetEnvironmentVariable("RENDER006_HOST"),kind="grid-original",outcome="PASS",exitCode=0,
            originalIdentity="authenticated-original-grid-and-actor",profile="HumanFixed",assessmentProfile=snapshot.ProfileId,
            context=snapshot.DeclaredContext.ContextId,contextFingerprint=_worldActor.Session!.State.Binding.ContextId,
            duration="1/2",graphical=true,actualViewports=viewports,negativeControls="PASS",
            renderedPhases=new[]{"Idle","Walk","Returning","OutboundAfterEntry","Blocked"},originalParity="NOT_VERIFIED",
            selectedEvidenceSha256=snapshot.InputIdentity.SelectedEvidenceSha256,evidenceEpoch=snapshot.EvidenceEpoch,
            blocker=new {sourceIndex=1,cell=new[]{5,1},anchor=new[]{-163,134},rectangle=new[]{-276,-128,204,267},visible="PASS",reason="ADMITTED_DESTINATION_SCENERY_BLOCKER"},cases,screenCases,keyboardRoute="original-world",keyboardInert
        }));
        _worldSettings=savedSettings; ReleaseSimulatedPadForSelfTest();
    }
}
