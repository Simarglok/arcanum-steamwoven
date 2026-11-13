using Arcanum.TerrainViewer.Core;
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

    private Task WindowArtSmokeAsync(string window, AreaInventoryGridArea area, IReadOnlyList<int> arts) =>
        WindowArtSmokeAsync(window, () => arts.Select(number => area.Look?.Art.GetValueOrDefault(number)).OfType<HdInterfaceTexture>().ToArray(), arts, area.DeviceScale,
            () => area.Look is not null);

    // ARC-UI-1195: the same part for any window: its textures (read when asked, after the interface setting is applied), their art numbers, the window's device scale and whether
    // it shows the original's look.
    private async Task WindowArtSmokeAsync(string window, Func<IReadOnlyList<HdInterfaceTexture>> textures, IReadOnlyList<int> arts, Func<float> deviceScale, Func<bool> shown, Vector2I? biggerTo = null)
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
            if (!shown())
            {
                GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the window does not show the original's look");
                throw new InvalidOperationException("ARC-UI-1194 window art smoke: the window does not show the original's look");
            }
            if (mode == "generated")
            {
                var found = textures();
                await VerifyGeneratedTexturesAsync("ARC-UI-1194", window, found, arts, deviceScale, Frames, SetSmokeWindowSizeAsync, Capture, biggerTo);
            }
            else
            {
                // In the legacy and HD runs no frame of the window is generated chrome.
                var drawn = textures();
                if (drawn.Count == 0 || drawn.Count != arts.Count || drawn.Any(texture => texture.IsGenerated))
                {
                    GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the {mode} frames are not generated ({drawn.Count} of {arts.Count} read)");
                    throw new InvalidOperationException("ARC-UI-1194 window art smoke: a " + mode + " frame is generated");
                }
                GD.Print($"ARC-UI-1194 window art {window} {mode} frames={drawn.Count} hd={drawn.Count(texture => texture.IsHd)}");
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

    // ARC-UI-1196: the same opt-in for the open logbook (called by the loot-room smoke after the inventory part). Its drawn frames (the side panel, the book, the page art, the seven
    // tabs: the shown tab's pressed frame, the others' idle one; the page buttons are drawn only with more than one spread, so the renderer's own tests cover them): in the
    // generated mode every one is generated chrome at round(size x paint scale) pixels of the window's own device scale, again after a bigger window; in the HD and Legacy modes none is.
    // ARC_AREA_WINDOW_CAPTURE=PREFIX saves the viewport as PREFIX-logbook-MODE[-big|-back].png (local, never in git).
    private async Task LogbookArtSmokeAsync()
    {
        var mode = System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_ART");
        if (string.IsNullOrEmpty(mode)) return;
        void Fail(string why)
        {
            GD.Print($"ARC-UI-1196 logbook art smoke FAIL {why}");
            throw new InvalidOperationException("ARC-UI-1196 logbook art smoke: " + why);
        }
        if (mode is not ("legacy" or "hd" or "generated")) Fail($"ARC_AREA_WINDOW_ART={mode} is not legacy, hd or generated");
        async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        if (mode != "legacy" && _canvas?.HdSource?.Pack is null) Fail($"the {mode} interface needs ARC_HD_PACK");
        var saved = _gameSettings;
        try
        {
            if (InventoryOpen) EndInventory();
            if (LogbookOpen) CloseLogbookByPlayer();
            _gameSettings = _gameSettings with { HdInterface = mode != "legacy", GeneratedInterface = mode == "generated" };
            SyncInterfaceHd();
            // The logbook builds its textures when its art is read: drop them so the book is read again in the mode under test.
            _logbookArtContent = null; _logbookArtTextures = null;
            await Frames(3);
            OpenLogbook();
            await Frames(3);
            if (!(LogbookOpen && _logbookPanel is { ArtMode: true } panel && _logbookArtTextures is { } art)) { Fail("the logbook shows the original's look"); return; }
            var view = panel.ArtViewForSelfTest;
            var shown = panel.ViewForSelfTest!.Tab;
            var numbers = new List<int> { AreaLogbookArtLayout.SideArt, AreaLogbookArtLayout.BookArt, AreaLogbookArtLayout.PageArt };
            var textures = numbers.Select(number => art[number][0]).ToList();
            for (var i = 0; i < AreaLogbookArtLayout.Tabs.Count; i++)
            {
                var tab = AreaLogbookArtLayout.Tabs[i].Art;
                numbers.Add(tab);
                textures.Add(art[tab][i == (int)shown ? Math.Min(1, art[tab].Length - 1) : 0]);
            }
            async Task Capture(string at)
            {
                if (System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_CAPTURE") is not { Length: > 0 } capture) return;
                await Frames(2);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var path = System.IO.Path.GetFullPath(capture) + $"-logbook-{mode}" + (at == "first" ? "" : "-" + at) + ".png";
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using var image = GetViewport().GetTexture().GetImage();
                if (image is null || image.SavePng(path) != Error.Ok) Fail("readback not saved: " + path);
                GD.Print($"ARC-UI-1196 logbook capture {mode} {at} {image!.GetWidth()}x{image.GetHeight()}");
            }
            if (mode == "generated")
            {
                var hd = textures.Select((texture, i) => texture as HdInterfaceTexture ?? throw new InvalidOperationException($"ARC-UI-1196 logbook art smoke: art {numbers[i]} is no interface texture")).ToArray();
                await VerifyGeneratedTexturesAsync("ARC-UI-1196", "logbook", hd, numbers, view.DeviceScale, Frames, SetSmokeWindowSizeAsync, Capture, new Vector2I(1600, 1200)); // the bars' whole factor (and the logbook's) steps to 2 only from 1200 high
            }
            else
            {
                await Frames(2);
                if (textures.OfType<HdInterfaceTexture>().Any(texture => texture.IsGenerated)) Fail($"a frame is generated in the {mode} run");
                GD.Print($"ARC-UI-1196 logbook art {mode} frames={textures.Count} hd={textures.OfType<HdInterfaceTexture>().Count(texture => texture.IsHd)} generated=0");
                await Capture("first");
            }
        }
        finally
        {
            if (LogbookOpen) CloseLogbookByPlayer();
            _gameSettings = saved;
            SyncInterfaceHd();
            _logbookArtContent = null; _logbookArtTextures = null;
            await Frames(2);
        }
    }
}
