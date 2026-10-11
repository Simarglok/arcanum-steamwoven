using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Explicit isolated restart probes of the ordinary preference store.</summary>
public partial class LabRoot
{
    private bool RunPresetPersistenceOperation(string operation, string report)
    {
        var axis = new LabSettings(CameraDeadzone: .3, ActorDeadzone: .1, CameraSensitivity: .75,
            ActorSensitivity: 1.5, InvertCameraX: false, InvertCameraY: true, MovementMode: MovementMode.NearbyGoal);
        var beforeActor = _actor.Position; var beforeCamera = _cameraCenter;
        switch (operation)
        {
            case "verify-migration":
                VerificationMode.Require(_settings == axis && _settingsStore.Diagnostic == "settings-migrated-v1", "literal legacy axis choices not migrated in Ready");
                break;
            case "verify-migrated-v2":
                VerificationMode.Require(_settings == axis && _settingsStore.Diagnostic == "settings-loaded-v2", "fresh v2 migration restart lost choices");
                break;
            case "write-contextual": case "write-quick-slots": case "write-radial":
                _settingsStore.Save(axis with { ActivePresetId = operation[6..] }); break;
            case "verify-contextual": case "verify-quick-slots": case "verify-radial":
                VerificationMode.Require(_settings == axis with { ActivePresetId = operation[7..] }, "fresh selected preset/axis restart mismatch"); break;
            case "write-copy":
                var profile = BuiltInGamepadPresets.Get("contextual").CreateCopy();
                // A/X physical digital swap and supported shoulder-role swap, not template mutation.
                profile = profile.WithBindings(profile.Bindings.Select(b => b.Context == "world"
                    ? new GamepadBinding(b.Context, b.Button switch { "a" => "x", "x" => "a", _ => b.Button }, b.Modifiers, b.ActionId, b.SlotId) : b))
                    .WithShoulderRoles("rb", "lb");
                VerificationMode.Require(GamepadBindingValidator.Validate(profile).Count == 0, "probe copy rejected");
                _settingsStore.Save(axis with { ActivePresetId = "user-copy", UserCopy = profile,
                    Slots = LabSlots.Default.Assign("slot-09", "synthetic.spell.ally").Assign("slot-10", null), UiScalePercent = 200 }); break;
            case "verify-copy":
                VerificationMode.Require(_settings.ActivePresetId == "user-copy" && _settings.UserCopy?.LbModifierButton == "rb"
                    && _settings.UserCopy.RbModifierButton == "lb" && _settings.UiScalePercent == 200
                    && _settings.Slots.Assignments["slot-09"] == "synthetic.spell.ally" && _settings.Slots.Assignments["slot-10"] is null,
                    "fresh copy/shoulder/ten-slot restart mismatch");
                var row = _settings.UserCopy!.Bindings.Single(b => b.Context == "world" && b.Modifiers.Count == 0 && b.Button == "x");
                VerificationMode.Require(row.ActionId == "interact", "literal physical X copy binding lost"); break;
            case "reject-copy":
                var saved = System.IO.File.ReadAllBytes(_settingsStore.SelectedPath);
                var original = _settings.UserCopy!;
                _hud.ShowSettings();
                TryApplyCopy(original.WithBindings(original.Bindings.Concat(new[] { original.Bindings[0] })));
                VerificationMode.Require(System.IO.File.ReadAllBytes(_settingsStore.SelectedPath).SequenceEqual(saved)
                    && _settings.UserCopy == original && _log.Last().Contains("Rejected binding"), "invalid copy replaced committed valid profile"); break;
            case "reset-copy":
                _hud.ShowSettings(); ApplyPresetSetting("Reset user copy"); break;
            case "verify-reset-copy":
                VerificationMode.Require(_settings.ActivePresetId == "user-copy" && _settings.UserCopy?.Equals(BuiltInGamepadPresets.Get("contextual").CreateCopy()) == true
                    && _settings.UiScalePercent == 200 && _settings.Slots.Assignments["slot-10"] is null, "fresh reset copy restart mismatch"); break;
            case "write-radial-widget-copy":
                _input.SetStartupDevices([3]); VerifyNeutral(); VerifyTap(JoyButton.Start); VerifyNeutral();
                VerifyChoose("Preset Radial"); VerifyNeutral(); VerifyChoose("Create user copy"); VerifyNeutral();
                VerifyChoose("Use user copy"); VerifyNeutral();
                var radialIndex = _settings.UserCopy!.Bindings.Select((b, n) => (b, n))
                    .Single(p => p.b.Context == "world" && p.b.Button == "x" && p.b.Modifiers.Count == 0).n;
                VerifyChoose("Edit bindings"); VerifyChoose(radialIndex.ToString()); VerifyChoose("Swap with y");
                CloseModal(); VerifyNeutral(); VerifyTap(JoyButton.Start); VerifyNeutral(); VerifyChoose("Swap shoulder roles");
                VerificationMode.Require(_settings.UserCopy!.LbModifierButton == "rb" && _settings.UserCopy.RbModifierButton == "lb", "actual Radial widget roles not saved");
                break;
            case "verify-radial-widget-copy":
                VerificationMode.Require(_settings.ActivePresetId == "user-copy" && _settings.UserCopy?.BasePresetId == "radial"
                    && _settings.UserCopy.LbModifierButton == "rb" && _settings.UserCopy.RbModifierButton == "lb", "fresh Radial widget copy roles/base lost");
                var radialCopy = _settings.UserCopy!;
                var wheelRows = radialCopy.Bindings.Where(b => b.ActionId == "wheel-open").ToArray();
                VerificationMode.Require(wheelRows.Length == 2 && wheelRows.All(b => b.Context == "world" && b.Button == "rb")
                    && wheelRows[0].Modifiers.Count == 0 && wheelRows[1].Modifiers.SequenceEqual(new[] { "rb" })
                    && radialCopy.Bindings.Single(b => b.Context == "world" && b.Button == "y" && b.Modifiers.Count == 0).ActionId == "inspect"
                    && radialCopy.Bindings.Single(b => b.Context == "world" && b.Button == "x" && b.Modifiers.Count == 0).ActionId == "context", "literal Radial wheel/custom rows restart lost");
                break;
            case "reject-radial-widget-copy":
                var radialBytes = System.IO.File.ReadAllBytes(_settingsStore.SelectedPath); var validRadial = _settings;
                _input.SetStartupDevices([3]); VerifyNeutral(); VerifyTap(JoyButton.Start); VerifyNeutral();
                var rejectedIndex = _settings.UserCopy!.Bindings.Select((b, n) => (b, n))
                    .Single(p => p.b.Context == "world" && p.b.Button == "y" && p.b.Modifiers.Count == 0).n;
                VerifyChoose("Edit bindings"); VerifyChoose(rejectedIndex.ToString()); VerifyChoose("x");
                VerificationMode.Require(_settings == validRadial && System.IO.File.ReadAllBytes(_settingsStore.SelectedPath).SequenceEqual(radialBytes), "occupied Radial widget changed saved bytes");
                var malformedWheel = validRadial.UserCopy!.WithBindings(validRadial.UserCopy.Bindings.Select(b => b.ActionId == "wheel-open"
                    ? new GamepadBinding(b.Context, "lb", b.Modifiers, b.ActionId, b.SlotId) : b));
                var refused = false;
                try { _settingsStore.Save(validRadial with { UserCopy = malformedWheel }); } catch (ArgumentException) { refused = true; }
                VerificationMode.Require(refused && _settings == validRadial && System.IO.File.ReadAllBytes(_settingsStore.SelectedPath).SequenceEqual(radialBytes), "invalid old-wheel save mutated preference bytes");
                break;
            case "reset-radial-widget-copy":
                _input.SetStartupDevices([3]); VerifyNeutral(); VerifyTap(JoyButton.Start); VerifyNeutral(); VerifyChoose("Reset user copy"); break;
            case "verify-reset-radial-widget-copy":
                VerificationMode.Require(_settings.ActivePresetId == "user-copy" && _settings.UserCopy?.Equals(BuiltInGamepadPresets.Get("radial").CreateCopy()) == true,
                    "fresh actual Radial widget reset did not restore immutable base"); break;
            case "verify-corrupt":
                VerificationMode.Require(_settings == LabSettings.Default && _settingsStore.Diagnostic == "settings-malformed", "corrupt v2 did not safely default without legacy fallback"); break;
            case "save-io-failure":
                var bytes = System.IO.File.ReadAllBytes(_settingsStore.SelectedPath); var failed = false;
                try { _settingsStore.Save(axis); }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { failed = true; }
                VerificationMode.Require(failed && System.IO.File.ReadAllBytes(_settingsStore.SelectedPath).SequenceEqual(bytes), "atomic save failure lost prior committed bytes"); break;
            default: return false;
        }
        VerificationMode.Require(_actor.Position == beforeActor && _cameraCenter == beforeCamera, "persistence probe moved world state");
        var encoded = System.IO.File.ReadAllBytes(_settingsStore.SelectedPath);
        System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { schema = "arcanum-gamepad-lab-persistence-v1", task = "ARC-GPAD-005",
            candidateCommit = OS.GetEnvironment("ARCANUM_GAMEPAD_CANDIDATE"), operation, status = "passed", isolatedTestStorage = true,
            origin = OS.HasFeature("template") ? "exported" : "source", diagnostic = _settingsStore.Diagnostic,
            storedJson = System.Text.Encoding.UTF8.GetString(encoded), sha256 = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant(),
            actorUnchanged = true, cameraUnchanged = true, provenance = VerificationMode.Simulation }));
        GD.Print("GamepadLab isolated persistence: PASS " + operation); GetTree().Quit(0); return true;
    }
}
