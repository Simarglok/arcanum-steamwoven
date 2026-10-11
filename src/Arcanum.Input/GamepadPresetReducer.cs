using GamepadLab.Core;

namespace Arcanum.Input;

public enum PresetLayer { World, Modal, PendingAction, GridPreview }
public sealed record ActionTargetSnapshot(string ActionId, string? SlotId, string? RequestedTargetId, int? RequestedRevision,
    string Kind, Vec2? GroundPoint, long ContextEpoch, long SelectingEdgeId);
public sealed record PresetHostContext(PresetLayer Layer = PresetLayer.World, long ContextEpoch = 0,
    int? SelectedDeviceId = null, ActionTargetSnapshot? Target = null);
public sealed record PresetState(bool WheelOpen = false, int? WheelSector = null, bool WheelStickEngaged = false,
    bool AwaitingCameraNeutral = false, long ContextEpoch = 0, long NextEdgeId = 1);
public sealed record PresetIntent(string ActionId, string? SlotId = null, long SelectingEdgeId = 0);
public sealed record DomainOwnership(bool Actor, bool Camera, bool Zoom, bool Highlight);
public sealed record PresetStep(PresetState State, IReadOnlyList<PresetIntent> DiscreteIntents, DomainOwnership DomainOwnership,
    IReadOnlyList<GamepadBinding> HudBindings, string Bank, bool Highlight, string? Diagnostic = null);

