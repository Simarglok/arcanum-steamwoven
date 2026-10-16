using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaCanvasAsync(Node host)
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var bodyKey = new AreaObjectKey(digest, 5051, AreaSourceKind.Static, 100, 0, 0, digest, 1);
        AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        var fields = new[] { Field(18,0), Field(8,0), Field(9,0xFFFFFF), Field(10,255), Field(11,100), Field(36,0) };
        var body = new AreaObjectRecord(bodyKey,119,3,1,digest,digest,digest,digest,AreaScope.Core,null,92928,82560,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,fields,fields,true,true,true);
        AreaVisualRecord Visual(AreaVisualKey key, int ordinal, int width, int height, int hx=0, int hy=0) => new(
            key, key.Kind == AreaSourceKind.Terrain ? 0u : 0x40000000u, 0,
            new(new(AreaSourceRole.Resource2),ordinal,0,1,1,0,digest),digest,1,0,0,0,0,0,0,false,
            width,height,hx,hy,17,-21,0,0,100,1,width*height*4,digest,"synthetic");
        var bv = Visual(new(AreaSourceKind.Static,new(digest,5051,100),0,digest,Object:bodyKey),1,160,160,40,80);
        var plan = AreaScenePlan.Create([], [body], [bv]);
        var frames = new Dictionary<AreaFrameKey,DecodedAreaFrame> {
            [AreaFrameKey.From(bv)] = new(160,160,SolidRgba(160,160,10,20,200)) };
        var synthetic = SyntheticActorFixture.Create(human:true);
        var input = synthetic.Arguments.ActorInput! with { MapX=0, MapY=0, CameraOriginX=0, CameraOriginY=0 };
        var actor = synthetic.Result.ActorPlan!.Select(input);
        using var cache = new TerrainTextureCache(); cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size=new(400,300),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless")
                await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            // Canopy OBJECT offsets are (0,0), ART offsets (17,-21) are
            // metadata sentinels, not placement/depth offsets. Behind actor
            // ranks (-40,-40) precede canopy (20,-20); front row1 wins by cell.
            foreach (var zoom in new[] { .5f, 1f, 2f })
            {
                // Half-zoom pixel centers must sample the fixture's one-pixel
                // feature rather than skip it; this translation is a synthetic
                // raster oracle, never a source-object placement correction.
                var origin = zoom == .5f ? new Vector2(99.75f,100.25f) : new Vector2(100,100);
                viewport.CanvasTransform = new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                input = input with { MapY=0, InstanceOffsetX=40, InstanceOffsetY=-40 };
                actor = actor.Select(input); canvas.PublishArea(plan,frames,actor,input,cache); await Settle();
                if (actor.Current.Destination != new TerrainRect(36,-23,9,7)
                    || plan.Commands.Single().Destination != new TerrainRect(0,-60,160,160))
                    throw new InvalidOperationException("AREA literal overlap rectangles");
                canvas.SetStaticVisibilityForReadback(false); await Settle();
                if (DisplayServer.GetName() != "headless")
                {
                    using var actorOnly=viewport.GetTexture().GetImage();
                    AssertPixel(actorOnly,(int)(origin.X+37*zoom),(int)(origin.Y-22*zoom),231,37,19,255);
                }
                canvas.SetStaticVisibilityForReadback(true); await Settle();
                if (DisplayServer.GetName() != "headless")
                {
                    using var behind=viewport.GetTexture().GetImage();
                    AssertPixel(behind,(int)(origin.X+37*zoom),(int)(origin.Y-22*zoom),10,20,200,255);
                }
                input = input with { MapY=1, InstanceOffsetX=0, InstanceOffsetY=0 };
                actor = actor.Select(input); canvas.PublishArea(plan,frames,actor,input,cache); await Settle();
                if (DisplayServer.GetName() != "headless")
                {
                    using var front=viewport.GetTexture().GetImage();
                    // An opaque interior actor pixel, not its guide or hotspot.
                    AssertPixel(front,(int)(origin.X+37*zoom),(int)(origin.Y+38*zoom),231,37,19,255);
                }
                if (canvas.AreaVisibleCount != 1 || canvas.AreaCulledCount != 0 || cache.AreaCount != 1)
                    throw new InvalidOperationException("AREA fixture occurrence/residency reconciliation");
            }
            var created=cache.CreatedCount;
            viewport.CanvasTransform=new Transform2D(0,new Vector2(2000,2000)); await Settle();
            if (cache.AreaCount != 0 || canvas.AreaVisibleCount != 0 || canvas.AreaCulledCount != 1)
                throw new InvalidOperationException("AREA offscreen eviction");
            viewport.CanvasTransform=new Transform2D(0,new Vector2(100,100)); await Settle();
            if (cache.AreaCount != 1 || cache.CreatedCount != created+1)
                throw new InvalidOperationException("AREA pan reupload/dedup");
            // Same camera and actor, different immutable generation: replacement
            // must invalidate culling and never keep the retired scene's command.
            var next = AreaScenePlan.Create([], [body with {WorldX=93000}], [bv]);
            canvas.PublishArea(next,frames,actor,input,cache); await Settle();
            if (canvas.AreaVisibleCount != 0 || cache.AreaCount != 0)
                throw new InvalidOperationException("AREA stale generation command");
            // Pool bound: after a large submission shrinks, at most max(256, 2 x visible)
            // draw items remain, and growing again reuses them.
            var bodies = Enumerable.Range(0,600).Select(i => body with { Key = bodyKey with { SourceIndex = i } }).ToArray();
            var visuals = bodies.Select(b => Visual(new(AreaSourceKind.Static,new(digest,5051,100),b.Key.SourceIndex,digest,Object:b.Key),1,160,160,40,80)).ToArray();
            var crowd = AreaScenePlan.Create([], bodies, visuals);
            viewport.CanvasTransform=new Transform2D(0,new Vector2(1,1),0,new Vector2(100,100));
            canvas.PublishArea(crowd,frames,actor,input,cache); await Settle();
            int Shown() => canvas.GetChildren().OfType<AreaDrawItem>().Count(item => item.Visible);
            var shownActor = Shown() - canvas.AreaVisibleCount;
            if (canvas.AreaVisibleCount != 600 || canvas.AreaPoolCount != 600 + shownActor || shownActor is < 0 or > 1)
                throw new InvalidOperationException("AREA crowd submission");
            viewport.CanvasTransform=new Transform2D(0,new Vector2(2000,2000)); await Settle();
            if (canvas.AreaVisibleCount != 0 || canvas.AreaPoolCount != 256 || Shown() != 0)
                throw new InvalidOperationException("AREA draw item pool bound");
            viewport.CanvasTransform=new Transform2D(0,new Vector2(1,1),0,new Vector2(100,100)); await Settle();
            if (canvas.AreaVisibleCount != 600 || canvas.AreaPoolCount != 600 + shownActor || Shown() != 600 + shownActor)
                throw new InvalidOperationException("AREA draw item pool regrowth");
            cache.Clear();
            if (cache.CreatedCount != cache.DisposedCount)
                throw new InvalidOperationException("AREA texture ownership imbalance");
        }
        finally { viewport.QueueFree(); }
        GD.Print("ARC-RENDER-007 self-test PASS area compositor/culling/generation/ownership" +
            (DisplayServer.GetName() == "headless" ? "; pixels NOT_VERIFIED headless" : "; independent overlap pixels"));
    }
}
