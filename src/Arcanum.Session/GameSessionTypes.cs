using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>Immutable process-local reference identity, distinct for each created or restored owner.</summary>
public sealed class GameSessionOwnerIdentity
{
    internal GameSessionOwnerIdentity() { }
}

/// <summary>Freshness guard for actor and map mutations. Clock and flags keep their own tokens.</summary>
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

    public GameSessionLimits(int maxMaps = PolicyMaxMaps, int maxChangesPerMap = PolicyMaxChangesPerMap,
        int maxTotalChanges = PolicyMaxTotalChanges)
    {
        if (maxMaps is < 1 or > PolicyMaxMaps) throw new ArgumentOutOfRangeException(nameof(maxMaps));
        if (maxChangesPerMap is < 1 or > PolicyMaxChangesPerMap) throw new ArgumentOutOfRangeException(nameof(maxChangesPerMap));
        if (maxTotalChanges is < 1 or > PolicyMaxTotalChanges) throw new ArgumentOutOfRangeException(nameof(maxTotalChanges));
        MaxMaps = maxMaps; MaxChangesPerMap = maxChangesPerMap; MaxTotalChanges = maxTotalChanges;
    }

    public int MaxMaps { get; }
    public int MaxChangesPerMap { get; }
    public int MaxTotalChanges { get; }
}

/// <summary>Explicit new-game inputs. The flag descriptor's identity becomes the session's world/module.</summary>
public sealed record GameSessionDescriptor(ProfileBinding Clock, GlobalFlagOwnerDescriptor? Flags,
    ActorPlacement Actor, GameSessionLimits? Limits = null);

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
    IncompatibleClockProfile, InvalidSnapshot, InvalidLighting,
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
    /// <summary>Schema 2 adds <see cref="Lighting"/> (ARC-WORLD-010). Schema 1 is not migrated: no saved
    /// snapshots existed before it.</summary>
    public const int CurrentSchemaVersion = 2;

    internal GameSessionSnapshot(int schemaVersion, GlobalFlagIdentity identity, long revision,
        SessionClockState clock, SessionFlagState flags, ActorPlacement actor, SessionMapState[] maps,
        SessionLightingState? lighting)
    {
        SchemaVersion = schemaVersion; Identity = identity; Revision = revision;
        Clock = clock; Flags = flags; Actor = actor; Maps = Array.AsReadOnly(maps); Lighting = lighting;
    }

    public int SchemaVersion { get; }
    public GlobalFlagIdentity Identity { get; }
    public long Revision { get; }
    public SessionClockState Clock { get; }
    public SessionFlagState Flags { get; }
    public ActorPlacement Actor { get; }
    public IReadOnlyList<SessionMapState> Maps { get; }
    /// <summary>Applied ambient lighting, or null when none was recorded (an unlit session).</summary>
    public SessionLightingState? Lighting { get; }
}

internal static class SessionValidation
{
    internal const int MaxEntityCharacters = 128;
    internal const int MaxPropertyCharacters = 64;

    internal static bool Map(SessionMapId map) => map.RuntimeMap >= 1;

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
