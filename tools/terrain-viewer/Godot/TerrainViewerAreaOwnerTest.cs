using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaOwnerAsync(Node host)
    {
        using var fixture=new AreaOwnerFixture();
        var snapshot=fixture.Owner.LoadStrict(); var plan=AreaScenePlan.Create(snapshot);
        // Invoke the internal production stage without exposing a fixture
        // admission API to ordinary callers or substituting frame decoding.
        var copy=typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("CopyFrames",BindingFlags.NonPublic|BindingFlags.Static)!;
        var frames=(IReadOnlyDictionary<AreaFrameKey,DecodedAreaFrame>)copy.Invoke(null,
            [fixture.Owner,snapshot,plan,CancellationToken.None,2444,81853096L,null])!;
        if(plan.Commands.Count!=16389 || plan.Commands.Select(c=>c.Visual).Distinct().Count()!=16389
            || frames.Count!=2 || frames.Values.Sum(f=>(long)f.Rgba.Length)!=94720)
            throw new InvalidOperationException("AREA_OWNER_COVERAGE");
        fixture.Owner.Dispose();
        // Disposal intentionally releases retained metadata reservations; pans
        // must be compared with that settled state, not the pre-disposal hold.
        var usage=fixture.Owner.Usage;
        if(fixture.Streams.Values.Any(s=>s.DisposeCount!=1)) throw new InvalidOperationException("AREA_OWNER_TRANSFERRED_HANDLES");
        using var cache=new TerrainTextureCache();
        var actor=SyntheticActorFixture.Create(human:true);
        cache.UploadActor(actor.Result.ActorTextures);
        var viewport=new SubViewport { Size=new(480,400),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
        var canvas=new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var views=0; var samples=0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        void Pixel(Image image,float zoom,Vector2 origin,int x,int y,string winner)
        {
            var sx=(int)MathF.Floor(origin.X+x*zoom); var sy=(int)MathF.Floor(origin.Y+y*zoom);
            if(image.GetSize()!=new Vector2I(480,400) || sx<0 || sy<0 || sx>=480 || sy>=400)
                throw new InvalidOperationException("AREA_OWNER_ACTUAL_VIEWPORT");
            var rgba=winner switch { "tile"=>(10,20,200,255),"right"=>(18,197,71,255),_=>(231,37,19,255) };
            try { AssertPixel(image,sx,sy,(byte)rgba.Item1,(byte)rgba.Item2,(byte)rgba.Item3,(byte)rgba.Item4); }
            catch(Exception error) { throw new InvalidOperationException($"AREA_OWNER_PIXEL world={x}/{y} screen={sx}/{sy} winner={winner}",error); }
            samples++;
        }
        try
        {
            canvas.SetActorVisibilityForReadback(false);
            foreach(var zoom in new[]{.5f,1f,2f})
            foreach(var pan in new[]{Vector2.Zero,new Vector2(13,-11),new Vector2(-17,9)})
            {
                var origin=(zoom==.5f?new Vector2(59.75f,100.25f):new Vector2(60,100))+pan-new Vector2(0,2520*zoom);
                viewport.CanvasTransform=new Transform2D(0,new(zoom,zoom),0,origin);
                canvas.PublishArea(plan,frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache);
                canvas.SetStaticVisibilityForReadback(true); await Settle();
                if(canvas.AreaVisibleCommands.Select(c=>c.Visual).Distinct().Count()!=canvas.AreaVisibleCount
                    || canvas.AreaVisibleCount+canvas.AreaCulledCount!=16389 || cache.AreaCount!=2)
                    throw new InvalidOperationException("AREA_OWNER_VISIBLE_RECONCILIATION");
                using(var image=viewport.GetTexture().GetImage())
                {
                    // Frozen world points around independently calculated X/Y
                    // seams and corner; never computed from submitted rectangles.
                    Pixel(image,zoom,origin,-20,2520,"tile"); Pixel(image,zoom,origin,0,2545,"tile");
                    Pixel(image,zoom,origin,50,2528,"tile");
                    Pixel(image,zoom,origin,20,2540,"left");
                    Pixel(image,zoom,origin,80,2580,"left");
                    Pixel(image,zoom,origin,160,2580,"right");
                    Pixel(image,zoom,origin,190,2580,"right");
                }
                // Hide only bodies: the opaque terrain under each winning body
                // must actually be present, not a background/nonempty-view proxy.
                canvas.SetStaticVisibilityForReadback(false); await Settle();
                using(var image=viewport.GetTexture().GetImage())
                {
                    Pixel(image,zoom,origin,20,2540,"tile");
                    Pixel(image,zoom,origin,80,2580,"tile");
                    Pixel(image,zoom,origin,160,2580,"tile");
                    Pixel(image,zoom,origin,190,2580,"tile");
                }
                canvas.SetStaticVisibilityForReadback(true); await Settle();
                using(var image=viewport.GetTexture().GetImage()) Pixel(image,zoom,origin,80,2580,"left");
                views++;
            }
            // B's logical center anchor (9,2547) is left of this world view.
            // Its selected canopy still covers the literal sample (20,2540).
            viewport.CanvasTransform=new Transform2D(0,Vector2.One,0,new(-15,-2400));
            canvas.PublishArea(plan,frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache); await Settle();
            if(!canvas.AreaVisibleCommands.Any(c=>c.Identity?.Sector==86570436013L))
                throw new InvalidOperationException("AREA_OWNER_OFFSCREEN_ANCHOR_CULL");
            using(var image=viewport.GetTexture().GetImage()) Pixel(image,1,new(-15,-2400),20,2540,"left");
            views++;
            if(fixture.Owner.Usage!=usage || fixture.Streams.Values.Any(s=>s.DisposeCount!=1))
                throw new InvalidOperationException("AREA_OWNER_PAN_REDECODE");
            GD.Print($"ARC-RENDER-007 owner fixture PASS sectors=4 terrain=16384 bodies=5 commands=16389 frames=2 cpuBytes=94720 views={views} samples={samples} viewport=480x400 mainViewport={host.GetViewport().GetVisibleRect().Size}; disposed-provider pans no re-decode; original parity NOT_VERIFIED");
        }
        finally
        {
            canvas.ClearScene(); viewport.Free(); cache.Dispose();
            if(cache.CreatedCount!=cache.DisposedCount || cache.AreaCount!=0)
                throw new InvalidOperationException("AREA_OWNER_TEXTURE_OWNERSHIP");
        }
    }
}
