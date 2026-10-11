using Arcanum.Input;
using GamepadLab.Core;

namespace GamepadLab.Tests;

internal static class BindingValidationTests
{
    public static void Run()
    {
        foreach (var id in new[] { "contextual", "quick-slots", "radial" })
        {
            var original = BuiltInGamepadPresets.Get(id);
            Assertions.Equal(GamepadBindingValidator.Validate(original).Count, 0, "built-in valid");
            var copy = original.CreateCopy();
            var rows = copy.Bindings.ToList(); rows.Add(rows[0]);
            Assertions.True(GamepadBindingValidator.Validate(copy.WithBindings(rows)).Any(i => i.Code == "ambiguous-equal-priority"), "duplicate predicates rejected");
            Assertions.True(GamepadBindingValidator.Validate(copy.WithShoulderRoles("lb", "lb")).Count > 0, "cyclic roles rejected");
            Assertions.True(GamepadBindingValidator.Validate(copy.WithBindings(rows.Take(1))).Count > 0, "required reachability");
            var json = GamepadBindingCodec.Serialize(copy);
            Assertions.True(GamepadBindingCodec.TryDeserialize(json, out var restored, out _), "copy JSON accepted");
            Assertions.Equal(restored!, copy, "immutable roundtrip");
            Assertions.True(!GamepadBindingCodec.TryDeserialize(json.Replace("\"version\":1", "\"version\":99"), out _, out _), "unknown version rejected");
            Assertions.True(!GamepadBindingCodec.TryDeserialize(json.Replace("\"version\":1", "\"version\":1,\"version\":1"), out _, out _), "duplicate keys rejected");
            Assertions.True(!GamepadBindingCodec.TryDeserialize(json.Replace("\"button\":\"a\"", "\"button\":\"guide\""), out _, out _), "Guide remains outside domain");
            Assertions.True(!GamepadBindingCodec.TryDeserialize(new string(' ', 65537), out _, out _), "byte cap");
            Assertions.True(!GamepadBindingCodec.TryDeserialize(new string('[', 17) + "0" + new string(']', 17), out _, out _), "depth cap");
            Assertions.True(copy.Bindings is not GamepadBinding[], "bindings don't expose backing array");
            var changed = rows.Select(b => b.Context == "modal" && b.Button == "a" ? new GamepadBinding("modal", "x", [], "modal-confirm") : b);
            Assertions.True(GamepadBindingValidator.Validate(copy.WithBindings(changed)).Count > 0, "fixed modal A/B safety");
        }
        ShoulderRoles();
        Console.WriteLine("Binding validation: caps/reserved/tokens/duplicate/roles/reachability/immutable PASS");
    }
    private static void ShoulderRoles()
    {
        foreach (var id in new[] { "contextual", "quick-slots", "radial" })
        {
            var template = BuiltInGamepadPresets.Get(id);
            var builtInBytes = GamepadBindingCodec.Serialize(template);
            var copy = template.CreateCopy();
            // Independent custom X/Y physical swap; role changes must not rewrite these rows.
            copy = copy.WithBindings(copy.Bindings.Select(b => b.Context == "world" && b.Modifiers.Count == 0
                ? new GamepadBinding(b.Context, b.Button switch { "x" => "y", "y" => "x", _ => b.Button }, b.Modifiers, b.ActionId, b.SlotId) : b));
            var originalBytes = GamepadBindingCodec.Serialize(copy);
            var swapped = copy.WithShoulderRoles("rb", "lb");
            Assertions.Equal(swapped.LbModifierButton, "rb", "literal new wheel/bank role");
            Assertions.Equal(swapped.RbModifierButton, "lb", "literal new highlight role");
            Assertions.Equal(GamepadBindingValidator.Validate(swapped).Count, 0, "all three swapped copies valid");
            Assertions.Equal(swapped.Schema, "arcanum-gamepad-bindings-v1", "schema preserved");
            Assertions.Equal(swapped.Version, 1, "version preserved");
            Assertions.Equal(swapped.Id, "user-copy", "copy identity preserved");
            Assertions.Equal(swapped.BasePresetId, id, "base preserved");
            Assertions.Equal(swapped.Bindings.Count, copy.Bindings.Count, "no dropped rows");
            for (var n = 0; n < copy.Bindings.Count; n++)
            {
                var before = copy.Bindings[n]; var after = swapped.Bindings[n];
                Assertions.Equal(after.Context, before.Context, "ordered context preserved");
                Assertions.Equal(after.ActionId, before.ActionId, "ordered action preserved");
                Assertions.Equal(after.SlotId ?? "null", before.SlotId ?? "null", "ordered slot preserved");
                Assertions.True(after.Modifiers.SequenceEqual(before.Modifiers), "semantic modifier tokens unchanged");
                Assertions.Equal(after.Button, before.ActionId == "wheel-open" ? "rb" : before.Button, "only role-owned wheel selects new physical RB");
            }
            if (id == "radial")
            {
                var wheels = swapped.Bindings.Where(b => b.ActionId == "wheel-open").ToArray();
                Assertions.Equal(wheels.Length, 2, "both wheel rows retained");
                Assertions.True(wheels[0].Button == "rb" && wheels[0].Context == "world" && wheels[0].Modifiers.Count == 0, "literal unmodified RB opener");
                Assertions.True(wheels[1].Button == "rb" && wheels[1].Context == "world" && wheels[1].Modifiers.SequenceEqual(new[] { "rb" }), "literal semantic rb/physical LB chord opener");
                Assertions.Equal(swapped.Resolve("world", "rb", ControllerButtons.RB)!.ActionId, "wheel-open", "RB selecting edge reachable");
                Assertions.Equal(swapped.Resolve("world", "rb", ControllerButtons.LB | ControllerButtons.RB)!.ActionId, "wheel-open", "LB+RB selecting edge reachable");
            }
            var swappedBytes = GamepadBindingCodec.Serialize(swapped);
            Assertions.True(GamepadBindingCodec.TryDeserialize(swappedBytes, out var loaded, out _), "swapped codec roundtrip accepted");
            Assertions.Equal(GamepadBindingCodec.Serialize(loaded!), swappedBytes, "roundtrip bytes exact");
            var back = swapped.WithShoulderRoles("lb", "rb");
            Assertions.Equal(GamepadBindingValidator.Validate(back).Count, 0, "inverse direction valid");
            Assertions.Equal(GamepadBindingCodec.Serialize(back), originalBytes, "swap-back byte identity");
            Assertions.Equal(GamepadBindingCodec.Serialize(back.WithShoulderRoles("rb", "lb")), swappedBytes, "repeat both directions deterministic");
            foreach (var (lb, rb) in new[] { ("lb", "lb"), ("rb", "rb"), ("a", "rb"), ("lb", "guide") })
                Assertions.True(GamepadBindingValidator.Validate(copy.WithShoulderRoles(lb, rb)).Any(i => i.Code == "cyclic-or-conflicting-role"), "invalid/cyclic requested roles rejected");
            var reserved = swapped.WithBindings(swapped.Bindings.Select(b => b.Context == "modal" && b.Button == "a" ? new GamepadBinding("modal", "x", [], "modal-confirm") : b));
            Assertions.True(GamepadBindingValidator.Validate(reserved).Any(i => i.Code == "reserved-domain"), "role change cannot waive modal safety");
            Assertions.True(GamepadBindingValidator.Validate(template.WithShoulderRoles("rb", "lb")).Any(i => i.Code == "immutable-template"), "API proposal cannot mutate template");
            Assertions.Equal(GamepadBindingCodec.Serialize(copy), originalBytes, "all rejected proposals leave source copy bytes");
            Assertions.Equal(GamepadBindingCodec.Serialize(template), builtInBytes, "built-in bytes unchanged");
        }
        var radial = BuiltInGamepadPresets.Get("radial").CreateCopy();
        var oldWheel = new BindingProfile("user-copy", "radial", radial.Bindings, "rb", "lb");
        Assertions.True(GamepadBindingValidator.Validate(oldWheel).Select(i => i.Code).SequenceEqual(new[] { "invalid-wheel-domain", "self-modifier-unreachable", "invalid-wheel-domain" }), "literal original defect signature");
        var oldJson = GamepadBindingCodec.Serialize(radial).Replace("\"lb\":\"lb\",\"rb\":\"rb\"", "\"lb\":\"rb\",\"rb\":\"lb\"");
        Assertions.True(!GamepadBindingCodec.TryDeserialize(oldJson, out var rejected, out var issues) && rejected is null
            && issues.Any(i => i.Code == "invalid-wheel-domain"), "malformed old-wheel import rejected not normalized");
        var refusedRepair = false;
        try { oldWheel.WithShoulderRoles("lb", "rb"); } catch (ArgumentException) { refusedRepair = true; }
        Assertions.True(refusedRepair, "helper refuses invalid source, not import repair");
        // A valid custom non-wheel selecting shoulder is physical, not role-owned.
        var custom = radial.WithBindings(radial.Bindings.Select(b => b.Context == "world" && b.Button == "x" && b.Modifiers.Count == 0
            ? new GamepadBinding("world", "rb", [], "inspect") : b));
        Assertions.Equal(GamepadBindingValidator.Validate(custom).Count, 0, "custom shoulder initially valid");
        var collision = custom.WithShoulderRoles("rb", "lb");
        Assertions.True(collision.Bindings.Any(b => b.Button == "rb" && b.ActionId == "inspect"), "non-wheel custom shoulder not silently moved");
        Assertions.True(GamepadBindingValidator.Validate(collision).Any(i => i.Code == "ambiguous-equal-priority"), "wheel/custom selecting collision fail closed");
        var selfSelecting = radial.WithBindings(radial.Bindings.Select(b => b.ActionId == "wheel-open" && b.Modifiers.Count == 0
            ? new GamepadBinding("world", "lb", ["lb"], "wheel-open") : b));
        Assertions.True(GamepadBindingValidator.Validate(selfSelecting).Any(i => i.Code == "self-modifier-unreachable"), "selecting shoulder cannot modify itself");
        Console.WriteLine("Shoulder role contract: all3/both directions/ordered literal wheel rows/custom/immutable/codec/fail-closed PASS");
    }
}
internal static class PersistenceMigrationTests
{
    private const string V1 = "{\"version\":1,\"cameraDeadzone\":0.25,\"actorDeadzone\":0.15,\"cameraSensitivity\":1.5,\"actorSensitivity\":0.5,\"invertCameraX\":false,\"invertCameraY\":true,\"movementMode\":1}";
    public static void Run()
    {
        Assertions.True(LabPreferences.TryDeserialize(V1, out var value, out var diagnostic, true), "literal v1 valid");
        Assertions.Equal(diagnostic, "settings-migrated-v1", "migration diagnostic");
        Assertions.Equal(value.CameraDeadzone, .25, "literal camera deadzone"); Assertions.Equal(value.ActorDeadzone, .15, "literal actor deadzone");
        Assertions.Equal(value.CameraSensitivity, 1.5, "literal camera sensitivity"); Assertions.Equal(value.ActorSensitivity, .5, "literal actor sensitivity");
        Assertions.True(!value.InvertCameraX && value.InvertCameraY && value.MovementMode == MovementMode.NearbyGoal, "explicit legacy choices retained");
        foreach (var id in new[] { "contextual", "quick-slots", "radial", "user-copy" })
        {
            var setting = value with { ActivePresetId = id, UserCopy = BuiltInGamepadPresets.Get("contextual").CreateCopy(), Slots = value.Slots.Assign("slot-09", null).Assign("slot-10", "synthetic.spell.ally"), UiScalePercent = 200 };
            var json = LabPreferences.Serialize(setting);
            Assertions.True(LabPreferences.TryDeserialize(json, out var restored, out _), "v2 accepted");
            Assertions.Equal(restored, setting, "all choices survive roundtrip");
            foreach (var invalid in new[] { json.Replace("\"version\":2", "\"version\":3"), json.Replace("\"uiScalePercent\":200", "\"uiScalePercent\":201"), json.Replace("synthetic.spell.ally", "unknown.action"), json.Replace("\"cameraDeadzone\":0.25", "\"cameraDeadzone\":1e999") })
                Assertions.True(!LabPreferences.TryDeserialize(invalid, out _, out _), "unsafe preferences rejected");
        }
        string[] expected = ["synthetic.item.self", "synthetic.skill.enemy", "synthetic.spell.ally", "synthetic.spell.ground", "synthetic.item.self", "synthetic.skill.enemy", "synthetic.spell.ally", "synthetic.spell.ground", "synthetic.item.self", "synthetic.spell.ground"];
        for (var n = 0; n < 10; n++) Assertions.Equal(LabSlots.Default.Assignments[$"slot-{n+1:00}"]!, expected[n], "literal default assignment");
        Assertions.True(!LabPreferences.TryDeserialize(V1, out _, out _), "v1 not confused with v2");
        Console.WriteLine("Persistence migration: literal v1 choices/v2 roundtrip/corruption/ten assignments PASS");
    }
}
internal static class ActionTargetTests
{
    public static void Run()
    {
        var target = new LabWorldTarget("enemy-ash", "enemy", new(10, 20), 3);
        var alternate = new LabWorldTarget("enemy-briar", "enemy", new(11, 20), 4);
        var ally = new LabWorldTarget("ally-iris", "ally", new(9, 20), 1);
        var executor = new LabActionExecutor(); executor.Boundary(5); executor.Select(target);
        executor.UseSlot("slot-02", LabSlots.Default, 1, [target, alternate], default);
        Assertions.Equal(executor.Journal.Last().TargetId!, "enemy-ash", "exact selected stable target"); Assertions.Equal(executor.ResourceUses, 1, "one mock resource effect");
        foreach (var invalid in new[] { Array.Empty<LabWorldTarget>(), new[] { target with { Revision = 2 }, alternate }, new[] { target with { Visible = false }, alternate }, new[] { target with { Alive = false }, alternate }, new[] { target with { Kind = "ally" }, alternate } })
        {
            executor.UseSlot("slot-02", LabSlots.Default, 2, invalid, default);
            Assertions.Equal(executor.Journal.Last().Kind, "TargetUnavailable", "stale authority denied"); Assertions.Equal(executor.ResourceUses, 1, "no replacement/resource use");
        }
        executor.Boundary(6); executor.Select(null); executor.UseSlot("slot-02", LabSlots.Default, 10, [alternate], default);
        Assertions.Equal(executor.Journal.Last().Kind, "PreviewOpened", "missing target preview"); executor.Confirm(10, [alternate]); Assertions.Equal(executor.ResourceUses, 1, "selecting edge cannot confirm");
        executor.Cycle([alternate], 1); executor.Confirm(11, [alternate]); Assertions.Equal(executor.Journal.Last().TargetId!, "enemy-briar", "explicit reselection required"); Assertions.Equal(executor.ResourceUses, 2, "explicit target confirm once");
        executor.Select(ally); executor.UseSlot("slot-03", LabSlots.Default, 12, [ally], default); Assertions.Equal(executor.Journal.Last().TargetId!, "ally-iris", "ally kind");
        executor.UseSlot("slot-10", LabSlots.Default, 13, [], new(25, 50)); Assertions.Equal(executor.Journal.Last().Kind, "PreviewOpened", "slot10 ground preview");
        executor.Confirm(13, []); Assertions.Equal(executor.ResourceUses, 3, "ground selecting edge not confirm"); executor.Cancel(14); Assertions.Equal(executor.ResourceUses, 3, "ground cancel no activation");
        executor.UseSlot("slot-10", LabSlots.Default, 15, [], new(25, 50)); executor.Confirm(16, []); Assertions.Equal(executor.Journal.Last().GroundPoint!.Value, new Vec2(25, 50), "literal ground point");
        executor.UseSlot("slot-09", LabSlots.Default, 17, [], default); Assertions.Equal(executor.Journal.Last().TargetId!, "actor", "slot09 self explicit actor");
        var empty = LabSlots.Default.Assign("slot-09", null); executor.UseSlot("slot-09", empty, 18, [], default); Assertions.Equal(executor.Journal.Last().Kind, "EmptySlot", "empty no neighbor substitution");
        executor.SelectAction("unsupported.fate", null, 19, [], default); Assertions.Equal(executor.Journal.Last().Kind, "Unsupported", "rare feature honest unsupported");
        Assertions.Equal(executor.Eligible([target, ally, target with { Id = "hidden", Visible = false }, target with { Id = "scenery", Interactive = false }]).Count, 2, "eligibility excludes hidden/scenery");
        Console.WriteLine("Action snapshots: self/enemy/ally/ground/stale ID/revision/preview/cancel/resources/slots09-10 PASS");
    }
}
internal static class GridAndLifetimeTests
{
    public static void Run()
    {
        var e = new LabActionExecutor { TurnBased = true }; e.PreviewGrid(new(1, 0), 0);
        Assertions.Equal(e.GridPreview!.Value, new Vec2(5, 4), "literal right cell preview"); Assertions.Equal(e.APSpent, 0, "preview not AP");
        e.Cancel(1); Assertions.Equal(e.GridActor, new Vec2(4, 4), "cancel actor unchanged");
        e.PreviewGrid(new(1, 1), 0); Assertions.Equal(e.GridPreview!.Value, new Vec2(5, 4), "diagonal X tie"); e.Confirm(2, []);
        Assertions.Equal(e.GridActor, new Vec2(5, 4), "literal mock move destination"); Assertions.Equal(e.APSpent, 1, "literal AP1"); e.Confirm(3, []); Assertions.Equal(e.APSpent, 1, "no repeat AP");
        for (var i = 0; i < 500; i++) { e.PreviewGrid(new(1, 0), .5); e.Record(new("MockIntent")); }
        Assertions.Equal(e.Journal.Count, 128, "bounded ring journal"); Assertions.True(e.GridPreview!.Value.X <= 9, "grid clamp");
        Assertions.Equal(LabZoomPolicy.Apply(1, 1, 1), 2d, "literal full RT doubles"); Assertions.Equal(LabZoomPolicy.Apply(1, -1, 1), .5d, "literal full LT halves");
        Assertions.Equal(LabZoomPolicy.Apply(1, 0, 1), 1d, "opposites no displacement");
        foreach (var delta in new[] { double.NaN, double.NegativeInfinity, -1d })
        {
            var rejected = false; try { LabZoomPolicy.Apply(1, 1, delta); } catch (ArgumentException) { rejected = true; }
            Assertions.True(rejected, "invalid delta rejected before integration");
        }
        Console.WriteLine("Mock grid/AP/diagonal/bounds/zoom/journal lifetime PASS");
    }
}
