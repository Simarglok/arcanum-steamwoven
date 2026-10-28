using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reputation;

namespace Arcanum.Session;

/// <summary>Immutable process-local reference identity, distinct for each created or restored owner.</summary>
public sealed class GameSessionOwnerIdentity
{
    internal GameSessionOwnerIdentity() { }
}

/// <summary>Freshness guard for actor, map and story mutations. Clock and flags keep their own tokens.</summary>
public readonly record struct GameSessionVersion(GameSessionOwnerIdentity? Owner, long Revision);

/// <summary>Runtime map number (the Area <c>RuntimeMap</c>); positive.</summary>
public readonly record struct SessionMapId(int RuntimeMap);

/// <summary>Where the actor stands at a settled boundary. In-flight movement is host state, not session state.</summary>
public readonly record struct ActorPlacement(SessionMapId Map, CellCoord Cell, NeighborDirection Facing);

public enum MapChangeScope { Sector = 1, Entity = 2 }

/// <summary>
/// One project-defined map-local value. A sector key carries the packed sector id and no entity;
/// an entity key carries a stable entity id and sector 0. Property names are project codes, not
/// original sector-diff fields.
/// </summary>
public readonly record struct MapChangeKey(MapChangeScope Scope, long Sector, string? Entity, string Property)
{
    public static MapChangeKey ForSector(long sector, string property) => new(MapChangeScope.Sector, sector, null, property);
    public static MapChangeKey ForEntity(string entity, string property) => new(MapChangeScope.Entity, 0, entity, property);
}

public readonly record struct MapChange(MapChangeKey Key, long Value);

/// <summary>One map change of an atomic <see cref="GameSessionOwner.ApplyChanges"/> step: set the key to
/// <see cref="Value"/>, or return it to the loaded baseline when <see cref="Value"/> is null (ARC-WORLD-017).</summary>
public readonly record struct MapChangeEdit(SessionMapId Map, MapChangeKey Key, long? Value);

/// <summary>
/// Applied ambient lighting (FR-LIGHT-012): the resolved scheme id (positive; the map-default sentinel 0 is
/// never applied) and the game hour it was selected for. The host records it after it applies a selection;
/// it is presentation state derived from the clock, map and sector, saved with the time (FR-LIGHT-012) so a
/// load restores the selected scheme of the saved hour together with the clock.
/// </summary>
public readonly record struct SessionLightingState(int SchemeId, int Hour);

/// <summary>Session limits can be lowered, never raised above the policy maxima. ARC-DATA-029: each maximum is sized
/// for a whole playthrough of any map against the census of all 81 installed maps (docs/world/limits-census.md); raising
/// one only admits more and does not change the snapshot format.</summary>
public sealed class GameSessionLimits
{
    /// <summary>The original's map list holds at most 200 maps (checked in the 1.0.7.4 executable); the installed module has 81.</summary>
    public const int PolicyMaxMaps = 200;
    /// <summary>Changes of one map: at least one for every object of the largest map (158,385 on the main world map).</summary>
    public const int PolicyMaxChangesPerMap = 262_144;
    /// <summary>Changes of all maps: at least one for every object of every installed map (276,625).</summary>
    public const int PolicyMaxTotalChanges = 524_288;
    /// <summary>Party records (followers, waiting NPCs and free NPCs with a flag).</summary>
    public const int PolicyMaxCompanions = 64;
    /// <summary>Items the PC took from the world; the same bound as one list of the project's T0 inventory reader.</summary>
    public const int PolicyMaxPcItems = 512;
    /// <summary>NPC life records (ARC-COMBAT-002): critters whose life the session changed; every NPC of every installed
    /// map (3,792) can hold one.</summary>
    public const int PolicyMaxLifeRecords = 8192;
    /// <summary>NPC reaction records (ARC-WORLD-026): NPCs whose reaction memory toward the PC the session changed (as life).</summary>
    public const int PolicyMaxReactionRecords = 8192;
    /// <summary>NPC experience records (ARC-WORLD-030): NPCs whose experience worth or pool the session changed (as life).</summary>
    public const int PolicyMaxExperienceRecords = 8192;
    /// <summary>NPC advancement records (ARC-WORLD-036): followers whose level the session updated.</summary>
    public const int PolicyMaxAdvancementRecords = 256;
    /// <summary>Item durability records (ARC-WORLD-053): items whose condition the session changed plus the items it deleted
    /// (as life: far more than the items a playthrough damages, breaks or repairs).</summary>
    public const int PolicyMaxDurabilityRecords = 8192;
    /// <summary>The hero's found schematics (ARC-TECHNOLOGY-003): the original's list has no limit; the installed data has 62 found
    /// records (57 with a document), so 128 is twice that.</summary>
    public const int PolicyMaxFoundSchematics = 128;
    /// <summary>Made items the session added to NPCs (ARC-WORLD-055), all NPCs together; one NPC's list holds at most
    /// <see cref="SessionMapInventorySeed.MaxListLength"/> items with its seed items. Far more than NPC crafting adds in a playthrough.</summary>
    public const int PolicyMaxNpcItems = 4096;
    /// <summary>Ammunition stacks the session tracks (ARC-WORLD-066): the hero's (at most one a prototype, four installed) and every
    /// other stack whose units it changed; far more than a playthrough's ammunition.</summary>
    public const int PolicyMaxAmmoRecords = 4096;
    /// <summary>NPC danger records (ARC-WORLD-079): NPCs whose danger bits, handles or enemy list the session holds (as life).</summary>
    public const int PolicyMaxDangerRecords = 8192;
    /// <summary>Enemy-list entries of all danger records together (ARC-WORLD-079; the original's lists have no limit): eight for
    /// every danger record, about 4.6 MB of snapshot at the cap.</summary>
    public const int PolicyMaxDangerEnemies = 65_536;

