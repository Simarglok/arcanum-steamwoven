using System.Text.Json;
using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
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
        var viewports=new List<int[]>(); var renderedPhases=new HashSet<string>();
        void Frame(int numerator,int denominator=16)
        {
            WorldFrameForSelfTest(level,Array.Empty<RawInputFrame>(),.1,true,SimulationTime.At(numerator,denominator));
            var advance=_worldActor.Session!.LastAdvance;
            if(advance is null) return;
            events.AddRange(advance.Movement.Events.Where(e=>!events.Any(old=>old.Ordinal==e.Ordinal)));
            evidence.AddRange(advance.Evidence.Where(e=>!evidence.Any(old=>old.Key==e.Key)));
        }
        void KeyAt(int numerator,int denominator,Key key,bool pressed)
        {
            _worldTestTime=SimulationTime.At(numerator,denominator);
            _Input(new InputEventKey {Keycode=key,Pressed=pressed});
            level=none with {KeyboardActor=pressed?new Vec2(key==Key.A?-1:1,0):default,KeyboardActorHeld=pressed};
        }
        void Prepare()
        {
            if(WorldModal) ToggleWorldSettings(); GetViewport().GuiReleaseFocus();
            _worldAdapter.Clear(); _worldReducer.SetStartupDevices(Array.Empty<int>()); RetireWorldLifecycle();
            _worldActor=new(); _worldActor.Configure(snapshot,SimulationTime.At(0));
            _worldGridInput=new(); _worldTestTime=SimulationTime.At(0); _worldFocused=true; level=none;
            QuarantineWorld(); Frame(1,10); Frame(2,10); Frame(3,10);
            Require(_worldGate.IsOpen,"two real complete neutral observations"); events.Clear(); evidence.Clear();
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor,WorldViewport,2);
        }
        JsonElement Reference()
        {
            var state=_worldActor.State; var action=state.Action==ActorDiagnosticAction.Walk?1:0;
            // Independent literal cardinal table, not Profile.Facing/MapFacing.
            var group=state.Facing is 1 or 7?7:5;
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
        foreach(var size in new[]{new Vector2I(1280,800),new(1440,900),new(800,600)})
        {
            await SetSmokeWindowSizeAsync(size); viewports.Add([size.X,size.Y]);
            Require(GetViewport().GetVisibleRect().Size==new Vector2(size.X,size.Y),"actual viewport");
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
            blocker=new {sourceIndex=1,cell=new[]{5,1},anchor=new[]{-163,134},rectangle=new[]{-276,-128,204,267},visible="PASS",reason="ADMITTED_DESTINATION_SCENERY_BLOCKER"},cases
        }));
    }
}
