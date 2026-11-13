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

/// <summary>
/// ARC-UI-1542 (schema 98): the lit state of the top strip's buttons that the original keeps in the Intgame part of its save (0x54A1A0, ledger GB13):
/// the five button art words and the map mode. The host records it after it changes a light and reads it back on a load; the session only keeps it.
/// </summary>
/// <param name="LitMask">Bit n set when the light slot n holds its lit art (0 Character, 1 Logbook, 2 town map, 3 world map, 4 Inventory; 0..31).</param>
/// <param name="MapMode">The Map button's face: 2 the town map's, 3 the world map's (the slot numbers of the two faces).</param>
public readonly record struct SessionBarLightsState(int LitMask, int MapMode);

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
    /// <summary>Gold piles the session tracks (ARC-WORLD-071): every NPC pile whose coins it changed or that it created.</summary>
    public const int PolicyMaxGoldRecords = 4096;
    /// <summary>NPC danger records (ARC-WORLD-079): NPCs whose danger bits, handles or enemy list the session holds (as life).</summary>
    public const int PolicyMaxDangerRecords = 8192;
    /// <summary>Enemy-list entries of all danger records together (ARC-WORLD-079; the original's lists have no limit): eight for
    /// every danger record, about 4.6 MB of snapshot at the cap.</summary>
    public const int PolicyMaxDangerEnemies = 65_536;
    /// <summary>Items scripts created on the ground that lie there still (ARC-WORLD-081): under Steamwoven at most
    /// <c>CreatedItemRules.Limit</c> lie around one object; under Original the plant generator adds without limit, and past this bound
    /// the session creates no more (the viewer reports it).</summary>
    public const int PolicyMaxCreatedItems = 8192;

    public GameSessionLimits(int maxMaps = PolicyMaxMaps, int maxChangesPerMap = PolicyMaxChangesPerMap,
        int maxTotalChanges = PolicyMaxTotalChanges, int maxCompanions = PolicyMaxCompanions, int maxPcItems = PolicyMaxPcItems,
        int maxLifeRecords = PolicyMaxLifeRecords, int maxReactionRecords = PolicyMaxReactionRecords,
        int maxExperienceRecords = PolicyMaxExperienceRecords, int maxAdvancementRecords = PolicyMaxAdvancementRecords,
        int maxDurabilityRecords = PolicyMaxDurabilityRecords, int maxFoundSchematics = PolicyMaxFoundSchematics, int maxNpcItems = PolicyMaxNpcItems,
        int maxAmmoRecords = PolicyMaxAmmoRecords, int maxGoldRecords = PolicyMaxGoldRecords, int maxDangerRecords = PolicyMaxDangerRecords,
        int maxDangerEnemies = PolicyMaxDangerEnemies, int maxCreatedItems = PolicyMaxCreatedItems)
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
        if (maxGoldRecords is < 1 or > PolicyMaxGoldRecords) throw new ArgumentOutOfRangeException(nameof(maxGoldRecords));
        if (maxDangerRecords is < 1 or > PolicyMaxDangerRecords) throw new ArgumentOutOfRangeException(nameof(maxDangerRecords));
        if (maxDangerEnemies is < 1 or > PolicyMaxDangerEnemies) throw new ArgumentOutOfRangeException(nameof(maxDangerEnemies));
        if (maxCreatedItems is < 1 or > PolicyMaxCreatedItems) throw new ArgumentOutOfRangeException(nameof(maxCreatedItems));
        MaxMaps = maxMaps; MaxChangesPerMap = maxChangesPerMap; MaxTotalChanges = maxTotalChanges; MaxCompanions = maxCompanions;
        MaxPcItems = maxPcItems; MaxLifeRecords = maxLifeRecords;
        MaxReactionRecords = maxReactionRecords; MaxExperienceRecords = maxExperienceRecords;
        MaxAdvancementRecords = maxAdvancementRecords; MaxDurabilityRecords = maxDurabilityRecords; MaxFoundSchematics = maxFoundSchematics;
        MaxNpcItems = maxNpcItems; MaxAmmoRecords = maxAmmoRecords; MaxGoldRecords = maxGoldRecords; MaxDangerRecords = maxDangerRecords;
        MaxDangerEnemies = maxDangerEnemies; MaxCreatedItems = maxCreatedItems;
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
    public int MaxGoldRecords { get; }
    public int MaxDangerRecords { get; }
    public int MaxDangerEnemies { get; }
    /// <summary>ARC-WORLD-081: the created items on the ground (<see cref="PolicyMaxCreatedItems"/>).</summary>
    public int MaxCreatedItems { get; }
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
/// <paramref name="LoadRandom"/> (ARC-DEV-077, GI6) is the generator the host is running, usually the current session's
/// <see cref="GameSessionOwner.QueryRandom"/>: under the Original setting (<see cref="LifecycleRuleProfile.LoadKeepsRunningRandom"/>)
/// the restored session goes on with it instead of the saved seed and position, as the original keeps its process generator
/// across a load; null there starts the default seed 0 at draw 0, as a new session without a seed. Steamwoven ignores it.
/// A negative position is <see cref="GameSessionFailure.InvalidDescriptor"/> in both modes.
/// </summary>
public sealed record GameSessionRestoreOptions(GlobalFlagIdentity ExpectedIdentity,
    ProfileBinding ExpectedClockProfile, OwnerEpoch FlagEpoch, GameSessionLimits? Limits = null,
    RuleMode Rules = RuleMode.Steamwoven, SessionExperienceRules? Experience = null, GameDifficulty Difficulty = GameDifficulty.Normal,
    SessionAutoLevelRules? AutoLevel = null, SessionMapSound? SoundMapDefault = null, ReputationCatalog? Reputations = null,
    SessionRandomState? LoadRandom = null);