    public GameSessionLimits(int maxMaps = PolicyMaxMaps, int maxChangesPerMap = PolicyMaxChangesPerMap,
        int maxTotalChanges = PolicyMaxTotalChanges, int maxCompanions = PolicyMaxCompanions, int maxPcItems = PolicyMaxPcItems,
        int maxLifeRecords = PolicyMaxLifeRecords, int maxReactionRecords = PolicyMaxReactionRecords,
        int maxExperienceRecords = PolicyMaxExperienceRecords, int maxAdvancementRecords = PolicyMaxAdvancementRecords,
        int maxDurabilityRecords = PolicyMaxDurabilityRecords, int maxFoundSchematics = PolicyMaxFoundSchematics, int maxNpcItems = PolicyMaxNpcItems,
        int maxAmmoRecords = PolicyMaxAmmoRecords, int maxDangerRecords = PolicyMaxDangerRecords, int maxDangerEnemies = PolicyMaxDangerEnemies)
    {
        if (maxMaps is < 1 or > PolicyMaxMaps) throw new ArgumentOutOfRangeException(nameof(maxMaps));
        if (maxChangesPerMap is < 1 or > PolicyMaxChangesPerMap) throw new ArgumentOutOfRangeException(nameof(maxChangesPerMap));
        if (maxTotalChanges is < 1 or > PolicyMaxTotalChanges) throw new ArgumentOutOfRangeException(nameof(maxTotalChanges));
        if (maxCompanions is < 1 or > PolicyMaxCompanions) throw new ArgumentOutOfRangeException(nameof(maxCompanions));
        if (maxPcItems is < 1 or > PolicyMaxPcItems) throw new ArgumentOutOfRangeException(nameof(maxPcItems));
        if (maxLifeRecords is < 1 or > PolicyMaxLifeRecords) throw new ArgumentOutOfRangeException(nameof(maxLifeRecords));
        if (maxReactionRecords is < 1 or > PolicyMaxReactionRecords) throw new ArgumentOutOfRangeException(nameof(maxReactionRecords));
        if (maxExperienceRecords is < 1 or > PolicyMaxExperienceRecords) throw new ArgumentOutOfRangeException(nameof(maxExperienceRecords));
        if (maxAdvancementRecords is < 1 or > PolicyMaxAdvancementRecords) throw new ArgumentOutOfRangeException(nameof(maxAdvancementRecords));
        if (maxDurabilityRecords is < 1 or > PolicyMaxDurabilityRecords) throw new ArgumentOutOfRangeException(nameof(maxDurabilityRecords));
        if (maxFoundSchematics is < 1 or > PolicyMaxFoundSchematics) throw new ArgumentOutOfRangeException(nameof(maxFoundSchematics));
        if (maxNpcItems is < 1 or > PolicyMaxNpcItems) throw new ArgumentOutOfRangeException(nameof(maxNpcItems));
        if (maxAmmoRecords is < 1 or > PolicyMaxAmmoRecords) throw new ArgumentOutOfRangeException(nameof(maxAmmoRecords));
        if (maxDangerRecords is < 1 or > PolicyMaxDangerRecords) throw new ArgumentOutOfRangeException(nameof(maxDangerRecords));
        if (maxDangerEnemies is < 1 or > PolicyMaxDangerEnemies) throw new ArgumentOutOfRangeException(nameof(maxDangerEnemies));
        MaxMaps = maxMaps; MaxChangesPerMap = maxChangesPerMap; MaxTotalChanges = maxTotalChanges; MaxCompanions = maxCompanions;
        MaxPcItems = maxPcItems; MaxLifeRecords = maxLifeRecords;
        MaxReactionRecords = maxReactionRecords; MaxExperienceRecords = maxExperienceRecords;
        MaxAdvancementRecords = maxAdvancementRecords; MaxDurabilityRecords = maxDurabilityRecords; MaxFoundSchematics = maxFoundSchematics;
        MaxNpcItems = maxNpcItems; MaxAmmoRecords = maxAmmoRecords; MaxDangerRecords = maxDangerRecords; MaxDangerEnemies = maxDangerEnemies;
    }

