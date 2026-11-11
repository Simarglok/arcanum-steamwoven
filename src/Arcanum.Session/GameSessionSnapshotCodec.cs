using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Ai;
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
/// JSON for <see cref="GameSessionSnapshot"/> at <see cref="GameSessionSnapshot.CurrentSchemaVersion"/> (decoding upgrades the older schemas
/// from <see cref="OldestReadableSchemaVersion"/> first, ARC-UI-037). Encoding is canonical and deterministic; decoding is
/// strict on structure and values (exact field sets, sorted collections, bounded size and counts) and never
/// builds an owner or throws on bad text.
/// Semantic admission (identity, clock profile, budgets, and the order and meaning of pending events, whose queue order
/// needs the kind registry) stays with <see cref="GameSessionOwner.Restore"/>.
/// </summary>
public static partial class GameSessionSnapshotCodec
{
    public const string Format = "arcanum-steamwoven-session";
    /// <summary>ARC-DATA-029: a policy cap on one snapshot's text (and save file). It holds a change for every object
    /// of every installed map (276,625, under the viewer's 68-character object ids) and a life, reaction and experience
    /// record with their timers for every NPC (3,792), about 43 MB, with 2.3 times headroom; a snapshot can still exceed
    /// it at the policy maxima with the longest keys, and saving it is then a typed <c>TooLarge</c>.</summary>
    public const int MaxEncodedCharacters = 96 * 1024 * 1024;

    private static readonly string[] RootFields = ["format", "schemaVersion", "world", "module", "revision", "clock", "flags", "actor", "maps", "story", "party", "hero", "inventory", "objects", "equipment", "durability", "schematics", "effects", "experience", "advancement", "random", "life", "reactions", "combat", "combatMode", "concealment", "danger", "sound", "events", "hotkeys", "scripts", "residency", "goals", "spells", "newspaper", "flights", "lighting"];
    private static readonly string[] ClockFields = ["schemaVersion", "policyId", "sourceRevision", "sourceDataSha256",
        "startupResourceSha256", "startYear", "startHour", "timestampMs", "paused", "pauseReason", "revision", "realMs", "animationMs"];
    private static readonly string[] FlagsFields = ["revision", "entries"];
    private static readonly string[] FlagFields = ["flag", "value"];
    private static readonly string[] ActorFields = ["map", "x", "y", "facing"];
    private static readonly string[] MapFields = ["map", "changes"];
    private static readonly string[] LightingFields = ["schemeId", "hour"];
    private static readonly string[] SectorChangeFields = ["scope", "sector", "property", "value"];
    private static readonly string[] EntityChangeFields = ["scope", "entity", "property", "value"];
    private static readonly string[] StoryFields = ["quests", "globalQuests", "rumors", "quelledRumors", "knownAreas", "lastArea", "reputations", "storyState",
        "revealedTownMaps", "townMapCells", "heroKeys", "killLog", "townNotes", "mapNotes"];
    private static readonly string[] KillLogFields = ["total", "records", "injuries"];
    private static readonly string[] TownMapCellFields = ["townMap", "bits"];
    private static readonly string[] TownNotesFields = ["townMap", "nextId", "notes"];
    private static readonly string[] MapNotesFields = ["view", "nextId", "notes"];
    private static readonly string[] TownNoteFields = ["id", "flags", "x", "y", "type", "text"];
    private static readonly string[] QuestFields = ["quest", "state", "botched", "timestampMs"];
    private static readonly string[] GlobalQuestFields = ["quest", "state"];
    private static readonly string[] RumorFields = ["rumor", "timestampMs"];
    private static readonly string[] ReputationFields = ["reputation", "timestampMs"];
    private static readonly string[] PartyFields = ["followers", "others"];
    private static readonly string[] FollowerFields = ["entity", "forced", "spreadOut", "loyalty"];
    private static readonly string[] OtherFields = ["entity", "standing", "spreadOut", "jilted", "loyalty"];
    private static readonly string[] HeroFields = ["prototype", "race", "gender", "stats", "level", "experience", "unspentPoints",
        "alignment", "advancement", "raceEffect", "genderEffect", "innateSkills", "fatePoints", "armedFates", "background", "backgroundDescription", "playerName", "portrait", "scripts"];
    private static readonly string[] EffectFields = ["effect", "modifiers"];
    private static readonly string[] InnateSkillFields = ["cause", "skill", "operator", "value"];
    // Skill names in Skill order (ARC-SKILLS-002), the innate skill change's causes.
    private static readonly string[] SkillNames = ["Bow", "Dodge", "Melee", "Throwing", "Backstab", "PickPocket", "Prowling", "SpotTrap",
        "Gambling", "Haggle", "Heal", "Persuasion", "Repair", "Firearms", "PickLocks", "DisarmTraps"];
    private static readonly string[] InnateCauseNames = ["Race", "Gender"];
    private static readonly string[] ModifierFields = ["stat", "operator", "value"];
    // ARC-SCRIPT-042 (schema 43): the kept item script slots, and a worn item's item event points; ARC-WORLD-085 (schema 44): the weight rows;
    // ARC-WORLD-090 (schema 50): the identified items; ARC-WORLD-094
    // (schema 51): the highest discarded made number; ARC-UI-019 (schema 52): the hero's inventory grid; ARC-WORLD-100 (schema 55): the
    // generated-once containers; ARC-WORLD-103 (schema 57): the dressed NPCs' worn items. The junk piles (schema 46), the pools of blood (schema 58) and the number marks (schema 60)
    // were inventory fields until schema 61 moved them to the objects section (ARC-DEV-091).
    private static readonly string[] InventoryFields = ["gold", "kit", "pc", "made", "npcs", "placed", "ammo", "given", "piles", "scriptSlots", "weights",
        "identified", "lastDiscardedMade", "grid", "generated", "npcWorn"];
    private static readonly string[] ObjectsFields = ["junkPiles", "bloodPools", "lastJunkNumber", "lastPoolNumber", "state", "links", "created", "lastCreatedNumber"];
    private static readonly string[] CreatedObjectFields = ["entity", "kind", "prototype", "map", "x", "y", "offsetX", "offsetY", "createdMs", "spellFlags", "summoner", "run"];
    private static readonly string[] ObjectFieldFields = ["entity", "property", "map", "value"];
    private static readonly string[] ObjectLinkFields = ["entity", "property", "map", "targets"];
    private static readonly string[] BloodPoolFields = ["entity", "critter", "deathTimeMs", "map", "x", "y", "offsetX", "offsetY", "createdMs"];
    private static readonly string[] NpcWearFields = ["npc", "worn"];
    private static readonly string[] NpcWornItemFields = ["slot", "item"];
    private static readonly string[] ScriptSlotFields = ["entity", "point", "script", "localFlags", "counters"];
    private static readonly string[] WeightFields = ["entity", "weight", "magicWeight", "complexity"];
    private static readonly string[] JunkPileFields = ["entity", "map", "x", "y", "items"];
    private static readonly string[] JunkPileItemFields = ["entity", "slot", "width", "height"];
    private static readonly string[] AmmoFields = ["entity", "ammoType", "units"];
    private static readonly string[] GivenFields = ["entity", "origin"];
    private static readonly string[] PileFields = ["entity", "coins"];
    private static readonly string[] PlacedItemFields = ["entity", "source", "origin", "map", "x", "y", "type", "prototype", "name", "description", "complexity"];
    // ARC-WORLD-090 (schema 50): a container's list (a barter window's sales).
    private static readonly string[] NpcInventoryFields = ["npc", "container", "items"];
    private static readonly string[] KitItemFields = ["entity", "type", "prototype", "name", "description"];
    // ARC-WORLD-083 (schema 41): a made item's complexity.
    private static readonly string[] MadeItemFields = ["entity", "type", "prototype", "name", "description", "complexity"];
    private static readonly string[] PcItemFields = ["entity", "map", "type", "prototype", "name", "description"];
    private static readonly string[] WornFields = ["slot", "entity", "item", "scripts", "itemSpells"];
    private static readonly string[] DurabilityFields = ["items", "destroyed", "uses", "charges"];
    private static readonly string[] ItemUseFields = ["entity", "uses"];
    private static readonly string[] ItemChargeFields = ["entity", "charges"];
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
    private static readonly string[] EffectsFields = ["hero", "definitions", "blessings", "curses", "critters"];
    private static readonly string[] CritterEffectFields = ["entity", "entries", "definitions"];
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
    // GR19 (ARC-DEV-078, schema 59): the session's subturn starts are saved with the round.
    private static readonly string[] CombatFields = ["turn", "participants", "current", "actionPoints", "subturnStarts"];
    private static readonly string[] CombatModeFields = ["hero", "critters"];
    private static readonly string[] ConcealmentFields = ["hero", "npcs"];
    private static readonly string[] DangerFields = ["records"];
    private static readonly string[] DangerRecordFields = ["entity", "flags"];
    private static readonly string[] SoundFields = ["slots", "restorePending", "restore", "combatMusic", "preCombat"];
    private static readonly string[] PendingEventFields = ["sequence", "kind", "deadlineMs", "entities", "values"];
    private static readonly string[] EventEntityFields = ["map", "entity"];
    // ARC-UI-022 (schema 53): the hotkey bar.
    private static readonly string[] HotkeysFields = ["entries", "recent"];
    // ARC-DEV-098 (schema 67): the global variables and the PC's flag elements and variables.
    private static readonly string[] ScriptsFields = ["globalVariables"];
    // ARC-DEV-266 (schema 79): the PC's flag elements and variables on the hero's record (fields 272 and 273 of the PC, SA14).
    private static readonly string[] HeroScriptsFields = ["flags", "variables"];
    // ARC-DEV-106 (schema 68): the open map's sector eviction history.
    private static readonly string[] ResidencyFields = ["map", "history"];
    private static readonly string[] HotkeyEntryFields = ["kind", "item", "index"];

