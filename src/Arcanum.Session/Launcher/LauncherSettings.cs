using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcanum.Data.Content;

namespace Arcanum.Session.Launcher;

/// <summary>Where the stored game folder came from: a GOG installation (its <c>goggame-1207658829.info</c> marker) or another one.</summary>
public enum GameSource { Other, Gog }

public enum LauncherWindowMode { Windowed, Fullscreen }

/// <summary>The display the game starts with (stored only; the launcher screen applies it, ARC-RES-074 task 2):
/// window or fullscreen, the monitor (<see cref="CurrentMonitor"/> is the one the system picks) and the UI scale in percent.</summary>
public sealed record LauncherDisplay(LauncherWindowMode WindowMode, int Monitor, int UiScalePercent)
{
    public const int CurrentMonitor = -1;
    public const int MaxMonitor = 15;
    public const int MinUiScalePercent = 50;
    public const int MaxUiScalePercent = 300;
    public static readonly LauncherDisplay Default = new(LauncherWindowMode.Windowed, CurrentMonitor, 100);

    public bool Valid => Enum.IsDefined(WindowMode) && Monitor is >= CurrentMonitor and <= MaxMonitor
                         && UiScalePercent is >= MinUiScalePercent and <= MaxUiScalePercent;
}

/// <summary>ARC-DEV-046: the launcher's settings (research ARC-RES-074, "Configuration file"), stored in <c>launcher_v1.json</c>:
/// the game folder with its source and the identity it had when it was last checked (so a changed installation is noticed at
/// start), whether the launcher shows at every start, the rule mode (alpha: the built-in Steamwoven and Original only; custom
/// profiles are a later schema), the display at start and the debug mode (ARC-DEV-050). Player preferences outside saves
/// (FR-LIFECYCLE-004); the session never reads this file and no snapshot holds any of it.</summary>
public sealed record LauncherSettings(string? GameRoot, GameSource GameSource, GameFolderIdentity? GameIdentity, bool ShowAtStart,
    RuleMode Rules, LauncherDisplay Display)
{
    public static readonly LauncherSettings Defaults = new(null, GameSource.Other, null, false, RuleMode.Steamwoven, LauncherDisplay.Default);

    /// <summary>ARC-DEV-050: the debug mode (off by default): every world-map area listed and open to an instant teleport, a
    /// development aid, never gameplay. Stored so that it stays on until it is turned off on the launcher screen.</summary>
    public bool DebugMode { get; init; }

    /// <summary>These settings with <paramref name="check"/>'s ready folder, source and identity; a folder that is not ready throws.</summary>
    public LauncherSettings WithGameFolder(GameFolderCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!check.Ready || check.Identity is null) throw new ArgumentException("A ready game folder is required.", nameof(check));
        return this with { GameRoot = check.Path, GameSource = check.Identity.GogMarker ? GameSource.Gog : GameSource.Other, GameIdentity = check.Identity };
    }
}

/// <summary>How a settings file read: absent (<see cref="Missing"/>), read (fields with an invalid value take their default and are
/// listed), not a settings object (<see cref="Invalid"/>: not JSON or not UTF-8, not an object, no <c>schema</c>, larger than
/// <see cref="LauncherSettingsCodec.MaxBytes"/>; such a file is replaced on the next write), a settings file of another schema
/// (<see cref="OtherSchema"/>: any <c>schema</c> but the integer 1, a newer build's, which this build never overwrites), or a file that
/// exists but cannot be read (<see cref="Unreadable"/>, never overwritten either). All but <see cref="Read"/> give the defaults.</summary>
public enum LauncherSettingsReadStatus { Missing, Read, Invalid, OtherSchema, Unreadable }

