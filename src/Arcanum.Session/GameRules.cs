using System.Text.Json;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.Session;

/// <summary>
/// The game-wide rule mode of owner decision D8 (contract docs/world/rule-modes.md). <see cref="Steamwoven"/> is the
/// original game with the recorded fixes and is the default; <see cref="Original"/> is the retail 1.0.7.4 behaviour,
/// including the bugs of the original game that were confirmed in its executable. A CE reconstruction slip that the
/// original does not have is not a mode difference: both modes follow the original there.
/// </summary>
public enum RuleMode
{
    Steamwoven,
    Original,
}

/// <summary>
/// The rules of every domain under one <see cref="RuleMode"/>: one immutable, stateless profile per domain, each holding
/// the switches of that domain's original-game bugs. A domain reads the profile of the session's active rules at the
/// moment it decides, never caches it, and keeps no state that depends on it, so the mode can change between any two
/// calls without a restart and any save loads under either mode (D4). A domain slice that adds switches adds its
/// profile here; until then it has no difference between the modes.
/// </summary>
public sealed record GameRules
{
    private GameRules(RuleMode mode, CombatAnomalyProfile combat, GameEventQueueProfile events)
    {
        Mode = mode;
        Combat = combat;
        Events = events;
    }

    public RuleMode Mode { get; }

    /// <summary>Stable name of the mode, <c>steamwoven</c> or <c>original</c>, shared by every domain profile.</summary>
    public string Name => RuleModeNames.Name(Mode);

    /// <summary>Combat (ARC-COMBAT-001): A2, A6, A7, A10 and the reserved A9.</summary>
    public CombatAnomalyProfile Combat { get; }

    /// <summary>Game event queue (ARC-WORLD-022): OQ2, AI ticks rebuilt on load.</summary>
    public GameEventQueueProfile Events { get; }

    /// <summary>The default mode: the original with every recorded fix.</summary>
    public static GameRules Steamwoven { get; } = new(RuleMode.Steamwoven, CombatAnomalyProfile.Steamwoven, GameEventQueueProfile.Steamwoven);

    /// <summary>The retail 1.0.7.4 behaviour in every domain.</summary>
    public static GameRules Original { get; } = new(RuleMode.Original, CombatAnomalyProfile.Original, GameEventQueueProfile.Original);

    /// <summary>Both modes, default first.</summary>
    public static IReadOnlyList<GameRules> All { get; } = [Steamwoven, Original];

    /// <summary>The rules of a mode; an undefined value throws.</summary>
    public static GameRules For(RuleMode mode) => mode switch
    {
        RuleMode.Steamwoven => Steamwoven,
        RuleMode.Original => Original,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

/// <summary>The stable mode names used by settings, traces and every domain profile's <c>Name</c>.</summary>
public static class RuleModeNames
{
    public const string Steamwoven = "steamwoven";
    public const string Original = "original";

    public static string Name(RuleMode mode) => mode switch
    {
        RuleMode.Steamwoven => Steamwoven,
        RuleMode.Original => Original,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Exact, case-sensitive lookup; any other text is not a mode.</summary>
    public static bool TryParse(string? name, out RuleMode mode)
    {
        switch (name)
        {
            case Steamwoven: mode = RuleMode.Steamwoven; return true;
            case Original: mode = RuleMode.Original; return true;
            default: mode = RuleMode.Steamwoven; return false;
        }
    }
}

/// <summary>
/// The saved rule mode setting for the settings menu: <c>{"schema":1,"rules":"steamwoven"|"original"}</c>. A missing,
/// unreadable or unknown value loads as <see cref="RuleMode.Steamwoven"/>; nothing else is stored, and the mode is
/// never part of a session snapshot or quick save.
/// </summary>
public static class RuleModeSettingCodec
{
    public const int Schema = 1;

    public static RuleMode DeserializeOrSteamwoven(string? json)
    {
        if (json is null) return RuleMode.Steamwoven;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return RuleMode.Steamwoven;
            // Exactly the two properties: an extra or repeated one is an unknown value.
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != 2 || !names.Contains("schema", StringComparer.Ordinal) || !names.Contains("rules", StringComparer.Ordinal))
                return RuleMode.Steamwoven;
            var schema = root.GetProperty("schema"); var rules = root.GetProperty("rules");
            if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != Schema
                || rules.ValueKind != JsonValueKind.String) return RuleMode.Steamwoven;
            return RuleModeNames.TryParse(rules.GetString(), out var mode) ? mode : RuleMode.Steamwoven;
        }
        // Text that is not valid UTF-16 is unreadable too: a raw lone surrogate (ArgumentException) or an escaped one
        // that the string read refuses (InvalidOperationException).
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return RuleMode.Steamwoven; }
    }

    public static string Serialize(RuleMode mode) => $"{{\"schema\":{Schema},\"rules\":\"{RuleModeNames.Name(mode)}\"}}";
}
