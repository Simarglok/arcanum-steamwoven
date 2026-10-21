using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

public enum SnapshotDecodeFailure
{
    None, TooLarge, MalformedJson, WrongFormat, UnsupportedSchema, MissingField, UnknownField,
    DuplicateField, WrongType, OutOfRange, NotCanonical, BudgetExceeded,
}

/// <summary>Typed decode outcome. <see cref="Path"/> names the offending field for diagnosis.</summary>
public sealed record SnapshotDecodeResult(GameSessionSnapshot? Snapshot, SnapshotDecodeFailure Failure, string? Path);

/// <summary>
/// JSON for <see cref="GameSessionSnapshot"/> at <see cref="GameSessionSnapshot.CurrentSchemaVersion"/>. Encoding is canonical and deterministic; decoding is
/// strict on structure and values (exact field sets, sorted collections, bounded size and counts) and never
/// builds an owner or throws on bad text.
/// Semantic admission (identity, clock profile, budgets, and the order and meaning of pending events, whose queue order
/// needs the kind registry) stays with <see cref="GameSessionOwner.Restore"/>.
/// </summary>
public static class GameSessionSnapshotCodec
{
    public const string Format = "arcanum-steamwoven-session";
    /// <summary>ARC-DATA-029: a policy cap on one snapshot's text (and save file). It holds a change for every object
    /// of every installed map (276,625, under the viewer's 68-character object ids) and a life, reaction and experience
    /// record with their timers for every NPC (3,792), about 43 MB, with 2.3 times headroom; a snapshot can still exceed
    /// it at the policy maxima with the longest keys, and saving it is then a typed <c>TooLarge</c>.</summary>
    public const int MaxEncodedCharacters = 96 * 1024 * 1024;

