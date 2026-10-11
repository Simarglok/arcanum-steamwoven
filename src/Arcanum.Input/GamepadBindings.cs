using System.Collections.ObjectModel;

namespace Arcanum.Input;

[Flags]
public enum ControllerButtons
{
    None = 0, A = 1, B = 2, X = 4, Y = 8, LB = 16, RB = 32,
    DpadUp = 64, DpadDown = 128, DpadLeft = 256, DpadRight = 512,
    L3 = 1024, R3 = 2048, View = 4096, Menu = 8192
}

/// <summary>Physical token spelling is a storage contract, not a Godot button ordinal.</summary>
public static class GamepadTokens
{
    public const string Schema = "arcanum-gamepad-bindings-v1";
    public static readonly IReadOnlyDictionary<string, ControllerButtons> Buttons = new ReadOnlyDictionary<string, ControllerButtons>(
        new Dictionary<string, ControllerButtons>(StringComparer.Ordinal) {
            ["a"] = ControllerButtons.A, ["b"] = ControllerButtons.B, ["x"] = ControllerButtons.X, ["y"] = ControllerButtons.Y,
            ["lb"] = ControllerButtons.LB, ["rb"] = ControllerButtons.RB, ["dpad-up"] = ControllerButtons.DpadUp,
            ["dpad-down"] = ControllerButtons.DpadDown, ["dpad-left"] = ControllerButtons.DpadLeft, ["dpad-right"] = ControllerButtons.DpadRight,
            ["l3"] = ControllerButtons.L3, ["r3"] = ControllerButtons.R3, ["view"] = ControllerButtons.View, ["menu"] = ControllerButtons.Menu });
    public static bool IsBasePreset(string id) => id is "contextual" or "quick-slots" or "radial";
    public static bool IsSlot(string? id) => id is "slot-01" or "slot-02" or "slot-03" or "slot-04" or "slot-05" or "slot-06" or "slot-07" or "slot-08" or "slot-09" or "slot-10";
    public static bool IsAction(string id) => id is "interact" or "back" or "inspect" or "context" or "quick-slot" or "target-previous" or "target-next"
        or "target-lock" or "camera-recenter" or "battle-surface" or "follower-surface" or "hub" or "settings" or "modal-confirm" or "modal-cancel" or "wheel-open";
}

/// <summary>Defensive copies at every boundary; neither built-ins nor user profiles expose arrays.</summary>
public sealed record GamepadBinding
{
    public string Context { get; }
    public string Button { get; }
    public IReadOnlyList<string> Modifiers { get; }
    public string ActionId { get; }
    public string? SlotId { get; }
    public GamepadBinding(string context, string button, IEnumerable<string> modifiers, string actionId, string? slotId = null)
    { Context = context; Button = button; Modifiers = Array.AsReadOnly(modifiers.ToArray()); ActionId = actionId; SlotId = slotId; }
    internal bool Matches(string button, int modifiers) => Button == button && ModifierMask == modifiers;
    internal int ModifierMask => (Modifiers.Contains("lb") ? 1 : 0) | (Modifiers.Contains("rb") ? 2 : 0);
}

