using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
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
/// JSON for <see cref="GameSessionSnapshot"/> schema 8. Encoding is canonical and deterministic; decoding is
/// strict on structure and values (exact field sets, sorted collections, bounded size and counts) and never
/// builds an owner or throws on bad text.
/// Semantic admission (identity, clock profile, budgets) stays with <see cref="GameSessionOwner.Restore"/>.
/// </summary>
public static class GameSessionSnapshotCodec
{
    public const string Format = "arcanum-steamwoven-session";
    public const int MaxEncodedCharacters = 8 * 1024 * 1024;

    private static readonly string[] RootFields = ["format", "schemaVersion", "world", "module", "revision", "clock", "flags", "actor", "maps", "story", "party", "hero", "inventory", "random", "life", "lighting"];
    private static readonly string[] ClockFields = ["schemaVersion", "policyId", "sourceRevision", "sourceDataSha256",
        "startupResourceSha256", "startYear", "startHour", "timestampMs", "paused", "pauseReason", "revision"];
    private static readonly string[] FlagsFields = ["revision", "entries"];
    private static readonly string[] FlagFields = ["flag", "value"];
    private static readonly string[] ActorFields = ["map", "x", "y", "facing"];
    private static readonly string[] MapFields = ["map", "changes"];
    private static readonly string[] LightingFields = ["schemeId", "hour"];
    private static readonly string[] SectorChangeFields = ["scope", "sector", "property", "value"];
    private static readonly string[] EntityChangeFields = ["scope", "entity", "property", "value"];
    private static readonly string[] StoryFields = ["quests", "globalQuests", "rumors"];
    private static readonly string[] QuestFields = ["quest", "state", "botched", "timestampMs"];
    private static readonly string[] GlobalQuestFields = ["quest", "state"];
    private static readonly string[] RumorFields = ["rumor", "timestampMs"];
    private static readonly string[] PartyFields = ["followers", "others"];
    private static readonly string[] FollowerFields = ["entity", "forced", "spreadOut"];
    private static readonly string[] OtherFields = ["entity", "standing", "spreadOut", "jilted"];
    private static readonly string[] HeroFields = ["prototype", "race", "gender", "stats", "level", "unspentPoints", "alignment",
        "raceEffect", "genderEffect"];
    private static readonly string[] EffectFields = ["effect", "modifiers"];
    private static readonly string[] ModifierFields = ["stat", "operator", "value"];
    private static readonly string[] InventoryFields = ["gold", "kit", "pc"];
    private static readonly string[] KitItemFields = ["entity", "type", "prototype", "name", "description"];
    private static readonly string[] PcItemFields = ["entity", "map", "type", "prototype", "name", "description"];
    private static readonly string[] RandomFields = ["seed", "draws"];
    private static readonly string[] LifeFields = ["hero", "critters"];
    private static readonly string[] LifeRecordFields = ["hpDamage", "fatigueDamage", "poison", "conditions", "scars", "deathTimeMs"];
    private static readonly string[] CritterLifeFields = ["entity", "hpDamage", "fatigueDamage", "poison", "conditions", "scars", "deathTimeMs"];

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

