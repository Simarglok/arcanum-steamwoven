using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1194: opt-in part of the native barter, container and loot-room smokes (ARC_AREA_WINDOW_ART=legacy|hd|generated, with ARC_HD_PACK for the last two; the
    // run is otherwise unchanged). With the open window's frames (the arts of its look) it turns the HD and generated interface on or off, checks in generated mode that
    // every frame draws generated chrome at the window's own device scale and again after a resize (VerifyGeneratedWindowAsync), saves the viewport (first size, bigger window, back) as
    // PREFIX-WINDOW-MODE[-STAGE].png when ARC_AREA_WINDOW_CAPTURE=PREFIX (local, never in git), and puts the settings back. A window without the variable is left alone.
    private static bool WindowArtSmokeRequested => System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_ART") is { Length: > 0 };

    private async Task WindowArtSmokeAsync(string window, AreaInventoryGridArea area, IReadOnlyList<int> arts)
    {
        var mode = System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_ART");
        if (string.IsNullOrEmpty(mode)) return;
        if (mode is not ("legacy" or "hd" or "generated"))
        {
            GD.Print($"ARC-UI-1194 window art smoke FAIL ARC_AREA_WINDOW_ART={mode} is not legacy, hd or generated");
            throw new InvalidOperationException("ARC-UI-1194 window art smoke: ARC_AREA_WINDOW_ART must be legacy, hd or generated");
        }
        async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var saved = _gameSettings;
        // ARC_AREA_WINDOW_CAPTURE=PREFIX saves the viewport after a drawn frame as PREFIX-WINDOW-MODE[-STAGE].png (local, never in git).
        async Task Capture(string stage)
        {
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_CAPTURE") is not { Length: > 0 } capture) return;
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var path = System.IO.Path.GetFullPath(capture) + $"-{window}-{mode}" + (stage == "first" ? "" : "-" + stage) + ".png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1194 window readback not saved: " + path);
            GD.Print($"ARC-UI-1194 window capture {window} {mode} {stage} {image.GetWidth()}x{image.GetHeight()}");
        }
        try
        {
            if (mode != "legacy" && _canvas?.HdSource?.Pack is null)
            {
                GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the {mode} interface needs ARC_HD_PACK");
                throw new InvalidOperationException("ARC-UI-1194 window art smoke: HD verification needs ARC_HD_PACK");
            }
            _gameSettings = _gameSettings with { HdInterface = mode != "legacy", GeneratedInterface = mode == "generated" };
            SyncInterfaceHd();
            await Frames(3);
            if (mode == "generated") await VerifyGeneratedWindowAsync("ARC-UI-1194", window, area, arts, Frames, SetSmokeWindowSizeAsync, Capture);
            else
            {
                // In the legacy and HD runs no frame of the window is generated chrome.
                var drawn = arts.Select(number => area.Look?.Art.GetValueOrDefault(number)).OfType<HdInterfaceTexture>().ToArray();
                if (drawn.Length != arts.Count || drawn.Any(texture => texture.IsGenerated))
                {
                    GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the {mode} frames are not generated ({drawn.Length} of {arts.Count} read)");
                    throw new InvalidOperationException("ARC-UI-1194 window art smoke: a " + mode + " frame is generated");
                }
                GD.Print($"ARC-UI-1194 window art {window} {mode} frames={drawn.Length} hd={drawn.Count(texture => texture.IsHd)}");
                await Capture("first");
            }
        }
        finally
        {
            _gameSettings = saved;
            SyncInterfaceHd();
            await Frames(2);
        }
    }
}
