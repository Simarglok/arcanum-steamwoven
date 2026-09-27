using GamepadLab.Core;
namespace GamepadLab.Tests;

internal static class TargetingTests
{
    public static void Run()
    {
        var engine = new TargetingEngine(); var viewport = new Rect(0, 0, 1280, 800); var focus = viewport.Center;
        var candidates = new[] { new TargetCandidate("crate", "container", focus + new Vec2(60, 0), 4), new TargetCandidate("tiny-gear", "item", focus + new Vec2(61, 0), 5), new TargetCandidate("alpha", "npc", focus + new Vec2(80, 0), 1), new TargetCandidate("zeta", "enemy", focus + new Vec2(-80, 0), 2) };
        var result = engine.Update(candidates, viewport, new(), new()); Assertions.Equal(result.Id!, "crate", "nearest selection");
        var cycle = engine.Cycle(result, 1); Assertions.Equal(cycle.SelectedId!, "tiny-gear", "crate then gear cycle");
        Assertions.Equal(engine.Cycle(engine.Update(candidates, viewport, cycle, new()), -1).SelectedId!, "crate", "backward cycle");
        var tie = engine.Update(new[] { candidates[2], candidates[3] }, viewport, new(), new()); Assertions.Equal(tie.Id!, "alpha", "ordinal tie");
        var locked = engine.Update(candidates, viewport, new("tiny-gear", true), new()); Assertions.Equal(locked.Id!, "tiny-gear", "manual lock");
        var invalid = engine.Update(candidates.Select(x => x.Id == "tiny-gear" ? x with { ContextValid = false } : x).ToArray(), viewport, new("tiny-gear", true), new()); Assertions.True(invalid.Id != "tiny-gear" && !invalid.ManualLock, "invalid clears lock");
        var offscreen = engine.Update(new[] { new TargetCandidate("off", "item", new(-1, 20), 1) }, viewport, new(), new()); Assertions.True(offscreen.IsGround && offscreen.GroundPoint == focus, "offscreen ground fallback");
        var radius = engine.Update(new[] { new TargetCandidate("edge", "item", focus + new Vec2(180, 0), 1) }, viewport, new(), new()); Assertions.Equal(radius.Id!, "edge", "radius inclusive");
        foreach (var size in new[] { new Rect(0, 0, 1280, 800), new Rect(0, 0, 1920, 1080) }) foreach (var zoom in new[] { .5, 1d, 2d }) Assertions.True(engine.Update(new[] { new TargetCandidate("center", "item", size.Center, 1) }, size, new(), new()).Id == "center", $"projection fixture {size.Width} {zoom}");
    }
}
