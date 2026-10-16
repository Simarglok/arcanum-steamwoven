using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaOwnerSeamsAsync(Node host)
    {
        var oraclePath=System.Environment.GetEnvironmentVariable("ARC_AREA_SEAM_ORACLE")
            ?? throw new InvalidOperationException("AREA_SEAM_ORACLE_REQUIRED");
        var bytes=File.ReadAllBytes(oraclePath);
        if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=
            "9b8bf682c750628679ceee642351b965d97a865f734e6dd7ea64b812fe946532")
            throw new InvalidOperationException("AREA_SEAM_ORACLE_IDENTITY");
        using var oracle=JsonDocument.Parse(bytes);
        using var fixture=new AreaOwnerFixture(seamMarkers:true);
        var read=typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea",BindingFlags.NonPublic|BindingFlags.Static)!;
        var result=(AreaLoadResult)read.Invoke(null,[23L,fixture.Owner,CancellationToken.None,null])!;
        if(result.Generation!=23 || result.Plan.Commands.Count!=16389 || result.Snapshot.Reconciliation.CoreTerrain!=16384
            || result.Frames.Count!=2 || result.Plan.Commands.Count(c=>c.Identity is null&&c.FlipX)!=4)
            throw new InvalidOperationException("AREA_SEAM_STRICT_COVERAGE");
        fixture.Owner.Dispose(); var settledUsage=fixture.Owner.Usage;
        using var cache=new TerrainTextureCache(); var actor=SyntheticActorFixture.Create(human:true);
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
        try
        {
            canvas.SetActorVisibilityForReadback(false); canvas.SetStaticVisibilityForReadback(false);
            foreach(var view in oracle.RootElement.GetProperty("views").EnumerateArray())
            {
                var zoom=view.GetProperty("zoom").GetSingle();
                var origin=new Vector2(view.GetProperty("origin_x").GetSingle(),view.GetProperty("origin_y").GetSingle());
                viewport.CanvasTransform=new Transform2D(0,new(zoom,zoom),0,origin);
                canvas.PublishArea(result.Plan,result.Frames,actor.Result.ActorPlan!,actor.Arguments.ActorInput!,cache);
                await Settle();
                if(canvas.AreaVisibleCount+canvas.AreaCulledCount!=16389
                    || canvas.AreaVisibleCommands.Select(c=>c.Visual).Distinct().Count()!=canvas.AreaVisibleCount)
                    throw new InvalidOperationException("AREA_SEAM_VISIBLE_IDENTITIES");
                using var image=viewport.GetTexture().GetImage();
                if(image.GetSize()!=new Vector2I(480,400)) throw new InvalidOperationException("AREA_SEAM_VIEWPORT");
                foreach(var sample in view.GetProperty("samples").EnumerateArray())
                {
                    // Expected screen pixels come only from the independently
                    // frozen full wire-grid oracle, never submitted destinations.
                    var x=sample.GetProperty("x").GetInt32(); var y=sample.GetProperty("y").GetInt32();
                    AssertPixel(image,x,y,sample.GetProperty("r").GetByte(),sample.GetProperty("g").GetByte(),
                        sample.GetProperty("b").GetByte(),sample.GetProperty("a").GetByte());
                    samples++;
                }
                // No cropped magenta column78/79 may enter this whole viewport.
                for(var y=0;y<400;y++) for(var x=0;x<480;x++)
                {
                    var pixel=image.GetPixel(x,y);
                    if(pixel.R8==255&&pixel.G8==0&&pixel.B8==255&&pixel.A8==255)
                        throw new InvalidOperationException("AREA_SEAM_CROPPED_SENTINEL_LEAK");
                }
                views++;
            }
            if(views!=9 || samples!=297 || fixture.Owner.Usage!=settledUsage
                || fixture.Streams.Values.Any(s=>s.DisposeCount!=1))
                throw new InvalidOperationException("AREA_SEAM_COMPLETE_PROOF");
            GD.Print($"ARC-RENDER-007 owner seams PASS views={views} samples={samples} mirroredCells=4 terrain=16384 commands=16389 readback=480x400 mainViewport={host.GetViewport().GetVisibleRect().Size}; full viewport cropped sentinel absent; strict repository/provider disposed; actor/traversal integration NOT_VERIFIED");
        }
        finally
        {
            canvas.ClearScene(); viewport.Free(); cache.Dispose();
            if(cache.CreatedCount!=cache.DisposedCount || cache.AreaCount!=0)
                throw new InvalidOperationException("AREA_SEAM_TEXTURE_OWNERSHIP");
        }
    }
}
