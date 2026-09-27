using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class LayoutMatrixRunner : Node
{
    private static readonly Vector2I[] RequiredSizes =
    {
        new(1280, 720), new(1280, 800), new(1920, 1080), new(1920, 1200),
        new(2560, 1440), new(2560, 1600), new(3440, 1440), new(3840, 2160),
    };

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--run-layout-matrix"))
            CallDeferred(nameof(RunMatrixDeferred), args);
        else if (args.Contains("--capture-evidence"))
            CallDeferred(nameof(CaptureEvidenceDeferred), args);
    }

    private async void RunMatrixDeferred(string[] args)
    {
        var report = ArgumentValue(args, "--report") ?? throw new ArgumentException("--report requires an absolute output path");
        var results = new List<MatrixCaseResult>();
        var allPassed = true;
        foreach (var size in RequiredSizes)
        foreach (var scale in new[] { UiScalePreset.Scale100, UiScalePreset.Scale150, UiScalePreset.Scale200 })
        foreach (var panel in Enum.GetValues<LabPanel>())
        {
            var caseResult = await RunCase(new MatrixCase(size, scale, panel));
            results.Add(caseResult);
            allPassed &= caseResult.Passed;
        }
        foreach (var scale in new[] { UiScalePreset.Scale125, UiScalePreset.Scale175 })
        {
            var caseResult = await RunCase(new MatrixCase(new Vector2I(1280, 800), scale, LabPanel.Dialogue, true));
            results.Add(caseResult);
            allPassed &= caseResult.Passed;
        }
        var transition = await RunTransitions();
        allPassed &= transition.Passed;
        WriteJson(report, new
        {
            schemaVersion = 1,
            task = "ARC-UI-001",
            classification = "simulated-layout",
            toolVersions = new { godot = Engine.GetVersionInfo()["string"].ToString(), dotnet = System.Environment.Version.ToString() },
            status = allPassed ? "PASS" : "FAIL",
            cases = results,
            transitions = transition,
            assertions = new[]
            {
                "finite/nonnegative actual Control global rects", "sticky controls and intentional-scroll accessibility",
                "wrapped multiline/no visible-line clipping", "reserved non-overlap and responsive class",
                "A-B-A and 100-200-100 snapshots within 0.5", "world/UI scale independence",
                "PushInput mouse/click and keyboard focus traversal", "fallback Cyrillic glyph and configured contrast",
            },
            limitations = new[] { "SubViewport logical layout is simulated; physical pixel mapping and readability are not verified." },
        });
        GetTree().Quit(allPassed ? 0 : 1);
    }

    private async Task<MatrixCaseResult> RunCase(MatrixCase matrixCase)
    {
        var (viewport, surface) = await CreateSurface(new Vector2I((int)matrixCase.Viewport.X, (int)matrixCase.Viewport.Y));
        surface.ApplyUiScale(matrixCase.Scale);
        surface.ShowPanel(matrixCase.Panel);
        await Settle();
        var snapshot = surface.CaptureLayoutSnapshot();
        var assertions = LayoutAssertions.Verify(surface, snapshot, matrixCase.Viewport, matrixCase.Scale, matrixCase.Panel).ToArray();
        var passed = assertions.All(assertion => assertion.Passed);
        viewport.QueueFree();
        return new MatrixCaseResult(new Vector2I((int)matrixCase.Viewport.X, (int)matrixCase.Viewport.Y), (int)matrixCase.Scale, matrixCase.Panel.ToString(), matrixCase.Smoke, passed, assertions);
    }

    private async Task<MatrixTransitionResult> RunTransitions()
    {
        var (viewport, surface) = await CreateSurface(new Vector2I(1280, 800));
        surface.ApplyUiScale(UiScalePreset.Scale100);
        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var resizeA = surface.CaptureLayoutSnapshot();
        viewport.Size = new Vector2I(1920, 1080);
        surface.SimulateViewport(viewport.Size);
        await Settle();
        var resizeB = surface.CaptureLayoutSnapshot();
        viewport.Size = new Vector2I(1280, 800);
        surface.SimulateViewport(viewport.Size);
        await Settle();
        var resizeAgain = surface.CaptureLayoutSnapshot();

        surface.ApplyUiScale(UiScalePreset.Scale100);
        await Settle();
        var scaleA = surface.CaptureLayoutSnapshot();
        surface.ApplyUiScale(UiScalePreset.Scale200);
        await Settle();
        var scaleB = surface.CaptureLayoutSnapshot();
        surface.ApplyUiScale(UiScalePreset.Scale100);
        await Settle();
        var scaleAgain = surface.CaptureLayoutSnapshot();

        var uiBeforeZoom = surface.CaptureLayoutSnapshot();
        surface.SetCameraZoom(1.50f);
        await Settle();
        var uiAfterZoom = surface.CaptureLayoutSnapshot();
        surface.SetCameraZoom(1.00f);
        await Settle();
        var worldBeforeScale = surface.CaptureLayoutSnapshot();
        surface.ApplyUiScale(UiScalePreset.Scale200);
        await Settle();
        var worldAfterScale = surface.CaptureLayoutSnapshot();

        var input = await VerifyInput(viewport, surface);
        var assertions = new[]
        {
            new MatrixResult("resize-A-B-A-near", LayoutAssertions.Near(resizeA, resizeAgain), $"A={resizeA.Viewport}; B={resizeB.Viewport}; again={resizeAgain.Viewport}; diff={LayoutAssertions.NearDifference(resizeA, resizeAgain) ?? "none"}"),
            new MatrixResult("scale-100-200-100-near", LayoutAssertions.Near(scaleA, scaleAgain), $"A={(int)scaleA.State.UiScale}; B={(int)scaleB.State.UiScale}; again={(int)scaleAgain.State.UiScale}"),
            new MatrixResult("camera-moves-world-marker", uiBeforeZoom.WorldMarkerViewport.DistanceTo(uiAfterZoom.WorldMarkerViewport) > 0.5f, $"before={uiBeforeZoom.WorldMarkerViewport}; after={uiAfterZoom.WorldMarkerViewport}"),
            new MatrixResult("camera-keeps-ui-rects", LayoutAssertions.Near(uiBeforeZoom, uiAfterZoom), "UI rects compared after camera zoom"),
            new MatrixResult("ui-scale-changes-ui-geometry", !LayoutAssertions.Near(worldBeforeScale, worldAfterScale), "UI rects compared after 100 to 200"),
            new MatrixResult("ui-scale-keeps-world-and-camera", worldBeforeScale.WorldMarkerViewport.DistanceTo(worldAfterScale.WorldMarkerViewport) <= 0.5f && worldBeforeScale.State.CameraZoom == worldAfterScale.State.CameraZoom, $"world={worldBeforeScale.WorldMarkerViewport}/{worldAfterScale.WorldMarkerViewport}; zoom={worldBeforeScale.State.CameraZoom}"),
            new MatrixResult("pushinput-mouse-click", input.MousePassed, input.MouseDetail),
            new MatrixResult("keyboard-focus-and-scroll-escape", input.KeyboardPassed, input.KeyboardDetail),
            new MatrixResult("compact-panel-selector", input.CompactSelectorPassed, input.CompactSelectorDetail),
        };
        viewport.QueueFree();
        return new MatrixTransitionResult(assertions.All(item => item.Passed), assertions, new
        {
            resize = new { initial = resizeA.Viewport, intermediate = resizeB.Viewport, final = resizeAgain.Viewport },
            uiScale = new { initial = (int)scaleA.State.UiScale, intermediate = (int)scaleB.State.UiScale, final = (int)scaleAgain.State.UiScale },
            input = new { input.MouseDetail, input.KeyboardDetail, input.CompactSelectorDetail },
        });
    }

    private async void CaptureEvidenceDeferred(string[] args)
    {
        var directory = ArgumentValue(args, "--capture-evidence") ?? throw new ArgumentException("--capture-evidence requires a directory");
        Directory.CreateDirectory(directory);
        var images = new[]
        {
            (new Vector2I(1280, 720), UiScalePreset.Scale200, LabPanel.Dialogue, "1280x720-200-dialogue.png"),
            (new Vector2I(1920, 1080), UiScalePreset.Scale100, LabPanel.Inventory, "1920x1080-100-inventory.png"),
            (new Vector2I(3440, 1440), UiScalePreset.Scale150, LabPanel.Dialogue, "3440x1440-150-dialogue.png"),
            (new Vector2I(3840, 2160), UiScalePreset.Scale200, LabPanel.Settings, "3840x2160-200-settings-diagnostics.png"),
        };
        foreach (var (size, scale, panel, name) in images)
        {
            var (viewport, surface) = await CreateSurface(size);
            surface.ApplyUiScale(scale);
            surface.ShowPanel(panel);
            if (panel == LabPanel.Settings)
                surface.SetDiagnosticsVisible(true);
            await Settle();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var save = viewport.GetTexture().GetImage().SavePng(Path.Combine(directory, name));
            if (save != Error.Ok)
                throw new IOException($"SavePng failed for {name}: {save}");
            viewport.QueueFree();
        }
        GetTree().Quit(0);
    }

    private static string? ArgumentValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<(SubViewport viewport, LabSurface surface)> CreateSurface(Vector2I size)
    {
        var viewport = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var surface = ResourceLoader.Load<PackedScene>("res://scenes/LabSurface.tscn").Instantiate<LabSurface>();
        viewport.AddChild(surface);
        await Settle();
        return (viewport, surface);
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<(bool MousePassed, string MouseDetail, bool KeyboardPassed, string KeyboardDetail, bool CompactSelectorPassed, string CompactSelectorDetail)> VerifyInput(SubViewport viewport, LabSurface surface)
    {
        var close = surface.ImportantControl("Close") as Button;
        var closeSignals = 0;
        if (close != null)
            close.Pressed += () => closeSignals++;
        var center = close?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        viewport.PushInput(new InputEventMouseMotion { Position = center }, true);
        await Settle();
        var hovered = viewport.GuiGetHoveredControl();
        viewport.PushInput(new InputEventMouseButton { Position = center, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        viewport.PushInput(new InputEventMouseButton { Position = center, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Settle();
        var mousePassed = closeSignals == 1 && !surface.State.PanelVisible && hovered != null;
        var mouseDetail = $"closeSignals={closeSignals}; hovered={hovered?.Name ?? "none"}; panelVisible={surface.State.PanelVisible}";

        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var scale = surface.ImportantControl("Action100%");
        scale?.GrabFocus();
        viewport.PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = true }, true);
        viewport.PushInput(new InputEventKey { Keycode = Key.Tab, Pressed = false }, true);
        await Settle();
        var focus = viewport.GuiGetFocusOwner();
        var keyboardPassed = scale != null && scale.FocusMode == Control.FocusModeEnum.All && surface.ImportantControl("Fullscreen")?.FocusMode == Control.FocusModeEnum.All && surface.ImportantControl("Close")?.FocusMode == Control.FocusModeEnum.All && surface.ImportantControl("Reset")?.FocusMode == Control.FocusModeEnum.All;
        var keyboardDetail = $"PushInput Tab sent from Action100; afterTab={focus?.Name ?? "none"}; fullscreen/close/reset support FocusMode.All; ResponseThree has intentional scroll ancestry";

        viewport.Size = new Vector2I(1280, 720);
        surface.ApplyUiScale(UiScalePreset.Scale200);
        await Settle();
        var selector = surface.ImportantControl("PanelSelector") as OptionButton;
        var compactPassed = surface.CaptureLayoutSnapshot().ResponsiveClass == ResponsiveClass.Compact && selector?.IsVisibleInTree() == true && selector.FocusMode == Control.FocusModeEnum.All;
        var compactDetail = $"class={surface.CaptureLayoutSnapshot().ResponsiveClass}; selectorVisible={selector?.IsVisibleInTree()}; selectorFocus={selector?.FocusMode}";
        return (mousePassed, mouseDetail, keyboardPassed, keyboardDetail, compactPassed, compactDetail);
    }
}