public enum GameSessionFailure
{
    None, InvalidDescriptor, ClockRejected, FlagsRejected, InvalidActor, InvalidMap, InvalidChangeKey,
    BudgetExceeded, StaleVersion, RevisionExhausted, UnsupportedSchema, WrongWorld, WrongModule,
    IncompatibleClockProfile, InvalidSnapshot, InvalidQuest, InvalidQuestState, InvalidRumor, InvalidReputation, InvalidLighting, InvalidBarLights,
    // Party domain (ARC-WORLD-013).
    InvalidCompanion, InvalidConsentFacts, ConsentRefused, LeaveRefused, NotInParty, NotWaiting,
    // Inventory domain (ARC-WORLD-014).
    InventoryUnavailable, InvalidInventorySeed, InventoryMismatch, InvalidItem, ItemNotAvailable, UnknownHolder,
    HolderNotLootable, HolderScriptUnsupported, ItemScriptUnsupported, ItemTypeUnsupported, WornItemUnsupported,
    SubstituteInventoryUnsupported, InsertRefused,
    // ARC-SCRIPT-1279: a script moves an item between two holders neither of which is the hero.
    HolderPairUnsupported,
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
    // NPC gold and transfers (ARC-WORLD-071).
    GoldShort, GoldUnavailable,
    // Boomerangs (ARC-COMBAT-019): no attack starts while the hero's boomerang is away (0x434B00).
    WeaponInFlight,
    // Hero inventory grid (ARC-UI-019): the target cells hold another item or leave the grid; a saved grid that disagrees with the lists.
    GridCellsTaken, InvalidGrid,
    // Hotkey bar (ARC-UI-022): a slot, entry or saved bar that does not fit; no hero to own a bar.
    InvalidHotkey, HotkeysUnavailable,
    // Hero drop (ARC-UI-025): the item's no-drop flag (item flag 0x20, the remove check 0x466DA0); the hero's tile outside the drop's bounds.
    ItemNotDroppable, DropTileUnfit,
    // Hero background (ARC-WORLD-097): a choice outside the stored range or whose effect is not the one given.
    InvalidBackground,
    // Hero player name (ARC-UI-033): longer than the stored bound or holding a NUL byte.
    InvalidPlayerName,
    // Trap state (ARC-WORLD-076): an object id, carrier or use script out of range.
    InvalidTrap,
    // Pick pocket (ARC-SKILLS-010): no living hero to steal; a victim's facts outside their bounds, or a victim that is not a living NPC;
    // in a turn-based round, not the hero's turn or too few action points.
    PickPocketUnavailable, InvalidPickPocketVictim, PickPocketOutOfTurn,
    // Script stores (ARC-DEV-098): an index outside the store's bounds; a PC flag or variable of a session without a hero (the accessors act only on a PC, SA14).
    InvalidScriptIndex, PcScriptsUnavailable,
    // Sector cache (ARC-DEV-104): a touch with a negative cell, or more distinct sectors than the cache has slots.
    InvalidSectors,
    // Change of place (ARC-DEV-113): a host step stopped the change, or its destination on another map could not be readied.
    ChangeOfPlaceStopped,
    // Town maps (ARC-DEV-205): a town-map number outside 1..SessionStoryLimits.LastTownMap.
    InvalidTownMap,
    // Hero keys (ARC-UI-081): a key number below 0, a key list already at SessionStoryLimits.MaxKeys, or a session without a hero (no PC, no key ring).
    InvalidKey, KeyRingFull, KeysUnavailable,
    // Kill and injury log (ARC-UI-082): a session without a hero has no log; an injury kind outside 0..3, a negative number, or a list at SessionStoryLimits.MaxInjuries.
    KillLogUnavailable, InvalidKillFacts, InjuryLogFull,
    // World and continent map notes (ARC-UI-110): a view outside 1..SessionStoryLimits.LastMapNoteView, or a list outside the limits or not canonical.
    InvalidMapNotes,
    // Created scenery and eye-candy objects (ARC-DEV-347): a kind, prototype, map, cell or offset outside its bounds, or a kind-prefixed entity that is no created object's.
    InvalidCreatedObject,
    // Disarm traps (ARC-SKILLS-1156): in a turn-based round, not the hero's turn or too few action points for the press.
    DisarmOutOfTurn,
}

