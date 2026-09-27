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
        return new MatrixCaseResult(SizeEvidence.From(matrixCase.Viewport), (int)matrixCase.Scale, matrixCase.Panel.ToString(), matrixCase.Smoke, passed, assertions);
    }

    private async Task<MatrixTransitionResult> RunTransitions()
    {
        var (viewport, surface) = await CreateSurface(new Vector2I(1280, 800));
        surface.ApplyUiScale(UiScalePreset.Scale100);
        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var resizeA = surface.CaptureLayoutSnapshot();
        var resizeAPosition = surface.CameraPosition;
        viewport.Size = new Vector2I(1920, 1080);
        surface.SimulateViewport(viewport.Size);
        await Settle();
        var resizeB = surface.CaptureLayoutSnapshot();
        var resizeBPosition = surface.CameraPosition;
        viewport.Size = new Vector2I(1280, 800);
        surface.SimulateViewport(viewport.Size);
        await Settle();
        var resizeAgain = surface.CaptureLayoutSnapshot();
        var resizeAgainPosition = surface.CameraPosition;

        surface.ApplyUiScale(UiScalePreset.Scale100);
        await Settle();
        var scaleA = surface.CaptureLayoutSnapshot();
        var scaleAPosition = surface.CameraPosition;
        surface.ApplyUiScale(UiScalePreset.Scale200);
        await Settle();
        var scaleB = surface.CaptureLayoutSnapshot();
        var scaleBPosition = surface.CameraPosition;
        surface.ApplyUiScale(UiScalePreset.Scale100);
        await Settle();
        var scaleAgain = surface.CaptureLayoutSnapshot();
        var scaleAgainPosition = surface.CameraPosition;

        var uiBeforeZoom = surface.CaptureLayoutSnapshot();
        var uiBeforeZoomPosition = surface.CameraPosition;
        surface.SetCameraZoom(1.50f);
        await Settle();
        var uiAfterZoom = surface.CaptureLayoutSnapshot();
        var uiAfterZoomPosition = surface.CameraPosition;
        surface.SetCameraZoom(1.00f);
        await Settle();
        var worldBeforeScale = surface.CaptureLayoutSnapshot();
        var worldBeforeScalePosition = surface.CameraPosition;
        surface.ApplyUiScale(UiScalePreset.Scale200);
        await Settle();
        var worldAfterScale = surface.CaptureLayoutSnapshot();
        var worldAfterScalePosition = surface.CameraPosition;

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
            new MatrixResult("ordinary-panel-selector-labels-and-focus", input.OrdinarySelectorPassed, input.OrdinarySelectorDetail),
            new MatrixResult("compact-panel-selector", input.CompactSelectorPassed, input.CompactSelectorDetail),
            new MatrixResult("routed-wheel-scroll-outcomes", input.ScrollPassed, input.ScrollDetail),
            new MatrixResult("routed-middle-drag-pan", input.PanPassed, input.PanDetail),
            new MatrixResult("windowed-escape-closes-panel", input.EscapePassed, input.EscapeDetail),
        };
        viewport.QueueFree();
        return new MatrixTransitionResult(assertions.All(item => item.Passed), assertions, new
        {
            resize = new { initial = SnapshotEvidence.From(resizeA, resizeAPosition), intermediate = SnapshotEvidence.From(resizeB, resizeBPosition), final = SnapshotEvidence.From(resizeAgain, resizeAgainPosition) },
            uiScale = new { initial = SnapshotEvidence.From(scaleA, scaleAPosition), intermediate = SnapshotEvidence.From(scaleB, scaleBPosition), final = SnapshotEvidence.From(scaleAgain, scaleAgainPosition) },
            independence = new { cameraBefore = SnapshotEvidence.From(uiBeforeZoom, uiBeforeZoomPosition), cameraAfter = SnapshotEvidence.From(uiAfterZoom, uiAfterZoomPosition), scaleBefore = SnapshotEvidence.From(worldBeforeScale, worldBeforeScalePosition), scaleAfter = SnapshotEvidence.From(worldAfterScale, worldAfterScalePosition) },
            input = input.Evidence,
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

    private async Task<InputProbe> VerifyInput(SubViewport viewport, LabSurface surface)
    {
        viewport.Size = new Vector2I(1920, 1080);
        surface.SimulateViewport(viewport.Size);
        surface.ApplyUiScale(UiScalePreset.Scale100);
        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var ordinarySelector = surface.ImportantControl("PanelInventory") as Button;
        var ordinarySelectorSignals = 0;
        if (ordinarySelector != null)
            ordinarySelector.Pressed += () => ordinarySelectorSignals++;
        var ordinaryCenter = ordinarySelector?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        viewport.PushInput(new InputEventMouseMotion { Position = ordinaryCenter }, true);
        viewport.PushInput(new InputEventMouseButton { Position = ordinaryCenter, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        viewport.PushInput(new InputEventMouseButton { Position = ordinaryCenter, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Settle();
        var ordinaryTitle = surface.ImportantControl("PanelTitle") as Label;
        var ordinaryPanelSelector = surface.ImportantControl("PanelSelector") as Button;
        var inventoryItem = surface.ImportantControl("Item01") as Button;
        inventoryItem?.GrabFocus();
        PushKey(viewport, Key.Tab);
        await Settle();
        var ordinaryNextFocus = viewport.GuiGetFocusOwner()?.Name.ToString() ?? "none";
        var ordinaryTitleText = ordinaryTitle?.Text;
        var ordinaryPanelSelectorText = ordinaryPanelSelector?.Text;
        var ordinaryPassed = ordinarySelectorSignals == 1
            && surface.State.ActivePanel == LabPanel.Inventory
            && ordinaryTitleText == "Inventory"
            && ordinaryPanelSelectorText == "Panel: Inventory"
            && ordinaryNextFocus.StartsWith("Item", StringComparison.Ordinal);

        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
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
        var hoveredName = hovered?.Name ?? "none";
        var closeHoverAncestry = IsControlOrDescendant(hovered, close);
        var mousePassed = closeSignals == 1 && !surface.State.PanelVisible && closeHoverAncestry;
        var mouseDetail = $"closeSignals={closeSignals}; hovered={hoveredName}; closeAncestry={closeHoverAncestry}; panelVisible={surface.State.PanelVisible}";

        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var selector = surface.ImportantControl("PanelSelector") as Button;
        selector?.GrabFocus();
        var focusNames = new List<string>();
        for (var index = 0; index < 24; index++)
        {
            PushKey(viewport, Key.Tab);
            await Settle();
            focusNames.Add(viewport.GuiGetFocusOwner()?.Name ?? "none");
        }
        var responseOne = surface.ImportantControl("ResponseOne") as Button;
        responseOne?.GrabFocus();
        PushKey(viewport, Key.Tab, shift: true);
        await Settle();
        var shiftedFocus = viewport.GuiGetFocusOwner()?.Name ?? "none";
        var scale = surface.ImportantControl("Action150%") as Button;
        scale?.GrabFocus();
        PushKey(viewport, Key.Enter);
        await Settle();
        var enterScale = surface.State.UiScale == UiScalePreset.Scale150;
        // Applying scale rebuilds the scene-facing controls; reacquire the next control only after it settles.
        var action200 = surface.ImportantControl("Action200%") as Button;
        action200?.GrabFocus();
        PushKey(viewport, Key.Space);
        await Settle();
        var spaceScale = surface.State.UiScale == UiScalePreset.Scale200;
        var response = surface.ImportantControl("ResponseThree");
        var scroll = response != null && surface.IsInsideIntentionalScroll(response);
        var keyboardPassed = focusNames.Any(name => name.StartsWith("Action", StringComparison.Ordinal))
            && new[] { "Fullscreen", "Close", "Reset" }.All(focusNames.Contains)
            && shiftedFocus == "Reset" && enterScale && spaceScale && scroll;
        var keyboardDetail = $"tabSequence={string.Join(",", focusNames)}; shiftTabFromResponseOne={shiftedFocus}; enter150={enterScale}; space200={spaceScale}; responseScroll={scroll}";

        viewport.Size = new Vector2I(1280, 720);
        surface.SimulateViewport(viewport.Size);
        await Settle();
        var compactSelector = surface.ImportantControl("PanelSelector") as Button;
        var selectorSignals = 0;
        if (compactSelector != null)
            compactSelector.Pressed += () => selectorSignals++;
        var selectorCenter = compactSelector?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        viewport.PushInput(new InputEventMouseMotion { Position = selectorCenter }, true);
        viewport.PushInput(new InputEventMouseButton { Position = selectorCenter, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        viewport.PushInput(new InputEventMouseButton { Position = selectorCenter, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Settle();
        var compactSnapshot = surface.CaptureLayoutSnapshot();
        var compactClose = surface.ImportantControl("Close") as Button;
        compactClose?.GrabFocus();
        PushKey(viewport, Key.Enter);
        await Settle();
        var compactTitle = surface.ImportantControl("PanelTitle") as Label;
        var compactPanelSelector = surface.ImportantControl("PanelSelector") as Button;
        var compactLabelsUpdated = compactTitle?.Text == surface.State.ActivePanel.ToString()
            && compactPanelSelector?.Text == $"Panel: {surface.State.ActivePanel}";
        var compactPassed = compactSnapshot.ResponsiveClass == ResponsiveClass.Compact && compactSelector?.IsVisibleInTree() == true && selectorSignals == 1 && surface.State.ActivePanel != LabPanel.Dialogue && compactLabelsUpdated && !surface.State.PanelVisible;
        var compactDetail = $"class={compactSnapshot.ResponsiveClass}; selectorVisible={compactSelector?.IsVisibleInTree()}; pressedSignals={selectorSignals}; active={surface.State.ActivePanel}; title={compactTitle?.Text}; selectorText={compactPanelSelector?.Text}; labelsUpdated={compactLabelsUpdated}; closed={!surface.State.PanelVisible}";

        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var panelScroll = surface.GetNode<ScrollContainer>("UiLayer/Hud/ActivePanel/PanelShell/PanelBodyScroll");
        var verticalBar = panelScroll.GetVScrollBar();
        var verticalBefore = verticalBar.Value;
        var panelPoint = panelScroll.GetGlobalRect().GetCenter();
        viewport.PushInput(new InputEventMouseButton { Position = panelPoint, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 3.0f }, true);
        await Settle();
        var verticalAfter = verticalBar.Value;
        var actionScroll = surface.GetNode<ScrollContainer>("UiLayer/Hud/ActionScroll");
        var horizontalBar = actionScroll.GetHScrollBar();
        var horizontalBefore = horizontalBar.Value;
        var lastVisibleAction = surface.ImportantControl("Action200%") as Button;
        var initiallyOffscreenAction = surface.ImportantControl("Zoom+") as Button;
        var actionViewport = actionScroll.GetGlobalRect();
        var initiallyOffscreen = initiallyOffscreenAction != null && !actionViewport.Encloses(initiallyOffscreenAction.GetGlobalRect());
        lastVisibleAction?.GrabFocus();
        PushKey(viewport, Key.Tab);
        await Settle();
        PushKey(viewport, Key.Tab);
        await Settle();
        var horizontalAfter = horizontalBar.Value;
        var horizontalFocus = viewport.GuiGetFocusOwner()?.Name ?? "none";
        var verticalMin = verticalBar.MinValue;
        var verticalMax = verticalBar.MaxValue;
        var horizontalMin = horizontalBar.MinValue;
        var horizontalMax = horizontalBar.MaxValue;
        var horizontalReached = initiallyOffscreen && horizontalFocus == "Zoom+";
        var scrollPassed = verticalMax > verticalMin && verticalAfter > verticalBefore
            && horizontalMax > horizontalMin && (horizontalAfter > horizontalBefore || horizontalReached);
        var scrollDetail = $"vertical={verticalBefore}->{verticalAfter} range={verticalMin}-{verticalMax}; horizontal={horizontalBefore}->{horizontalAfter} range={horizontalMin}-{horizontalMax}; initiallyOffscreen={initiallyOffscreen}; keyboardFocus={horizontalFocus}";

        viewport.Size = new Vector2I(1920, 1080);
        surface.SimulateViewport(viewport.Size);
        surface.ApplyUiScale(UiScalePreset.Scale100);
        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        var panBefore = surface.CaptureLayoutSnapshot();
        var panCameraBefore = surface.CameraPosition;
        var worldPoint = new Vector2(160, 220);
        viewport.PushInput(new InputEventMouseButton { Position = worldPoint, ButtonIndex = MouseButton.Middle, Pressed = true }, true);
        viewport.PushInput(new InputEventMouseMotion { Position = worldPoint + new Vector2(60, 30), Relative = new Vector2(60, 30) }, true);
        viewport.PushInput(new InputEventMouseButton { Position = worldPoint + new Vector2(60, 30), ButtonIndex = MouseButton.Middle, Pressed = false }, true);
        await Settle();
        var panAfter = surface.CaptureLayoutSnapshot();
        var panCameraAfter = surface.CameraPosition;
        var panPassed = panCameraBefore.DistanceTo(panCameraAfter) > 0.5f
            && panBefore.WorldMarkerViewport.DistanceTo(panAfter.WorldMarkerViewport) > 0.5f
            && LayoutAssertions.Near(panBefore, panAfter);
        var panDetail = $"camera={panCameraBefore}->{panCameraAfter}; marker={panBefore.WorldMarkerViewport}->{panAfter.WorldMarkerViewport}; uiNear={LayoutAssertions.Near(panBefore, panAfter)}";

        surface.ShowPanel(LabPanel.Dialogue);
        await Settle();
        PushKey(viewport, Key.Escape);
        await Settle();
        var escapePassed = !surface.State.PanelVisible;
        var escapeDetail = $"fullscreen=false; panelVisible={surface.State.PanelVisible}";
        var evidence = new RoutedInputEvidence(
            focusNames, shiftedFocus, closeSignals, hoveredName, closeHoverAncestry,
            verticalBefore, verticalAfter, verticalMin, verticalMax,
            horizontalBefore, horizontalAfter, horizontalMin, horizontalMax, initiallyOffscreen, horizontalFocus,
            SnapshotEvidence.From(panBefore, panCameraBefore), SnapshotEvidence.From(panAfter, panCameraAfter),
            escapePassed, compactSnapshot.ResponsiveClass.ToString(), surface.State.ActivePanel.ToString());
        var ordinaryDetail = $"pressedSignals={ordinarySelectorSignals}; activeAfterSelection=Inventory; title={ordinaryTitleText}; selectorText={ordinaryPanelSelectorText}; nextFocus={ordinaryNextFocus}";
        return new InputProbe(mousePassed, mouseDetail, keyboardPassed, keyboardDetail, ordinaryPassed, ordinaryDetail, compactPassed, compactDetail, scrollPassed, scrollDetail, panPassed, panDetail, escapePassed, escapeDetail, evidence);
    }

    private static void PushKey(Viewport viewport, Key key, bool shift = false)
    {
        viewport.PushInput(new InputEventKey { Keycode = key, ShiftPressed = shift, Pressed = true }, true);
        viewport.PushInput(new InputEventKey { Keycode = key, ShiftPressed = shift, Pressed = false }, true);
    }

    private sealed record InputProbe(
        bool MousePassed, string MouseDetail, bool KeyboardPassed, string KeyboardDetail,
        bool OrdinarySelectorPassed, string OrdinarySelectorDetail, bool CompactSelectorPassed, string CompactSelectorDetail, bool ScrollPassed, string ScrollDetail,
        bool PanPassed, string PanDetail, bool EscapePassed, string EscapeDetail, RoutedInputEvidence Evidence);

    private sealed record RoutedInputEvidence(
        IReadOnlyList<string> TabSequence, string ShiftTabFocus, int CloseSignals, string HoveredControl, bool CloseHoverAncestry,
        double VerticalScrollBefore, double VerticalScrollAfter, double VerticalScrollMin, double VerticalScrollMax,
        double HorizontalScrollBefore, double HorizontalScrollAfter, double HorizontalScrollMin, double HorizontalScrollMax, bool InitiallyOffscreenAction, string HorizontalKeyboardFocus,
        SnapshotEvidence PanBefore, SnapshotEvidence PanAfter, bool WindowedEscapeClosed, string CompactClass, string FinalActivePanel);

    private static bool IsControlOrDescendant(Control? hovered, Control? expected)
    {
        for (Node? current = hovered; current != null; current = current.GetParent())
            if (current == expected)
                return true;
        return false;
    }
}
