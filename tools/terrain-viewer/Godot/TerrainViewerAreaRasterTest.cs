using Arcanum.TerrainViewer.Core;
using Arcanum.Data.Terrain;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaRasterAsync(Node host)
    {
        if (DisplayServer.GetName() == "headless") return;
        // Independently authored 12x12 source: transparent border and
        // uniquely colored interior texels. Crop [2,10)x[2,10) excludes border.
        var rgba = new byte[12 * 12 * 4];
        for (var y=2; y<10; y++)
        for (var x=2; x<10; x++)
        {
            var color = new byte[]{(byte)(x*17+3),(byte)(y*19+5),(byte)((x+y)*11+7),255};
            color.CopyTo(rgba,(y*12+x)*4);
        }
        using var image = Image.CreateFromData(12,12,false,Image.Format.Rgba8,rgba);
        using var texture = ImageTexture.CreateFromImage(image);
        var viewport = new SubViewport {Size=new(240,220),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
        var item = new AreaDrawItem {FrameTexture=texture,TextureFilter=CanvasItem.TextureFilterEnum.Nearest};
        viewport.AddChild(item); host.AddChild(viewport);
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        var checkedPixels=0; var boundaryMismatches=0; var boundaryPixels=0;
        try
        {
            foreach (var snap in new[]{false,true})
            foreach (var zoom in new[]{.5f,1f,2f})
            foreach (var origin in new[]{new Vector2(100,100),new Vector2(99.75f,100.25f),new Vector2(100.5f,100.5f),new Vector2(99.5f,99.5f)})
            foreach (var flipX in new[]{false,true})
            foreach (var flipY in new[]{false,true})
            foreach (var destination in new[]{new TerrainRect(0,0,8,8),new TerrainRect(1,3,8,8)})
            {
                viewport.Snap2DTransformsToPixel=snap;
                viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                item.Command=new(default,default!,null,new TerrainRect(2,2,8,8),destination,flipX,flipY,0,0,0,0,0,255,false);
                item.QueueRedraw(); await Settle();
                using var observed=viewport.GetTexture().GetImage();
                var effectiveOrigin=snap ? new Vector2(MathF.Ceiling(origin.X-.5f),MathF.Ceiling(origin.Y-.5f)) : origin;
                for(var sy=99;sy<125;sy++)
                for(var sx=99;sx<125;sx++)
                {
                    // Screen-center inversion calculated here from RAW fixture
                    // inputs, never production projection/sampling helpers.
                    var u=(sx+.5-effectiveOrigin.X)/zoom-destination.X; var v=(sy+.5-effectiveOrigin.Y)/zoom-destination.Y;
                    if(u<0 || v<0 || u>=8 || v>=8) continue;
                    var tx=2+(flipX ? 7-(int)Math.Floor(u) : (int)Math.Floor(u));
                    var ty=2+(flipY ? 7-(int)Math.Floor(v) : (int)Math.Floor(v));
                    var at=(ty*12+tx)*4;
                    var actual=observed.GetPixel(sx,sy);
                    var match=Math.Abs(actual.R*255-rgba[at])<=2 && Math.Abs(actual.G*255-rgba[at+1])<=2
                        && Math.Abs(actual.B*255-rgba[at+2])<=2 && Math.Abs(actual.A*255-255)<=2;
                    var margin=Math.Min(Math.Abs(u-Math.Round(u)),Math.Abs(v-Math.Round(v)));
                    if(margin<.1)
                    {
                        boundaryPixels++; if(!match) boundaryMismatches++;
                        continue;
                    }
                    if(!match) throw new InvalidOperationException($"AREA raster interior snap={snap} zoom={zoom} origin={origin} flip={flipX}/{flipY} screen={sx}/{sy} uv={u}/{v} source={tx}/{ty} actual={actual}");
                    checkedPixels++;
                }
                // Frozen exact one-texel control from raw [0,0,8,8] at1x.
                if(!flipX && !flipY && zoom==1 && origin==new Vector2(100,100) && destination.X==0)
                    AssertPixel(observed,101,101,54,62,73,255);
            }
            if(checkedPixels==0 || boundaryPixels==0) throw new InvalidOperationException("AREA raster empty oracle domain");
            GD.Print($"ARC-RENDER-007 raster probe PASS deterministicInterior={checkedPixels} boundaryPixels={boundaryPixels} boundaryMismatches={boundaryMismatches} crop/flip/zoom/fractional/snap");
        }
        finally { viewport.QueueFree(); }
    }
}