            var story = snapshot.Story;
            w.WriteStartObject("story");
            w.WriteStartArray("quests");
            foreach (var quest in story.Quests)
            {
                w.WriteStartObject();
                w.WriteNumber("quest", quest.Quest);
                w.WriteString("state", QuestName(quest.State));
                w.WriteBoolean("botched", quest.Botched);
                w.WriteNumber("timestampMs", quest.TimestampMs);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("globalQuests");
            foreach (var quest in story.GlobalQuests)
            {
                w.WriteStartObject();
                w.WriteNumber("quest", quest.Quest);
                w.WriteString("state", QuestName(quest.State));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("rumors");
            foreach (var rumor in story.Rumors)
            {
                w.WriteStartObject();
                w.WriteNumber("rumor", rumor.Rumor);
                w.WriteNumber("timestampMs", rumor.TimestampMs);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("party");
            w.WriteStartArray("followers");
            foreach (var follower in snapshot.Party.Followers)
            {
                w.WriteStartObject();
                w.WriteString("entity", follower.Entity);
                w.WriteBoolean("forced", follower.Forced);
                w.WriteBoolean("spreadOut", follower.SpreadOut);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("others");
            foreach (var other in snapshot.Party.Others)
            {
                w.WriteStartObject();
                w.WriteString("entity", other.Entity);
                w.WriteString("standing", StandingName(other.Standing));
                w.WriteBoolean("spreadOut", other.SpreadOut);
                w.WriteBoolean("jilted", other.Jilted);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            if (snapshot.Hero is { } hero)
            {
                w.WriteStartObject("hero");
                w.WriteNumber("prototype", hero.Prototype);
                w.WriteString("race", RaceNames[(int)hero.Race]);
                w.WriteString("gender", hero.Gender == CharacterGender.Female ? "Female" : "Male");
                w.WriteStartObject("stats");
                for (var i = 0; i < StoredStatNames.Length; i++) w.WriteNumber(StoredStatNames[i], hero.Stats[i]);
                w.WriteEndObject();
                w.WriteNumber("level", hero.Level);
                w.WriteNumber("unspentPoints", hero.UnspentPoints);
                w.WriteNumber("alignment", hero.Alignment);
                WriteEffect(w, "raceEffect", hero.RaceEffect);
                WriteEffect(w, "genderEffect", hero.GenderEffect);
                w.WriteEndObject();
            }
            else w.WriteNull("hero");

            w.WriteStartObject("inventory");
            w.WriteNumber("gold", snapshot.Inventory.Gold);
            w.WriteStartArray("kit");
            foreach (var item in snapshot.Inventory.Kit)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteString("type", ItemTypeName(item.Kind.Type));
                w.WriteNumber("prototype", item.Kind.Prototype);
                w.WriteNumber("name", item.Kind.Name);
                w.WriteNumber("description", item.Kind.Description);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("pc");
            foreach (var item in snapshot.Inventory.Pc)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteNumber("map", item.Origin.RuntimeMap);
                w.WriteString("type", ItemTypeName(item.Kind.Type));
                w.WriteNumber("prototype", item.Kind.Prototype);
                w.WriteNumber("name", item.Kind.Name);
                w.WriteNumber("description", item.Kind.Description);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("random");
            w.WriteString("seed", snapshot.Random.Seed.ToString("x16", CultureInfo.InvariantCulture));
            w.WriteNumber("draws", snapshot.Random.Draws);
            w.WriteEndObject();

            w.WriteStartObject("life");
            if (snapshot.Life.Hero is { } heroLife)
            {
                w.WriteStartObject("hero");
                WriteLifeRecord(w, heroLife);
                w.WriteEndObject();
            }
            else w.WriteNull("hero");
            w.WriteStartArray("critters");
            foreach (var critter in snapshot.Life.Critters)
            {
                w.WriteStartObject();
                w.WriteString("entity", critter.Entity);
                WriteLifeRecord(w, critter.Record);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            if (snapshot.Lighting is { } lighting)
            {
                w.WriteStartObject("lighting");
                w.WriteNumber("schemeId", lighting.SchemeId);
                w.WriteNumber("hour", lighting.Hour);
                w.WriteEndObject();
            }
            else w.WriteNull("lighting");
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteLifeRecord(Utf8JsonWriter w, SessionLifeRecord record)
    {
        var state = record.State;
        w.WriteNumber("hpDamage", state.HitPointDamage);
        w.WriteNumber("fatigueDamage", state.FatigueDamage);
        w.WriteNumber("poison", state.PoisonLevel);
        w.WriteStartArray("conditions");
        for (var i = 0; i < ConditionNames.Length; i++)
            if (((int)state.Conditions & (1 << i)) != 0) w.WriteStringValue(ConditionNames[i]);
        w.WriteEndArray();
        w.WriteNumber("scars", state.Scars);
        if (record.DeathTimeMs is { } time) w.WriteNumber("deathTimeMs", time);
        else w.WriteNull("deathTimeMs");
    }

    private static void WriteEffect(Utf8JsonWriter w, string name, StatEffectDefinition? effect)
    {
        if (effect is null)
        {
            w.WriteNull(name);
            return;
        }
        w.WriteStartObject(name);
        w.WriteNumber("effect", effect.EffectId);
        w.WriteStartArray("modifiers");
        foreach (var modifier in effect.Modifiers)
        {
            w.WriteStartObject();
            w.WriteString("stat", StatNames[(int)modifier.Stat]);
            w.WriteString("operator", OperatorNames[(int)modifier.Operator]);
            w.WriteNumber("value", modifier.Value);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
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
        Int(f["schemaVersion"], "$.schemaVersion", GameSessionSnapshot.CurrentSchemaVersion, GameSessionSnapshot.CurrentSchemaVersion);
        var identity = new GlobalFlagIdentity(new(Guid(f["world"], "$.world")), new(Guid(f["module"], "$.module")));
        var revision = Long(f["revision"], "$.revision", 0, long.MaxValue);
        return new(GameSessionSnapshot.CurrentSchemaVersion, identity, revision,
            Clock(f["clock"]), Flags(f["flags"]), Actor(f["actor"]), Maps(f["maps"]), Story(f["story"]), Party(f["party"]),
            Hero(f["hero"]), Inventory(f["inventory"]), Random(f["random"]), Life(f["life"]), Lighting(f["lighting"]));
    }

    // Ranges and canonical spelling; the hero's presence, death times against the clock and budgets are Restore's.
    private static SessionLifeState Life(JsonElement element)
    {
        const string p = "$.life";
        var f = Fields(element, p, LifeFields);
        SessionLifeRecord? hero = f["hero"].ValueKind == JsonValueKind.Null
            ? null
            : LifeRecord(Fields(f["hero"], p + ".hero", LifeRecordFields), p + ".hero");
        var items = Array(f["critters"], p + ".critters", GameSessionLimits.PolicyMaxLifeRecords);
        var critters = new SessionCritterLife[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.critters[{i}]";
            var item = Fields(items[i], path, CritterLifeFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(critters[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            critters[i] = new(entity, LifeRecord(item, path));
        }
        return new(hero, critters);
    }

    private static SessionLifeRecord LifeRecord(Dictionary<string, JsonElement> f, string path)
    {
        var conditionItems = Array(f["conditions"], path + ".conditions", ConditionNames.Length);
        var conditions = LifeConditions.None;
        var last = -1;
        for (var i = 0; i < conditionItems.Length; i++)
        {
            var at = $"{path}.conditions[{i}]";
            var index = Name(conditionItems[i], at, ConditionNames);
            // Flag order, each once: one spelling per set.
            if (index <= last) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at);
            last = index;
            conditions |= (LifeConditions)(1 << index);
        }
        var death = f["deathTimeMs"];
        return new(new(Int(f["hpDamage"], path + ".hpDamage", 0, LifeLimits.MaxDamage),
                Int(f["fatigueDamage"], path + ".fatigueDamage", 0, LifeLimits.MaxDamage),
                Int(f["poison"], path + ".poison", 0, LifeLimits.MaxPoisonLevel), conditions,
                Int(f["scars"], path + ".scars", 0, LifeLimits.MaxScars)),
            death.ValueKind == JsonValueKind.Null ? null : Long(death, path + ".deathTimeMs", 0, long.MaxValue));
    }

    private static SessionRandomState Random(JsonElement element)
    {
        const string p = "$.random";
        var f = Fields(element, p, RandomFields);
        // Exactly 16 lowercase hex digits, so one seed has one spelling; uppercase digits are another spelling.
        var text = String(f["seed"], p + ".seed");
        if (text.Length != 16 || !text.All(char.IsAsciiHexDigit))
            throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".seed");
        if (!text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            throw new DecodeException(SnapshotDecodeFailure.NotCanonical, p + ".seed");
        var seed = ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new(seed, Long(f["draws"], p + ".draws", 0, long.MaxValue));
    }

    private static SessionLightingState? Lighting(JsonElement element)
    {
        const string p = "$.lighting";
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, p, LightingFields);
        return new(Int(f["schemeId"], p + ".schemeId", 1, int.MaxValue), Int(f["hour"], p + ".hour", 0, 23));
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

    private static SessionStoryState Story(JsonElement element)
    {
        const string p = "$.story";
        var f = Fields(element, p, StoryFields);
        var items = Array(f["quests"], p + ".quests", SessionStoryLimits.MaxQuests);
        var quests = new SessionQuest[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.quests[{i}]";
            var item = Fields(items[i], path, QuestFields);
            var quest = Int(item["quest"], path + ".quest", SessionStoryLimits.FirstQuest, SessionStoryLimits.LastQuest);
            if (i > 0 && quest <= quests[i - 1].Quest) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".quest");
            var state = QuestState(item["state"], path + ".state");
            var botched = Bool(item["botched"], path + ".botched");
            // The stored state is never Botched itself, and an untouched quest is not listed.
            if (state == SessionQuestState.Botched || (state == SessionQuestState.Unknown && !botched))
                throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".state");
            quests[i] = new(quest, state, botched, Long(item["timestampMs"], path + ".timestampMs", 0, long.MaxValue));
        }
        items = Array(f["globalQuests"], p + ".globalQuests", SessionStoryLimits.MaxQuests);
        var globals = new SessionGlobalQuest[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.globalQuests[{i}]";
            var item = Fields(items[i], path, GlobalQuestFields);
            var quest = Int(item["quest"], path + ".quest", SessionStoryLimits.FirstQuest, SessionStoryLimits.LastQuest);
            if (i > 0 && quest <= globals[i - 1].Quest) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".quest");
            var state = QuestState(item["state"], path + ".state");
            // Accepted is the default and is not listed; only Completed and Botched leave it.
            if (state is not (SessionQuestState.Completed or SessionQuestState.Botched))
                throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".state");
            globals[i] = new(quest, state);
        }
        items = Array(f["rumors"], p + ".rumors", SessionStoryLimits.MaxRumors);
        var rumors = new SessionRumor[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.rumors[{i}]";
            var item = Fields(items[i], path, RumorFields);
            var rumor = Int(item["rumor"], path + ".rumor", SessionStoryLimits.FirstRumor, SessionStoryLimits.LastRumor);
            if (i > 0 && rumor <= rumors[i - 1].Rumor) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".rumor");
            rumors[i] = new(rumor, Long(item["timestampMs"], path + ".timestampMs", 0, long.MaxValue));
        }
        return new(quests, globals, rumors);
    }

    private static SessionPartyState Party(JsonElement element)
    {
        const string p = "$.party";
        var f = Fields(element, p, PartyFields);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = Array(f["followers"], p + ".followers", GameSessionLimits.PolicyMaxCompanions);
        var followers = new SessionCompanion[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.followers[{i}]";
            var item = Fields(items[i], path, FollowerFields);
            var entity = Entity(item["entity"], path + ".entity");
            // The array order is meaningful, so it is kept; an entity may appear once in the whole party.
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            followers[i] = new(entity, CompanionStanding.Following, Bool(item["forced"], path + ".forced"),
                Bool(item["spreadOut"], path + ".spreadOut"), false);
        }
        items = Array(f["others"], p + ".others", GameSessionLimits.PolicyMaxCompanions);
        if (followers.Length + items.Length > GameSessionLimits.PolicyMaxCompanions)
            throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, p + ".others");
        var others = new SessionCompanion[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.others[{i}]";
            var item = Fields(items[i], path, OtherFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (!seen.Add(entity) || (i > 0 && string.CompareOrdinal(others[i - 1].Entity, entity) >= 0))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            var standing = String(item["standing"], path + ".standing") switch
            {
                "Waiting" => CompanionStanding.Waiting,
                "Free" => CompanionStanding.Free,
                _ => throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".standing"),
            };
            var record = new SessionCompanion(entity, standing, false, Bool(item["spreadOut"], path + ".spreadOut"),
                Bool(item["jilted"], path + ".jilted"));
            // Jilted only while free, and a free NPC without a flag is not listed.
            if ((standing == CompanionStanding.Waiting && record.Jilted) || record.IsBaseline)
                throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".jilted");
            others[i] = record;
        }
        return new(followers, others);
    }

    // Structure and names only; ranges, effect validity and stats within the race's limits are Restore's (InvalidHero).
    private static SessionHeroState? Hero(JsonElement element)
    {
        const string p = "$.hero";
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, p, HeroFields);
        var race = Name(f["race"], p + ".race", RaceNames);
        var gender = String(f["gender"], p + ".gender") switch
        {
            "Female" => CharacterGender.Female,
            "Male" => CharacterGender.Male,
            _ => throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".gender"),
        };
        var statFields = Fields(f["stats"], p + ".stats", StoredStatNames);
        var stats = StoredStatNames.Select(name => Int(statFields[name], $"{p}.stats.{name}", int.MinValue, int.MaxValue)).ToArray();
        return new(Int(f["prototype"], p + ".prototype", 1, int.MaxValue), (CharacterRace)race, gender, stats,
            Int(f["level"], p + ".level", int.MinValue, int.MaxValue), Int(f["unspentPoints"], p + ".unspentPoints", int.MinValue, int.MaxValue),
            Int(f["alignment"], p + ".alignment", int.MinValue, int.MaxValue), Effect(f["raceEffect"], p + ".raceEffect"),
            Effect(f["genderEffect"], p + ".genderEffect"));
    }

