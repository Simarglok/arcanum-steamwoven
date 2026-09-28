using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class NativeSmokeRunner : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--run-native-smoke") >= 0)
            CallDeferred(nameof(RunDeferred), args);
    }

    private async void RunDeferred(string[] args)
    {
        var report = ArgumentValue(args, "--report") ?? throw new ArgumentException("--run-native-smoke requires --report");
        var candidate = ArgumentValue(args, "--candidate") ?? "unknown";
        var app = GetNode<DisplayUiLabApp>("..");
        var surface = GetNode<LabSurface>("../LabSurface");
        var window = GetWindow();
        var originalSize = window.Size;
        var originalPosition = window.Position;
        var checks = new List<object>();
        var passed = true;
        try
        {
            foreach (var size in new[] { new Vector2I(1100, 740), new Vector2I(1280, 800) })
            {
                window.Size = size;
                var settled = await WaitUntil(() => DisplayServer.WindowGetSize() == size);
                // Window-size acknowledgement precedes container sorting on macOS. Sample
                // only after bounded frame settlement so hidden Medium selectors have reflowed.
                await Settle();
                var snapshot = surface.CaptureLayoutSnapshot();
                var observed = DisplayServer.WindowGetSize();
                var requiredStickyControlsInViewport = RequiredStickyControlsInViewport(surface, snapshot.Viewport);
                var sizePassed = settled && observed == size && snapshot.Viewport.X > 0 && snapshot.Viewport.Y > 0 && requiredStickyControlsInViewport;
                checks.Add(new { name = "programmatic-client-relayout", status = sizePassed ? "PASS" : "FAIL", requested = SizeEvidence.From(size), observed = SizeEvidence.From(observed), requiredStickyControlsInViewport, snapshot = SnapshotEvidence.From(snapshot, surface.CameraPosition) });
                passed &= sizePassed;
            }

            surface.ShowPanel(LabPanel.Dialogue);
            await Settle();
            var fullscreen = surface.ImportantControl("Fullscreen") as Button;
            fullscreen?.GrabFocus();
            PushKey(Key.Enter);
            var entered = await WaitUntil(() => app.IsFullscreen);
            PushKey(Key.Escape);
            var escaped = await WaitUntil(() => !app.IsFullscreen && window.Size == app.SavedWindowSize && window.Position == app.SavedWindowPosition) && surface.State.PanelVisible;
            PushKey(Key.F11);
            var f11Entered = await WaitUntil(() => app.IsFullscreen);
            PushKey(Key.Escape);
            var f11Escaped = await WaitUntil(() => !app.IsFullscreen && window.Size == app.SavedWindowSize && window.Position == app.SavedWindowPosition) && surface.State.PanelVisible;
            PushKey(Key.Escape);
            var secondEscapeClosedPanel = await WaitUntil(() => !surface.State.PanelVisible);
            var fullscreenPassed = entered && escaped && f11Entered && f11Escaped && secondEscapeClosedPanel;
            checks.Add(new { name = "keyboard-ordinary-fullscreen-and-escape-precedence", status = fullscreenPassed ? "PASS" : "FAIL", entered, escaped, f11Entered, f11Escaped, secondEscapeClosedPanel });
            passed &= fullscreenPassed;

            surface.ShowPanel(LabPanel.Dialogue);
            surface.ApplyUiScale(UiScalePreset.Scale200);
            surface.SetCameraZoom(1.50f);
            await Settle();
            window.Size = new Vector2I(1100, 740);
            await WaitUntil(() => DisplayServer.WindowGetSize() == new Vector2I(1100, 740));
            var reset = surface.ImportantControl("Reset") as Button;
            var resetSignals = 0;
            if (reset != null)
                reset.Pressed += () => resetSignals++;
            reset?.GrabFocus();
            PushKey(Key.Enter);
            var screen = DisplayServer.WindowGetCurrentScreen();
            var usable = DisplayServer.ScreenGetUsableRect(screen);
            var expectedResetSize = new Vector2I(Mathf.Min(1280, usable.Size.X), Mathf.Min(800, usable.Size.Y));
            var resetPassed = await WaitUntil(() => !app.IsFullscreen
                && window.Size == expectedResetSize
                && surface.State.UiScale == UiScalePreset.Scale100
                && Mathf.IsEqualApprox(surface.State.CameraZoom, 1.0f)
                && surface.State.PanelVisible);
            checks.Add(new { name = "keyboard-reset-after-mutated-window-scale-and-zoom", status = resetPassed ? "PASS" : "FAIL", resetSignals, expectedLogicalWindow = SizeEvidence.From(expectedResetSize), observedLogicalWindow = SizeEvidence.From(window.Size), reportedClient = SizeEvidence.From(DisplayServer.WindowGetSize()), state = StateEvidence.From(surface.State) });
            passed &= resetPassed;

            surface.ShowPanel(LabPanel.Dialogue);
            await Settle();
            var close = surface.ImportantControl("Close") as Button;
            var signals = 0;
            if (close != null)
                close.Pressed += () => signals++;
            var point = close?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Settle();
            var hovered = GetViewport().GuiGetHoveredControl();
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            await Settle();
            var mousePassed = signals == 1 && !surface.State.PanelVisible && IsControlOrDescendant(hovered, close);
            checks.Add(new { name = "root-viewport-mouse-close", status = mousePassed ? "PASS" : "FAIL", signals, hovered = hovered?.Name.ToString() ?? "none", closeAncestry = IsControlOrDescendant(hovered, close), panelVisible = surface.State.PanelVisible });
            passed &= mousePassed;
        }
        catch (Exception exception)
        {
            passed = false;
            checks.Add(new { name = "native-smoke-exception", status = "FAIL", detail = exception.Message });
        }
        finally
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            window.Size = originalSize;
            window.Position = originalPosition;
            var restored = await WaitUntil(() => window.Size == originalSize && window.Position == originalPosition);
            checks.Add(new { name = "finally-restores-original-window-bounds", status = restored ? "PASS" : "FAIL", expectedSize = SizeEvidence.From(originalSize), expectedPosition = PointEvidence.From(originalPosition), observedSize = SizeEvidence.From(window.Size), observedPosition = PointEvidence.From(window.Position) });
            passed &= restored;
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, JsonSerializer.Serialize(new { schemaVersion = 1, task = "ARC-UI-001", candidate, classification = "actual-programmatic-native-window", status = passed ? "PASS" : "FAIL", originalWindow = new { size = SizeEvidence.From(originalSize), position = PointEvidence.From(originalPosition) }, checks, restoredWindow = new { size = SizeEvidence.From(window.Size), position = PointEvidence.From(window.Position), matchesOriginal = restored } }, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit(passed ? 0 : 1);
        }
    }

    private void PushKey(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = false }, true);
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<bool> WaitUntil(Func<bool> condition, int maximumFrames = 180)
    {
        for (var frame = 0; frame < maximumFrames; frame++)
        {
            if (condition())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return condition();
    }

    private static string? ArgumentValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsControlOrDescendant(Control? hovered, Control? expected)
    {
        for (Node? current = hovered; current != null; current = current.GetParent())
            if (current == expected)
                return true;
        return false;
    }

    private static bool RequiredStickyControlsInViewport(LabSurface surface, Vector2 viewport)
    {
        foreach (var id in new[] { "Fullscreen", "PanelSelector", "PanelTitle", "Close", "Reset" })
        {
            var control = surface.ImportantControl(id);
            if (control == null || !control.IsVisibleInTree())
                return false;
            var rect = control.GetGlobalRect();
            if (rect.Position.X < 0 || rect.Position.Y < 0 || rect.End.X > viewport.X || rect.End.Y > viewport.Y)
                return false;
        }
        return true;
    }
}
