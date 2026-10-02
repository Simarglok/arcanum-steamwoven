using System.Text.Json;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;
using Environment = System.Environment;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Explicit native-smoke only. The private reference is independently read
    // from authenticated original wire bytes, not made from the cache/plan.
    private async Task RunHumanReadbackAsync(TerrainLoadResult result)
    {
        var path = Environment.GetEnvironmentVariable("RENDER006_HUMAN_ORACLE");
        if (string.IsNullOrEmpty(path)) return; // inherited WORLD smoke remains separate
        if (!_nativeSmoke || DisplayServer.GetName() == "headless") throw HumanFailure();
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "arcanum-human-private-oracle-v1"
            || root.GetProperty("actorSha256").GetString() != result.ActorResourceFingerprint!.Sha256
            || root.GetProperty("actorBytes").GetInt64() != result.ActorResourceFingerprint.Size) throw HumanFailure();
        var rows = root.GetProperty("rows").EnumerateArray().ToArray();
        if (rows.Length != 55 || result.ActorTextures.Count != 55 || result.ActorPlan!.Profile.Id != ActorDiagnosticProfileId.HumanFixed) throw HumanFailure();
        long bytes = 0;
        foreach (var row in rows)
        {
            var action = row.GetProperty("action").GetInt32();
            var key = new ActorDiagnosticTextureKey(action == 0 ? 2667 : 2668, action == 0 ? 0x20000000u : 0x20000040u,
                row.GetProperty("group").GetInt32(), row.GetProperty("frame").GetInt32(), 0);
            var frame = result.ActorPlan.Catalog[((ActorDiagnosticAction)action, key.PhysicalGroup, key.Frame)];
            var rgba = Convert.FromBase64String(row.GetProperty("rgba").GetString()!);
            if (frame.Width != row.GetProperty("width").GetInt32() || frame.Height != row.GetProperty("height").GetInt32()
                || frame.HotX != row.GetProperty("hotX").GetInt32() || frame.HotY != row.GetProperty("hotY").GetInt32()
                || frame.ArtOffsetX != row.GetProperty("offsetX").GetInt32() || frame.ArtOffsetY != row.GetProperty("offsetY").GetInt32()
                || !result.ActorTextures[key].Rgba.AsSpan().SequenceEqual(rgba)) throw HumanFailure();
            bytes += rgba.Length;
        }
        if (bytes != 457676) throw HumanFailure();
        var samples = 0; var progression = false; var actual = new List<int[]>();
        var directions = new[] { new Vec2(0,-1), new(1,-1), new(1,0), new(1,1), new(0,1), new(-1,1), new(-1,0), new(-1,-1) };
        // Literal ratified table, independent of Profile.Facing/MapFacing.
        var groups = new[] {0,7,6,5,4,5,6,7};
        foreach (var size in new[] {new Vector2I(1280,800), new(1440,900), new(800,600)})
        {
            await SetSmokeWindowSizeAsync(size); actual.Add([size.X,size.Y]);
            ResetWorldScene(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
            foreach (var action in new[] {0,1})
            for (var slot = 0; slot < 8; slot++)
            {
                _worldActor.Reset();
                // Motion intentionally preserves state for dt0. A positive
                // sub-frame interval admits the requested motion octant.
                _worldActor.Update(directions[slot],.0001);
                if (action == 0) _worldActor.Update(default,0);
                for (var frame = 0; frame < (action == 0 ? 1 : 10); frame++)
                {
                    if (frame > 0) _worldActor.Update(directions[slot],1d/17);
                    PublishWorldSelection(); _camera.ResetWorld(WorldAnchor, WorldViewport,2);
                    if (_worldActor.CurrentFrame != frame) throw HumanFailure();
                    progression |= frame > 0;
                    var row = rows.Single(r => r.GetProperty("action").GetInt32() == action
                        && r.GetProperty("group").GetInt32() == groups[slot] && r.GetProperty("frame").GetInt32() == frame);
                    await NextFrameAsync();
                    using var image = GetViewport().GetTexture().GetImage();
                    if (image.GetWidth() != size.X || image.GetHeight() != size.Y
                        || GetViewport().GetVisibleRect().Size != new Vector2(size.X,size.Y)
                        || !MatchesHumanReference(image,row,slot))
                    {
                        GD.Print($"ARC-RENDER-006 human mismatch size={size.X}x{size.Y} action={action} slot={slot} frame={frame} actualAction={_worldActor.State.Action} actualFacing={_worldActor.State.Facing}");
                        throw HumanFailure();
                    }
                    samples++;
                }
            }
            // Negative controls use the actual main combined canvas, not a
            // fake expected failure or merely a texture image.
            _worldActor.Reset(); _worldActor.Update(directions[2],.0001); PublishWorldSelection();
            var reference = rows.Single(r => r.GetProperty("action").GetInt32()==1 && r.GetProperty("group").GetInt32()==6 && r.GetProperty("frame").GetInt32()==0);
            _camera.ResetWorld(WorldAnchor,WorldViewport,2); await NextFrameAsync();
            _canvas!.SetActorVisibilityForReadback(false); await NextFrameAsync();
            using (var hidden = GetViewport().GetTexture().GetImage()) if (MatchesHumanReference(hidden,reference,2)) throw HumanFailure();
            _canvas.SetActorVisibilityForReadback(true);
            _worldActor.Update(directions[6],.0001); PublishWorldSelection(); await NextFrameAsync();
            using (var wrong = GetViewport().GetTexture().GetImage()) if (MatchesHumanReference(wrong,reference,2)) throw HumanFailure();
        }
        if (samples != 264 || !progression) throw HumanFailure();
        GD.Print("ARC-RENDER-006 human-readback " + JsonSerializer.Serialize(new {
            schema="arcanum-human-validation-v1", task="ARC-RENDER-006",
            candidateCommit=Environment.GetEnvironmentVariable("RENDER006_CANDIDATE"),
            host=Environment.GetEnvironmentVariable("RENDER006_HOST"), kind="human", profile="HumanFixed", outcome="PASS", exitCode=0,
            originalIdentity="authenticated-actor-role", physicalRows=88, distinctKeys=55, rgbaBytes=457676,
            metadata="PASS", allUsedRgba="PASS", graphical=true, actualViewports=actual,
            renderedSelections=samples, negativeControls="PASS", defaultZoom=2, progression="PASS"
        }));
    }
    private bool MatchesHumanReference(Image image, JsonElement row, int slot)
    {
        var width = row.GetProperty("width").GetInt32(); var height = row.GetProperty("height").GetInt32();
        var hotX = row.GetProperty("hotX").GetInt32(); var hotY = row.GetProperty("hotY").GetInt32();
        var flip = slot is >= 1 and <= 3;
        var rgba = Convert.FromBase64String(row.GetProperty("rgba").GetString()!);
        // Independent anchor projection. ART offsets are metadata only.
        var position = _worldActor.State.Position;
        var ax = 40*(position.Y-position.X); var ay = 20*(position.X+position.Y)+20;
        var left = Math.Round(ax,MidpointRounding.ToEven) - (flip ? width-hotX-1 : hotX);
        var top = Math.Round(ay,MidpointRounding.ToEven)-hotY;
        var transform = _canvas!.GetGlobalTransformWithCanvas(); var checkedPixels = 0;
        for (var y=2;y<height-2;y++) for(var x=2;x<width-2;x++)
        {
            var source = (y*width+x)*4; if(rgba[source+3]!=255) continue;
            var wx = left+(flip ? width-1-x : x); var wy=top+y;
            if (Math.Abs(wx-ax)<=5 && Math.Abs(wy-ay)<=5) continue;
            var point=transform*new Vector2((float)(wx+.5),(float)(wy+.5));
            var px=(int)Math.Floor(point.X);var py=(int)Math.Floor(point.Y);
            // The normal sticky HUD occupies 35% of the viewport. Its actual
            // panel intentionally paints above the actor; exclude that region
            // rather than treating overlay pixels as sprite pixels.
            if (_hud!.Rect.HasPoint(new Vector2(px,py))) continue;
            if(px<0||py<0||px>=image.GetWidth()||py>=image.GetHeight())
            { GD.Print($"ARC-RENDER-006 human sample outside px={px} py={py}"); return false; }
            var color=image.GetPixel(px,py);
            if(Math.Abs(Math.Round(color.R*255)-rgba[source])>1 || Math.Abs(Math.Round(color.G*255)-rgba[source+1])>1
                || Math.Abs(Math.Round(color.B*255)-rgba[source+2])>1)
            {
                GD.Print($"ARC-RENDER-006 human sample mismatch sourceXY={x},{y} screenXY={px},{py} checked={checkedPixels} actualRect={_readyActor!.Destination} expectedRect={left},{top},{width},{height} canvasScale={transform.Scale}");
                return false;
            }
            checkedPixels++;
        }
        return checkedPixels>=20;
    }
    private static TerrainViewerException HumanFailure() => new(new(TerrainViewerErrorCode.RenderFailure,Stage:TerrainViewerFailureStage.Readback));
}
