using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // Diagnostic actual maps are never expected color/index tables. This keeps
    // the rejected rational rule visible while discriminating repeat stability.
    private static async Task AssertAreaIndexStabilityAsync(Node host)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("AREA stability requires native renderer");
        var sub = new SubViewport { Size = new(400,300), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        host.AddChild(sub);
        var hud = host.GetNode<TerrainDiagnosticHud>("Hud"); var camera = host.GetNode<Camera2D>("TerrainCamera");
        var savedHud = hud.Visible; var savedCamera = camera.Enabled; hud.Hide(); camera.Enabled = false;
        var records = new List<object>();
        var rgba = new byte[31*44*4];
        for (var y=0;y<44;y++) for (var x=0;x<31;x++)
        { var at=(y*31+x)*4; rgba[at]=(byte)((x+1)*5); rgba[at+1]=(byte)((y+1)*5); rgba[at+2]=(byte)((x+y)*2+5); rgba[at+3]=255; }
        var sourceSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rgba)).ToLowerInvariant();
        var command = new AreaDrawCommand(default,default!,null,new(0,0,31,44),new(40,20,31,44),false,false,0,0,0,0,0,255,false);
        var stable = true; var baselineMismatch = 0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        try
        {
            foreach (var viewport in new Viewport[] { sub,host.GetViewport() })
            {
                var saved = viewport.CanvasTransform; var snap = viewport.Snap2DTransformsToPixel;
                viewport.Snap2DTransformsToPixel = true;
                var transform = new Transform2D(0,new Vector2(.5f,.5f),0,new(100,100));
                byte[]? firstMap = null;
                try
                {
                    // Repeat unchanged binding, exact pan restore, then release
                    // and reupload the same public source to a new draw item.
                    for (var generation=0;generation<2;generation++)
                    {
                        using var source = Image.CreateFromData(31,44,false,Image.Format.Rgba8,rgba);
                        using var texture = ImageTexture.CreateFromImage(source);
                        var item = new AreaDrawItem { TextureFilter = CanvasItem.TextureFilterEnum.Nearest, FrameTexture = texture, Command = command };
                        viewport.AddChild(item);
                        try
                        {
                            for (var repetition=0;repetition<4;repetition++)
                            {
                                if (repetition==3) { viewport.CanvasTransform = new Transform2D(0,new Vector2(.5f,.5f),0,new(113,91)); await Settle(); }
                                viewport.CanvasTransform = transform; item.QueueRedraw(); await Settle();
                                using var observed = viewport.GetTexture().GetImage();
                                var data = observed.GetData(); var width = observed.GetWidth();
                                if (observed.GetFormat()!=Image.Format.Rgba8) throw new InvalidOperationException("AREA_STABILITY_RGBA8_REQUIRED");
                                var pixels = new List<object>(); var map = new List<byte>(); var failed=0;
                                // Same already-failing full-frame calibration case.
                                for (var sy=110;sy<132;sy++) for (var sx=120;sx<135;sx++)
                                {
                                    var at=(sy*width+sx)*4; var r=data[at];var g=data[at+1];var b=data[at+2];var a=data[at+3];
                                    var tx=r/5-1;var ty=g/5-1;
                                    if (r%5!=0||g%5!=0||tx<0||tx>=31||ty<0||ty>=44||b!=(tx+ty)*2+5||a!=255)
                                        throw new InvalidOperationException("AREA_STABILITY_INDEX_ENCODING_INVALID");
                                    var u=2*(sx+.5-100)-40;var v=2*(sy+.5-100)-20;
                                    var expectedX=(int)Math.Floor(u);var expectedY=(int)Math.Floor(v);
                                    if (tx!=expectedX||ty!=expectedY) failed++;
                                    map.Add(r);map.Add(g);map.Add(b);map.Add(a);
                                    pixels.Add(new { ScreenX=sx,ScreenY=sy,RawRgba=new[]{(int)r,g,b,a},ActualX=tx,ActualY=ty,
                                        RationalX=expectedX,RationalY=expectedY,MismatchX=tx!=expectedX,MismatchY=ty!=expectedY });
                                }
                                var bytes=map.ToArray(); firstMap??=bytes;var same=firstMap.SequenceEqual(bytes);stable&=same;baselineMismatch+=failed;
                                records.Add(new { MainViewport=ReferenceEquals(viewport,host.GetViewport()),Viewport=viewport.GetInstanceId(),
                                    ViewportWidth=width,ViewportHeight=observed.GetHeight(),Generation=generation,Repetition=repetition,
                                    Item=item.GetInstanceId(),Texture=texture.GetInstanceId(),Frame=Engine.GetProcessFrames(),
                                    RawTransform=viewport.CanvasTransform.ToString(),ItemTransform=item.Transform.ToString(),
                                    GlobalTransform=item.GetGlobalTransformWithCanvas().ToString(),Command=command,Nearest=true,
                                    SnapTransforms=viewport.Snap2DTransformsToPixel,SnapVertices=viewport.Snap2DVerticesToPixel,
                                    StableAgainstFirst=same,RationalMismatches=failed,Pixels=pixels });
                            }
                        }
                        finally { item.FrameTexture=null;viewport.RemoveChild(item);item.Free(); }
                    }
                }
                finally { viewport.CanvasTransform=saved;viewport.Snap2DTransformsToPixel=snap;await Settle(); }
            }
            var path = System.Environment.GetEnvironmentVariable("ARC_AREA_REPORT");
            if (!string.IsNullOrWhiteSpace(path)) System.IO.File.WriteAllText(path+".index-stability.json",System.Text.Json.JsonSerializer.Serialize(new {
                Schema="arcanum-area-index-stability-v1",Outcome=stable?"STABLE":"UNSTABLE",PredictorOutcome="NOT_VERIFIED",
                SourceSha256=sourceSha,SourceWidth=31,SourceHeight=44,Filter="Nearest",Baseline="rejected-raw-rational-floor",
                RationalMismatches=baselineMismatch,Cases=records }, new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
            if (!stable) throw new InvalidOperationException("AREA_INDEX_REPEAT_UNSTABLE");
            if (baselineMismatch==0) throw new InvalidOperationException("AREA_EXPECTED_REJECTED_CALIBRATION_CASE_NOT_REPRODUCED");
            GD.Print($"ARC-RENDER-007 index stability STABLE cases={records.Count} rejectedRationalMismatches={baselineMismatch}; exact native predictor/holdout NOT_VERIFIED");
        }
        finally { sub.QueueFree();hud.Visible=savedHud;camera.Enabled=savedCamera; }
    }
}
