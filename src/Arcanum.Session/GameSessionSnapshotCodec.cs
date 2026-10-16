using System.Buffers;
using System.Text;
using System.Text.Json;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public enum SnapshotDecodeFailure
{
    None, TooLarge, MalformedJson, WrongFormat, UnsupportedSchema, MissingField, UnknownField,
    DuplicateField, WrongType, OutOfRange, NotCanonical, BudgetExceeded,
}

/// <summary>Typed decode outcome. <see cref="Path"/> names the offending field for diagnosis.</summary>
public sealed record SnapshotDecodeResult(GameSessionSnapshot? Snapshot, SnapshotDecodeFailure Failure, string? Path);

/// <summary>
/// JSON for <see cref="GameSessionSnapshot"/> schema 1. Encoding is canonical and deterministic; decoding is
/// strict on structure and values (exact field sets, sorted collections, bounded size and counts) and never
/// builds an owner or throws on bad text.
/// Semantic admission (identity, clock profile, budgets) stays with <see cref="GameSessionOwner.Restore"/>.
/// </summary>
public static class GameSessionSnapshotCodec
{
    public const string Format = "arcanum-steamwoven-session";
    public const int MaxEncodedCharacters = 8 * 1024 * 1024;

    private static readonly string[] RootFields = ["format", "schemaVersion", "world", "module", "revision", "clock", "flags", "actor", "maps"];
    private static readonly string[] ClockFields = ["schemaVersion", "policyId", "sourceRevision", "sourceDataSha256",
        "startupResourceSha256", "startYear", "startHour", "timestampMs", "paused", "pauseReason", "revision"];
    private static readonly string[] FlagsFields = ["revision", "entries"];
    private static readonly string[] FlagFields = ["flag", "value"];
    private static readonly string[] ActorFields = ["map", "x", "y", "facing"];
    private static readonly string[] MapFields = ["map", "changes"];
    private static readonly string[] SectorChangeFields = ["scope", "sector", "property", "value"];
    private static readonly string[] EntityChangeFields = ["scope", "entity", "property", "value"];

    public static string Encode(GameSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("schemaVersion", snapshot.SchemaVersion);
            w.WriteString("world", snapshot.Identity.World.Value.ToString("D"));
            w.WriteString("module", snapshot.Identity.Module.Value.ToString("D"));
            w.WriteNumber("revision", snapshot.Revision);

            var clock = snapshot.Clock;
            w.WriteStartObject("clock");
            w.WriteNumber("schemaVersion", clock.Profile.ClockSchemaVersion);
            w.WriteString("policyId", clock.Profile.PolicyId);
            w.WriteString("sourceRevision", clock.Profile.SourceRevision);
            w.WriteString("sourceDataSha256", clock.Profile.SourceDataSha256);
            w.WriteString("startupResourceSha256", clock.Profile.StartupResourceSha256);
            w.WriteNumber("startYear", clock.Profile.StartYear);
            w.WriteNumber("startHour", clock.Profile.StartHour);
            w.WriteNumber("timestampMs", clock.GameTimestampMs);
            w.WriteBoolean("paused", clock.Pause.Paused);
            w.WriteString("pauseReason", PauseName(clock.Pause.Reason));
            w.WriteNumber("revision", clock.Revision);
            w.WriteEndObject();

            w.WriteStartObject("flags");
            w.WriteNumber("revision", snapshot.Flags.Revision);
            w.WriteStartArray("entries");
            foreach (var entry in snapshot.Flags.Entries)
            {
                w.WriteStartObject();
                w.WriteNumber("flag", entry.Flag.Value);
                if (entry.Value is { } value) w.WriteBoolean("value", value);
                else w.WriteNull("value");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("actor");
            w.WriteNumber("map", snapshot.Actor.Map.RuntimeMap);
            w.WriteNumber("x", snapshot.Actor.Cell.X);
            w.WriteNumber("y", snapshot.Actor.Cell.Y);
            w.WriteString("facing", FacingName(snapshot.Actor.Facing));
            w.WriteEndObject();

            w.WriteStartArray("maps");
            foreach (var map in snapshot.Maps)
            {
                w.WriteStartObject();
                w.WriteNumber("map", map.Map.RuntimeMap);
                w.WriteStartArray("changes");
                foreach (var change in map.Changes)
                {
                    w.WriteStartObject();
                    if (change.Key.Scope == MapChangeScope.Sector)
                    {
                        w.WriteString("scope", "sector");
                        w.WriteNumber("sector", change.Key.Sector);
                    }
                    else
                    {
                        w.WriteString("scope", "entity");
                        w.WriteString("entity", change.Key.Entity);
                    }
                    w.WriteString("property", change.Key.Property);
                    w.WriteNumber("value", change.Value);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static SnapshotDecodeResult TryDecode(string? text)
    {
        if (text is null) return new(null, SnapshotDecodeFailure.MalformedJson, "$");
        if (text.Length > MaxEncodedCharacters) return new(null, SnapshotDecodeFailure.TooLarge, "$");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                MaxDepth = 8, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException) { return new(null, SnapshotDecodeFailure.MalformedJson, "$"); }
        catch (ArgumentException) { return new(null, SnapshotDecodeFailure.MalformedJson, "$"); }
        using (document)
        {
            try { return new(Root(document.RootElement), SnapshotDecodeFailure.None, null); }
            catch (DecodeException e) { return new(null, e.Failure, e.Path); }
        }
    }

    private static GameSessionSnapshot Root(JsonElement root)
    {
        // Format and version first, so a newer schema with other fields reads as UnsupportedSchema, not corruption.
        if (root.ValueKind != JsonValueKind.Object) throw new DecodeException(SnapshotDecodeFailure.WrongType, "$");
        if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            String(format, "$.format") != Format) throw new DecodeException(SnapshotDecodeFailure.WrongFormat, "$.format");
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt64(out var version) || version != GameSessionSnapshot.CurrentSchemaVersion)
            throw new DecodeException(SnapshotDecodeFailure.UnsupportedSchema, "$.schemaVersion");
        var f = Fields(root, "$", RootFields);
        Int(f["schemaVersion"], "$.schemaVersion", 1, 1);
        var identity = new GlobalFlagIdentity(new(Guid(f["world"], "$.world")), new(Guid(f["module"], "$.module")));
        var revision = Long(f["revision"], "$.revision", 0, long.MaxValue);
        return new(GameSessionSnapshot.CurrentSchemaVersion, identity, revision,
            Clock(f["clock"]), Flags(f["flags"]), Actor(f["actor"]), Maps(f["maps"]));
    }

    private static SessionClockState Clock(JsonElement element)
    {
        const string p = "$.clock";
        var f = Fields(element, p, ClockFields);
        var profile = new ProfileBinding(
            Int(f["schemaVersion"], p + ".schemaVersion", int.MinValue, int.MaxValue),
            String(f["policyId"], p + ".policyId"), String(f["sourceRevision"], p + ".sourceRevision"),
            String(f["sourceDataSha256"], p + ".sourceDataSha256"),
            String(f["startupResourceSha256"], p + ".startupResourceSha256"),
            Int(f["startYear"], p + ".startYear", 0, int.MaxValue), Int(f["startHour"], p + ".startHour", 0, 23));
        var timestamp = Long(f["timestampMs"], p + ".timestampMs", 0, long.MaxValue);
        var paused = Bool(f["paused"], p + ".paused");
        var reason = String(f["pauseReason"], p + ".pauseReason") switch
        {
            "None" => PauseReason.None, "User" => PauseReason.User, "Menu" => PauseReason.Menu,
            "TurnBased" => PauseReason.TurnBased,
            _ => throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".pauseReason"),
        };
        if (paused == (reason == PauseReason.None)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".pauseReason");
        return new(profile, timestamp, new(paused, reason), Long(f["revision"], p + ".revision", 0, long.MaxValue));
    }

