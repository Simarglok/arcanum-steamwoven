using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

public partial class LabRoot
{
    private bool _explicitVerificationPoll;
    // Only explicit --self-test/--smoke/--preset-matrix can enter this injected fixture path.
    // Live rearm always reads native polling and never treats cleared adapter values as observed.
    private void CompleteInjectedObservation()
    {
        if (!_injectedObservations) throw new InvalidOperationException("not an injected verification run");
        if (_explicitVerificationPoll) return; // Preserve deliberately invalid/incomplete production adapter polls.
        _adapter.RefreshKeyboardLevels(_adapter.IsKeyHeld);
        if (_input.ActiveDevice is not int device || _adapter.SampleController(device, _focused) is not RawInputFrame raw) return;
        _adapter.RefreshControllerLevels(device, axis => axis switch
        {
            JoyAxis.LeftX => (float)raw.LeftStick.X, JoyAxis.LeftY => (float)raw.LeftStick.Y,
            JoyAxis.RightX => (float)raw.RightStick.X, JoyAxis.RightY => (float)raw.RightStick.Y,
            JoyAxis.TriggerLeft => (float)raw.LeftTrigger, JoyAxis.TriggerRight => (float)raw.RightTrigger, _ => 0
        }, button => raw.ControllerButtons.HasFlag(button switch
        {
            JoyButton.A => ControllerButtons.A, JoyButton.B => ControllerButtons.B, JoyButton.X => ControllerButtons.X, JoyButton.Y => ControllerButtons.Y,
            JoyButton.LeftShoulder => ControllerButtons.LB, JoyButton.RightShoulder => ControllerButtons.RB, JoyButton.DpadUp => ControllerButtons.DpadUp,
            JoyButton.DpadDown => ControllerButtons.DpadDown, JoyButton.DpadLeft => ControllerButtons.DpadLeft, JoyButton.DpadRight => ControllerButtons.DpadRight,
            JoyButton.LeftStick => ControllerButtons.L3, JoyButton.RightStick => ControllerButtons.R3, JoyButton.Back => ControllerButtons.View,
            JoyButton.Start => ControllerButtons.Menu, _ => ControllerButtons.None
        }));
    }
}
