using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;

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

/// <summary>Session limits can be lowered, never raised above the policy maxima.</summary>
public sealed class GameSessionLimits
{
    public const int PolicyMaxMaps = 64;
    public const int PolicyMaxChangesPerMap = 4096;
    public const int PolicyMaxTotalChanges = 16384;
    /// <summary>Party records (followers, waiting NPCs and free NPCs with a flag).</summary>
    public const int PolicyMaxCompanions = 64;
    /// <summary>Items the PC took from the world; the same bound as one list of the project's T0 inventory reader.</summary>
    public const int PolicyMaxPcItems = 512;

    public GameSessionLimits(int maxMaps = PolicyMaxMaps, int maxChangesPerMap = PolicyMaxChangesPerMap,
        int maxTotalChanges = PolicyMaxTotalChanges, int maxCompanions = PolicyMaxCompanions, int maxPcItems = PolicyMaxPcItems)
    {
        if (maxMaps is < 1 or > PolicyMaxMaps) throw new ArgumentOutOfRangeException(nameof(maxMaps));
        if (maxChangesPerMap is < 1 or > PolicyMaxChangesPerMap) throw new ArgumentOutOfRangeException(nameof(maxChangesPerMap));
        if (maxTotalChanges is < 1 or > PolicyMaxTotalChanges) throw new ArgumentOutOfRangeException(nameof(maxTotalChanges));
        if (maxCompanions is < 1 or > PolicyMaxCompanions) throw new ArgumentOutOfRangeException(nameof(maxCompanions));
        if (maxPcItems is < 1 or > PolicyMaxPcItems) throw new ArgumentOutOfRangeException(nameof(maxPcItems));
        MaxMaps = maxMaps; MaxChangesPerMap = maxChangesPerMap; MaxTotalChanges = maxTotalChanges; MaxCompanions = maxCompanions;
        MaxPcItems = maxPcItems;
    }

    public int MaxMaps { get; }
    public int MaxChangesPerMap { get; }
    public int MaxTotalChanges { get; }
    public int MaxCompanions { get; }
    public int MaxPcItems { get; }
}

/// <summary>
/// Explicit new-game inputs. The flag descriptor's identity becomes the session's world/module. <paramref name="Hero"/>
/// (ARC-WORLD-015) is the player character the module starts with; a session without one keeps no character and its host
/// supplies PC facts itself.
/// </summary>
public sealed record GameSessionDescriptor(ProfileBinding Clock, GlobalFlagOwnerDescriptor? Flags,
    ActorPlacement Actor, GameSessionLimits? Limits = null, SessionHeroDefinition? Hero = null);

/// <summary>
/// Restore expectations, checked before any component is built. <paramref name="FlagEpoch"/> is the
/// new process-local GF epoch; saved epochs are not persisted.
/// </summary>
public sealed record GameSessionRestoreOptions(GlobalFlagIdentity ExpectedIdentity,
    ProfileBinding ExpectedClockProfile, OwnerEpoch FlagEpoch, GameSessionLimits? Limits = null);

public enum GameSessionFailure
{
    None, InvalidDescriptor, ClockRejected, FlagsRejected, InvalidActor, InvalidMap, InvalidChangeKey,
    BudgetExceeded, StaleVersion, RevisionExhausted, UnsupportedSchema, WrongWorld, WrongModule,
    IncompatibleClockProfile, InvalidSnapshot, InvalidQuest, InvalidQuestState, InvalidRumor, InvalidLighting,
    // Party domain (ARC-WORLD-013).
    InvalidCompanion, InvalidConsentFacts, ConsentRefused, LeaveRefused, NotInParty, NotWaiting,
    // Inventory domain (ARC-WORLD-014).
    InventoryUnavailable, InvalidInventorySeed, InventoryMismatch, InvalidItem, ItemNotAvailable, UnknownHolder,
    HolderNotLootable, HolderScriptUnsupported, ItemScriptUnsupported, ItemTypeUnsupported, WornItemUnsupported,
    SubstituteInventoryUnsupported, InsertRefused,
    // Hero domain (ARC-WORLD-015).
    InvalidHero,
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
}

/// <summary>Saved story state: PC quests, non-default shared quest states and known rumors, each ascending.</summary>
public sealed class SessionStoryState
{
    internal SessionStoryState(SessionQuest[] quests, SessionGlobalQuest[] globalQuests, SessionRumor[] rumors)
    {
        Quests = Array.AsReadOnly(quests);
        GlobalQuests = Array.AsReadOnly(globalQuests);
        Rumors = Array.AsReadOnly(rumors);
    }

    public IReadOnlyList<SessionQuest> Quests { get; }
    public IReadOnlyList<SessionGlobalQuest> GlobalQuests { get; }
    public IReadOnlyList<SessionRumor> Rumors { get; }
}

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
    /// added <see cref="Party"/> (ARC-WORLD-013); schema 5 added <see cref="Inventory"/> (ARC-WORLD-014); schema 6 adds
    /// <see cref="Hero"/>, the PC's gold and starting-kit items (ARC-WORLD-015). Schemas 1..5 are refused, not migrated.</summary>
    public const int CurrentSchemaVersion = 6;

    internal GameSessionSnapshot(int schemaVersion, GlobalFlagIdentity identity, long revision,
        SessionClockState clock, SessionFlagState flags, ActorPlacement actor, SessionMapState[] maps, SessionStoryState story,
        SessionPartyState party, SessionHeroState? hero, SessionInventoryState inventory, SessionLightingState? lighting)
    {
        SchemaVersion = schemaVersion; Identity = identity; Revision = revision;
        Clock = clock; Flags = flags; Actor = actor; Maps = Array.AsReadOnly(maps); Story = story; Party = party;
        Hero = hero; Inventory = inventory; Lighting = lighting;
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
    /// <summary>Applied ambient lighting, or null when none was recorded (an unlit session).</summary>
    public SessionLightingState? Lighting { get; }
}

internal static class SessionValidation
{
    internal const int MaxEntityCharacters = 128;
    internal const int MaxPropertyCharacters = 64;

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