    public int MaxMaps { get; }
    public int MaxChangesPerMap { get; }
    public int MaxTotalChanges { get; }
    public int MaxCompanions { get; }
    public int MaxPcItems { get; }
    public int MaxLifeRecords { get; }
    public int MaxReactionRecords { get; }
    public int MaxExperienceRecords { get; }
    public int MaxAdvancementRecords { get; }
    public int MaxDurabilityRecords { get; }
    public int MaxFoundSchematics { get; }
    public int MaxNpcItems { get; }
    public int MaxAmmoRecords { get; }
    public int MaxDangerRecords { get; }
    public int MaxDangerEnemies { get; }
}

/// <summary>
/// Explicit new-game inputs. The flag descriptor's identity becomes the session's world/module. <paramref name="Hero"/>
/// (ARC-WORLD-015) is the player character the module starts with; a session without one keeps no character and its host
/// supplies PC facts itself. <paramref name="RandomSeed"/> (ARC-WORLD-019) seeds the session's gameplay randomness; the
/// owner never reads a clock or OS entropy, so a host that wants a new sequence per game passes a new seed, and the
/// default 0 gives every such session the same rolls. <paramref name="Rules"/> (ARC-WORLD-023) is the starting rule mode,
/// normally the player's saved setting; an undefined value is <see cref="GameSessionFailure.InvalidDescriptor"/>.
/// <paramref name="Experience"/> (ARC-WORLD-030) is the experience data the host read (level thresholds, quest experience);
/// without it the session awards no experience. <paramref name="Difficulty"/> is the starting game difficulty, the player's
/// setting; an undefined value is <see cref="GameSessionFailure.InvalidDescriptor"/>. <paramref name="AutoLevel"/>
/// (ARC-WORLD-036) is the scheme data the host read; without it no auto-level scheme runs. <paramref name="Reputations"/>
/// (ARC-WORLD-044) is the module's reputation table; without it a held reputation has no effect and grants no faction.
/// </summary>
public sealed record GameSessionDescriptor(ProfileBinding Clock, GlobalFlagOwnerDescriptor? Flags,
    ActorPlacement Actor, GameSessionLimits? Limits = null, SessionHeroDefinition? Hero = null, ulong RandomSeed = 0,
    RuleMode Rules = RuleMode.Steamwoven, SessionExperienceRules? Experience = null, GameDifficulty Difficulty = GameDifficulty.Normal,
    SessionAutoLevelRules? AutoLevel = null, ReputationCatalog? Reputations = null);

/// <summary>
/// Restore expectations, checked before any component is built. <paramref name="FlagEpoch"/> is the
/// new process-local GF epoch; saved epochs are not persisted. <paramref name="Rules"/> (ARC-WORLD-023) is the rule
/// mode to continue under: a snapshot never records one, so any save loads under either mode (D4). <paramref name="Experience"/>
/// and <paramref name="Difficulty"/> (ARC-WORLD-030) are data and a setting, never saved, as in the descriptor; so is
/// <paramref name="AutoLevel"/> (ARC-WORLD-036) and <paramref name="Reputations"/> (ARC-WORLD-044).
/// <paramref name="SoundMapDefault"/> (ARC-WORLD-040) is the loaded map's default music and ambient pair (its map info's
/// <c>SoundScheme</c>, null without one): the original opens the map, which plays it, before it loads the sound state.
/// </summary>
public sealed record GameSessionRestoreOptions(GlobalFlagIdentity ExpectedIdentity,
    ProfileBinding ExpectedClockProfile, OwnerEpoch FlagEpoch, GameSessionLimits? Limits = null,
    RuleMode Rules = RuleMode.Steamwoven, SessionExperienceRules? Experience = null, GameDifficulty Difficulty = GameDifficulty.Normal,
    SessionAutoLevelRules? AutoLevel = null, SessionMapSound? SoundMapDefault = null, ReputationCatalog? Reputations = null);