/// <param name="Defaulted">The keys present with a value this build cannot use, which took their default.</param>
/// <param name="UnknownKeys">The keys this build does not know (top level, and inside the <c>rules</c> and <c>display</c> objects as
/// <c>rules.KEY</c>, <c>display.KEY</c>); a rewrite keeps them, so an older build does not erase a newer build's settings. Keys inside
/// <c>gameIdentity</c> are not kept (the identity is rewritten whole from a check), nor is a <c>rules</c> or <c>display</c> value that is
/// not an object.</param>
public sealed record LauncherSettingsRead(LauncherSettingsReadStatus Status, LauncherSettings Settings, IReadOnlyList<string> Defaulted,
    IReadOnlyList<string> UnknownKeys)
{
    internal JsonObject? Unknown { get; init; }
}

/// <summary>The text of <c>launcher_v1.json</c>: a JSON object with <c>schema</c> 1, <c>gameRoot</c> (a path or null),
/// <c>gameSource</c> (<c>gog</c>, <c>other</c>), <c>gameIdentity</c> (null or <c>version</c>, <c>exeBytes</c>, <c>exeSha256</c>,
/// <c>gog</c>, <c>archives</c>: <c>[{"name","bytes"}]</c> in mount order), <c>showAtStart</c>, <c>rules</c>
/// (<c>{"active":"steamwoven"|"original"}</c>), <c>display</c> (<c>window</c>: <c>windowed</c>/<c>fullscreen</c>, <c>monitor</c>,
/// <c>uiScale</c>) and <c>debugMode</c> (ARC-DEV-050; absent is off, so an older build's file reads as before). <c>gameRoot</c> must be a full path of this system. Reading never throws: see <see cref="LauncherSettingsReadStatus"/>.</summary>
public static class LauncherSettingsCodec
{
    public const int Schema = 1;
    /// <summary>A longer file is not read.</summary>
    public const int MaxBytes = 64 * 1024;
    public const int MaxArchives = GameFolderLocator.MaxArchives;
    public const int MaxArchiveName = GameFolderLocator.MaxPathLength;
    public const int MaxVersionLength = GameFolderLocator.MaxVersionLength;

    private static readonly string[] KnownKeys = ["schema", "gameRoot", "gameSource", "gameIdentity", "showAtStart", "rules", "display", "debugMode"];
    private static readonly string[] DisplayKeys = ["window", "monitor", "uiScale"];