    public static string Encode(GameSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // ARC-DEV-266: the PC's arrays are saved on the hero's record, so a snapshot without a hero has nowhere to keep them (Restore refuses it too).
        if (snapshot.Hero is null && (snapshot.Scripts.PcFlags.Count > 0 || snapshot.Scripts.PcVariables.Count > 0))
            throw new ArgumentException("PC script cells need a hero to be saved on.", nameof(snapshot));
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
            w.WriteNumber("realMs", clock.RealMs);
            w.WriteNumber("animationMs", clock.AnimationMs);
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
            w.WriteNumber("storyState", story.StoryState);
            w.WriteStartArray("revealedTownMaps");
            foreach (var townMap in story.RevealedTownMaps) w.WriteNumberValue(townMap);
            w.WriteEndArray();
            // ARC-WORLD-122 (schema 75): the cells walked into, per town map the bitmap as lowercase hex.
            w.WriteStartArray("townMapCells");
            foreach (var cells in story.TownMapCells)
            {
                w.WriteStartObject();
                w.WriteNumber("townMap", cells.TownMap);
                w.WriteString("bits", Convert.ToHexString(cells.Bits.ToArray()).ToLowerInvariant());
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-UI-081 (schema 77): the PC's key numbers in the order added.
            w.WriteStartArray("heroKeys");
            foreach (var key in story.HeroKeys) w.WriteNumberValue(key);
            w.WriteEndArray();
            // ARC-UI-082 (schema 78): the PC's kill statistics (the total, six [description number, value] slots) and injuries ([description number, kind], oldest first).
            w.WriteStartObject("killLog");
            w.WriteNumber("total", story.KillLog.Total);
            w.WriteStartArray("records");
            foreach (var record in new[] { story.KillLog.HighestLevel, story.KillLog.LowestLevel, story.KillLog.MostPositiveAlignment, story.KillLog.MostNegativeAlignment,
                story.KillLog.HighestAptitude, story.KillLog.LowestAptitude })
            {
                w.WriteStartArray(); w.WriteNumberValue(record.NameId); w.WriteNumberValue(record.Value); w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteStartArray("injuries");
            foreach (var injury in story.KillLog.Injuries)
            {
                w.WriteStartArray(); w.WriteNumberValue(injury.NameId); w.WriteNumberValue(injury.Kind); w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            // ARC-UI-104 (schema 81): the notes of each town map that has any.
            w.WriteStartArray("townNotes");
            foreach (var town in story.TownNotes)
            {
                w.WriteStartObject();
                w.WriteNumber("townMap", town.TownMap);
                w.WriteNumber("nextId", town.NextId);
                w.WriteStartArray("notes");
                foreach (var note in town.Notes)
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", note.Id);
                    w.WriteNumber("flags", note.Flags);
                    w.WriteNumber("x", note.PixelX);
                    w.WriteNumber("y", note.PixelY);
                    w.WriteNumber("type", note.Type);
                    w.WriteString("text", note.Text);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-UI-110 (schema 82): the notes of each world-map view that has any.
            w.WriteStartArray("mapNotes");
            foreach (var view in story.MapNotes)
            {
                w.WriteStartObject();
                w.WriteNumber("view", view.View);
                w.WriteNumber("nextId", view.NextId);
                w.WriteStartArray("notes");
                foreach (var note in view.Notes)
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", note.Id);
                    w.WriteNumber("flags", note.Flags);
                    w.WriteNumber("x", note.PixelX);
                    w.WriteNumber("y", note.PixelY);
                    w.WriteNumber("type", note.Type);
                    w.WriteString("text", note.Text);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
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
                w.WriteNumber("backgroundDescription", hero.BackgroundDescription);
                w.WriteString("playerName", Convert.ToHexString(hero.PlayerName.AsSpan()).ToLowerInvariant());
                w.WriteNumber("portrait", hero.Portrait);
                // ARC-DEV-266 (schema 79): the PC's flag elements and variables, fields 272 and 273 of the PC, the non-zero ones as [index, value] pairs ascending.
                w.WriteStartObject("scripts");
                WriteScriptCells(w, "flags", snapshot.Scripts.PcFlags);
                WriteScriptCells(w, "variables", snapshot.Scripts.PcVariables);
                w.WriteEndObject();
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
                w.WriteBoolean("container", npc.Container);
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
                w.WriteNumber("complexity", item.Complexity);
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
            // ARC-WORLD-071: the items the hero gave NPCs (origin 0 for a kit item) and the tracked gold piles, by entity in ordinal order.
            w.WriteStartArray("given");
            foreach (var given in snapshot.Inventory.Given)
            {
                w.WriteStartObject();
                w.WriteString("entity", given.Entity);
                w.WriteNumber("origin", given.Origin?.RuntimeMap ?? 0);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("piles");
            foreach (var pile in snapshot.Inventory.Piles)
            {
                w.WriteStartObject();
                w.WriteString("entity", pile.Entity);
                w.WriteNumber("coins", pile.Quantity);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-SCRIPT-042: by entity in ordinal order, then by point.
            w.WriteStartArray("scriptSlots");
            foreach (var slot in snapshot.Inventory.ScriptSlots)
            {
                w.WriteStartObject();
                w.WriteString("entity", slot.Entity);
                w.WriteNumber("point", slot.Point);
                w.WriteNumber("script", slot.Script);
                w.WriteNumber("localFlags", slot.LocalFlags);
                w.WriteNumber("counters", slot.Counters);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-085: the weight rows by entity in ordinal order.
            w.WriteStartArray("weights");
            foreach (var row in snapshot.Inventory.Weights)
            {
                w.WriteStartObject();
                w.WriteString("entity", row.Entity);
                w.WriteNumber("weight", row.Facts.Weight);
                w.WriteNumber("magicWeight", row.Facts.MagicWeight);
                w.WriteNumber("complexity", row.Facts.Complexity);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-090: by entity in ordinal order.
            w.WriteStartArray("identified");
            foreach (var entity in snapshot.Inventory.Identified) w.WriteStringValue(entity);
            w.WriteEndArray();
            // ARC-WORLD-094.
            w.WriteNumber("lastDiscardedMade", snapshot.Inventory.LastDiscardedMade);
            // ARC-UI-019: the hero's grid in slot order.
            w.WriteStartArray("grid");
            foreach (var item in snapshot.Inventory.Grid)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteNumber("slot", item.Slot);
                w.WriteNumber("width", item.Width);
                w.WriteNumber("height", item.Height);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-100: the generated-once containers by map and then entity in ordinal order.
            w.WriteStartArray("generated");
            foreach (var row in snapshot.Inventory.Generated)
            {
                w.WriteStartObject();
                w.WriteNumber("map", row.Map.RuntimeMap);
                w.WriteString("entity", row.Entity);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-WORLD-103: the dressed NPCs by entity in ordinal order, each with its worn items in slot order.
            w.WriteStartArray("npcWorn");
            foreach (var npc in snapshot.Inventory.NpcWorn)
            {
                w.WriteStartObject();
                w.WriteString("npc", npc.Npc);
                w.WriteStartArray("worn");
                foreach (var worn in npc.Worn)
                {
                    w.WriteStartObject();
                    w.WriteNumber("slot", (int)worn.Slot);
                    w.WriteString("item", worn.Item);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            // ARC-DEV-091 (MIG6d-3, schema 61): the created objects the directory records: junk piles and pools of blood with their number marks.
            w.WriteStartObject("objects");
            w.WriteStartArray("junkPiles");
            foreach (var pile in snapshot.Objects.JunkPiles)
            {
                w.WriteStartObject();
                w.WriteString("entity", pile.Entity);
                w.WriteNumber("map", pile.Map.RuntimeMap);
                w.WriteNumber("x", pile.Cell.X);
                w.WriteNumber("y", pile.Cell.Y);
                w.WriteStartArray("items");
                foreach (var item in pile.Items)
                {
                    w.WriteStartObject();
                    w.WriteString("entity", item.Entity);
                    w.WriteNumber("slot", item.Slot);
                    w.WriteNumber("width", item.Width);
                    w.WriteNumber("height", item.Height);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-RENDER-041: the pools of blood in the order they were left.
            w.WriteStartArray("bloodPools");
            foreach (var pool in snapshot.Objects.BloodPools)
            {
                w.WriteStartObject();
                w.WriteString("entity", pool.Entity);
                w.WriteString("critter", pool.Critter);
                w.WriteNumber("deathTimeMs", pool.DeathTimeMs);
                w.WriteNumber("map", pool.Map.RuntimeMap);
                w.WriteNumber("x", pool.Cell.X);
                w.WriteNumber("y", pool.Cell.Y);
                w.WriteNumber("offsetX", pool.OffsetX);
                w.WriteNumber("offsetY", pool.OffsetY);
                w.WriteNumber("createdMs", pool.CreatedMs);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-DEV-084 (schema 60, section moved in schema 61): the high-water marks of the junk and pool numbers.
            w.WriteNumber("lastJunkNumber", snapshot.Objects.LastJunkNumber);
            w.WriteNumber("lastPoolNumber", snapshot.Objects.LastPoolNumber);
            // ARC-DEV-092 (MIG7a, schema 62), ARC-DEV-093 (MIG7b, schema 63): the recorded fields of mobile and static map-data objects, by entity, property and map.
            w.WriteStartArray("state");
            foreach (var field in snapshot.Objects.State)
            {
                w.WriteStartObject();
                w.WriteString("entity", field.Entity);
                w.WriteString("property", field.Property);
                w.WriteNumber("map", field.Map.RuntimeMap);
                w.WriteNumber("value", field.Value);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-DEV-095 (MIG7d, schema 65): the link fields of the NPCs' entries, by holder then field; a target is the entity id, "" for the hero (never a valid id).
            w.WriteStartArray("links");
            foreach (var link in snapshot.Objects.Links)
            {
                w.WriteStartObject();
                w.WriteString("entity", link.Entity);
                w.WriteString("property", link.Property);
                w.WriteNumber("map", link.Map.RuntimeMap);
                w.WriteStartArray("targets");
                foreach (var target in link.Targets) w.WriteStringValue(target.Entity ?? "");
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            // ARC-DEV-347 (E1-a, schema 85): the scenery and eye-candy objects the session created from prototypes, in the order they were created, and the one counter's mark.
            w.WriteStartArray("created");
            foreach (var made in snapshot.Objects.Created)
            {
                w.WriteStartObject();
                w.WriteString("entity", made.Entity);
                w.WriteNumber("kind", (int)made.Kind);
                w.WriteNumber("prototype", made.Prototype);
                w.WriteNumber("map", made.Map.RuntimeMap);
                w.WriteNumber("x", made.Cell.X);
                w.WriteNumber("y", made.Cell.Y);
                w.WriteNumber("offsetX", made.OffsetX);
                w.WriteNumber("offsetY", made.OffsetY);
                w.WriteNumber("createdMs", made.CreatedMs);
                // ARC-RES-101: the spell flags, the summoner ("" none: never a valid entity) and the owning spell run (-1 none).
                w.WriteNumber("spellFlags", made.SpellFlags);
                w.WriteString("summoner", made.Summoner ?? "");
                w.WriteNumber("run", made.Run ?? -1);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("lastCreatedNumber", snapshot.Objects.LastCreatedNumber);
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
            w.WriteStartArray("charges");
            foreach (var item in snapshot.Durability.Charges)
            {
                w.WriteStartObject();
                w.WriteString("entity", item.Entity);
                w.WriteNumber("charges", item.Charges);
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
            foreach (var definition in snapshot.Effects.Definitions) WriteEffectDefinition(w, definition);
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
            w.WriteStartArray("critters");
            foreach (var record in snapshot.Effects.Critters)
            {
                w.WriteStartObject();
                w.WriteString("entity", record.Entity);
                w.WriteStartArray("entries");
                foreach (var entry in record.Entries)
                {
                    w.WriteStartObject();
                    w.WriteNumber("effect", entry.Number);
                    w.WriteString("cause", entry.Cause.ToString());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("definitions");
                foreach (var definition in record.Definitions) WriteEffectDefinition(w, definition);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
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
                w.WriteNumber("subturnStarts", combat.SubturnStarts);
                w.WriteEndObject();
            }
            else w.WriteNull("combat");

            w.WriteStartObject("combatMode");
            w.WriteBoolean("hero", snapshot.CombatMode.Hero);
            w.WriteStartArray("critters");
            foreach (var entity in snapshot.CombatMode.Critters) w.WriteStringValue(entity);
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("concealment");
            w.WriteBoolean("hero", snapshot.Concealment.Hero);
            w.WriteStartArray("npcs");
            foreach (var entity in snapshot.Concealment.Npcs) w.WriteStringValue(entity);
            w.WriteEndArray();
            w.WriteEndObject();

            // ARC-WORLD-079: the danger bits of an NPC (schema 65: its links are the rows of objects.links, ARC-DEV-095).
            w.WriteStartObject("danger");
            w.WriteStartArray("records");
            foreach (var record in snapshot.Danger.Records)
            {
                w.WriteStartObject();
                w.WriteString("entity", record.Entity);
                w.WriteStartArray("flags");
                for (var i = 0; i < DangerFlagNames.Length; i++)
                    if (((int)record.Flags & (1 << i)) != 0) w.WriteStringValue(DangerFlagNames[i]);
                w.WriteEndArray();
                w.WriteEndObject();
            }
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

            // ARC-UI-022: ten entries and two recent actions, each its kind (0..4), its item or null, and its index (−1 for none).
            w.WriteStartObject("hotkeys");
            WriteHotkeys(w, "entries", snapshot.Hotkeys.Entries);
            WriteHotkeys(w, "recent", snapshot.Hotkeys.Recent);
            w.WriteEndObject();

            // ARC-DEV-098: the global variables, the non-zero ones as [index, value] pairs ascending (the PC's arrays are the hero's, above, ARC-DEV-266).
            w.WriteStartObject("scripts");
            WriteScriptCells(w, "globalVariables", snapshot.Scripts.GlobalVariables);
            w.WriteEndObject();

            // ARC-DEV-106: the map the sector eviction history belongs to (null with an empty history) and its records oldest first, each [sector id, game ms].
            w.WriteStartObject("residency");
            if (snapshot.Residency.Map is { } residencyMap) w.WriteNumber("map", residencyMap.RuntimeMap); else w.WriteNull("map");
            w.WriteStartArray("history");
            foreach (var record in snapshot.Residency.History)
            {
                w.WriteStartArray();
                w.WriteNumberValue(record.Sector);
                w.WriteNumberValue(record.EvictedAtMs);
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();

            WriteGoals(w, snapshot.Goals);
            WriteSpells(w, snapshot.Spells);
            WriteNewspaper(w, snapshot.Newspaper);
            WriteFlights(w, snapshot.Flights);

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

    private static void WriteScriptCells(Utf8JsonWriter w, string name, IReadOnlyList<SessionScriptCell> cells)
    {
        w.WriteStartArray(name);
        foreach (var cell in cells)
        {
            w.WriteStartArray();
            w.WriteNumberValue(cell.Index);
            w.WriteNumberValue(cell.Value);
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }

    private static void WriteHotkeys(Utf8JsonWriter w, string name, IReadOnlyList<Arcanum.Simulation.Hotkeys.HotkeyEntry> entries)
    {
        w.WriteStartArray(name);
        foreach (var entry in entries)
        {
            w.WriteStartObject();
            w.WriteNumber("kind", (int)entry.Kind);
            if (entry.Item is null) w.WriteNull("item"); else w.WriteString("item", entry.Item);
            w.WriteNumber("index", entry.Index);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // Shapes and ranges: the map is null exactly when the history is empty, the history holds at most twice the largest cache's slot count, each record a
    // sector id and a game time; whether the map is the hero's, the order and the time against the clock are Restore's.
    private static SessionResidencyState Residency(JsonElement element)
    {
        const string p = "$.residency";
        var f = Fields(element, p, ResidencyFields);
        var map = f["map"].ValueKind == JsonValueKind.Null ? (SessionMapId?)null : new SessionMapId(Int(f["map"], p + ".map", 1, int.MaxValue));
        var rows = Array(f["history"], p + ".history", 2 * Arcanum.Simulation.Residency.SectorCacheLimits.MaximumSlots);
        var history = new SessionSectorEviction[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{p}.history[{i}]";
            var pair = Array(rows[i], at, 2);
            if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.WrongType, at);
            history[i] = new(Long(pair[0], at + "[0]", 0, long.MaxValue), Long(pair[1], at + "[1]", 0, long.MaxValue));
        }
        if ((map is null) != (history.Length == 0)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, p + ".map");
        return new(map, history);
    }

    // Shapes, ranges and the one canonical order (ascending unique index, no zero value): the global variables of the scripts section and, since
    // schema 79 (ARC-DEV-266), the PC's arrays of the hero's section, so a snapshot without a hero has none.
    private static SessionScriptState Scripts(JsonElement element, JsonElement hero)
    {
        const string p = "$.scripts", h = "$.hero.scripts";
        var f = Fields(element, p, ScriptsFields);
        var global = ScriptCells(f["globalVariables"], p + ".globalVariables", SessionScriptLimits.GlobalVariables);
        if (hero.ValueKind == JsonValueKind.Null) return new(global, [], []);
        // The hero's own fields were checked by Hero(), which the root decode reads first, so this second pass over them cannot fail.
        var pc = Fields(Fields(hero, "$.hero", HeroFields)["scripts"], h, HeroScriptsFields);
        return new(global, ScriptCells(pc["flags"], h + ".flags", SessionScriptLimits.PcFlagElements),
            ScriptCells(pc["variables"], h + ".variables", SessionScriptLimits.PcIndexes));
    }

    private static SessionScriptCell[] ScriptCells(JsonElement element, string path, int count)
    {
        var rows = Array(element, path, count);
        var cells = new SessionScriptCell[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{path}[{i}]";
            var pair = Array(rows[i], at, 2);
            if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.WrongType, at);
            var index = Int(pair[0], at + "[0]", 0, count - 1);
            var value = Int(pair[1], at + "[1]", int.MinValue, int.MaxValue);
            if (value == 0 || i > 0 && index <= cells[i - 1].Index) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at);
            cells[i] = new(index, value);
        }
        return cells;
    }

    // Shapes: exactly ten entries and two recent actions, each fitting its kind; who holds the items and the kinds of the recent actions
    // are Restore's.
    private static SessionHotkeyState Hotkeys(JsonElement element)
    {
        const string p = "$.hotkeys";
        var f = Fields(element, p, HotkeysFields);
        return new(HotkeyEntries(f["entries"], p + ".entries", Arcanum.Simulation.Hotkeys.HotkeyRules.Entries),
            HotkeyEntries(f["recent"], p + ".recent", Arcanum.Simulation.Hotkeys.HotkeyRules.RecentActions));
    }

    private static Arcanum.Simulation.Hotkeys.HotkeyEntry[] HotkeyEntries(JsonElement element, string path, int count)
    {
        var rows = Array(element, path, count);
        if (rows.Length != count) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
        var entries = new Arcanum.Simulation.Hotkeys.HotkeyEntry[count];
        for (var i = 0; i < count; i++)
        {
            var at = $"{path}[{i}]";
            var e = Fields(rows[i], at, HotkeyEntryFields);
            var kind = (Arcanum.Simulation.Hotkeys.HotkeyKind)Int(e["kind"], at + ".kind", 0, (int)Arcanum.Simulation.Hotkeys.HotkeyKind.ItemSpell);
            var item = e["item"].ValueKind == JsonValueKind.Null ? null : Entity(e["item"], at + ".item");
            var entry = new Arcanum.Simulation.Hotkeys.HotkeyEntry(kind, item, Int(e["index"], at + ".index", -1, Arcanum.Simulation.Magic.ItemSpellPaneRules.NoSpell - 1));
            if (!entry.IsWellFormed) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at);
            entries[i] = entry.IsEmpty ? Arcanum.Simulation.Hotkeys.HotkeyEntry.Empty : entry;
        }
        return entries;
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
        int? older;
        using (document)
        {
            try
            {
                older = UpgradableVersion(document.RootElement);
                if (older is null) return new(Root(document.RootElement), SnapshotDecodeFailure.None, null);
            }
            catch (DecodeException e) { return new(null, e.Failure, e.Path); }
            // Valid JSON may escape a lone UTF-16 surrogate in a property name, which has no name to compare.
            catch (InvalidOperationException) { return new(null, SnapshotDecodeFailure.MalformedJson, "$"); }
        }
        // ARC-UI-037: an older schema this build reads is upgraded to the current one (its document disposed first), then decoded as
        // any current text.
        try { return TryDecode(Upgrade(text, older.Value)); }
        catch (DecodeException e) { return new(null, e.Failure, e.Path); }
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
            Hero(f["hero"]), Inventory(f["inventory"]), Equipment(f["equipment"]), Durability(f["durability"]), Schematics(f["schematics"]), Effects(f["effects"]), Experience(f["experience"]), Advancement(f["advancement"]), Random(f["random"]), Life(f["life"]), Reactions(f["reactions"]), Combat(f["combat"]), CombatMode(f["combatMode"]), Concealment(f["concealment"]), Danger(f["danger"]), Events(f["events"]), Lighting(f["lighting"]), Sound(f["sound"]),
            Hotkeys(f["hotkeys"]), Objects(f["objects"]), Scripts(f["scripts"], f["hero"]), Residency(f["residency"]), Goals(f["goals"]), Spells(f["spells"]), Newspaper(f["newspaper"]), Flights(f["flights"]));
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
            Int(f["actionPoints"], p + ".actionPoints", 0, TurnBasedRules.MaxActionPoints),
            Long(f["subturnStarts"], p + ".subturnStarts", 0, long.MaxValue));
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

    // Shapes and ordinal order; the hero's presence and the followers are Restore's.
    private static SessionConcealmentState Concealment(JsonElement element)
    {
        const string p = "$.concealment";
        var f = Fields(element, p, ConcealmentFields);
        var items = Array(f["npcs"], p + ".npcs", SessionConcealmentState.MaxNpcs);
        var npcs = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            npcs[i] = Entity(items[i], $"{p}.npcs[{i}]");
            if (i > 0 && string.CompareOrdinal(npcs[i - 1], npcs[i]) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, $"{p}.npcs[{i}]");
        }
        return new(Bool(f["hero"], p + ".hero"), npcs);
    }

    // Shapes, flag order and ordinal record order; the baseline and the budgets are Restore's (schema 65: the links are objects.links').
    private static SessionDangerState Danger(JsonElement element)
    {
        const string p = "$.danger";
        var f = Fields(element, p, DangerFields);
        var items = Array(f["records"], p + ".records", GameSessionLimits.PolicyMaxDangerRecords);
        var records = new SessionDanger[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.records[{i}]";
            var item = Fields(items[i], path, DangerRecordFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(records[i - 1].Entity, entity) >= 0)
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            var flagItems = Array(item["flags"], path + ".flags", DangerFlagNames.Length);
            var flags = DangerFlags.None;
            var last = -1;
            for (var j = 0; j < flagItems.Length; j++)
            {
                var at = $"{path}.flags[{j}]";
                var index = Name(flagItems[j], at, DangerFlagNames);
                if (index <= last) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at);
                last = index;
                flags |= (DangerFlags)(1 << index);
            }
            records[i] = new(entity, flags, null, null, null, []);
        }
        return new(records);
    }

    private static SessionCombatant Handle(JsonElement element, string path) =>
        String(element, path) is "" ? SessionCombatant.Hero : SessionCombatant.Critter(Entity(element, path));

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
        // MIG12 S3 (schema 70): the real clock starts at 1 ms (TL15), the animation clock at the start time, never below 0; a stamp within one
        // admission of the 64-bit limit would refuse every frame, so it is refused here.
        return new(profile, timestamp, new(paused, reason), Long(f["revision"], p + ".revision", 0, long.MaxValue),
            Long(f["realMs"], p + ".realMs", FrameClockKeeper.StartRealMs, FrameClockKeeper.MaxAdmittingStampMs),
            Long(f["animationMs"], p + ".animationMs", 0, FrameClockKeeper.MaxAdmittingStampMs));
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
        var storyState = Int(f["storyState"], p + ".storyState", 0, int.MaxValue);
        // ARC-DEV-205 (schema 71): the town maps revealed whole, ascending town-map numbers.
        items = Array(f["revealedTownMaps"], p + ".revealedTownMaps", SessionStoryLimits.LastTownMap);
        var townMaps = new int[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.revealedTownMaps[{i}]";
            townMaps[i] = Int(items[i], path, 1, SessionStoryLimits.LastTownMap);
            if (i > 0 && townMaps[i] <= townMaps[i - 1]) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        // ARC-WORLD-122 (schema 75): the cells walked into, ascending town maps, each a non-empty bitmap in lowercase hex whose last byte is not zero.
        items = Array(f["townMapCells"], p + ".townMapCells", SessionStoryLimits.MaxTownMapsWithCells);
        var townMapCells = new SessionTownMapCells[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.townMapCells[{i}]";
            var item = Fields(items[i], path, TownMapCellFields);
            var townMap = Int(item["townMap"], path + ".townMap", 1, SessionStoryLimits.LastTownMap);
            if (i > 0 && townMap <= townMapCells[i - 1].TownMap) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".townMap");
            var text = String(item["bits"], path + ".bits");
            if (text.Length == 0 || text.Length % 2 != 0 || text.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) || text.EndsWith("00", StringComparison.Ordinal))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".bits");
            if (text.Length / 2 > SessionStoryLimits.LastTownMapCell / 8 + 1) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".bits");
            townMapCells[i] = new(townMap, Convert.FromHexString(text));
        }
        // ARC-UI-081 (schema 77): the PC's key numbers in the order added (repeats and any order are kept).
        items = Array(f["heroKeys"], p + ".heroKeys", SessionStoryLimits.MaxKeys);
        var heroKeys = new int[items.Length];
        for (var i = 0; i < items.Length; i++) heroKeys[i] = Int(items[i], $"{p}.heroKeys[{i}]", 0, int.MaxValue);
        // ARC-UI-082 (schema 78): the PC's kill statistics: the total, six slots of a description number and a value, and the injuries, each a description
        // number and a kind 0..3 (a description number is never negative).
        var logPath = p + ".killLog";
        var logFields = Fields(f["killLog"], logPath, KillLogFields);
        var killTotal = Int(logFields["total"], logPath + ".total", 0, int.MaxValue);
        (int, int) Pair(JsonElement element, string path, int maxSecond, int minSecond = 0, int minFirst = 0)
        {
            var pair = Array(element, path, 2);
            if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path);
            return (Int(pair[0], path + "[0]", minFirst, int.MaxValue), Int(pair[1], path + "[1]", minSecond, maxSecond));
        }
        var recordItems = Array(logFields["records"], logPath + ".records", 6);
        if (recordItems.Length != 6) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, logPath + ".records");
        var records = new SessionKillRecord[6];
        for (var i = 0; i < 6; i++)
        {
            var (name, value) = Pair(recordItems[i], $"{logPath}.records[{i}]", int.MaxValue, int.MinValue);
            records[i] = new(name, value);
        }
        var injuryItems = Array(logFields["injuries"], logPath + ".injuries", SessionStoryLimits.MaxInjuries);
        var injuries = new SessionInjury[injuryItems.Length];
        for (var i = 0; i < injuries.Length; i++)
        {
            var (name, kind) = Pair(injuryItems[i], $"{logPath}.injuries[{i}]", SessionStoryLimits.InjuryKinds - 1, 0, 1);
            injuries[i] = new(name, kind);
        }
        var killLog = new SessionKillLog(killTotal, records[0], records[1], records[2], records[3], records[4], records[5], injuries);
        // ARC-UI-104 (schema 81): the notes of each town map, ascending town maps, each with 1..199 notes whose ids ascend from 200 below the next id.
        items = Array(f["townNotes"], p + ".townNotes", SessionStoryLimits.MaxTownMapsWithCells);
        var townNotes = new SessionTownNotes[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.townNotes[{i}]";
            var item = Fields(items[i], path, TownNotesFields);
            var townMap = Int(item["townMap"], path + ".townMap", 1, SessionStoryLimits.LastTownMap);
            if (i > 0 && townMap <= townNotes[i - 1].TownMap) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".townMap");
            var nextId = Int(item["nextId"], path + ".nextId", SessionStoryLimits.FirstTownNoteId + 1, int.MaxValue);
            var noteItems = Array(item["notes"], path + ".notes", SessionStoryLimits.MaxTownNotes);
            if (noteItems.Length == 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".notes");
            var noteList = new SessionTownNote[noteItems.Length];
            for (var n = 0; n < noteItems.Length; n++)
            {
                var notePath = $"{path}.notes[{n}]";
                var note = Fields(noteItems[n], notePath, TownNoteFields);
                var text = String(note["text"], notePath + ".text");
                if (!SessionStoryLimits.IsTownNoteText(text)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, notePath + ".text");
                noteList[n] = new(Int(note["id"], notePath + ".id", SessionStoryLimits.FirstTownNoteId, int.MaxValue - 1),
                    Int(note["flags"], notePath + ".flags", 0, SessionStoryLimits.MaxTownNoteFlags),
                    Int(note["x"], notePath + ".x", 0, SessionStoryLimits.MaxTownNotePixel), Int(note["y"], notePath + ".y", 0, SessionStoryLimits.MaxTownNotePixel),
                    Int(note["type"], notePath + ".type", 0, SessionStoryLimits.LastTownNoteType), text);
                if (n > 0 && noteList[n].Id <= noteList[n - 1].Id || noteList[n].Id >= nextId) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, notePath + ".id");
            }
            townNotes[i] = new(townMap, nextId, noteList);
        }
        // ARC-UI-110 (schema 82): the notes of each world-map view, ascending views, each with 1..199 notes whose ids ascend from 200 below the next id.
        items = Array(f["mapNotes"], p + ".mapNotes", SessionStoryLimits.LastMapNoteView);
        var mapNotes = new SessionMapNotes[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}.mapNotes[{i}]";
            var item = Fields(items[i], path, MapNotesFields);
            var view = Int(item["view"], path + ".view", SessionStoryLimits.MapNoteView, SessionStoryLimits.LastMapNoteView);
            if (i > 0 && view <= mapNotes[i - 1].View) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".view");
            var nextId = Int(item["nextId"], path + ".nextId", SessionStoryLimits.FirstTownNoteId + 1, int.MaxValue);
            var noteItems = Array(item["notes"], path + ".notes", SessionStoryLimits.MaxTownNotes);
            if (noteItems.Length == 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".notes");
            var noteList = new SessionTownNote[noteItems.Length];
            for (var n = 0; n < noteItems.Length; n++)
            {
                var notePath = $"{path}.notes[{n}]";
                var note = Fields(noteItems[n], notePath, TownNoteFields);
                var text = String(note["text"], notePath + ".text");
                if (!SessionStoryLimits.IsTownNoteText(text)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, notePath + ".text");
                noteList[n] = new(Int(note["id"], notePath + ".id", SessionStoryLimits.FirstTownNoteId, int.MaxValue - 1),
                    Int(note["flags"], notePath + ".flags", 0, SessionStoryLimits.MaxTownNoteFlags),
                    Int(note["x"], notePath + ".x", 0, SessionStoryLimits.MaxMapNotePixel), Int(note["y"], notePath + ".y", 0, SessionStoryLimits.MaxMapNotePixel),
                    Int(note["type"], notePath + ".type", 0, SessionStoryLimits.LastTownNoteType), text);
                if (n > 0 && noteList[n].Id <= noteList[n - 1].Id || noteList[n].Id >= nextId) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, notePath + ".id");
            }
            mapNotes[i] = new(view, nextId, noteList);
        }
        return new(quests, globals, rumors, quelled, areas, last, reputations, storyState, townMaps, townMapCells, heroKeys, killLog, townNotes, mapNotes);
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
                Int(f["armedFates"], p + ".armedFates", int.MinValue, int.MaxValue)), Int(f["background"], p + ".background", int.MinValue, int.MaxValue),
            Int(f["backgroundDescription"], p + ".backgroundDescription", int.MinValue, int.MaxValue), PlayerName(f["playerName"], p + ".playerName"),
            Int(f["portrait"], p + ".portrait", int.MinValue, int.MaxValue));
    }

    // ARC-UI-033: the player name's bytes as lowercase hex (canonical); the length and the NUL byte are Restore's (InvalidHero).
    private static ImmutableArray<byte> PlayerName(JsonElement element, string path)
    {
        var text = String(element, path);
        if (text.Length % 2 != 0 || text.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        return [.. Convert.FromHexString(text)];
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
            var item = Fields(madeItems[i], path, MadeItemFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            made[i] = new(entity, Kind(item, path), Complexity(item, path));
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
                var item = Fields(rows[j], itemPath, MadeItemFields);
                var entity = Entity(item["entity"], itemPath + ".entity");
                if (!seen.Add(entity)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, itemPath + ".entity");
                list[j] = new(entity, Kind(item, itemPath), Complexity(item, itemPath));
            }
            npcs[i] = new(npc, System.Array.AsReadOnly(list)) { Container = Bool(row["container"], path + ".container") };
        }
        // ARC-WORLD-059: the hero's items placed on the ground share the hero's budget and its once-only rule; ARC-WORLD-081: the items
        // scripts created there have a budget of their own.
        var placedRows = Array(f["placed"], p + ".placed", GameSessionLimits.PolicyMaxPcItems + GameSessionLimits.PolicyMaxCreatedItems);
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
            // ARC-WORLD-083: only a made item keeps a complexity of its own (ARC-WORLD-081: and a created one).
            var complexity = Complexity(item, path);
            if (complexity != 0 && source is not (SessionPlacedSource.Made or SessionPlacedSource.Created))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".complexity");
            placed[i] = new(entity, Kind(item, path), source, origin == 0 ? null : new SessionMapId(origin),
                new(Int(item["map"], path + ".map", 1, int.MaxValue)),
                new(Int(item["x"], path + ".x", 0, MaxPlacedCoordinate), Int(item["y"], path + ".y", 0, MaxPlacedCoordinate)), complexity);
        }
        var created = placed.Count(item => item.Source == SessionPlacedSource.Created);
        if (kit.Length + items.Length + madeItems.Length + placed.Length - created > GameSessionLimits.PolicyMaxPcItems || created > GameSessionLimits.PolicyMaxCreatedItems)
            throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, p + ".placed");
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
        // ARC-WORLD-071: the given items and the gold piles, each by entity in strictly ascending ordinal order.
        var givenRows = Array(f["given"], p + ".given", GameSessionLimits.PolicyMaxNpcItems);
        var given = new SessionGivenItem[givenRows.Length];
        string? lastGiven = null;
        for (var i = 0; i < givenRows.Length; i++)
        {
            var path = $"{p}.given[{i}]";
            var row = Fields(givenRows[i], path, GivenFields);
            var entity = Entity(row["entity"], path + ".entity");
            if (lastGiven is not null && string.CompareOrdinal(lastGiven, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            lastGiven = entity;
            var origin = Int(row["origin"], path + ".origin", 0, int.MaxValue);
            given[i] = new(entity, origin == 0 ? null : new SessionMapId(origin));
        }
        var pileRows = Array(f["piles"], p + ".piles", GameSessionLimits.PolicyMaxGoldRecords);
        var piles = new SessionGoldRecord[pileRows.Length];
        string? lastPile = null;
        for (var i = 0; i < pileRows.Length; i++)
        {
            var path = $"{p}.piles[{i}]";
            var row = Fields(pileRows[i], path, PileFields);
            var entity = Entity(row["entity"], path + ".entity");
            if (lastPile is not null && string.CompareOrdinal(lastPile, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            lastPile = entity;
            piles[i] = new(entity, Int(row["coins"], path + ".coins", 0, int.MaxValue));
        }
        // ARC-SCRIPT-042: the kept item script slots by entity in ordinal order, then by strictly ascending point.
        var slotRows = Array(f["scriptSlots"], p + ".scriptSlots", SessionItemScripts.MaxStoredSlots);
        var scriptSlots = new SessionItemScriptSlot[slotRows.Length];
        for (var i = 0; i < slotRows.Length; i++)
        {
            var path = $"{p}.scriptSlots[{i}]";
            var row = Fields(slotRows[i], path, ScriptSlotFields);
            var entity = Entity(row["entity"], path + ".entity");
            var point = Int(row["point"], path + ".point", 0, Arcanum.Scripting.Scr.ScrPoint.Count - 1);
            if (i > 0 && (string.CompareOrdinal(scriptSlots[i - 1].Entity, entity) is var order && (order > 0 || order == 0 && scriptSlots[i - 1].Point >= point)))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".point");
            scriptSlots[i] = new(entity, point, Int(row["script"], path + ".script", 0, Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber),
                UInt(row["localFlags"], path + ".localFlags"), UInt(row["counters"], path + ".counters"));
        }
        // ARC-WORLD-085: the weight rows by entity in strictly ascending ordinal order, at most one an object the inventory can hold.
        var weightRows = Array(f["weights"], p + ".weights", GameSessionLimits.PolicyMaxPcItems + GameSessionLimits.PolicyMaxNpcItems);
        var weights = new SessionItemWeightRecord[weightRows.Length];
        string? lastWeight = null;
        for (var i = 0; i < weightRows.Length; i++)
        {
            var path = $"{p}.weights[{i}]";
            var row = Fields(weightRows[i], path, WeightFields);
            var entity = Entity(row["entity"], path + ".entity");
            if (lastWeight is not null && string.CompareOrdinal(lastWeight, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            lastWeight = entity;
            weights[i] = new(entity, new(Int(row["weight"], path + ".weight", -Arcanum.Simulation.Items.CarryLimits.MaxWeight, Arcanum.Simulation.Items.CarryLimits.MaxWeight),
                Int(row["magicWeight"], path + ".magicWeight", -Arcanum.Simulation.Items.CarryLimits.MaxWeight, Arcanum.Simulation.Items.CarryLimits.MaxWeight),
                Complexity(row, path)));
        }
        // ARC-WORLD-090: the identified items by entity in strictly ascending ordinal order.
        var identifiedRows = Array(f["identified"], p + ".identified", SessionInventoryState.MaxIdentified);
        var identified = new string[identifiedRows.Length];
        for (var i = 0; i < identifiedRows.Length; i++)
        {
            var path = $"{p}.identified[{i}]";
            identified[i] = Entity(identifiedRows[i], path);
            if (i > 0 && string.CompareOrdinal(identified[i], identified[i - 1]) <= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        // ARC-UI-019: the hero's grid in strictly ascending slot order; who holds each item, bounds and overlaps are checked on restore.
        var gridRows = Array(f["grid"], p + ".grid", GameSessionLimits.PolicyMaxPcItems);
        var grid = new SessionHeroGridItem[gridRows.Length];
        for (var i = 0; i < gridRows.Length; i++)
        {
            var path = $"{p}.grid[{i}]";
            var item = Fields(gridRows[i], path, JunkPileItemFields);
            grid[i] = new(Entity(item["entity"], path + ".entity"), Int(item["slot"], path + ".slot", 0, MaxHeroGridSlot),
                Int(item["width"], path + ".width", 1, Arcanum.Simulation.Items.InventoryGridRules.Columns),
                Int(item["height"], path + ".height", 1, Arcanum.Simulation.Items.InventoryGridRules.CritterRows));
            if (i > 0 && grid[i].Slot <= grid[i - 1].Slot) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".slot");
        }
        // ARC-WORLD-100: the generated-once containers by map and then entity in strictly ascending ordinal order.
        var generatedRows = Array(f["generated"], p + ".generated", SessionInventoryState.MaxGenerated);
        var generated = new SessionGeneratedContainer[generatedRows.Length];
        for (var i = 0; i < generatedRows.Length; i++)
        {
            var path = $"{p}.generated[{i}]";
            var row = Fields(generatedRows[i], path, EventEntityFields);
            generated[i] = new(new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)), Entity(row["entity"], path + ".entity"));
            if (i > 0 && (generated[i - 1].Map.RuntimeMap > generated[i].Map.RuntimeMap || generated[i - 1].Map == generated[i].Map &&
                string.CompareOrdinal(generated[i - 1].Entity, generated[i].Entity) >= 0)) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        // ARC-WORLD-103: the dressed NPCs by entity in strictly ascending ordinal order, each with its worn items in strictly ascending slots.
        var npcWornRows = Array(f["npcWorn"], p + ".npcWorn", SessionInventoryState.MaxNpcWorn);
        var npcWorn = new SessionNpcWear[npcWornRows.Length];
        for (var i = 0; i < npcWornRows.Length; i++)
        {
            var path = $"{p}.npcWorn[{i}]";
            var row = Fields(npcWornRows[i], path, NpcWearFields);
            var npc = Entity(row["npc"], path + ".npc");
            var wornRows = Array(row["worn"], path + ".worn", Enum.GetValues<Arcanum.Simulation.Equipment.EquipmentSlot>().Length);
            var worn = new SessionNpcWornItem[wornRows.Length];
            for (var j = 0; j < wornRows.Length; j++)
            {
                var at = $"{path}.worn[{j}]";
                var item = Fields(wornRows[j], at, NpcWornItemFields);
                worn[j] = new((Arcanum.Simulation.Equipment.EquipmentSlot)Int(item["slot"], at + ".slot",
                    (int)Arcanum.Simulation.Equipment.EquipmentSlot.Helmet, (int)Arcanum.Simulation.Equipment.EquipmentSlot.Boots), Entity(item["item"], at + ".item"));
                if (j > 0 && worn[j - 1].Slot >= worn[j].Slot) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".slot");
            }
            npcWorn[i] = new(npc, System.Array.AsReadOnly(worn));
            if (i > 0 && string.CompareOrdinal(npcWorn[i - 1].Npc, npc) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".npc");
        }
        return new(gold, kit, pc, made, npcs, placed, ammo, given, piles, scriptSlots, weights, identified,
            Int(f["lastDiscardedMade"], p + ".lastDiscardedMade", 0, int.MaxValue), grid, generated, npcWorn);
    }

    // ARC-DEV-091 (MIG6d-3, schema 61): the created objects the directory records, in the order they were made; the owner's restore checks the rest.
    private static SessionObjectRecords Objects(JsonElement element)
    {
        const string p = "$.objects";
        var f = Fields(element, p, ObjectsFields);
        // ARC-WORLD-063: the junk piles, in the order they were made; the grid's bounds and overlaps are checked on restore.
        var junkRows = Array(f["junkPiles"], p + ".junkPiles", SessionJunkPile.MaxPiles);
        var junkPiles = new SessionJunkPile[junkRows.Length];
        var junkItems = 0;
        for (var i = 0; i < junkRows.Length; i++)
        {
            var path = $"{p}.junkPiles[{i}]";
            var row = Fields(junkRows[i], path, JunkPileFields);
            var itemRows = Array(row["items"], path + ".items", SessionJunkPile.MaxItems);
            if ((junkItems += itemRows.Length) > SessionJunkPile.MaxItems) throw new DecodeException(SnapshotDecodeFailure.BudgetExceeded, path + ".items");
            var pileItems = new SessionJunkPileItem[itemRows.Length];
            for (var j = 0; j < itemRows.Length; j++)
            {
                var itemPath = $"{path}.items[{j}]";
                var item = Fields(itemRows[j], itemPath, JunkPileItemFields);
                pileItems[j] = new(Entity(item["entity"], itemPath + ".entity"), Int(item["slot"], itemPath + ".slot", 0, MaxJunkSlot),
                    Int(item["width"], itemPath + ".width", 1, Arcanum.Simulation.Items.InventoryGridRules.Columns),
                    Int(item["height"], itemPath + ".height", 1, Arcanum.Simulation.Items.InventoryGridRules.ContainerRows));
            }
            junkPiles[i] = new(Entity(row["entity"], path + ".entity"), new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)),
                new(Int(row["x"], path + ".x", 0, MaxPlacedCoordinate), Int(row["y"], path + ".y", 0, MaxPlacedCoordinate)), System.Array.AsReadOnly(pileItems));
        }
        // ARC-RENDER-041: the pools of blood in the order they were left (the owner's restore checks the rest).
        var poolRows = Array(f["bloodPools"], p + ".bloodPools", SessionBloodPool.MaxPools);
        var bloodPools = new SessionBloodPool[poolRows.Length];
        for (var i = 0; i < poolRows.Length; i++)
        {
            var path = $"{p}.bloodPools[{i}]";
            var row = Fields(poolRows[i], path, BloodPoolFields);
            bloodPools[i] = new(Entity(row["entity"], path + ".entity"), Entity(row["critter"], path + ".critter"),
                Long(row["deathTimeMs"], path + ".deathTimeMs", 0, long.MaxValue), new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)),
                new(Int(row["x"], path + ".x", 0, MaxPlacedCoordinate), Int(row["y"], path + ".y", 0, MaxPlacedCoordinate)),
                Int(row["offsetX"], path + ".offsetX", int.MinValue, int.MaxValue), Int(row["offsetY"], path + ".offsetY", int.MinValue, int.MaxValue),
                Long(row["createdMs"], path + ".createdMs", 0, long.MaxValue));
        }
        // ARC-DEV-092 (MIG7a, schema 62), ARC-DEV-093 (MIG7b, schema 63): the object fields in canonical order (entity, then property, both ordinal, then map);
        // the owner's restore checks the keys and the budgets.
        var stateRows = Array(f["state"], p + ".state", GameSessionLimits.PolicyMaxTotalChanges);
        var state = new SessionObjectField[stateRows.Length];
        for (var i = 0; i < stateRows.Length; i++)
        {
            var path = $"{p}.state[{i}]";
            var row = Fields(stateRows[i], path, ObjectFieldFields);
            state[i] = new(String(row["entity"], path + ".entity"), String(row["property"], path + ".property"),
                new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)), Long(row["value"], path + ".value", long.MinValue, long.MaxValue));
            if (!SessionValidation.Code(state[i].Entity, SessionValidation.MaxEntityCharacters)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".entity");
            if (!SessionValidation.Code(state[i].Property, SessionValidation.MaxPropertyCharacters)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".property");
            // By entity, then property, then map (a static object's records on different maps share both; a mobile object has one record, and a
            // second one is the owner's restore refusal).
            if (i > 0 && (string.CompareOrdinal(state[i - 1].Entity, state[i].Entity) is var order && (order > 0 ||
                    order == 0 && string.CompareOrdinal(state[i - 1].Property, state[i].Property) is var byProperty && (byProperty > 0 ||
                    byProperty == 0 && state[i - 1].Map.RuntimeMap >= state[i].Map.RuntimeMap))))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        // ARC-DEV-095 (MIG7d, schema 65): the link fields in canonical order (holder, then field, both ordinal); the owner's restore checks the vocabulary,
        // the targets and the budgets.
        var linkRows = Array(f["links"], p + ".links", GameSessionLimits.PolicyMaxDangerRecords * 4);
        var links = new SessionObjectLink[linkRows.Length];
        for (var i = 0; i < linkRows.Length; i++)
        {
            var path = $"{p}.links[{i}]";
            var row = Fields(linkRows[i], path, ObjectLinkFields);
            var targetRows = Array(row["targets"], path + ".targets", GameSessionLimits.PolicyMaxDangerEnemies);
            var targets = new SessionCombatant[targetRows.Length];
            for (var j = 0; j < targetRows.Length; j++) targets[j] = Handle(targetRows[j], $"{path}.targets[{j}]");
            links[i] = new(Entity(row["entity"], path + ".entity"), String(row["property"], path + ".property"),
                new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)), System.Array.AsReadOnly(targets));
            if (!SessionValidation.Code(links[i].Property, SessionValidation.MaxPropertyCharacters)) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, path + ".property");
            if (i > 0 && (string.CompareOrdinal(links[i - 1].Entity, links[i].Entity) is var byHolder && (byHolder > 0 ||
                    byHolder == 0 && string.CompareOrdinal(links[i - 1].Property, links[i].Property) >= 0)))
                throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        }
        // ARC-DEV-347 (schema 85): the created scenery and eye-candy objects in the order they were created (the owner's restore checks the entities, the bounds and the mark).
        var createdRows = Array(f["created"], p + ".created", SessionCreatedObject.MaxObjects);
        var created = new SessionCreatedObject[createdRows.Length];
        for (var i = 0; i < createdRows.Length; i++)
        {
            var path = $"{p}.created[{i}]";
            var row = Fields(createdRows[i], path, CreatedObjectFields);
            created[i] = new(Entity(row["entity"], path + ".entity"), (SessionCreatedKind)Int(row["kind"], path + ".kind", 0, (int)SessionCreatedKind.Critter),
                Int(row["prototype"], path + ".prototype", 1, SessionCreatedObject.MaxPrototype), new SessionMapId(Int(row["map"], path + ".map", 1, int.MaxValue)),
                new(Int(row["x"], path + ".x", 0, MaxPlacedCoordinate), Int(row["y"], path + ".y", 0, MaxPlacedCoordinate)),
                Int(row["offsetX"], path + ".offsetX", -SessionCreatedObject.MaxOffset, SessionCreatedObject.MaxOffset),
                Int(row["offsetY"], path + ".offsetY", -SessionCreatedObject.MaxOffset, SessionCreatedObject.MaxOffset),
                Long(row["createdMs"], path + ".createdMs", 0, long.MaxValue), UInt(row["spellFlags"], path + ".spellFlags"),
                row["summoner"].ValueKind == JsonValueKind.String && row["summoner"].GetString() == "" ? null : Entity(row["summoner"], path + ".summoner"),
                Int(row["run"], path + ".run", -1, int.MaxValue) is var run && run < 0 ? null : run);
        }
        return new(junkPiles, bloodPools, Int(f["lastJunkNumber"], p + ".lastJunkNumber", 0, int.MaxValue), Int(f["lastPoolNumber"], p + ".lastPoolNumber", 0, int.MaxValue), state, links,
            created, Int(f["lastCreatedNumber"], p + ".lastCreatedNumber", 0, int.MaxValue));
    }

    // A junk pile item's slot lies in the container grid (ARC-WORLD-063).
    private const int MaxJunkSlot = Arcanum.Simulation.Items.InventoryGridRules.ContainerRows * Arcanum.Simulation.Items.InventoryGridRules.Columns - 1;

    // A hero grid item's slot lies in the critter grid (ARC-UI-019).
    private const int MaxHeroGridSlot = Arcanum.Simulation.Items.InventoryGridRules.CritterRows * Arcanum.Simulation.Items.InventoryGridRules.Columns - 1;

    // ARC-WORLD-083: a made or placed item's magick/tech complexity, any 32-bit value (the original's field 0x60 is one).
    private static int Complexity(Dictionary<string, JsonElement> item, string path) => Int(item["complexity"], path + ".complexity", int.MinValue, int.MaxValue);

    // A placed item's cell coordinates fit 24 bits (the drop's packing, ARC-COMBAT-009).
    private const int MaxPlacedCoordinate = (1 << 24) - 1;

    private static string PlacedSourceName(SessionPlacedSource source) => source switch
    {
        SessionPlacedSource.Kit => "kit",
        SessionPlacedSource.Pc => "pc",
        SessionPlacedSource.Made => "made",
        SessionPlacedSource.Created => "created",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static SessionPlacedSource PlacedSource(JsonElement element, string path) => String(element, path) switch
    {
        "kit" => SessionPlacedSource.Kit,
        "pc" => SessionPlacedSource.Pc,
        "made" => SessionPlacedSource.Made,
        "created" => SessionPlacedSource.Created,
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
        w.WriteNumber("complexity", item.Complexity);
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
        // ARC-SCRIPT-042: the item event points, ascending.
        w.WriteStartArray("scripts");
        foreach (var point in worn.ScriptPoints) w.WriteNumberValue(point);
        w.WriteEndArray();
        // ARC-MAGIC-022: whether its wear spells ran through a trigger host.
        w.WriteBoolean("itemSpells", worn.ItemSpells);
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
            // ARC-SCRIPT-042: the item event points (21, 24, 25, 28) strictly ascending.
            var points = Array(f["scripts"], path + ".scripts", EquipmentDomain.MoveScripts.Length);
            var scripts = new int[points.Length];
            for (var j = 0; j < points.Length; j++)
            {
                scripts[j] = Int(points[j], $"{path}.scripts[{j}]", SessionScriptPoint.InsertItem, SessionScriptPoint.RemoveItem);
                if (!EquipmentDomain.MoveScripts.Contains(scripts[j]) || j > 0 && scripts[j] <= scripts[j - 1])
                    throw new DecodeException(SnapshotDecodeFailure.NotCanonical, $"{path}.scripts[{j}]");
            }
            worn[i] = new(slot, entity, EquipmentItemValue(f["item"], path + ".item"))
                { ScriptPoints = EquipmentDomain.MovePoints(scripts), ItemSpells = Bool(f["itemSpells"], path + ".itemSpells") };
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
        var spent = Array(f["charges"], p + ".charges", GameSessionLimits.PolicyMaxDurabilityRecords);
        var charges = new SessionItemCharges[spent.Length];
        for (var i = 0; i < spent.Length; i++)
        {
            var path = $"{p}.charges[{i}]";
            var item = Fields(spent[i], path, ItemChargeFields);
            var entity = Entity(item["entity"], path + ".entity");
            if (i > 0 && string.CompareOrdinal(entity, charges[i - 1].Entity) <= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path + ".entity");
            charges[i] = new(entity, Int(item["charges"], path + ".charges", 0, SessionItemUseLimits.MaxUses));
        }
        return new(items, destroyed, uses, charges);
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
        var definitions = EffectDefinitions(f["definitions"], p + ".definitions", SessionEffectLimits.MaxHeroEffects);
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
        var rows = Array(f["critters"], p + ".critters", SessionNpcEffectLimits.MaxCritters);
        var critters = new SessionCritterEffectRecord[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{p}.critters[{i}]";
            var c = Fields(rows[i], at, CritterEffectFields);
            var entity = Entity(c["entity"], at + ".entity");
            if (i > 0 && string.CompareOrdinal(critters[i - 1].Entity, entity) >= 0) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".entity");
            var listed = Array(c["entries"], at + ".entries", SessionNpcEffectLimits.MaxEntries);
            var kept = new SessionEffect[listed.Length];
            for (var j = 0; j < listed.Length; j++)
            {
                var path = $"{at}.entries[{j}]";
                var e = Fields(listed[j], path, HeroEffectFields);
                kept[j] = new(Int(e["effect"], path + ".effect", SessionEffectLimits.FirstNumber, SessionEffectLimits.LastNumber),
                    Named<Arcanum.Simulation.Character.EffectCause>(e["cause"], path + ".cause"));
            }
            critters[i] = new(entity, kept, EffectDefinitions(c["definitions"], at + ".definitions", SessionNpcEffectLimits.MaxEntries));
        }
        return new(hero, definitions, Blessings("blessings"), Blessings("curses"), critters);
    }

    private static void WriteEffectDefinition(Utf8JsonWriter w, SessionEffectDefinition definition)
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

    // A list of effect rules by strictly ascending number (the hero's and each NPC's).
    private static SessionEffectDefinition[] EffectDefinitions(JsonElement element, string p, int max)
    {
        var items = Array(element, p, max);
        var definitions = new SessionEffectDefinition[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var path = $"{p}[{i}]";
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
        return definitions;
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

    private static uint UInt(JsonElement element, string path) => (uint)Long(element, path, 0, uint.MaxValue);

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
    /// <summary>Wire names of <see cref="DangerFlags"/> by bit, in flag order (ARC-WORLD-079; SpellFlee ARC-WORLD-082; CheckWeapon
    /// ARC-WORLD-084; NoFlee ARC-WORLD-086; the look-for bits ARC-WORLD-088).</summary>
    private static readonly string[] DangerFlagNames =
        ["Fighting", "CheckWield", "CheckGrenade", "BackingOff", "DemaintainSpells", "NoAttack", "Fleeing", "Surrendered", "TargetLocked",
            "SpellFlee", "CheckWeapon", "NoFlee", "LookForAmmo", "LookForWeapon", "LookForArmor"];
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