public enum GameSessionFailure
{
    None, InvalidDescriptor, ClockRejected, FlagsRejected, InvalidActor, InvalidMap, InvalidChangeKey,
    BudgetExceeded, StaleVersion, RevisionExhausted, UnsupportedSchema, WrongWorld, WrongModule,
    IncompatibleClockProfile, InvalidSnapshot, InvalidQuest, InvalidQuestState, InvalidRumor, InvalidReputation, InvalidLighting,
    // Party domain (ARC-WORLD-013).
    InvalidCompanion, InvalidConsentFacts, ConsentRefused, LeaveRefused, NotInParty, NotWaiting,
    // Inventory domain (ARC-WORLD-014).
    InventoryUnavailable, InvalidInventorySeed, InventoryMismatch, InvalidItem, ItemNotAvailable, UnknownHolder,
    HolderNotLootable, HolderScriptUnsupported, ItemScriptUnsupported, ItemTypeUnsupported, WornItemUnsupported,
    SubstituteInventoryUnsupported, InsertRefused,
    // Hero domain (ARC-WORLD-015).
    InvalidHero,
    // Session randomness (ARC-WORLD-019).
    InvalidRoll, RandomExhausted,
    // Life domain (ARC-COMBAT-002).
    InvalidLifeTarget, InvalidLifeFacts, InvalidLifeState, InvalidLifeAmount,
    // Event queue (ARC-WORLD-024).
    EventBudgetExceeded, EventConflict, InvalidEventPass, InvalidEvents,
    // Reaction domain (ARC-WORLD-026).
    InvalidReactionTarget, InvalidReactionFacts, InvalidReactionAmount,
    // Turn-based round (ARC-COMBAT-003).
    CombatUnavailable, InvalidCombatant, InvalidCombatFacts, InvalidCombatCost, InvalidCombatRound,
    // Equipment domain (ARC-WORLD-028).
    EquipmentUnavailable, ItemEffectUnsupported, WearRefused, InvalidEquipment,
    // Durable effects (ARC-WORLD-029).
    EffectsUnavailable, InvalidEffect, EffectMismatch, InnateEffect, EffectCapacityExceeded, UnknownTimedEffect,
    // Experience (ARC-WORLD-030).
    ExperienceUnavailable, InvalidExperience, InvalidExperienceTarget,
    // Advancement (ARC-WORLD-036).
    AdvancementUnavailable, InvalidAdvancement,
    // Combat mode (ARC-COMBAT-004).
    CombatModeInactive, InvalidCombatMode,
    // Hero attack (ARC-COMBAT-005).
    AttackUnavailable, InvalidAttack, AttackUnsupported,
    // Music and ambient (ARC-WORLD-040).
    InvalidSound,
    // Locks of portals and containers (ARC-WORLD-050).
    InvalidOpenable,
    // Skills (ARC-SKILLS-004).
    SkillsUnavailable, InvalidSkill,
    // Character point pool (ARC-WORLD-052).
    HeroEditorUnavailable, HeroEditorClosed, InvalidPointTarget, PointRefused,
    // Item durability (ARC-WORLD-053).
    InvalidDurability,
    // Schematic knowledge (ARC-TECHNOLOGY-003).
    InvalidSchematics,
    // Schematic assembly (ARC-TECHNOLOGY-004).
    AssemblyRefused,
    // Spell book (ARC-MAGIC-004).
    SpellBookUnavailable, InvalidSpellBook, SpellBookRefused,
    // Fate points (ARC-WORLD-054).
    FateUnavailable, InvalidFateKind, FateRefused,
    // World-map areas (ARC-WORLD-056).
    InvalidArea,
    // Heal with a healing item (ARC-SKILLS-005).
    HealUnavailable, InvalidHealingItem,
    // Combat wear (ARC-COMBAT-007).
    InvalidWear,
    // Object damage (ARC-COMBAT-008).
    InvalidObject, ObjectUnavailable,
    // Pick locks (ARC-SKILLS-006).
    PickLockUnavailable, InvalidLockpick,
    // Repair (ARC-SKILLS-007).
    RepairUnavailable,
    // Ammunition (ARC-WORLD-066).
    InvalidAmmo, AmmoShort,
    // Spell runs (ARC-MAGIC-010).
    InvalidSpellRun, SpellRunRefused,
    // Stealth (ARC-SKILLS-008).
    ConcealmentUnavailable, InvalidConcealment,
    // NPC danger state (ARC-WORLD-079).
    InvalidDangerTarget, InvalidDangerFacts,
}

