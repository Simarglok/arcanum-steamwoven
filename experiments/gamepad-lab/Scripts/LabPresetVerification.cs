using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Input;
using GamepadLab.Core;
using Godot;

namespace GamepadLab;

/// <summary>Explicit non-headless production-scene observations, never an ordinary/physical-use claim.</summary>
public partial class LabRoot
{
    private bool _d4ContextDiagnostic;
    private sealed class D4ContextPrefixComplete : Exception { }
    private object? _activeMatrixStep;
    private readonly List<object> _scrollObservations = new();
    private readonly List<object> _scenarioCaptures = new();
    // Reached only AFTER the original Require path, never a substitute oracle.
    private readonly List<string> _o5Completed = new();
    private void O5Reached(string id) => _o5Completed.Add(id);
    private async Task CaptureScenario(string name, string directory, string prefix)
    {
        UpdateHud(); await RenderedFrame();
        var file = prefix + "-" + name + ".png";
        using var image = GetViewport().GetTexture().GetImage();
        VerificationMode.Require(image.SavePng(System.IO.Path.Combine(directory, file)) == Error.Ok, "scenario capture failed");
        IEnumerable<Node> Descendants(Node n) => n.GetChildren().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        _scenarioCaptures.Add(new { scenario = name, frame = Engine.GetProcessFrames(), generation = _highlight.LayoutGeneration,
            actualViewport = RectEvidence(GetViewport().GetVisibleRect()), raster = new[] { image.GetWidth(), image.GetHeight() },
            prompt = _hud.PromptText, modal = _hud.ModalKind.ToString(),
            controls = Descendants(_hud).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => new { text = l.Text,
                rect = RectEvidence(l.GetGlobalRect()), font = l.GetThemeFontSize("font_size") }).ToArray(),
            labels = _highlight.PassiveGeometryEvidence(), bounds = _hud.LayoutBounds.ToDictionary(p => p.Key, p => RectEvidence(p.Value)),
            wheel = new { open = _presetState.WheelOpen, sector = _presetState.WheelSector },
            journal = _actions.Journal.TakeLast(8).ToArray(), capture = file,
            sha256 = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory, file)))).ToLowerInvariant() });
    }
    private async Task RenderedFrame() => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    // Independent passive oracle: no minimum/line-count query or cached expected sizes.
    private string NativeAllocation() => JsonSerializer.Serialize(new
    {
        generation = _highlight.LayoutGeneration, stack = _highlight.Overflow.GetChild<Control>(0).Size.ToString(),
        max = _highlight.Overflow.GetVScrollBar().MaxValue, page = _highlight.Overflow.GetVScrollBar().Page,
        rows = _highlight.Labels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key,
            instance = p.Value.GetInstanceId(), parent = p.Value.GetParent().GetInstanceId(), index = p.Value.GetIndex(),
            size = p.Value.Size.ToString(), position = p.Value.Position.ToString() }).ToArray()
    });
    private async Task AwaitLabelSettlement()
    {
        var generation = _highlight.LayoutGeneration;
        for (var i = 0; i < 8 && _highlight.SettlementStatus == "pending"; i++) await RenderedFrame();
        VerificationMode.Require(generation == _highlight.LayoutGeneration && _highlight.SettledGeneration == generation && _highlight.SettlementStatus == "settled",
            "production label generation NOT_SETTLED " + JsonSerializer.Serialize(_highlight.SettlementEvidence()));
        var before = NativeAllocation(); await RenderedFrame();
        VerificationMode.Require(before == NativeAllocation(), "native allocation changed after settlement");
        await RenderedFrame(); VerificationMode.Require(before == NativeAllocation(), "native second passive allocation changed after settlement");
    }
    private Rect2 UsableOverflowClip()
    {
        var overflow = _highlight.Overflow; var panel = overflow.GetThemeStylebox("panel");
        var rect = overflow.GetGlobalRect();
        return new Rect2(rect.Position + new Vector2(panel.GetMargin(Side.Left), panel.GetMargin(Side.Top)),
            rect.Size - panel.GetMinimumSize() - new Vector2(overflow.GetVScrollBar().Size.X, 0));
    }
    private async Task VerifyNativeWheelScroll(Label[] rows)
    {
        var overflow = _highlight.Overflow; var allocation = NativeAllocation();
        overflow.ScrollVertical = 0; await RenderedFrame();
        var first = rows.OrderBy(r => r.Position.Y).First(); var last = rows.OrderBy(r => r.Position.Y).Last();
        VerificationMode.Require(UsableOverflowClip().Encloses(first.GetGlobalRect()), "native wheel baseline first row unreadable");
        var pos = UsableOverflowClip().GetCenter();
        using var motion = new InputEventMouseMotion { Position = pos, GlobalPosition = pos };
        GetViewport().PushInput(motion, true);
        var before = overflow.GetVScrollBar().Value;
        for (var i = 0; i < 64 && !UsableOverflowClip().Encloses(last.GetGlobalRect()); i++)
        {
            using var press = new InputEventMouseButton { Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 };
            GetViewport().PushInput(press, true); await RenderedFrame();
        }
        _scrollObservations.Add(new { phase = "viewport-native-wheel", generation = _highlight.LayoutGeneration,
            before, after = overflow.GetVScrollBar().Value, passive = _highlight.PassiveGeometryEvidence() });
        VerificationMode.Require(UsableOverflowClip().Encloses(last.GetGlobalRect()), "native viewport wheel did not reach full last row");
        VerificationMode.Require(allocation == NativeAllocation(), "native wheel changed shape/allocation");
        overflow.ScrollVertical = 0; await RenderedFrame();
    }
    private static object RectEvidence(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, width = r.Size.X, height = r.Size.Y };
    private void VerifyTap(JoyButton button)
    {
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = button, Pressed = true }, VerificationMode.Simulation);
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = button, Pressed = false }, VerificationMode.Simulation);
    }
    private void VerifyNeutral()
    {
        _adapter.Clear(); _adapter.RefreshKeyboardLevels(_ => false);
        _adapter.RefreshControllerLevels(3, _ => 0, _ => false);
        _Process(0); _Process(0);
    }
    private void VerifyChoose(string choice)
    {
        for (var i = 0; i < _hud.OptionCount && _hud.SelectedOption != choice; i++) _hud.Navigate(1);
        VerificationMode.Require(_hud.SelectedOption == choice, $"missing production option {choice}");
        VerifyTap(JoyButton.A);
    }
    private async Task VerifyLabelGeometry()
    {
        await AwaitLabelSettlement();
        var viewport = GetViewport().GetVisibleRect();
        var adjacent = _highlight.Labels.Values.Where(l => l.GetParent() == _highlight).ToArray();
        foreach (var label in adjacent)
        {
            var rect = label.GetGlobalRect();
            VerificationMode.Require(rect.Size.X > 0 && rect.Size.Y > 0 && viewport.Encloses(rect), "actual adjacent label containment " + label.Text);
            VerificationMode.Require(!_highlight.Overflow.Visible || !rect.Intersects(_highlight.Overflow.GetGlobalRect()), "label overlaps actual overflow clip");
            foreach (var other in adjacent.Where(l => l != label)) VerificationMode.Require(!rect.Intersects(other.GetGlobalRect()), "actual adjacent label overlap");
        }
        var overflow = _highlight.Overflow;
        var rows = _highlight.Labels.Values.Where(l => l.GetParent() is VBoxContainer).ToArray();
        if (rows.Length == 0) return;
        VerificationMode.Require(overflow.Visible && viewport.Encloses(overflow.GetGlobalRect()), "overflow clip outside viewport");
        // Both traversals prevent earlier Ensure calls incidentally masking readiness.
        foreach (var row in rows.Concat(rows.Reverse()))
        {
            foreach (var other in rows.Where(l => l != row)) VerificationMode.Require(!row.GetGlobalRect().Intersects(other.GetGlobalRect()), "overflow rows overlap");
            var generation = _highlight.LayoutGeneration; var allocation = NativeAllocation();
            var pre = _highlight.PassiveGeometryEvidence();
            overflow.EnsureControlVisible(row);
            var post = _highlight.PassiveGeometryEvidence(); await RenderedFrame();
            var rendered = _highlight.PassiveGeometryEvidence(); await RenderedFrame();
            _scrollObservations.Add(new { phase = "ensure-once", target = row.Text, generation, pre, post, rendered, followup = _highlight.PassiveGeometryEvidence() });
            VerificationMode.Require(generation == _highlight.LayoutGeneration && allocation == NativeAllocation(), "row allocation changed after Ensure");
            var rect = row.GetGlobalRect(); var clip = UsableOverflowClip();
            VerificationMode.Require(clip.Encloses(rect), $"overflow row full-text scroll reachability {row.Text}; row={rect}; clip={clip}; scroll={overflow.ScrollVertical}; max={overflow.GetVScrollBar().MaxValue}; page={overflow.GetVScrollBar().Page}");
            VerificationMode.Require(row.Size.Y >= row.GetMinimumSize().Y && !row.ClipText && row.Visible, "overflow text truncated");
        }
        await VerifyNativeWheelScroll(rows);
        overflow.ScrollVertical = 0; await LayoutFrames();
    }
    private static int RecursiveNodeCount(Node node) => 1 + node.GetChildren().Sum(RecursiveNodeCount);
    private async Task VerifyLayoutBoundaries(Vector2I originalSize, int ui)
    {
        var actor = _actor.Position; var center = _cameraCenter; var mode = _cameraState.Mode;
        var viewport = (SubViewport)GetViewport();
        var before = _highlight.Labels.ToDictionary(p => p.Key, p => p.Value.GetGlobalRect());
        // Shrink width for B so the existing center remains legal under the
        // independent map-edge camera clamp. Expanding height can necessarily
        // clamp a boundary center and is not a no-state-change layout fixture.
        foreach (var size in new[] { new Vector2I(originalSize.X - 64, originalSize.Y), originalSize })
        {
            _activeMatrixStep = new { phase = "same-scene-resize", size };
            viewport.Size = size; await RenderedFrame(); _Process(0); await VerifyLabelGeometry();
            VerificationMode.Require(_actor.Position == actor && _cameraCenter == center && _cameraState.Mode == mode,
                "resize changed actor/camera state");
        }
        foreach (var p in before) VerificationMode.Require(_highlight.Labels[p.Key].GetGlobalRect() == p.Value, "resize A-B-A label allocation did not return");
        // Start and cancel a genuine new pending generation before any postdraw callback.
        _settings = _settings with { UiScalePercent = ui == 200 ? 100 : 200 }; _Process(0);
        VerificationMode.Require(_highlight.SettlementStatus == "pending", "missing pending generation for release test");
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.RightShoulder, Pressed = false }, VerificationMode.Simulation); _Process(0);
        var cancelled = _highlight.LayoutGeneration; await RenderedFrame(); await RenderedFrame();
        VerificationMode.Require(_highlight.Labels.Count == 0 && _highlight.LayoutGeneration == cancelled && _highlight.SettledGeneration == cancelled,
            "released generation resurrected labels");
        _settings = _settings with { UiScalePercent = ui }; _hud.SetScale(ui);
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.RightShoulder, Pressed = true }, VerificationMode.Simulation); _Process(0);
        await VerifyLabelGeometry();
        _activeMatrixStep = null;
    }
    private async Task<object> VerifyTuple(Vector2I size, string preset, int ui, float zoom, string evidenceDir)
    {
        var prefix = $"{preset}-{size.X}x{size.Y}-ui{ui}-zoom{zoom:F1}";
        _o5Completed.Clear();
        _injectedObservations = true; SetProcess(false); SetProcessInput(false);
        _focused = true; _settings = LabSettings.Default with { ActivePresetId = preset, UiScalePercent = ui };
        _hud.SetScale(ui); _camera.Zoom = new(zoom, zoom); _input.SetStartupDevices([3]);
        ClearWorldIntents("SIMULATION tuple start"); VerifyNeutral();
        _cameraState = new(CameraMode.Manual, false, false); _cameraCenter = CameraBounds().ClampCenter(new(635, 475)); ApplyCameraCenter();
        var actor = _actor.Position; var center = _cameraCenter;
        // Startup-device identity alone does not qualify gamepad presentation.
        // Two fresh R3 toggles qualify the real adapter while restoring unlocked state.
        VerifyTap(JoyButton.RightStick); VerifyTap(JoyButton.RightStick);
        await LayoutFrames();
        VerificationMode.Require(GetViewport().GetVisibleRect().Size == new Vector2(size.X, size.Y), "actual logical viewport mismatch");
        var trace = new List<string>();
        // Literal test-owned face/bank expectations, invoking the production event adapter/router.
        string[][] expected = preset == "quick-slots"
            ? [["slot-01", "slot-02", "slot-03", "slot-04"], ["slot-05", "slot-06", "slot-07", "slot-08"], ["interact", "inspect", "context", "back"], ["slot-05", "slot-06", "slot-07", "slot-08"]]
            : [["interact", "inspect", "context", "back"], ["slot-01", "slot-02", "slot-03", "slot-04"], ["interact", "inspect", "context", "back"], ["slot-05", "slot-06", "slot-07", "slot-08"]];
        if (preset == "radial") expected = [expected[0], expected[0], expected[0], expected[0]];
        JoyButton[] faces = [JoyButton.A, JoyButton.X, JoyButton.Y, JoyButton.B];
        for (var mask = 0; mask < 4; mask++)
        for (var f = 0; f < 4; f++)
        {
            if (preset == "radial" && mask % 2 == 1) continue; // LB is a wheel domain, never contextual faces.
            _hud.HideModal(); ClearWorldIntents("SIMULATION face case"); VerifyNeutral();
            var modifier = (mask % 2 == 1 ? ControllerButtons.LB : 0) | (mask >= 2 ? ControllerButtons.RB : 0);
            // Full poll-only simultaneous modifier snapshot; radial LB-open is tested separately below.
            _adapter.RefreshControllerLevels(3, _ => 0, b => b == JoyButton.LeftShoulder && mask % 2 == 1 || b == JoyButton.RightShoulder && mask >= 2);
            var raw = _adapter.SampleController(3, true)!;
            _input.Reduce(raw, Context(0, false));
            _Process(0);
            VerificationMode.Require(new[] { "A", "X", "Y", "B" }.Select((glyph, index) => _hud.PromptText.Contains("[" + glyph + "] " + expected[mask][index])).All(matches => matches), "actual HUD all four face diamond/bank mismatch");
            if (f == 0) await CaptureScenario("bank-" + mask, evidenceDir, prefix);
            var uses = _actions.ResourceUses; var seq = _actions.EventSequence;
            VerifyTap(faces[f]);
            var token = expected[mask][f];
            if (token == "context") VerificationMode.Require(_hud.ModalKind == ModalKind.Context && _hud.OptionCount == 12, "context enters full ten surface");
            else if (token == "back") VerificationMode.Require(_actions.ResourceUses == uses, "back not resource use");
            else VerificationMode.Require(_actions.Journal.Last().SlotId == token || _actions.Journal.Last().ActionId == token, "literal face routing mismatch " + token);
            trace.Add($"mask{mask}/{faces[f]}:{token}");
            _Process(0); VerificationMode.Require(_actions.ResourceUses <= uses + 1, "selecting edge repeated/confirmed itself");
        }
        O5Reached("faces");
        // Actual fixed modal A/B and scroll/navigation to09/10 assignment and use.
        _hud.HideModal(); ClearWorldIntents("SIMULATION slots"); VerifyNeutral();
        _hud.ShowSlots(_settings.Slots); ClearWorldIntents("SIMULATION full-ten boundary"); VerifyNeutral();
        await CaptureScenario("full-ten-modal-override", evidenceDir, prefix);
        foreach (var slot in new[] { "slot-09", "slot-10" })
        {
            VerifyChoose(slot); VerifyChoose("Assign"); VerifyChoose("synthetic.item.self");
            VerificationMode.Require(_settings.Slots.Assignments[slot] == "synthetic.item.self", "assignment identity");
            VerifyChoose(slot); var uses = _actions.ResourceUses; VerifyChoose("Use");
            VerificationMode.Require(!_hud.ModalVisible && _actions.ResourceUses == uses + 1 && _actions.Journal.Last().SlotId == slot, "full-ten use identity/effect");
            VerifyNeutral(); _hud.ShowSlots(_settings.Slots); ClearWorldIntents("SIMULATION next slot"); VerifyNeutral();
            trace.Add(slot + ":assign/use:self/actor/1");
        }
        var cancelUses = _actions.ResourceUses; VerifyTap(JoyButton.B);
        VerificationMode.Require(!_hud.ModalVisible && _actions.ResourceUses == cancelUses, "modal B must not consume QuickSlots04"); VerifyNeutral();
        // Exact target identity and lock/unlock through the ordinary physical route.
        var ally = _courtyard.WorldTargets.Single(t => t.Id == "ally-iris");
        _actions.Select(ally, false); VerifyTap(JoyButton.RightStick);
        VerificationMode.Require(_actions.Locked && _actions.SelectedTargetId == "ally-iris", "R3 did not lock exact ally");
        _settings = _settings with { Slots = _settings.Slots.Assign("slot-03", "synthetic.spell.ally") };
        var allyUses = _actions.ResourceUses;
        Dispatch(new("quick-slot", "slot-03", _presetState.NextEdgeId)); _presetState = _presetState with { NextEdgeId = _presetState.NextEdgeId + 1 };
        VerificationMode.Require(_actions.ResourceUses == allyUses + 1 && _actions.Journal.Last().TargetId == "ally-iris", "ally activation target/resource mismatch");
        _Process(0); VerificationMode.Require(_hud.PromptText.Contains("lock True") && _hud.PromptText.Contains("ally-iris"), "HUD target/lock readback missing");
        await CaptureScenario("target-lock-journal", evidenceDir, prefix);
        VerifyTap(JoyButton.RightStick); VerificationMode.Require(!_actions.Locked, "R3 unlock failed");
        // Pending ground selection cannot confirm its own edge, stale selected target never substitutes.
        _actions.Select(null); _settings = _settings with { Slots = _settings.Slots.Assign("slot-10", "synthetic.spell.ground") };
        Dispatch(new("quick-slot", "slot-10", _presetState.NextEdgeId)); _presetState = _presetState with { NextEdgeId = _presetState.NextEdgeId + 1 };
        var pendingUses = _actions.ResourceUses;
        VerificationMode.Require(_actions.Pending?.Kind == "ground" && _actions.ResourceUses == pendingUses, "ground preview only");
        VerifyTap(JoyButton.B); VerificationMode.Require(_actions.Pending is null && _actions.ResourceUses == pendingUses, "ground cancel no effect");
        Dispatch(new("quick-slot", "slot-10", _presetState.NextEdgeId)); _presetState = _presetState with { NextEdgeId = _presetState.NextEdgeId + 1 };
        _Process(0); await CaptureScenario("pending-ground", evidenceDir, prefix);
        VerifyTap(JoyButton.A);
        VerificationMode.Require(_actions.Pending is null && _actions.ResourceUses == pendingUses + 1 && _actions.Journal.Last().Kind == "MockActivated", "fresh A did not confirm ground exactly once");
        var ash = _courtyard.WorldTargets.Single(t => t.Id == "enemy-ash"); _actions.Select(ash); _courtyard.RemoveTarget("enemy-ash"); RefreshSelection();
        Dispatch(new("quick-slot", "slot-02", _presetState.NextEdgeId));
        VerificationMode.Require(_actions.Journal.Last().Kind == "TargetUnavailable" && _actions.Journal.Last().TargetId == "enemy-ash", "stale target replaced by presentation");
        _courtyard.ReloadTargets(); trace.Add("pending:cancel/0;stale:enemy-ash/denied/0");
        // Literal grid/AP oracle via actual battle surface and LS polling.
        _hud.HideModal(); ClearWorldIntents("SIMULATION grid"); VerifyNeutral();
        VerifyTap(JoyButton.DpadUp); VerifyNeutral(); VerifyChoose("Turn-based comparison"); VerifyNeutral();
        _adapter.RefreshControllerLevels(3, a => a == JoyAxis.LeftX ? 1 : 0, _ => false); _Process(0);
        VerificationMode.Require(_actions.GridActor == new Vec2(4, 4) && _actions.GridPreview == new Vec2(5, 4) && _actions.APSpent == 0, "literal grid preview mismatch");
        await CaptureScenario("grid-preview", evidenceDir, prefix);
        _adapter.RefreshControllerLevels(3, _ => 0, _ => false); _Process(0); VerifyTap(JoyButton.B);
        VerificationMode.Require(_actions.GridActor == new Vec2(4, 4) && _actions.APSpent == 0, "grid cancel changed actor/AP");
        _adapter.RefreshControllerLevels(3, a => a == JoyAxis.LeftX ? 1 : 0, _ => false); _Process(0);
        _adapter.RefreshControllerLevels(3, _ => 0, _ => false); _Process(0); VerifyTap(JoyButton.A);
        VerificationMode.Require(_actions.GridActor == new Vec2(5, 4) && _actions.APSpent == 1 && _actions.Journal.Last().APCost == 1, "grid fresh confirm not AP1");
        _actions.TurnBased = false; trace.Add("grid:4,4->5,4/cancel0/confirmAP1");
        O5Reached("modal-slots-target-grid");
        // Wheel production ownership, latched choice, no-selection and cancel; other presets remain contextual.
        _hud.HideModal(); ClearWorldIntents("SIMULATION wheel"); VerifyNeutral();
        if (preset == "radial")
        {
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.LeftShoulder, Pressed = true }, VerificationMode.Simulation);
            VerifyTap(JoyButton.A); VerificationMode.Require(_presetState.WheelOpen && _presetState.WheelSector is null, "no default wheel choice");
            await CaptureScenario("wheel-no-selection", evidenceDir, prefix);
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightY, AxisValue = -1 }, VerificationMode.Simulation);
            _Process(0); VerificationMode.Require(_presetState.WheelSector == 0 && _cameraState.Mode == CameraMode.Manual && _cameraCenter == center, "wheel owns RS without camera state mutation");
            Handle(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.RightY, AxisValue = 0 }, VerificationMode.Simulation);
            _Process(0); VerificationMode.Require(_presetState.WheelSector == 0, "wheel neutral latch");
            await CaptureScenario("wheel-up01-latched", evidenceDir, prefix);
            var uses = _actions.ResourceUses; VerifyTap(JoyButton.A); VerificationMode.Require(!_presetState.WheelOpen && _actions.ResourceUses == uses + 1, "wheel01 choose once");
            _Process(0); VerificationMode.Require(!_presetState.WheelOpen, "held LB reopened wheel");
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.LeftShoulder, Pressed = false }, VerificationMode.Simulation); _Process(0);
            Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.LeftShoulder, Pressed = true }, VerificationMode.Simulation); VerifyTap(JoyButton.B);
            VerificationMode.Require(!_presetState.WheelOpen, "wheel B cancel");
            trace.Add("wheel:no-selection/up01/latch/choose1/fresh-LB/cancel");
        }
        await VerifyWheelExtremes(evidenceDir, prefix);
        O5Reached("wheel");
        await VerifyLifecycleProjection(evidenceDir, prefix);
        O5Reached("lifecycle-projection");
        await VerifyPhysicalTargets(evidenceDir, prefix);
        O5Reached("physical-targets");
        await VerifySettingsWidgets(evidenceDir, prefix);
        O5Reached("settings-widgets");
        await VerifyEveryLifecycleControl(evidenceDir, prefix);
        O5Reached("lifecycle-controls");
        await VerifyAllModalText(evidenceDir, prefix);
        O5Reached("modal-text");
        ClearWorldIntents("SIMULATION labels"); VerifyNeutral();
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.RightShoulder, Pressed = true }, VerificationMode.Simulation); _Process(0); await LayoutFrames();
        var projected = _courtyard.Candidates(_world);
        var visibleIds = projected.Where(t => t.Visible && t.Alive && t.ContextValid && new Rect(0, 0, size.X, size.Y).Contains(t.Anchor)).Select(t => t.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        VerificationMode.Require(_highlight.Labels.Keys.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(visibleIds), "projected visible label membership");
        VerificationMode.Require(!_highlight.Labels.ContainsKey("hidden-interactive") && !_highlight.Labels.ContainsKey("scenery"), "hidden/scenery revealed");
        await VerifyLabelGeometry();
        foreach (var label in _highlight.Labels.Values)
        {
            VerificationMode.Require(label.GetThemeFontSize("font_size") == 16 * ui / 100, "UI-scale label font");
            foreach (var c in label.Text.Where(c => c >= '\u0400' && c <= '\u04ff')) VerificationMode.Require(label.GetThemeFont("font").HasChar(c), "Cyrillic glyph missing");
        }
        var adjacent = _highlight.Labels.Values.Where(l => l.GetParent() == _highlight).Select(l => l.GetGlobalRect()).ToArray();
        for (var i = 0; i < adjacent.Length; i++)
        { VerificationMode.Require(GetViewport().GetVisibleRect().Encloses(adjacent[i]), "label not clipped to viewport"); for (var j = 0; j < i; j++) VerificationMode.Require(!adjacent[i].Intersects(adjacent[j]), "adjacent label overlap"); }
        var membership = _highlight.Labels.Count; _courtyard.RemoveTarget("tiny-gear"); _Process(0);
        VerificationMode.Require(!_highlight.Labels.ContainsKey("tiny-gear"), "removed label retained"); _courtyard.ReloadTargets(); _Process(0); await LayoutFrames();
        VerificationMode.Require(_highlight.Labels.Count == membership, "reload duplicate/missing labels");
        await VerifyLabelGeometry();
        await VerifyIndependentLabels(evidenceDir, prefix);
        O5Reached("labels");
        // Same instances, actual displayed labels and native layout after scale A-B-A.
        var original = _highlight.Labels.ToDictionary(p => p.Key, p => (Instance: p.Value.GetInstanceId(), Rect: p.Value.GetGlobalRect(), Text: p.Value.Text));
        var transitionIndex = 0;
        foreach (var nextScale in new[] { 100, 200, 100, 200, 150, ui })
        {
            _activeMatrixStep = new { phase = "label-scale-return", transitionIndex = transitionIndex++, requestedScale = nextScale };
            _settings = _settings with { UiScalePercent = nextScale }; _hud.SetScale(nextScale); _Process(0); await LayoutFrames();
            await VerifyLabelGeometry();
            foreach (var (id, entry) in original)
                VerificationMode.Require(_highlight.Labels[id].GetInstanceId() == entry.Instance && _highlight.Labels[id].Text == entry.Text, "scale transition replaced/duplicated Label");
        }
        foreach (var (id, entry) in original) VerificationMode.Require(_highlight.Labels[id].GetGlobalRect() == entry.Rect,
            $"scale A-B-A geometry did not return: {id}; original={entry.Rect}; final={_highlight.Labels[id].GetGlobalRect()}");
        var stableGeneration = _highlight.LayoutGeneration;
        var work = (_highlight.ShapeWork, _highlight.SortWork, _highlight.ReparentWork);
        _Process(0); await LayoutFrames();
        VerificationMode.Require(_highlight.LayoutGeneration == stableGeneration, "unchanged frame invalidated label layout");
        VerificationMode.Require(work == (_highlight.ShapeWork, _highlight.SortWork, _highlight.ReparentWork), "unchanged settled generation did shape/sort/reparent work");
        await VerifyLayoutBoundaries(size, ui);
        O5Reached("scale-resize");
        await CaptureScenario("projected-labels", evidenceDir, prefix);
        var filename = $"{preset}-{size.X}x{size.Y}-ui{ui}-zoom{zoom:F1}.png";
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var labels = _highlight.Labels.Select(p => new { id = p.Key, text = p.Value.Text, rect = RectEvidence(p.Value.GetGlobalRect()), fontSize = p.Value.GetThemeFontSize("font_size"), overflow = p.Value.GetParent() is VBoxContainer }).ToArray();
        var labelGeometry = _highlight.GeometryEvidence();
        // Passive packing inputs at the existing final geometry readback. No
        // shaping/layout mutation, added frame, scroll write or injection.
        var packing = new { contentWidth = UsableOverflowClip().Size.X,
            labels = _highlight.Labels.Select(p => new { id = p.Key, clipText = p.Value.ClipText,
                wrapMode = p.Value.AutowrapMode.ToString(), lines = p.Value.GetLineCount(),
                minimumHeight = p.Value.GetMinimumSize().Y, maximumWidth = p.Value.CustomMaximumSize.X }).ToArray() };
        var pixelColorCount = 0;
        using (var image = GetViewport().GetTexture().GetImage())
        {
            VerificationMode.Require(image.GetWidth() == size.X && image.GetHeight() == size.Y, "actual raster/image dimensions mismatch");
            var colors = new HashSet<Color>(); for (var x = 0; x < size.X; x += 40) for (var y = 0; y < size.Y; y += 40) colors.Add(image.GetPixel(x, y));
            VerificationMode.Require(colors.Count > 8, "rendered image not substantive scene pixels");
            pixelColorCount = colors.Count;
            VerificationMode.Require(image.SavePng(System.IO.Path.Combine(evidenceDir, filename)) == Error.Ok, "capture failed");
        }
        Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.RightShoulder, Pressed = false }, VerificationMode.Simulation); _Process(0);
        VerificationMode.Require(_highlight.Labels.Count == 0, $"RB release did not hide labels: count={_highlight.Labels.Count}, active={_input.ActiveDevice}, focus={_focused}, raw={_adapter.SampleController(3, _focused)?.ControllerButtons}, step={_presetStep?.Highlight}");
        trace.Add("labels:projected/Cyrillic/long/packed/removal/reload/release");
        // Settings reset preserves world state and zoom; copy lifecycle uses actual production options.
        _hud.ShowSettings(); ClearWorldIntents("SIMULATION settings"); VerifyNeutral();
        VerifyChoose("Create user copy"); VerifyNeutral(); VerifyChoose("Use user copy"); VerifyNeutral();
        VerificationMode.Require(_settings.ActivePresetId == "user-copy" && _settings.UserCopy is not null, "copy not applied");
        VerifyChoose("Reset user copy"); VerifyNeutral(); VerifyChoose("Reset defaults"); VerifyNeutral();
        VerificationMode.Require(_settings.ActivePresetId == "contextual" && _actor.Position == actor && _cameraCenter == center && _camera.Zoom == new Vector2(zoom, zoom) && _cameraState.Mode == CameraMode.Manual, "settings reset altered positions/mode/zoom");
        _hud.SetScale(ui); _settings = _settings with { UiScalePercent = ui, ActivePresetId = preset }; UpdateHud(); await LayoutFrames();
        while (_hud.SelectedIndex != _hud.OptionCount - 1) _hud.Navigate(1);
        await LayoutFrames(); _hud.ScrollSelected(); await LayoutFrames();
        VerificationMode.Require(_hud.SelectedOption == "UI scale" && _hud.UsableModalClip.Encloses(_hud.ChoiceRows[_hud.SelectedIndex].GetGlobalRect()), "last full settings row unreachable by scroll");
        VerificationMode.Require(_hud.ChoiceRows.All(r => !r.ClipText && r.Size.Y >= r.GetMinimumSize().Y), "settings text truncated");
        VerificationMode.Require(_hud.HasReadableLayout(GetViewport().GetVisibleRect()), "production clipped HUD layout");
        await CaptureScenario("settings-last-row", evidenceDir, prefix);
        var bounds = _hud.LayoutBounds.ToDictionary(p => p.Key, p => RectEvidence(p.Value));
        var nodes = RecursiveNodeCount(this);
        for (var i = 0; i < 20; i++) { _hud.ShowSettings(); _hud.Navigate(22); _hud.HideModal(); }
        _hud.ShowSettings(); await LayoutFrames();
        VerificationMode.Require(RecursiveNodeCount(this) == nodes && _hud.ChoiceRows.Count == 23 && _log.Count <= 128 && _actions.Journal.Count <= 128, "recursive modal/journal lifetime unbounded");
        var before = _courtyard.Candidates(_world).Single(t => t.Id == "crate").Anchor;
        _hud.SetScale(100); _hud.SetScale(200); _hud.SetScale(100); _hud.SetScale(ui);
        VerificationMode.Require(_courtyard.Candidates(_world).Single(t => t.Id == "crate").Anchor == before, "UI scale changed world projection");
        trace.Add("settings:create/use/reset-copy/reset-defaults/scroll/positions-preserved;bounded-lifetime");
        var capture = System.IO.Path.Combine(evidenceDir, filename);
        O5Reached("pixels-release-settings");
        return new { preset, width = size.X, height = size.Y, uiScalePercent = ui, zoom, classification = "offscreen-logical-GUI/non-headless/production-Main.tscn",
            actualViewport = new[] { size.X, size.Y }, actualRaster = new[] { size.X, size.Y }, imageDimensions = new[] { size.X, size.Y }, status = "passed", trace, labels, bounds,
            labelGeometry, packing, pixelColorCount, proof = new { schema = "arcanum-gamepad-o5-proof-v1", completed = _o5Completed.ToArray() },
            scenarios = _scenarioCaptures.ToArray(), contractObservations = _contractObservations.ToArray(), settlement = _highlight.SettlementEvidence(), scrollObservations = _scrollObservations.ToArray(),
            capture = filename, captureSha256 = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(capture))).ToLowerInvariant() };
    }
    public async void RunPresetMatrix(string[] args)
    {
        if (args.Contains("--d4-modal-trace") || args.Contains("--d4-slot-trace")) { await RunD4ModalTrace(args); return; }
        var index = Array.IndexOf(args, "--preset-matrix");
        if (index < 0 || index + 2 >= args.Length) { GetTree().Quit(2); return; }
        var report = args[index + 1]; var dir = args[index + 2]; var tuples = new List<object>(); SubViewport? viewport = null;
        var lifetimes = new List<object>(); int baselineNodes = 0; double? baselineResources = null;
        LabRoot? activeScene = null; object? activeTuple = null;
        try
        {
            VerificationMode.Require(DisplayServer.GetName() != "headless" && _settingsStore.UsesIsolatedTestStorage, "matrix requires non-headless renderer and isolated storage");
            if (args.Contains("--d4-context-prefix")) VerificationMode.Require(!OS.HasFeature("standalone") && !args.Contains("--layout-focus"), "Context diagnostic requires full original SOURCE prefix");
            SetProcess(false); SetProcessInput(false); System.IO.Directory.CreateDirectory(dir);
            await LayoutFrames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var nativeSize = GetViewport().GetVisibleRect().Size;
            baselineNodes = RecursiveNodeCount(GetTree().Root);
            using (var image = GetViewport().GetTexture().GetImage()) VerificationMode.Require(image.SavePng(System.IO.Path.Combine(dir, "native-baseline.png")) == Error.Ok, "native baseline capture");
            foreach (var preset in new[] { "contextual", "quick-slots", "radial" })
            foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(2560, 1440), new Vector2I(3440, 1440) })
            foreach (var ui in new[] { 100, 150, 200 })
            foreach (var zoom in new[] { .5f, 2f })
            {
                if (args.Contains("--layout-focus") && (preset != "contextual" || size != new Vector2I(1280, 800))) continue;
                if (args.Contains("--o5-binding-probe") && (preset != "contextual" || size != new Vector2I(1280, 800) || ui != 100)) continue;
                viewport = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(viewport);
                var scene = ResourceLoader.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<LabRoot>(); viewport.AddChild(scene); scene.SetProcess(false); scene.SetProcessInput(false);
                activeScene = scene; activeTuple = new { preset, width = size.X, height = size.Y, ui, zoom };
                scene._d4ContextDiagnostic = args.Contains("--d4-context-prefix") && preset == "contextual" && size == new Vector2I(1280, 800) && ui == 200 && zoom == .5f;
                await LayoutFrames();
                tuples.Add(await scene.VerifyTuple(size, preset, ui, zoom, dir));
                // Exit with a pending generation: callbacks must not retain/free-use child controls.
                scene._hud.HideModal(); scene.ClearWorldIntents("SIMULATION exit"); scene.VerifyNeutral();
                scene.Handle(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.RightShoulder, Pressed = true }, VerificationMode.Simulation); scene._Process(0);
                var layer = scene._highlight; var exitedGeneration = layer.LayoutGeneration;
                viewport.Free(); viewport = null; activeScene = null; await RenderedFrame(); await RenderedFrame();
                VerificationMode.Require(layer.SettlementStatus == "cancelled" && layer.LayoutGeneration == exitedGeneration + 1, "freed generation callback survived exit");
                var nodes = RecursiveNodeCount(GetTree().Root); var resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
                baselineResources ??= resources;
                lifetimes.Add(new { tuple = activeTuple, nodes, baselineNodes, resources, baselineResources,
                    exitedGeneration, cancelledGeneration = layer.LayoutGeneration, settlementStatus = layer.SettlementStatus });
                VerificationMode.Require(nodes == baselineNodes && resources <= baselineResources + 4, "scene/image/subscription resource plateau failed");
                GD.Print($"Preset matrix observed {preset} {size} UI{ui} zoom{zoom}");
            }
            System.IO.File.WriteAllText(report, LabEvidenceCodec.Encode(new { schema = "arcanum-gamepad-lab-matrix-v1", task = "ARC-GPAD-005",
                candidateCommit = OS.GetEnvironment("ARCANUM_GAMEPAD_CANDIDATE"), origin = OS.HasFeature("standalone") ? "exported" : "source", status = "passed",
                isolatedTestStorage = true, backend = DisplayServer.GetName(), renderer = RenderingServer.GetCurrentRenderingMethod(), godotVersion = Engine.GetVersionInfo()["string"].AsString(),
                nativeBaseline = new { actualViewport = new[] { nativeSize.X, nativeSize.Y }, window = new[] { GetWindow().Size.X, GetWindow().Size.Y }, capture = "native-baseline.png",
                    sha256 = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, "native-baseline.png")))).ToLowerInvariant() },
                binding = new { nonce = OS.GetEnvironment("ARCANUM_GAMEPAD_MATRIX_NONCE"), inputMapSha256 = OS.GetEnvironment("ARCANUM_GAMEPAD_INPUT_MAP_SHA256"),
                    buildReceiptSha256 = OS.GetEnvironment("ARCANUM_GAMEPAD_BUILD_RECEIPT_SHA256"), runtimeInventorySha256 = OS.GetEnvironment("ARCANUM_GAMEPAD_RUNTIME_SHA256") },
                coverage = args.Contains("--layout-focus") ? "focused-layout-only" : args.Contains("--o5-binding-probe") ? "discriminatory-binding-only" : "full-contract-o5",
                classification = "logical GUI coverage only; not native high-resolution window or physical comfort certification", lifetimes, tuples }));
            GD.Print("GamepadLab preset matrix: PASS (SIMULATION / exact-size non-headless logical GUI)"); GetTree().Quit(0);
        }
        catch (Exception e)
        {
            var passiveGeometry = activeScene?._highlight.PassiveGeometryEvidence();
            var geometry = activeScene?._highlight.GeometryEvidence();
            System.IO.File.WriteAllText(report, JsonSerializer.Serialize(new { schema = "arcanum-gamepad-lab-matrix-v1", task = "ARC-GPAD-005", status = "failed",
                candidateCommit = OS.GetEnvironment("ARCANUM_GAMEPAD_CANDIDATE"), origin = OS.HasFeature("standalone") ? "exported" : "source",
                backend = DisplayServer.GetName(), renderer = RenderingServer.GetCurrentRenderingMethod(), activeTuple,
                activeStep = activeScene?._activeMatrixStep, passiveGeometry, geometry,
                diagnosticOnly = args.Contains("--d4-context-prefix"), acceptanceClaimed = false,
                originalTargetAssertionPassed = e is D4ContextPrefixComplete,
                modalTrace = activeScene?._hud.D4ScrollTrace,
                settlement = activeScene?._highlight.SettlementEvidence(), scrollObservations = activeScene?._scrollObservations.ToArray(),
                input = activeScene is null ? null : new { focused = activeScene._focused, activeDevice = activeScene._input.ActiveDevice,
                    modal = activeScene._hud.ModalVisible, raw = activeScene._adapter.SampleController(3, activeScene._focused)?.ControllerButtons.ToString(),
                    log = activeScene._log.TakeLast(8).ToArray(), preset = activeScene._presetState.ToString(), step = activeScene._presetStep?.ToString() },
                completedTuples = tuples, error = e.Message }, new JsonSerializerOptions { WriteIndented = true }));
            GD.PushError(e.ToString()); GetTree().Quit(1);
        }
        finally { viewport?.Free(); }
    }
}
