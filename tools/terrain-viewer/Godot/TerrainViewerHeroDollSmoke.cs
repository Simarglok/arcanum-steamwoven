using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-1918: the hero's own doll toggle of a window of modes 1 to 4 on the original art (the real-data smokes <c>loot-worn</c>, <c>pickpocket</c>, <c>barter</c>, <c>item-aim</c>): the shared steps of
    /// <see cref="HeroDollToggleForSelfTest"/> when the cover and the buttons read, a named "not driven" line when they do not. <c>ARC_AREA_HERO_DOLL_CAPTURE=PATH.png</c> also saves the window three ways
    /// beside PATH (-before: without the toggle's art, as before the change; -after: the buttons, the grid; -doll: the toggle set) for the graphics report; never committed.
    /// </summary>
    private async Task<string> HeroDollWindowStepAsync(AreaInventoryGridArea grid, AreaInventoryView view, string window, bool picks = true)
    {
        if (!grid.HeroTogglesShown) return $"hero doll toggle not driven ({window}: the cover or the buttons' art does not read)";
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_HERO_DOLL_CAPTURE") is { Length: > 0 } capture) await CaptureHeroDollAsync(grid, capture, window);
        return HeroDollToggleForSelfTest(grid, view, window, picks);
    }

    private async Task CaptureHeroDollAsync(AreaInventoryGridArea grid, string path, string window)
    {
        async Task Shot(string suffix)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(System.IO.Path.ChangeExtension(path, null) + "-" + window + "-" + suffix + ".png") != Error.Ok) throw new InvalidOperationException("AREA_HERO_DOLL_CAPTURE_FAILED");
        }
        var look = grid.Look!;
        grid.SetHeroDoll(false);
        grid.SetLook(look with { Art = look.Art.Where(entry => entry.Key != AreaHeroDollToggle.CoverArt).ToDictionary(entry => entry.Key, entry => entry.Value) });
        await Shot("before");
        grid.SetLook(look);
        await Shot("after");
        grid.SetHeroDoll(true);
        await Shot("doll");
        grid.SetHeroDoll(false);
    }
}
