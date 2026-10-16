using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaCompositionMatrixAsync(Node host)
    {
        // Exact visible residency is asserted below: no LRU retention.
        using var cache = new TerrainTextureCache { AreaRetainFrames = 0 };
        var synthetic = SyntheticActorFixture.Create(human:true);
        cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size=new(480,400),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var native = DisplayServer.GetName() != "headless";
        var views = 0; var samples = 0;
        GD.Print($"ARC-RENDER-007 composition environment mainViewport={host.GetViewport().GetVisibleRect().Size} native={native}");
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            if (native) await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        void Pixel(float zoom, Vector2 origin, int x, int y, string winner)
        {
            if (!native) return;
            // Screen coordinates come from the fixture's LITERAL expected world
            // rectangle, never the command under test or AreaPixelDiagnostics.
            var sx = (int)MathF.Floor(origin.X+x*zoom); var sy = (int)MathF.Floor(origin.Y+y*zoom);
            using var image = viewport.GetTexture().GetImage();
            if (image.GetSize() != new Vector2I(480,400)) throw new InvalidOperationException("AREA_COMPOSITION_ACTUAL_VIEWPORT");
            if (sx<0 || sy<0 || sx>=480 || sy>=400) throw new InvalidOperationException("AREA_COMPOSITION_ORACLE_OUTSIDE_VIEWPORT");
            var rgba = winner switch
            {
                "A" => (10,20,200,255), "B" => (18,197,71,255),
                "actor-right" => (24,218,236,255), "marker" => (250,150,50,255),
                _ => (231,37,19,255)
            };
            try { AssertPixel(image,sx,sy,(byte)rgba.Item1,(byte)rgba.Item2,(byte)rgba.Item3,(byte)rgba.Item4); }
            catch (Exception error) { throw new InvalidOperationException($"AREA_COMPOSITION_PIXEL world={x}/{y} screen={sx}/{sy} winner={winner}",error); }
            samples++;
        }

        try
        {
            foreach (var item in AreaCompositionFixture.Cases)
            {
                var scene=AreaCompositionFixture.Create(item);
                var placement=AreaCompositionFixture.Placement(item);
                var actor=synthetic.Result.ActorPlan!.Select(placement);
                if (actor.Current.Destination != item.ActorRectangle
                    || AreaCompositionFixture.Order(scene.Plan,placement,actor.Current) != item.Order)
                    throw new InvalidOperationException("AREA_COMPOSITION_LITERAL_PLAN:"+item.Name);
                var a=scene.Plan.Commands.SingleOrDefault(command=>command.Identity==scene.A.Key);
                if (a is not null && a.Destination != item.CanopyRectangle)
                    throw new InvalidOperationException("AREA_COMPOSITION_CANOPY_RECT:"+item.Name);
                var frames=new Dictionary<AreaFrameKey,DecodedAreaFrame> {
                    [AreaFrameKey.From(scene.Av)]=new(item.Width,item.Height,AreaCompositionFixture.Pixels(item.Width,item.Height,true)),
                    [AreaFrameKey.From(scene.Bv)]=new(160,200,AreaCompositionFixture.Pixels(160,200,false)) };
                foreach (var zoom in new[] { .5f,1f,2f })
                foreach (var pan in new[] { Vector2.Zero,new Vector2(13,-11),new Vector2(-17,9) })
                {
                    var origin=(zoom == .5f ? new Vector2(99.75f,100.25f) : new Vector2(100,100))+pan;
                    viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                    canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(true);
                    canvas.PublishArea(scene.Plan,frames,actor,placement,cache); await Settle();
                    var order=string.Join(',',canvas.GetChildren().OfType<AreaDrawItem>().Where(node=>node.Visible)
                        .Select(node=>AreaCompositionFixture.Name(node.Command!)));
                    if (order != item.Order || canvas.AreaVisibleCount != item.Order.Split(',').Length-1
                        || canvas.AreaCulledCount != 0 || cache.AreaCount != canvas.AreaVisibleCount)
                        throw new InvalidOperationException($"AREA_COMPOSITION_SUBMISSION:{item.Name}:{order}");
                    var x=item.ActorRectangle.X+(item.Winner == "actor-right" ? 7 : 1);
                    var y=item.ActorRectangle.Y+1;
                    Pixel(zoom,origin,x,y,item.Winner);
                    // Nonvacuity: actor-only proves its opaque sample exists at
                    // that point; scenery-only independently proves both bodies
                    // cover it and phase/order, not a guide or background, wins.
                    canvas.SetStaticVisibilityForReadback(false); await Settle();
                    Pixel(zoom,origin,x,y,item.Winner == "actor-right" ? "actor-right" : "actor");
                    canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(false); await Settle();
                    var sceneryWinner=a is null || item.Name == "flat-before-upright" ? "B" : "A";
                    Pixel(zoom,origin,x,y,sceneryWinner);
                    canvas.SetActorVisibilityForReadback(true); await Settle();
                    Pixel(zoom,origin,x,y,item.Winner); // restore/repeat must not retain the hidden order
                    views++;
                }
                GD.Print($"ARC-RENDER-007 composition case PASS {item.Name} order={item.Order} winner={item.Winner}");
            }
            // Reflect the asymmetric four-pixel marker through DATA descriptor
            // mirror XOR serialized blit-X and independent blit-Y. Destination
            // and map ranks remain fixed: this is not hotspot calibration.
            foreach (var mirrored in new[] { false,true })
            foreach (var blit in new uint[] { 0,1,2,3 })
            {
                var body=AreaCompositionFixture.Body(0,blit:blit);
                var visual=AreaCompositionFixture.Visual(body,160,160,40,80,mirrored:mirrored);
                var plan=AreaScenePlan.Create([], [body], [visual]);
                var frames=new Dictionary<AreaFrameKey,DecodedAreaFrame> {
                    [AreaFrameKey.From(visual)]=new(160,160,AreaCompositionFixture.Pixels(160,160,true)) };
                var placement=AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]);
                var actor=synthetic.Result.ActorPlan!.Select(placement);
                foreach (var zoom in new[] { .5f,1f,2f })
                foreach (var pan in new[] { Vector2.Zero,new Vector2(13,-11),new Vector2(-17,9) })
                {
                    // Frame the complete reflection landmark range at 2x,
                    // including negative pan. This changes only the synthetic
                    // camera, never source placement or expected marker pixels.
                    var origin=new Vector2(100,130)+pan;
                    viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                    canvas.SetActorVisibilityForReadback(false); canvas.SetStaticVisibilityForReadback(true);
                    canvas.PublishArea(plan,frames,actor,placement,cache); await Settle();
                    // Literal marker rectangles: X10..13 or146..149,
                    // Y(-50)..(-47) or86..89. Probe their 2x2 inner block.
                    Pixel(zoom,origin,mirrored != ((blit&1)!=0) ? 147 : 11,(blit&2)!=0 ? 87 : -49,"marker");
                    if (plan.Commands.Single().Destination != new TerrainRect(0,-60,160,160))
                        throw new InvalidOperationException("AREA_COMPOSITION_REFLECTION_ANCHOR");
                    views++;
                }
            }
            var canopyScene=AreaCompositionFixture.Create(AreaCompositionFixture.Cases[0]);
            var canopyFrames=new Dictionary<AreaFrameKey,DecodedAreaFrame> {
                [AreaFrameKey.From(canopyScene.Av)]=new(160,160,AreaCompositionFixture.Pixels(160,160,true)),
                [AreaFrameKey.From(canopyScene.Bv)]=new(160,200,AreaCompositionFixture.Pixels(160,200,false)) };
            var canopyPlacement=AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]);
            var canopyActor=synthetic.Result.ActorPlan!.Select(canopyPlacement);
            foreach (var zoom in new[] { .5f,1f,2f })
            {
                var origin=new Vector2(-60*zoom,100);
                viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                canvas.SetActorVisibilityForReadback(false);
                canvas.PublishArea(canopyScene.Plan,canopyFrames,canopyActor,canopyPlacement,cache); await Settle();
                if (canvas.AreaVisibleCount != 2 || canvas.AreaCulledCount != 0 || cache.AreaCount != 2)
                    throw new InvalidOperationException("AREA_COMPOSITION_OFFSCREEN_ANCHOR_CULL");
                Pixel(zoom,origin,120,-30,"A"); views++;
            }
            canvas.ClearScene(); cache.Clear(); await Settle();
            if (cache.Count != 0 || cache.CreatedCount != cache.DisposedCount
                || canvas.AreaVisibleCount != 0 || canvas.GetChildren().OfType<AreaDrawItem>().Any())
                throw new InvalidOperationException("AREA_COMPOSITION_OWNERSHIP");
        }
        finally { canvas.ClearScene(); viewport.QueueFree(); }
        GD.Print($"ARC-RENDER-007 self-test PASS composition matrix cases=20 views={views} samples={samples} viewport=480x400"+
            (native ? "; independent literal order/pixels/reflection/phase/visibility/offscreen-anchor/ownership" : "; pixels NOT_VERIFIED headless"));
    }
}
