using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-UI-142 native observation (inside the stage "tech-craft", whose hero has Herbology degrees and the game's data): the schematic window opens in the original's
    // look (art 365, the round view's lens), a click on a discipline tab, the page buttons and the list tabs acts as the art buttons do, the previous button is gone on the
    // first page, the pointer over the assemble button writes its text, and a release in the lens closes the window. ARC_AREA_SCHEMATIC_CAPTURE=PATH.png saves readbacks
    // (PATH-original.png and PATH-diagnostic.png, the same page in both looks).
    private async Task TechSmokeSchematicLookAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-142 schematic window smoke: " + why); }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (var i = 0; i < 1800 && AdoptSchematicArt() is null; i++) await Frame();
        var content = AdoptSchematicArt();
        Require(content is { Ready: true }, "the window's art read: " + content?.Detail);
        // ARC_AREA_SCHEMATIC_HD_VERIFY=1 (with ARC_HD_PACK naming a pack that holds the window's arts): the HD interface is switched on for this look, every window art
        // frame the pack holds resolves to its HD frame, and a PATH-hd.png capture is saved; the switch is put back at the end.
        var verifyHd = System.Environment.GetEnvironmentVariable("ARC_AREA_SCHEMATIC_HD_VERIFY") == "1";
        var savedInterfaceHd = _gameSettings.HdInterface;
        if (verifyHd)
        {
            Require(_canvas?.HdSource?.Pack is not null, "HD verification needs ARC_HD_PACK");
            _gameSettings = _gameSettings with { HdInterface = true };
            SyncInterfaceHd();
            Require(InterfaceHd.Enabled, "the HD interface switch is on for this look");
            var resolved = 0;
            foreach (var load in content!.Art.Values.Where(load => AreaSchematicWindowLayout.ArtNumbers.Contains(load.Number)))
                foreach (var frame in load.Frames.Where(f => f.Hd is not null))
                    if (_canvas!.HdSource!.Pack!.ResolveSprite(frame.Hd!, frame.Rgba).Status == Arcanum.Data.Hd.HdFrameStatus.Hd) resolved++;
            Require(resolved > 0, "the pack holds HD frames of the window's arts");
            GD.Print($"ARC-UI-142 schematic window HD frames resolved={resolved}");
        }
        var pages = 0;
        try {
        OpenSchematics();
        Require(SchematicsOpen && _schematicsPanel is { ArtMode: true, ArtViewForSelfTest.Visible: true }, "the window opens in the original's look: " + _schematicsStatus);
        var panel = _schematicsPanel!;
        var art = panel.ArtViewForSelfTest;
        await Frame();
        async Task Click(double x, double y, bool release = true)
        {
            var at = art.PointForSelfTest(x, y);
            foreach (var pressed in release ? new[] { true, false } : new[] { false })
            {
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0 });
                await Frame();
            }
        }
        async Task Move(double x, double y)
        {
            var at = art.PointForSelfTest(x, y);
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Frame();
        }
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SCHEMATIC_CAPTURE");
        async Task Capture(string suffix)
        {
            if (string.IsNullOrEmpty(capture)) return;
            await Frame();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            if (shot.SavePng(capture.Replace(".png", suffix + ".png")) != Error.Ok) throw new InvalidOperationException("ARC-UI-142 schematic window smoke: capture failed");
        }
        Require(panel.View is { Page: 0 }, "the window opens on page 0");
        // The previous button is hidden on page 0: a click on its place does nothing.
        var (px, py) = AreaSchematicWindowLayout.Previous;
        await Move(px + 2, py + 2);
        Require(art.HoveredForSelfTest is null, "the previous button is hidden on the first page and takes no pointer");
        var soundsBefore = _schematicSounds.Count;
        await Click(px + 2, py + 2);
        Require(panel.View is { Page: 0 } && _schematicSounds.Count == soundsBefore, "a click on the hidden previous button does nothing");
        await Capture(verifyHd ? "-hd" : "-original");
        // A discipline tab: Chemistry (1) then back to Herbology (0): the tab keeps the page 0 and the window follows.
        var (dx, dy) = AreaSchematicWindowLayout.DisciplineTab(1);
        await Click(dx + 4, dy + 4);
        Require(_schematicsPanel!.View is { Discipline: 1, Page: 0 }, $"the art tab selects discipline 1 (shows {panel.View?.Discipline})");
        (dx, dy) = AreaSchematicWindowLayout.DisciplineTab(0);
        await Click(dx + 4, dy + 4);
        Require(panel.View is { Discipline: 0, Page: 0 }, $"the art tab selects discipline 0 (shows {panel.View?.Discipline})");
        // The next button turns the page and the previous one comes back.
        pages = panel.View!.Pages;
        if (pages > 1)
        {
            var (nx, ny) = AreaSchematicWindowLayout.Next;
            await Click(nx + 4, ny + 4);
            Require(panel.View is { Page: 1 }, "the next button turns to page 1");
            await Click(px + 4, py + 4);
            Require(panel.View is { Page: 0 }, "the previous button turns back to page 0");
        }
        // The found tab acts from the learned tab, the learned tab brings it back.
        var (fx, fy) = AreaSchematicWindowLayout.ListTab(found: true);
        await Click(fx + 4, fy + 4);
        Require(panel.View is { Tab: AreaSchematicTab.Found }, "the found tab shows the found list");
        (fx, fy) = AreaSchematicWindowLayout.ListTab(found: false);
        await Click(fx + 4, fy + 4);
        Require(panel.View is { Tab: AreaSchematicTab.Learned }, "the learned tab shows the learned list");
        // The pointer over the assemble button writes its text (schematic_text.mes 3) to the text bar.
        var (ax, ay) = AreaSchematicWindowLayout.Assemble;
        await Move(ax + 4, ay + 4);
        Require(_messageLog.Shown is { Kind: AreaMessageKind.Hover } && art.HoveredForSelfTest is not null, "the pointer over assemble writes a hover text");
        await Move(5, 395);
        // The shown record's components are put in the pack: both slots turn to found and the button to can-assemble; a click on the button assembles.
        var data = _schematicData!.Data!;
        var shownRecord = data.Catalog.Read(panel.View!.Record).Record!;
        TechSmokeGive(data, TechSmokeCheapest(data, shownRecord.Component1.Bases));
        TechSmokeGive(data, TechSmokeCheapest(data, shownRecord.Component2.Bases));
        SyncAreaSchematics(force: true);
        await Frame();
        Require(panel.View is { Component1.Found: true, Component2.Found: true, Button: AssemblyButton.CanAssemble }, $"the slots turn to found (button {panel.View?.Button})");
        await Capture((verifyHd ? "-hd" : "-original") + "-found");
        await Click(ax + 4, ay + 4);
        Require(_schematicMessage is { Length: > 0 } text && !text.StartsWith("not assembled", StringComparison.Ordinal), "a click on assemble assembled: " + _schematicMessage);
        Require(_messageLog.Shown is { Kind: AreaMessageKind.Message }, "the completion text is written to the text bar");
        // The same page in the diagnostic layout.
        panel.DiagnosticLook = true;
        Render();
        await Frame();
        Require(!panel.ArtMode && panel.ArtViewForSelfTest.Visible == false, "the diagnostic layout shows when asked");
        await Capture(verifyHd ? "-hd-diagnostic" : "-diagnostic");
        panel.DiagnosticLook = false;
        Render();
        await Frame();
        Require(panel.ArtMode, "the original look comes back");
        // A release in the lens closes the window (0x56D530, the point test 0x551000).
        var (lx, ly, side, _) = AreaSchematicWindowLayout.RoundView;
        await Click(lx + side / 2.0, ly + side / 2.0);
        Require(!SchematicsOpen, "a release in the lens closes the window");
        }
        finally
        {
            if (verifyHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd }; SyncInterfaceHd(); }
        }
        GD.Print($"ARC-UI-142 native-smoke schematic-look PASS art={content!.Detail} pages={pages}");
    }
}