public enum GameSessionDisposition { Changed, NoChange, Rejected }

/// <summary>Rejected creation or restore returns no owner and leaves no component alive.</summary>
public sealed record GameSessionCreationResult(GameSessionOwner? Owner, GameSessionFailure Failure,
    ClockFailure ClockFailure, GlobalFlagReason FlagReason);

public readonly record struct GameSessionMutationResult(GameSessionDisposition Disposition,
    GameSessionFailure Failure, GameSessionVersion Version);

/// <summary>Saved clock state without the process-local owner identity. Schema 70 (MIG12 S3, ARC-DEV-122) adds the real and animation
/// stamps of the session's three clocks (TL34: the original's save stores all three): real from 1 ms, animation from 0; the world
/// milliseconds and the frame admission reference are run-local and not saved.</summary>
public readonly record struct SessionClockState(ProfileBinding Profile, long GameTimestampMs,
    PauseDisposition Pause, long Revision, long RealMs, long AnimationMs);

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

    /// <summary>ARC-DEV-205: the town-map numbers a reveal can name, 1..<see cref="LastTownMap"/> (project policy, as for areas: the original
    /// names a town map by its line of <c>rules\townmap.mes</c>, 0 is no town map).</summary>
    public const int LastTownMap = 4095;
    public static bool IsTownMap(int townMap) => townMap is >= 1 and <= LastTownMap;

    /// <summary>ARC-UI-081: the key numbers the PC's key ring can hold, 0..<see cref="int.MaxValue"/> (a key number is the message of
    /// <c>mes\gamekey.mes</c> that names it; the original stores any number), and how many keys the session keeps (project policy: the original's
    /// ring is a growing array, its logbook buffer holds 3000).</summary>
    public const int MaxKeys = 3000;
    public static bool IsKey(int key) => key >= 0;

    /// <summary>ARC-UI-082: how many injury records the session keeps (project policy: the original's list grows with no cap, its logbook buffers
    /// hold 2991 after the nine fixed lines) and the injury kinds: 0 blinded, 1 crippled arm, 2 crippled legs, 3 scar.</summary>
    public const int MaxInjuries = 2000, InjuryKinds = 4;

    /// <summary>ARC-WORLD-122 (schema 75): the cells of a town map's reveal bitmap, 0..<see cref="LastTownMapCell"/> (the original's bit of one tile, index = row ×
    /// columns + column, WM78; the largest shipped map has 140 × 140 = 19 600, the reader of the info file refuses more than 65 536), and how many town maps may
    /// hold walked cells (the shipped list has 144).</summary>
    public const int LastTownMapCell = 65535, MaxTownMapsWithCells = 1024;
    public static bool IsTownMapCell(int cell) => cell is >= 0 and <= LastTownMapCell;

    /// <summary>ARC-UI-104 (schema 81): the notes of a town map (WM91, WM127, WM128): at most 199 (0x564160 refuses the 200th), ids from 200, a text buffer of 250 bytes
    /// (249 characters), the pixel in the town's picture (the info file refuses a picture over 131 072 pixels) and the marker type 0..7 (WM45).</summary>
    public const int MaxTownNotes = 199, FirstTownNoteId = 200, MaxTownNoteText = 249, MaxTownNotePixel = 1 << 17, LastTownNoteType = 7, MaxTownNoteFlags = 0xFFFF;
    public static bool IsTownNoteText(string text) => text.Length <= MaxTownNoteText && text.All(c => !char.IsControl(c) && !char.IsSurrogate(c));

    /// <summary>ARC-UI-110 (schema 82): the views of the world map that keep notes, 1 the map view and 2 the continent view; a note's pixel is one of the world-map picture.</summary>
    public const int MapNoteView = 1, ContinentNoteView = 2, LastMapNoteView = 2, MaxMapNotePixel = 4096;
    public static bool IsMapNoteView(int view) => view is >= MapNoteView and <= LastMapNoteView;
}

/// <summary>ARC-UI-104 (ledger WM91): one note of a town map, as the original's record keeps it (the label rectangle is always zero and is not kept).</summary>
public sealed record SessionTownNote(int Id, int Flags, int PixelX, int PixelY, int Type, string Text);

