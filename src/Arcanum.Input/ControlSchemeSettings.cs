using System.Text.Json;

namespace Arcanum.Input;

/// <summary>ARC-GPAD-1373, concept §0.1: the controller scheme a pad drives. The original mouse and keyboard are always active
/// and are not a value here (KM-01), so no setting can switch them off.</summary>
public enum PadScheme { Off, Direct, Pointer }

/// <summary>Touch scheme: <see cref="TouchPad"/> is scheme C (touch for the world and windows, pad for walking and hotkeys).</summary>
public enum TouchScheme { Off, TouchPad }

/// <summary>Which button glyph family prompts show; <see cref="Auto"/> follows the connected pad (§0.3).</summary>
public enum PromptStyle { Auto, Xbox, PlayStation, Nintendo, SteamDeck }

/// <summary>User-level choice of experimental schemes (concept §0.1). Defaults are Off: the test script chooses a scheme.
/// A run-time switch replaces the record; nothing in it needs a restart. Storage belongs to the host.</summary>
public sealed record ControlSchemeSettings(int Version = 1, PadScheme Pad = PadScheme.Off, TouchScheme Touch = TouchScheme.Off,
    PromptStyle Prompts = PromptStyle.Auto)
{
    public const int CurrentVersion = 1;
    public static ControlSchemeSettings Default => new();

    /// <summary>The original mouse and keyboard stay live whatever else is chosen.</summary>
    public bool OriginalActive => true;

    /// <summary>A copy with every field inside its enum range; a foreign version gives the defaults.</summary>
    public ControlSchemeSettings Validated() => Version != CurrentVersion ? Default : this with
    {
        Pad = Enum.IsDefined(Pad) ? Pad : PadScheme.Off,
        Touch = Enum.IsDefined(Touch) ? Touch : TouchScheme.Off,
        Prompts = Enum.IsDefined(Prompts) ? Prompts : PromptStyle.Auto
    };
}

public static class ControlSchemeSettingsCodec
{
    public const int MaximumBytes = 4096;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize(ControlSchemeSettings settings)
    {
        var s = settings.Validated();
        return JsonSerializer.Serialize(new { version = s.Version, pad = Token(s.Pad), touch = Token(s.Touch), prompts = Token(s.Prompts) }, Options);
    }

    /// <summary>Corrupt, oversized or foreign-version text gives the defaults; an unknown value of one field gives that field's default
    /// and keeps the others, so a newer build's file never switches the original controls or breaks a launch.</summary>
    public static ControlSchemeSettings DeserializeOrDefault(string? json)
    {
        if (json is null || System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) return ControlSchemeSettings.Default;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ControlSchemeSettings.Default;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number) || number != ControlSchemeSettings.CurrentVersion) return ControlSchemeSettings.Default;
            return new ControlSchemeSettings(number, Read(root, "pad", PadScheme.Off), Read(root, "touch", TouchScheme.Off), Read(root, "prompts", PromptStyle.Auto));
        }
        // A lone surrogate or a bad escape throws ArgumentException or InvalidOperationException, not JsonException.
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { return ControlSchemeSettings.Default; }
    }

    private static string Token<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static T Read<T>(JsonElement root, string name, T fallback) where T : struct, Enum
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return fallback;
        var text = element.GetString();
        foreach (var candidate in Enum.GetValues<T>())
            if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)) return candidate;
        return fallback;
    }
}