public sealed class BindingProfile : IEquatable<BindingProfile>
{
    public string Schema { get; }
    public int Version { get; }
    public string Id { get; }
    public string BasePresetId { get; }
    public string LbModifierButton { get; }
    public string RbModifierButton { get; }
    public IReadOnlyList<GamepadBinding> Bindings { get; }
    public BindingProfile(string id, string basePresetId, IEnumerable<GamepadBinding> bindings,
        string lbModifierButton = "lb", string rbModifierButton = "rb", string schema = GamepadTokens.Schema, int version = 1)
    {
        Schema = schema; Version = version; Id = id; BasePresetId = basePresetId;
        LbModifierButton = lbModifierButton; RbModifierButton = rbModifierButton;
        Bindings = Array.AsReadOnly(bindings.Take(129).Select(b => new GamepadBinding(b.Context, b.Button, b.Modifiers, b.ActionId, b.SlotId)).ToArray());
    }
    public BindingProfile CreateCopy() => new("user-copy", BasePresetId, Bindings, LbModifierButton, RbModifierButton);
    public BindingProfile WithBindings(IEnumerable<GamepadBinding> rows) => new(Id, BasePresetId, rows, LbModifierButton, RbModifierButton);
    /// <summary>
    /// Propose a role change from a valid profile, moving only role-owned Radial wheel selectors.
    /// Modifier tokens retain their semantic roles; all other selecting buttons remain physical.
    /// The result still requires full validation before apply/save (including immutable-template,
    /// invalid requested roles and custom binding collisions). This is not an import repair API.
    /// </summary>
    public BindingProfile WithShoulderRoles(string lb, string rb)
    {
        if (GamepadBindingValidator.Validate(this).Count != 0) throw new ArgumentException("valid source profile required");
        var rows = Bindings.Select(b => BasePresetId == "radial" && b.Context == "world"
            && b.ActionId == "wheel-open" && b.Button == LbModifierButton
                ? new GamepadBinding(b.Context, lb, b.Modifiers, b.ActionId, b.SlotId) : b);
        return new(Id, BasePresetId, rows, lb, rb, Schema, Version);
    }
    public int Modifiers(ControllerButtons down, string? selectingButton = null)
    {
        if (selectingButton is not null && GamepadTokens.Buttons.TryGetValue(selectingButton, out var selecting)) down &= ~selecting;
        return (down.HasFlag(GamepadTokens.Buttons[LbModifierButton]) ? 1 : 0) | (down.HasFlag(GamepadTokens.Buttons[RbModifierButton]) ? 2 : 0);
    }
    public GamepadBinding? Resolve(string context, string button, ControllerButtons down) =>
        Bindings.SingleOrDefault(b => b.Context == context && b.Matches(button, Modifiers(down, button)));
    public bool Equals(BindingProfile? other) => other is not null && GamepadBindingCodec.SerializeUnchecked(this) == GamepadBindingCodec.SerializeUnchecked(other);
    public override bool Equals(object? other) => other is BindingProfile profile && Equals(profile);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(GamepadBindingCodec.SerializeUnchecked(this));
}

public static class BuiltInGamepadPresets
{
    private static readonly IReadOnlyDictionary<string, BindingProfile> Profiles = new ReadOnlyDictionary<string, BindingProfile>(
        new[] { "contextual", "quick-slots", "radial" }.ToDictionary(id => id, Build, StringComparer.Ordinal));
    public static BindingProfile Get(string id) => Profiles.TryGetValue(id, out var profile) ? profile : throw new ArgumentException("unknown preset", nameof(id));
    private static BindingProfile Build(string id)
    {
        var rows = new List<GamepadBinding>();
        string[] faces = ["a", "x", "y", "b"]; string[] contextual = ["interact", "inspect", "context", "back"];
        (string button, string action)[] common = [("dpad-left", "target-previous"), ("dpad-right", "target-next"), ("dpad-up", "battle-surface"),
            ("dpad-down", "follower-surface"), ("l3", "camera-recenter"), ("r3", "target-lock"), ("view", "hub"), ("menu", "settings")];
        for (var mask = 0; mask < 4; mask++)
        {
            var modifiers = new List<string>(); if ((mask & 1) != 0) modifiers.Add("lb"); if ((mask & 2) != 0) modifiers.Add("rb");
            for (var i = 0; i < 4; i++)
            {
                var bank = id == "contextual" ? ((mask & 1) == 0 ? 0 : mask == 3 ? 5 : 1)
                    : id == "quick-slots" ? ((mask & 1) != 0 ? 5 : mask == 2 ? 0 : 1) : 0;
                rows.Add(new("world", faces[i], modifiers, bank == 0 ? contextual[i] : "quick-slot", bank == 0 ? null : $"slot-{bank+i:00}"));
            }
            foreach (var (button, action) in common) rows.Add(new("world", button, modifiers, action));
        }
        // LB selecting edge excludes its own modifier level; RB remains a compatible held highlight alias.
        if (id == "radial") { rows.Add(new("world", "lb", [], "wheel-open")); rows.Add(new("world", "lb", ["rb"], "wheel-open")); }
        rows.Add(new("modal", "a", [], "modal-confirm")); rows.Add(new("modal", "b", [], "modal-cancel"));
        return new(id, id, rows);
    }
}

