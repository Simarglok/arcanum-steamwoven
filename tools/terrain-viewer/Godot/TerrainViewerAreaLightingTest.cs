using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    // ARC-RENDER-010: the production compositor multiplies each item by the ambient colour of
    // its own channel, follows a channel/pair change without a plan republish, and draws
    // unchanged source pixels with no ambient. Expected pixels are literal hand-computed values.
    private static async Task AssertAreaLightingAsync(Node host)
    {
        using var cache = new TerrainTextureCache();
        var synthetic = SyntheticActorFixture.Create(human: true, area: true);
        cache.UploadActor(synthetic.Result.ActorTextures);
        var viewport = new SubViewport { Size = new(480, 400), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new TerrainCanvas(); viewport.AddChild(canvas); host.AddChild(viewport);
        var native = DisplayServer.GetName() != "headless";
        var samples = 0;
        async Task Settle()
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (native) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        AreaDrawItem Item(bool actor) => canvas.GetChildren().OfType<AreaDrawItem>().Single(node => node.Visible && (node.Actor is not null) == actor);
        void Expect(string label, Color itemAmbient, (int R, int G, int B) marker)
        {
            if (Item(false).Ambient != itemAmbient) throw new InvalidOperationException("AREA_LIGHTING_ITEM_AMBIENT:" + label);
            if (!native) return;
            // Marker block X10..13/Y10..13 of the 160x160 body at destination (0,-60), origin (100,130), zoom 1.
            using var image = viewport.GetTexture().GetImage();
            var pixel = image.GetPixel(100 + 11, 130 - 49);
            var actual = ((int)Math.Round(pixel.R * 255), (int)Math.Round(pixel.G * 255), (int)Math.Round(pixel.B * 255));
            if (Math.Abs(actual.Item1 - marker.R) > 1 || Math.Abs(actual.Item2 - marker.G) > 1 || Math.Abs(actual.Item3 - marker.B) > 1)
                throw new InvalidOperationException($"AREA_LIGHTING_PIXEL:{label} expected {marker} actual {actual}");
            samples++;
        }
        try
        {
            var body = AreaCompositionFixture.Body(0);
            var visual = AreaCompositionFixture.Visual(body, 160, 160, 40, 80);
            var plan = AreaScenePlan.Create([], [body], [visual]);
            var frames = new Dictionary<AreaFrameKey, DecodedAreaFrame> {
                [AreaFrameKey.From(visual)] = new(160, 160, AreaCompositionFixture.Pixels(160, 160, true)) };
            var placement = AreaCompositionFixture.Placement(AreaCompositionFixture.Cases[0]);
            var actor = synthetic.Result.ActorPlan!.Select(placement);
            viewport.CanvasTransform = new Transform2D(0, Vector2.One, 0, new Vector2(100, 130));
            canvas.SetStaticVisibilityForReadback(true); canvas.SetActorVisibilityForReadback(true);
            canvas.PublishArea(plan, frames, actor, placement, cache); await Settle();
            Expect("unlit", Colors.White, (250, 150, 50));
            if (Item(true).Ambient != Colors.White) throw new InvalidOperationException("AREA_LIGHTING_ACTOR_UNLIT");

            var pair = new AmbientPair(new(128, 64, 255), new(255, 0, 0));
            var outdoor = new Color(128 / 255f, 64 / 255f, 1f); var indoor = new Color(1f, 0f, 0f);
            canvas.SetAreaAmbient(new(pair, new Dictionary<AreaVisualKey, AreaAmbientChannel> { [visual.Key] = AreaAmbientChannel.Outdoor })); await Settle();
            Expect("outdoor", outdoor, (125, 38, 50));
            if (Item(true).Ambient != outdoor) throw new InvalidOperationException("AREA_LIGHTING_ACTOR_OUTDOOR");
            canvas.SetAreaAmbient(new(pair, new Dictionary<AreaVisualKey, AreaAmbientChannel> { [visual.Key] = AreaAmbientChannel.Indoor })); await Settle();
            Expect("indoor", indoor, (250, 0, 0));
            canvas.SetAreaAmbient(new(pair, new Dictionary<AreaVisualKey, AreaAmbientChannel> { [visual.Key] = AreaAmbientChannel.Unlit })); await Settle();
            Expect("unlit-channel", Colors.White, (250, 150, 50));
            canvas.SetAreaAmbient(new(AmbientPair.Neutral, new Dictionary<AreaVisualKey, AreaAmbientChannel> { [visual.Key] = AreaAmbientChannel.Outdoor })); await Settle();
            Expect("neutral", Colors.White, (250, 150, 50));
            canvas.SetAreaAmbient(null); await Settle();
            Expect("cleared", Colors.White, (250, 150, 50));
            // A plan visual without a channel is a defect, never a silent unlit draw.
            canvas.SetAreaAmbient(new(pair, new Dictionary<AreaVisualKey, AreaAmbientChannel>()));
            var rejected = false; canvas.AreaFailure = _ => rejected = true; await Settle();
            if (!rejected) throw new InvalidOperationException("AREA_LIGHTING_MISSING_CHANNEL_ACCEPTED");
            canvas.SetAreaAmbient(null);
        }
        finally { canvas.ClearScene(); viewport.QueueFree(); }
        GD.Print($"ARC-RENDER-010 self-test PASS ambient channels/pair change/unlit samples={samples}" + (native ? "" : "; pixels NOT_VERIFIED headless"));
    }
}
