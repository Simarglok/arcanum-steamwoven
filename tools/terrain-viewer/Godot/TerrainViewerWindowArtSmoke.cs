using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-1194, ARC-UI-3143: opt-in part of the native barter, container and loot-room smokes (ARC_AREA_WINDOW_ART=legacy|hd, with ARC_HD_PACK for hd; the
    // run is otherwise unchanged). With the open window's frames (the arts of its look) it turns the HD interface on or off, checks that the frames are the pack's redraw (hd) or
    // the Legacy art (legacy), saves the viewport as PREFIX-WINDOW-MODE.png when ARC_AREA_WINDOW_CAPTURE=PREFIX (local, never in git), and puts the settings back.
    // A window without the variable is left alone. The generated mode of ARC-UI-140 is retired (ARC-UI-3143).
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
        if (mode is not ("legacy" or "hd"))
        {
            GD.Print($"ARC-UI-1194 window art smoke FAIL ARC_AREA_WINDOW_ART={mode} is not legacy or hd");
            throw new InvalidOperationException("ARC-UI-1194 window art smoke: ARC_AREA_WINDOW_ART must be legacy or hd");
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
            SteamwovenHud.Suspended = true; // ARC-UI-2452: these checks measure the whole-factor geometry
            _gameSettings = _gameSettings with { HdInterface = mode != "legacy" };
            SyncInterfaceHd();
            await Frames(3);
            if (!shown())
            {
                GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the window does not show the original's look");
                throw new InvalidOperationException("ARC-UI-1194 window art smoke: the window does not show the original's look");
            }
            var drawn = textures();
            if (mode == "hd" && (drawn.Count == 0 || drawn.Count != arts.Count))
            {
                GD.Print($"ARC-UI-1194 window art smoke FAIL {window}: the hd frames are not all read ({drawn.Count} of {arts.Count})");
                throw new InvalidOperationException("ARC-UI-1194 window art smoke: an hd frame is missing");
            }
            GD.Print($"ARC-UI-1194 window art {window} {mode} frames={drawn.Count} hd={drawn.Count(texture => texture.IsHd)}");
            await Capture("first");
        }
        finally
        {
            _gameSettings = saved;
            SyncInterfaceHd();
            SteamwovenHud.Suspended = false; _gameBarPanel?.Relayout();
            await Frames(2);
        }
    }

    // ARC-UI-1196, ARC-UI-3143: the same opt-in for the open logbook (called by the loot-room smoke after the inventory part). Its drawn frames (the side panel, the book, the page art, the seven
    // tabs: the shown tab's pressed frame, the others' idle one) are the pack's redraw in the hd mode and the Legacy art in the legacy mode.
    // ARC_AREA_WINDOW_CAPTURE=PREFIX saves the viewport as PREFIX-logbook-MODE.png (local, never in git).
    private async Task LogbookArtSmokeAsync()
    {
        var mode = System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_ART");
        if (string.IsNullOrEmpty(mode)) return;
        void Fail(string why)
        {
            GD.Print($"ARC-UI-1196 logbook art smoke FAIL {why}");
            throw new InvalidOperationException("ARC-UI-1196 logbook art smoke: " + why);
        }
        if (mode is not ("legacy" or "hd")) Fail($"ARC_AREA_WINDOW_ART={mode} is not legacy or hd");
        async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        if (mode != "legacy" && _canvas?.HdSource?.Pack is null) Fail($"the {mode} interface needs ARC_HD_PACK");
        var saved = _gameSettings;
        try
        {
            if (InventoryOpen) EndInventory();
            if (LogbookOpen) CloseLogbookByPlayer();
            SteamwovenHud.Suspended = true; // ARC-UI-2452: these checks measure the whole-factor geometry
            _gameSettings = _gameSettings with { HdInterface = mode != "legacy" };
            SyncInterfaceHd();
            // The logbook builds its textures when its art is read: drop them so the book is read again in the mode under test.
            _logbookArtContent = null; _logbookArtTextures = null;
            await Frames(3);
            OpenLogbook();
            await Frames(3);
            if (!(LogbookOpen && _logbookPanel is { ArtMode: true } panel && _logbookArtTextures is { } art)) { Fail("the logbook shows the original's look"); return; }
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
            await Frames(2);
            GD.Print($"ARC-UI-1196 logbook art {mode} frames={textures.Count} hd={textures.OfType<HdInterfaceTexture>().Count(texture => texture.IsHd)}");
            await Capture("first");
        }
        finally
        {
            if (LogbookOpen) CloseLogbookByPlayer();
            _gameSettings = saved;
            SyncInterfaceHd();
            SteamwovenHud.Suspended = false; _gameBarPanel?.Relayout();
            _logbookArtContent = null; _logbookArtTextures = null;
            await Frames(2);
        }
    }
}
