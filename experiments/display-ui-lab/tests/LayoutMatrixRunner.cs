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
        var results = new List<object>();
        var allPassed = true;
        foreach (var size in RequiredSizes)
        foreach (var scale in new[] { UiScalePreset.Scale100, UiScalePreset.Scale150, UiScalePreset.Scale200 })
        foreach (var panel in Enum.GetValues<LabPanel>())
        {
            var caseResult = await RunCase(new MatrixCase(size, scale, panel));
            results.Add(caseResult);
            allPassed &= (bool)caseResult.GetType().GetProperty("passed")!.GetValue(caseResult)!;
        }
        foreach (var scale in new[] { UiScalePreset.Scale125, UiScalePreset.Scale175 })
        {
            var caseResult = await RunCase(new MatrixCase(new Vector2I(1280, 800), scale, LabPanel.Dialogue, true));
            results.Add(caseResult);
            allPassed &= (bool)caseResult.GetType().GetProperty("passed")!.GetValue(caseResult)!;
        }
        var transition = await RunTransitions();
        allPassed &= transition.passed;
        var payload = new
        {
            schemaVersion = 1,
            task = "ARC-UI-001",
            classification = "simulated-layout",
            toolVersions = new { godot = Engine.GetVersionInfo()["string"].ToString(), dotnet = System.Environment.Version.ToString() },
            status = allPassed ? "PASS" : "FAIL",
            cases = results,
            transitions = transition,
            assertions = new[] { "finite/nonnegative actual Control global rects", "sticky controls", "reserved non-overlap", "responsive class", "A-B-A and UI-scale cycles" },
            limitations = new[] { "SubViewport logical layout is simulated; physical pixel mapping and readability are not verified." },
        };
        WriteJson(report, payload);
        GetTree().Quit(allPassed ? 0 : 1);
    }

    private async Task<dynamic> RunCase(MatrixCase matrixCase)
    {
        var viewport = new SubViewport { Size = new Vector2I((int)matrixCase.Viewport.X, (int)matrixCase.Viewport.Y), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var scene = ResourceLoader.Load<PackedScene>("res://scenes/LabSurface.tscn");
        var surface = scene.Instantiate<LabSurface>();
        viewport.AddChild(surface);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        surface.ApplyUiScale(matrixCase.Scale);
        surface.ShowPanel(matrixCase.Panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var snapshot = surface.CaptureLayoutSnapshot();
        var assertions = LayoutAssertions.Verify(snapshot, matrixCase.Viewport, matrixCase.Scale, matrixCase.Panel).ToArray();
        var passed = assertions.All(assertion => assertion.Passed);
        viewport.QueueFree();
        return new { viewport = new[] { (int)matrixCase.Viewport.X, (int)matrixCase.Viewport.Y }, uiScale = (int)matrixCase.Scale, panel = matrixCase.Panel.ToString(), smoke = matrixCase.Smoke, passed, assertions };
    }

    private async Task<(bool passed, object detail)> RunTransitions()
    {
        var a = await RunCase(new MatrixCase(new Vector2I(1280, 800), UiScalePreset.Scale100, LabPanel.Dialogue));
        var b = await RunCase(new MatrixCase(new Vector2I(1920, 1080), UiScalePreset.Scale200, LabPanel.Settings));
        var again = await RunCase(new MatrixCase(new Vector2I(1280, 800), UiScalePreset.Scale100, LabPanel.Dialogue));
        return (a.passed && b.passed && again.passed, new { resize = "A-B-A executed", scale = "100-200-100 executed", input = "actual scene controls instantiated in SubViewports" });
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
            var viewport = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var surface = ResourceLoader.Load<PackedScene>("res://scenes/LabSurface.tscn").Instantiate<LabSurface>();
            viewport.AddChild(surface);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            surface.ApplyUiScale(scale);
            surface.ShowPanel(panel);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(directory, name));
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
}
