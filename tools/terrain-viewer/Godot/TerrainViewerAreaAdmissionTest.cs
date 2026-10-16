using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaAdmissionAsync(Node host)
    {
        if (DisplayServer.GetName() == "headless") return;
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var synthetic = SyntheticActorFixture.Create(human: true, run: true);
        var input = synthetic.Arguments.ActorInput!;
        using var cache = new TerrainTextureCache(); cache.UploadActor(synthetic.Result.ActorTextures);
        // Exercise both actual main viewport and a subviewport with identical
        // public data. Nothing here changes original camera/draw semantics.
        var sub = new SubViewport { Size=new(400,300),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
        host.AddChild(sub);
        var evidence = new List<object>();
        var frontierCount = 0;
        var hud=host.GetNode<TerrainDiagnosticHud>("Hud");
        var camera=host.GetNode<Camera2D>("TerrainCamera");
        var savedHud=hud.Visible; var savedCamera=camera.Enabled;
        hud.Hide(); camera.Enabled=false;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            foreach (var viewport in new Viewport[] {sub,host.GetViewport()})
            {
                var savedTransform=viewport.CanvasTransform;
                var canvas=new TerrainCanvas(); viewport.AddChild(canvas);
                canvas.SetActorVisibilityForReadback(false);
                try
                {
                    foreach(var size in new[]{new Vector2I(31,44),new Vector2I(27,35),new Vector2I(10,23)})
                    foreach(var zoom in new[]{.5f,1f,2f})
                    foreach(var offset in new[]{new Vector2I(0,0),new Vector2I(1,3),new Vector2I(-41,-23)})
                    foreach(var reflected in new[]{false,true})
                    {
                        var key=new AreaObjectKey(digest,5051,AreaSourceKind.Mobile,null,1,0,digest,1);
                        AreaFieldValue F(int slot,long value)=>new(slot,AreaPresence.Value,AreaValueOrigin.Instance,value,[],null);
                        var fields=new[]{F(18,0),F(8,reflected?3:0),F(9,0xFFFFFF),F(10,255),F(11,100)};
                        var body=new AreaObjectRecord(key,119,9,1,digest,digest,digest,digest,AreaScope.Core,null,92928,82560,
                            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,fields,fields,true,true,true);
                        var visual=new AreaVisualRecord(new(AreaSourceKind.Mobile,new(digest,5051,100),1,digest,Object:key),0x60000000u,0,
                            new(new(AreaSourceRole.Resource2),size.X*100+size.Y,0,1,1,0,digest),digest,1,0,0,0,0,0,0,false,
                            size.X,size.Y,0,0,17,-21,offset.X,offset.Y,100,1,size.X*size.Y*4,digest,"synthetic");
                        var plan=AreaScenePlan.Create([], [body], [visual]);
                        // Transparent border and disconnected one-texel features.
                        // Adjacent opaque texels are deliberately different; no
                        // identical opaque 2x2 block can ratify the admission rule.
                        var rgba=new byte[size.X*size.Y*4];
                        for(var y=2;y<size.Y-2;y++)
                        for(var x=2;x<size.X-2;x++)
                        {
                            if((x+y)%5==0) continue;
                            var color=((x+y)%3) switch {0=>new byte[]{231,37,19,255},1=>new byte[]{17,199,43,255},_=>new byte[]{24,218,236,255}};
                            color.CopyTo(rgba,(y*size.X+x)*4);
                        }
                        var frames=new Dictionary<AreaFrameKey,DecodedAreaFrame>{[AreaFrameKey.From(visual)]=new(size.X,size.Y,rgba)};
                        var origin=new Vector2(100,100);
                        viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                        canvas.PublishArea(plan,frames,synthetic.Result.ActorPlan!,input,cache); await Settle();
                        using var image=viewport.GetTexture().GetImage();
                        var command=plan.Commands.Single();
                        if(command.Destination!=new TerrainRect(40+offset.X,20+offset.Y,size.X,size.Y))
                            throw new InvalidOperationException("AREA sparse raw placement oracle");
                        // Visibility is independently observed by an exact public
                        // palette census in the raw destination envelope, not by
                        // the observation helper or nonempty input texture bytes.
                        var visible=0;
                        var left=(int)Math.Floor(100+(40+offset.X)*zoom);
                        var top=(int)Math.Floor(100+(20+offset.Y)*zoom);
                        for(var sy=top;sy<top+(int)Math.Ceiling(size.Y*zoom);sy++)
                        for(var sx=left;sx<left+(int)Math.Ceiling(size.X*zoom);sx++)
                        {
                            var c=image.GetPixel(sx,sy);
                            if((Math.Abs(c.R*255-231)<=2 && Math.Abs(c.G*255-37)<=2 && Math.Abs(c.B*255-19)<=2)
                                || (Math.Abs(c.R*255-17)<=2 && Math.Abs(c.G*255-199)<=2 && Math.Abs(c.B*255-43)<=2)
                                || (Math.Abs(c.R*255-24)<=2 && Math.Abs(c.G*255-218)<=2 && Math.Abs(c.B*255-236)<=2)) visible++;
                        }
                        if(visible==0) throw new InvalidOperationException($"AREA sparse fixture not visibly rendered main={ReferenceEquals(viewport,host.GetViewport())} size={size} zoom={zoom} offset={offset} reflected={reflected} transform={viewport.CanvasTransform} visibleCommands={canvas.AreaVisibleCount}");
                        var admitted=TerrainViewerApp.TryAreaPixel(command,frames,canvas.AreaVisibleCommands,image,viewport,null,null,zoom,out _,out var diagnostic);
                        if(diagnostic.UniqueScreenCenters!=diagnostic.Admitted+diagnostic.Rejections.Values.Sum())
                            throw new InvalidOperationException("AREA admission first-rejection partition");
                        evidence.Add(new{MainViewport=ReferenceEquals(viewport,host.GetViewport()),size.X,size.Y,Zoom=zoom,Offset=offset.ToString(),reflected,VisiblePalettePixels=visible,Admitted=admitted,Diagnostic=diagnostic});
                        if(!admitted) frontierCount++;
                        if(size==new Vector2I(31,44) && zoom==.5f && offset==Vector2I.Zero && !reflected)
                        {
                            // A later upright body at the SAME cell/rectangle,
                            // ordered after target by independent occurrence tie.
                            // Full opaque cover denies; a single sampled hole
                            // admits exactly target texel3/3 at screen121/111.
                            var coverKey=key with { SourceIndex=2 };
                            var coverBody=body with { Key=coverKey };
                            var coverVisual=visual with { Key=visual.Key with { SourceIndex=2,Object=coverKey },
                                Entry=visual.Entry with { Ordinal=10000+visual.Entry.Ordinal } };
                            var coveredPlan=AreaScenePlan.Create([], [body,coverBody], [visual,coverVisual]);
                            var coverPixels=SolidRgba(size.X,size.Y,10,20,200);
                            var coveredFrames=new Dictionary<AreaFrameKey,DecodedAreaFrame>(frames) {
                                [AreaFrameKey.From(coverVisual)]=new(size.X,size.Y,coverPixels) };
                            canvas.PublishArea(coveredPlan,coveredFrames,synthetic.Result.ActorPlan!,input,cache);await Settle();
                            using(var opaque=viewport.GetTexture().GetImage())
                            {
                                AssertPixel(opaque,121,111,10,20,200,255);
                                if(TerrainViewerApp.TryAreaPixel(command,coveredFrames,canvas.AreaVisibleCommands,opaque,viewport,null,null,zoom,out _,out var denied)
                                    || denied.Rejections.GetValueOrDefault("later-alpha")==0)
                                    throw new InvalidOperationException("AREA_NEAREST_OPAQUE_OCCLUDER_ADMITTED");
                            }
                            var holePixels=(byte[])coverPixels.Clone();holePixels[(3*size.X+3)*4+3]=0;
                            var holeFrames=new Dictionary<AreaFrameKey,DecodedAreaFrame>(coveredFrames) {
                                [AreaFrameKey.From(coverVisual)]=new(size.X,size.Y,holePixels) };
                            // Force a genuine resource re-publication, not a
                            // stale same-key cache pretending bytes changed.
                            cache.Clear();cache.UploadActor(synthetic.Result.ActorTextures);
                            canvas.PublishArea(coveredPlan,holeFrames,synthetic.Result.ActorPlan!,input,cache);await Settle();
                            using var hole=viewport.GetTexture().GetImage();
                            AssertPixel(hole,121,111,231,37,19,255);
                            if(!TerrainViewerApp.TryAreaPixel(command,holeFrames,canvas.AreaVisibleCommands,hole,viewport,null,null,zoom,out var through,out _))
                                throw new InvalidOperationException("AREA_NEAREST_TRANSPARENT_HOLE_REJECTED");
                            using var value=System.Text.Json.JsonSerializer.SerializeToDocument(through);
                            if(value.RootElement.GetProperty("SourceX").GetInt32()!=3 || value.RootElement.GetProperty("SourceY").GetInt32()!=3
                                || value.RootElement.GetProperty("ScreenX").GetInt32()!=121 || value.RootElement.GetProperty("ScreenY").GetInt32()!=111)
                                throw new InvalidOperationException("AREA_NEAREST_HOLE_LITERAL_INDEX");
                        }
                    }
                }
                finally {viewport.CanvasTransform=savedTransform;canvas.QueueFree();await Settle();}
            }
            var path=System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
            if(!string.IsNullOrWhiteSpace(path)) System.IO.File.WriteAllText(path+".sparse-diagnostics.json",
                System.Text.Json.JsonSerializer.Serialize(new{Schema="arcanum-area-sparse-diagnosis-v1",Outcome=frontierCount==0?"PASS":"FAIL",RejectedVisibleCases=frontierCount,Cases=evidence}));
            if(frontierCount!=0) throw new InvalidOperationException($"AREA_SPARSE_VISIBLE_BUT_REJECTED cases={frontierCount}");
            GD.Print("ARC-RENDER-007 sparse production-admission PASS");
        }
        finally {sub.QueueFree();hud.Visible=savedHud;camera.Enabled=savedCamera;}
    }
}