public enum GameSessionDisposition { Changed, NoChange, Rejected }

/// <summary>Rejected creation or restore returns no owner and leaves no component alive.</summary>
public sealed record GameSessionCreationResult(GameSessionOwner? Owner, GameSessionFailure Failure,
    ClockFailure ClockFailure, GlobalFlagReason FlagReason);

public readonly record struct GameSessionMutationResult(GameSessionDisposition Disposition,
    GameSessionFailure Failure, GameSessionVersion Version);

/// <summary>Saved clock state without the process-local owner identity.</summary>
public readonly record struct SessionClockState(ProfileBinding Profile, long GameTimestampMs,
    PauseDisposition Pause, long Revision);

/// <summary>Saved GF state: every admitted flag in ascending order; null is Unknown, never false.</summary>
public sealed class SessionFlagState
{
    internal SessionFlagState(long revision, GlobalFlagEntry[] entries)
    {
        Revision = revision;
        Entries = Array.AsReadOnly(entries);
    }

    public long Revision { get; }
    public IReadOnlyList<GlobalFlagEntry> Entries { get; }
}

/// <summary>Quest states in source order. <see cref="Botched"/> is reported over a kept underlying state.</summary>
public enum SessionQuestState
{
    Unknown = 0, Mentioned = 1, Accepted = 2, Achieved = 3, Completed = 4, OtherCompleted = 5, Botched = 6,
}

/// <summary>
/// The PC's record of one quest: the stored state (never <see cref="SessionQuestState.Botched"/>), the botched
/// mark kept over it, and the session game time of the last change.
/// </summary>
public readonly record struct SessionQuest(int Quest, SessionQuestState State, bool Botched, long TimestampMs)
{
    public SessionQuestState Effective => Botched ? SessionQuestState.Botched : State;
}

/// <summary>A quest's shared state once it left the source default (Accepted): Completed or Botched.</summary>
public readonly record struct SessionGlobalQuest(int Quest, SessionQuestState State);

/// <summary>A rumor the PC knows, with the session game time it was learned.</summary>
public readonly record struct SessionRumor(int Rumor, long TimestampMs);

/// <summary>ARC-WORLD-044: a reputation the PC holds, with the session game time it was first gained.</summary>
public readonly record struct SessionReputation(int Reputation, long TimestampMs);

/// <summary>Number ranges of the story domain: the source indexes quests and rumors from 1000.</summary>
public static class SessionStoryLimits
{
    public const int FirstQuest = 1000;
    public const int LastQuest = 1999;
    public const int FirstRumor = 1000;
    public const int LastRumor = 2999;
    public const int MaxQuests = LastQuest - FirstQuest + 1;
    public const int MaxRumors = LastRumor - FirstRumor + 1;

    public static bool IsQuest(int quest) => quest is >= FirstQuest and <= LastQuest;
    public static bool IsRumor(int rumor) => rumor is >= FirstRumor and <= LastRumor;

    /// <summary>ARC-WORLD-056: world-map areas the PC can know, 1..<see cref="LastArea"/> (project policy; the installed module
    /// has 81, area 0 is the unknown area).</summary>
    public const int LastArea = 4095;
    public static bool IsArea(int area) => area is >= 1 and <= LastArea;

    /// <summary>ARC-WORLD-044: the reputation numbers the PC can hold (the table's range; the original stores any number, A5).</summary>
    public const int FirstReputation = 1000;
    public const int LastReputation = 1999;
    public const int MaxReputations = LastReputation - FirstReputation + 1;

    public static bool IsReputation(int reputation) => reputation is >= FirstReputation and <= LastReputation;
}