    private static StatEffectDefinition? Effect(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, path, EffectFields);
        var items = Array(f["modifiers"], path + ".modifiers", CharacterStatLimits.ModifiersPerEffectCeiling);
        var modifiers = new StatModifier[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var at = $"{path}.modifiers[{i}]";
            var item = Fields(items[i], at, ModifierFields);
            modifiers[i] = new((CharacterStat)Name(item["stat"], at + ".stat", StatNames),
                (StatModifierOperator)Name(item["operator"], at + ".operator", OperatorNames),
                Int(item["value"], at + ".value", int.MinValue, int.MaxValue));
        }
        return new(Int(f["effect"], path + ".effect", 0, int.MaxValue), ImmutableArray.Create(modifiers));
    }

    private static int Name(JsonElement element, string path, string[] names)
    {
        var index = System.Array.IndexOf(names, String(element, path));
        return index >= 0 ? index : throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
    }

    private static SessionInventoryState Inventory(JsonElement element)
    {
        const string p = "$.inventory";
        var f = Fields(element, p, InventoryFields);
        var gold = Int(f["gold"], p + ".gold", 0, int.MaxValue);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kitItems = Array(f["kit"], p + ".kit", GameSessionLimits.PolicyMaxPcItems);
        var kit = new SessionKitItem[kitItems.Length];
        for (var i = 0; i < kitItems.Length; i++)
        {
            var path = $"{p}.kit[{i}]";
            var item = Fields(kitItems[i], path, KitItemFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            kit[i] = new(entity, Kind(item, path));
        }
        var items = Array(f["pc"], p + ".pc", GameSessionLimits.PolicyMaxPcItems);
        if (kit.Length + items.Length > GameSessionLimits.PolicyMaxPcItems) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, p + ".pc");
        var pc = new SessionPcItem[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.pc[{i}]";
            var item = Fields(items[i], path, PcItemFields);
            var entity = Entity(item["entity"], path + ".entity");
            // The list order is meaningful, so it is kept; an item may appear once.
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            pc[i] = new(entity, new(Int(item["map"], path + ".map", 1, int.MaxValue)), Kind(item, path));
        }
        return new(gold, kit, pc);
    }

    private static SessionItemKind Kind(Dictionary<string, JsonElement> item, string path)
    {
        var type = Enum.GetValues<SessionItemType>().Where(t => ItemTypeName(t) == String(item["type"], path + ".type"))
            .Cast<SessionItemType?>().FirstOrDefault() ?? throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".type");
        return new(type, Int(item["prototype"], path + ".prototype", int.MinValue, int.MaxValue),
            Int(item["name"], path + ".name", int.MinValue, int.MaxValue), Int(item["description"], path + ".description", int.MinValue, int.MaxValue));
    }

    private static string Entity(JsonElement element, string path)
    {
        var entity = String(element, path);
        if (!SessionValidation.Entity(entity)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        return entity;
    }

    private static SessionQuestState QuestState(JsonElement element, string path) =>
        Enum.GetValues<SessionQuestState>().Where(state => QuestName(state) == String(element, path)).Cast<SessionQuestState?>().FirstOrDefault()
            ?? throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);

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

    // Wire names by enum value (the arrays are indexed by the Steamwoven enum, never by a source code).
    private static readonly string[] RaceNames =
        ["Human", "Dwarf", "Elf", "HalfElf", "Gnome", "Halfling", "HalfOrc", "HalfOgre", "DarkElf", "Ogre", "Orc"];
    private static readonly string[] StatNames =
    [
        "Strength", "Dexterity", "Constitution", "Beauty", "Intelligence", "Perception", "Willpower", "Charisma",
        "MagickPoints", "TechPoints", "CarryWeight", "DamageBonus", "AcAdjustment", "Speed", "HealRate", "PoisonRecovery",
        "ReactionModifier", "MaxFollowers", "MagickTechAptitude",
    ];
    private static readonly string[] OperatorNames = ["Add", "Multiply", "Divide", "Minimum", "Maximum", "Percent"];
    /// <summary>Wire names of <see cref="LifeConditions"/> by bit, in flag order.</summary>
    private static readonly string[] ConditionNames = ["Stunned", "Blinded", "CrippledArmOne", "CrippledArmsBoth", "CrippledLegsBoth"];
    /// <summary>Field names of <c>hero.stats</c> in <see cref="SessionHeroLimits.StoredStats"/> order.</summary>
    private static readonly string[] StoredStatNames =
        ["strength", "dexterity", "constitution", "beauty", "intelligence", "perception", "willpower", "charisma", "magickPoints", "techPoints"];

    private static string PauseName(PauseReason reason) => reason switch
    {
        PauseReason.None => "None", PauseReason.User => "User", PauseReason.Menu => "Menu",
        PauseReason.TurnBased => "TurnBased",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string QuestName(SessionQuestState state) => state switch
    {
        SessionQuestState.Unknown => "Unknown", SessionQuestState.Mentioned => "Mentioned", SessionQuestState.Accepted => "Accepted",
        SessionQuestState.Achieved => "Achieved", SessionQuestState.Completed => "Completed",
        SessionQuestState.OtherCompleted => "OtherCompleted", SessionQuestState.Botched => "Botched",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string ItemTypeName(SessionItemType type) => type switch
    {
        SessionItemType.Weapon => "Weapon", SessionItemType.Ammo => "Ammo", SessionItemType.Armor => "Armor",
        SessionItemType.Gold => "Gold", SessionItemType.Food => "Food", SessionItemType.Scroll => "Scroll",
        SessionItemType.Key => "Key", SessionItemType.KeyRing => "KeyRing", SessionItemType.Written => "Written",
        SessionItemType.Generic => "Generic",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string StandingName(CompanionStanding standing) => standing switch
    {
        CompanionStanding.Waiting => "Waiting", CompanionStanding.Free => "Free",
        _ => throw new ArgumentOutOfRangeException(nameof(standing)),
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
