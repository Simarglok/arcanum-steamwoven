using System.Text;
using System.Text.Json;

namespace Arcanum.Input;

public static class GamepadBindingCodec
{
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumDepth = 16;
    public static string Serialize(BindingProfile profile)
    {
        if (GamepadBindingValidator.Validate(profile).Count != 0) throw new ArgumentException("invalid binding profile", nameof(profile));
        return SerializeUnchecked(profile);
    }
    internal static string SerializeUnchecked(BindingProfile profile) => JsonSerializer.Serialize(new {
        schema = profile.Schema, version = profile.Version, id = profile.Id, basePresetId = profile.BasePresetId,
        shoulderRoles = new { lb = profile.LbModifierButton, rb = profile.RbModifierButton },
        bindings = profile.Bindings.Select(b => new { context = b.Context, button = b.Button, modifiers = b.Modifiers, actionId = b.ActionId, slotId = b.SlotId }) });
    public static bool TryDeserialize(string? json, out BindingProfile? profile, out IReadOnlyList<BindingIssue> issues)
    {
        profile = null; issues = Array.AsReadOnly(new[] { new BindingIssue("profile", "malformed") });
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumBytes) { issues = Array.AsReadOnly(new[] { new BindingIssue("profile", "byte-limit") }); return false; }
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = doc.RootElement;
            RequireFields(root, "schema", "version", "id", "basePresetId", "shoulderRoles", "bindings");
            var roles = root.GetProperty("shoulderRoles"); RequireFields(roles, "lb", "rb");
            var rows = root.GetProperty("bindings");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 128) throw new JsonException("row-limit");
            var bindings = new List<GamepadBinding>();
            foreach (var row in rows.EnumerateArray())
            {
                RequireFields(row, "context", "button", "modifiers", "actionId", "slotId");
                var modifiers = row.GetProperty("modifiers");
                if (modifiers.ValueKind != JsonValueKind.Array || modifiers.GetArrayLength() > 2) throw new JsonException("modifier-limit");
                bindings.Add(new(Token(row,"context"), Token(row,"button"), modifiers.EnumerateArray().Select(Token), Token(row,"actionId"),
                    row.GetProperty("slotId").ValueKind == JsonValueKind.Null ? null : Token(row,"slotId")));
            }
            var candidate = new BindingProfile(Token(root,"id"), Token(root,"basePresetId"), bindings, Token(roles,"lb"), Token(roles,"rb"), Token(root,"schema"), root.GetProperty("version").GetInt32());
            issues = GamepadBindingValidator.Validate(candidate);
            if (issues.Count != 0) return false;
            profile = candidate; return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { return false; }
    }
    public static void RequireFields(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("expected object");
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != fields.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || fields.Any(f => !actual.Contains(f, StringComparer.Ordinal)))
            throw new JsonException("unknown, duplicate or missing field");
    }
    public static string Token(JsonElement parent, string property) => Token(parent.GetProperty(property));
    private static string Token(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not string token || token.Length is 0 or > 64) throw new JsonException("invalid token");
        return token;
    }
}