/// <summary>ARC-UI-104 (schema 81): the notes of one town map (the original keeps them in <c>Save\Current\&lt;town&gt;.tmn</c>, WM91): the id the next note takes and
/// the notes in the order they were added, ids ascending and below <see cref="NextId"/>. At least one note.</summary>
public sealed class SessionTownNotes
{
    public SessionTownNotes(int townMap, int nextId, IEnumerable<SessionTownNote> notes)
    {
        TownMap = townMap; NextId = nextId; Notes = Array.AsReadOnly(notes.ToArray());
    }
    public int TownMap { get; }
    public int NextId { get; }
    public IReadOnlyList<SessionTownNote> Notes { get; }

    /// <summary>Whether the notes are within the policy limits and canonical: a town-map number, 1..199 notes, ids from 200 ascending below the next id, pixels,
    /// flags and type in range, and a text that fits the buffer.</summary>
    public bool IsValid()
    {
        if (!SessionStoryLimits.IsTownMap(TownMap) || Notes.Count is 0 or > SessionStoryLimits.MaxTownNotes || NextId <= SessionStoryLimits.FirstTownNoteId) return false;
        var previous = SessionStoryLimits.FirstTownNoteId - 1;
        foreach (var note in Notes)
        {
            if (note.Id <= previous || note.Id >= NextId || note.Flags is < 0 or > SessionStoryLimits.MaxTownNoteFlags
                || note.PixelX is < 0 or > SessionStoryLimits.MaxTownNotePixel || note.PixelY is < 0 or > SessionStoryLimits.MaxTownNotePixel
                || note.Type is < 0 or > SessionStoryLimits.LastTownNoteType || note.Text is null || !SessionStoryLimits.IsTownNoteText(note.Text)) return false;
            previous = note.Id;
        }
        return true;
    }
}

/// <summary>
/// ARC-UI-110 (schema 82; Steamwoven rules, no original counterpart): the notes of one view of the world map, kept the way the town maps keep theirs. <see cref="View"/> is
/// <see cref="SessionStoryLimits.MapNoteView"/> (the map view) or <see cref="SessionStoryLimits.ContinentNoteView"/> (the continent view); a note sits at a pixel of the
/// world-map picture (0..<see cref="SessionStoryLimits.MaxMapNotePixel"/>, the picture is at most 4096 wide) and follows the rules of a town note (ids from 200
/// ascending below the next id, at most 199, text of at most 249 characters without control characters or surrogates, type 0..7, flags 0..65535). At least one note.
/// </summary>
public sealed class SessionMapNotes
{
    public SessionMapNotes(int view, int nextId, IEnumerable<SessionTownNote> notes)
    {
        View = view; NextId = nextId; Notes = Array.AsReadOnly(notes.ToArray());
    }
    public int View { get; }
    public int NextId { get; }
    public IReadOnlyList<SessionTownNote> Notes { get; }

    /// <summary>Whether the notes are within the policy limits and canonical.</summary>
    public bool IsValid()
    {
        if (!SessionStoryLimits.IsMapNoteView(View) || Notes.Count is 0 or > SessionStoryLimits.MaxTownNotes || NextId <= SessionStoryLimits.FirstTownNoteId) return false;
        var previous = SessionStoryLimits.FirstTownNoteId - 1;
        foreach (var note in Notes)
        {
            if (note.Id <= previous || note.Id >= NextId || note.Flags is < 0 or > SessionStoryLimits.MaxTownNoteFlags
                || note.PixelX is < 0 or > SessionStoryLimits.MaxMapNotePixel || note.PixelY is < 0 or > SessionStoryLimits.MaxMapNotePixel
                || note.Type is < 0 or > SessionStoryLimits.LastTownNoteType || note.Text is null || !SessionStoryLimits.IsTownNoteText(note.Text)) return false;
            previous = note.Id;
        }
        return true;
    }
}

/// <summary>ARC-WORLD-122: the revealed cells of one town map: bit <c>cell % 8</c> of byte <c>cell / 8</c> (the original's bitmap), no trailing zero byte,
/// at least one bit set.</summary>
public sealed class SessionTownMapCells
{
    internal SessionTownMapCells(int townMap, byte[] bits) { TownMap = townMap; Bits = Array.AsReadOnly(bits); }
    public int TownMap { get; }
    public IReadOnlyList<byte> Bits { get; }
    /// <summary>Whether the cell's bit is set.</summary>
    public bool Has(int cell) => cell >= 0 && cell / 8 < Bits.Count && (Bits[cell / 8] & (1 << (cell % 8))) != 0;
}

/// <summary>ARC-UI-082: one slot of the PC's kill log, a description number (0 = none yet) and the value that earned it.</summary>
public readonly record struct SessionKillRecord(int NameId, int Value);

