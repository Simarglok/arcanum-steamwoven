using System.Text.Json;
using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

// Diagnostic-only selected tuple: no matrix/export PASS, verifier scroll write,
// production pass-budget change or behavior correction.
public partial class LabRoot
{
    private bool _d4SlotDiagnostic;
    private object? _d4SlotOriginalFailure;
    private sealed class D4SlotPrefixComplete : Exception { }
    private async Task<object> TraceD4Slot(string directory)
    {
        _d4SlotDiagnostic = true;
        string? failure = null;
        try { await VerifyTuple(new(1280, 800), "contextual", 100, .5f, directory); }
        catch (D4SlotPrefixComplete) { }
        catch (Exception e) { failure = e.ToString(); GD.PushError(e.ToString()); }
        return new { experimentComplete = true, status = failure is null ? "passed-scoped-oracle-only" : "failed-scoped-oracle",
            acceptanceClaimed = false, error = failure, originalFailure = _d4SlotOriginalFailure,
            activeStep = _activeMatrixStep, trace = _hud.D4ScrollTrace,
            coverage = "original first tuple prehistory and Settings/Context/slot-01/Slot title and row prefix only",
            isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage };
    }
    private async Task<object> TraceD4Settings(string directory)
    {
        _injectedObservations = true; SetProcess(false); SetProcessInput(false);
        _focused = true; _settings = LabSettings.Default with { ActivePresetId = "contextual", UiScalePercent = 100 };
        _hud.SetScale(100); _camera.Zoom = new(.5f, .5f); _input.SetStartupDevices([3]);
        ClearWorldIntents("SIMULATION D4 diagnostic tuple"); VerifyNeutral();
        _cameraState = new(CameraMode.Manual, false, false); _cameraCenter = CameraBounds().ClampCenter(new(635, 475)); ApplyCameraCenter();
        VerifyTap(JoyButton.RightStick); VerifyTap(JoyButton.RightStick);
        await LayoutFrames();
        VerificationMode.Require(GetViewport().GetVisibleRect().Size == new Vector2(1280, 800), "D4 actual viewport mismatch");
        _hud.EnableD4Trace();
        _hud.ObserveD4Scroll("input.Menu.before", "verifier invoking production Handle only");
        VerifyTap(JoyButton.Start); VerifyNeutral();
        VerificationMode.Require(_hud.ModalKind == ModalKind.Settings, "D4 native Menu did not enter Settings");
        await RenderedFrame(); await RenderedFrame();
        var title = _hud.ModalTitle;
        var titleEnclosed = _hud.UsableModalClip.Encloses(title.GetGlobalRect()) && title.Text.Contains("[A/Enter]")
            && title.Text.Contains("[B/Esc]") && !title.ClipText && title.Size.Y >= title.GetMinimumSize().Y;
        var observations = new List<object>(); var failures = new List<string>();
        if (!titleEnclosed) failures.Add("full padded title");
        bool Observe(string phase)
        {
            _hud.ObserveD4Scroll(phase, "passive verifier; no scroll/layout write");
            var row = _hud.ChoiceRows[_hud.SelectedIndex]; var clip = _hud.UsableModalClip;
            var enclosed = clip.Encloses(row.GetGlobalRect());
            var fullOracle = enclosed && !row.ClipText && row.Size.Y >= row.GetMinimumSize().Y
                && row.GetThemeFontSize("font_size") == 18
                && _hud.ChoiceRows.Where(r => r != row).All(r => !row.GetGlobalRect().Intersects(r.GetGlobalRect()));
            observations.Add(new { phase, frame = Engine.GetProcessFrames(), selected = _hud.SelectedIndex,
                instance = row.GetInstanceId(), text = row.Text, rect = RectEvidence(row.GetGlobalRect()), clip = RectEvidence(clip),
                fullOracle, paddedEncloses = enclosed, outerEncloses = _hud.ModalClip.Encloses(row.GetGlobalRect()),
                font = row.GetThemeFontSize("font_size"), minimum = row.GetMinimumSize().ToString(), scroll = _hud.ModalScroll.ScrollVertical });
            return fullOracle;
        }
        for (var index = 0; index <= 11; index++)
        {
            if (index > 0)
            {
                var old = _hud.SelectedIndex;
                _hud.ObserveD4Scroll("input.DpadDown.before", "verifier invoking real polling/MenuRepeater");
                VerifyLevels(buttons: [JoyButton.DpadDown]); _Process(0);
                VerifyLevels(); _Process(0);
                VerificationMode.Require(_hud.SelectedIndex == old + 1, "D4 production polling/MenuRepeater selection");
                _hud.ObserveD4Scroll("input.DpadDown.after", "verifier invoking real polling/MenuRepeater");
            }
            await RenderedFrame(); await RenderedFrame();
            if (!Observe("original-two-rendered-frame-oracle")) failures.Add("Settings/" + index + " at original oracle");
        }
        // These are PASSIVE measurements beyond the existing oracle instant:
        // all production passes still have the original hard cap4.
        for (var frame = 0; frame < 6; frame++)
        { await RenderedFrame(); Observe("row11-passive-followup-" + frame); }
        _hud.ObserveD4Scroll("capture.before", "passive verifier image readback");
        using var image = GetViewport().GetTexture().GetImage();
        VerificationMode.Require(image.GetWidth() == 1280 && image.GetHeight() == 800, "D4 actual raster mismatch");
        var file = System.IO.Path.Combine(directory, "settings-row11.png");
        VerificationMode.Require(image.SavePng(file) == Error.Ok, "D4 capture failed");
        return new { experimentComplete = true, status = failures.Count == 0 ? "passed-scoped-oracle-only" : "failed-scoped-oracle",
            behaviorCorrected = false, acceptanceClaimed = false, titleEnclosed, failures, observations,
            trace = _hud.D4ScrollTrace, settings = new { preset = _settings.ActivePresetId, ui = _settings.UiScalePercent, zoom = _camera.Zoom.X },
            actualViewport = RectEvidence(GetViewport().GetVisibleRect()), raster = new[] { image.GetWidth(), image.GetHeight() },
            inputRoute = "Menu Handle/event -> InputState; Dpad selected-device polling -> InputState -> MenuRepeater -> Navigate -> RenderModal -> native EnsureControlVisible",
            isolatedTestStorage = _settingsStore.UsesIsolatedTestStorage, physicalControllerEvidence = false };
    }
    private async Task RunD4ModalTrace(string[] args)
    {
        var index = Array.IndexOf(args, "--preset-matrix");
        if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(2); return; }
        var report = args[index + 1]; var dir = args[index + 2]; SubViewport? viewport = null;
        LabRoot? scene = null;
        try
        {
            VerificationMode.Require(DisplayServer.GetName() != "headless" && !OS.HasFeature("standalone") && _settingsStore.UsesIsolatedTestStorage,
                "D4 trace requires SOURCE non-headless renderer and isolated storage");
            SetProcess(false); SetProcessInput(false); System.IO.Directory.CreateDirectory(dir);
            await LayoutFrames(); await RenderedFrame();
            var nativeSize = GetViewport().GetVisibleRect().Size;
            using (var baseline = GetViewport().GetTexture().GetImage())
                VerificationMode.Require(baseline.SavePng(System.IO.Path.Combine(dir, "native-baseline.png")) == Error.Ok, "D4 native capture failed");
            viewport = new SubViewport { Size = new(1280, 800), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            scene = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<LabRoot>(); viewport.AddChild(scene);
            scene.SetProcess(false); scene.SetProcessInput(false); await LayoutFrames();
            var slot = args.Contains("--d4-slot-trace");
            var result = slot ? await scene.TraceD4Slot(dir) : await scene.TraceD4Settings(dir);
            System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { schema = "arcanum-gamepad-d4-verification-only-v1",
                task = "ARC-GPAD-005", candidateCommit = OS.GetEnvironment("ARCANUM_GAMEPAD_CANDIDATE"), origin = "source",
                coverage = slot ? "ONE contextual/1280x800/UI100/zoom0.5/original prefix through Slot" : "ONE contextual/1280x800/UI100/zoom0.5/Settings-through11", backend = DisplayServer.GetName(),
                renderer = RenderingServer.GetCurrentRenderingMethod(), nativeBaseline = new { viewport = new[] { nativeSize.X, nativeSize.Y },
                    window = new[] { GetWindow().Size.X, GetWindow().Size.Y } },
                classification = "same production Main exact-size non-headless offscreen logical GUI; not native-window size or physical comfort", result },
                new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("D4 verification-only trace COMPLETE; full contract remains OPEN. Strong oracle result is recorded, not waived.");
            // Keep the actual quality FAIL as a nonzero child exit without inventing
            // an engine error: the diagnostic completed, the implementation did not.
            var failed = JsonSerializer.Serialize(result).Contains("failed-scoped-oracle");
            GetTree().Quit(failed ? 1 : 0);
        }
        catch (Exception e)
        {
            System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { schema = "arcanum-gamepad-d4-verification-only-v1",
                experimentComplete = false, error = e.ToString(), trace = scene?._hud.D4ScrollTrace }, new JsonSerializerOptions { WriteIndented = true }));
            GD.PushError(e.ToString()); GetTree().Quit(2);
        }
        finally { viewport?.Free(); }
    }
}