    public static LauncherSettingsRead Deserialize(string? json)
    {
        if (json is null) return Defaults(LauncherSettingsReadStatus.Missing);
        if (json.Length > MaxBytes || Encoding.UTF8.GetByteCount(json) > MaxBytes) return Defaults(LauncherSettingsReadStatus.Invalid);
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return Defaults(LauncherSettingsReadStatus.Invalid);
            // Enumerating the object materialises it, so a repeated key fails here (ArgumentException) as unreadable.
            var names = root.Select(property => property.Key).ToArray();
            // Any schema but the integer 1 is another build's file (never overwritten); only an object without one is invalid.
            if (!root.ContainsKey("schema")) return Defaults(LauncherSettingsReadStatus.Invalid);
            if (root["schema"] is not JsonValue schemaValue || schemaValue.GetValueKind() != JsonValueKind.Number || !schemaValue.TryGetValue<int>(out var schema)
                || schema != Schema) return Defaults(LauncherSettingsReadStatus.OtherSchema);
            var defaulted = new List<string>();
            var settings = LauncherSettings.Defaults;
            if (root.TryGetPropertyValue("gameRoot", out var gameRoot) && gameRoot is not null)
            {
                if (Text(gameRoot) is { } path && SafePath(path)) settings = settings with { GameRoot = path };
                else defaulted.Add("gameRoot");
            }
            if (root.TryGetPropertyValue("gameSource", out var source))
            {
                if (Text(source) is ("gog" or "other") and var name) settings = settings with { GameSource = name == "gog" ? GameSource.Gog : GameSource.Other };
                else defaulted.Add("gameSource");
            }
            if (root.TryGetPropertyValue("gameIdentity", out var identity) && identity is not null)
            {
                if (Identity(identity) is { } read) settings = settings with { GameIdentity = read };
                else defaulted.Add("gameIdentity");
            }
            if (root.TryGetPropertyValue("showAtStart", out var show))
            {
                if (Boolean(show) is { } value) settings = settings with { ShowAtStart = value };
                else defaulted.Add("showAtStart");
            }
            if (root.TryGetPropertyValue("debugMode", out var debug))
            {
                if (Boolean(debug) is { } value) settings = settings with { DebugMode = value };
                else defaulted.Add("debugMode");
            }
            var unknownRules = new JsonObject();
            if (root.TryGetPropertyValue("rules", out var rules))
            {
                if (rules is JsonObject block && block.TryGetPropertyValue("active", out var active) && RuleModeNames.TryParse(Text(active), out var mode))
                    settings = settings with { Rules = mode };
                else defaulted.Add("rules");
                if (rules is JsonObject kept)
                    foreach (var property in kept.Where(property => property.Key != "active")) unknownRules[property.Key] = property.Value?.DeepClone();
            }
            var unknownDisplay = new JsonObject();
            if (root.TryGetPropertyValue("display", out var display))
            {
                if (Display(display) is { } read) settings = settings with { Display = read };
                else defaulted.Add("display");
                if (display is JsonObject kept)
                    foreach (var property in kept.Where(property => !DisplayKeys.Contains(property.Key, StringComparer.Ordinal))) unknownDisplay[property.Key] = property.Value?.DeepClone();
            }
            var unknown = new JsonObject();
            foreach (var name in names.Where(name => !KnownKeys.Contains(name, StringComparer.Ordinal))) unknown[name] = root[name]?.DeepClone();
            if (unknownRules.Count > 0) unknown["rules"] = unknownRules;
            if (unknownDisplay.Count > 0) unknown["display"] = unknownDisplay;
            var unknownKeys = names.Where(name => !KnownKeys.Contains(name, StringComparer.Ordinal))
                .Concat(unknownRules.Select(property => "rules." + property.Key)).Concat(unknownDisplay.Select(property => "display." + property.Key)).ToArray();
            return new(LauncherSettingsReadStatus.Read, settings, defaulted.AsReadOnly(), unknownKeys) { Unknown = unknown };
        }
        // Text that is not valid UTF-16 is unreadable too (as in RuleModeSettingCodec), and so is a repeated key.
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return Defaults(LauncherSettingsReadStatus.Invalid);
        }
    }

    /// <summary>The file text of <paramref name="settings"/>, keeping <paramref name="previous"/>'s unknown keys (a <see cref="LauncherSettingsReadStatus.Read"/> read only).
    /// Settings this codec cannot write back (an unsafe path, an invalid display or identity) throw.</summary>
    public static string Serialize(LauncherSettings settings, LauncherSettingsRead? previous = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GameRoot is { } path && !SafePath(path)) throw new ArgumentException("The game folder path cannot be stored.", nameof(settings));
        if (!Enum.IsDefined(settings.GameSource) || !settings.Display.Valid || settings.GameIdentity is { } identity && !Valid(identity))
            throw new ArgumentException("The settings cannot be stored.", nameof(settings));
        var kept = previous?.Status == LauncherSettingsReadStatus.Read ? previous.Unknown : null;
        var rules = new JsonObject { ["active"] = RuleModeNames.Name(settings.Rules) };
        if (kept?["rules"] is JsonObject unknownRules)
            foreach (var property in unknownRules) rules[property.Key] = property.Value?.DeepClone();
        var root = new JsonObject
        {
            ["schema"] = Schema,
            ["gameRoot"] = settings.GameRoot,
            ["gameSource"] = settings.GameSource == GameSource.Gog ? "gog" : "other",
            ["gameIdentity"] = settings.GameIdentity is null ? null : new JsonObject
            {
                ["version"] = settings.GameIdentity.Version,
                ["exeBytes"] = settings.GameIdentity.ExeBytes,
                ["exeSha256"] = settings.GameIdentity.ExeSha256,
                ["gog"] = settings.GameIdentity.GogMarker,
                ["archives"] = new JsonArray(settings.GameIdentity.Archives.Select(archive => (JsonNode)new JsonObject { ["name"] = archive.Name, ["bytes"] = archive.Bytes }).ToArray()),
            },
            ["showAtStart"] = settings.ShowAtStart,
            ["rules"] = rules,
            ["display"] = new JsonObject
            {
                ["window"] = settings.Display.WindowMode == LauncherWindowMode.Fullscreen ? "fullscreen" : "windowed",
                ["monitor"] = settings.Display.Monitor,
                ["uiScale"] = settings.Display.UiScalePercent,
            },
            ["debugMode"] = settings.DebugMode,
        };
        if (kept?["display"] is JsonObject unknownDisplay)
            foreach (var property in unknownDisplay) root["display"]![property.Key] = property.Value?.DeepClone();
        if (kept is not null)
            foreach (var property in kept.Where(property => !KnownKeys.Contains(property.Key, StringComparer.Ordinal)))
                root[property.Key] = property.Value?.DeepClone();
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static LauncherSettingsRead Defaults(LauncherSettingsReadStatus status) => new(status, LauncherSettings.Defaults, [], []);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static bool? Boolean(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;
    private static long? Integer(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<long>(out var number) ? number : null;

    /// <summary>A full path of this system (a relative path or another system's would resolve against the working folder).</summary>
    private static bool SafePath(string path) => path.Length is > 0 and <= GameFolderLocator.MaxPathLength && !path.Any(char.IsControl) && path.Trim().Length > 0
                                                 && System.IO.Path.IsPathFullyQualified(path);

    private static LauncherDisplay? Display(JsonNode? node)
    {
        if (node is not JsonObject display) return null;
        var mode = Text(display["window"]) switch { "windowed" => LauncherWindowMode.Windowed, "fullscreen" => LauncherWindowMode.Fullscreen, _ => (LauncherWindowMode?)null };
        if (mode is null || Integer(display["monitor"]) is not { } monitor || Integer(display["uiScale"]) is not { } scale
            || monitor is < LauncherDisplay.CurrentMonitor or > LauncherDisplay.MaxMonitor || scale is < LauncherDisplay.MinUiScalePercent or > LauncherDisplay.MaxUiScalePercent) return null;
        return new(mode.Value, (int)monitor, (int)scale);
    }

    private static GameFolderIdentity? Identity(JsonNode node)
    {
        if (node is not JsonObject identity || identity["archives"] is not JsonArray archives || archives.Count > MaxArchives
            || Boolean(identity["gog"]) is not { } gog) return null;
        var version = identity["version"] is null ? null : Text(identity["version"]) ?? "\0";
        long? exeBytes = identity["exeBytes"] is null ? null : Integer(identity["exeBytes"]) ?? -1;
        var exeSha256 = identity["exeSha256"] is null ? null : Text(identity["exeSha256"]) ?? "\0";
        var sizes = new List<GameArchiveSize>();
        foreach (var archive in archives)
        {
            if (archive is not JsonObject entry || Text(entry["name"]) is not { } name || Integer(entry["bytes"]) is not { } bytes) return null;
            sizes.Add(new(name, bytes));
        }
        var read = new GameFolderIdentity(version, exeBytes, exeSha256, gog, sizes.AsReadOnly());
        return Valid(read) ? read : null;
    }

    private static bool Valid(GameFolderIdentity identity)
        => (identity.Version is null || identity.Version.Length is > 0 and <= MaxVersionLength && identity.Version.All(c => c is >= ' ' and <= '~'))
           && identity.ExeBytes is null or >= 0
           && (identity.ExeSha256 is null || identity.ExeSha256.Length == 64 && identity.ExeSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
           && identity.Archives.Count <= MaxArchives
           && identity.Archives.All(archive => archive.Name.Length is > 0 and <= MaxArchiveName && !archive.Name.Any(char.IsControl) && archive.Bytes >= 0);
}
