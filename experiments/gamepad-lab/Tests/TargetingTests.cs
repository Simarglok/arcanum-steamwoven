using GamepadLab.Core;
namespace GamepadLab.Tests;

internal static class TargetingTests
{
    public static void Run()
    {
        var engine = new TargetingEngine(); var viewport = new Rect(0, 0, 1280, 800); var focus = viewport.Center;
        var candidates = new[] { new TargetCandidate("crate", "container", focus + new Vec2(60, 0), 4), new TargetCandidate("tiny-gear", "item", focus + new Vec2(61, 0), 5), new TargetCandidate("alpha", "npc", focus + new Vec2(80, 0), 1), new TargetCandidate("zeta", "enemy", focus + new Vec2(-80, 0), 2) };
        var result = engine.Update(candidates, viewport, new(), new()); Assertions.Equal(result.Id!, "crate", "nearest selection");
        var cycle = engine.Cycle(result, 1); Assertions.Equal(cycle.SelectedId!, "tiny-gear", "crate then tiny gear cycle"); Assertions.Equal(engine.Cycle(engine.Update(candidates, viewport, cycle, new()), -1).SelectedId!, "crate", "bidirectional cycle");
        var first = engine.Update(candidates, viewport, engine.Pick("alpha"), new());
        Assertions.Equal(engine.Cycle(first, -1).SelectedId!, "tiny-gear", "previous wraps first cycle entry to last");
        var last = engine.Update(candidates, viewport, engine.Pick("tiny-gear"), new());
        Assertions.Equal(engine.Cycle(last, 1).SelectedId!, "alpha", "next wraps last cycle entry to first");
        var epsilonTie = engine.Update(new[] { new TargetCandidate("zeta", "enemy", focus + new Vec2(80.0005, 0), 2), new TargetCandidate("alpha", "npc", focus + new Vec2(80, 0), 1) }, viewport, new(), new()); Assertions.Equal(epsilonTie.Id!, "alpha", "sub-epsilon tie uses ordinal id");
        var exactTie = engine.Update(new[] { candidates[2], candidates[3] }, viewport, new(), new()); Assertions.Equal(exactTie.Id!, "alpha", "equal tie uses ordinal id");
        var retained = engine.Update(candidates, viewport, new("tiny-gear"), new()); Assertions.Equal(retained.Id!, "tiny-gear", "automatic hysteresis retains within 24px");
        var exactHysteresis = engine.Update(new[] { new TargetCandidate("best", "npc", focus + new Vec2(40, 0), 1), new TargetCandidate("held", "item", focus + new Vec2(64, 0), 2) }, viewport, new("held"), new()); Assertions.Equal(exactHysteresis.Id!, "held", "exact 24px hysteresis boundary retains selection");
        var outsideHysteresis = engine.Update(candidates.Select(x => x.Id == "tiny-gear" ? x with { Anchor = focus + new Vec2(85, 0) } : x).ToArray(), viewport, new("tiny-gear"), new()); Assertions.True(outsideHysteresis.Id != "tiny-gear", "automatic hysteresis releases beyond 24px");
        var locked = engine.Update(candidates, viewport, new("tiny-gear", true), new()); Assertions.Equal(locked.Id!, "tiny-gear", "manual lock"); Assertions.True(!engine.Update(candidates.Select(x => x.Id == "tiny-gear" ? x with { ContextValid = false } : x).ToArray(), viewport, new("tiny-gear", true), new()).ManualLock, "context invalidation clears lock");
        var cancelToAuto = engine.Update(candidates, viewport, engine.CancelLock(), new());
        Assertions.True(!cancelToAuto.ManualLock && cancelToAuto.Id == "crate", "cancel lock returns to automatic best in same update");
        Assertions.True(!engine.Update(candidates.Where(x => x.Id != "tiny-gear").ToArray(), viewport, new("tiny-gear", true), new()).ManualLock, "removal clears lock");
        Assertions.True(!engine.Update(candidates.Select(x => x.Id == "tiny-gear" ? x with { Anchor = focus + new Vec2(181, 0) } : x).ToArray(), viewport, new("tiny-gear", true), new()).ManualLock, "out of radius clears lock");
        var offscreen = engine.Update(new[] { new TargetCandidate("off", "item", new(-1, 20), 1) }, viewport, new(), new(), new(10, 20)); Assertions.True(offscreen.IsGround && offscreen.GroundPoint == new Vec2(10, 20), "offscreen rejected and actual world ground retained");
        var clickedGround = engine.Update(candidates, viewport, engine.PickGround(new(123, 456)), new(), new(640, 400)); Assertions.True(clickedGround.IsGround && clickedGround.GroundPoint == new Vec2(123, 456) && clickedGround.ManualLock, "mouse ground pick persists its canvas-converted world coordinate");
        var radius = engine.Update(new[] { new TargetCandidate("edge", "item", focus + new Vec2(180, 0), 1) }, viewport, new(), new()); Assertions.Equal(radius.Id!, "edge", "radius inclusive");
        foreach (var size in new[] { new Rect(0, 0, 1280, 800), new Rect(0, 0, 1920, 1080) })
        {
            var cameraWorld = new Vec2(640, 400); var worldAnchor = new Vec2(900, 500);
            foreach (var zoom in new[] { .5, 1d, 2d })
            {
                var projected = size.Center + (worldAnchor - cameraWorld) * zoom;
                var ground = cameraWorld + (new Vec2(32, -24) * (1d / zoom));
                var projection = engine.Update(new[] { new TargetCandidate("projected", "item", projected, 1) }, size, new(), new(), ground);
                Assertions.True(zoom == .5 ? projection.Id == "projected" : projection.Id is null, $"noncenter projection changes radius eligibility at {size.Width} zoom {zoom}");
                Assertions.Equal(engine.Update([], size, new(), new(), ground).GroundPoint, ground, $"noncenter ground conversion fixture {size.Width} zoom {zoom}");
            }
        }
    }
}