/// <summary>
/// ARC-UI-082 (ledger LB21, snapshot schema 78): the PC's kill statistics as the original keeps them in the PC's logbook field 0x10C: the number
/// of kills and, of the creatures it killed, the highest and the lowest level, the most positive and the most negative alignment and the highest
/// positive and most negative magick/tech aptitude, each as the description number of the victim and the value; plus the injuries it suffered,
/// each the description number of the one who caused it and a kind (0 blinded, 1 crippled arm, 2 crippled legs, 3 scar), oldest first.
/// </summary>
public sealed record SessionKillLog(int Total, SessionKillRecord HighestLevel, SessionKillRecord LowestLevel, SessionKillRecord MostPositiveAlignment,
    SessionKillRecord MostNegativeAlignment, SessionKillRecord HighestAptitude, SessionKillRecord LowestAptitude, IReadOnlyList<SessionInjury> Injuries)
{
    public static SessionKillLog Empty { get; } = new(0, default, default, default, default, default, default, []);

    /// <summary>Whether nothing was ever recorded (a new game's log).</summary>
    public bool IsEmpty => Total == 0 && Injuries.Count == 0 && HighestLevel == default && LowestLevel == default && MostPositiveAlignment == default
        && MostNegativeAlignment == default && HighestAptitude == default && LowestAptitude == default;
}

/// <summary>ARC-UI-082: one injury of the PC: who caused it (a description number) and the kind (0..3).</summary>
public readonly record struct SessionInjury(int NameId, int Kind);

