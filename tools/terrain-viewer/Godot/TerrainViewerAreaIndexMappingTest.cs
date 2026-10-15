using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaIndexMappingAsync(Node host)
    {
        if(DisplayServer.GetName()=="headless") throw new InvalidOperationException("AREA index mapping requires native renderer");
        var sub=new SubViewport{Size=new(400,300),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
        host.AddChild(sub);
        var hud=host.GetNode<TerrainDiagnosticHud>("Hud"); var camera=host.GetNode<Camera2D>("TerrainCamera");
        var savedHud=hud.Visible;var savedCamera=camera.Enabled;hud.Hide();camera.Enabled=false;
        var rows=new List<object>();var mismatches=0;var checkedPixels=0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            foreach(var viewport in new Viewport[]{sub,host.GetViewport()})
            {
                var saved=viewport.CanvasTransform;var snap=viewport.Snap2DTransformsToPixel;viewport.Snap2DTransformsToPixel=true;
                var item=new AreaDrawItem{TextureFilter=CanvasItem.TextureFilterEnum.Nearest};viewport.AddChild(item);
                try
                {
                    // Public indexed calibration sources; raw rational nearest
                    // hypothesis is frozen BEFORE readback. This is a diagnostic,
                    // not a neighboring-color or calibration-only PASS oracle.
                    foreach(var size in new[]{new Vector2I(31,44),new Vector2I(27,35),new Vector2I(10,23)})
                    {
                        var rgba=new byte[size.X*size.Y*4];
                        for(var y=0;y<size.Y;y++)for(var x=0;x<size.X;x++)
                        {var at=(y*size.X+x)*4;rgba[at]=(byte)((x+1)*5);rgba[at+1]=(byte)((y+1)*5);rgba[at+2]=(byte)((x+y)*2+5);rgba[at+3]=255;}
                        using var source=Image.CreateFromData(size.X,size.Y,false,Image.Format.Rgba8,rgba);
                        using var texture=ImageTexture.CreateFromImage(source);item.FrameTexture=texture;
                        foreach(var zoom in new[]{.5f,1f,2f})
                        foreach(var origin in new[]{new Vector2(100,100),new Vector2(99.5f,99.5f),new Vector2(100.5f,100.5f),new Vector2(99.75f,100.25f)})
                        foreach(var offset in new[]{new Vector2I(0,0),new Vector2I(-41,-23)})
                        foreach(var fx in new[]{false,true})foreach(var fy in new[]{false,true})
                        {
                            var dx=40+offset.X;var dy=20+offset.Y;
                            viewport.CanvasTransform=new Transform2D(0,new Vector2(zoom,zoom),0,origin);
                            item.Command=new(default,default!,null,new(0,0,size.X,size.Y),new(dx,dy,size.X,size.Y),fx,fy,0,0,0,0,0,255,false);
                            item.QueueRedraw();await Settle();
                            using var observed=viewport.GetTexture().GetImage();
                            var ex=Math.Ceiling(origin.X-.5);var ey=Math.Ceiling(origin.Y-.5);
                            var cases=0;var failed=0;object? first=null;
                            for(var sy=(int)Math.Floor(ey+dy*zoom);sy<(int)Math.Ceiling(ey+(dy+size.Y)*zoom);sy++)
                            for(var sx=(int)Math.Floor(ex+dx*zoom);sx<(int)Math.Ceiling(ex+(dx+size.X)*zoom);sx++)
                            {
                                var u=(sx+.5-ex)/zoom-dx;var v=(sy+.5-ey)/zoom-dy;
                                if(u<=0||v<=0||u>=size.X||v>=size.Y)continue;
                                // Reflection reverses UV before nearest selection:
                                // floor(width-u), NOT width-1-floor(u) at exact ties.
                                var tx=(int)Math.Floor(fx?size.X-u:u);var ty=(int)Math.Floor(fy?size.Y-v:v);
                                var at=(ty*size.X+tx)*4;var c=observed.GetPixel(sx,sy);
                                var match=Math.Abs(c.R*255-rgba[at])<=2&&Math.Abs(c.G*255-rgba[at+1])<=2
                                    &&Math.Abs(c.B*255-rgba[at+2])<=2&&Math.Abs(c.A*255-255)<=2;
                                cases++;checkedPixels++;
                                if(!match){failed++;mismatches++;first??=new{sx,sy,u,v,tx,ty,Expected=new[]{(int)rgba[at],rgba[at+1],rgba[at+2],255},Actual=c.ToString()};}
                            }
                            rows.Add(new{MainViewport=ReferenceEquals(viewport,host.GetViewport()),size.X,size.Y,Zoom=zoom,Origin=origin.ToString(),Offset=offset.ToString(),fx,fy,Checked=cases,Mismatches=failed,First=first});
                        }
                        item.FrameTexture=null;
                    }
                }
                finally{viewport.CanvasTransform=saved;viewport.Snap2DTransformsToPixel=snap;item.QueueFree();await Settle();}
            }
            var path=System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
            if(!string.IsNullOrWhiteSpace(path)) System.IO.File.WriteAllText(path+".index-hypothesis.json",System.Text.Json.JsonSerializer.Serialize(
                new{Schema="arcanum-area-index-hypothesis-v1",Outcome=mismatches==0?"PASS":"FAIL",Model="raw-rational-floor-reflect-before-nearest",Checked=checkedPixels,Mismatches=mismatches,Cases=rows}));
            if(mismatches!=0)throw new InvalidOperationException($"AREA_EXACT_INDEX_HYPOTHESIS_REJECTED pixels={checkedPixels} mismatches={mismatches}");
            GD.Print($"ARC-RENDER-007 raw index hypothesis PASS pixels={checkedPixels}; sparse holdout/admission still separately required");
        }
        finally{sub.QueueFree();hud.Visible=savedHud;camera.Enabled=savedCamera;}
    }
}
