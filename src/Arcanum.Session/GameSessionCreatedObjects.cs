using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>ARC-DEV-347 (E1-a): what a created object is. <see cref="Scenery"/> is a scenery object made from a prototype of object type 3 (a wall of fire's piece, a
/// flash's marker): it is drawn and picked as scenery. <see cref="EyeCandy"/> is an object made only to be seen (an effect's art): it is drawn and never picked. The exact kind of
/// each spell's creation is the research slice E1-r's (ledger rows), so the frame is generic by prototype.</summary>
public enum SessionCreatedKind { Scenery, EyeCandy }

/// <summary>
/// ARC-DEV-347 (E1-a, TM4): a scenery or eye-candy object the session created from a prototype on a map, a created object of the directory
/// (<c>scenery:n</c> or <c>eyecandy:n</c>; origins <see cref="SessionObjectOrigin.Scenery"/> and <see cref="SessionObjectOrigin.EyeCandy"/>): its prototype number (the host
/// reads the art and flags from the game's data by it), its map, its cell and pixel offsets, and the game time it was created. It is map-local state: it belongs to <see cref="Map"/>
/// and lies in the sector of its cell, so the open map lists it among its residents and the sector's fold reaches it; the other maps' objects stay in the save until their map opens
/// (nothing here reads the game's data). Saved (schema 85) and never given its number again.
/// </summary>
public sealed record SessionCreatedObject(string Entity, SessionCreatedKind Kind, int Prototype, SessionMapId Map, CellCoord Cell, int OffsetX, int OffsetY, long CreatedMs)
{
    /// <summary>The created objects the session holds at once (project bound; the original has none but the object table's).</summary>
    public const int MaxObjects = 4096;
    /// <summary>The highest prototype number a created object may name (a prototype file's number has six digits, the data reader's own bound).</summary>
    public const int MaxPrototype = 999_999;
    /// <summary>The largest pixel offset in either direction (a cell is under 100 pixels wide: a larger offset is another cell).</summary>
    public const int MaxOffset = 256;
    /// <summary>The entity prefix of a created scenery object.</summary>
    public const string SceneryPrefix = "scenery:";
    /// <summary>The entity prefix of a created eye-candy object.</summary>
    public const string EyeCandyPrefix = "eyecandy:";

    /// <summary>The entity prefix of a kind.</summary>
    public static string PrefixOf(SessionCreatedKind kind) => kind == SessionCreatedKind.Scenery ? SceneryPrefix : EyeCandyPrefix;

    /// <summary>The object's number in its entity, or null when the entity is not a created scenery or eye-candy object's.</summary>
    public static int? Number(string? entity) => Number(entity, SceneryPrefix) ?? Number(entity, EyeCandyPrefix);

    /// <summary>The number of a <paramref name="prefix"/> entity, or null.</summary>
    public static int? Number(string? entity, string prefix) =>
        entity is not null && entity.StartsWith(prefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;

    /// <summary>The kind an entity names (null when it is no created object's).</summary>
    public static SessionCreatedKind? KindOf(string? entity) =>
        Number(entity, SceneryPrefix) is not null ? SessionCreatedKind.Scenery : Number(entity, EyeCandyPrefix) is not null ? SessionCreatedKind.EyeCandy : null;
}

/// <summary>The outcome of <see cref="GameSessionOwner.CreateObject"/> and <see cref="GameSessionOwner.DestroyCreatedObject"/>: the object created or destroyed (Changed), or none
/// (Rejected, <see cref="Failure"/> says why; NoChange for a destroy of an object the session does not hold).</summary>
public sealed record SessionCreatedObjectResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCreatedObject? Object, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-347 (E1-a): why <see cref="CreateObject"/> would refuse a creation before any effect (an invalid kind, prototype, map, cell or offset, or the bound of
    /// <see cref="SessionCreatedObject.MaxObjects"/>, or the numbers running out), or null when it would not. The host checks a run's creations with it before it applies anything.
    /// </summary>
    public GameSessionFailure? QueryCreateObjectRefusal(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX = 0, int offsetY = 0)
    {
        ThrowIfDisposed();
        return CreateObjectRefusal(kind, prototype, map, cell, offsetX, offsetY);
    }

    private GameSessionFailure? CreateObjectRefusal(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX, int offsetY)
    {
        if (!Enum.IsDefined(kind) || prototype is < 1 or > SessionCreatedObject.MaxPrototype || !SessionValidation.Map(map) || !DropCellFits(cell) ||
            Math.Abs((long)offsetX) > SessionCreatedObject.MaxOffset || Math.Abs((long)offsetY) > SessionCreatedObject.MaxOffset)
            return GameSessionFailure.InvalidCreatedObject;
        if (_inventory.Objects.Created.Count >= SessionCreatedObject.MaxObjects || NextObjectEntity(CreatedOrigin(kind)) is null) return GameSessionFailure.BudgetExceeded;
        return null;
    }

    private static SessionObjectOrigin CreatedOrigin(SessionCreatedKind kind) => kind == SessionCreatedKind.Scenery ? SessionObjectOrigin.Scenery : SessionObjectOrigin.EyeCandy;

    /// <summary>
    /// ARC-DEV-347 (E1-a, TM4, GR4): creates a scenery or eye-candy object by <paramref name="prototype"/> on <paramref name="map"/> at <paramref name="cell"/> with the pixel offsets from
    /// the tile's centre, and returns it (the one create path of these kinds, <see cref="SessionObjectOrigin.Scenery"/> and <see cref="SessionObjectOrigin.EyeCandy"/>; the id is the
    /// next number of the shared counter, never given again). The map need not be the open one: the object is map-local and waits for its map. One revision. The session reads no game
    /// data, so the prototype is the host's to have checked (the host reads its art, type and flags when it draws it). Refused before any effect: the version, an invalid input,
    /// <see cref="SessionCreatedObject.MaxObjects"/>, the numbers running out, the revision bound.
    /// </summary>
    public SessionCreatedObjectResult CreateObject(GameSessionVersion expected, SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX = 0, int offsetY = 0)
    {
        ThrowIfDisposed();
        SessionCreatedObjectResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));
        if (CreateObjectRefusal(kind, prototype, map, cell, offsetX, offsetY) is { } refused) return Reject(refused);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var made = CreateCreatedObject(kind, prototype, map, cell, offsetX, offsetY, _clock.QueryClockSnapshot().GameTimestampMs);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, made, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-DEV-347 (E1-a, TM4): destroys a created scenery or eye-candy object (the one destroy path, <see cref="DestroyObject"/>; the number stays used). NoChange for an entity the
    /// session holds no such object under; Rejected for a stale version. One revision. The run that created it (a spell's END, a dispel) calls this.
    /// </summary>
    public SessionCreatedObjectResult DestroyCreatedObject(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        SessionCreatedObjectResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));
        if (!SessionValidation.Entity(entity) || SessionCreatedObject.KindOf(entity) is null) return Reject(GameSessionFailure.InvalidCreatedObject);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_inventory.Objects.CreatedObject(entity) is not { } held) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, new(_identity, _revision));
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        DestroyObject(entity);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, held, new(_identity, _revision));
    }

    /// <summary>ARC-DEV-347: the created scenery and eye-candy objects lying on <paramref name="map"/> now, in the order they were created.</summary>
    public IReadOnlyList<SessionCreatedObject> QueryCreatedObjects(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_inventory.Objects.Created.Where(made => made.Map == map).ToArray());
    }

    /// <summary>ARC-DEV-347: a created object of the session by its entity, or null.</summary>
    public SessionCreatedObject? QueryCreatedObject(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? _inventory.Objects.CreatedObject(entity) : null;
    }

    /// <summary>
    /// ARC-DEV-347 (TM4): the one creation of a scenery or eye-candy object: it takes the next number of the shared counter (the high-water mark moves to it, so the number is never
    /// given again) and is recorded. The caller made the checks (<see cref="CreateObjectRefusal"/>) and keeps the revision.
    /// </summary>
    internal SessionCreatedObject CreateCreatedObject(SessionCreatedKind kind, int prototype, SessionMapId map, CellCoord cell, int offsetX, int offsetY, long createdMs)
    {
        var number = AllocateObjectNumber(CreatedOrigin(kind));
        var made = new SessionCreatedObject(SessionCreatedObject.PrefixOf(kind) + number.ToString(System.Globalization.CultureInfo.InvariantCulture), kind, prototype, map, cell, offsetX, offsetY, createdMs);
        _inventory.Objects.AddCreated(made);
        return made;
    }

    // The saved created objects within the bounds, each a scenery or eye-candy entity once, by the prefix of its kind, of a prototype in range, on a valid map and cell, created not after the saved clock.
    private static GameSessionFailure ImportCreatedObjects(SessionObjectRecords saved, long clockMs, InventoryDomain inventory)
    {
        if (saved.Created.Count > SessionCreatedObject.MaxObjects || saved.LastCreatedNumber < 0) return GameSessionFailure.BudgetExceeded;
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var made in saved.Created)
        {
            if (made is null || !Enum.IsDefined(made.Kind) || SessionCreatedObject.KindOf(made.Entity) != made.Kind || !entities.Add(made.Entity) ||
                made.Prototype is < 1 or > SessionCreatedObject.MaxPrototype || !SessionValidation.Map(made.Map) || !DropCellFits(made.Cell) ||
                Math.Abs((long)made.OffsetX) > SessionCreatedObject.MaxOffset || Math.Abs((long)made.OffsetY) > SessionCreatedObject.MaxOffset ||
                made.CreatedMs < 0 || made.CreatedMs > clockMs)
                return GameSessionFailure.InvalidSnapshot;
            inventory.Objects.AddCreated(made);
        }
        inventory.Objects.LastCreatedNumber = Math.Max(saved.LastCreatedNumber, saved.Created.Select(made => SessionCreatedObject.Number(made.Entity) ?? 0).DefaultIfEmpty(0).Max());
        return GameSessionFailure.None;
    }
}
