using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Single production router, shared by ordinary events, native polling and explicit verification.</summary>
public partial class LabRoot
{
    private bool _cameraHandoffObservation;
    private BindingProfile Profile => _settings.ActivePresetId == "user-copy" ? _settings.UserCopy! : BuiltInGamepadPresets.Get(_settings.ActivePresetId);
    private PresetLayer Layer => _hud.ModalVisible ? PresetLayer.Modal : _actions.Pending is not null ? PresetLayer.PendingAction : _actions.GridPreview is not null ? PresetLayer.GridPreview : PresetLayer.World;
    private IntentFrame Route(IntentFrame intent)
    {
        var layer = Layer; var pending = _actions.Pending;
        var step = _presets.Reduce(_presetState, intent, Profile, new(layer, _epoch, _input.ActiveDevice, pending));
        _presetState = step.State;
        if (intent.SourceDeviceId >= 0 || _input.ActiveDevice is null) _presetStep = step;
        if (!step.DomainOwnership.Camera && intent.SourceDeviceId >= 0) _cameraHandoffObservation = true;
        _hud.SetPreset(_presetState);
        var sequence = _actions.EventSequence;
        foreach (var action in step.DiscreteIntents)
        {
            Dispatch(action);
            // A selection opening a layer consumes the rest of the observation, not replaying into it.
            if (Layer != layer || _actions.Pending != pending || !intent.Accepted) break;
        }
        foreach (var e in _actions.Journal.TakeLast((int)Math.Min(128, _actions.EventSequence - sequence))) AddLog($"{e.Kind}: {e.ActionId ?? ""} {e.SlotId ?? ""} {e.TargetId ?? ""} (synthetic)");
        return intent with { CameraPan = step.DomainOwnership.Camera ? intent.CameraPan : default,
            ZoomDrive = step.DomainOwnership.Zoom ? intent.ZoomDrive : 0,
            // Pending/grid domains still consume LS for their preview, not world locomotion.
            Movement = _hud.ModalVisible ? default : intent.Movement };
    }
    private void Dispatch(PresetIntent action)
    {
        var ground = _world.MakeCanvasPositionLocal(GetViewport().GetVisibleRect().GetCenter());
        switch (action.ActionId)
        {
            case "settings": _hud.ShowSettings(); ClearWorldIntents("Settings opened."); break;
            case "context": _hud.ShowSlots(_settings.Slots); ClearWorldIntents("Full ten slots/catalog opened."); break;
            case "hub": OpenMock(ModalKind.Hub, "SYNTHETIC HUB", ["Inventory", "Character", "Journal", "Maps", "Catalog", "Close"]); break;
            case "battle-surface": OpenMock(ModalKind.Battle, "SYNTHETIC BATTLE — no production combat", ["Turn-based comparison", "Real-time comparison", "mock.turn-end", "Close"]); break;
            case "follower-surface": OpenMock(ModalKind.Follower, "SYNTHETIC FOLLOWER COMMANDS", ["mock.follower-follow", "Close"]); break;
            case "modal-cancel": if (_hud.ModalVisible) CloseModal(); else _actions.Cancel(action.SelectingEdgeId); break;
            case "modal-confirm": if (_hud.ModalVisible) ActivateModal(); else _actions.Confirm(action.SelectingEdgeId, _courtyard.WorldTargets); break;
            case "quick-slot": _actions.UseSlot(action.SlotId!, _settings.Slots, action.SelectingEdgeId, _courtyard.WorldTargets, new(ground.X, ground.Y)); break;
            case "target-previous": case "target-next": _actions.Cycle(_courtyard.WorldTargets, action.ActionId == "target-next" ? 1 : -1); _selection = _targeting.Pick(_actions.SelectedTargetId!); break;
            case "target-lock": _actions.ToggleLock(); break;
            case "camera-recenter": ResetCameraCentered(); AddLog("Camera-only recenter (L3); actor unchanged."); break;
            case "back": _actions.Cancel(action.SelectingEdgeId); _actions.Select(null, false); _selection = _targeting.CancelLock(); ClearWorldIntents("Back; world requires genuine neutral."); break;
            case "interact": case "inspect":
                var target = _courtyard.WorldTargets.FirstOrDefault(t => t.Id == _actions.SelectedTargetId && t.Revision == _actions.SelectedRevision && t.Visible && t.Interactive && t.Alive);
                _actions.Record(new(target is null && _actions.SelectedTargetId is not null ? "TargetUnavailable" : "MockIntent", action.ActionId, TargetId: _actions.SelectedTargetId, EdgeId: action.SelectingEdgeId)); break;
            default: _actions.Record(new(action.ActionId, EdgeId: action.SelectingEdgeId)); break;
        }
    }
    private void OpenMock(ModalKind kind, string title, string[] ids) { _hud.ShowSurface(kind, title, ids); ClearWorldIntents(title); }
    private void ShowCatalog(bool assignment) => _hud.ShowSurface(assignment ? ModalKind.Assign : ModalKind.Hub,
        assignment ? $"ASSIGN {_editingSlot} — synthetic catalog" : "SYNTHETIC CATALOG — rare entries explicitly unsupported",
        LabActionCatalog.Entries.Select(a => a.Id).Append("Empty").Append("Close").ToArray(),
        LabActionCatalog.Entries.Select(a => $"{a.Category}: {a.Label}").Append("Empty slot").Append("Close").ToArray());
    private bool ActivatePresetSurface(string choice)
    {
        if (_hud.ModalKind == ModalKind.Settings) return ApplyPresetSetting(choice);
        if (_hud.ModalKind == ModalKind.Context && choice.StartsWith("slot-", StringComparison.Ordinal))
        {
            _editingSlot = choice; _hud.ShowSurface(ModalKind.Slot, $"{choice} — {_settings.Slots.Assignments[choice] ?? "EMPTY"}", ["Use", "Assign", "Empty", "Close"]); return true;
        }
        if (choice == "Catalog") { ShowCatalog(false); return true; }
        if (_hud.ModalKind == ModalKind.Slot)
        {
            if (choice == "Assign") ShowCatalog(true);
            else if (choice == "Empty") { SavePreferences(_settings with { Slots = _settings.Slots.Assign(_editingSlot!, null) }); _hud.ShowSlots(_settings.Slots); }
            else if (choice == "Use")
            {
                CloseModal(); var g = _world.MakeCanvasPositionLocal(GetViewport().GetVisibleRect().GetCenter());
                _actions.UseSlot(_editingSlot!, _settings.Slots, _presetState.NextEdgeId, _courtyard.WorldTargets, new(g.X, g.Y));
                _presetState = _presetState with { NextEdgeId = _presetState.NextEdgeId + 1 };
            }
            return true;
        }
        if (_hud.ModalKind == ModalKind.Assign)
        { SavePreferences(_settings with { Slots = _settings.Slots.Assign(_editingSlot!, choice == "Empty" ? null : choice) }); _hud.ShowSlots(_settings.Slots); return true; }
        if (_hud.ModalKind == ModalKind.Bindings)
        {
            _editingBinding = choice;
            var buttons = GamepadTokens.Buttons.Keys.Where(b => b is not ("menu" or "lb" or "rb")).ToArray();
            _hud.ShowSurface(ModalKind.BindingButton, "Choose selecting button; occupied bindings require an explicit atomic Swap (profile validated before apply)",
                buttons.Concat(buttons.Select(b => "Swap with " + b)).Append("Close").ToArray()); return true;
        }
        if (_hud.ModalKind == ModalKind.BindingButton)
        {
            var original = _settings.UserCopy!; var index = int.Parse(_editingBinding!);
            var swap = choice.StartsWith("Swap with ", StringComparison.Ordinal);
            var button = swap ? choice[10..] : choice;
            var selected = original.Bindings[index];
            var bindings = original.Bindings.Select((b, n) => n == index ? new GamepadBinding(b.Context, button, b.Modifiers, b.ActionId, b.SlotId)
                : swap && b.Context == selected.Context && b.Button == button && b.Modifiers.SequenceEqual(selected.Modifiers)
                    ? new GamepadBinding(b.Context, selected.Button, b.Modifiers, b.ActionId, b.SlotId) : b);
            TryApplyCopy(new BindingProfile("user-copy", original.BasePresetId, bindings, lbModifierButton: original.LbModifierButton, rbModifierButton: original.RbModifierButton));
            ShowBindingEditor(); return true;
        }
        if (choice is "Turn-based comparison" or "Real-time comparison") { _actions.TurnBased = choice == "Turn-based comparison"; CloseModal(); return true; }
        var catalog = LabActionCatalog.Get(choice);
        if (catalog is not null)
        { CloseModal(); _actions.SelectAction(choice, null, _presetState.NextEdgeId, _courtyard.WorldTargets, _result?.GroundPoint ?? default); _presetState = _presetState with { NextEdgeId = _presetState.NextEdgeId + 1 }; return true; }
        if (_hud.ModalKind is ModalKind.Hub or ModalKind.Battle or ModalKind.Follower)
        { _actions.Record(new("MockIntent", choice)); AddLog($"{choice} (synthetic mock)"); CloseModal(); return true; }
        return false;
    }
    private void ShowBindingEditor()
    {
        if (_settings.UserCopy is null) { AddLog("Create a user copy first."); return; }
        var editable = _settings.UserCopy.Bindings.Select((b, n) => (b, n)).Where(p => p.b.Context == "world" && p.b.Button != "menu" && p.b.ActionId != "wheel-open").ToArray();
        _hud.ShowSurface(ModalKind.Bindings, "USER COPY DIGITAL BINDINGS — invalid changes leave old profile intact",
            editable.Select(p => p.n.ToString()).Append("Close").ToArray(), editable.Select(p => $"{string.Join('+', p.b.Modifiers)}+{p.b.Button}: {p.b.ActionId} {p.b.SlotId}").Append("Close").ToArray());
    }
    private void TryApplyCopy(BindingProfile copy)
    {
        var issues = GamepadBindingValidator.Validate(copy);
        if (issues.Count != 0) { AddLog("Rejected binding: " + string.Join(", ", issues.Select(i => i.Code))); return; }
        SavePreferences(_settings with { UserCopy = copy }); ClearWorldIntents("Validated copy applied; world unchanged.");
    }
    private void SavePreferences(LabSettings settings)
    {
        if (!settings.IsValid) { AddLog("Rejected invalid preference; previous valid setting retained."); return; }
        try { _settingsStore.Save(settings); _settings = settings; _hud.SetScale(settings.UiScalePercent); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { AddLog("Save failed; previous committed preferences retained."); }
    }
    private bool ApplyPresetSetting(string choice)
    {
        if (choice.StartsWith("Preset ", StringComparison.Ordinal))
        {
            var id = choice[7..] switch { "Contextual" => "contextual", "QuickSlots" => "quick-slots", "Radial" => "radial", _ => "" };
            SavePreferences(_settings with { ActivePresetId = id }); ClearWorldIntents("Preset switched; positions/mode/zoom unchanged."); return true;
        }
        if (choice == "Create user copy") { SavePreferences(_settings with { UserCopy = BuiltInGamepadPresets.Get(Profile.BasePresetId).CreateCopy() }); ClearWorldIntents("Created one user copy."); return true; }
        if (choice == "Use user copy") { if (_settings.UserCopy is not null) { SavePreferences(_settings with { ActivePresetId = "user-copy" }); ClearWorldIntents("Using validated user copy."); } else AddLog("No user copy; create one first."); return true; }
        if (choice == "Reset user copy") { if (_settings.UserCopy is not null) TryApplyCopy(BuiltInGamepadPresets.Get(_settings.UserCopy.BasePresetId).CreateCopy()); return true; }
        if (choice == "Edit bindings") { ShowBindingEditor(); return true; }
        if (choice == "Swap shoulder roles")
        {
            if (_settings.UserCopy is BindingProfile p) TryApplyCopy(p.WithShoulderRoles(p.RbModifierButton, p.LbModifierButton));
            else AddLog("Create a user copy first."); return true;
        }
        if (choice == "UI scale") { SavePreferences(_settings with { UiScalePercent = _settings.UiScalePercent == 100 ? 150 : _settings.UiScalePercent == 150 ? 200 : 100 }); return true; }
        return false;
    }
}