/// <summary>Saved story state: PC quests, non-default shared quest states, known rumors and quelled rumors (ARC-UI-008),
/// each ascending, the PC's known world-map areas and last discovered area (ARC-WORLD-056), and the PC's reputations in the
/// order it gained them (ARC-WORLD-044).</summary>
public sealed class SessionStoryState
{
    internal SessionStoryState(SessionQuest[] quests, SessionGlobalQuest[] globalQuests, SessionRumor[] rumors, int[] quelledRumors,
        int[] knownAreas, int lastArea, SessionReputation[] reputations, int storyState = 0)
    {
        StoryState = storyState;
        Quests = Array.AsReadOnly(quests);
        GlobalQuests = Array.AsReadOnly(globalQuests);
        Rumors = Array.AsReadOnly(rumors);
        QuelledRumors = Array.AsReadOnly(quelledRumors);
        KnownAreas = Array.AsReadOnly(knownAreas);
        LastArea = lastArea;
        Reputations = Array.AsReadOnly(reputations);
    }

    /// <summary>ARC-WORLD-056: the world-map areas the PC knows (ledger WT2), ascending.</summary>
    public IReadOnlyList<int> KnownAreas { get; }
    /// <summary>ARC-WORLD-056: the PC's last discovered area, 0 for none (ledger WT2).</summary>
    public int LastArea { get; }

    /// <summary>ARC-WORLD-044: the PC's reputations in acquisition order (the original's list order), each with its time.</summary>
    public IReadOnlyList<SessionReputation> Reputations { get; }

    /// <summary>ARC-SCRIPT-019: the game's story-state number (ledger SS1: one game-wide number, 0 at a new game, only ever
    /// raised); 0..<see cref="int.MaxValue"/>.</summary>
    public int StoryState { get; }

    public IReadOnlyList<SessionQuest> Quests { get; }
    public IReadOnlyList<SessionGlobalQuest> GlobalQuests { get; }
    public IReadOnlyList<SessionRumor> Rumors { get; }
    /// <summary>Rumors proven false: a game-wide set, independent of whether the PC knows the rumor.</summary>
    public IReadOnlyList<int> QuelledRumors { get; }
}

/// <summary>ARC-UI-008: one quest of the PC's logbook: its number, the state the logbook shows (Botched when the botched mark
/// is set, otherwise the stored state; never Unknown) and the game time of its last change.</summary>
public readonly record struct SessionQuestLogEntry(int Quest, SessionQuestState State, long TimestampMs);

/// <summary>ARC-UI-008: one rumor of the PC's logbook: its number, the game time the PC learned it and whether it is quelled
/// (proven false).</summary>
public readonly record struct SessionRumorLogEntry(int Rumor, long TimestampMs, bool Quelled);

/// <summary>Changes of one map in canonical key order; a map without changes is not listed.</summary>
public sealed class SessionMapState
{
    internal SessionMapState(SessionMapId map, MapChange[] changes)
    {
        Map = map;
        Changes = Array.AsReadOnly(changes);
    }

    public SessionMapId Map { get; }
    public IReadOnlyList<MapChange> Changes { get; }
}

