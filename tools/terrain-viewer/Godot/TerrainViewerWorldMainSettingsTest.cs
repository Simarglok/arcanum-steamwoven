using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// Bounded public main-window scheduling control. Real Window focus is a
    /// prerequisite, never a forced production flag. Buffered tracing is still
    /// instrumentation; success does not explain the historical settings failure.
    /// </summary>
    internal async Task<bool> WorldMainSettingsForSelfTestAsync()
    {
        if (!SelfTestBootstrap || GetViewport() != GetWindow() || DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("MAIN_SETTINGS_NATIVE_HOST_REQUIRED");
        var window = GetWindow();
        var oldMode = window.Mode;
        var records = new List<string>();
        var outcomes = new List<string>();
        var toggles = _worldPanel!.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().ToArray();
        if (toggles.Length != 4 || toggles[0].Name != "CameraimageXinversion")
            throw new InvalidOperationException("MAIN_SETTINGS_CONTROL_IDENTITY");
        var none = new RawInputFrame(-1, default);
        var baseline = new InputSettings(InvertCameraX: false, InvertCameraY: false, InvertActorX: false, InvertActorY: false);
        _worldInputTraceForSelfTest = line => records.Add($"frame={Engine.GetProcessFrames()} {line}");
        _worldPanel.InputTraceForSelfTest = TraceWorldInput;
        async Task<bool> ObserveFocus(bool focused)
        {
            // Native frame opportunities, not elapsed sleeps or forced flags.
            for (var frame = 0; frame < 8; frame++)
            {
                await NextFrameAsync();
                TraceWorldInput($"MainProbe:focus-required={focused}");
                if (window.HasFocus() == focused && _worldFocused == focused) return true;
            }
            return false;
        }
        void Frame(RawInputFrame levels)
        {
            var focused = window.HasFocus();
            TraceWorldInput($"MainProbe:complete-level confirm={levels.Confirm} actualFocus={focused}");
            WorldFrameForSelfTest(levels with { Focused = focused }, Array.Empty<RawInputFrame>(), levels.Confirm ? 2 : .1, complete: focused);
        }
        void Sequence()
        {
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = true });
            Frame(none with { Confirm = true });
            GetViewport().PushInput(new InputEventMouseMotion { Position = new(799, 599) });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = false });
        }
        try
        {
            window.GrabFocus();
            if (!await ObserveFocus(true))
            {
                outcomes.Add("NOT_VERIFIED MAIN_WINDOW_FOCUS_UNAVAILABLE");
                return false;
            }
            if (!WorldModal) ToggleWorldSettings();
            foreach (var settled in new[] { false, true })
            {
                var arm = settled ? "B-settled" : "A-immediate";
                SaveWorldSettings(baseline); _worldPanel.Bind(baseline); _worldAdapter.Clear();
                _worldReducer.SetStartupDevices(Array.Empty<int>()); QuarantineWorld();
                toggles[0].GrabFocus();
                if (settled) { await NextFrameAsync(); await NextFrameAsync(); }
                if (!window.HasFocus() || !_worldFocused || GetViewport().GuiGetFocusOwner() != toggles[0])
                {
                    outcomes.Add($"NOT_VERIFIED {arm} FOCUS_PRECONDITION_UNAVAILABLE");
                    return false;
                }
                for (var i = 0; i < 3; i++) Frame(none);
                var changes = 0;
                void Changed(InputSettings _) { changes++; TraceWorldInput($"MainProbe:{arm}:Changed count={changes}"); }
                _worldPanel.Changed += Changed;
                try
                {
                    var actor = _worldActor.State.Position; var camera = _camera!.State;
                    TraceWorldInput($"MainProbe:{arm}:before-press");
                    Sequence(); await NextFrameAsync();
                    var correct = _worldSettings.InvertCameraX && !_worldSettings.InvertCameraY
                        && !_worldSettings.InvertActorX && !_worldSettings.InvertActorY && changes == 1
                        && _worldPanel.Settings == _worldSettings && _worldStore.Load() == _worldSettings
                        && _worldActor.State.Position == actor && !_worldActor.IsPlaying && _camera.State == camera
                        && !_worldGate.IsOpen && !_camera.FollowState.RecenterArmed && WorldModal;
                    outcomes.Add($"{arm} {(correct ? "PASS" : "FAIL")} settings={_worldSettings.InvertCameraX}/{_worldSettings.InvertCameraY}/{_worldSettings.InvertActorX}/{_worldSettings.InvertActorY} changes={changes} actor={actor.X}/{actor.Y}");
                    if (!correct) throw new InvalidOperationException("MAIN_SETTINGS_ACTIVATION_INVARIANT");
                }
                finally { _worldPanel.Changed -= Changed; }
            }
            // Request an actual native focus loss. Failure to observe it is a
            // capability outcome, not simulated OS-focus evidence.
            SaveWorldSettings(baseline); _worldPanel.Bind(baseline); _worldAdapter.Clear(); QuarantineWorld();
            toggles[0].GrabFocus();
            var returnChanges = 0;
            void ReturnChanged(InputSettings _) { returnChanges++; }
            _worldPanel.Changed += ReturnChanged;
            try
            {
                window.Mode = Window.ModeEnum.Minimized;
                if (!await ObserveFocus(false))
                {
                    outcomes.Add("NOT_VERIFIED MAIN_WINDOW_FOCUS_LOSS_UNAVAILABLE");
                    return false;
                }
                Sequence(); await NextFrameAsync();
                var rejected = returnChanges == 0 && _worldSettings == baseline && !_worldGate.IsOpen;
                outcomes.Add($"false-focus {(rejected ? "PASS" : "FAIL")} changes={returnChanges}");
                if (!rejected) throw new InvalidOperationException("MAIN_SETTINGS_FALSE_FOCUS_ACTIVATED");
                window.Mode = oldMode; window.GrabFocus();
                if (!await ObserveFocus(true))
                {
                    outcomes.Add("NOT_VERIFIED MAIN_WINDOW_FOCUS_RETURN_UNAVAILABLE");
                    return false;
                }
                toggles[0].GrabFocus(); await NextFrameAsync();
                if (GetViewport().GuiGetFocusOwner() != toggles[0])
                {
                    outcomes.Add("NOT_VERIFIED MAIN_CONTROL_FOCUS_RETURN_UNAVAILABLE");
                    return false;
                }
                // Held input cannot replace the complete neutral rearm.
                Frame(none with { Confirm = true });
                if (returnChanges != 0) throw new InvalidOperationException("MAIN_SETTINGS_REARM_BYPASSED");
                for (var i = 0; i < 3; i++) Frame(none);
                var actor = _worldActor.State.Position; var camera = _camera!.State;
                Sequence(); await NextFrameAsync();
                var correct = returnChanges == 1 && _worldSettings.InvertCameraX && !_worldSettings.InvertCameraY
                    && !_worldSettings.InvertActorX && !_worldSettings.InvertActorY
                    && _worldActor.State.Position == actor && !_worldActor.IsPlaying && _camera.State == camera
                    && !_worldGate.IsOpen && !_camera.FollowState.RecenterArmed && WorldModal;
                outcomes.Add($"focus-return-neutral-fresh {(correct ? "PASS" : "FAIL")} changes={returnChanges}");
                if (!correct) throw new InvalidOperationException("MAIN_SETTINGS_FOCUS_RETURN_INVARIANT");
            }
            finally { _worldPanel.Changed -= ReturnChanged; }
            return true;
        }
        finally
        {
            // Outcomes were captured before any diagnostics were printed.
            foreach (var outcome in outcomes) GD.Print("MAIN_SETTINGS_OUTCOME " + outcome);
            foreach (var record in records) GD.Print("MAIN_SETTINGS_TRACE " + record);
            _worldInputTraceForSelfTest = null; _worldPanel.InputTraceForSelfTest = null;
            window.Mode = oldMode;
            if (WorldModal) ToggleWorldSettings();
            _worldAdapter.Clear();
        }
    }
}