/// <summary>Saved story state: PC quests, non-default shared quest states, known rumors and quelled rumors (ARC-UI-008),
/// each ascending, the PC's known world-map areas and last discovered area (ARC-WORLD-056), the PC's reputations in the
/// order it gained them (ARC-WORLD-044), the town maps revealed whole (ARC-DEV-205, schema 71), ascending, and the cells of town maps the PC revealed by
/// walking (ARC-WORLD-122, schema 75).</summary>
public sealed class SessionStoryState
{
    internal SessionStoryState(SessionQuest[] quests, SessionGlobalQuest[] globalQuests, SessionRumor[] rumors, int[] quelledRumors,
        int[] knownAreas, int lastArea, SessionReputation[] reputations, int storyState = 0, int[]? revealedTownMaps = null,
        SessionTownMapCells[]? townMapCells = null, int[]? heroKeys = null, SessionKillLog? killLog = null, SessionTownNotes[]? townNotes = null,
        SessionMapNotes[]? mapNotes = null)
    {
        MapNotes = Array.AsReadOnly(mapNotes ?? []);
        TownNotes = Array.AsReadOnly(townNotes ?? []);
        KillLog = killLog ?? SessionKillLog.Empty;
        HeroKeys = Array.AsReadOnly(heroKeys ?? []);
        TownMapCells = Array.AsReadOnly(townMapCells ?? []);
        StoryState = storyState;
        RevealedTownMaps = Array.AsReadOnly(revealedTownMaps ?? []);
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

    /// <summary>ARC-DEV-205: the town maps whose every cell is revealed (ledger UF1–UF3: unfog town map, 113), ascending, each in
    /// 1..<see cref="SessionStoryLimits.LastTownMap"/>; the cells the PC revealed by walking are <see cref="TownMapCells"/> (ARC-WORLD-122).</summary>
    public IReadOnlyList<int> RevealedTownMaps { get; }

    /// <summary>ARC-WORLD-122 (ledger WM78, PS25): the cells of each town map the PC revealed by walking, ascending by town map, each with at least one
    /// cell (the original keeps a bit per cell in <c>Save\Current\&lt;town&gt;.tmf</c>).</summary>
    public IReadOnlyList<SessionTownMapCells> TownMapCells { get; }

    /// <summary>ARC-UI-104 (schema 81): the notes of each town map that has any, ascending by town map (ledger WM91).</summary>
    public IReadOnlyList<SessionTownNotes> TownNotes { get; }

    /// <summary>ARC-UI-110 (schema 82): the notes of the world map's views that have any, ascending by view (Steamwoven rules).</summary>
    public IReadOnlyList<SessionMapNotes> MapNotes { get; }

    /// <summary>ARC-UI-081 (schema 77): the key numbers of the PC's key ring in the order they were added (the original's ring is an item of the PC's
    /// inventory holding a list of key numbers, ledger LB20; the session keeps the list, not the ring item), each 0..<see cref="int.MaxValue"/>, at
    /// most <see cref="SessionStoryLimits.MaxKeys"/>; a number may repeat.</summary>
    public IReadOnlyList<int> HeroKeys { get; }

    /// <summary>ARC-UI-082 (schema 78): the PC's kill statistics and injuries (ledger LB21); empty at a new game.</summary>
    public SessionKillLog KillLog { get; }

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
    /// (ARC-SCRIPT-019); schema 38 adds the NPCs' danger records (<see cref="Danger"/>, ARC-WORLD-079); schema 39 adds the items the
    /// hero gave NPCs and the tracked gold piles to <see cref="Inventory"/> (ARC-WORLD-071); schema 40 adds the SpellFlee danger bit
    /// (ARC-WORLD-082); schema 41 adds the made and placed items' complexity to <see cref="Inventory"/> (ARC-WORLD-083); schema 42 adds
    /// the CheckWeapon danger bit and the AI ticks to <see cref="Events"/> (ARC-WORLD-084); schema 43 adds
    /// the kept item script slots to <see cref="Inventory"/> and a worn item's item event points to <see cref="Equipment"/>
    /// (ARC-SCRIPT-042); schema 44 adds the weight rows to <see cref="Inventory"/> (ARC-WORLD-085); schema 45 adds the NoFlee danger
    /// bit (ARC-WORLD-086); schema 46 adds the junk piles to <see cref="Inventory"/> (ARC-WORLD-063); schema 47 adds the
    /// LookForAmmo, LookForWeapon and LookForArmor danger bits (ARC-WORLD-088); schema 48 adds the items scripts created on the ground to
    /// the placed items of <see cref="Inventory"/> (source <c>created</c>, ARC-WORLD-081); schema 49 adds the items' spent spell charges to
    /// <see cref="Durability"/> and a worn item's item spells mark to <see cref="Equipment"/> (ARC-MAGIC-022); schema 50 adds the identified items and the
    /// containers' added lists to <see cref="Inventory"/> (ARC-WORLD-090); schema 51 adds the highest discarded made number to
    /// <see cref="Inventory"/> (ARC-WORLD-094); schema 52 adds the hero's inventory grid to <see cref="Inventory"/> (ARC-UI-019); schema 53
    /// adds the hotkey bar (<see cref="Hotkeys"/>, ARC-UI-022); schema 54 adds the hero's background description number to <see cref="Hero"/>
    /// (ARC-WORLD-097); schema 55 adds the generated-once containers to <see cref="Inventory"/> (ARC-WORLD-100); schema 56 adds the hero's
    /// player name and portrait to <see cref="Hero"/> (ARC-UI-033); schema 57 adds the worn items of the NPCs the session dressed to
    /// <see cref="Inventory"/> (ARC-WORLD-103); schema 58 adds the pools of blood the session left to <see cref="Inventory"/>
    /// (ARC-RENDER-041); schema 59 adds the session's subturn starts to a saved <see cref="Combat"/> round (GR19, ARC-DEV-078); schema 60 adds the junk and pool
    /// number high-water marks to <see cref="Inventory"/> (MIG6, ARC-DEV-084); schema 61 moves the junk piles, the pools of blood and the two marks out of
    /// <see cref="Inventory"/> into the section <see cref="Objects"/> (MIG6d-3, ARC-DEV-091); schema 62 moves the entity changes of mobile map-data objects out of
    /// <see cref="Maps"/> into <see cref="SessionObjectRecords.State"/> (MIG7a, ARC-DEV-092); schema 63 moves the entity changes of static map-data objects
    /// (<c>sector:S:I</c> on the map that holds them) there too (MIG7b, ARC-DEV-093); schema 64 adds the object field <c>npc.pos</c>, the place a walking
    /// map NPC was left on (MIG7c, ARC-DEV-094); schema 65 moves the NPCs' links (combat focus, fleeing from, who hit the holder last, the enemy
    /// list) out of the danger records into <see cref="SessionObjectRecords.Links"/>, each with the map it was written on (MIG7d, ARC-DEV-095); schema 66
    /// moves the script attachments (<c>dialog.*</c>, <c>use.*</c>, <c>scr&lt;point&gt;.*</c> of mobile and static map-data objects) out of
    /// <see cref="Maps"/> into <see cref="SessionObjectRecords.State"/> (MIG8, ARC-DEV-096); schema 67 adds the <see cref="Scripts"/> section, the global variables and the PC's flags
    /// and variables (MIG8, GR29, ARC-DEV-098); schema 68 adds the <see cref="Residency"/> section, the open map's sector eviction history (MIG9 S6, ARC-DEV-106); schema 69 adds the object field <c>npc.teleport</c>, an NPC's teleport map (MIG11 S5, ARC-DEV-116); schema 70 adds the real and
    /// animation stamps to the clock section (MIG12 S3, ARC-DEV-122); schema 71 adds the town maps revealed whole to the story section
    /// (<see cref="SessionStoryState.RevealedTownMaps"/>, ARC-DEV-205); schema 72 adds the <see cref="Goals"/> section, the goal runs of the active map (MIG14 S2, ARC-DEV-208). schema 73 adds the <see cref="Spells"/> section, the settled spell runs with the maintain bar (MIG14 S4, ARC-DEV-210); schema 75 adds the cells of town maps revealed by walking to the story section (<see cref="SessionStoryState.TownMapCells"/>, ARC-WORLD-122); schema 74 adds the <see cref="Newspaper"/> section, the newspaper issue queue, and the object field <c>npc.origin</c>, an NPC's origin (ARC-DEV-230); schema 76 lists the hero's gold pile in the inventory grid (<c>gold:pc</c>, ARC-UI-090); schema 77 adds the PC's key numbers to the story section (<see cref="SessionStoryState.HeroKeys"/>, ARC-UI-081); schema 78 adds the PC's kill statistics and injuries (<see cref="SessionStoryState.KillLog"/>, ARC-UI-082); schema 79 moves the PC's flag and variable arrays from the <see cref="Scripts"/> section onto the hero's record (<c>hero.scripts</c>, ARC-DEV-266); schema 80 adds the <see cref="Flights"/> section, the projectiles in flight with the shots they carry (ARC-DEV-288); schema 81 adds the notes of the town maps to the story section (<see cref="SessionStoryState.TownNotes"/>, ARC-UI-104); schema 82 adds the notes of the world map's views to the story section (<see cref="SessionStoryState.MapNotes"/>, ARC-UI-110); schema 83 adds the flags the spell components
    /// wrote on critters to the spells section (<see cref="SessionSpellState.ObjectFlags"/>, ARC-DEV-302); schema 84 adds the spell missiles in flight to the <see cref="Flights"/> section and saves the NPCs'
    /// casting goals with the spell runs they name (ARC-DEV-289); schema 85 adds the created scenery and eye-candy
    /// objects and their number counter to the <see cref="Objects"/> section (<see cref="SessionObjectRecords.Created"/>, ARC-DEV-347).
    /// Schema 86 adds the object field <c>obj.teleported</c>, the mark a change of place to another map leaves on a carried object (ARC-DEV-351).
    /// Schema 87 adds the effect lists of NPCs to the effects section (<see cref="SessionEffectState.Critters"/>, ARC-DEV-348).
    /// Schema 88 adds the object-flags change (<c>obj_f_flags</c>) to each critter's written flags in the spells section (<see cref="SessionObjectFlags.ObjectSet"/>, ARC-DEV-358).
    /// Schema 89 adds the monster bodies of the Trait component (Polymorph, the body spells) to the spells section (<see cref="SessionSpellState.TraitArt"/>, ARC-MAGIC-040).
    /// Schema 90 adds the second critter flags and the blit flags change to each critter's written flags (<see cref="SessionObjectFlags.Critter2Set"/>, <see cref="SessionObjectFlags.BlitSet"/>, ARC-MAGIC-047).
    /// Schema 91 adds the <see cref="Generators"/> section: the monster generators' counter table, the creatures they made so far and the slot mark of each critter they made (ARC-WORLD-1157).
    /// Schema 92 adds each spell run's object list to the spells section (<see cref="SpellRun.ObjectList"/>, the original's run list +0x130, ARC-MAGIC-1234).
    /// Schema 93 adds the NPC flags change to each critter's written flags (<see cref="SessionObjectFlags.NpcSet"/>, ARC-MAGIC-1142).
    /// Schema 94 adds the item fields scripts wrote (written start, inventory display bit) to the inventory section (<see cref="SessionInventoryState.ItemFields"/>, ARC-SCRIPT-1279).
    /// Schema 95 adds the arranged slots of seed holders (a container's or an NPC's grid laid out at a loot, steal or See Contents open) to the inventory section (<see cref="SessionInventoryState.HolderSlots"/>, ARC-RES-1441).
    /// Schema 96 adds the wait flag to each party record (<see cref="SessionCompanion.WaitFlag"/>, the WAIT bit of an NPC with no leader or one that joined with it, ARC-SCRIPT-1136).
    /// Schema 97 adds the encounters section: the use counts of the world-map random encounter rows (<see cref="SessionEncounterState"/>, ARC-WORLD-1159).
    /// Schema 98 adds the lit state of the top strip's buttons and the Map button's face (<see cref="BarLights"/>, ARC-UI-1542).
    /// Schema 99 adds the hero's stored resistance array and stored age (<see cref="SessionHeroState.Resistances"/>, <see cref="SessionHeroState.Age"/>, ARC-MAGIC-1658).
    /// Schema 100 adds the native traps' shots in flight to the flights section (<see cref="SessionFlightState.TrapShots"/>, ARC-TECHNOLOGY-1635).
    /// Schemas 1..55 are refused, not migrated; schemas 56 to 99 are upgraded on decode (ARC-UI-037).</summary>
    public const int CurrentSchemaVersion = 101;

    internal GameSessionSnapshot(int schemaVersion, GlobalFlagIdentity identity, long revision,
        SessionClockState clock, SessionFlagState flags, ActorPlacement actor, SessionMapState[] maps, SessionStoryState story,
        SessionPartyState party, SessionHeroState? hero, SessionInventoryState inventory, SessionEquipmentState equipment, SessionDurabilityState durability, SessionSchematicState schematics,
        SessionEffectState effects, SessionExperienceState experience, SessionAdvancementState advancement, SessionRandomState random,
        SessionLifeState life, SessionReactionState reactions, SessionCombatRound? combat, SessionCombatModeState combatMode,
        SessionConcealmentState concealment, SessionDangerState danger, SessionEventState events, SessionLightingState? lighting, SessionSoundState sound,
        SessionHotkeyState? hotkeys = null, SessionObjectRecords? objects = null, SessionScriptState? scripts = null, SessionResidencyState? residency = null, SessionGoalState? goals = null, SessionSpellState? spells = null, SessionNewspaperState? newspaper = null, SessionFlightState? flights = null, SessionGeneratorState? generators = null, SessionEncounterState? encounters = null, SessionBarLightsState? barLights = null)
    {
        SchemaVersion = schemaVersion; Identity = identity; Revision = revision;
        Clock = clock; Flags = flags; Actor = actor; Maps = Array.AsReadOnly(maps); Story = story; Party = party;
        Hero = hero; Inventory = inventory; Equipment = equipment; Durability = durability; Schematics = schematics; Effects = effects; Experience = experience; Advancement = advancement; Random = random; Life = life; Reactions = reactions; Combat = combat; CombatMode = combatMode; Concealment = concealment; Danger = danger; Events = events; Lighting = lighting; Sound = sound;
        Hotkeys = hotkeys ?? SessionHotkeyState.Empty;
        Objects = objects ?? SessionObjectRecords.Empty;
        Scripts = scripts ?? SessionScriptState.Empty;
        Residency = residency ?? SessionResidencyState.Empty;
        Goals = goals ?? SessionGoalState.Empty;
        Spells = spells ?? SessionSpellState.Empty;
        Newspaper = newspaper ?? SessionNewspaperState.NewGame;
        Flights = flights ?? SessionFlightState.Empty;
        Generators = generators ?? SessionGeneratorState.Empty;
        Encounters = encounters ?? SessionEncounterState.Empty;
        BarLights = barLights;
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
    /// <summary>The junk piles and pools of blood the object directory records, with the high-water marks of their numbers (ARC-DEV-091, schema 61).</summary>
    public SessionObjectRecords Objects { get; }
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
    /// <summary>The hero's hotkey bar: ten entries and two recent actions (ARC-UI-022).</summary>
    public SessionHotkeyState Hotkeys { get; }
    /// <summary>The global variables and the PC's flags and variables (ARC-DEV-098, schema 67); the global flags are <see cref="Flags"/>, the story number is in <see cref="Story"/>.</summary>
    public SessionScriptState Scripts { get; }
    /// <summary>The open map's sector eviction history, with a record for every sector the cache held at the save (ARC-DEV-106, schema 68).</summary>
    public SessionResidencyState Residency { get; }
    /// <summary>The goal runs of the active map (ARC-DEV-208, schema 72): the next run id and the saved runs with their goal stacks.</summary>
    public SessionGoalState Goals { get; }
    /// <summary>The settled spell runs, the maintain bar and the last charged cost (ARC-DEV-210, schema 73).</summary>
    public SessionSpellState Spells { get; }
    /// <summary>The newspaper issue queue (ARC-DEV-230, schema 74).</summary>
    public SessionNewspaperState Newspaper { get; }
    /// <summary>The projectiles in flight on the active map with the shots they carry (ARC-DEV-288, schema 80).</summary>
    public SessionFlightState Flights { get; }
    /// <summary>The monster generators' counter table, the creatures they made and the slot mark of each critter they made (ARC-WORLD-1157, schema 91).</summary>
    public SessionGeneratorState Generators { get; }

    /// <summary>The use counts of the world-map random encounter rows that fired (ARC-WORLD-1159, schema 97).</summary>
    public SessionEncounterState Encounters { get; }
    /// <summary>The lit state of the top strip's buttons, or null when none was recorded (a session whose host never lit a button, or a save of schema 97 or older: all idle, ARC-UI-1542).</summary>
    public SessionBarLightsState? BarLights { get; }
}

internal static class SessionValidation
{
    internal const int MaxEntityCharacters = 128;
    internal const int MaxPropertyCharacters = 64;
    internal const int MaxRollLabelCharacters = 64;

    internal static bool Map(SessionMapId map) => map.RuntimeMap >= 1;

    internal static bool Entity(string? entity) => Code(entity, MaxEntityCharacters);

    internal static bool Lighting(SessionLightingState lighting) => lighting.SchemeId >= 1 && lighting.Hour is >= 0 and <= 23;

    internal static bool BarLights(SessionBarLightsState lights) => lights.LitMask is >= 0 and <= 31 && lights.MapMode is 2 or 3;

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