public sealed record BindingIssue(string Field, string Code);
public static class GamepadBindingValidator
{
    private static readonly string[] Required = ["interact", "back", "inspect", "context", "target-previous", "target-next", "target-lock", "camera-recenter", "battle-surface", "follower-surface", "hub", "settings"];
    public static IReadOnlyList<BindingIssue> Validate(BindingProfile profile)
    {
        var issues = new List<BindingIssue>();
        void Issue(string field, string code) { if (issues.Count < 128) issues.Add(new(field, code)); }
        if (profile.Schema != GamepadTokens.Schema || profile.Version != 1) Issue("schema", "unknown-schema-version");
        if (!GamepadTokens.IsBasePreset(profile.BasePresetId) || (profile.Id != "user-copy" && profile.Id != profile.BasePresetId)) Issue("id", "unknown-preset");
        if (profile.LbModifierButton is not ("lb" or "rb") || profile.RbModifierButton is not ("lb" or "rb") || profile.LbModifierButton == profile.RbModifierButton)
            Issue("shoulderRoles", "cyclic-or-conflicting-role");
        if (profile.Bindings.Count is 0 or > 128) Issue("bindings", "row-limit");
        for (var i = 0; i < profile.Bindings.Count; i++)
        {
            var b = profile.Bindings[i]; var field = $"bindings[{i}]";
            if (b.Context is not ("world" or "modal") || !GamepadTokens.Buttons.ContainsKey(b.Button) || !GamepadTokens.IsAction(b.ActionId)) Issue(field, "unknown-token");
            if (b.Modifiers.Count > 2 || b.Modifiers.Distinct().Count() != b.Modifiers.Count || b.Modifiers.Any(m => m is not ("lb" or "rb"))) Issue(field, "invalid-modifier");
            if (b.Modifiers.Any(m => (m == "lb" ? profile.LbModifierButton : profile.RbModifierButton) == b.Button)) Issue(field, "self-modifier-unreachable");
            if (b.ActionId == "quick-slot" ? !GamepadTokens.IsSlot(b.SlotId) : b.SlotId is not null) Issue(field, "invalid-slot-action");
            if (b.Context == "modal" && (b.Modifiers.Count != 0 || !((b.Button == "a" && b.ActionId == "modal-confirm") || (b.Button == "b" && b.ActionId == "modal-cancel")))) Issue(field, "reserved-domain");
            if (b.Context == "world" && (b.Button == "menu" ? b.ActionId != "settings" : b.ActionId is "settings" or "modal-confirm" or "modal-cancel")) Issue(field, "reserved-domain");
            if (b.ActionId == "wheel-open" && (profile.BasePresetId != "radial" || b.Context != "world" || b.Button != profile.LbModifierButton)) Issue(field, "invalid-wheel-domain");
        }
        // Enumerate the complete finite modifier/button domain. Exact predicates never silently shadow another row.
        foreach (var context in new[] { "world", "modal" })
        foreach (var button in GamepadTokens.Buttons.Keys)
        for (var mask = 0; mask < 4; mask++)
        {
            var count = profile.Bindings.Count(b => b.Context == context && b.Matches(button, mask));
            if (count > 1) Issue("bindings", "ambiguous-equal-priority");
            if (context == "world" && button == "menu" && count != 1) Issue("bindings", "settings-unreachable");
        }
        foreach (var action in Required)
            if (!profile.Bindings.Any(b => b.Context == "world" && b.ActionId == action)) Issue("bindings", "required-action-unreachable:"+action);
        foreach (var (button, action) in new[] { ("a", "modal-confirm"), ("b", "modal-cancel") })
            if (profile.Bindings.Count(b => b.Context == "modal" && b.Button == button && b.Modifiers.Count == 0 && b.ActionId == action) != 1) Issue("bindings", "modal-safety-unreachable");
        if (profile.BasePresetId == "radial" && profile.Bindings.Count(b => b.ActionId == "wheel-open") != 2) Issue("bindings", "wheel-unreachable");
        if (profile.Id != "user-copy" && GamepadTokens.IsBasePreset(profile.Id))
        {
            var original = BuiltInGamepadPresets.Get(profile.Id);
            if (GamepadBindingCodec.SerializeUnchecked(original) != GamepadBindingCodec.SerializeUnchecked(profile)) Issue("bindings", "immutable-template");
        }
        return issues.AsReadOnly();
    }
}
