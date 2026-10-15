using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data.Terrain;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaNearestHoldoutsAsync(Node host)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA_NEAREST_NATIVE_REQUIRED");
        var path = System.Environment.GetEnvironmentVariable("ARC_AREA_NEAREST_ORACLE");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("AREA_NEAREST_FROZEN_ORACLE_REQUIRED");
        var bytes = System.IO.File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != "2fcc52540cbcc85cdbf94a3033d16dadd91ad82a8b298d418f1fc7bedc915fcf")
            throw new InvalidOperationException("AREA_NEAREST_FROZEN_ORACLE_CHANGED");
        using var oracle = JsonDocument.Parse(bytes);
        var sub = new SubViewport { Size = new(400,300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        host.AddChild(sub);
        var hud = host.GetNode<TerrainDiagnosticHud>("Hud"); var camera = host.GetNode<Camera2D>("TerrainCamera");
        var oldHud = hud.Visible; var oldCamera = camera.Enabled; hud.Hide(); camera.Enabled = false;
        var checkedPixels = 0; var holes = 0; var cases = 0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            foreach (var viewport in new Viewport[] { sub, host.GetViewport() })
            {
                var oldTransform = viewport.CanvasTransform; var oldSnap = viewport.Snap2DTransformsToPixel;
                viewport.Snap2DTransformsToPixel = false;
                var item = new AreaDrawItem(); viewport.AddChild(item);
                try
                {
                    foreach (var row in oracle.RootElement.EnumerateArray())
                    {
                        var width = row.GetProperty("width").GetInt32(); var height = row.GetProperty("height").GetInt32();
                        var rgba = new byte[width*height*4];
                        for (var y=0;y<height;y++) for(var x=0;x<width;x++)
                        {
                            var at=(y*width+x)*4; rgba[at]=(byte)((x+1)*5); rgba[at+1]=(byte)((y+1)*5);
                            rgba[at+2]=(byte)((x+y)*2+5); rgba[at+3]=(byte)((x+3*y)%7==0?0:255);
                        }
                        using var source = Image.CreateFromData(width,height,false,Image.Format.Rgba8,rgba);
                        using var texture = ImageTexture.CreateFromImage(source); item.FrameTexture=texture;
                        var crop=row.GetProperty("crop").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                        var dest=row.GetProperty("destination").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                        var origin=row.GetProperty("origin").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                        var flip=row.GetProperty("flip").EnumerateArray().Select(v=>v.GetBoolean()).ToArray();
                        var zoom=row.GetProperty("zoom").GetSingle();
                        item.Command=new(default,default!,null,new(crop[0],crop[1],crop[2],crop[3]),new(dest[0],dest[1],dest[2],dest[3]),flip[0],flip[1],0,0,0,0,0,255,false);
                        async Task Check(int pan)
                        {
                            viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,new(origin[0]+pan,origin[1]+pan));
                            item.QueueRedraw(); await Settle();
                            using var observed=viewport.GetTexture().GetImage();
                            foreach(var point in row.GetProperty("points").EnumerateArray())
                            {
                                var p=point.EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                                if(p[4]==0)
                                {
                                    if(viewport==sub && observed.GetPixel(p[0]+pan,p[1]+pan).A > .01f)
                                        throw new InvalidOperationException("AREA_NEAREST_TRANSPARENT_HOLE");
                                    holes++; continue;
                                }
                                // Oracle indices came from separately frozen Python
                                // Fraction arithmetic, not sampler commands or GPU.
                                AssertPixel(observed,p[0]+pan,p[1]+pan,(byte)((p[2]+1)*5),(byte)((p[3]+1)*5),(byte)((p[2]+p[3])*2+5),255);
                                checkedPixels++;
                            }
                        }
                        await Check(0);
                        if(cases%54==0)
                        {
                            await Check(12); await Check(0);
                            item.ReleaseMapping(); item.FrameTexture=null;
                            using var republished=ImageTexture.CreateFromImage(source); item.FrameTexture=republished;
                            await Check(0); item.FrameTexture=null;
                        }
                        item.FrameTexture=null; cases++;
                    }
                }
                finally { viewport.CanvasTransform=oldTransform; viewport.Snap2DTransformsToPixel=oldSnap; item.QueueFree(); await Settle(); }
            }
            GD.Print($"ARC-RENDER-007 exact nearest holdouts PASS cases={cases} opaquePixels={checkedPixels} holes={holes} main/sub .5/1/2 crop/reflection/sign/fractional/pan/restore/reupload frozenOracle=2fcc5254");
        }
        finally { sub.QueueFree(); hud.Visible=oldHud; camera.Enabled=oldCamera; }
    }
}
