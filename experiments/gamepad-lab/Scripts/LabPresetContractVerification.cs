using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Additive production-seam witnesses for the fourth contract audit; no ordinary input injection.</summary>
public partial class LabRoot
{
    private readonly List<object> _contractObservations = new();
    private sealed class MissingCrateLabel : ILabDisplayLabels
    {
        private readonly SyntheticDisplayLabels _valid = new();
        public bool TryGetLabel(string id, string locale, out string label)
        { if (id == "crate") { label = ""; return false; } return _valid.TryGetLabel(id, locale, out label); }
    }
    private async Task VerifyIndependentLabels(string directory, string prefix)
    {
        (string Id, Vector2 Anchor, string Text)[] fixtures = [
            ("npc-ada", new(570, 350), "Синтетическая Ада"), ("npc-bram", new(710, 400), "Synthetic Bram"),
            ("enemy-ash", new(650, 290), "Синтетический противник Ash"), ("enemy-briar", new(770, 470), "Synthetic Briar"),
            ("door-north", new(640, 210), "Северная дверь"), ("door-east", new(930, 400), "East door"),
            ("crate", new(620, 480), "Синтетический контейнер с очень длинной подписью для проверки переноса текста"),
            ("tiny-gear", new(650, 492), "Tiny synthetic gear"), ("loose-vial", new(520, 455), "Синтетический флакон"),
            ("isolated-marker", new(320, 630), "Synthetic marker"), ("ally-iris", new(560, 450), "Союзник Ирис") ];
        var viewport = GetViewport().GetVisibleRect();
        Vector2 Expected(Vector2 anchor) => viewport.GetCenter() + (anchor - ToGodot(_cameraCenter)) * _camera.Zoom;
        var expected = fixtures.Where(f => Expected(f.Anchor).X >= 0 && Expected(f.Anchor).Y >= 0
            && Expected(f.Anchor).X < viewport.Size.X && Expected(f.Anchor).Y < viewport.Size.Y).ToArray();
        VerificationMode.Require(_highlight.Labels.Keys.Order().SequenceEqual(expected.Select(f => f.Id).Order()), "independent known fixture visibility/eligibility membership");
        foreach (var f in expected)
        {
            var actual = _courtyard.Candidates(_world).Single(t => t.Id == f.Id).Anchor;
            VerificationMode.Require(ToGodot(actual).DistanceTo(Expected(f.Anchor)) < .02f && _highlight.Labels[f.Id].Text == f.Text, "independent world-anchor/display-label " + f.Id);
        }
        var provider = _highlight.Provider;
        _highlight.Provider = new MissingCrateLabel(); _Process(0); await VerifyLabelGeometry();
        VerificationMode.Require(_highlight.Labels["crate"].Text == "[Missing synthetic display label: crate]", "missing provider fabricated original display name");
        await CaptureScenario("independent-label-provider", directory, prefix);
        _highlight.Provider = provider; _Process(0); await VerifyLabelGeometry();
        VerificationMode.Require(_highlight.Labels["crate"].Text == fixtures.Single(f => f.Id == "crate").Text, "provider restoration stale");
        _contractObservations.Add(new { oracle = "independent-literal-label-fixtures", expected = expected.Select(f => new { f.Id, text = f.Text, pixel = Expected(f.Anchor).ToString() }).ToArray() });
    }
    private void VerifyUseSurface(string slot)
    {
        _hud.HideModal(); ClearWorldIntents("SIMULATION physical full-ten selection"); VerifyNeutral();
        _hud.ShowSlots(_settings.Slots); ClearWorldIntents("SIMULATION modal boundary"); VerifyNeutral();
        VerifyChoose(slot); VerifyChoose("Use");
    }
    private void VerifyPick(string id)
    {
        // Literal fixture coordinates, not Candidates or PickVisible-derived expectations.
        var at = id switch { "ally-iris" => new Vector2(560, 450), "enemy-ash" => new Vector2(650, 290), _ => throw new ArgumentException("fixture id") };
        var pixel = _world.GetGlobalTransformWithCanvas() * at;
        Handle(new InputEventMouseButton { Position = pixel, ButtonIndex = MouseButton.Left, Pressed = true }, VerificationMode.Simulation);
        VerificationMode.Require(_actions.SelectedTargetId == id, "physical mouse target identity " + id);
    }
    private async Task VerifyPhysicalTargets(string directory, string prefix)
    {
        var saved = _settings;
        var uses = _actions.ResourceUses; VerifyPick("ally-iris"); VerifyUseSurface("slot-03");
        VerificationMode.Require(_actions.ResourceUses == uses + 1 && _actions.Journal.Last().TargetId == "ally-iris", "physical ally selection/use/resource");
        await CaptureScenario("physical-target-ally", directory, prefix);
        foreach (var variant in new[] { "removed", "revision", "hidden", "dead", "ineligible", "wrong-kind" })
        {
            _courtyard.ReloadTargets(); _hud.HideModal(); ClearWorldIntents("SIMULATION target fixture"); VerifyNeutral(); VerifyPick("enemy-ash");
            var target = _courtyard.WorldTargets.Single(t => t.Id == "enemy-ash");
            if (variant == "removed") _courtyard.RemoveTarget("enemy-ash");
            else _courtyard.SetTargetState(variant switch
            {
                "revision" => target with { Revision = 2 }, "hidden" => target with { Visible = false },
                "dead" => target with { Alive = false }, "ineligible" => target with { Interactive = false },
                _ => target with { Kind = "ally" }
            });
            uses = _actions.ResourceUses; VerifyUseSurface("slot-02"); _Process(0);
            VerificationMode.Require(_actions.ResourceUses == uses && _actions.Journal.Last().Kind == "TargetUnavailable"
                && _actions.Journal.Last().TargetId == "enemy-ash" && _actions.SelectedTargetId == "enemy-ash", "stale physical target substituted/consumed " + variant);
            _contractObservations.Add(new { oracle = "physical-target-denial", variant, before = uses, after = _actions.ResourceUses, last = _actions.Journal.Last() });
        }
        _courtyard.ReloadTargets(); _actions.Select(null); uses = _actions.ResourceUses;
        VerifyUseSurface("slot-04");
        VerificationMode.Require(_actions.Pending?.Kind == "ground" && _actions.ResourceUses == uses, "selecting ground edge confirmed itself");
        _Process(0); VerificationMode.Require(_actions.ResourceUses == uses, "held selecting ground edge confirmed itself");
        VerifyNeutral(); VerifyTap(JoyButton.B);
        VerificationMode.Require(_actions.Pending is null && _actions.ResourceUses == uses, "physical ground cancel consumed");
        VerifyUseSurface("slot-04"); VerifyNeutral(); VerifyTap(JoyButton.A);
        VerificationMode.Require(_actions.Pending is null && _actions.ResourceUses == uses + 1, "fresh physical ground confirm must consume once");
        _settings = saved with { Slots = saved.Slots.Assign("slot-09", null) }; uses = _actions.ResourceUses;
        VerifyUseSurface("slot-09"); VerificationMode.Require(_actions.ResourceUses == uses && _actions.Journal.Last().Kind == "EmptySlot", "empty slot fell back");
        // All ten IDs are selected/assigned/used through actual modal controls.
        foreach (var slot in Enumerable.Range(1, 10).Select(n => $"slot-{n:00}"))
        {
            _hud.ShowSlots(_settings.Slots); ClearWorldIntents("SIMULATION ten assignment"); VerifyNeutral();
            VerifyChoose(slot); VerifyChoose("Assign"); VerifyChoose("synthetic.item.self");
            uses = _actions.ResourceUses; VerifyChoose(slot); VerifyChoose("Use");
            VerificationMode.Require(_actions.ResourceUses == uses + 1 && _actions.Journal.Last().SlotId == slot && _actions.Journal.Last().TargetId == "actor", "physical full-ten identity/count " + slot);
            VerifyNeutral();
        }
        _settings = saved; _hud.HideModal(); ClearWorldIntents("SIMULATION target suite done"); VerifyNeutral();
        await CaptureScenario("physical-target-ground-ten", directory, prefix);
    }
    private async Task VerifySettingsWidgets(string directory, string prefix)
    {
        var saved = _settings; var actor = _actor.Position; var center = _cameraCenter; var camera = _cameraState.Mode; var zoom = _camera.Zoom; var uses = _actions.ResourceUses;
        _hud.ShowSettings(); ClearWorldIntents("SIMULATION binding widgets"); VerifyNeutral(); VerifyChoose("Create user copy"); VerifyNeutral(); VerifyChoose("Use user copy"); VerifyNeutral();
        var original = _settings.UserCopy!;
        var editableIndex = original.Bindings.Select((b, index) => (b, index)).Single(p => p.b.Context == "world" && p.b.Button == "x" && p.b.Modifiers.Count == 0).index;
        VerifyChoose("Edit bindings"); VerifyChoose(editableIndex.ToString());
        VerificationMode.Require(_hud.ModalKind == ModalKind.BindingButton, "actual binding chooser absent");
        // Direct occupied edit must be rejected without changing committed preferences.
        var savedBytes = System.IO.File.ReadAllBytes(_settingsStore.SelectedPath);
        VerifyChoose("y"); VerificationMode.Require(_settings.UserCopy!.Equals(original)
            && System.IO.File.ReadAllBytes(_settingsStore.SelectedPath).SequenceEqual(savedBytes), "conflicting widget edit applied");
        VerifyChoose(editableIndex.ToString()); VerifyChoose("Swap with y");
        var changed = _settings.UserCopy!;
        VerificationMode.Require(!changed.Equals(original) && changed.Bindings[editableIndex].Button == "y"
            && changed.Bindings.Any(b => b.Context == "world" && b.Modifiers.Count == 0 && b.Button == "x" && b.ActionId == original.Bindings.Single(b => b.Context == "world" && b.Modifiers.Count == 0 && b.Button == "y").ActionId), "explicit atomic widget swap absent");
        VerificationMode.Require(_hud.ChoiceRows.Any(row => row.Text.Contains("+y:")), "edited physical binding not displayed");
        await CaptureScenario("settings-binding-edit", directory, prefix);
        CloseModal(); VerifyNeutral(); _Process(0);
        VerificationMode.Require(_hud.PromptText.Contains("user-copy"), "template/copy HUD distinction absent");
        _hud.ShowSettings(); ClearWorldIntents("SIMULATION shoulder widget"); VerifyNeutral(); VerifyChoose("Swap shoulder roles");
        VerificationMode.Require(_settings.UserCopy!.LbModifierButton == "rb" && _settings.UserCopy.RbModifierButton == "lb", "shoulder roles widget swap absent");
        var swapped = _settings.UserCopy!;
        VerificationMode.Require(GamepadBindingValidator.Validate(swapped).Count == 0 && _settingsStore.Load() == _settings, "widget role transaction/store disagree");
        if (swapped.BasePresetId == "radial")
        {
            var wheelRows = swapped.Bindings.Where(b => b.ActionId == "wheel-open").ToArray();
            VerificationMode.Require(wheelRows.Length == 2 && wheelRows.All(b => b.Button == "rb" && b.Context == "world")
                && wheelRows[0].Modifiers.Count == 0 && wheelRows[1].Modifiers.SequenceEqual(new[] { "rb" }), "literal swapped widget wheel rows");
            CloseModal(); VerifyNeutral();
            VerifyLevels(buttons: [JoyButton.LeftShoulder]); _Process(0);
            VerificationMode.Require(!_presetState.WheelOpen && _presetStep?.Highlight == true && _hud.PromptText.Contains("LB labels")
                && _hud.PromptText.Contains("hold RB wheel"), "swapped physical LB/highlight/RB-wheel HUD roles");
            VerifyLevels(); _Process(0); VerifyLevels(buttons: [JoyButton.RightShoulder]); _Process(0);
            VerificationMode.Require(_presetState.WheelOpen && _presetStep?.Highlight == false && _cameraCenter == center && _cameraState.Mode == camera,
                "physical RB widget wheel/world invariant");
            VerifyTap(JoyButton.A); VerificationMode.Require(_presetState.WheelOpen && _presetState.WheelSector is null && _actions.ResourceUses == uses,
                "swapped widget no-selection activated");
            VerifyLevels(rightY: -1, buttons: [JoyButton.RightShoulder, JoyButton.LeftShoulder]); _Process(0);
            VerificationMode.Require(_presetState.WheelSector == 0 && _presetStep?.Highlight == true && _cameraCenter == center && _cameraState.Mode == camera,
                "swapped widget RB+LB/RS latch camera invariant");
            VerifyLevels(buttons: [JoyButton.RightShoulder, JoyButton.LeftShoulder]); _Process(0);
            VerificationMode.Require(_presetState.WheelSector == 0, "swapped widget latch lost at neutral");
            await CaptureScenario("settings-swapped-radial-roles", directory, prefix);
            VerifyTap(JoyButton.B); VerificationMode.Require(!_presetState.WheelOpen && _actions.ResourceUses == uses, "swapped widget B cancel consumed");
            VerifyLevels(); _Process(0); VerifyLevels(buttons: [JoyButton.RightShoulder]); _Process(0);
            VerificationMode.Require(_presetState.WheelOpen, "fresh physical RB failed reopening");
            VerifyLevels(); _Process(0); VerificationMode.Require(!_presetState.WheelOpen && _actions.ResourceUses == uses, "swapped RB release consumed");
            VerifyNeutral(); VerifyTap(JoyButton.Start);
        }
        VerifyNeutral(); VerifyChoose("Reset user copy"); VerificationMode.Require(_settings.UserCopy!.Equals(original), "widget reset did not restore immutable template copy");
        VerificationMode.Require(_actor.Position == actor && _cameraCenter == center && _cameraState.Mode == camera && _camera.Zoom == zoom && _actions.ResourceUses == uses, "binding widgets changed world/resources");
        _settings = saved; _hud.HideModal(); ClearWorldIntents("SIMULATION settings widget suite done"); VerifyNeutral();
    }
    private async Task VerifyEveryLifecycleControl(string directory, string prefix)
    {
        var saved = _settings; var actor = _actor; var center = _cameraCenter; var camera = _cameraState; var zoom = _camera.Zoom;
        JoyButton[] digital = [JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y, JoyButton.LeftShoulder, JoyButton.RightShoulder,
            JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.LeftStick, JoyButton.RightStick, JoyButton.Back, JoyButton.Start];
        var cases = digital.Select(b => (Name: b.ToString(), Buttons: new[] { b }, LS: 0f, RS: 0f, LT: 0f, RT: 0f))
            .Concat(new (string Name, JoyButton[] Buttons, float LS, float RS, float LT, float RT)[] { ("LT-only", Array.Empty<JoyButton>(), 0f, 0f, 1f, 0f), ("RT-only", Array.Empty<JoyButton>(), 0f, 0f, 0f, 1f),
                ("LS-only", Array.Empty<JoyButton>(), 1f, 0f, 0f, 0f), ("RS-only", Array.Empty<JoyButton>(), 0f, 1f, 0f, 0f),
                ("opposing-digital", new[] { JoyButton.DpadLeft, JoyButton.DpadRight }, 0f, 0f, 0f, 0f) });
        _explicitVerificationPoll = true;
        try
        {
            foreach (var boundary in new[] { "focus", "modal", "preset", "reset", "disconnect-reconnect", "device-change" })
            foreach (var test in cases)
            {
                _settings = saved with { ActivePresetId = "quick-slots" }; _hud.HideModal(); _actions.TurnBased = false;
                _input.SetStartupDevices([3]); ClearWorldIntents("SIMULATION individual lifecycle fixture"); VerifyNeutral();
                _actor = new(new Vec2(460, 610), new Vec2(1, 0)); SyncActor(); _camera.Zoom = new(2, 2); _cameraCenter = new(800, 800); _cameraState = new(CameraMode.Manual, false, false); ApplyCameraCenter();
                VerifyLevels(test.LS, test.RS, lt: test.LT, rt: test.RT, buttons: test.Buttons);
                if (boundary == "focus") { ApplyFocus(false); ApplyFocus(true); }
                else if (boundary == "modal") { _hud.ShowSettings(); ClearWorldIntents("SIMULATION modal entry"); CloseModal(); }
                else if (boundary == "preset") { _hud.ShowSettings(); ApplyPresetSetting("Preset Radial"); CloseModal(); }
                else if (boundary == "reset") { _hud.ShowSettings(); VerifyChoose("Reset defaults"); CloseModal(); }
                else if (boundary == "disconnect-reconnect") { OnJoyConnectionChanged(3, false); OnJoyConnectionChanged(3, true); Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(0); }
                else { OnJoyConnectionChanged(3, false); OnJoyConnectionChanged(5, true); Handle(new InputEventJoypadButton { Device = 5, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(0); VerificationMode.Require(_input.ActiveDevice == 5, "device-change fixture not selected5"); OnJoyConnectionChanged(5, false); OnJoyConnectionChanged(3, true); Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true }, VerificationMode.Simulation); _Process(0); }
                _hud.HideModal(); VerifyLevels(test.LS, test.RS, lt: test.LT, rt: test.RT, buttons: test.Buttons);
                var p = _actor.Position; var c = _cameraCenter; var z = _camera.Zoom; var mode = _cameraState.Mode; var uses = _actions.ResourceUses;
                _Process(.1); _Process(.1);
                _adapter.RefreshKeyboardLevels(_ => false); _adapter.RefreshControllerLevels(7, _ => 0, _ => false); OnJoyConnectionChanged(7, false); OnJoyConnectionChanged(7, true); _Process(.1);
                VerificationMode.Require(_input.ActiveDevice == 3 && _actor.Position == p && _cameraCenter == c && _cameraState.Mode == mode && _camera.Zoom == z && _actions.ResourceUses == uses
                    && _actions.Pending is null && !_presetState.WheelOpen && !_cameraState.RecenterArmed, "individual boundary ghost " + boundary + "/" + test.Name);
                // Cleared caches, malformed polls and callback/keyboard zeros are not real selected neutral frames.
                _adapter.Clear(); _adapter.Map(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0 }, true); _Process(0);
                foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, 1.1f })
                {
                    var accepted = _adapter.RefreshControllerLevels(3, a => a == JoyAxis.TriggerRight ? invalid : 0, _ => false);
                    VerificationMode.Require(!accepted && _adapter.SampleController(3, true)?.CompleteObservation == false, "invalid poll fixture accepted");
                    _Process(0);
                }
                VerificationMode.Require(_actor.Position == p && _cameraCenter == c && _camera.Zoom == z && _actions.ResourceUses == uses && !_cameraState.RecenterArmed, "incomplete/invalid poll rearmed " + boundary + "/" + test.Name);
                _settings = saved with { ActivePresetId = "quick-slots" }; VerifyNeutral(); VerifyTap(JoyButton.A);
                VerificationMode.Require(_actions.ResourceUses == uses + 1, "two selected frames then fresh A not exactly once " + boundary + "/" + test.Name);
                _contractObservations.Add(new { oracle = "individual-lifecycle", boundary, control = test.Name, actor = p, camera = c, mode = mode.ToString(), zoom = z.X, before = uses, after = _actions.ResourceUses });
            }
        }
        finally { _explicitVerificationPoll = false; _settings = saved; _hud.SetScale(saved.UiScalePercent); _actor = actor; SyncActor(); _cameraCenter = center; _cameraState = camera; _camera.Zoom = zoom; ApplyCameraCenter(); _hud.HideModal(); ClearWorldIntents("SIMULATION individual lifecycle done"); VerifyNeutral(); }
        await CaptureScenario("lifecycle-every-control", directory, prefix);
    }

    private async Task VerifyAllModalText(string directory, string prefix)
    {
        var saved = _settings;
        if (_d4SlotDiagnostic || _d4ContextDiagnostic) _hud.EnableD4Trace();
        foreach (var kind in new[] { ModalKind.Settings, ModalKind.Context, ModalKind.Slot, ModalKind.Assign, ModalKind.Hub,
            ModalKind.Battle, ModalKind.Follower, ModalKind.Bindings, ModalKind.BindingButton })
        {
            _hud.HideModal(); ClearWorldIntents("SIMULATION modal readable rows"); VerifyNeutral();
            if (kind is ModalKind.Settings or ModalKind.Bindings or ModalKind.BindingButton)
            {
                VerifyTap(JoyButton.Start); VerifyNeutral();
                if (kind is ModalKind.Bindings or ModalKind.BindingButton)
                {
                    VerifyChoose("Create user copy"); VerifyNeutral(); VerifyChoose("Edit bindings");
                    if (kind == ModalKind.BindingButton) VerifyChoose("0");
                }
            }
            else if (kind == ModalKind.Hub) VerifyTap(JoyButton.Back);
            else if (kind == ModalKind.Battle) VerifyTap(JoyButton.DpadUp);
            else if (kind == ModalKind.Follower) VerifyTap(JoyButton.DpadDown);
            else
            {
                // Keyboard C is the ordinary contextual/full-ten fallback, independent of the preset's face map.
                Handle(new InputEventKey { Keycode = Key.C, Pressed = true }, VerificationMode.Simulation);
                Handle(new InputEventKey { Keycode = Key.C, Pressed = false }, VerificationMode.Simulation); VerifyNeutral();
                if (kind is ModalKind.Slot or ModalKind.Assign) VerifyChoose("slot-01");
                if (kind == ModalKind.Assign) VerifyChoose("Assign");
            }
            VerificationMode.Require(_hud.ModalKind == kind, "actual modal surface entry " + kind); VerifyNeutral();
            _hud.ObserveD4Scroll("modal.title.entry", "original modal entry/navigation complete");
            await RenderedFrame();
            _hud.ObserveD4Scroll("modal.title.original-wait-1", "passive original first rendered wait");
            await RenderedFrame();
            _hud.ObserveD4Scroll("modal.title.original-wait-2", "passive original second rendered wait");
            var title = _hud.ModalTitle;
            _activeMatrixStep = new { phase = "modal-title-before-original-assertion", kind = kind.ToString(), instance = title.GetInstanceId(),
                text = title.Text, rect = RectEvidence(title.GetGlobalRect()), clip = RectEvidence(_hud.UsableModalClip),
                nativeMinimum = RectEvidence(new Rect2(Vector2.Zero, title.GetMinimumSize())), font = title.GetThemeFontSize("font_size"),
                predicates = new { enclosed = _hud.UsableModalClip.Encloses(title.GetGlobalRect()), hasA = title.Text.Contains("[A/Enter]"),
                    hasB = title.Text.Contains("[B/Esc]"), unclipped = !title.ClipText, heightAllocated = title.Size.Y >= title.GetMinimumSize().Y } };
            _hud.ObserveD4Scroll("modal.title.before-original-assertion", "passive original title predicate capture");
            try
            {
                VerificationMode.Require(_hud.UsableModalClip.Encloses(title.GetGlobalRect()) && title.Text.Contains("[A/Enter]")
                    && title.Text.Contains("[B/Esc]") && !title.ClipText && title.Size.Y >= title.GetMinimumSize().Y, "modal full title/fixed override " + kind);
                _contractObservations.Add(new { oracle = "modal-title", observation = _activeMatrixStep });
            }
            catch
            {
                if (_d4SlotDiagnostic)
                {
                    _d4SlotOriginalFailure = _activeMatrixStep;
                    using (var image = GetViewport().GetTexture().GetImage())
                        VerificationMode.Require(image.SavePng(System.IO.Path.Combine(directory, "slot-original-failure.png")) == Error.Ok, "Slot failure capture");
                    // Passive tail retains the original failure, never a later PASS.
                    for (var frame = 0; frame < 6; frame++)
                    { await RenderedFrame(); _hud.ObserveD4Scroll("modal.title.failed-passive-tail-" + frame, "passive after original failure; not acceptance"); }
                }
                throw;
            }
            // Diagnostic stops at the original fifth-tuple title assertion even
            // if it passes; it never claims whole-matrix acceptance or adds waits.
            if (_d4ContextDiagnostic && kind == ModalKind.Context) throw new D4ContextPrefixComplete();
            var rows = _hud.OptionCount;
            for (var direction = 1; direction >= -1; direction -= 2)
            for (var step = 0; step < rows; step++)
            {
                // Native mapped D-pad polling goes through InputState, MenuRepeater and the real HUD.
                if (step > 0)
                {
                    var old = _hud.SelectedIndex;
                    VerifyLevels(buttons: [direction > 0 ? JoyButton.DpadDown : JoyButton.DpadUp]); _Process(0);
                    VerifyLevels(); _Process(0);
                    VerificationMode.Require(_hud.SelectedIndex == (old + direction + rows) % rows, "native modal navigation " + kind);
                }
                await RenderedFrame(); await RenderedFrame();
                var row = _hud.ChoiceRows[_hud.SelectedIndex]; var clip = _hud.UsableModalClip;
                _activeMatrixStep = new { phase = "modal-text-passive-readback", kind = kind.ToString(), direction, index = _hud.SelectedIndex,
                    text = row.Text, rect = RectEvidence(row.GetGlobalRect()), clip = RectEvidence(clip), minimum = row.GetMinimumSize().ToString(),
                    scroll = _hud.ModalScroll.ScrollVertical, scrollRect = RectEvidence(_hud.ModalClip), font = row.GetThemeFontSize("font_size"), ui = _settings.UiScalePercent };
                VerificationMode.Require(clip.Encloses(row.GetGlobalRect()) && !row.ClipText && row.Size.Y >= row.GetMinimumSize().Y
                    && row.GetThemeFontSize("font_size") == 18 * _settings.UiScalePercent / 100, "full padded wrapped modal row " + kind + "/" + _hud.SelectedIndex);
                foreach (var other in _hud.ChoiceRows.Where(r => r != row)) VerificationMode.Require(!row.GetGlobalRect().Intersects(other.GetGlobalRect()), "modal rows overlap " + kind);
                _contractObservations.Add(new { oracle = "modal-text", kind = kind.ToString(), direction, index = _hud.SelectedIndex,
                    text = row.Text, rect = RectEvidence(row.GetGlobalRect()), clip = RectEvidence(clip), minimum = row.GetMinimumSize().ToString(), font = row.GetThemeFontSize("font_size"), frame = Engine.GetProcessFrames() });
            }
            await CaptureScenario("modal-text-" + kind, directory, prefix);
            if (_d4SlotDiagnostic && kind == ModalKind.Slot) throw new D4SlotPrefixComplete();
        }
        _settings = saved; _hud.SetScale(saved.UiScalePercent); _hud.HideModal(); ClearWorldIntents("SIMULATION modal text done"); VerifyNeutral();
        _activeMatrixStep = null;
    }
}