/// <summary>
/// Detached, coherent session value at one caller-serialized boundary. Created only by
/// <see cref="GameSessionOwner.Capture"/> or <see cref="GameSessionSnapshotCodec.TryDecode"/>.
/// </summary>
public sealed class GameSessionSnapshot
{
    /// <summary>Schema 3 added <see cref="Story"/> (ARC-SCRIPT-006) and <see cref="Lighting"/> (ARC-WORLD-010); schema 4
    /// added <see cref="Party"/> (ARC-WORLD-013); schema 5 added <see cref="Inventory"/> (ARC-WORLD-014); schema 6 added
    /// <see cref="Hero"/>, the PC's gold and starting-kit items (ARC-WORLD-015); schema 7 added <see cref="Random"/>
    /// (ARC-WORLD-019); schema 8 added <see cref="Life"/> (ARC-COMBAT-002); schema 9 added <see cref="Events"/> (ARC-WORLD-024);
    /// schema 10 added <see cref="Reactions"/> (ARC-WORLD-026), which replaced the viewer's per-NPC <c>reaction.pc</c> map
    /// change; schema 11 added <see cref="Combat"/> (ARC-COMBAT-003); schema 12 added <see cref="Equipment"/> (ARC-WORLD-028);
    /// schema 13 added <see cref="Effects"/> (ARC-WORLD-029); schema 14 added the hero's experience and
    /// <see cref="Experience"/> (ARC-WORLD-030); schema 15 added <see cref="CombatMode"/> (ARC-COMBAT-004); schema 16 binds the clock profile
    /// to the retail executable (<see cref="Arcanum.Simulation.Clock.GameClockOwner.RetailExecutableSha256"/>, ARC-DEV-009); schema 17
    /// adds the hero's blessings and curses to <see cref="Effects"/> (ARC-WORLD-035); schema 18 adds <see cref="Sound"/> (ARC-WORLD-040); schema 19 adds the
    /// quelled rumors to the story (ARC-UI-008); schema 20 adds the hero's advancement fields and <see cref="Advancement"/> (ARC-WORLD-036);
    /// schema 21 adds the hero's innate skill changes (ARC-SKILLS-002); schema 22 adds <see cref="Durability"/> (ARC-WORLD-053);
    /// schema 23 adds <see cref="Schematics"/> (ARC-TECHNOLOGY-003); schema 24 adds the made items to <see cref="Inventory"/>
    /// (ARC-TECHNOLOGY-004); schema 25 adds the mastered college of the hero and of the NPC advancement sheets (ARC-MAGIC-004);
    /// schema 26 adds the hero's fate points and armed fates (ARC-WORLD-054); schema 27 adds each party record's loyalty marks
    /// (ARC-WORLD-045); schema 28 adds the PC's known world-map areas and last discovered area to the story (ARC-WORLD-056).
    /// Schema 29 adds the made items added to NPCs to <see cref="Inventory"/> (ARC-WORLD-055); schema 30 adds the PC's reputations to
    /// the story (ARC-WORLD-044); schema 31 adds the items' remaining uses to <see cref="Durability"/> (ARC-SKILLS-005); schema 32 adds
    /// the hero's items placed on the ground to <see cref="Inventory"/> (ARC-WORLD-059); schema 33 adds the waiting followers' missed
    /// level steps to <see cref="Advancement"/> (ARC-WORLD-047); schema 34 adds the ammunition stacks to <see cref="Inventory"/>
    /// (ARC-WORLD-066); schema 35 adds the hero's and its followers' concealment (<see cref="Concealment"/>, ARC-SKILLS-008); schema 36
    /// adds the hero's background id to <see cref="Hero"/> (ARC-WORLD-069); schema 37 adds the story-state number to the story
    /// (ARC-SCRIPT-019); schema 38 adds the NPCs' danger records (<see cref="Danger"/>, ARC-WORLD-079); schema 39 adds the
    /// SpellFlee danger bit (ARC-WORLD-082). Schemas 1..38 are refused, not migrated.</summary>
    public const int CurrentSchemaVersion = 39;

    internal GameSessionSnapshot(int schemaVersion, GlobalFlagIdentity identity, long revision,
        SessionClockState clock, SessionFlagState flags, ActorPlacement actor, SessionMapState[] maps, SessionStoryState story,
        SessionPartyState party, SessionHeroState? hero, SessionInventoryState inventory, SessionEquipmentState equipment, SessionDurabilityState durability, SessionSchematicState schematics,
        SessionEffectState effects, SessionExperienceState experience, SessionAdvancementState advancement, SessionRandomState random,
        SessionLifeState life, SessionReactionState reactions, SessionCombatRound? combat, SessionCombatModeState combatMode,
        SessionConcealmentState concealment, SessionDangerState danger, SessionEventState events, SessionLightingState? lighting, SessionSoundState sound)
    {
        SchemaVersion = schemaVersion; Identity = identity; Revision = revision;
        Clock = clock; Flags = flags; Actor = actor; Maps = Array.AsReadOnly(maps); Story = story; Party = party;
        Hero = hero; Inventory = inventory; Equipment = equipment; Durability = durability; Schematics = schematics; Effects = effects; Experience = experience; Advancement = advancement; Random = random; Life = life; Reactions = reactions; Combat = combat; CombatMode = combatMode; Concealment = concealment; Danger = danger; Events = events; Lighting = lighting; Sound = sound;
    }

