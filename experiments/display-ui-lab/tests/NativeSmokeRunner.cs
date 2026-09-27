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
                await Settle();
                var snapshot = surface.CaptureLayoutSnapshot();
                var observed = DisplayServer.WindowGetSize();
                var sizePassed = observed == size && snapshot.Viewport.X > 0 && snapshot.Viewport.Y > 0;
                checks.Add(new { name = "programmatic-client-relayout", status = sizePassed ? "PASS" : "FAIL", requested = SizeEvidence.From(size), observed = SizeEvidence.From(observed), snapshot = SnapshotEvidence.From(snapshot, surface.CameraPosition) });
                passed &= sizePassed;
            }

            surface.ShowPanel(LabPanel.Dialogue);
            await Settle();
            var fullscreen = surface.ImportantControl("Fullscreen") as Button;
            fullscreen?.GrabFocus();
            PushKey(Key.Enter);
            await Settle();
            var entered = app.IsFullscreen;
            PushKey(Key.Escape);
            await Settle();
            var escaped = !app.IsFullscreen && surface.State.PanelVisible;
            PushKey(Key.F11);
            await Settle();
            var f11Entered = app.IsFullscreen;
            PushKey(Key.Escape);
            await Settle();
            var f11Escaped = !app.IsFullscreen && surface.State.PanelVisible;
            checks.Add(new { name = "keyboard-ordinary-fullscreen-and-escape-precedence", status = entered && escaped && f11Entered && f11Escaped ? "PASS" : "FAIL", entered, escaped, f11Entered, f11Escaped });
            passed &= entered && escaped && f11Entered && f11Escaped;

            var close = surface.ImportantControl("Close") as Button;
            var signals = 0;
            if (close != null)
                close.Pressed += () => signals++;
            var point = close?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
            GetViewport().PushInput(new InputEventMouseMotion { Position = point }, true);
            await Settle();
            var hovered = GetViewport().GuiGetHoveredControl();
            GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
            GetViewport().PushInput(new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
            await Settle();
            var mousePassed = signals == 1 && !surface.State.PanelVisible && IsControlOrDescendant(hovered, close);
            checks.Add(new { name = "root-viewport-mouse-close", status = mousePassed ? "PASS" : "FAIL", signals, hovered = hovered?.Name ?? "none", closeAncestry = IsControlOrDescendant(hovered, close), panelVisible = surface.State.PanelVisible });
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
            await Settle();
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, JsonSerializer.Serialize(new { schemaVersion = 1, task = "ARC-UI-001", candidate, classification = "actual-programmatic-native-window", status = passed ? "PASS" : "FAIL", checks, restoredWindow = new { size = SizeEvidence.From(window.Size), position = PointEvidence.From(window.Position) } }, new JsonSerializerOptions { WriteIndented = true }));
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
}