/// <summary>One resolver for every preset. Emits data only: hosts own action execution and UI.</summary>
public sealed class GamepadPresetReducer
{
    private BindingProfile? _validated;
    public PresetStep Reduce(PresetState previous, IntentFrame frame, BindingProfile profile, PresetHostContext host)
    {
        if (!ReferenceEquals(profile, _validated))
        {
            if (GamepadBindingValidator.Validate(profile).Count != 0) throw new ArgumentException("profile must be validated", nameof(profile));
            _validated = profile;
        }
        var state = previous.ContextEpoch == host.ContextEpoch ? previous : new PresetState(ContextEpoch: host.ContextEpoch, NextEdgeId: previous.NextEdgeId);
        var intents = new List<PresetIntent>(); string? diagnostic = null;
        var down = frame.ControllerDown; var pressed = frame.ControllerPressed;
        var world = host.Layer == PresetLayer.World;
        var highlight = frame.Accepted && world && down.HasFlag(GamepadTokens.Buttons[profile.RbModifierButton]);
        var suppressCamera = state.WheelOpen || state.AwaitingCameraNeutral;
        void Emit(string action, string? slot = null) { intents.Add(new(action, slot, state.NextEdgeId)); state = state with { NextEdgeId = state.NextEdgeId + 1 }; }
        PresetStep Step()
        {
            var hud = new[] { "a", "x", "y", "b" }.Select(button => world ? profile.Resolve("world", button, down)
                : new GamepadBinding("modal", button, [], button == "a" ? "modal-confirm" : button == "b" ? "modal-cancel" : "context")).OfType<GamepadBinding>().ToArray();
            var bank = hud.Any(b => b.SlotId == "slot-05") ? "5–8" : hud.Any(b => b.SlotId == "slot-01") ? "1–4" : "Contextual";
            return new(state, intents.AsReadOnly(), new(frame.Accepted && world, frame.Accepted && world && !suppressCamera,
                frame.Accepted && world, frame.Accepted && world), Array.AsReadOnly(hud), bank, highlight, diagnostic);
        }
        if (!frame.Accepted) return Step();
        bool Fresh(ControllerButtons button) => pressed.HasFlag(button);
        bool confirm = Fresh(ControllerButtons.A) || frame.Confirm;
        bool cancel = Fresh(ControllerButtons.B) || frame.Cancel;
        if (Fresh(ControllerButtons.Menu) || frame.Settings)
        { state = state with { WheelOpen = false, WheelSector = null, AwaitingCameraNeutral = false }; suppressCamera = true; Emit("settings"); return Step(); }
        if (!world)
        {
            state = state with { WheelOpen = false, WheelSector = null };
            if (cancel) Emit("modal-cancel"); else if (confirm) Emit("modal-confirm");
            else if (host.Layer == PresetLayer.PendingAction)
            {
                if (Fresh(ControllerButtons.DpadLeft) || frame.Previous) Emit("target-previous");
                else if (Fresh(ControllerButtons.DpadRight) || frame.Next) Emit("target-next");
            }
            return Step();
        }
        var lb = GamepadTokens.Buttons[profile.LbModifierButton];
        if (state.AwaitingCameraNeutral)
        {
            // Keyboard/other-device zeros are never controller RS neutral proof.
            if (frame.SourceDeviceId == host.SelectedDeviceId && frame.GenuineCompleteObservation && frame.RadialSelection == default(Vec2))
                state = state with { AwaitingCameraNeutral = false };
            suppressCamera = true; // The neutral handoff observation itself never pans or arms Manual recenter.
        }
        if (state.WheelOpen)
        {
            suppressCamera = true;
            var controllerObservation = frame.SourceDeviceId >= 0 && frame.SourceDeviceId == host.SelectedDeviceId;
            if ((controllerObservation && !down.HasFlag(lb)) || cancel || !Finite(frame.RadialSelection))
            { state = state with { WheelOpen = false, WheelSector = null, WheelStickEngaged = false, AwaitingCameraNeutral = true }; Emit("wheel-cancel"); return Step(); }
            if (controllerObservation)
            {
                var magnitude = frame.RadialSelection.Length;
                if (magnitude <= .45 + 1e-7) state = state with { WheelStickEngaged = false };
                else if (magnitude >= .65 - 1e-7 || state.WheelStickEngaged)
                    state = state with { WheelSector = Sector(frame.RadialSelection), WheelStickEngaged = true };
            }
            if (confirm)
            {
                if (state.WheelSector is int sector)
                {
                    Emit("quick-slot", $"slot-{sector+1:00}");
                    state = state with { WheelOpen = false, WheelSector = null, WheelStickEngaged = false, AwaitingCameraNeutral = true };
                }
                else { diagnostic = "NoSelection"; Emit("wheel-no-selection"); }
            }
            return Step();
        }
        if (profile.BasePresetId == "radial" && Fresh(lb))
        {
            state = state with { WheelOpen = true, WheelSector = null, WheelStickEngaged = false };
            suppressCamera = true; Emit("wheel-open"); return Step();
        }
        // Higher-layer entry consumes the entire observation before faces can activate.
        foreach (var button in new[] { "view", "dpad-up", "dpad-down" })
            if (Fresh(GamepadTokens.Buttons[button]) && profile.Resolve("world", button, down) is GamepadBinding binding)
            { Emit(binding.ActionId, binding.SlotId); return Step(); }
        foreach (var button in new[] { "a", "x", "y", "b", "dpad-left", "dpad-right", "l3", "r3" })
        {
            if (Fresh(GamepadTokens.Buttons[button]) && profile.Resolve("world", button, down) is GamepadBinding binding)
            { Emit(binding.ActionId, binding.SlotId); if (binding.ActionId is "context" or "back" or "hub" or "battle-surface" or "follower-surface") break; }
        }
        // Keyboard/mouse aliases use these same semantic surfaces, never a controller table twice.
        if (frame.Cancel) Emit("back"); else if (frame.Context) Emit("context");
        else if (frame.Confirm) Emit("interact"); else if (frame.Inspect) Emit("inspect");
        if (frame.Previous) Emit("target-previous"); else if (frame.Next) Emit("target-next");
        return Step();
    }
    private static bool Finite(Vec2 value) => double.IsFinite(value.X) && double.IsFinite(value.Y);
    // Up is zero. Equal halfway boundaries round clockwise (including the 10->1 wrap).
    public static int Sector(Vec2 direction)
    {
        if (!Finite(direction) || direction == default(Vec2)) throw new ArgumentException("finite nonzero direction required", nameof(direction));
        var turns = Math.Atan2(direction.X, -direction.Y) / (Math.PI * 2);
        if (turns < 0) turns += 1;
        return (int)Math.Floor(turns * 10 + .5 + 1e-10) % 10;
    }
}