    public int SchemaVersion { get; }
    public GlobalFlagIdentity Identity { get; }
    public long Revision { get; }
    public SessionClockState Clock { get; }
    public SessionFlagState Flags { get; }
    public ActorPlacement Actor { get; }
    public IReadOnlyList<SessionMapState> Maps { get; }
    public SessionStoryState Story { get; }
    public SessionPartyState Party { get; }
    /// <summary>The player character, or null for a session started without one.</summary>
    public SessionHeroState? Hero { get; }
    public SessionInventoryState Inventory { get; }
    /// <summary>The hero's worn items (ARC-WORLD-028).</summary>
    public SessionEquipmentState Equipment { get; }
    /// <summary>The conditions of the items the session damaged, broke or repaired, and the items it deleted (ARC-WORLD-053).</summary>
    public SessionDurabilityState Durability { get; }
    /// <summary>The hero's found schematics (ARC-TECHNOLOGY-003).</summary>
    public SessionSchematicState Schematics { get; }
    /// <summary>The hero's durable effects (ARC-WORLD-029); their timed ENDs are in <see cref="Events"/>.</summary>
    public SessionEffectState Effects { get; }
    /// <summary>The experience worth and pool of the NPCs the session changed (ARC-WORLD-030); the hero's experience is in
    /// <see cref="Hero"/>.</summary>
    public SessionExperienceState Experience { get; }
    /// <summary>The advancement records of the followers the session levelled (ARC-WORLD-036); the hero's are in <see cref="Hero"/>.</summary>
    public SessionAdvancementState Advancement { get; }
    /// <summary>The gameplay generator's seed and position (ARC-WORLD-019).</summary>
    public SessionRandomState Random { get; }
    /// <summary>The hero's and the changed NPCs' life (ARC-COMBAT-002).</summary>
    public SessionLifeState Life { get; }
    /// <summary>The changed NPCs' reaction memory toward the PC (ARC-WORLD-026).</summary>
    public SessionReactionState Reactions { get; }
    /// <summary>The active turn-based round, or null when none is active (ARC-COMBAT-003).</summary>
    public SessionCombatRound? Combat { get; }
    /// <summary>The hero's and the NPCs' combat-mode flags (ARC-COMBAT-004).</summary>
    public SessionCombatModeState CombatMode { get; }
    /// <summary>The hero's and its followers' concealment (ARC-SKILLS-008).</summary>
    public SessionConcealmentState Concealment { get; }
    /// <summary>The NPCs' danger records: bits, focus, fleeing-from, who-hit-me-last and enemy list (ARC-WORLD-079).</summary>
    public SessionDangerState Danger { get; }
    /// <summary>The pending game events (ARC-WORLD-024).</summary>
    public SessionEventState Events { get; }
    /// <summary>Applied ambient lighting, or null when none was recorded (an unlit session).</summary>
    public SessionLightingState? Lighting { get; }
    /// <summary>The music and ambient scheme slots, the transition restore and combat music (ARC-WORLD-040).</summary>
    public SessionSoundState Sound { get; }
}

internal static class SessionValidation
{
    internal const int MaxEntityCharacters = 128;
    internal const int MaxPropertyCharacters = 64;
    internal const int MaxRollLabelCharacters = 64;

    internal static bool Map(SessionMapId map) => map.RuntimeMap >= 1;

    internal static bool Entity(string? entity) => Code(entity, MaxEntityCharacters);

    internal static bool Lighting(SessionLightingState lighting) => lighting.SchemeId >= 1 && lighting.Hour is >= 0 and <= 23;

    internal static bool Actor(ActorPlacement actor) => Map(actor.Map) &&
        actor.Cell.X >= 0 && actor.Cell.Y >= 0 && Enum.IsDefined(actor.Facing);

    internal static bool Key(MapChangeKey key) => key.Scope switch
    {
        MapChangeScope.Sector => key.Sector >= 0 && key.Entity is null && Code(key.Property, MaxPropertyCharacters),
        MapChangeScope.Entity => key.Sector == 0 && Code(key.Entity, MaxEntityCharacters) && Code(key.Property, MaxPropertyCharacters),
        _ => false,
    };

    // ASCII letters, digits and . _ : - only: no case folding, trimming or aliases.
    internal static bool Code(string? value, int max)
    {
        if (value is null || value.Length < 1 || value.Length > max) return false;
        foreach (var c in value)
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-'))
                return false;
        return true;
    }
}

/// <summary>Canonical order: scope, sector, entity (ordinal), property (ordinal).</summary>
internal sealed class MapChangeKeyComparer : IComparer<MapChangeKey>
{
    internal static readonly MapChangeKeyComparer Instance = new();

    public int Compare(MapChangeKey x, MapChangeKey y)
    {
        var order = x.Scope.CompareTo(y.Scope);
        if (order == 0) order = x.Sector.CompareTo(y.Sector);
        if (order == 0) order = string.CompareOrdinal(x.Entity, y.Entity);
        if (order == 0) order = string.CompareOrdinal(x.Property, y.Property);
        return order;
    }
}