    private static SessionFlagState Flags(JsonElement element)
    {
        const string p = "$.flags";
        var f = Fields(element, p, FlagsFields);
        var revision = Long(f["revision"], p + ".revision", 0, long.MaxValue);
        var items = Array(f["entries"], p + ".entries", GlobalFlagLimits.MaxFlags);
        var entries = new GlobalFlagEntry[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.entries[{i}]";
            var item = Fields(items[i], path, FlagFields);
            var flag = Int(item["flag"], path + ".flag", 0, int.MaxValue);
            if (i > 0 && flag <= entries[i - 1].Flag.Value) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".flag");
            var value = item["value"];
            entries[i] = new(new(flag), value.ValueKind == JsonValueKind.Null ? null : Bool(value, path + ".value"));
        }
        return new(revision, entries);
    }

    private static ActorPlacement Actor(JsonElement element)
    {
        const string p = "$.actor";
        var f = Fields(element, p, ActorFields);
        var facing = String(f["facing"], p + ".facing");
        var direction = Enum.GetValues<NeighborDirection>().Where(d => FacingName(d) == facing).Cast<NeighborDirection?>().FirstOrDefault()
            ?? throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".facing");
        return new(new(Int(f["map"], p + ".map", 1, int.MaxValue)),
            new(Int(f["x"], p + ".x", 0, int.MaxValue), Int(f["y"], p + ".y", 0, int.MaxValue)), direction);
    }

    private static SessionMapState[] Maps(JsonElement element)
    {
        var items = Array(element, "$.maps", GameSessionLimits.PolicyMaxMaps);
        var maps = new SessionMapState[items.Length];
        var total = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"$.maps[{i}]";
            var f = Fields(items[i], path, MapFields);
            var map = Int(f["map"], path + ".map", 1, int.MaxValue);
            if (i > 0 && map <= maps[i - 1].Map.RuntimeMap) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".map");
            var changeItems = Array(f["changes"], path + ".changes", GameSessionLimits.PolicyMaxChangesPerMap);
            if (changeItems.Length == 0) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".changes");
            total += changeItems.Length;
            if (total > GameSessionLimits.PolicyMaxTotalChanges) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, path + ".changes");
            var changes = new MapChange[changeItems.Length];
            for (var j = 0; j < changeItems.Length; j++)
            {
                changes[j] = Change(changeItems[j], $"{path}.changes[{j}]");
                if (j > 0 && MapChangeKeyComparer.Instance.Compare(changes[j - 1].Key, changes[j].Key) >= 0)
                    throw new DecodeException(SnapshotDecodeFailure.NotCanonical, $"{path}.changes[{j}]");
            }
            maps[i] = new(new(map), changes);
        }
        return maps;
    }

    private static MapChange Change(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        var scope = element.TryGetProperty("scope", out var s) ? String(s, path + ".scope") : null;
        MapChangeKey key;
        Dictionary<string, JsonElement> f;
        if (scope == "sector")
        {
            f = Fields(element, path, SectorChangeFields);
            key = MapChangeKey.ForSector(Long(f["sector"], path + ".sector", 0, long.MaxValue), String(f["property"], path + ".property"));
        }
        else if (scope == "entity")
        {
            f = Fields(element, path, EntityChangeFields);
            key = MapChangeKey.ForEntity(String(f["entity"], path + ".entity"), String(f["property"], path + ".property"));
            if (!SessionValidation.Code(key.Entity, SessionValidation.MaxEntityCharacters))
                throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".entity");
        }
        else
        {
            Fields(element, path, SectorChangeFields);
            throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".scope");
        }
        if (!SessionValidation.Code(key.Property, SessionValidation.MaxPropertyCharacters))
            throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".property");
        return new(key, Long(f["value"], path + ".value", long.MinValue, long.MaxValue));
    }

    // Exact field set: duplicates, unknown and missing names are distinct failures.
    private static Dictionary<string, JsonElement> Fields(JsonElement element, string path, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
                throw new DecodeException(SnapshotDecodeFailure.UnknownField, $"{path}.{property.Name}");
            if (!fields.TryAdd(property.Name, property.Value))
                throw new DecodeException(SnapshotDecodeFailure.DuplicateField, $"{path}.{property.Name}");
        }
        foreach (var name in names)
            if (!fields.ContainsKey(name)) throw new DecodeException(SnapshotDecodeFailure.MissingField, $"{path}.{name}");
        return fields;
    }

    private static JsonElement[] Array(JsonElement element, string path, int max)
    {
        if (element.ValueKind != JsonValueKind.Array) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        if (element.GetArrayLength() > max) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, path);
        return element.EnumerateArray().ToArray();
    }

    private static string String(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        // Valid JSON may still escape a lone UTF-16 surrogate, which has no string value.
        try { return element.GetString()!; }
        catch (InvalidOperationException) { throw new DecodeException(SnapshotDecodeFailure.MalformedJson, path); }
    }

    private static bool Bool(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new DecodeException(SnapshotDecodeFailure.WrongType, path),
    };

    private static long Long(JsonElement element, string path, long min, long max)
    {
        if (element.ValueKind != JsonValueKind.Number) throw new DecodeException(SnapshotDecodeFailure.WrongType, path);
        // Integer literals only: fractions and exponents fail TryGetInt64 even when integral.
        var raw = element.GetRawText();
        if (!element.TryGetInt64(out var value) || !IsIntegerLiteral(raw) || raw == "-0")
            throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        if (value < min || value > max) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        return value;
    }

    private static int Int(JsonElement element, string path, int min, int max) => (int)Long(element, path, min, max);

    private static bool IsIntegerLiteral(string raw)
    {
        foreach (var c in raw)
            if (c is not ((>= '0' and <= '9') or '-')) return false;
        return true;
    }

    private static Guid Guid(JsonElement element, string path)
    {
        var text = String(element, path);
        if (!System.Guid.TryParseExact(text, "D", out var value) || value.ToString("D") != text)
            throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        return value;
    }

    private static string PauseName(PauseReason reason) => reason switch
    {
        PauseReason.None => "None", PauseReason.User => "User", PauseReason.Menu => "Menu",
        PauseReason.TurnBased => "TurnBased",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string FacingName(NeighborDirection facing) => facing switch
    {
        NeighborDirection.XMinusYMinus => "XMinusYMinus", NeighborDirection.XMinus => "XMinus",
        NeighborDirection.XMinusYPlus => "XMinusYPlus", NeighborDirection.YPlus => "YPlus",
        NeighborDirection.XPlusYPlus => "XPlusYPlus", NeighborDirection.XPlus => "XPlus",
        NeighborDirection.XPlusYMinus => "XPlusYMinus", NeighborDirection.YMinus => "YMinus",
        _ => throw new ArgumentOutOfRangeException(nameof(facing)),
    };

    private sealed class DecodeException(SnapshotDecodeFailure failure, string path) : Exception
    {
        public SnapshotDecodeFailure Failure { get; } = failure;
        public string Path { get; } = path;
    }
}
