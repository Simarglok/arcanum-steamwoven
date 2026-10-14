using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Independent literal production-scene oracles, enabled only by the explicit matrix.</summary>
public partial class LabRoot
{
    private void VerifyLevels(float leftX = 0, float rightX = 0, float rightY = 0, float lt = 0, float rt = 0, params JoyButton[] buttons)
    {
        _adapter.RefreshControllerLevels(3, axis => axis switch
        {
            JoyAxis.LeftX => leftX, JoyAxis.RightX => rightX, JoyAxis.RightY => rightY,
            JoyAxis.TriggerLeft => lt, JoyAxis.TriggerRight => rt, _ => 0
        }, buttons.Contains);
    }
    private async Task VerifyLifecycleProjection(string directory, string prefix)
    {
        var saved = _settings; var actor = _actor; var center = _cameraCenter; var cameraState = _cameraState; var zoom = _camera.Zoom;
        var uses = _actions.ResourceUses;
        _hud.HideModal(); ClearWorldIntents("SIMULATION common actions"); VerifyNeutral();
        // Poll-only A must be visible exactly once, even if the event route then observes the held button.
        _settings = _settings with { ActivePresetId = "quick-slots", Slots = _settings.Slots.Assign("slot-01", "synthetic.item.self") };
        VerifyLevels(buttons: [JoyButton.A]); _Process(0);
        VerificationMode.Require(_actions.ResourceUses == uses + 1 && _actions.Journal.Last().SlotId == "slot-01", "poll-only A not exactly one self activation");
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(0);
        VerificationMode.Require(_actions.ResourceUses == uses + 1, "event repeated poll-owned held A");
        VerifyLevels(); _Process(0); VerifyTap(JoyButton.A);
        VerificationMode.Require(_actions.ResourceUses == uses + 2, "short event-only tap was lost");
        VerifyLevels(); _Process(0); _settings = saved;
        // Keep a legal, unclamped center and an open-space actor for the literal numeric policy oracle.
        _actor = new(new Vec2(460, 610), new Vec2(1, 0)); SyncActor();
        // At zoom2 the literal map midpoint is legal even for the3440-wide tuple.
        _camera.Zoom = new(2, 2); _cameraCenter = new(800, 800); _cameraState = new(CameraMode.Manual, false, false); ApplyCameraCenter();
        foreach (var x in new[] { false, true })
        foreach (var y in new[] { false, true })
        {
            _settings = saved with { InvertCameraX = x, InvertCameraY = y, MovementMode = MovementMode.DirectIntent, CameraSensitivity = 1, ActorSensitivity = 1 };
            var beforeActor = _actor.Position; var beforeCenter = _cameraCenter;
            VerifyLevels(leftX: 1, rightX: 1, rightY: 1); _Process(.01);
            var displacement = _cameraCenter - beforeCenter;
            VerificationMode.Require(_actor.Position.X > beforeActor.X && _actor.Position.Y == beforeActor.Y, "concurrent LS/RS actor domain changed");
            VerificationMode.Require((displacement.X < 0) == x && (displacement.Y < 0) == y && _cameraState.Mode == CameraMode.Manual, "literal actual image inversion direction");
        }
        VerifyLevels(leftX: 1); _Process(.01);
        VerificationMode.Require(_cameraState.Mode == CameraMode.Manual && !_cameraState.RecenterArmed, "RS release with LS held recentered");
        VerifyLevels(); _Process(0);
        VerificationMode.Require(_cameraState.Mode == CameraMode.Manual && _cameraState.RecenterArmed, "real full neutral did not arm Manual");
        VerifyLevels(leftX: 1); _Process(.01);
        VerificationMode.Require(_cameraState.Mode == CameraMode.Follow && !_cameraState.RecenterArmed, "fresh actor-only edge did not resume Follow");
        var atL3 = _actor.Position; VerifyTap(JoyButton.LeftStick);
        VerificationMode.Require(_actor.Position == atL3 && _cameraState.Mode == CameraMode.Follow, "L3 reset actor instead of only camera");
        VerifyLevels(); _Process(0); _camera.Zoom = Vector2.One;
        VerifyLevels(rt: 1); _Process(1);
        VerificationMode.Require(_camera.Zoom == new Vector2(2, 2), "literal full RT 1s must reach2");
        _camera.Zoom = Vector2.One; VerifyLevels(lt: 1); _Process(1);
        VerificationMode.Require(_camera.Zoom == new Vector2(.5f, .5f), "literal full LT 1s must reach0.5");
        _camera.Zoom = Vector2.One; VerifyLevels(lt: 1, rt: 1); _Process(1);
        VerificationMode.Require(_camera.Zoom == Vector2.One, "opposing triggers changed zoom");
        VerifyLevels(); _Process(0);
        // Every lifecycle boundary forbids held buttons/axes/triggers and other-device/keyboard zeros.
        foreach (var boundary in new[] { "focus", "modal", "preset", "reset", "disconnect", "device-change" })
        {
            _settings = saved with { ActivePresetId = "quick-slots" };
            _cameraState = new(CameraMode.Manual, false, false); _cameraCenter = new(800, 800); ApplyCameraCenter();
            var p = _actor.Position; var c = _cameraCenter; var z = _camera.Zoom; var count = _actions.ResourceUses;
            VerifyLevels(leftX: 1, rightX: 1, lt: 1, rt: 1, buttons: [JoyButton.A, JoyButton.LeftShoulder, JoyButton.RightShoulder]);
            if (boundary == "focus") { ApplyFocus(false); _Process(.1); ApplyFocus(true); }
            else if (boundary == "modal") { VerifyTap(JoyButton.Start); CloseModal(); }
            else if (boundary == "preset") { _hud.ShowSettings(); ApplyPresetSetting("Preset Radial"); CloseModal(); }
            else if (boundary == "reset") { _hud.ShowSettings(); VerifyChoose("Reset defaults"); CloseModal(); }
            else
            {
                OnJoyConnectionChanged(3, false); OnJoyConnectionChanged(5, true);
                Handle(new InputEventJoypadButton { Device = 5, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation);
                _Process(0); VerificationMode.Require(_input.ActiveDevice == 5, "replacement device not acquired deterministically");
                _adapter.RefreshControllerLevels(5, _ => 0, b => b == JoyButton.A); _Process(.1);
                VerificationMode.Require(_actions.ResourceUses == count, "held acquisition button activated");
                OnJoyConnectionChanged(5, false); OnJoyConnectionChanged(3, true);
                Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(0);
                VerificationMode.Require(_input.ActiveDevice == 3, "original selected device not reacquired");
                VerifyLevels(leftX: 1, rightX: 1, lt: 1, rt: 1, buttons: [JoyButton.A, JoyButton.LeftShoulder, JoyButton.RightShoulder]);
            }
            _Process(.1); _Process(.1);
            _adapter.RefreshKeyboardLevels(_ => false); _adapter.RefreshControllerLevels(7, _ => 0, _ => false); _Process(.1);
            VerificationMode.Require(_actor.Position == p && _cameraCenter == c && _camera.Zoom == z && _actions.ResourceUses == count, "ghost world effect across " + boundary);
            VerifyNeutral(); _settings = saved with { ActivePresetId = "quick-slots" }; VerifyTap(JoyButton.A);
            VerificationMode.Require(_actions.ResourceUses == count + 1, "fresh selected edge after " + boundary + " not exactly once");
        }
        _settings = saved; _actor = actor; SyncActor(); _camera.Zoom = zoom; _cameraCenter = center; _cameraState = cameraState; ApplyCameraCenter();
        ClearWorldIntents("SIMULATION restored lifecycle fixture"); VerifyNeutral();
        await CaptureScenario("lifecycle", directory, prefix);
        // Projection readback uses the real Canvas transform; UI scale must be irrelevant.
        var originalZoom = _camera.Zoom; var originalCenter = _cameraCenter;
        _cameraCenter = new(800, 800); ApplyCameraCenter();
        Vec2 anchor(float scale)
        {
            _camera.Zoom = new(scale, scale); _Process(0);
            return _courtyard.Candidates(_world).Single(t => t.Id == "crate").Anchor;
        }
        var a = anchor(.5f); var b = anchor(2f); var origin = GetViewport().GetVisibleRect().GetCenter();
        VerificationMode.Require(Math.Abs((b.X - origin.X) - 4 * (a.X - origin.X)) < .02 && Math.Abs((b.Y - origin.Y) - 4 * (a.Y - origin.Y)) < .02, "real zoom projection not4x about camera");
        _hud.SetScale(200); VerificationMode.Require(_courtyard.Candidates(_world).Single(t => t.Id == "crate").Anchor == b, "UI scale changed projection"); _hud.SetScale(saved.UiScalePercent);
        _contractObservations.Add(new { oracle = "actual-projection", a = new[] { a.X, a.Y }, b = new[] { b.X, b.Y },
            origin = new[] { (double)origin.X, origin.Y }, uiReturned = _courtyard.Candidates(_world).Single(t => t.Id == "crate").Anchor });
        _camera.Zoom = originalZoom; _cameraCenter = originalCenter; ApplyCameraCenter(); _Process(0);
        await CaptureScenario("projection-zoom", directory, prefix);
    }
    private async Task VerifyWheelExtremes(string directory, string prefix)
    {
        if (Profile.BasePresetId != "radial") return;
        var c = _cameraCenter; var mode = _cameraState.Mode; var actor = _actor; var slots = _settings.Slots;
        foreach (var (id, x, y, sector) in new[] { ("slot-09", -.9510565f, -.309017f, 8), ("slot-10", -.5877853f, -.809017f, 9) })
        {
            ClearWorldIntents("SIMULATION wheel09/10"); VerifyNeutral();
            _settings = _settings with { Slots = _settings.Slots.Assign(id, "synthetic.item.self") };
            VerifyLevels(buttons: [JoyButton.LeftShoulder]); _Process(0);
            VerifyLevels(rightX: x, rightY: y, buttons: [JoyButton.LeftShoulder, JoyButton.RightShoulder]); _Process(0);
            VerificationMode.Require(_presetState.WheelSector == sector && _presetStep?.Highlight == true && _cameraCenter == c && _cameraState.Mode == mode, "literal09/10 wheel/RS/RB ownership");
            await CaptureScenario("wheel-" + id, directory, prefix);
            var uses = _actions.ResourceUses; VerifyTap(JoyButton.A);
            VerificationMode.Require(_actions.ResourceUses == uses + 1 && _actions.Journal.Last().SlotId == id, "wheel09/10 not exactly once");
            VerifyLevels(rightX: x, rightY: y); _Process(.01);
            VerificationMode.Require(_cameraCenter == c && _cameraState.Mode == mode && !_cameraState.RecenterArmed, "wheel held RS returned early or fabricated Manual neutral");
            VerifyLevels(); _Process(0); VerificationMode.Require(_cameraCenter == c && !_cameraState.RecenterArmed, "neutral handoff observation armed Manual");
        }
        await CaptureScenario("wheel-09-10", directory, prefix);
        _settings = _settings with { Slots = slots }; _actor = actor; SyncActor();
    }
}