    private static readonly string[] RootFields = ["format", "schemaVersion", "world", "module", "revision", "clock", "flags", "actor", "maps", "story", "party", "hero", "inventory", "equipment", "durability", "schematics", "effects", "experience", "advancement", "random", "life", "reactions", "combat", "combatMode", "sound", "events", "lighting"];
    private static readonly string[] ClockFields = ["schemaVersion", "policyId", "sourceRevision", "sourceDataSha256",
        "startupResourceSha256", "startYear", "startHour", "timestampMs", "paused", "pauseReason", "revision"];
    private static readonly string[] FlagsFields = ["revision", "entries"];
    private static readonly string[] FlagFields = ["flag", "value"];
    private static readonly string[] ActorFields = ["map", "x", "y", "facing"];
    private static readonly string[] MapFields = ["map", "changes"];
    private static readonly string[] LightingFields = ["schemeId", "hour"];
    private static readonly string[] SectorChangeFields = ["scope", "sector", "property", "value"];
    private static readonly string[] EntityChangeFields = ["scope", "entity", "property", "value"];
    private static readonly string[] StoryFields = ["quests", "globalQuests", "rumors", "quelledRumors", "knownAreas", "lastArea", "reputations"];
    private static readonly string[] QuestFields = ["quest", "state", "botched", "timestampMs"];
    private static readonly string[] GlobalQuestFields = ["quest", "state"];
    private static readonly string[] RumorFields = ["rumor", "timestampMs"];
    private static readonly string[] ReputationFields = ["reputation", "timestampMs"];
    private static readonly string[] PartyFields = ["followers", "others"];
    private static readonly string[] FollowerFields = ["entity", "forced", "spreadOut", "loyalty"];
    private static readonly string[] OtherFields = ["entity", "standing", "spreadOut", "jilted", "loyalty"];
    private static readonly string[] HeroFields = ["prototype", "race", "gender", "stats", "level", "experience", "unspentPoints",
        "alignment", "advancement", "raceEffect", "genderEffect", "innateSkills", "fatePoints", "armedFates", "background"];
    private static readonly string[] EffectFields = ["effect", "modifiers"];
    private static readonly string[] InnateSkillFields = ["cause", "skill", "operator", "value"];
    // Skill names in Skill order (ARC-SKILLS-002), the innate skill change's causes.
    private static readonly string[] SkillNames = ["Bow", "Dodge", "Melee", "Throwing", "Backstab", "PickPocket", "Prowling", "SpotTrap",
        "Gambling", "Haggle", "Heal", "Persuasion", "Repair", "Firearms", "PickLocks", "DisarmTraps"];
    private static readonly string[] InnateCauseNames = ["Race", "Gender"];
    private static readonly string[] ModifierFields = ["stat", "operator", "value"];
    private static readonly string[] InventoryFields = ["gold", "kit", "pc", "made", "npcs", "placed", "ammo"];
    private static readonly string[] AmmoFields = ["entity", "ammoType", "units"];
    private static readonly string[] PlacedItemFields = ["entity", "source", "origin", "map", "x", "y", "type", "prototype", "name", "description"];
    private static readonly string[] NpcInventoryFields = ["npc", "items"];
    private static readonly string[] KitItemFields = ["entity", "type", "prototype", "name", "description"];
    private static readonly string[] PcItemFields = ["entity", "map", "type", "prototype", "name", "description"];
    private static readonly string[] WornFields = ["slot", "entity", "item"];
    private static readonly string[] DurabilityFields = ["items", "destroyed", "uses"];
    private static readonly string[] ItemUseFields = ["entity", "uses"];
    private static readonly string[] SchematicsFields = ["found"];
    private static readonly string[] ItemConditionFields = ["entity", "points", "adjustment", "damage", "broken", "inventoryArtBroken"];
    private static readonly string[] EquipmentItemFields = ["kind", "complexity", "noDrop", "broken", "uber", "light", "usesShieldLocation",
        "hp", "maxHp", "weapon", "armor"];
    private static readonly string[] EquipmentWeaponFields = ["twoHanded", "handCountFixed", "ignoresResistance", "damagesArmor", "skill",
        "damage", "magicDamage", "toHit", "magicToHit", "speed", "magicSpeed", "range", "magicRange", "minStrength", "magicMinStrength",
        "ammoType", "ammoConsumption", "critHitChart", "critMissChart", "magicCritHitChance", "magicCritHitEffect", "magicCritMissChance",
        "magicCritMissEffect"];
    private static readonly string[] EquipmentArmorFields = ["coverage", "sizes", "maleOnly", "femaleOnly", "ac", "magicAc", "resistances",
        "magicResistances", "unarmedBonus"];
    private static readonly string[] EffectsFields = ["hero", "definitions", "blessings", "curses"];
    private static readonly string[] BlessingFields = ["number", "effect", "timeMs"];
    private static readonly string[] HeroEffectFields = ["effect", "cause"];
    private static readonly string[] DefinitionFields = ["effect", "changes"];
    private static readonly string[] ChangeFields = ["kind", "attribute", "operator", "value"];
    private static readonly string[] ExperienceFields = ["npcs"];
    private static readonly string[] NpcExperienceFields = ["entity", "worth", "pool"];
    private static readonly string[] HeroAdvancementFields = ["basicSkills", "techSkills", "colleges", "mastery", "techDegrees",
        "hitPointPoints", "fatiguePoints", "scheme"];
    private static readonly string[] AdvancementFields = ["npcs", "missed"];
    private static readonly string[] MissedFields = ["entity", "steps"];
    private static readonly string[] MissedStepFields = ["oldPcLevel", "newPcLevel", "times"];
    private static readonly string[] NpcAdvancementFields = ["entity", "race", "monstrous", "stats", "magickPoints", "techPoints", "level",
        "unspentPoints", "basicSkills", "techSkills", "colleges", "mastery", "techDegrees", "hitPointPoints", "hitPointAdjustment",
        "fatiguePoints", "fatigueAdjustment", "scheme"];
    private static readonly string[] RandomFields = ["seed", "draws"];
    private static readonly string[] LifeFields = ["hero", "critters"];
    private static readonly string[] LifeRecordFields = ["hpDamage", "fatigueDamage", "poison", "conditions", "scars", "deathTimeMs"];
    private static readonly string[] CritterLifeFields = ["entity", "hpDamage", "fatigueDamage", "poison", "conditions", "scars", "deathTimeMs"];
    private static readonly string[] ReactionsFields = ["records"];
    private static readonly string[] ReactionFields = ["entity", "remembered", "conversation"];
    private static readonly string[] EventsFields = ["registry", "nextSequence", "pending"];
    private static readonly string[] CombatFields = ["turn", "participants", "current", "actionPoints"];
    private static readonly string[] CombatModeFields = ["hero", "critters"];
    private static readonly string[] SoundFields = ["slots", "restorePending", "restore", "combatMusic", "preCombat"];
    private static readonly string[] PendingEventFields = ["sequence", "kind", "deadlineMs", "entities", "values"];
    private static readonly string[] EventEntityFields = ["map", "entity"];

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
            w.WriteStartArray("quelledRumors");
            foreach (var rumor in story.QuelledRumors) w.WriteNumberValue(rumor);
            w.WriteEndArray();
            w.WriteStartArray("knownAreas");
            foreach (var area in story.KnownAreas) w.WriteNumberValue(area);
            w.WriteEndArray();
            w.WriteNumber("lastArea", story.LastArea);
            w.WriteStartArray("reputations");
            foreach (var reputation in story.Reputations)
            {
                w.WriteStartObject();
                w.WriteNumber("reputation", reputation.Reputation);
                w.WriteNumber("timestampMs", reputation.TimestampMs);
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
                w.WriteNumber("loyalty", (int)follower.Loyalty);
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
                w.WriteNumber("loyalty", (int)other.Loyalty);
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
                w.WriteNumber("experience", hero.Experience);
                w.WriteNumber("unspentPoints", hero.UnspentPoints);
                w.WriteNumber("alignment", hero.Alignment);
                w.WriteStartObject("advancement");
                WriteInts(w, "basicSkills", hero.Advancement.BasicSkills);
                WriteInts(w, "techSkills", hero.Advancement.TechSkills);
                WriteInts(w, "colleges", hero.Advancement.Colleges);
                w.WriteNumber("mastery", hero.Advancement.MasteredCollege);
                WriteInts(w, "techDegrees", hero.Advancement.TechDegrees);
                w.WriteNumber("hitPointPoints", hero.Advancement.HitPointPoints);
                w.WriteNumber("fatiguePoints", hero.Advancement.FatiguePoints);
                w.WriteNumber("scheme", hero.Advancement.AutoLevelScheme);
                w.WriteEndObject();
                WriteEffect(w, "raceEffect", hero.RaceEffect);
                WriteEffect(w, "genderEffect", hero.GenderEffect);
                w.WriteStartArray("innateSkills");
                foreach (var change in hero.InnateSkills)
                {
                    w.WriteStartObject();
                    w.WriteString("cause", change.Cause == EffectCause.Race ? "Race" : "Gender");
                    w.WriteString("skill", SkillNames[(int)change.Skill]);
                    w.WriteString("operator", OperatorNames[(int)change.Operator]);
                    w.WriteNumber("value", change.Value);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteNumber("fatePoints", hero.Fate.Points);
                w.WriteNumber("armedFates", hero.Fate.Armed);
                w.WriteNumber("background", hero.Background);
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
            w.WriteStartArray("made");
            foreach (var item in snapshot.Inventory.Made) WriteMade(w, item);
            w.WriteEndArray();
            // ARC-WORLD-055: by NPC entity in ordinal order, each list in its order.
            w.WriteStartArray("npcs");
            foreach (var npc in snapshot.Inventory.Npcs)
            {
                w.WriteStartObject();
                w.WriteString("npc", npc.Npc);
                w.WriteStartArray("items");
                foreach (var item in npc.Items) WriteMade(w, item);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-059: in placing order; the origin map only for an item the PC took from the world (else 0).
            w.WriteStartArray("placed");
            foreach (var item in snapshot.Inventory.Placed)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteString("source", PlacedSourceName(item.Source));
                w.WriteNumber("origin", item.Origin?.RuntimeMap ?? 0);
                w.WriteNumber("map", item.Map.RuntimeMap);
                w.WriteNumber("x", item.Cell.X);
                w.WriteNumber("y", item.Cell.Y);
                w.WriteString("type", ItemTypeName(item.Kind.Type));
                w.WriteNumber("prototype", item.Kind.Prototype);
                w.WriteNumber("name", item.Kind.Name);
                w.WriteNumber("description", item.Kind.Description);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-066: by entity in ordinal order.
            w.WriteStartArray("ammo");
            foreach (var stack in snapshot.Inventory.Ammo)
            {
                w.WriteStartObject();
                w.WriteString("entity", stack.Entity);
                w.WriteNumber("ammoType", stack.AmmoType);
                w.WriteNumber("units", stack.Units);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartArray("equipment");
            foreach (var worn in snapshot.Equipment.Worn) WriteWorn(w, worn);
            w.WriteEndArray();

            w.WriteStartObject("durability");
            w.WriteStartArray("items");
            foreach (var item in snapshot.Durability.Items)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteNumber("points", item.Condition.Points);
                w.WriteNumber("adjustment", item.Condition.Adjustment);
                w.WriteNumber("damage", item.Condition.Damage);
                w.WriteBoolean("broken", item.Condition.Broken);
                w.WriteBoolean("inventoryArtBroken", item.Condition.InventoryArtBroken);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("destroyed");
            foreach (var entity in snapshot.Durability.Destroyed) w.WriteStringValue(entity);
            w.WriteEndArray();
            w.WriteStartArray("uses");
            foreach (var item in snapshot.Durability.Uses)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteNumber("uses", item.Uses);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("schematics");
            w.WriteStartArray("found");
            foreach (var number in snapshot.Schematics.Found) w.WriteNumberValue(number);
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("effects");
            w.WriteStartArray("hero");
            foreach (var entry in snapshot.Effects.Hero)
            {
                w.WriteStartObject();
                w.WriteNumber("effect", entry.Number);
                w.WriteString("cause", entry.Cause.ToString());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("definitions");
            foreach (var definition in snapshot.Effects.Definitions)
            {
                w.WriteStartObject();
                w.WriteNumber("effect", definition.Number);
                w.WriteStartArray("changes");
                foreach (var change in definition.Changes)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", change.Kind.ToString());
                    w.WriteNumber("attribute", change.Attribute);
                    w.WriteString("operator", change.Operator.ToString());
                    w.WriteNumber("value", change.Value);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            foreach (var (name, list) in new[] { ("blessings", snapshot.Effects.Blessings), ("curses", snapshot.Effects.Curses) })
            {
                w.WriteStartArray(name);
                foreach (var entry in list)
                {
                    w.WriteStartObject();
                    w.WriteNumber("number", entry.Number);
                    w.WriteNumber("effect", entry.Effect);
                    w.WriteNumber("timeMs", entry.TimeMs);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();

            w.WriteStartObject("experience");
            w.WriteStartArray("npcs");
            foreach (var npc in snapshot.Experience.Npcs)
            {
                w.WriteStartObject();
                w.WriteString("entity", npc.Entity);
                w.WriteNumber("worth", npc.Worth);
                w.WriteNumber("pool", npc.Pool);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("advancement");
            w.WriteStartArray("npcs");
            foreach (var npc in snapshot.Advancement.Npcs)
            {
                var sheet = npc.Sheet;
                w.WriteStartObject();
                w.WriteString("entity", npc.Entity);
                w.WriteString("race", RaceNames[(int)sheet.Race]);
                w.WriteBoolean("monstrous", sheet.Monstrous);
                WriteInts(w, "stats", sheet.Stats);
                w.WriteNumber("magickPoints", sheet.MagickPoints);
                w.WriteNumber("techPoints", sheet.TechPoints);
                w.WriteNumber("level", sheet.Level);
                w.WriteNumber("unspentPoints", sheet.UnspentPoints);
                WriteInts(w, "basicSkills", sheet.BasicSkills);
                WriteInts(w, "techSkills", sheet.TechSkills);
                WriteInts(w, "colleges", sheet.Colleges);
                w.WriteNumber("mastery", sheet.MasteredCollege);
                WriteInts(w, "techDegrees", sheet.TechDegrees);
                w.WriteNumber("hitPointPoints", sheet.HitPointPoints);
                w.WriteNumber("hitPointAdjustment", sheet.HitPointAdjustment);
                w.WriteNumber("fatiguePoints", sheet.FatiguePoints);
                w.WriteNumber("fatigueAdjustment", sheet.FatigueAdjustment);
                w.WriteNumber("scheme", npc.AutoLevelScheme);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("missed");
            foreach (var missed in snapshot.Advancement.Missed)
            {
                w.WriteStartObject();
                w.WriteString("entity", missed.Entity);
                w.WriteStartArray("steps");
                foreach (var step in missed.Steps)
                {
                    w.WriteStartObject();
                    w.WriteNumber("oldPcLevel", step.Step.OldPcLevel);
                    w.WriteNumber("newPcLevel", step.Step.NewPcLevel);
                    w.WriteNumber("times", step.Times);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
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

            w.WriteStartObject("reactions");
            w.WriteStartArray("records");
            foreach (var record in snapshot.Reactions.Records)
            {
                w.WriteStartObject();
                w.WriteString("entity", record.Entity);
                if (record.Remembered is { } remembered) w.WriteNumber("remembered", remembered);
                else w.WriteNull("remembered");
                if (record.Conversation is { } conversation) w.WriteNumber("conversation", conversation);
                else w.WriteNull("conversation");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            if (snapshot.Combat is { } combat)
            {
                w.WriteStartObject("combat");
                w.WriteNumber("turn", combat.Turn);
                w.WriteStartArray("participants");
                foreach (var participant in combat.Participants)
                {
                    if (participant.Entity is { } entity) w.WriteStringValue(entity);
                    else w.WriteNullValue();
                }
                w.WriteEndArray();
                w.WriteNumber("current", combat.Current);
                w.WriteNumber("actionPoints", combat.ActionPoints);
                w.WriteEndObject();
            }
            else w.WriteNull("combat");

            w.WriteStartObject("combatMode");
            w.WriteBoolean("hero", snapshot.CombatMode.Hero);
            w.WriteStartArray("critters");
            foreach (var entity in snapshot.CombatMode.Critters) w.WriteStringValue(entity);
            w.WriteEndArray();
            w.WriteEndObject();

            var sound = snapshot.Sound;
            w.WriteStartObject("sound");
            w.WriteStartArray("slots"); w.WriteNumberValue(sound.Slot0); w.WriteNumberValue(sound.Slot1); w.WriteEndArray();
            w.WriteBoolean("restorePending", sound.RestorePending);
            w.WriteStartArray("restore"); w.WriteNumberValue(sound.Restore0); w.WriteNumberValue(sound.Restore1); w.WriteEndArray();
            w.WriteBoolean("combatMusic", sound.CombatMusic);
            w.WriteStartArray("preCombat"); w.WriteNumberValue(sound.PreCombat0); w.WriteNumberValue(sound.PreCombat1); w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("events");
            w.WriteNumber("registry", snapshot.Events.Registry);
            w.WriteNumber("nextSequence", snapshot.Events.NextSequence);
            w.WriteStartArray("pending");
            foreach (var pending in snapshot.Events.Pending)
            {
                w.WriteStartObject();
                w.WriteNumber("sequence", pending.Sequence);
                w.WriteString("kind", pending.Kind);
                w.WriteNumber("deadlineMs", pending.DeadlineMs);
                w.WriteStartArray("entities");
                foreach (var entity in pending.Entities)
                {
                    w.WriteStartObject();
                    w.WriteNumber("map", entity.Map);
                    w.WriteString("entity", entity.Code);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("values");
                foreach (var value in pending.Values) w.WriteNumberValue(value);
                w.WriteEndArray();
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
            Hero(f["hero"]), Inventory(f["inventory"]), Equipment(f["equipment"]), Durability(f["durability"]), Schematics(f["schematics"]), Effects(f["effects"]), Experience(f["experience"]), Advancement(f["advancement"]), Random(f["random"]), Life(f["life"]), Reactions(f["reactions"]), Combat(f["combat"]), CombatMode(f["combatMode"]), Events(f["events"]), Lighting(f["lighting"]), Sound(f["sound"]));
    }

    // Shapes and ordinal order; the values' ranges and the budget are Restore's.
    private static SessionExperienceState Experience(JsonElement element)
    {
        const string p = "$.experience";
        var f = Fields(element, p, ExperienceFields);
        var items = Array(f["npcs"], p + ".npcs", GameSessionLimits.PolicyMaxExperienceRecords);
        var npcs = new SessionNpcExperience[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.npcs[{i}]";
            var item = Fields(items[i], path, NpcExperienceFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(npcs[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            npcs[i] = new(entity, Int(item["worth"], path + ".worth", int.MinValue, int.MaxValue),
                Int(item["pool"], path + ".pool", int.MinValue, int.MaxValue));
        }
        return new(npcs);
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

    // Ranges, ordinal order and no baseline record; the budget is Restore's.
    private static SessionReactionState Reactions(JsonElement element)
    {
        const string p = "$.reactions";
        var f = Fields(element, p, ReactionsFields);
        var items = Array(f["records"], p + ".records", GameSessionLimits.PolicyMaxReactionRecords);
        var records = new SessionReaction[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.records[{i}]";
            var item = Fields(items[i], path, ReactionFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(records[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            int? Slot(string name) => item[name].ValueKind == JsonValueKind.Null
                ? null : Int(item[name], $"{path}.{name}", -ReactionLimits.MaxMagnitude, ReactionLimits.MaxMagnitude);
            records[i] = new(entity, Slot("remembered"), Slot("conversation"));
            if (records[i].IsBaseline) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        return new(records);
    }

    // Shapes and ranges; the hero's presence, unique participants and the index against the list are Restore's.
    private static SessionCombatRound? Combat(JsonElement element)
    {
        const string p = "$.combat";
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, p, CombatFields);
        var items = Array(f["participants"], p + ".participants", SessionCombatLimits.MaxParticipants);
        var participants = new SessionCombatant[items.Length];
        for (var i = 0; i < items.Length; i++)
            participants[i] = items[i].ValueKind == JsonValueKind.Null
                ? SessionCombatant.Hero
                : SessionCombatant.Critter(Entity(items[i], $"{p}.participants[{i}]"));
        return new(Int(f["turn"], p + ".turn", 1, int.MaxValue), participants,
            Int(f["current"], p + ".current", 0, SessionCombatLimits.MaxParticipants - 1),
            Int(f["actionPoints"], p + ".actionPoints", 0, TurnBasedRules.MaxActionPoints));
    }

    // Shapes and ordinal order; the hero's presence is Restore's.
    private static SessionCombatModeState CombatMode(JsonElement element)
    {
        const string p = "$.combatMode";
        var f = Fields(element, p, CombatModeFields);
        var items = Array(f["critters"], p + ".critters", SessionCombatModeLimits.MaxCritters);
        var critters = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            critters[i] = Entity(items[i], $"{p}.critters[{i}]");
            if (i > 0 && string.CompareOrdinal(critters[i - 1], critters[i]) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, $"{p}.critters[{i}]");
        }
        return new(Bool(f["hero"], p + ".hero"), critters);
    }

    // Shapes and ranges; the slot invariants are Restore's.
    private static SessionSoundState Sound(JsonElement element)
    {
        const string p = "$.sound";
        var f = Fields(element, p, SoundFields);
        (int, int) Pair(string name)
        {
            var items = Array(f[name], $"{p}.{name}", 16);
            if (items.Length != 2) throw new DecodeException(SnapshotDecodeFailure.WrongType, $"{p}.{name}");
            return (Int(items[0], $"{p}.{name}[0]", 0, int.MaxValue), Int(items[1], $"{p}.{name}[1]", 0, int.MaxValue));
        }
        var slots = Pair("slots"); var restore = Pair("restore"); var pre = Pair("preCombat");
        return new(slots.Item1, slots.Item2, Bool(f["restorePending"], p + ".restorePending"), restore.Item1, restore.Item2,
            Bool(f["combatMusic"], p + ".combatMusic"), pre.Item1, pre.Item2);
    }

    // Shapes and ranges only: kinds, sequence order, deadlines against the clock and the NPCs' records are Restore's.
    private static SessionEventState Events(JsonElement element)
    {
        const string p = "$.events";
        var f = Fields(element, p, EventsFields);
        var registry = Int(f["registry"], p + ".registry", 1, int.MaxValue);
        var next = Long(f["nextSequence"], p + ".nextSequence", 1, long.MaxValue);
        var items = Array(f["pending"], p + ".pending", SessionEventKinds.MaxPending);
        var pending = new SessionPendingEvent[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.pending[{i}]";
            var item = Fields(items[i], path, PendingEventFields);
            var kind = String(item["kind"], path + ".kind");
            if (!SessionValidation.Code(kind, GameEventRegistry.MaxNameCharacters)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".kind");
            var entityItems = Array(item["entities"], path + ".entities", GameEventQueueLimits.Default.MaxEntitiesPerEvent);
            var entities = ImmutableArray.CreateBuilder<GameEventEntity>(entityItems.Length);
            for (var j = 0; j < entityItems.Length; j++)
            {
                var at = $"{path}.entities[{j}]";
                var entity = Fields(entityItems[j], at, EventEntityFields);
                entities.Add(new(Int(entity["map"], at + ".map", 1, int.MaxValue), Entity(entity["entity"], at + ".entity")));
            }
            var valueItems = Array(item["values"], path + ".values", GameEventQueueLimits.Default.MaxValuesPerEvent);
            var values = ImmutableArray.CreateBuilder<long>(valueItems.Length);
            for (var j = 0; j < valueItems.Length; j++) values.Add(Long(valueItems[j], $"{path}.values[{j}]", long.MinValue, long.MaxValue));
            pending[i] = new(Long(item["sequence"], path + ".sequence", 1, long.MaxValue), kind,
                Long(item["deadlineMs"], path + ".deadlineMs", 0, long.MaxValue), entities.MoveToImmutable(), values.MoveToImmutable());
        }
        return new(registry, next, pending);
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
        // ARC-UI-008: the rumors proven false, a game-wide set (not the PC's): ascending numbers in the rumor range.
        items = Array(f["quelledRumors"], p + ".quelledRumors", SessionStoryLimits.MaxRumors);
        var quelled = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.quelledRumors[{i}]";
            quelled[i] = Int(items[i], path, SessionStoryLimits.FirstRumor, SessionStoryLimits.LastRumor);
            if (i > 0 && quelled[i] <= quelled[i - 1]) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        items = Array(f["knownAreas"], p + ".knownAreas", SessionStoryLimits.LastArea);
        var areas = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.knownAreas[{i}]";
            areas[i] = Int(items[i], path, 1, SessionStoryLimits.LastArea);
            if (i > 0 && areas[i] <= areas[i - 1]) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        var last = Int(f["lastArea"], p + ".lastArea", 0, SessionStoryLimits.LastArea);
        // ARC-WORLD-044: the PC's reputations in the order gained (not sorted): numbers in the reputation range, none repeated,
        // times never decreasing.
        items = Array(f["reputations"], p + ".reputations", SessionStoryLimits.MaxReputations);
        var reputations = new SessionReputation[items.Length];
        var held = new HashSet<int>();
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.reputations[{i}]";
            var item = Fields(items[i], path, ReputationFields);
            var reputation = Int(item["reputation"], path + ".reputation", SessionStoryLimits.FirstReputation, SessionStoryLimits.LastReputation);
            if (!held.Add(reputation)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".reputation");
            reputations[i] = new(reputation, Long(item["timestampMs"], path + ".timestampMs", 0, long.MaxValue));
            if (i > 0 && reputations[i].TimestampMs < reputations[i - 1].TimestampMs)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".timestampMs");
        }
        return new(quests, globals, rumors, quelled, areas, last, reputations);
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
                Bool(item["spreadOut"], path + ".spreadOut"), false, Loyalty(item["loyalty"], path + ".loyalty"));
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
                Bool(item["jilted"], path + ".jilted"), Loyalty(item["loyalty"], path + ".loyalty"));
            // Jilted only while free, and a free NPC without a flag is not listed.
            if ((standing == CompanionStanding.Waiting && record.Jilted) || record.IsBaseline)
                throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".jilted");
            others[i] = record;
        }
        return new(followers, others);
    }

    // ARC-WORLD-045: the four loyalty marks as their bit sum.
    private static CompanionLoyalty Loyalty(JsonElement element, string path) => (CompanionLoyalty)Int(element, path, 0, (int)CompanionLoyalty.All);

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
            Effect(f["genderEffect"], p + ".genderEffect"), Int(f["experience"], p + ".experience", int.MinValue, int.MaxValue),
            HeroAdvancement(f["advancement"], p + ".advancement"), InnateSkills(f["innateSkills"], p + ".innateSkills"),
            new FateState(Int(f["fatePoints"], p + ".fatePoints", int.MinValue, int.MaxValue),
                Int(f["armedFates"], p + ".armedFates", int.MinValue, int.MaxValue)), Int(f["background"], p + ".background", int.MinValue, int.MaxValue));
    }

    // Shapes only; the count, magnitudes and divisors are the hero's import (InvalidHero).
    private static ImmutableArray<SessionInnateSkillChange> InnateSkills(JsonElement element, string p)
    {
        var items = Array(element, p, SessionInnateSkillChange.MaxChanges);
        var changes = ImmutableArray.CreateBuilder<SessionInnateSkillChange>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var at = $"{p}[{i}]";
            var item = Fields(items[i], at, InnateSkillFields);
            changes.Add(new(Name(item["cause"], at + ".cause", InnateCauseNames) == 0 ? EffectCause.Race : EffectCause.Gender,
                (Skill)Name(item["skill"], at + ".skill", SkillNames), (StatModifierOperator)Name(item["operator"], at + ".operator", OperatorNames),
                Int(item["value"], at + ".value", int.MinValue, int.MaxValue)));
        }
        return changes.MoveToImmutable();
    }

    // Shapes only; lengths and ranges are the hero's import (InvalidHero).
    private static SessionHeroAdvancement HeroAdvancement(JsonElement element, string p)
    {
        var f = Fields(element, p, HeroAdvancementFields);
        return new(Ints(f["basicSkills"], p + ".basicSkills"), Ints(f["techSkills"], p + ".techSkills"), Ints(f["colleges"], p + ".colleges"),
            Ints(f["techDegrees"], p + ".techDegrees"), Int(f["hitPointPoints"], p + ".hitPointPoints", int.MinValue, int.MaxValue),
            Int(f["fatiguePoints"], p + ".fatiguePoints", int.MinValue, int.MaxValue), Int(f["scheme"], p + ".scheme", int.MinValue, int.MaxValue),
            Int(f["mastery"], p + ".mastery", int.MinValue, int.MaxValue));
    }

    // Shapes and ordinal order; lengths, ranges and the budget are Restore's.
    private static SessionAdvancementState Advancement(JsonElement element)
    {
        const string p = "$.advancement";
        var f = Fields(element, p, AdvancementFields);
        var items = Array(f["npcs"], p + ".npcs", GameSessionLimits.PolicyMaxAdvancementRecords);
        var npcs = new SessionNpcAdvancement[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.npcs[{i}]";
            var n = Fields(items[i], path, NpcAdvancementFields);
            var entity = Entity(n["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(npcs[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            int Value(string name) => Int(n[name], $"{path}.{name}", int.MinValue, int.MaxValue);
            var sheet = new Arcanum.Simulation.Character.AdvancementSheet((CharacterRace)Name(n["race"], path + ".race", RaceNames),
                Bool(n["monstrous"], path + ".monstrous"), Ints(n["stats"], path + ".stats"), Value("magickPoints"), Value("techPoints"),
                Value("level"), Value("unspentPoints"), Ints(n["basicSkills"], path + ".basicSkills"), Ints(n["techSkills"], path + ".techSkills"),
                Ints(n["colleges"], path + ".colleges"), Ints(n["techDegrees"], path + ".techDegrees"), Value("hitPointPoints"),
                Value("hitPointAdjustment"), Value("fatiguePoints"), Value("fatigueAdjustment"), Value("mastery"));
            npcs[i] = new(entity, sheet, Value("scheme"));
        }
        var missedItems = Array(f["missed"], p + ".missed", GameSessionLimits.PolicyMaxAdvancementRecords);
        var missed = new SessionMissedLevelSteps[missedItems.Length];
        for (var i = 0; i < missedItems.Length; i++)
        {
            var path = $"{p}.missed[{i}]";
            var m = Fields(missedItems[i], path, MissedFields);
            var entity = Entity(m["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(missed[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            var stepItems = Array(m["steps"], path + ".steps", Arcanum.Simulation.Character.ExperienceRules.MaxMissedStepEntries);
            var steps = new Arcanum.Simulation.Character.MissedLevelStep[stepItems.Length];
            for (var j = 0; j < stepItems.Length; j++)
            {
                var stepPath = $"{path}.steps[{j}]";
                var s = Fields(stepItems[j], stepPath, MissedStepFields);
                int Level(string name) => Int(s[name], $"{stepPath}.{name}", 0, Arcanum.Simulation.Character.ExperienceRules.MaxLevel);
                steps[j] = new(new(Level("oldPcLevel"), Level("newPcLevel")),
                    Int(s["times"], stepPath + ".times", 1, Arcanum.Simulation.Character.ExperienceRules.MaxLevel));
            }
            missed[i] = new(entity, steps);
        }
        return new(npcs, missed);
    }

    private static ImmutableArray<int> Ints(JsonElement element, string path)
    {
        var items = Array(element, path, 32);
        return [.. items.Select((item, i) => Int(item, $"{path}[{i}]", int.MinValue, int.MaxValue))];
    }

    private static void WriteInts(Utf8JsonWriter w, string name, ImmutableArray<int> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values) w.WriteNumberValue(value);
        w.WriteEndArray();
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
        var madeItems = Array(f["made"], p + ".made", GameSessionLimits.PolicyMaxPcItems);
        if (kit.Length + items.Length + madeItems.Length > GameSessionLimits.PolicyMaxPcItems) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, p + ".made");
        var made = new SessionMadeItem[madeItems.Length];
        for (var i = 0; i < madeItems.Length; i++)
        {
            var path = $"{p}.made[{i}]";
            var item = Fields(madeItems[i], path, KitItemFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            made[i] = new(entity, Kind(item, path));
        }
        // ARC-WORLD-055: the NPCs in strictly ascending ordinal order, none with an empty list; an item listed once in the whole
        // inventory.
        var npcRows = Array(f["npcs"], p + ".npcs", GameSessionLimits.PolicyMaxNpcItems);
        var npcs = new SessionNpcInventory[npcRows.Length];
        var held = 0;
        string? previous = null;
        for (var i = 0; i < npcRows.Length; i++)
        {
            var path = $"{p}.npcs[{i}]";
            var row = Fields(npcRows[i], path, NpcInventoryFields);
            var npc = Entity(row["npc"], path + ".npc");
            if (previous is not null && string.CompareOrdinal(previous, npc) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".npc");
            previous = npc;
            var rows = Array(row["items"], path + ".items", SessionMapInventorySeed.MaxListLength);
            if (rows.Length == 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".items");
            held += rows.Length;
            if (held > GameSessionLimits.PolicyMaxNpcItems) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, path + ".items");
            var list = new SessionMadeItem[rows.Length];
            for (var j = 0; j < rows.Length; j++)
            {
                var itemPath = $"{path}.items[{j}]";
                var item = Fields(rows[j], itemPath, KitItemFields);
                var entity = Entity(item["entity"], itemPath + ".entity");
                if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, itemPath + ".entity");
                list[j] = new(entity, Kind(item, itemPath));
            }
            npcs[i] = new(npc, System.Array.AsReadOnly(list));
        }
        // ARC-WORLD-059: the hero's items placed on the ground share the hero's budget and its once-only rule.
        var placedRows = Array(f["placed"], p + ".placed", GameSessionLimits.PolicyMaxPcItems);
        if (kit.Length + items.Length + madeItems.Length + placedRows.Length > GameSessionLimits.PolicyMaxPcItems)
            throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, p + ".placed");
        var placed = new SessionPlacedItem[placedRows.Length];
        for (var i = 0; i < placedRows.Length; i++)
        {
            var path = $"{p}.placed[{i}]";
            var item = Fields(placedRows[i], path, PlacedItemFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            var source = PlacedSource(item["source"], path + ".source");
            var origin = Int(item["origin"], path + ".origin", 0, int.MaxValue);
            if ((origin == 0) != (source != SessionPlacedSource.Pc)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".origin");
            placed[i] = new(entity, Kind(item, path), source, origin == 0 ? null : new SessionMapId(origin),
                new(Int(item["map"], path + ".map", 1, int.MaxValue)),
                new(Int(item["x"], path + ".x", 0, MaxPlacedCoordinate), Int(item["y"], path + ".y", 0, MaxPlacedCoordinate)));
        }
        // ARC-WORLD-066: the ammunition stacks by entity in strictly ascending ordinal order.
        var ammoRows = Array(f["ammo"], p + ".ammo", GameSessionLimits.PolicyMaxAmmoRecords);
        var ammo = new SessionAmmoRecord[ammoRows.Length];
        string? lastAmmo = null;
        for (var i = 0; i < ammoRows.Length; i++)
        {
            var path = $"{p}.ammo[{i}]";
            var row = Fields(ammoRows[i], path, AmmoFields);
            var entity = Entity(row["entity"], path + ".entity");
            if (lastAmmo is not null && string.CompareOrdinal(lastAmmo, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            lastAmmo = entity;
            ammo[i] = new(entity, Int(row["ammoType"], path + ".ammoType", 0, SessionAmmoRecord.AmmoTypes - 1), Int(row["units"], path + ".units", 0, int.MaxValue));
        }
        return new(gold, kit, pc, made, npcs, placed, ammo);
    }

    // A placed item's cell coordinates fit 24 bits (the drop's packing, ARC-COMBAT-009).
    private const int MaxPlacedCoordinate = (1 << 24) - 1;

    private static string PlacedSourceName(SessionPlacedSource source) => source switch
    {
        SessionPlacedSource.Kit => "kit",
        SessionPlacedSource.Pc => "pc",
        SessionPlacedSource.Made => "made",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static SessionPlacedSource PlacedSource(JsonElement element, string path) => String(element, path) switch
    {
        "kit" => SessionPlacedSource.Kit,
        "pc" => SessionPlacedSource.Pc,
        "made" => SessionPlacedSource.Made,
        _ => throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path),
    };

    private static void WriteMade(Utf8JsonWriter w, SessionMadeItem item)
    {
        w.WriteStartObject();
        w.WriteString("entity", item.Entity);
        w.WriteString("type", ItemTypeName(item.Kind.Type));
        w.WriteNumber("prototype", item.Kind.Prototype);
        w.WriteNumber("name", item.Kind.Name);
        w.WriteNumber("description", item.Kind.Description);
        w.WriteEndObject();
    }

    private static void WriteWorn(Utf8JsonWriter w, SessionWornItem worn)
    {
        var item = worn.Item;
        w.WriteStartObject();
        w.WriteString("slot", worn.Slot.ToString());
        w.WriteString("entity", worn.Entity);
        w.WriteStartObject("item");
        w.WriteString("kind", item.Kind.ToString());
        w.WriteNumber("complexity", item.MagickTechComplexity);
        w.WriteBoolean("noDrop", item.NoDrop);
        w.WriteBoolean("broken", item.Broken);
        w.WriteBoolean("uber", item.Uber);
        w.WriteNumber("light", (int)item.Light);
        w.WriteBoolean("usesShieldLocation", item.UsesShieldLocation);
        w.WriteNumber("hp", item.HitPoints);
        w.WriteNumber("maxHp", item.MaxHitPoints);
        if (item.Weapon is { } weapon)
        {
            w.WriteStartObject("weapon");
            w.WriteBoolean("twoHanded", weapon.TwoHanded);
            w.WriteBoolean("handCountFixed", weapon.HandCountFixed);
            w.WriteBoolean("ignoresResistance", weapon.IgnoresResistance);
            w.WriteBoolean("damagesArmor", weapon.DamagesArmor);
            w.WriteString("skill", weapon.Skill.ToString());
            w.WriteStartArray("damage");
            foreach (var type in Enum.GetValues<DamageType>())
            {
                w.WriteStartArray();
                w.WriteNumberValue(weapon.Damage[type].Min);
                w.WriteNumberValue(weapon.Damage[type].Max);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteStartArray("magicDamage");
            foreach (var type in Enum.GetValues<DamageType>()) w.WriteNumberValue(weapon.MagicDamage[type]);
            w.WriteEndArray();
            w.WriteNumber("toHit", weapon.ToHit);
            w.WriteNumber("magicToHit", weapon.MagicToHit);
            w.WriteNumber("speed", weapon.SpeedFactor);
            w.WriteNumber("magicSpeed", weapon.MagicSpeed);
            w.WriteNumber("range", weapon.Range);
            w.WriteNumber("magicRange", weapon.MagicRange);
            w.WriteNumber("minStrength", weapon.MinimumStrength);
            w.WriteNumber("magicMinStrength", weapon.MagicMinimumStrength);
            if (weapon.AmmoType is { } ammo) w.WriteNumber("ammoType", ammo);
            else w.WriteNull("ammoType");
            w.WriteNumber("ammoConsumption", weapon.AmmoConsumption);
            w.WriteString("critHitChart", weapon.CriticalHitChart.ToString());
            w.WriteString("critMissChart", weapon.CriticalMissChart.ToString());
            w.WriteNumber("magicCritHitChance", weapon.MagicCriticalHitChance);
            w.WriteNumber("magicCritHitEffect", weapon.MagicCriticalHitEffect);
            w.WriteNumber("magicCritMissChance", weapon.MagicCriticalMissChance);
            w.WriteNumber("magicCritMissEffect", weapon.MagicCriticalMissEffect);
            w.WriteEndObject();
        }
        else w.WriteNull("weapon");
        if (item.Armor is { } armor)
        {
            w.WriteStartObject("armor");
            w.WriteString("coverage", armor.Coverage.ToString());
            w.WriteNumber("sizes", (int)armor.Sizes);
            w.WriteBoolean("maleOnly", armor.MaleOnly);
            w.WriteBoolean("femaleOnly", armor.FemaleOnly);
            w.WriteNumber("ac", armor.AcAdjustment);
            w.WriteNumber("magicAc", armor.MagicAcAdjustment);
            WriteResistances(w, "resistances", armor.Resistances);
            WriteResistances(w, "magicResistances", armor.MagicResistances);
            w.WriteNumber("unarmedBonus", armor.UnarmedBonusDamage);
            w.WriteEndObject();
        }
        else w.WriteNull("armor");
        w.WriteEndObject();
        w.WriteEndObject();
    }

    // Resistances in the source's order: normal, fire, electrical, poison, magic.
    private static void WriteResistances(Utf8JsonWriter w, string name, EquipmentResistances r)
    {
        w.WriteStartArray(name);
        foreach (var value in new[] { r.Normal, r.Fire, r.Electrical, r.Poison, r.Magic }) w.WriteNumberValue(value);
        w.WriteEndArray();
    }

    // Shapes, names and ranges, slots strictly ascending and entities unique; that each item is held and fits its slot is Restore's.
    private static SessionEquipmentState Equipment(JsonElement element)
    {
        const string p = "$.equipment";
        var items = Array(element, p, Enum.GetValues<EquipmentSlot>().Length);
        var worn = new SessionWornItem[items.Length];
        var entities = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}[{i}]";
            var f = Fields(items[i], path, WornFields);
            var slot = Named<EquipmentSlot>(f["slot"], path + ".slot");
            if (i > 0 && slot <= worn[i - 1].Slot) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".slot");
            var entity = Entity(f["entity"], path + ".entity");
            if (!entities.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            worn[i] = new(slot, entity, EquipmentItemValue(f["item"], path + ".item"));
        }
        return new(worn);
    }

    // The found numbers in registration order (not sorted); repeats, the hero's presence and the limit are Restore's.
    private static SessionSchematicState Schematics(JsonElement element)
    {
        const string p = "$.schematics";
        var f = Fields(element, p, SchematicsFields);
        var rows = Array(f["found"], p + ".found", GameSessionLimits.PolicyMaxFoundSchematics);
        var found = new int[rows.Length];
        for (var i = 0; i < rows.Length; i++) found[i] = Int(rows[i], $"{p}.found[{i}]", int.MinValue, int.MaxValue);
        return new([.. found]);
    }

    // Shapes, ranges and strictly ascending ordinal entities in each list; the record budget, a deleted item with a record and the
    // lists' agreement with the inventory and the worn items are Restore's.
    private static SessionDurabilityState Durability(JsonElement element)
    {
        const string p = "$.durability";
        const int m = Arcanum.Simulation.Items.ItemDurabilityLimits.MaxMagnitude;
        var f = Fields(element, p, DurabilityFields);
        var rows = Array(f["items"], p + ".items", GameSessionLimits.PolicyMaxDurabilityRecords);
        var items = new SessionItemDurability[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var path = $"{p}.items[{i}]";
            var item = Fields(rows[i], path, ItemConditionFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(entity, items[i - 1].Entity) <= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            items[i] = new(entity, new(Int(item["points"], path + ".points", -m, m), Int(item["adjustment"], path + ".adjustment", -m, m),
                Int(item["damage"], path + ".damage", 0, Arcanum.Simulation.Items.ItemDurabilityLimits.MaxDamage),
                Bool(item["broken"], path + ".broken"), Bool(item["inventoryArtBroken"], path + ".inventoryArtBroken")));
        }
        var listed = Array(f["destroyed"], p + ".destroyed", GameSessionLimits.PolicyMaxDurabilityRecords);
        var destroyed = new string[listed.Length];
        for (var i = 0; i < listed.Length; i++)
        {
            var path = $"{p}.destroyed[{i}]";
            destroyed[i] = Entity(listed[i], path);
            if (i > 0 && string.CompareOrdinal(destroyed[i], destroyed[i - 1]) <= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        var used = Array(f["uses"], p + ".uses", GameSessionLimits.PolicyMaxDurabilityRecords);
        var uses = new SessionItemUses[used.Length];
        for (var i = 0; i < used.Length; i++)
        {
            var path = $"{p}.uses[{i}]";
            var item = Fields(used[i], path, ItemUseFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(entity, uses[i - 1].Entity) <= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            uses[i] = new(entity, Int(item["uses"], path + ".uses", 1, SessionItemUseLimits.MaxUses));
        }
        return new(items, destroyed, uses);
    }

    // Shapes, names and ranges, definitions by strictly ascending number; causes, rules in use and the hero's presence are Restore's.
    private static SessionEffectState Effects(JsonElement element)
    {
        const string p = "$.effects";
        var f = Fields(element, p, EffectsFields);
        var entries = Array(f["hero"], p + ".hero", SessionEffectLimits.MaxHeroEffects);
        var hero = new SessionEffect[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var path = $"{p}.hero[{i}]";
            var e = Fields(entries[i], path, HeroEffectFields);
            hero[i] = new(Int(e["effect"], path + ".effect", SessionEffectLimits.FirstNumber, SessionEffectLimits.LastNumber),
                Named<Arcanum.Simulation.Character.EffectCause>(e["cause"], path + ".cause"));
        }
        var items = Array(f["definitions"], p + ".definitions", SessionEffectLimits.MaxHeroEffects);
        var definitions = new SessionEffectDefinition[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.definitions[{i}]";
            var d = Fields(items[i], path, DefinitionFields);
            var number = Int(d["effect"], path + ".effect", SessionEffectLimits.FirstNumber, SessionEffectLimits.LastNumber);
            if (i > 0 && number <= definitions[i - 1].Number) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".effect");
            var list = Array(d["changes"], path + ".changes", 6 * Arcanum.Data.Characters.EffectRules.MaxChangesPerCollection);
            var changes = new Arcanum.Data.Characters.EffectChange[list.Length];
            for (var j = 0; j < list.Length; j++)
            {
                var at = $"{path}.changes[{j}]";
                var c = Fields(list[j], at, ChangeFields);
                changes[j] = new(Named<Arcanum.Data.Characters.EffectAttributeKind>(c["kind"], at + ".kind"),
                    Int(c["attribute"], at + ".attribute", 0, 27),
                    Named<Arcanum.Data.Characters.EffectChangeOperator>(c["operator"], at + ".operator"),
                    Int(c["value"], at + ".value", -SessionEffectLimits.MaxMagnitude, SessionEffectLimits.MaxMagnitude));
            }
            definitions[i] = new(number, ImmutableArray.Create(changes));
        }
        SessionBlessing[] Blessings(string name)
        {
            var list = Array(f[name], $"{p}.{name}", SessionEffectLimits.MaxBlessings);
            var result = new SessionBlessing[list.Length];
            for (var i = 0; i < list.Length; i++)
            {
                var at = $"{p}.{name}[{i}]";
                var b = Fields(list[i], at, BlessingFields);
                result[i] = new(Int(b["number"], at + ".number", int.MinValue, int.MaxValue),
                    Int(b["effect"], at + ".effect", SessionEffectLimits.FirstNumber, SessionEffectLimits.LastNumber),
                    Long(b["timeMs"], at + ".timeMs", 0, long.MaxValue));
            }
            return result;
        }
        return new(hero, definitions, Blessings("blessings"), Blessings("curses"));
    }

    private static EquipmentItem EquipmentItemValue(JsonElement element, string p)
    {
        const int m = EquipmentLimits.MaxMagnitude;
        var f = Fields(element, p, EquipmentItemFields);
        int Num(Dictionary<string, JsonElement> fields, string name, string path, int min = -m, int max = m) => Int(fields[name], path + "." + name, min, max);
        var item = new EquipmentItem
        {
            Kind = Named<EquipmentItemKind>(f["kind"], p + ".kind"),
            MagickTechComplexity = Num(f, "complexity", p),
            NoDrop = Bool(f["noDrop"], p + ".noDrop"),
            Broken = Bool(f["broken"], p + ".broken"),
            Uber = Bool(f["uber"], p + ".uber"),
            Light = (EquipmentLight)Num(f, "light", p, 0, 15),
            UsesShieldLocation = Bool(f["usesShieldLocation"], p + ".usesShieldLocation"),
            HitPoints = Num(f, "hp", p, 0, EquipmentLimits.MaxHitPoints),
            MaxHitPoints = Num(f, "maxHp", p, 0, EquipmentLimits.MaxHitPoints),
        };
        if (f["weapon"].ValueKind != JsonValueKind.Null)
        {
            var wp = p + ".weapon";
            var w = Fields(f["weapon"], wp, EquipmentWeaponFields);
            var damage = Array(w["damage"], wp + ".damage", 5);
            var magic = Array(w["magicDamage"], wp + ".magicDamage", 5);
            if (damage.Length != 5) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, wp + ".damage");
            if (magic.Length != 5) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, wp + ".magicDamage");
            var ranges = new DamageRange[5];
            for (var i = 0; i < 5; i++)
            {
                var pair = Array(damage[i], $"{wp}.damage[{i}]", 2);
                if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, $"{wp}.damage[{i}]");
                ranges[i] = new(Int(pair[0], $"{wp}.damage[{i}][0]", -m, m), Int(pair[1], $"{wp}.damage[{i}][1]", -m, m));
            }
            var magicDamage = Enumerable.Range(0, 5).Select(i => Int(magic[i], $"{wp}.magicDamage[{i}]", -m, m)).ToArray();
            item = item with
            {
                Weapon = new EquipmentWeapon
                {
                    TwoHanded = Bool(w["twoHanded"], wp + ".twoHanded"),
                    HandCountFixed = Bool(w["handCountFixed"], wp + ".handCountFixed"),
                    IgnoresResistance = Bool(w["ignoresResistance"], wp + ".ignoresResistance"),
                    DamagesArmor = Bool(w["damagesArmor"], wp + ".damagesArmor"),
                    Skill = Named<CombatSkill>(w["skill"], wp + ".skill"),
                    Damage = new(ranges[0], ranges[1], ranges[2], ranges[3], ranges[4]),
                    MagicDamage = new(magicDamage[0], magicDamage[1], magicDamage[2], magicDamage[3], magicDamage[4]),
                    ToHit = Num(w, "toHit", wp), MagicToHit = Num(w, "magicToHit", wp),
                    SpeedFactor = Num(w, "speed", wp, -EquipmentLimits.MaxSpeed, EquipmentLimits.MaxSpeed),
                    MagicSpeed = Num(w, "magicSpeed", wp, -EquipmentLimits.MaxSpeed, EquipmentLimits.MaxSpeed),
                    Range = Num(w, "range", wp), MagicRange = Num(w, "magicRange", wp),
                    MinimumStrength = Num(w, "minStrength", wp), MagicMinimumStrength = Num(w, "magicMinStrength", wp),
                    AmmoType = w["ammoType"].ValueKind == JsonValueKind.Null ? null : Num(w, "ammoType", wp, 0),
                    AmmoConsumption = Num(w, "ammoConsumption", wp, 0),
                    CriticalHitChart = Named<CriticalHitChart>(w["critHitChart"], wp + ".critHitChart"),
                    CriticalMissChart = Named<CriticalMissChart>(w["critMissChart"], wp + ".critMissChart"),
                    MagicCriticalHitChance = Num(w, "magicCritHitChance", wp), MagicCriticalHitEffect = Num(w, "magicCritHitEffect", wp),
                    MagicCriticalMissChance = Num(w, "magicCritMissChance", wp), MagicCriticalMissEffect = Num(w, "magicCritMissEffect", wp),
                },
            };
        }
        if (f["armor"].ValueKind != JsonValueKind.Null)
        {
            var ap = p + ".armor";
            var a = Fields(f["armor"], ap, EquipmentArmorFields);
            item = item with
            {
                Armor = new EquipmentArmor
                {
                    Coverage = Named<ArmorCoverage>(a["coverage"], ap + ".coverage"),
                    Sizes = (ArmorSizes)Num(a, "sizes", ap, 0, 7),
                    MaleOnly = Bool(a["maleOnly"], ap + ".maleOnly"),
                    FemaleOnly = Bool(a["femaleOnly"], ap + ".femaleOnly"),
                    AcAdjustment = Num(a, "ac", ap), MagicAcAdjustment = Num(a, "magicAc", ap),
                    Resistances = ResistanceValues(a["resistances"], ap + ".resistances"),
                    MagicResistances = ResistanceValues(a["magicResistances"], ap + ".magicResistances"),
                    UnarmedBonusDamage = Num(a, "unarmedBonus", ap),
                },
            };
        }
        // Which blocks a kind has, and the condition order, are the item rules' (Restore refuses them as InvalidEquipment).
        return item;
    }

    private static EquipmentResistances ResistanceValues(JsonElement element, string path)
    {
        const int m = EquipmentLimits.MaxMagnitude;
        var values = Array(element, path, 5);
        if (values.Length != 5) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        int At(int i) => Int(values[i], $"{path}[{i}]", -m, m);
        return new(At(0), At(1), At(2), At(3), At(4));
    }

    private static T Named<T>(JsonElement element, string path) where T : struct, Enum =>
        Enum.TryParse<T>(String(element, path), false, out var value) && Enum.IsDefined(value) && value.ToString() == String(element, path)
            ? value : throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);

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
