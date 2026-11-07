using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
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
        var copy=typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
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
        // Exact visible residency is asserted below: no LRU retention.
        using var cache=new TerrainTextureCache { AreaRetainFrames = 0 };
        var actor=SyntheticActorFixture.Create(human: true, area: true);
        cache.UploadActor(actor.Result.ActorTextures);
        var viewport=new SubViewport { Size=new(480,400),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
        var canvas=new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var views=0; var samples=0; var compared=0;
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
        // The whole readback with terrain chunks must equal the per-command readback.
        async Task SameAsPerCommand(string view,int level)
        {
            if(canvas.AreaChunkCount==0 || canvas.AreaChunkLevel!=level)
                throw new InvalidOperationException($"AREA_OWNER_CHUNK_LEVEL {view} chunks={canvas.AreaChunkCount} level={canvas.AreaChunkLevel}");
            byte[] chunkedPixels; using(var image=viewport.GetTexture().GetImage()) chunkedPixels=image.GetData();
            canvas.AreaTerrainChunksEnabled=false; await Settle();
            byte[] commandPixels; using(var image=viewport.GetTexture().GetImage()) commandPixels=image.GetData();
            canvas.AreaTerrainChunksEnabled=true; await Settle();
            if(chunkedPixels.Length!=commandPixels.Length) throw new InvalidOperationException($"AREA_OWNER_CHUNK_READBACK {view}");
            var opaque=0;
            for(var i=0;i<chunkedPixels.Length;i++)
            {
                if(chunkedPixels[i]!=commandPixels[i]) throw new InvalidOperationException($"AREA_OWNER_CHUNK_PIXEL {view} byte={i}");
                if(i%4==3 && chunkedPixels[i]==255) opaque++;
            }
            if(opaque<1000) throw new InvalidOperationException($"AREA_OWNER_CHUNK_EMPTY {view}");
            compared++;
        }
        try
        {
            canvas.SetActorVisibilityForReadback(false);
            // Every view twice: terrain as pre-composed chunks (production default) and
            // per command. Both must show the same frozen pixels.
            foreach(var chunked in new[]{true,false})
            foreach(var zoom in new[]{.5f,1f,2f})
            foreach(var pan in new[]{Vector2.Zero,new Vector2(13,-11),new Vector2(-17,9)})
            {
                var origin=(zoom==.5f?new Vector2(59.75f,100.25f):new Vector2(60,100))+pan-new Vector2(0,2520*zoom);
                viewport.CanvasTransform=new Transform2D(0,new(zoom,zoom),0,origin);
                canvas.AreaTerrainChunksEnabled=chunked;
                canvas.PublishArea(plan,frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache);
                canvas.SetStaticVisibilityForReadback(true); await Settle();
                if(canvas.AreaVisibleCommands.Select(c=>c.Visual).Distinct().Count()!=canvas.AreaVisibleCount
                    || canvas.AreaVisibleCount+canvas.AreaCulledCount!=16389 || (canvas.AreaChunkCount>0)!=chunked
                    || cache.AreaCount!=(chunked?1:2))
                    throw new InvalidOperationException("AREA_OWNER_VISIBLE_RECONCILIATION");
                if(chunked) await SameAsPerCommand($"zoom {zoom} pan {pan}",0);
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
            // Decimated chunk levels need an integer raster origin at zoom 2^-k.
            foreach(var (zoom,level) in new[]{(.5f,1),(.25f,2),(.125f,3)})
            foreach(var pan in new[]{Vector2.Zero,new Vector2(7,-5)})
            {
                viewport.CanvasTransform=new Transform2D(0,new(zoom,zoom),0,new Vector2(240,200)+pan-new Vector2(0,MathF.Round(2520*zoom)));
                canvas.AreaTerrainChunksEnabled=true;
                canvas.PublishArea(plan,frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache); await Settle();
                await SameAsPerCommand($"decimated zoom {zoom} pan {pan}",level);
                views++;
            }
            // ARC-RENDER-010: under ambient lighting a single-channel chunk is tinted like its
            // per-command terrain (identical readback), and a mixed-channel chunk is not drawn
            // as a chunk. Fixture terrain words have type bit 0 (indoor).
            var channels=AreaAmbientPolicy.Channels(snapshot,plan);
            if(plan.Commands.Where(c=>c.Identity is null).Any(c=>channels[c.Visual]!=AreaAmbientChannel.Indoor))
                throw new InvalidOperationException("AREA_OWNER_LIGHT_CHANNELS");
            var pair=new Arcanum.Data.Lighting.AmbientPair(new(200,180,120),new(90,140,230));
            foreach(var (zoom,level) in new[]{(1f,0),(.5f,1)})
            {
                var origin=new Vector2(60,100)-new Vector2(0,MathF.Round(2520*zoom));
                viewport.CanvasTransform=new Transform2D(0,new(zoom,zoom),0,origin);
                canvas.AreaTerrainChunksEnabled=true; canvas.SetStaticVisibilityForReadback(false);
                canvas.SetAreaAmbient(new(pair,channels));
                canvas.PublishArea(plan,frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache); await Settle();
                await SameAsPerCommand($"lit zoom {zoom}",level);
                if(zoom==1f)
                    using(var image=viewport.GetTexture().GetImage())
                    {
                        // Tile source (10,20,200) x indoor (90,140,230)/255, hand-computed: (3.5,11.0,180.4).
                        var p=image.GetPixel((int)MathF.Floor(origin.X-20),(int)MathF.Floor(origin.Y+2520));
                        var lit=((int)Math.Round(p.R*255),(int)Math.Round(p.G*255),(int)Math.Round(p.B*255));
                        if(Math.Abs(lit.Item1-4)>1 || Math.Abs(lit.Item2-11)>1 || Math.Abs(lit.Item3-180)>1)
                            throw new InvalidOperationException($"AREA_OWNER_LIT_TILE {lit}");
                        samples++;
                    }
                var mixed=new Dictionary<AreaVisualKey,AreaAmbientChannel>(channels);
                foreach(var command in plan.Commands.Where(c=>c.Identity is null).Where((_,i)=>i%2==1)) mixed[command.Visual]=AreaAmbientChannel.Outdoor;
                canvas.SetAreaAmbient(new(pair,mixed)); await Settle();
                if(canvas.AreaChunkCount!=0) throw new InvalidOperationException($"AREA_OWNER_MIXED_CHUNK_DRAWN zoom {zoom}");
                canvas.SetAreaAmbient(null); canvas.SetStaticVisibilityForReadback(true); await Settle();
                views++;
            }
            canvas.AreaTerrainChunksEnabled=true;
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
            GD.Print($"ARC-RENDER-007 owner fixture PASS sectors=4 terrain=16384 bodies=5 commands=16389 frames=2 cpuBytes=94720 views={views} samples={samples} chunkReadbacks={compared} viewport=480x400 mainViewport={host.GetViewport().GetVisibleRect().Size}; disposed-provider pans no re-decode; original parity NOT_VERIFIED");
        }
        finally
        {
            canvas.ClearScene(); viewport.Free(); cache.Dispose();
            if(cache.CreatedCount!=cache.DisposedCount || cache.AreaCount!=0)
                throw new InvalidOperationException("AREA_OWNER_TEXTURE_OWNERSHIP");
        }
    }
}
